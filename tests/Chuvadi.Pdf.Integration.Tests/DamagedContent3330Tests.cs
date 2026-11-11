// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.8.2 (content streams), §7.4.4 (FlateDecode)
// PHASE: 3.33.0 (ticket 39 option A, D-325) — a page whose content stream is
//        damaged shows and extracts what decoded before the damage, with a
//        StreamDamaged warning; undamaged pages are unchanged. D-332: after h
//        a path continues from the closed subpath's start (as Poppler does),
//        and a curve with no current point is skipped with a warning.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Rendering.DisplayList;
using Chuvadi.Pdf.Text;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Integration.Tests;

public sealed class DamagedContent3330Tests
{
    private const string Resources = "<</Font<</Helv<</Type/Font/Subtype/Type1/BaseFont/Helvetica/Encoding/WinAnsiEncoding>>>>>>";

    // Decodes fine: a blue box and "Hello". The damage starts inside a red
    // box's operands, which must not be drawn.
    private const string GoodPart = "0 0 1 rg 20 20 60 60 re f\nBT /Helv 20 Tf 20 120 Td (Hello) Tj ET\n1 0 0 rg 120 20 4";

    private const string LostPart = "0 40 re f\nBT /Helv 20 Tf 20 160 Td (Lost) Tj ET\n";

    [Fact]
    public void DamagedPage_ExtractsTheTextBeforeTheDamage_WithAWarning()
    {
        List<PdfWarning> warnings = new List<PdfWarning>();
        using PdfDocument doc = TestPdf.Open(TwoPages(DamagedFlate()), warnings);

        string text = new TextExtractor(doc.Objects).ExtractText(doc.Pages[0]);

        text.Should().Contain("Hello");
        text.Should().NotContain("Lost");
        warnings.Should().Contain(w => w.Kind == PdfWarningKind.StreamDamaged && w.PageNumber == 1
            && w.Reason.Contains("The page shows what decoded before the damage.", StringComparison.Ordinal));
    }

    [Fact]
    public void DamagedPage_DrawsWhatDecoded_AndNotTheCutOperator()
    {
        List<PdfWarning> warnings = new List<PdfWarning>();
        using PdfDocument doc = TestPdf.Open(TwoPages(DamagedFlate()), warnings);

        TestPdf.Ink(doc, AnnotationMode.View, 25, 25, 75, 75).Should().BeGreaterThan(2000, "the blue box decoded");
        TestPdf.Ink(doc, AnnotationMode.View, 120, 20, 160, 60).Should().Be(0, "the red box's operands were cut");
        warnings.Should().Contain(w => w.Kind == PdfWarningKind.StreamDamaged && w.PageNumber == 1);
    }

    [Fact]
    public void DamagedPage_BuildsADisplayList()
    {
        using PdfDocument doc = TestPdf.Open(TwoPages(DamagedFlate()));

        FluentActions.Invoking(() => DisplayListBuilder.Build(doc, 0)).Should().NotThrow();
    }

    [Fact]
    public void UnreadableStream_GivesAnEmptyPage_WithAWarning()
    {
        List<PdfWarning> warnings = new List<PdfWarning>();
        byte[] garbage = { 0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC };
        using PdfDocument doc = TestPdf.Open(TwoPages(garbage), warnings);

        new TextExtractor(doc.Objects).ExtractText(doc.Pages[0]).Trim().Should().BeEmpty();
        TestPdf.Ink(doc, AnnotationMode.View, 0, 0, 200, 200).Should().Be(0);
        warnings.Should().Contain(w => w.Kind == PdfWarningKind.StreamDamaged && w.PageNumber == 1
            && w.Reason.Contains("could not be read and is left out", StringComparison.Ordinal));
    }

    [Fact]
    public void UndamagedPage_IsUnchanged_AndHasNoWarning()
    {
        List<PdfWarning> warnings = new List<PdfWarning>();
        using PdfDocument doc = TestPdf.Open(TwoPages(DamagedFlate()), warnings);

        new TextExtractor(doc.Objects).ExtractText(doc.Pages[1]).Trim().Should().Be("Second page");
        warnings.Should().NotContain(w => w.PageNumber == 2);
    }

    [Fact]
    public void GoodFlateStream_GivesNoWarning()
    {
        List<PdfWarning> warnings = new List<PdfWarning>();
        using PdfDocument doc = TestPdf.Open(TwoPages(Zlib(GoodPart + "0 40 re f\n")), warnings);

        new TextExtractor(doc.Objects).ExtractText(doc.Pages[0]).Should().Contain("Hello");
        TestPdf.Ink(doc, AnnotationMode.View, 125, 25, 155, 55).Should().BeGreaterThan(500, "the red box is whole");
        warnings.Should().NotContain(w => w.Kind == PdfWarningKind.StreamDamaged);
    }

