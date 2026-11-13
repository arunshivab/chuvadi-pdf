// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.34.0 (D-335) — Redactor.ApplyWithReport.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Redaction;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Integration.Tests;

public sealed class RedactionReport3340Tests
{
    // Page 1 (200 x 200):
    //   "HELLO" at (10, 100), 12 pt Helvetica         -> inside the region
    //   image Im1 placed at (100, 20) 50 x 50          -> half inside: blanked
    //   path 10 10 40 40 re f                          -> inside: removed
    //   path 100 100 50 50 re f                        -> crosses x = 120: cut
    //   path 150 150 20 20 re f                        -> outside: kept
    //   a Square annotation and a text-field widget inside the region
    // Region: (0, 0) to (120, 120).
    private const string Content =
        "BT /F1 12 Tf 10 100 Td (HELLO) Tj ET\n"
        + "q 50 0 0 50 100 20 cm /Im1 Do Q\n"
        + "0 0 1 rg 10 10 40 40 re f\n"
        + "100 100 50 50 re f\n"
        + "150 150 20 20 re f\n";

    [Fact]
    public void ApplyWithReport_CountsWhatWasRemoved_PerPage()
    {
        using PdfDocument doc = TestPdf.Open(Fixture());
        using MemoryStream output = new MemoryStream();

        RedactionReport report = Redactor.ApplyWithReport(output, doc, new RedactionOptions
        {
            Rectangles = new List<RedactionRect>
            {
                new RedactionRect(0, new RectangleF(0, 0, 120, 120)),
                new RedactionRect(2, new RectangleF(0, 0, 5, 5)),
            },
        });

        report.Pages.Should().HaveCount(2, "page 2 has no rectangle and is not rewritten");
        PageRedactionReport first = report.Pages[0];
        first.PageIndex.Should().Be(0);
        first.GlyphsRemoved.Should().Be(5);
        first.ImagesBlanked.Should().Be(1);
        first.ImagesRemoved.Should().Be(0);
        first.PathsRemoved.Should().Be(1);
        first.PathsCut.Should().Be(1);
        first.AnnotationsRemoved.Should().Be(1);
        first.FieldValuesCleared.Should().Be(1);

        PageRedactionReport third = report.Pages[1];
        third.PageIndex.Should().Be(2);
        (third.GlyphsRemoved + third.ImagesBlanked + third.ImagesRemoved + third.PathsCut + third.PathsRemoved
            + third.AnnotationsRemoved + third.FieldValuesCleared + third.AlternateTextsRemoved).Should().Be(0);
    }

    [Fact]
    public void ApplyWithReport_WritesTheSameBytesAsApply()
    {
        RedactionOptions options = new RedactionOptions
        {
            Rectangles = new List<RedactionRect> { new RedactionRect(0, new RectangleF(0, 0, 120, 120)) },
        };
        using PdfDocument doc = TestPdf.Open(Fixture());
        using MemoryStream viaApply = new MemoryStream();
        using MemoryStream viaReport = new MemoryStream();

        Redactor.Apply(viaApply, doc, options);
        Redactor.ApplyWithReport(viaReport, doc, options);

        viaReport.ToArray().Should().Equal(viaApply.ToArray());
    }

    [Fact]
    public void ApplyWithReport_CountsTextInsideAFormXObject_OnTheFirstPageThatDrawsIt()
    {
        TestPdf pdf = new TestPdf();
        int catalog = pdf.Reserve();
        int pages = pdf.Reserve();
        int font = pdf.Add("<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>");
        int form = pdf.AddStream("<</Type/XObject/Subtype/Form/BBox[0 0 200 200]/Resources<</Font<</F1 " + N(font) + " 0 R>>>>>>", "BT /F1 12 Tf 10 100 Td (ABC) Tj ET");
        int c1 = pdf.AddStream("<<>>", "/Fm1 Do");
        int c2 = pdf.AddStream("<<>>", "/Fm1 Do");
        string res = "<</XObject<</Fm1 " + N(form) + " 0 R>>>>";
        int p1 = pdf.Add("<</Type/Page/Parent " + N(pages) + " 0 R/MediaBox[0 0 200 200]/Resources " + res + "/Contents " + N(c1) + " 0 R>>");
        int p2 = pdf.Add("<</Type/Page/Parent " + N(pages) + " 0 R/MediaBox[0 0 200 200]/Resources " + res + "/Contents " + N(c2) + " 0 R>>");
        pdf.Set(pages, "<</Type/Pages/Kids[" + N(p1) + " 0 R " + N(p2) + " 0 R]/Count 2>>");
        pdf.Set(catalog, "<</Type/Catalog/Pages " + N(pages) + " 0 R>>");
        using PdfDocument doc = TestPdf.Open(pdf.Build(catalog));
        using MemoryStream output = new MemoryStream();

        RedactionReport report = Redactor.ApplyWithReport(output, doc, new RedactionOptions
        {
            Rectangles = new List<RedactionRect>
            {
                new RedactionRect(0, new RectangleF(0, 90, 200, 30)),
                new RedactionRect(1, new RectangleF(0, 90, 200, 30)),
            },
        });

        report.Pages[0].GlyphsRemoved.Should().Be(3);
        report.Pages[1].GlyphsRemoved.Should().Be(0, "the shared form is counted once, on page 1");
    }

    private static byte[] Fixture()
    {
        TestPdf pdf = new TestPdf();
        int catalog = pdf.Reserve();
        int pages = pdf.Reserve();
        int font = pdf.Add("<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>");
        int image = pdf.AddStream("<</Type/XObject/Subtype/Image/Width 2/Height 2/ColorSpace/DeviceRGB/BitsPerComponent 8>>", new byte[12]);
        int p1 = pdf.Reserve();
        int square = pdf.Add("<</Type/Annot/Subtype/Square/Rect[20 20 60 60]/P " + N(p1) + " 0 R/C[1 0 0]>>");
        int widget = pdf.Add("<</Type/Annot/Subtype/Widget/FT/Tx/T(mrn)/V(12345)/Rect[20 70 110 90]/P " + N(p1) + " 0 R/F 4>>");
        string resources = "<</Font<</F1 " + N(font) + " 0 R>>/XObject<</Im1 " + N(image) + " 0 R>>>>";
        int c1 = pdf.AddStream("<<>>", Content);
        pdf.Set(p1, "<</Type/Page/Parent " + N(pages) + " 0 R/MediaBox[0 0 200 200]/Resources " + resources + "/Contents " + N(c1) + " 0 R/Annots[" + N(square) + " 0 R " + N(widget) + " 0 R]>>");
        int c2 = pdf.AddStream("<<>>", Content);
        int p2 = pdf.Add("<</Type/Page/Parent " + N(pages) + " 0 R/MediaBox[0 0 200 200]/Resources " + resources + "/Contents " + N(c2) + " 0 R>>");
        int c3 = pdf.AddStream("<<>>", "150 150 20 20 re f");
        int p3 = pdf.Add("<</Type/Page/Parent " + N(pages) + " 0 R/MediaBox[0 0 200 200]/Contents " + N(c3) + " 0 R>>");
        pdf.Set(pages, "<</Type/Pages/Kids[" + N(p1) + " 0 R " + N(p2) + " 0 R " + N(p3) + " 0 R]/Count 3>>");
        pdf.Set(catalog, "<</Type/Catalog/Pages " + N(pages) + " 0 R/AcroForm<</Fields[" + N(widget) + " 0 R]>>>>");
        return pdf.Build(catalog);
    }

    private static string N(int n) => n.ToString(CultureInfo.InvariantCulture);
}
