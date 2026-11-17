// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.35.0 (OPS-01) — a page that is deleted, or left out of an extract
//        or a split, is never written: a reference to it from a kept page
//        (a link's /Dest or GoTo, an annotation's /P, any other key) becomes
//        null instead of pulling the page, and its content, back in. A
//        reference to a kept page is pointed at that page's new object.
//        The inputs follow the Checker's operations probes (D-394).

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

public sealed class RemovedPages3350Tests
{
    private const string Secret = "SALARY";

    public static TheoryData<string> Links => new TheoryData<string>
    {
        "none",
        "dest-on-page-3",
        "goto-on-page-1",
        "named-dest",
        "annot-p",
        "extra-key",
    };

    [Theory]
    [MemberData(nameof(Links))]
    public void DeletePages_RemovedPageIsNotWritten(string link)
    {
        using PdfDocument doc = Open(ThreePages(link));
        using MemoryStream output = new MemoryStream();

        PageOperations.DeletePages(output, doc, new[] { 1 });

        AssertClean(output.ToArray(), expectedPages: 2);
    }

    [Theory]
    [MemberData(nameof(Links))]
    public void ExtractPages_LeftOutPageIsNotWritten(string link)
    {
        using PdfDocument doc = Open(ThreePages(link));
        using MemoryStream first = new MemoryStream();
        using MemoryStream last = new MemoryStream();

        PageOperations.ExtractPages(first, doc, 0, 1);
        PageOperations.ExtractPages(last, doc, 2, 1);

        AssertClean(first.ToArray(), expectedPages: 1);
        AssertClean(last.ToArray(), expectedPages: 1);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("dest-on-page-3")]
    [InlineData("annot-p")]
    public void SplitPages_EachPartHoldsOnlyItsPage(string link)
    {
        using PdfDocument doc = Open(ThreePages(link));

        List<MemoryStream> parts = PageOperations.SplitPages(doc);

        parts.Should().HaveCount(3);
        AssertClean(parts[0].ToArray(), expectedPages: 1);
        AssertClean(parts[2].ToArray(), expectedPages: 1);
        Raw(parts[1].ToArray()).Should().Contain(Secret, "the part made from page 2 holds page 2");
    }

    [Fact]
    public void DeletePages_LinkToRemovedPage_BecomesNull()
    {
        using PdfDocument doc = Open(ThreePages("dest-on-page-3"));
        using MemoryStream output = new MemoryStream();

        PageOperations.DeletePages(output, doc, new[] { 1 });

        using PdfDocument result = Open(output.ToArray());
        PdfArray dest = (PdfArray)result.Objects.Resolve(FirstAnnot(result, 1)[PdfName.Intern("Dest")])!;
        result.Objects.Resolve(dest[0]).Should().BeOfType<PdfNull>();
    }

    [Fact]
    public void DeletePages_LinkToKeptPage_PointsAtItsNewObject()
    {
        using PdfDocument doc = Open(ThreePages("dest-page-1-to-3"));
        using MemoryStream output = new MemoryStream();

        PageOperations.DeletePages(output, doc, new[] { 1 });

        using PdfDocument result = Open(output.ToArray());
        PdfArray dest = (PdfArray)result.Objects.Resolve(FirstAnnot(result, 0)[PdfName.Intern("Dest")])!;
        PdfDictionary root = (PdfDictionary)result.Objects.Resolve(result.Trailer[PdfName.Root])!;
        PdfDictionary tree = (PdfDictionary)result.Objects.Resolve(root[PdfName.Intern("Pages")])!;
        PdfArray kids = (PdfArray)result.Objects.Resolve(tree[PdfName.Intern("Kids")])!;
        ((PdfReference)dest[0]).ObjectId.Should().Be(((PdfReference)kids[1]).ObjectId);
    }

    private static PdfDictionary FirstAnnot(PdfDocument doc, int page)
    {
        PdfArray annots = (PdfArray)doc.Objects.Resolve(doc.Pages[page].Dictionary[PdfName.Intern("Annots")])!;
        return (PdfDictionary)doc.Objects.Resolve(annots[0])!;
    }

