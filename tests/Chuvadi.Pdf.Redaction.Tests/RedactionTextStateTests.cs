// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// Tests for v3.20 (adversarial review): text placed by the content stream's
// own cm, by text rise or by horizontal scaling is redacted where it is
// drawn; survivors keep their positions under Tc/Tw; and objects that only a
// removed image or fill used (soft masks, tiling patterns) leave the file.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Redaction.Tests;

public sealed class RedactionTextStateTests
{
    private static readonly RectangleF Box = new RectangleF(250, 250, 150, 100);

    [Theory]
    [InlineData("q 10 0 0 10 0 0 cm BT /F1 1.2 Tf 30 30 Td (SECRET) Tj ET Q")]
    [InlineData("q 1 0 0 1 200 200 cm BT /F1 12 Tf 100 100 Td (SECRET) Tj ET Q")]
    [InlineData("q 1 0 0 -1 0 400 cm BT /F1 12 Tf 1 0 0 -1 300 90 Tm (SECRET) Tj ET Q")]
    [InlineData("0.5 0 0 0.5 0 0 cm BT /F1 24 Tf 600 600 Td (SECRET) Tj ET")]
    [InlineData("BT /F1 12 Tf 300 200 Td 100 Ts (SECRET) Tj ET")]
    [InlineData("BT /F1 12 Tf 200 300 Td 300 Tz (ABCSECRET) Tj ET")]
    public void TextDrawnInsideTheBox_IsRemoved(string content)
    {
        byte[] output = Redact(TextPage(content));

        Leaks(output, "SECRET").Should().BeFalse();
        using PdfDocument doc = Open(output);
        RedactionVerifier.Verify(doc, new[] { new RedactionRect(0, Box) }).IsClean.Should().BeTrue();
    }

    [Theory]
    [InlineData("BT /F1 12 Tf 200 300 Td 20 Tc (ABCDESECRETJ) Tj ET")]
    [InlineData("BT /F1 12 Tf 200 300 Td 20 Tc [(ABCDE) -10 (SECRETJ)] TJ ET")]
    [InlineData("BT /F1 12 Tf 200 300 Td 30 Tw (A B C SECRET X Y Z) Tj ET")]
    public void Survivors_KeepTheirPositions_UnderCharAndWordSpacing(string content)
    {
        byte[] source = TextPage(content);
        RectangleF everything = new RectangleF(0, 0, 1000, 400);

        List<(string? Text, double X)> before = Glyphs(source, everything);
        List<(string? Text, double X)> after = Glyphs(Redact(source), everything);

        after.Should().NotBeEmpty();
        foreach ((string? text, double x) in after)
        {
            before.Should().Contain(g => g.Text == text && Math.Abs(g.X - x) < 0.01,
                "survivor '{0}' at {1:F2} must not move", text, x);
        }
    }