    [Fact]
    public void LineAfterClosePath_ContinuesFromTheSubpathStart()
    {
        // Before 3.33.0 the l after h threw "LineTo requires a current point".
        List<PdfWarning> warnings = new List<PdfWarning>();
        using PdfDocument doc = TestPdf.Open(TestPdf.OnePage("0 0 1 RG 6 w 20 20 m 180 20 l h 20 180 l S", Resources, System.Array.Empty<string>()), warnings);

        TestPdf.Ink(doc, AnnotationMode.View, 14, 60, 26, 140).Should().BeGreaterThan(100, "the line from (20 20) up to (20 180)");
        TestPdf.Ink(doc, AnnotationMode.View, 60, 60, 140, 140).Should().Be(0);
        warnings.Should().BeEmpty();
    }

    [Fact]
    public void RectangleThenLine_ContinuesFromTheRectangleCorner()
    {
        using PdfDocument doc = TestPdf.Open(TestPdf.OnePage("0 0 1 RG 6 w 20 20 40 40 re 160 20 l S", Resources, System.Array.Empty<string>()));

        TestPdf.Ink(doc, AnnotationMode.View, 90, 14, 150, 26).Should().BeGreaterThan(100, "the line from (20 20) to (160 20)");
    }

    [Fact]
    public void CurveWithoutCurrentPoint_IsSkippedWithAWarning()
    {
        List<PdfWarning> warnings = new List<PdfWarning>();
        using PdfDocument doc = TestPdf.Open(TestPdf.OnePage("0 0 1 rg 30 30 40 40 50 50 c 20 20 60 60 re f", Resources, System.Array.Empty<string>()), warnings);

        TestPdf.Ink(doc, AnnotationMode.View, 25, 25, 75, 75).Should().BeGreaterThan(2000, "the box after the skipped curve");
        warnings.Should().ContainSingle(w => w.Kind == PdfWarningKind.ContentOperatorSkipped)
            .Which.Subject.Should().Be("path operator c");
    }

    // A zlib stream of two stored blocks: the first holds GoodPart, the
    // second LostPart with a wrong length complement, so inflating stops
    // with an error after the first block.
    private static byte[] DamagedFlate()
    {
        byte[] good = Encoding.Latin1.GetBytes(GoodPart);
        byte[] lost = Encoding.Latin1.GetBytes(LostPart);
        using MemoryStream ms = new MemoryStream();
        ms.WriteByte(0x78);
        ms.WriteByte(0x01);
        StoredBlock(ms, good, final: false, complement: (ushort)~good.Length);
        StoredBlock(ms, lost, final: true, complement: 0x1234);
        return ms.ToArray();
    }

    private static void StoredBlock(MemoryStream ms, byte[] data, bool final, ushort complement)
    {
        ms.WriteByte(final ? (byte)1 : (byte)0);
        ms.WriteByte((byte)data.Length);
        ms.WriteByte((byte)(data.Length >> 8));
        ms.WriteByte((byte)complement);
        ms.WriteByte((byte)(complement >> 8));
        ms.Write(data);
    }

    private static byte[] Zlib(string content)
    {
        using MemoryStream ms = new MemoryStream();
        using (System.IO.Compression.ZLibStream z = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            z.Write(Encoding.Latin1.GetBytes(content));
        }

        return ms.ToArray();
    }

    // Page 1 has the given FlateDecode content; page 2 is an ordinary page.
    private static byte[] TwoPages(byte[] firstContent)
    {
        TestPdf pdf = new TestPdf();
        int catalog = pdf.Reserve();
        int pages = pdf.Reserve();
        int first = pdf.AddStream("<</Filter/FlateDecode>>", firstContent);
        int second = pdf.AddStream("<<>>", "BT /Helv 20 Tf 20 120 Td (Second page) Tj ET");
        string pagesRef = N(pages) + " 0 R";
        int page1 = pdf.Add("<</Type/Page/Parent " + pagesRef + "/MediaBox[0 0 200 200]/Resources " + Resources + "/Contents " + N(first) + " 0 R>>");
        int page2 = pdf.Add("<</Type/Page/Parent " + pagesRef + "/MediaBox[0 0 200 200]/Resources " + Resources + "/Contents " + N(second) + " 0 R>>");
        pdf.Set(catalog, "<</Type/Catalog/Pages " + pagesRef + ">>");
        pdf.Set(pages, "<</Type/Pages/Kids[" + N(page1) + " 0 R " + N(page2) + " 0 R]/Count 2>>");
        return pdf.Build(catalog);
    }

    private static string N(int n) => n.ToString(CultureInfo.InvariantCulture);
}
