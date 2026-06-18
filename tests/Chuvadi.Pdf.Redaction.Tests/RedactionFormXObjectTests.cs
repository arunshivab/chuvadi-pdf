// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.10 (form XObjects), §7.8 (content streams)
// PHASE: Redaction — form XObject recursion (data-leak fix)
//
// Regression coverage for the redaction data leak: text drawn inside a form
// XObject must be physically removed from the form's own content stream, not
// merely covered by the overlay. Each test asserts the secret string is absent
// from the ENTIRE output (the form streams are emitted uncompressed), proving
// the bytes are gone rather than just undrawn.

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

namespace Chuvadi.Pdf.Redaction.Tests;

public sealed class RedactionFormXObjectTests
{
    [Fact]
    public void Apply_TextInsideFormXObject_IsPhysicallyRemoved()
    {
        // Secret text lives inside a form XObject at device (100,700).
        using MemoryStream source = BuildPageWithFormText(
            "BT /F1 12 Tf 100 700 Td (SECRET_FORM_TEXT) Tj ET");
        using PdfDocument doc = PdfDocument.Open(source, leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        RedactionOptions opts = new RedactionOptions
        {
            Rectangles = new List<RedactionRect>
            {
                new RedactionRect(0, new RectangleF(90, 690, 220, 30)),
            },
        };
        Redactor.Apply(output, doc, opts);

        string outputText = Encoding.Latin1.GetString(output.ToArray());
        outputText.Should().NotContain("SECRET_FORM_TEXT",
            "text inside a form XObject must be removed from the form content stream, not merely overlaid");
    }

    [Fact]
    public void Apply_FormTextOutsideRect_IsPreserved()
    {
        // Two strings in the form: one under the rect, one far away.
        using MemoryStream source = BuildPageWithFormText(
            "BT /F1 12 Tf 100 700 Td (SECRET_FORM_TEXT) Tj ET\n" +
            "BT /F1 12 Tf 100 100 Td (KEEP_FORM_TEXT) Tj ET");
        using PdfDocument doc = PdfDocument.Open(source, leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        RedactionOptions opts = new RedactionOptions
        {
            Rectangles = new List<RedactionRect>
            {
                new RedactionRect(0, new RectangleF(90, 690, 220, 30)),
            },
        };
        Redactor.Apply(output, doc, opts);

        string outputText = Encoding.Latin1.GetString(output.ToArray());
        outputText.Should().NotContain("SECRET_FORM_TEXT",
            "the in-rect form string must be removed");
        outputText.Should().Contain("KEEP_FORM_TEXT",
            "form text outside the rectangle must be preserved");
    }

    [Fact]
    public void Apply_TextInsideScaledForm_IsRemovedAtPlacedPosition()
    {
        // The form is invoked under a translating CTM, so the form-local origin
        // (0,640) lands at device (100,700) under the redaction rect.
        using MemoryStream source = BuildPageWithFormText(
            "BT /F1 12 Tf 0 640 Td (SCALED_SECRET) Tj ET",
            placementCm: "1 0 0 1 100 60");
        using PdfDocument doc = PdfDocument.Open(source, leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        RedactionOptions opts = new RedactionOptions
        {
            Rectangles = new List<RedactionRect>
            {
                new RedactionRect(0, new RectangleF(90, 690, 220, 30)),
            },
        };
        Redactor.Apply(output, doc, opts);

        string outputText = Encoding.Latin1.GetString(output.ToArray());
        outputText.Should().NotContain("SCALED_SECRET",
            "form text must be tested at its placed device position, not its form-local position");
    }

    // Builds a single-page PDF whose page content invokes a form XObject (object
    // 5) via Do. The form carries the supplied content. Placement is controlled
    // by placementCm (the page-level cm before the Do).
    private static MemoryStream BuildPageWithFormText(string formContent, string placementCm = "1 0 0 1 0 0")
    {
        PdfObjectId catalogId = new(1, 0);
        PdfObjectId pagesId = new(2, 0);
        PdfObjectId pageId = new(3, 0);
        PdfObjectId contentsId = new(4, 0);
        PdfObjectId formId = new(5, 0);

        byte[] pageBytes = Encoding.Latin1.GetBytes($"q {placementCm} cm /Fm0 Do Q");
        PdfDictionary pageContentDict = new();
        pageContentDict.Set(PdfName.Intern("Length"), pageBytes.Length);
        PdfStream pageContents = new(pageContentDict, pageBytes);

        byte[] formBytes = Encoding.Latin1.GetBytes(formContent);
        PdfDictionary formFonts = new();
        PdfDictionary formResources = new();
        formResources.Set(PdfName.Intern("Font"), formFonts);

        PdfDictionary formDict = new();
        formDict.Set(PdfName.Type, PdfName.Intern("XObject"));
        formDict.Set(PdfName.Intern("Subtype"), PdfName.Intern("Form"));
        formDict.Set(PdfName.Intern("BBox"), new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(612), new PdfInteger(792),
        }));
        formDict.Set(PdfName.Intern("Resources"), formResources);
        formDict.Set(PdfName.Intern("Length"), formBytes.Length);
        PdfStream form = new(formDict, formBytes);

        PdfDictionary xobjects = new();
        xobjects.Set(PdfName.Intern("Fm0"), new PdfReference(formId));
        PdfDictionary resources = new();
        resources.Set(PdfName.Intern("XObject"), xobjects);

        PdfDictionary pageDict = new();
        pageDict.Set(PdfName.Type, PdfName.Page);
        pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
        pageDict.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(612), new PdfInteger(792),
        }));
        pageDict.Set(PdfName.Intern("Resources"), resources);
        pageDict.Set(PdfName.Intern("Contents"), new PdfReference(contentsId));

        PdfDictionary pagesDict = new();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, new PdfArray(new PdfPrimitive[] { new PdfReference(pageId) }));
        pagesDict.Set(PdfName.Count, 1);

        PdfDictionary catalogDict = new();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(catalogId, catalogDict),
            new PdfIndirectObject(pagesId, pagesDict),
            new PdfIndirectObject(pageId, pageDict),
            new PdfIndirectObject(contentsId, pageContents),
            new PdfIndirectObject(formId, form),
        };

        PdfDictionary trailer = new();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        ms.Seek(0, SeekOrigin.Begin);
        return ms;
    }
}
