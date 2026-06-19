// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.7 (interactive forms), §12.7.3.3 (field values)
// PHASE: Redaction R1 — form-field value redaction
//
// A form field whose widget /Rect falls inside a redaction region must have its
// value physically removed: the field's /V string (here an INDIRECT object) is
// gone from the output, while the field object itself is kept but emptied so the
// AcroForm tree stays consistent. The control proves an out-of-region field is
// left untouched.

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

public sealed class RedactionFormFieldTests
{
    private const string SecretValue = "SECRETFIELDVAL98765";
    private const string FieldName = "patientfield";

    [Fact]
    public void Apply_FieldWidgetInRegion_ValueIsPhysicallyRemoved()
    {
        using MemoryStream source = BuildFormPdf(690, 720, SecretValue);
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

        string bytes = Encoding.Latin1.GetString(output.ToArray()).ToUpperInvariant();
        bytes.Should().NotContain(ToHex(SecretValue),
            "the field value must be physically removed, including its indirect /V string");
        bytes.Should().Contain(ToHex(FieldName),
            "the field object is kept (still referenced by AcroForm) but emptied, not deleted");
    }

    [Fact]
    public void Apply_FieldWidgetOutsideRegion_ValueIsPreserved()
    {
        using MemoryStream source = BuildFormPdf(100, 130, SecretValue);
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

        string bytes = Encoding.Latin1.GetString(output.ToArray()).ToUpperInvariant();
        bytes.Should().Contain(ToHex(SecretValue),
            "a form field outside every redaction region must keep its value");
    }

    private static string ToHex(string value)
    {
        StringBuilder sb = new StringBuilder();
        foreach (byte b in Encoding.ASCII.GetBytes(value))
        {
            sb.Append(b.ToString("X2"));
        }

        return sb.ToString();
    }

    private static MemoryStream BuildFormPdf(double rectLly, double rectUry, string value)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);
        PdfObjectId contentId = new PdfObjectId(4, 0);
        PdfObjectId fontId = new PdfObjectId(5, 0);
        PdfObjectId fieldId = new PdfObjectId(6, 0);
        PdfObjectId valueId = new PdfObjectId(7, 0);
        PdfObjectId acroFormId = new PdfObjectId(8, 0);

        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(pagesId));
        catalog.Set(PdfName.Intern("AcroForm"), new PdfReference(acroFormId));

        PdfDictionary acroForm = new PdfDictionary();
        acroForm.Set(PdfName.Intern("Fields"),
            new PdfArray(new PdfPrimitive[] { new PdfReference(fieldId) }));

        PdfDictionary pages = new PdfDictionary();
        pages.Set(PdfName.Type, PdfName.Pages);
        pages.Set(PdfName.Kids, new PdfArray(new PdfPrimitive[] { new PdfReference(pageId) }));
        pages.Set(PdfName.Count, 1);
        pages.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(612), new PdfInteger(792),
        }));

        PdfDictionary font = new PdfDictionary();
        font.Set(PdfName.Type, PdfName.Intern("Font"));
        font.Set(PdfName.Intern("Subtype"), PdfName.Intern("Type1"));
        font.Set(PdfName.Intern("BaseFont"), PdfName.Intern("Helvetica"));

        PdfDictionary fontResources = new PdfDictionary();
        fontResources.Set(PdfName.Intern("F1"), new PdfReference(fontId));
        PdfDictionary resources = new PdfDictionary();
        resources.Set(PdfName.Intern("Font"), fontResources);

        PdfDictionary page = new PdfDictionary();
        page.Set(PdfName.Type, PdfName.Page);
        page.Set(PdfName.Parent, new PdfReference(pagesId));
        page.Set(PdfName.Contents, new PdfReference(contentId));
        page.Set(PdfName.Intern("Resources"), resources);
        page.Set(PdfName.Intern("Annots"),
            new PdfArray(new PdfPrimitive[] { new PdfReference(fieldId) }));

        byte[] content = Encoding.ASCII.GetBytes("BT /F1 12 Tf 100 400 Td (BODYTEXT) Tj ET");
        PdfDictionary contentDict = new PdfDictionary();
        contentDict.Set(PdfName.Length, content.Length);

        // Merged widget + terminal text field. /V is a SEPARATE indirect string
        // object: stripping /V must physically remove that object too.
        PdfDictionary field = new PdfDictionary();
        field.Set(PdfName.Type, PdfName.Intern("Annot"));
        field.Set(PdfName.Intern("Subtype"), PdfName.Intern("Widget"));
        field.Set(PdfName.Intern("FT"), PdfName.Intern("Tx"));
        field.Set(PdfName.Intern("T"), new PdfString(FieldName));
        field.Set(PdfName.Intern("V"), new PdfReference(valueId));
        field.Set(PdfName.Intern("Rect"), new PdfArray(new PdfPrimitive[]
        {
            new PdfReal(90), new PdfReal(rectLly), new PdfReal(310), new PdfReal(rectUry),
        }));

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(catalogId, catalog),
            new PdfIndirectObject(pagesId, pages),
            new PdfIndirectObject(pageId, page),
            new PdfIndirectObject(contentId, new PdfStream(contentDict, content)),
            new PdfIndirectObject(fontId, font),
            new PdfIndirectObject(fieldId, field),
            new PdfIndirectObject(valueId, new PdfString(value)),
            new PdfIndirectObject(acroFormId, acroForm),
        };

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        ms.Position = 0;
        return ms;
    }
}
