// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.11 — Optional content
// PHASE: 3.21 (PDF-14) — layers hidden in the default configuration are not
//        rendered (SVG, raster, text display list); per-render overrides and
//        IgnoreOptionalContent; OCMD policies, visibility expressions and the
//        View auto-state.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Rendering;
using Chuvadi.Pdf.Rendering.DisplayList;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Svg.Tests;

public sealed class OptionalContentRenderTests
{
    // Object 5 = OCG "Shown" (ON), object 6 = OCG "Secret" (OFF in /D).
    // Blue square at (100,100)-(150,150) inside the Secret layer; red square
    // at (250,250)-(300,300) inside the Shown layer.
    private const string TwoLayerContent =
        "/OC /L1 BDC 1 0 0 rg 250 250 50 50 re f BT /F1 12 Tf 50 350 Td (SHOWNTEXT) Tj ET EMC "
        + "/OC /L2 BDC 0 0 1 rg 100 100 50 50 re f BT /F1 12 Tf 50 50 Td (HIDDENTEXT) Tj ET EMC";

    private static readonly Dictionary<string, bool> ShowSecret = new() { ["Secret"] = true };
    private static readonly Dictionary<string, bool> HideShown = new() { ["Shown"] = false };

    [Fact]
    public void Raster_HidesTheOffLayer_ByDefault()
    {
        using PdfDocument doc = Open(TwoLayerPdf(TwoLayerContent));

        PixelBuffer pixels = Rasterize(doc, new RenderOptions { Dpi = 72 });

        IsBlue(pixels, 125, 125).Should().BeFalse("the Secret layer is OFF");
        IsRed(pixels, 275, 275).Should().BeTrue("the Shown layer is ON");
    }

    [Fact]
    public void Raster_IgnoreOptionalContent_DrawsEveryLayer()
    {
        using PdfDocument doc = Open(TwoLayerPdf(TwoLayerContent));

        PixelBuffer pixels = Rasterize(doc, new RenderOptions { Dpi = 72, IgnoreOptionalContent = true });

        IsBlue(pixels, 125, 125).Should().BeTrue();
    }

    [Fact]
    public void Raster_LayerOverrides_ShowAndHideByName()
    {
        using PdfDocument doc = Open(TwoLayerPdf(TwoLayerContent));

        IsBlue(Rasterize(doc, new RenderOptions { Dpi = 72, LayerVisibility = ShowSecret }), 125, 125)
            .Should().BeTrue();
        IsRed(Rasterize(doc, new RenderOptions { Dpi = 72, LayerVisibility = HideShown }), 275, 275)
            .Should().BeFalse();
    }

    [Fact]
    public void Svg_HidesTheOffLayer_ByDefault_AndShowsItWhenIgnored()
    {
        using PdfDocument doc = Open(TwoLayerPdf(TwoLayerContent));

        string hidden = new SvgRenderer().RenderPage(doc, 0);
        string all = new SvgRenderer(new SvgExportOptions { IgnoreOptionalContent = true }).RenderPage(doc, 0);

        hidden.Should().NotContain("rgb(0,0,255)");
        hidden.Should().Contain("rgb(255,0,0)");
        all.Should().Contain("rgb(0,0,255)");
    }

    [Fact]
    public void DisplayList_DefaultBuild_LeavesOutHiddenText_ButKeepsItWithAllVisible()
    {
        using PdfDocument doc = Open(TwoLayerPdf(TwoLayerContent));

        Text(DisplayListBuilder.Build(doc, 0)).Should().Contain("SHOWNTEXT").And.NotContain("HIDDENTEXT");
        Text(DisplayListBuilder.Build(doc, 0, OptionalContentVisibility.AllVisible)).Should().Contain("HIDDENTEXT");
        doc.GetContentInventory(0).Paths.Should().Be(1, "only the visible square counts as drawn");
    }

    [Fact]
    public void GraphicsStateSetInsideAHiddenLayer_StillApplies()
    {
        // The colour is set inside the hidden sequence; the fill after EMC
        // must use it (hidden content is not drawn, but state still changes).
        using PdfDocument doc = Open(TwoLayerPdf("/OC /L2 BDC 0 0 1 rg EMC 100 100 50 50 re f"));

        IsBlue(Rasterize(doc, new RenderOptions { Dpi = 72 }), 125, 125).Should().BeTrue();
    }

