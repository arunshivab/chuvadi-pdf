// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.32.0 (D-259) — a form XObject inherits the graphics state in effect
//        at the Do (PDF 32000-1:2008 §8.10.1): colours, line style, text
//        state, alpha and blend mode. Before 3.32.0 only the alpha carried
//        over and forms painted black (LibreOffice watermarks).

using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.Tests;

public sealed class FormInheritance3320Tests
{
    private const string Square = "0 0 100 100 re f";

    [Fact]
    public void LibreOfficeWatermark_GreyAtHalfAlpha_Is223_NotBlack()
    {
        // 0.75 grey at 50% over white: 0.5 x 0.75 + 0.5 x 1 = 0.875, about 223.
        PixelBuffer page = Render("0.75 g /GS0 gs /Fm0 Do", Square);

        (byte b, byte g, byte r, _) = page.GetPixelBgra(50, 150);
        r.Should().BeInRange(220, 226, "the form inherits the 0.75 grey fill");
        g.Should().Be(r);
        b.Should().Be(r);
    }

    [Fact]
    public void FillColour_IsInherited_InRgbAndCmyk()
    {
        IsColour(Render("1 0 0 rg /Fm0 Do", Square).GetPixelBgra(50, 150), 255, 0, 0).Should().BeTrue();
        (byte b, byte g, byte r, _) = Render("0 1 1 0 k /Fm0 Do", Square).GetPixelBgra(50, 150);
        r.Should().BeGreaterThan(180, "CMYK 0 1 1 0 is red");
        g.Should().BeLessThan(80);
        b.Should().BeLessThan(80);
    }

    [Fact]
    public void StrokeColour_AndLineWidth_AreInherited()
    {
        // A 10 pt blue stroke along y = 50 inside the form.
        PixelBuffer page = Render("0 0 1 RG 10 w /Fm0 Do", "0 50 m 100 50 l S");

        IsColour(page.GetPixelBgra(50, 154), 0, 0, 255).Should().BeTrue("4 pt from the line centre, inside a 10 pt line");
    }

    [Fact]
    public void FormThatSetsItsOwnColour_IsUnchanged()
    {
        IsColour(Render("1 0 0 rg /Fm0 Do", "0 1 0 rg " + Square).GetPixelBgra(50, 150), 0, 255, 0).Should().BeTrue();
    }

    [Fact]
    public void NestedForm_InheritsThroughBothLevels()
    {
        PixelBuffer page = Render("0 0 1 rg /Fm0 Do", "/Fm1 Do", inner: Square);

        IsColour(page.GetPixelBgra(50, 150), 0, 0, 255).Should().BeTrue();
    }

    [Fact]
    public void ColourSetInsideTheForm_DoesNotLeakOut()
    {
        // After the Do the page paints with its own red again.
        PixelBuffer page = Render("1 0 0 rg /Fm0 Do 100 0 100 100 re f", "0 1 0 rg " + Square);

        IsColour(page.GetPixelBgra(150, 150), 255, 0, 0).Should().BeTrue();
    }

