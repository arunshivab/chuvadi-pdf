// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.32.0 (D-221 P2, D-230 Q8) — SVG shows the CropBox by default

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

public sealed class SvgDisplayBox3320Tests
{
    [Fact]
    public void Default_SizesAndTranslatesByTheCropBox()
    {
        string svg = Render(new[] { 100, 100, 300, 250 }, new SvgExportOptions());

        svg.Should().Contain("width=\"200pt\" height=\"150pt\" viewBox=\"0 0 200 150\"");
        svg.Should().Contain("<g transform=\"matrix(1 0 0 -1 -100 250)\">");
    }

    [Fact]
    public void MediaBoxOption_ShowsTheWholeMediaBox()
    {
        string svg = Render(new[] { 100, 100, 300, 250 }, new SvgExportOptions { DisplayBox = PdfDisplayBox.MediaBox });

        svg.Should().Contain("width=\"400pt\" height=\"400pt\" viewBox=\"0 0 400 400\"");
        svg.Should().Contain("<g transform=\"matrix(1 0 0 -1 0 400)\">");
    }

    [Fact]
    public void NoCropBox_IsUnchanged()
    {
        string svg = Render(null, new SvgExportOptions());

        svg.Should().Contain("width=\"400pt\" height=\"400pt\" viewBox=\"0 0 400 400\"");
        svg.Should().Contain("<g transform=\"matrix(1 0 0 -1 0 400)\">");
    }

    [Fact]
    public void PageShading_FillsTheCropBox_FromItsCorner()
    {
        string svg = Render(new[] { 100, 100, 300, 250 }, new SvgExportOptions(), "/Sh0 sh");

        svg.Should().Contain("<path d=\"M 100 100 L 300 100 L 300 250 L 100 250 Z\" fill=\"url(#sh");
    }

    [Fact]
    public void PageShading_WithoutACropBox_IsUnchanged()
    {
        string svg = Render(null, new SvgExportOptions(), "/Sh0 sh");

        svg.Should().Contain("<path d=\"M 0 0 L 400 0 L 400 400 L 0 400 Z\" fill=\"url(#sh");
    }

    private static string Render(int[]? cropBox, SvgExportOptions options, string drawing = "1 0 0 rg 150 150 100 100 re f")
    {
        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(2, 0));

        PdfDictionary pages = new PdfDictionary();
        pages.Set(PdfName.Type, PdfName.Pages);
        pages.Set(PdfName.Kids, new PdfArray(new PdfPrimitive[] { new PdfReference(3, 0) }));
        pages.Set(PdfName.Count, 1);

        PdfDictionary page = new PdfDictionary();
        page.Set(PdfName.Type, PdfName.Page);
        page.Set(PdfName.Intern("Parent"), new PdfReference(2, 0));
        page.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[] { new PdfInteger(0), new PdfInteger(0), new PdfInteger(400), new PdfInteger(400) }));
        if (cropBox is not null)
        {
            page.Set(PdfName.CropBox, new PdfArray(new PdfPrimitive[] { new PdfInteger(cropBox[0]), new PdfInteger(cropBox[1]), new PdfInteger(cropBox[2]), new PdfInteger(cropBox[3]) }));
        }

        page.Set(PdfName.Resources, Resources());
        page.Set(PdfName.Contents, new PdfReference(4, 0));
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

        using PdfDocument doc = PdfDocument.Open(output, leaveOpen: false);
        return new SvgRenderer(options).RenderPage(doc, 0);
    }

    // An axial red-to-blue shading named /Sh0.
    private static PdfDictionary Resources()
    {
        PdfDictionary function = new PdfDictionary();
        function.Set(PdfName.Intern("FunctionType"), 2);
        function.Set(PdfName.Intern("Domain"), new PdfArray(new PdfPrimitive[] { new PdfInteger(0), new PdfInteger(1) }));
        function.Set(PdfName.Intern("C0"), new PdfArray(new PdfPrimitive[] { new PdfInteger(1), new PdfInteger(0), new PdfInteger(0) }));
        function.Set(PdfName.Intern("C1"), new PdfArray(new PdfPrimitive[] { new PdfInteger(0), new PdfInteger(0), new PdfInteger(1) }));
        function.Set(PdfName.Intern("N"), 1);

        PdfDictionary shading = new PdfDictionary();
        shading.Set(PdfName.Intern("ShadingType"), 2);
        shading.Set(PdfName.Intern("ColorSpace"), PdfName.Intern("DeviceRGB"));
        shading.Set(PdfName.Intern("Coords"), new PdfArray(new PdfPrimitive[] { new PdfInteger(0), new PdfInteger(0), new PdfInteger(400), new PdfInteger(0) }));
        shading.Set(PdfName.Intern("Function"), function);
        shading.Set(PdfName.Intern("Extend"), new PdfArray(new PdfPrimitive[] { PdfBoolean.True, PdfBoolean.True }));

        PdfDictionary shadings = new PdfDictionary();
        shadings.Set(PdfName.Intern("Sh0"), shading);
        PdfDictionary resources = new PdfDictionary();
        resources.Set(PdfName.Intern("Shading"), shadings);
        return resources;
    }
}
