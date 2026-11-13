// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §11.6.4.3 (soft mask in the graphics state),
//        §8.10.1 (form XObjects)
// PHASE: 3.34.0 (#42, D-264) — the soft mask in effect at a form's Do
//        applies to the form's content.

using System.Globalization;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Rendering;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Integration.Tests;

public sealed class FormSoftMask3340Tests
{
    [Fact]
    public void SoftMaskSetBeforeDo_MasksTheForm()
    {
        // The luminosity mask is white on the left half, black on the right:
        // the red form shows on the left only.
        using PdfDocument doc = TestPdf.Open(Document("q /GS1 gs /Fx Do Q"));
        PixelBuffer px = new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72 }).Rasterize(doc.Pages[0]);

        Colour(px, 50, 100).Should().Be("R");
        Colour(px, 150, 100).Should().Be("W");
    }

    [Fact]
    public void FormOutsideTheMask_IsUnchanged()
    {
        using PdfDocument doc = TestPdf.Open(Document("q /GS1 gs Q /Fx Do"));
        PixelBuffer px = new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72 }).Rasterize(doc.Pages[0]);

        Colour(px, 50, 100).Should().Be("R");
        Colour(px, 150, 100).Should().Be("R");
    }

    private static byte[] Document(string content)
    {
        TestPdf pdf = new TestPdf();
        int catalog = pdf.Reserve();
        int pages = pdf.Reserve();
        int group = pdf.AddStream("<</Type/XObject/Subtype/Form/BBox[0 0 200 200]/Group<</S/Transparency/CS/DeviceGray>>>>", "1 g 0 0 100 200 re f 0 g 100 0 100 200 re f");
        int form = pdf.AddStream("<</Type/XObject/Subtype/Form/BBox[0 0 200 200]>>", "1 0 0 rg 0 0 200 200 re f");
        int contents = pdf.AddStream("<<>>", content);
        int page = pdf.Add("<</Type/Page/Parent " + N(pages) + " 0 R/MediaBox[0 0 200 200]/Resources<</ExtGState<</GS1<</SMask<</S/Luminosity/G "
            + N(group) + " 0 R>>>>>>/XObject<</Fx " + N(form) + " 0 R>>>>/Contents " + N(contents) + " 0 R>>");
        pdf.Set(pages, "<</Type/Pages/Kids[" + N(page) + " 0 R]/Count 1>>");
        pdf.Set(catalog, "<</Type/Catalog/Pages " + N(pages) + " 0 R>>");
        return pdf.Build(catalog);
    }

    private static string Colour(PixelBuffer px, int x, int y)
    {
        (byte b, byte g, byte r, byte _) = px.GetPixelBgra(x, y);
        return (r > 200, g > 200, b > 200) switch
        {
            (true, false, false) => "R",
            (true, true, true) => "W",
            _ => FormattableString(r, g, b),
        };
    }

    private static string FormattableString(byte r, byte g, byte b) =>
        string.Create(CultureInfo.InvariantCulture, $"?{r},{g},{b}");

    private static string N(int n) => n.ToString(CultureInfo.InvariantCulture);
}
