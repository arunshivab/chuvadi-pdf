// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.10 — Form XObjects
// PHASE: Phase 2 — rendering conformance (form XObject recursion)
//
// Regression: text (or any marking operators) inside a form XObject invoked
// with `Do` must render. Previously the SVG builder only handled image
// XObjects and silently dropped forms, so stamped/annotated text painted via
// a form (e.g. PageStamper.Place) was invisible in Chuvadi while visible in
// Adobe/Chrome. This builds a page whose only visible content lives inside a
// form and asserts the text reaches the SVG.

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

public sealed class FormXObjectRenderTests
{
    [Fact]
    public void TextInsideFormXObject_IsRendered()
    {
        using MemoryStream pdf = BuildFormTextPdf();
        using PdfDocument doc = PdfDocument.Open(pdf, leaveOpen: true);

        string svg = new SvgRenderer().RenderPage(doc, 0);

        svg.Should().Contain("HELLO",
            "text painted inside a form XObject must render, not be dropped");
    }

    private static MemoryStream BuildFormTextPdf()
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);
        PdfObjectId contentId = new PdfObjectId(4, 0);
        PdfObjectId fontId = new PdfObjectId(5, 0);
        PdfObjectId formId = new PdfObjectId(6, 0);

        PdfDictionary catalogDict = new PdfDictionary();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

        PdfArray kids = new PdfArray(new PdfPrimitive[] { new PdfReference(pageId) });
        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, kids);
        pagesDict.Set(PdfName.Count, 1);
        pagesDict.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(612), new PdfInteger(792),
        }));

        PdfDictionary xobjects = new PdfDictionary();
        xobjects.Set(PdfName.Intern("Fm0"), new PdfReference(formId));
        PdfDictionary resources = new PdfDictionary();
        resources.Set(PdfName.Intern("XObject"), xobjects);

        PdfDictionary pageDict = new PdfDictionary();
        pageDict.Set(PdfName.Type, PdfName.Page);
        pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
        pageDict.Set(PdfName.Contents, new PdfReference(contentId));
        pageDict.Set(PdfName.Intern("Resources"), resources);

        byte[] contentBytes = Encoding.ASCII.GetBytes("q 1 0 0 1 0 0 cm /Fm0 Do Q");
        PdfDictionary contentDict = new PdfDictionary();
        contentDict.Set(PdfName.Length, contentBytes.Length);
        PdfStream contentStream = new PdfStream(contentDict, contentBytes);

        PdfDictionary fontDict = new PdfDictionary();
        fontDict.Set(PdfName.Type, PdfName.Intern("Font"));
        fontDict.Set(PdfName.Intern("Subtype"), PdfName.Intern("Type1"));
        fontDict.Set(PdfName.Intern("BaseFont"), PdfName.Intern("Helvetica"));
        fontDict.Set(PdfName.Intern("Encoding"), PdfName.Intern("WinAnsiEncoding"));

        byte[] formBytes = Encoding.ASCII.GetBytes(
            "q 0 0 0 rg BT /Helv 24 Tf 100 700 Td (HELLO) Tj ET Q");
        PdfDictionary formFonts = new PdfDictionary();
        formFonts.Set(PdfName.Intern("Helv"), new PdfReference(fontId));
        PdfDictionary formResources = new PdfDictionary();
        formResources.Set(PdfName.Intern("Font"), formFonts);

        PdfDictionary formDict = new PdfDictionary();
        formDict.Set(PdfName.Type, PdfName.Intern("XObject"));
        formDict.Set(PdfName.Intern("Subtype"), PdfName.Intern("Form"));
        formDict.Set(PdfName.Intern("FormType"), 1);
        formDict.Set(PdfName.Intern("BBox"), new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(612), new PdfInteger(792),
        }));
        formDict.Set(PdfName.Intern("Resources"), formResources);
        formDict.Set(PdfName.Length, formBytes.Length);
        PdfStream formStream = new PdfStream(formDict, formBytes);

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(catalogId, catalogDict),
            new PdfIndirectObject(pagesId, pagesDict),
            new PdfIndirectObject(pageId, pageDict),
            new PdfIndirectObject(contentId, contentStream),
            new PdfIndirectObject(fontId, fontDict),
            new PdfIndirectObject(formId, formStream),
        };

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream output = new MemoryStream();
        PdfWriter.Write(output, objects, trailer);
        output.Position = 0;
        return output;
    }
}