    [Fact]
    public void FormXObject_WithHiddenOc_IsNotDrawn()
    {
        List<string> extra = new List<string>
        {
            Stream("/Type/XObject/Subtype/Form/BBox[0 0 400 400]/OC 6 0 R", "0 0 1 rg 100 100 50 50 re f"),
        };
        using PdfDocument doc = Open(TwoLayerPdf("/Fm0 Do", xobjects: "/Fm0 8 0 R", extra: extra));

        IsBlue(Rasterize(doc, new RenderOptions { Dpi = 72 }), 125, 125).Should().BeFalse();
        Svg(doc).Should().NotContain("rgb(0,0,255)");
        IsBlue(Rasterize(doc, new RenderOptions { Dpi = 72, IgnoreOptionalContent = true }), 125, 125)
            .Should().BeTrue();
    }

    [Theory]
    [InlineData("/P/AllOn/OCGs[5 0 R 6 0 R]", false)]
    [InlineData("/P/AnyOn/OCGs[5 0 R 6 0 R]", true)]
    [InlineData("/P/AnyOff/OCGs[5 0 R 6 0 R]", true)]
    [InlineData("/P/AllOff/OCGs[5 0 R 6 0 R]", false)]
    [InlineData("/OCGs 6 0 R", false)]
    [InlineData("/VE[/Not 6 0 R]", true)]
    [InlineData("/VE[/And 5 0 R [/Not 6 0 R]]", true)]
    [InlineData("/VE[/Or 6 0 R [/Not 5 0 R]]", false)]
    public void MembershipDictionaries_FollowTheirPolicyOrExpression(string ocmd, bool visible)
    {
        List<string> extra = new List<string> { "<</Type/OCMD" + ocmd + ">>" };
        using PdfDocument doc = Open(TwoLayerPdf(
            "/OC /M BDC 0 0 1 rg 100 100 50 50 re f EMC", properties: "/M 8 0 R", extra: extra));

        IsBlue(Rasterize(doc, new RenderOptions { Dpi = 72 }), 125, 125).Should().Be(visible);
    }

    [Fact]
    public void BaseStateOff_ShowsOnlyTheOnList()
    {
        using PdfDocument doc = Open(TwoLayerPdf(TwoLayerContent, defaultConfig: "<</BaseState/OFF/ON[6 0 R]>>"));

        PixelBuffer pixels = Rasterize(doc, new RenderOptions { Dpi = 72 });

        IsBlue(pixels, 125, 125).Should().BeTrue("Secret is listed ON");
        IsRed(pixels, 275, 275).Should().BeFalse("Shown falls back to BaseState OFF");
    }

    [Fact]
    public void ViewAutoState_TurnsAPrintOnlyLayerOffForViewing()
    {
        // "Shown" is ON in /D but its usage says ViewState OFF, applied by /AS
        // for the View event (a print-only watermark layer).
        using PdfDocument doc = Open(TwoLayerPdf(
            TwoLayerContent,
            defaultConfig: "<</OFF[6 0 R]/AS[<</Event/View/Category[/View]/OCGs[5 0 R]>>]>>",
            shownUsage: "/Usage<</View<</ViewState/OFF>>>>"));

        IsRed(Rasterize(doc, new RenderOptions { Dpi = 72 }), 275, 275).Should().BeFalse();
        OptionalContentVisibility.FromDocument(doc).IsLayerVisible("Shown").Should().BeFalse();
    }

    [Fact]
    public void Visibility_ReportsLayersByName_WithOverrides()
    {
        using PdfDocument doc = Open(TwoLayerPdf(TwoLayerContent));

        OptionalContentVisibility defaults = OptionalContentVisibility.FromDocument(doc);
        defaults.IsLayerVisible("Shown").Should().BeTrue();
        defaults.IsLayerVisible("Secret").Should().BeFalse();
        defaults.IsLayerVisible("NoSuchLayer").Should().BeTrue();
        OptionalContentVisibility.FromDocument(doc, ShowSecret).IsLayerVisible("Secret").Should().BeTrue();
        doc.Pages[0].OptionalContent.IsLayerVisible("Secret").Should().BeFalse();
    }

