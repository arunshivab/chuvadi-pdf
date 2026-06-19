// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.5 (annotations), §12.6.4.7 (URI actions)
// PHASE: Redaction R1 — annotation / link-URL redaction
//
// An annotation whose /Rect falls inside a redaction region must be removed
// entirely, and any object it solely owns (here, an INDIRECT URI action
// holding the link target) must be physically removed too — not merely
// unlinked. Each test asserts the secret URL is absent from / present in the
// full output bytes, proving physical removal rather than visual overlay.

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

public sealed class RedactionAnnotationTests
{
    private const string SecretUrl = "SECRETLEAKURL12345PHI";

    // PdfWriter serialises the URI string in hex form (<...>), so the byte-level
    // checks match the secret as it is physically stored.
    private const string SecretHex = "5345435245544C45414B55524C3132333435504849";

    [Fact]
    public void Apply_LinkAnnotationInRegion_UrlIsPhysicallyRemoved()
    {
        // Annotation /Rect [90 690 310 720] sits inside the redaction region.
        using MemoryStream source = BuildPageWithLinkAnnotation(690, 720, SecretUrl);
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
        bytes.Should().NotContain(SecretHex,
            "the redacted link's URI action object must be physically removed, not just unlinked");
        bytes.Should().NotContain(SecretUrl,
            "the secret must not survive in any literal form either");
    }

    [Fact]
    public void Apply_LinkAnnotationOutsideRegion_UrlIsPreserved()
    {
        // Annotation /Rect [90 100 310 130] is far from the redaction region.
        using MemoryStream source = BuildPageWithLinkAnnotation(100, 130, SecretUrl);
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
        bytes.Should().Contain(SecretHex,
            "an annotation outside every redaction region must be left intact");
    }

    private static MemoryStream BuildPageWithLinkAnnotation(
        double rectLly, double rectUry, string url)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);
        PdfObjectId contentId = new PdfObjectId(4, 0);
        PdfObjectId fontId = new PdfObjectId(5, 0);
        PdfObjectId annotId = new PdfObjectId(6, 0);
        PdfObjectId actionId = new PdfObjectId(7, 0);

        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(pagesId));

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
            new PdfArray(new PdfPrimitive[] { new PdfReference(annotId) }));

        byte[] content = Encoding.ASCII.GetBytes("BT /F1 12 Tf 100 400 Td (BODYTEXT) Tj ET");
        PdfDictionary contentDict = new PdfDictionary();
        contentDict.Set(PdfName.Length, content.Length);

        // URI action as a separate INDIRECT object: only the annotation
        // references it, so redacting the annotation must remove it too.
        PdfDictionary action = new PdfDictionary();
        action.Set(PdfName.Intern("S"), PdfName.Intern("URI"));
        action.Set(PdfName.Intern("URI"), new PdfString(url));

        PdfDictionary annot = new PdfDictionary();
        annot.Set(PdfName.Type, PdfName.Intern("Annot"));
        annot.Set(PdfName.Intern("Subtype"), PdfName.Intern("Link"));
        annot.Set(PdfName.Intern("Rect"), new PdfArray(new PdfPrimitive[]
        {
            new PdfReal(90), new PdfReal(rectLly), new PdfReal(310), new PdfReal(rectUry),
        }));
        annot.Set(PdfName.Intern("A"), new PdfReference(actionId));

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(catalogId, catalog),
            new PdfIndirectObject(pagesId, pages),
            new PdfIndirectObject(pageId, page),
            new PdfIndirectObject(contentId, new PdfStream(contentDict, content)),
            new PdfIndirectObject(fontId, font),
            new PdfIndirectObject(annotId, annot),
            new PdfIndirectObject(actionId, action),
        };

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        ms.Position = 0;
        return ms;
    }
}
