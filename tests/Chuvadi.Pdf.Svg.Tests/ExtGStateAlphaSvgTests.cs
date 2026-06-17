// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.4.5 — ExtGState (/ca, /CA constant alpha)
//
// Regression coverage: content drawn under an ExtGState constant alpha
// (/ca fill, /CA stroke) must render translucent in SVG via fill-opacity /
// stroke-opacity. Previously the gs operator was a no-op, so a 50%-opacity
// watermark rendered fully opaque.

using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Svg.Tests;

public sealed class ExtGStateAlphaSvgTests
{
    // Builds a one-page PDF whose content fills and strokes a rectangle. When
    // withAlpha is true the painting happens under an ExtGState with /ca and
    // /CA of 0.5.
    private static MemoryStream BuildPdf(bool withAlpha)
    {
        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Intern("Catalog"));
        catalog.Set(PdfName.Intern("Pages"), new PdfReference(2, 0));

        PdfDictionary pages = new PdfDictionary();
        pages.Set(PdfName.Type, PdfName.Intern("Pages"));
        pages.Set(PdfName.Intern("Kids"), new PdfArray(new PdfPrimitive[] { new PdfReference(3, 0) }));
        pages.Set(PdfName.Intern("Count"), 1);

        PdfDictionary page = new PdfDictionary();
        page.Set(PdfName.Type, PdfName.Intern("Page"));
        page.Set(PdfName.Intern("Parent"), new PdfReference(2, 0));
        page.Set(PdfName.Intern("MediaBox"), new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(200), new PdfInteger(200),
        }));
        page.Set(PdfName.Intern("Contents"), new PdfReference(4, 0));

        PdfDictionary resources = new PdfDictionary();
        if (withAlpha)
        {
            PdfDictionary gs = new PdfDictionary();
            gs.Set(PdfName.Type, PdfName.Intern("ExtGState"));
            gs.Set(PdfName.Intern("ca"), new PdfReal(0.5));
            gs.Set(PdfName.Intern("CA"), new PdfReal(0.5));
            PdfDictionary extGState = new PdfDictionary();
            extGState.Set(PdfName.Intern("GS"), gs);
            resources.Set(PdfName.Intern("ExtGState"), extGState);
        }

        page.Set(PdfName.Intern("Resources"), resources);

        string drawing = withAlpha
            ? "q /GS gs 1 0 0 rg 0 0 1 RG 4 w 40 40 120 120 re B Q"
            : "q 1 0 0 rg 0 0 1 RG 4 w 40 40 120 120 re B Q";
        byte[] content = Encoding.ASCII.GetBytes(drawing);
        PdfDictionary contentDict = new PdfDictionary();
        contentDict.Set(PdfName.Length, content.Length);

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(new PdfObjectId(1, 0), catalog),
            new PdfIndirectObject(new PdfObjectId(2, 0), pages),
            new PdfIndirectObject(new PdfObjectId(3, 0), page),
            new PdfIndirectObject(new PdfObjectId(4, 0), new PdfStream(contentDict, content)),
        };

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(1, 0));

        MemoryStream output = new MemoryStream();
        PdfWriter.Write(output, objects, trailer);
        output.Position = 0;
        return output;
    }

    [Fact]
    public void Fill_UnderConstantAlpha_EmitsFillOpacity()
    {
        using MemoryStream pdf = BuildPdf(withAlpha: true);
        using PdfDocument doc = PdfDocument.Open(pdf, leaveOpen: true);

        string svg = new SvgRenderer().RenderPage(doc, 0);

        svg.Should().Contain("fill-opacity=\"0.5\"");
        svg.Should().Contain("stroke-opacity=\"0.5\"");
    }

    [Fact]
    public void Fill_WithoutAlpha_EmitsNoOpacity()
    {
        using MemoryStream pdf = BuildPdf(withAlpha: false);
        using PdfDocument doc = PdfDocument.Open(pdf, leaveOpen: true);

        string svg = new SvgRenderer().RenderPage(doc, 0);

        svg.Should().NotContain("fill-opacity");
        svg.Should().NotContain("stroke-opacity");
    }
}