    private static void AssertClean(byte[] pdf, int expectedPages)
    {
        string raw = Raw(pdf);
        raw.Should().NotContain(Secret, "the removed page's content is not written");
        Regex.Matches(raw, @"/Type\s*/Page\b(?!s)").Count.Should().Be(expectedPages);
        using PdfDocument reopened = Open(pdf);
        reopened.PageCount.Should().Be(expectedPages);
    }

    // The file's bytes, with every Flate stream inflated.
    private static string Raw(byte[] pdf)
    {
        string s = Encoding.Latin1.GetString(pdf);
        StringBuilder sb = new StringBuilder(s);
        foreach (Match m in Regex.Matches(s, @"stream\r?\n(.*?)endstream", RegexOptions.Singleline))
        {
            try
            {
                using ZLibStream z = new ZLibStream(new MemoryStream(Encoding.Latin1.GetBytes(m.Groups[1].Value)), CompressionMode.Decompress);
                using MemoryStream o = new MemoryStream();
                z.CopyTo(o);
                sb.Append(Encoding.Latin1.GetString(o.ToArray()));
            }
            catch (InvalidDataException)
            {
                // Not Flate data: already in the raw text.
            }
        }

        return sb.ToString();
    }

    private static PdfDocument Open(byte[] pdf) => PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);

    private static string Content(string text)
    {
        string data = "BT /F1 18 Tf 72 700 Td (" + text + ") Tj ET";
        return "<< /Length " + data.Length.ToString(CultureInfo.InvariantCulture) + " >>\nstream\n" + data + "\nendstream";
    }

    // Objects: 1 catalog, 2 pages, 3–5 the pages, 6 font, 7–9 contents, 10 annotation.
    private static byte[] ThreePages(string link)
    {
        string annots1 = string.Empty;
        string annots3 = string.Empty;
        string catalogExtra = string.Empty;
        string annotation = "<< >>";
        switch (link)
        {
            case "dest-on-page-3":
                annotation = "<< /Type /Annot /Subtype /Link /Rect [72 600 300 620] /Dest [4 0 R /Fit] >>";
                annots3 = "/Annots [10 0 R]";
                break;
            case "dest-page-1-to-3":
                annotation = "<< /Type /Annot /Subtype /Link /Rect [72 600 300 620] /Dest [5 0 R /Fit] >>";
                annots1 = "/Annots [10 0 R]";
                break;
            case "goto-on-page-1":
                annotation = "<< /Type /Annot /Subtype /Link /Rect [72 600 300 620] /A << /S /GoTo /D [4 0 R /XYZ 0 792 0] >> >>";
                annots1 = "/Annots [10 0 R]";
                break;
            case "named-dest":
                annotation = "<< /Type /Annot /Subtype /Link /Rect [72 600 300 620] /Dest /sal >>";
                annots1 = "/Annots [10 0 R]";
                catalogExtra = "/Dests << /sal [4 0 R /Fit] >>";
                break;
            case "annot-p":
                annotation = "<< /Type /Annot /Subtype /Text /Rect [72 600 90 620] /Contents (note) /P 4 0 R >>";
                annots1 = "/Annots [10 0 R]";
                break;
            case "extra-key":
                annots1 = "/PieceInfo << /X << /Data 4 0 R >> >>";
                break;
            default:
                break;
        }

        const string resources = "/Resources << /Font << /F1 6 0 R >> >>";
        string[] objects =
        {
            "<< /Type /Catalog /Pages 2 0 R " + catalogExtra + " >>",
            "<< /Type /Pages /Kids [3 0 R 4 0 R 5 0 R] /Count 3 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] " + resources + " /Contents 7 0 R " + annots1 + " >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] " + resources + " /Contents 8 0 R >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] " + resources + " /Contents 9 0 R " + annots3 + " >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            Content("Page one: public"),
            Content(Secret + ": 9,99,999"),
            Content("Page three: public"),
            annotation,
        };
        StringBuilder sb = new StringBuilder("%PDF-1.7\n");
        List<int> offsets = new List<int>();
        for (int i = 0; i < objects.Length; i++)
        {
            offsets.Add(sb.Length);
            sb.Append(CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        int xref = sb.Length;
        sb.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (int offset in offsets)
        {
            sb.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        sb.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(sb.ToString());
    }
}