    [Fact]
    public void FormMatrix_AndClip_StillApply()
    {
        // Clip to x < 50, then paint the form moved right by 20 with its /Matrix.
        PixelBuffer page = Render("0 0 50 200 re W n 1 0 0 rg /Fm0 Do", Square, matrix: "1 0 0 1 20 0");

        IsColour(page.GetPixelBgra(30, 150), 255, 0, 0).Should().BeTrue("inside the clip and the moved square");
        IsColour(page.GetPixelBgra(10, 150), 255, 255, 255).Should().BeTrue("left of the moved square");
        IsColour(page.GetPixelBgra(60, 150), 255, 255, 255).Should().BeTrue("outside the clip");
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private static bool IsColour((byte B, byte G, byte R, byte A) p, int r, int g, int b) =>
        System.Math.Abs(p.R - r) < 30 && System.Math.Abs(p.G - g) < 30 && System.Math.Abs(p.B - b) < 30;

    // A 200 x 200 page: content `page`, a form /Fm0 with content `form`, and
    // (optionally) /Fm1 inside /Fm0 with content `inner`. /GS0 sets ca 0.5.
    private static PixelBuffer Render(string page, string form, string? inner = null, string? matrix = null)
    {
        List<PdfIndirectObject> objects = new List<PdfIndirectObject>();

        PdfDictionary formResources = new PdfDictionary();
        if (inner is not null)
        {
            objects.Add(new PdfIndirectObject(new PdfObjectId(6, 0), Form(inner, null, new PdfDictionary())));
            PdfDictionary innerX = new PdfDictionary();
            innerX.Set(PdfName.Intern("Fm1"), new PdfReference(6, 0));
            formResources.Set(PdfName.XObject, innerX);
        }

        objects.Add(new PdfIndirectObject(new PdfObjectId(5, 0), Form(form, matrix, formResources)));

        PdfDictionary gs = new PdfDictionary();
        gs.Set(PdfName.Intern("ca"), new PdfReal(0.5));
        PdfDictionary extG = new PdfDictionary();
        extG.Set(PdfName.Intern("GS0"), gs);
        PdfDictionary xobjects = new PdfDictionary();
        xobjects.Set(PdfName.Intern("Fm0"), new PdfReference(5, 0));
        PdfDictionary resources = new PdfDictionary();
        resources.Set(PdfName.XObject, xobjects);
        resources.Set(PdfName.Intern("ExtGState"), extG);

        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(2, 0));
        PdfDictionary pages = new PdfDictionary();
        pages.Set(PdfName.Type, PdfName.Pages);
        pages.Set(PdfName.Kids, new PdfArray(new PdfPrimitive[] { new PdfReference(3, 0) }));
        pages.Set(PdfName.Count, 1);
        PdfDictionary pageDict = new PdfDictionary();
        pageDict.Set(PdfName.Type, PdfName.Page);
        pageDict.Set(PdfName.Intern("Parent"), new PdfReference(2, 0));
        pageDict.Set(PdfName.MediaBox, Numbers("0 0 200 200"));
        pageDict.Set(PdfName.Resources, resources);
        pageDict.Set(PdfName.Contents, new PdfReference(4, 0));
        byte[] content = Encoding.ASCII.GetBytes(page);
        PdfDictionary contentDict = new PdfDictionary();
        contentDict.Set(PdfName.Length, content.Length);

        objects.Add(new PdfIndirectObject(new PdfObjectId(1, 0), catalog));
        objects.Add(new PdfIndirectObject(new PdfObjectId(2, 0), pages));
        objects.Add(new PdfIndirectObject(new PdfObjectId(3, 0), pageDict));
        objects.Add(new PdfIndirectObject(new PdfObjectId(4, 0), new PdfStream(contentDict, content)));
        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(1, 0));
        using MemoryStream output = new MemoryStream();
        PdfWriter.Write(output, objects, trailer);
        output.Position = 0;

        using PdfDocument doc = PdfDocument.Open(output, leaveOpen: false);
        return new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72 }).Rasterize(doc.Pages[0]);
    }

    private static PdfStream Form(string content, string? matrix, PdfDictionary resources)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(content);
        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Type, PdfName.XObject);
        dict.Set(PdfName.Subtype, PdfName.Intern("Form"));
        dict.Set(PdfName.Intern("BBox"), Numbers("0 0 200 200"));
        if (matrix is not null)
        {
            dict.Set(PdfName.Intern("Matrix"), Numbers(matrix));
        }

        dict.Set(PdfName.Resources, resources);
        dict.Set(PdfName.Length, bytes.Length);
        return new PdfStream(dict, bytes);
    }

    private static PdfArray Numbers(string values)
    {
        PdfArray array = new PdfArray();
        foreach (string v in values.Split(' '))
        {
            array.Add(new PdfReal(double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)));
        }

        return array;
    }
}
