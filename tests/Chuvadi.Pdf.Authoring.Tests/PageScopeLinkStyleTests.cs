// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// 3.25 PageBuilder additions for Layout: clipping and opacity scopes, links
// over a rectangle (URI and in-document), dashed line styles, image crops,
// and the soft mask of a PNG with alpha.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Images;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;
using static Chuvadi.Pdf.Authoring.Tests.AuthoringTestKit;

namespace Chuvadi.Pdf.Authoring.Tests;

public sealed class PageScopeLinkStyleTests
{
    private static readonly Color Red = Color.FromHex("#FF0000");
    private static readonly Color Blue = Color.FromHex("#0000FF");
    private static readonly double[] Dash3On2Off = { 3, 2 };

    // ── Clipping ─────────────────────────────────────────────────────────

    [Fact]
    public void PushClip_CutsDrawingToTheRectangle_UntilPopClip()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        PageBuilder page = builder.AddPage(PageSize.A4);
        page.PushClip(100, 100, 50, 50);
        page.DrawRectangle(0, 0, 300, 300, fill: Red);
        page.PopClip();
        page.DrawRectangle(0, 200, 20, 20, fill: Blue);

        byte[] pdf = builder.ToByteArray();
        PixelBuffer px = Raster(pdf);

        // Content-stream lines end in the platform newline (CRLF on Windows).
        Encoding.Latin1.GetString(pdf).Replace("\r\n", "\n", StringComparison.Ordinal).Should().Contain("re\nW n");
        Rgb(px, 120, 120).Should().Be(((byte)255, (byte)0, (byte)0));
        IsWhite(px, 50, 50).Should().BeTrue("outside the clip");
        IsWhite(px, 160, 120).Should().BeTrue("outside the clip");
        Rgb(px, 10, 210).Should().Be(((byte)0, (byte)0, (byte)255), "after PopClip nothing is clipped");
    }

    [Fact]
    public void PushClip_Path_ClipsToTheOutline()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        PageBuilder page = builder.AddPage(PageSize.A4);
        Graphics.Path triangle = new Graphics.Path();
        triangle.MoveTo(new PointF(100, 100));
        triangle.LineTo(new PointF(200, 100));
        triangle.LineTo(new PointF(100, 200));
        triangle.ClosePath();
        page.PushClip(triangle);
        page.DrawRectangle(0, 0, 300, 300, fill: Red);
        page.PopClip();

        PixelBuffer px = Raster(builder.ToByteArray());

        Rgb(px, 120, 120).Should().Be(((byte)255, (byte)0, (byte)0), "inside the triangle");
        IsWhite(px, 180, 180).Should().BeTrue("beyond the hypotenuse");
    }

    [Fact]
    public void Scopes_MustBePoppedInOrder()
    {
        PageBuilder page = PdfDocumentBuilder.Create().AddPage(PageSize.A4);

        Action popNothing = () => page.PopClip();
        popNothing.Should().Throw<InvalidOperationException>();

        page.PushClip(0, 0, 10, 10).PushOpacity(0.5);
        Action wrongKind = () => page.PopClip();
        wrongKind.Should().Throw<InvalidOperationException>().WithMessage("*opacity*");

        page.PopOpacity().PopClip();
        Action popAgain = () => page.PopOpacity();
        popAgain.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void OpenScopes_AreClosedAtSave_BeforeTheHeader()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .SetHeader((p, n, total) => p.DrawRectangle(0, 0, 20, 20, fill: Red));
        PageBuilder page = builder.AddPage(PageSize.A4);
        page.PushClip(300, 300, 10, 10).PushOpacity(0.2);

        byte[] pdf = builder.ToByteArray();
        PixelBuffer px = Raster(pdf);

        Rgb(px, 10, 10).Should().Be(((byte)255, (byte)0, (byte)0), "the header is neither clipped nor translucent");
        string[] lines = Encoding.Latin1.GetString(pdf).Split('\n');
        lines.Count(l => l == "q").Should().Be(lines.Count(l => l == "Q"), "every q has its Q");
    }

    // ── Opacity ──────────────────────────────────────────────────────────

    [Fact]
    public void PushOpacity_MakesDrawingTranslucent_AndNestedScopesMultiply()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        PageBuilder page = builder.AddPage(PageSize.A4);
        page.PushOpacity(0.5);
        page.DrawRectangle(0, 0, 50, 50, fill: Red);
        page.PushOpacity(0.5, 1.0);
        page.DrawRectangle(100, 0, 50, 50, fill: Red);
        page.PopOpacity().PopOpacity();

        byte[] pdf = builder.ToByteArray();
        PixelBuffer px = Raster(pdf);

        string body = Encoding.Latin1.GetString(pdf);
        body.Should().Contain("/ca 0.5").And.Contain("/ca 0.25").And.Contain("/CA 0.5");
        Rgb(px, 25, 25).G.Should().BeInRange(120, 135, "half red over white");
        Rgb(px, 125, 25).G.Should().BeInRange(185, 197, "a quarter red over white");
    }

    [Fact]
    public void ImageOpacity_MultipliesWithTheScope()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        PageBuilder page = builder.AddPage(PageSize.A4);
        page.PushOpacity(0.5);
        page.DrawImage(Png(Solid(4, 4, 255, 0, 0, 255), alpha: false), 0, 0, 40, 40, 0.5);
        page.PopOpacity();

        Encoding.Latin1.GetString(builder.ToByteArray()).Should().Contain("/ca 0.25");
    }

    [Fact]
    public void PushOpacity_OutOfRange_Throws()
    {
        PageBuilder page = PdfDocumentBuilder.Create().AddPage(PageSize.A4);

        Action act = () => page.PushOpacity(1.5);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ── Links ────────────────────────────────────────────────────────────

    [Fact]
    public void AddLink_Uri_AddsABorderlessLinkAndDrawsNothing()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        builder.AddPage(PageSize.A4).AddLink(100, 100, 80, 20, new Uri("https://example.com/a?b=1"));

        byte[] pdf = builder.ToByteArray();
        using PdfDocument doc = Open(pdf);
        PdfDictionary annot = Annotations(doc, 0).Single();
        PdfDictionary action = annot.GetAs<PdfDictionary>(PdfName.Intern("A"))!;

        annot.GetName(PdfName.Intern("Subtype"))!.Value.Should().Be("Link");
        action.GetName(PdfName.Intern("S"))!.Value.Should().Be("URI");
        action.GetAs<PdfString>(PdfName.Intern("URI"))!.ToTextString().Should().Be("https://example.com/a?b=1");
        PdfArray rect = annot.GetAs<PdfArray>(PdfName.Intern("Rect"))!;
        Numbers(rect).Should().Equal(100, 842 - 120, 180, 842 - 100);
        ExtractedText(pdf).Should().BeEmpty();
    }

    [Fact]
    public void AddLink_ToAnotherPage_IsAGoToWithTheTopOfTheTarget()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        builder.AddPage(PageSize.A4).AddLink(50, 50, 100, 20, 1, 72);
        builder.AddPage(PageSize.Letter);

        using PdfDocument doc = Open(builder.ToByteArray());
        PdfDictionary action = Annotations(doc, 0).Single().GetAs<PdfDictionary>(PdfName.Intern("A"))!;
        PdfArray dest = action.GetAs<PdfArray>(PdfName.Intern("D"))!;

        action.GetName(PdfName.Intern("S"))!.Value.Should().Be("GoTo");
        PdfDictionary target = doc.Objects.ResolveAs<PdfDictionary>(dest[0])!;
        target.GetAs<PdfArray>(PdfName.Intern("MediaBox"))!.Should().HaveCount(4);
        Numbers(target.GetAs<PdfArray>(PdfName.Intern("MediaBox"))!)[3].Should().Be(792, "it is the Letter page");
        ((PdfName)dest[1]).Value.Should().Be("XYZ");
        Numbers(dest)[3].Should().Be(792 - 72, "72 pt from the top of the target page");
    }

    [Fact]
    public void AddLink_ToAMissingPage_FailsAtSave()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        builder.AddPage(PageSize.A4).AddLink(0, 0, 10, 10, 3, 0);

        Action save = () => builder.ToByteArray();
        Action negative = () => builder.AddPage(PageSize.A4).AddLink(0, 0, 10, 10, -1, 0);

        save.Should().Throw<InvalidOperationException>();
        negative.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ── Line styles ──────────────────────────────────────────────────────

    [Fact]
    public void LineStyle_WritesDashCapAndJoin_AndDashesTheLine()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        PageBuilder page = builder.AddPage(PageSize.A4);
        LineStyle dashed = new LineStyle { Width = 4, DashPattern = new double[] { 10, 10 }, Cap = LineCap.Butt, Join = LineJoin.Round };
        page.DrawLine(0, 100, 200, 100, Colors.Black, dashed);
        page.DrawRectangle(20, 200, 100, 50, null, Colors.Black, new LineStyle { DashPattern = Dash3On2Off, DashPhase = 1, Cap = LineCap.Round });
        Graphics.Path path = new Graphics.Path();
        path.MoveTo(new PointF(0, 300));
        path.LineTo(new PointF(100, 300));
        page.DrawPath(path, null, Colors.Black, new LineStyle { MiterLimit = 4 });

        byte[] pdf = builder.ToByteArray();
        string body = Encoding.Latin1.GetString(pdf);
        PixelBuffer px = Raster(pdf);

        body.Should().Contain("[10 10] 0 d").And.Contain("1 j").And.Contain("[3 2] 1 d").And.Contain("1 J").And.Contain("4 M");
        IsDark(px, 5, 100).Should().BeTrue("the first dash");
        IsWhite(px, 15, 100).Should().BeTrue("the first gap");
        IsDark(px, 25, 100).Should().BeTrue("the second dash");
    }

    [Fact]
    public void LineStyle_Invalid_Throws()
    {
        PageBuilder page = PdfDocumentBuilder.Create().AddPage(PageSize.A4);

        Action allZero = () => page.DrawLine(0, 0, 1, 1, Colors.Black, new LineStyle { DashPattern = new double[] { 0, 0 } });
        Action negative = () => page.DrawLine(0, 0, 1, 1, Colors.Black, new LineStyle { Width = -1 });

        allZero.Should().Throw<ArgumentException>();
        negative.Should().Throw<ArgumentException>();
    }

    // ── Images ───────────────────────────────────────────────────────────

    [Fact]
    public void DrawImage_Crop_ShowsOnlyThePart()
    {
        // 4 x 2 image: left half red, right half blue. Cropping to the right
        // half fills the destination with blue.
        ImageFrame frame = ImageFrame.Create(4, 2, ImageColorFormat.Rgb24);
        for (int y = 0; y < 2; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                frame.Pixels.SetPixelBgra(x, y, (byte)(x < 2 ? 0 : 255), 0, (byte)(x < 2 ? 255 : 0), 255);
            }
        }

        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        PageBuilder page = builder.AddPage(PageSize.A4);
        page.DrawImage(Png(frame, alpha: false), 100, 100, 80, 80, new ImageCrop(2, 0, 2, 2));
        page.DrawImage(frame, 300, 100, 80, 80, new ImageCrop(0, 0, 2, 2));

        PixelBuffer px = Raster(builder.ToByteArray());

        Rgb(px, 110, 140).Should().Be(((byte)0, (byte)0, (byte)255), "the crop's left edge is the image's blue half");
        Rgb(px, 170, 140).Should().Be(((byte)0, (byte)0, (byte)255));
        IsWhite(px, 90, 140).Should().BeTrue("nothing is drawn outside the destination");
        IsWhite(px, 190, 140).Should().BeTrue("nothing is drawn outside the destination");
        Rgb(px, 370, 140).Should().Be(((byte)255, (byte)0, (byte)0), "the frame overload crops to the red half");
    }

    [Fact]
    public void DrawImage_EmptyCrop_Throws()
    {
        PageBuilder page = PdfDocumentBuilder.Create().AddPage(PageSize.A4);

        Action act = () => page.DrawImage(Png(Solid(2, 2, 0, 0, 0, 255), alpha: false), 0, 0, 10, 10, new ImageCrop(0, 0, 0, 2));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void DrawImage_RgbaPng_IsTransparentWhereAlphaIsZero()
    {
        // A triangle: opaque red below the diagonal, fully transparent above.
        ImageFrame frame = ImageFrame.Create(40, 40, ImageColorFormat.Rgba32);
        for (int y = 0; y < 40; y++)
        {
            for (int x = 0; x < 40; x++)
            {
                frame.Pixels.SetPixelBgra(x, y, 0, 0, 255, (byte)(x <= y ? 255 : 0));
            }
        }

        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        PageBuilder page = builder.AddPage(PageSize.A4);
        page.DrawRectangle(0, 0, 200, 200, fill: Blue);
        page.DrawImage(Png(frame, alpha: true), 50, 50, 80, 80);

        byte[] pdf = builder.ToByteArray();
        PixelBuffer px = Raster(pdf);

        Encoding.Latin1.GetString(pdf).Should().Contain("/SMask");
        Rgb(px, 60, 120).Should().Be(((byte)255, (byte)0, (byte)0), "below the diagonal the image is opaque");
        Rgb(px, 120, 60).Should().Be(((byte)0, (byte)0, (byte)255), "above it the blue background shows through");
    }

    private static IEnumerable<PdfDictionary> Annotations(PdfDocument doc, int page)
    {
        PdfArray annots = doc.Pages[page].Dictionary.GetAs<PdfArray>(PdfName.Intern("Annots"))!;
        for (int i = 0; i < annots.Count; i++)
        {
            yield return doc.Objects.ResolveAs<PdfDictionary>(annots[i])!;
        }
    }

    private static double[] Numbers(PdfArray array)
    {
        double[] values = new double[array.Count];
        for (int i = 0; i < array.Count; i++)
        {
            values[i] = array[i] switch
            {
                PdfInteger n => n.Value,
                PdfReal r => Math.Round(r.Value, 2),
                _ => double.NaN,
            };
        }

        return values;
    }

    private static ImageFrame Solid(int w, int h, byte r, byte g, byte b, byte a)
    {
        ImageFrame frame = ImageFrame.Create(w, h, ImageColorFormat.Rgba32);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                frame.Pixels.SetPixelBgra(x, y, b, g, r, a);
            }
        }

        return frame;
    }

    private static byte[] Png(ImageFrame frame, bool alpha)
    {
        using MemoryStream ms = new MemoryStream();
        PngEncoder.Encode(frame, ms, alpha);
        return ms.ToArray();
    }
}
