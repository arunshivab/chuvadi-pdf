// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.4.5 — ExtGState (/ca constant alpha)
//
// Regression coverage: a fill drawn under an ExtGState /ca must composite
// translucently in the rasteriser. Previously the gs operator was ignored,
// so a 50%-opacity fill rendered fully opaque.

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

public sealed class ExtGStateAlphaRasterTests
{
    // One-page (100x100) PDF whose content fills the whole page black. When
    // withAlpha is true the fill happens under an ExtGState with /ca 0.5.
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
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(100), new PdfInteger(100),
        }));
        page.Set(PdfName.Intern("Contents"), new PdfReference(4, 0));

        PdfDictionary resources = new PdfDictionary();
        if (withAlpha)
        {
            PdfDictionary gs = new PdfDictionary();
            gs.Set(PdfName.Type, PdfName.Intern("ExtGState"));
            gs.Set(PdfName.Intern("ca"), new PdfReal(0.5));
            PdfDictionary extGState = new PdfDictionary();
            extGState.Set(PdfName.Intern("GS"), gs);
            resources.Set(PdfName.Intern("ExtGState"), extGState);
        }

        page.Set(PdfName.Intern("Resources"), resources);

        string drawing = withAlpha
            ? "q /GS gs 0 0 0 rg 0 0 100 100 re f Q"
            : "q 0 0 0 rg 0 0 100 100 re f Q";
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

    private static int CentreRed(bool withAlpha)
    {
        using MemoryStream pdf = BuildPdf(withAlpha);
        using PdfDocument doc = PdfDocument.Open(pdf, leaveOpen: true);
        PageRasterizer rasterizer = new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72 });
        PixelBuffer buffer = rasterizer.Rasterize(doc.Pages[0]);
        (byte _, byte _, byte r, byte _) = buffer.GetPixelBgra(buffer.Width / 2, buffer.Height / 2);
        return r;
    }

    [Fact]
    public void Fill_UnderConstantAlpha_CompositesTranslucently()
    {
        int opaque = CentreRed(withAlpha: false);
        int translucent = CentreRed(withAlpha: true);

        // A fully opaque black fill leaves the centre near black.
        opaque.Should().BeLessThan(20);

        // /ca 0.5 must lighten the result substantially (black composited over
        // the white page sheet); the exact value depends on gamma-correct
        // blending, so assert a clear, generous separation rather than a point.
        translucent.Should().BeGreaterThan(opaque + 80);
    }

    // One-page (100x100) PDF whose black page-filling content lives inside a
    // form XObject. When alphaOutsideForm is true the form is invoked under an
    // ExtGState /ca 0.5 set on the page (outside the form), mirroring how
    // PageOverlay fades existing content by wrapping it in a form and painting
    // it under a constant alpha.
    private static MemoryStream BuildFormPdf(bool alphaOutsideForm)
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
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(100), new PdfInteger(100),
        }));
        page.Set(PdfName.Intern("Contents"), new PdfReference(4, 0));

        PdfDictionary resources = new PdfDictionary();

        PdfDictionary xobjects = new PdfDictionary();
        xobjects.Set(PdfName.Intern("Fm"), new PdfReference(5, 0));
        resources.Set(PdfName.Intern("XObject"), xobjects);

        if (alphaOutsideForm)
        {
            PdfDictionary gs = new PdfDictionary();
            gs.Set(PdfName.Type, PdfName.Intern("ExtGState"));
            gs.Set(PdfName.Intern("ca"), new PdfReal(0.5));
            PdfDictionary extGState = new PdfDictionary();
            extGState.Set(PdfName.Intern("GS"), gs);
            resources.Set(PdfName.Intern("ExtGState"), extGState);
        }

        page.Set(PdfName.Intern("Resources"), resources);

        string pageDrawing = alphaOutsideForm ? "q /GS gs /Fm Do Q" : "q /Fm Do Q";
        byte[] pageContent = Encoding.ASCII.GetBytes(pageDrawing);
        PdfDictionary pageContentDict = new PdfDictionary();
        pageContentDict.Set(PdfName.Length, pageContent.Length);

        // The form paints the whole page black, with no alpha of its own — the
        // only opacity comes from the enclosing graphics state.
        byte[] formContent = Encoding.ASCII.GetBytes("0 0 0 rg 0 0 100 100 re f");
        PdfDictionary formDict = new PdfDictionary();
        formDict.Set(PdfName.Type, PdfName.Intern("XObject"));
        formDict.Set(PdfName.Intern("Subtype"), PdfName.Intern("Form"));
        formDict.Set(PdfName.Intern("FormType"), 1);
        formDict.Set(PdfName.Intern("BBox"), new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(100), new PdfInteger(100),
        }));
        formDict.Set(PdfName.Length, formContent.Length);

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(new PdfObjectId(1, 0), catalog),
            new PdfIndirectObject(new PdfObjectId(2, 0), pages),
            new PdfIndirectObject(new PdfObjectId(3, 0), page),
            new PdfIndirectObject(new PdfObjectId(4, 0), new PdfStream(pageContentDict, pageContent)),
            new PdfIndirectObject(new PdfObjectId(5, 0), new PdfStream(formDict, formContent)),
        };

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(1, 0));

        MemoryStream output = new MemoryStream();
        PdfWriter.Write(output, objects, trailer);
        output.Position = 0;
        return output;
    }

    private static int CentreRedForm(bool alphaOutsideForm)
    {
        using MemoryStream pdf = BuildFormPdf(alphaOutsideForm);
        using PdfDocument doc = PdfDocument.Open(pdf, leaveOpen: true);
        PageRasterizer rasterizer = new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72 });
        PixelBuffer buffer = rasterizer.Rasterize(doc.Pages[0]);
        (byte _, byte _, byte r, byte _) = buffer.GetPixelBgra(buffer.Width / 2, buffer.Height / 2);
        return r;
    }

    [Fact]
    public void FormContent_InheritsOuterConstantAlpha_CompositesTranslucently()
    {
        int opaque = CentreRedForm(alphaOutsideForm: false);
        int translucent = CentreRedForm(alphaOutsideForm: true);

        // The form's black fill is opaque on its own.
        opaque.Should().BeLessThan(20);

        // A constant alpha set on the page (outside the form) must carry into the
        // form's content during rasterization — otherwise faded form-wrapped
        // content (e.g. PageOverlay output) exports fully opaque.
        translucent.Should().BeGreaterThan(opaque + 80);
    }
}