    [Fact]
    public void DocumentWithoutOptionalContent_DrawsEverything()
    {
        using PdfDocument doc = Open(Build(new List<string>
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 400 400]/Contents 4 0 R>>",
            Stream(string.Empty, "/OC /L2 BDC 0 0 1 rg 100 100 50 50 re f EMC"),
        }));

        doc.Pages[0].OptionalContent.ShowsEverything.Should().BeTrue();
        IsBlue(Rasterize(doc, new RenderOptions { Dpi = 72 }), 125, 125).Should().BeTrue();
    }

    // ── helpers ──

    private static string Svg(PdfDocument doc) => new SvgRenderer().RenderPage(doc, 0);

    private static PixelBuffer Rasterize(PdfDocument doc, RenderOptions options) =>
        new PageRasterizer(doc.Objects, options).Rasterize(doc.Pages[0]);

    // Pixel at PDF point (x, y) on a 400-point page rendered at 72 DPI.
    private static (byte B, byte G, byte R, byte A) At(PixelBuffer pixels, int x, int y) =>
        pixels.GetPixelBgra(x, 399 - y);

    private static bool IsBlue(PixelBuffer pixels, int x, int y)
    {
        (byte b, byte g, byte r, byte _) = At(pixels, x, y);
        return b > 200 && g < 60 && r < 60;
    }

    private static bool IsRed(PixelBuffer pixels, int x, int y)
    {
        (byte b, byte g, byte r, byte _) = At(pixels, x, y);
        return r > 200 && g < 60 && b < 60;
    }

    private static string Text(PageDisplayList list) =>
        string.Concat(list.OfType<TextOp>().SelectMany(t => t.Glyphs).Select(g => g.Unicode));

    private static byte[] TwoLayerPdf(
        string content, string defaultConfig = "<</OFF[6 0 R]>>", string shownUsage = "",
        string xobjects = "", string properties = "", List<string>? extra = null)
    {
        List<string> bodies = new List<string>
        {
            "<</Type/Catalog/Pages 2 0 R/OCProperties<</OCGs[5 0 R 6 0 R]/D " + defaultConfig + ">>>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 400 400]/Resources<</Font<</F1 7 0 R>>"
                + "/Properties<</L1 5 0 R/L2 6 0 R" + properties + ">>"
                + (xobjects.Length > 0 ? "/XObject<<" + xobjects + ">>" : string.Empty)
                + ">>/Contents 4 0 R>>",
            Stream(string.Empty, content),
            "<</Type/OCG/Name(Shown)" + shownUsage + ">>",
            "<</Type/OCG/Name(Secret)>>",
            "<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>",
        };
        if (extra is not null)
        {
            bodies.AddRange(extra);
        }

        return Build(bodies);
    }

    private static string Stream(string entries, string content) =>
        "<<" + entries + "/Length " + Encoding.Latin1.GetByteCount(content).ToString(CultureInfo.InvariantCulture)
        + ">>\nstream\n" + content + "\nendstream";

    private static PdfDocument Open(byte[] pdf) => PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);

    private static byte[] Build(List<string> bodies)
    {
        StringBuilder sb = new StringBuilder("%PDF-1.7\n");
        List<int> offsets = new List<int>();
        for (int i = 0; i < bodies.Count; i++)
        {
            offsets.Add(sb.Length);
            sb.Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append(" 0 obj\n").Append(bodies[i]).Append("\nendobj\n");
        }

        int xref = sb.Length;
        sb.Append("xref\n0 ").Append((bodies.Count + 1).ToString(CultureInfo.InvariantCulture)).Append("\n0000000000 65535 f \n");
        foreach (int off in offsets)
        {
            sb.Append(off.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        sb.Append("trailer <</Size ").Append((bodies.Count + 1).ToString(CultureInfo.InvariantCulture))
            .Append("/Root 1 0 R>>\nstartxref\n").Append(xref.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF");
        return Encoding.Latin1.GetBytes(sb.ToString());
    }
}
