// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.11.4.4 (usage and usage application: the Print
//        event and /PrintState), §12.5.2 (an annotation's /OC)
// PHASE: 3.34.0 (#43, D-353) — print-state layers; flattening keeps an
//        annotation's layer.

using System.Globalization;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Operations;
using Chuvadi.Pdf.Rendering;
using Chuvadi.Pdf.Text;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Integration.Tests;

public sealed class PrintLayers3340Tests
{
    [Fact]
    public void PrintOnlyLayer_IsHiddenOnScreen_AndShownForPrint()
    {
        using PdfDocument doc = TestPdf.Open(Document(printState: "ON", annotation: false));

        Red(doc, AnnotationMode.View).Should().BeFalse("the layer is off in the default configuration");
        Red(doc, AnnotationMode.Print).Should().BeTrue("its /Usage /Print /PrintState is ON");
        new TextExtractor(doc.Objects) { AnnotationMode = AnnotationMode.Print }.ExtractText(doc.Pages[0]).Should().Contain("Printed");
        new TextExtractor(doc.Objects).ExtractText(doc.Pages[0]).Should().NotContain("Printed");
    }

    [Fact]
    public void ScreenOnlyLayer_IsHiddenForPrint()
    {
        using PdfDocument doc = TestPdf.Open(Document(printState: "OFF", annotation: false, baseOn: true));

        Red(doc, AnnotationMode.View).Should().BeTrue();
        Red(doc, AnnotationMode.Print).Should().BeFalse();
    }

    [Fact]
    public void FlattenedAnnotation_StaysInItsLayer()
    {
        using PdfDocument doc = TestPdf.Open(Document(printState: "ON", annotation: true));
        Red(doc, AnnotationMode.View).Should().BeFalse("the annotation's layer is off");

        using MemoryStream output = new MemoryStream();
        AnnotationFlattener.Flatten(output, doc);
        output.Position = 0;
        using PdfDocument flat = PdfDocument.Open(output, leaveOpen: true);

        Red(flat, AnnotationMode.View).Should().BeFalse("before 3.34.0 the baked annotation showed whatever the layer");
        Red(flat, AnnotationMode.Print).Should().BeTrue();
    }

    private static bool Red(PdfDocument doc, AnnotationMode mode)
    {
        PixelBuffer px = new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72, AnnotationMode = mode }).Rasterize(doc.Pages[0]);
        (byte b, byte g, byte r, byte _) = px.GetPixelBgra(100, 100);
        return r > 200 && g < 60 && b < 60;
    }

    // One layer, off on screen (unless baseOn) and with the given print
    // state; a red square in it, drawn by the page content or by an
    // annotation whose /OC is the layer.
    private static byte[] Document(string printState, bool annotation, bool baseOn = false)
    {
        TestPdf pdf = new TestPdf();
        int catalog = pdf.Reserve();
        int pages = pdf.Reserve();
        int ocg = pdf.Add("<</Type/OCG/Name(Stamp)/Usage<</Print<</PrintState/" + printState + ">>>>>>");
        string square = "1 0 0 rg 50 50 100 100 re f BT /F1 12 Tf 60 60 Td (Printed) Tj ET";
        string font = "/Font<</F1<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>>>";
        int contents;
        string annots = string.Empty;
        if (annotation)
        {
            contents = pdf.AddStream("<<>>", string.Empty);
            int ap = pdf.AddStream("<</Type/XObject/Subtype/Form/BBox[0 0 200 200]/Resources<<" + font + ">>>>", square);
            int annot = pdf.Add("<</Type/Annot/Subtype/Stamp/F 4/Rect[0 0 200 200]/OC " + N(ocg) + " 0 R/AP<</N " + N(ap) + " 0 R>>>>");
            annots = "/Annots[" + N(annot) + " 0 R]";
        }
        else
        {
            contents = pdf.AddStream("<<>>", "/OC /L BDC " + square + " EMC");
        }

        int page = pdf.Add("<</Type/Page/Parent " + N(pages) + " 0 R/MediaBox[0 0 200 200]/Resources<<" + font + "/Properties<</L " + N(ocg) + " 0 R>>>>/Contents "
            + N(contents) + " 0 R" + annots + ">>");
        pdf.Set(pages, "<</Type/Pages/Kids[" + N(page) + " 0 R]/Count 1>>");
        pdf.Set(catalog, "<</Type/Catalog/Pages " + N(pages) + " 0 R/OCProperties<</OCGs[" + N(ocg) + " 0 R]/D<<"
            + (baseOn ? string.Empty : "/OFF[" + N(ocg) + " 0 R]") + "/AS[<</Event/Print/Category[/Print]/OCGs[" + N(ocg) + " 0 R]>>]>>>>>>");
        return pdf.Build(catalog);
    }

    private static string N(int n) => n.ToString(CultureInfo.InvariantCulture);
}