    [Fact]
    public void RemovedImage_TakesItsSoftMaskWithIt()
    {
        byte[] output = Redact(Build(new List<byte[]>
        {
            Obj("<</Type/Catalog/Pages 2 0 R>>"),
            Obj("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Obj("<</Type/Page/Parent 2 0 R/MediaBox[0 0 400 400]/Resources<</XObject<</Im0 5 0 R>>>>/Contents 4 0 R>>"),
            Stream(string.Empty, "q 50 0 0 50 300 300 cm /Im0 Do Q"),
            Stream("/Type/XObject/Subtype/Image/Width 7/Height 1/ColorSpace/DeviceGray/BitsPerComponent 8/SMask 6 0 R",
                "ZZZZZZZ"),
            Stream("/Type/XObject/Subtype/Image/Width 7/Height 1/ColorSpace/DeviceGray/BitsPerComponent 8", "SMASKSE"),
        }));

        Leaks(output, "SMASKSE").Should().BeFalse();
    }

    [Fact]
    public void FigureAltText_IsStripped_WhenItsImageIsRemoved()
    {
        byte[] output = Redact(Build(new List<byte[]>
        {
            Obj("<</Type/Catalog/Pages 2 0 R/StructTreeRoot 6 0 R/MarkInfo<</Marked true>>>>"),
            Obj("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Obj("<</Type/Page/Parent 2 0 R/MediaBox[0 0 400 400]/StructParents 0/Resources<</XObject<</Im0 5 0 R>>>>/Contents 4 0 R>>"),
            Stream(string.Empty, "/Figure <</MCID 0>> BDC q 50 0 0 50 300 300 cm /Im0 Do Q EMC"),
            Stream("/Type/XObject/Subtype/Image/Width 1/Height 1/ColorSpace/DeviceGray/BitsPerComponent 8", "\u0007"),
            Obj("<</Type/StructTreeRoot/K 7 0 R/ParentTree 8 0 R>>"),
            Obj("<</Type/StructElem/S/Figure/P 6 0 R/Pg 3 0 R/K 0/Alt (ALTSECRET)>>"),
            Obj("<</Nums[0 [7 0 R]]>>"),
        }));

        Leaks(output, "ALTSECRET").Should().BeFalse();
    }

    [Fact]
    public void TilingPatternOnlyUsedByARemovedFill_LeavesTheFile()
    {
        byte[] output = Redact(PatternPage("q /Pattern cs /P0 scn 260 260 100 50 re f Q"));

        Leaks(output, "CELLSECRET").Should().BeFalse();
    }

    [Fact]
    public void TilingPatternStillPaintedOutsideTheBox_IsKept()
    {
        byte[] output = Redact(PatternPage(
            "q /Pattern cs /P0 scn 260 260 100 50 re f Q q /Pattern cs /P0 scn 10 10 50 50 re f Q"));

        Leaks(output, "CELLSECRET").Should().BeTrue("the second fill still shows the pattern");
    }

    [Fact]
    public void GraphicsStateStillUsed_IsKept()
    {
        byte[] output = Redact(Build(new List<byte[]>
        {
            Obj("<</Type/Catalog/Pages 2 0 R>>"),
            Obj("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Obj("<</Type/Page/Parent 2 0 R/MediaBox[0 0 400 400]"
                + "/Resources<</Font<</F1 5 0 R>>/ExtGState<</GS0 6 0 R>>>>/Contents 4 0 R>>"),
            Stream(string.Empty, "/GS0 gs BT /F1 12 Tf 300 300 Td (SECRET) Tj ET BT /F1 12 Tf 20 20 Td (KEEP) Tj ET"),
            Obj("<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>"),
            Obj("<</Type/ExtGState/ca 0.5/GSMARKER true>>"),
        }));

        Encoding.Latin1.GetString(output).Should().Contain("GSMARKER");
    }

    // ── helpers ──

    private static byte[] PatternPage(string content) => Build(new List<byte[]>
    {
        Obj("<</Type/Catalog/Pages 2 0 R>>"),
        Obj("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
        Obj("<</Type/Page/Parent 2 0 R/MediaBox[0 0 400 400]/Resources<</Font<</F1 5 0 R>>/Pattern<</P0 6 0 R>>>>/Contents 4 0 R>>"),
        Stream(string.Empty, content),
        Obj("<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>"),
        Stream("/PatternType 1/PaintType 1/TilingType 1/BBox[0 0 100 100]/XStep 100/YStep 100"
            + "/Resources<</Font<</F1 5 0 R>>>>", "BT /F1 12 Tf 10 10 Td (CELLSECRET) Tj ET"),
    });

    private static List<(string? Text, double X)> Glyphs(byte[] pdf, RectangleF region)
    {
        using PdfDocument doc = Open(pdf);
        return RedactionVerifier.ExtractWithin(doc, 0, region)
            .Where(s => s.Kind == RedactionSurvivorKind.Text)
            .Select(s => (s.Text, (double)s.Bounds.X))
            .ToList();
    }

    private static bool Leaks(byte[] pdf, string secret)
    {
        string text = Encoding.Latin1.GetString(pdf);
        string hex = Convert.ToHexString(Encoding.Latin1.GetBytes(secret));
        return text.Contains(secret, StringComparison.Ordinal)
            || text.Contains(hex, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] Redact(byte[] source)
    {
        using PdfDocument doc = Open(source);
        using MemoryStream output = new MemoryStream();
        Redactor.Apply(output, doc, new RedactionOptions
        {
            Rectangles = new List<RedactionRect> { new RedactionRect(0, Box) },
        });
        return output.ToArray();
    }

    private static byte[] TextPage(string content) => Build(new List<byte[]>
    {
        Obj("<</Type/Catalog/Pages 2 0 R>>"),
        Obj("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
        Obj("<</Type/Page/Parent 2 0 R/MediaBox[0 0 400 400]/Resources<</Font<</F1 5 0 R>>>>/Contents 4 0 R>>"),
        Stream(string.Empty, content),
        Obj("<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>"),
    });

    private static PdfDocument Open(byte[] bytes) => PdfDocument.Open(new MemoryStream(bytes), leaveOpen: false);

    private static byte[] Obj(string body) => Encoding.Latin1.GetBytes(body);

    private static byte[] Stream(string entries, string content)
    {
        byte[] data = Encoding.Latin1.GetBytes(content);
        using MemoryStream ms = new MemoryStream();
        ms.Write(Encoding.Latin1.GetBytes(
            "<<" + entries + "/Length " + data.Length.ToString(CultureInfo.InvariantCulture) + ">>\nstream\n"));
        ms.Write(data);
        ms.Write(Encoding.Latin1.GetBytes("\nendstream"));
        return ms.ToArray();
    }

    private static byte[] Build(List<byte[]> bodies)
    {
        using MemoryStream ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.Latin1.GetBytes(s));
        Write("%PDF-1.7\n");
        List<long> offsets = new List<long>();
        for (int i = 0; i < bodies.Count; i++)
        {
            offsets.Add(ms.Length);
            Write((i + 1).ToString(CultureInfo.InvariantCulture) + " 0 obj\n");
            ms.Write(bodies[i]);
            Write("\nendobj\n");
        }

        long xref = ms.Length;
        Write("xref\n0 " + (bodies.Count + 1).ToString(CultureInfo.InvariantCulture) + "\n0000000000 65535 f \n");
        foreach (long off in offsets)
        {
            Write(off.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        }

        Write("trailer <</Size " + (bodies.Count + 1).ToString(CultureInfo.InvariantCulture)
            + "/Root 1 0 R>>\nstartxref\n" + xref.ToString(CultureInfo.InvariantCulture) + "\n%%EOF");
        return ms.ToArray();
    }
}
