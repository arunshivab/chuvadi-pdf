// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §14.7 (logical structure), §14.9.4 (replacement text),
//        §14.6 (marked content)
// PHASE: LA-04 — strip text from tagged PDFs
//
// Redacting marked content must also remove the text that mirrors it in the
// logical structure tree (/ActualText, /Alt) and in inline marked-content
// property lists, otherwise the redacted words are still recoverable by an
// accessibility-aware reader. Only the structure elements whose marked content
// was actually redacted are neutralised; untouched elements keep their text.

using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Text;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Redaction.Tests;

public sealed class RedactionTaggedStructTreeTests
{
    [Fact]
    public void Apply_StripsActualTextAndAlt_WhenMarkedContentRedacted()
    {
        using MemoryStream source = BuildTaggedSingleSpan(
            "Secret amount here", actualText: "Secret amount here", alt: "secret");
        using PdfDocument doc = PdfDocument.Open(source, leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        RedactionOptions opts = new RedactionOptions
        {
            Rectangles = new List<RedactionRect>
            {
                new RedactionRect(0, new RectangleF(30, 696, 220, 20)),
            },
        };
        Redactor.Apply(output, doc, opts);

        using PdfDocument result = PdfDocument.Open(output, leaveOpen: true);

        string after = new TextExtractor(result.Objects).ExtractText(result.Pages[0]);
        after.Should().NotContain("Secret", "the visible glyphs must be removed");
        after.Should().NotContain("amount");
        after.Should().NotContain("here");

        PdfDictionary element = FindStructElement(result, mcid: 0);
        element.ContainsKey(PdfName.Intern("ActualText")).Should().BeFalse(
            "the structure element's replacement text must not survive redaction");
        element.ContainsKey(PdfName.Intern("Alt")).Should().BeFalse(
            "the structure element's alternate text must not survive redaction");
    }

    [Fact]
    public void Apply_PreservesActualText_OnElementsWhoseContentSurvives()
    {
        // Two tagged spans; only the first line is redacted. The second element's
        // replacement text must be left intact (precision, not page-wide blanking).
        using MemoryStream source = BuildTaggedTwoSpans();
        using PdfDocument doc = PdfDocument.Open(source, leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        RedactionOptions opts = new RedactionOptions
        {
            Rectangles = new List<RedactionRect>
            {
                new RedactionRect(0, new RectangleF(30, 696, 200, 20)),
            },
        };
        Redactor.Apply(output, doc, opts);

        using PdfDocument result = PdfDocument.Open(output, leaveOpen: true);

        string after = new TextExtractor(result.Objects).ExtractText(result.Pages[0]);
        after.Should().NotContain("SecretOne", "the redacted line's glyphs must be gone");
        after.Should().Contain("PublicTwo", "the untouched line's glyphs must survive");

        PdfDictionary redacted = FindStructElement(result, mcid: 0);
        redacted.ContainsKey(PdfName.Intern("ActualText")).Should().BeFalse(
            "the redacted element loses its replacement text");

        PdfDictionary survivor = FindStructElement(result, mcid: 1);
        survivor.TryGetValue(PdfName.Intern("ActualText"), out PdfPrimitive? kept)
            .Should().BeTrue("the untouched element keeps its replacement text");
        ((PdfString)kept!).ToTextString().Should().Be("public two");
    }

    [Fact]
    public void Apply_StripsInlineActualText_FromContentStream_WhenRedacted()
    {
        // /ActualText carried inline on the BDC property list (no structure tree)
        // must be dropped when the sequence's glyphs are redacted.
        using MemoryStream source = BuildInlineActualTextSpan(
            "Secret hidden text", actualText: "Secret hidden text");
        using PdfDocument doc = PdfDocument.Open(source, leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        RedactionOptions opts = new RedactionOptions
        {
            Rectangles = new List<RedactionRect>
            {
                new RedactionRect(0, new RectangleF(30, 696, 220, 20)),
            },
        };
        Redactor.Apply(output, doc, opts);

        using PdfDocument result = PdfDocument.Open(output, leaveOpen: true);

        string after = new TextExtractor(result.Objects).ExtractText(result.Pages[0]);
        after.Should().NotContain("Secret");
        after.Should().NotContain("hidden");

        string content = PageContentAscii(result);
        content.Should().NotContain("ActualText",
            "the inline replacement text key must be stripped from the content stream");
        content.Should().NotContain("Secret hidden text",
            "the inline replacement text value must not survive");
    }

    [Fact]
    public void Apply_PreservesInlineActualText_WhenContentSurvives()
    {
        // A redaction rectangle that misses the span leaves the inline /ActualText
        // untouched (the buffered sequence is re-emitted unchanged).
        using MemoryStream source = BuildInlineActualTextSpan(
            "Secret hidden text", actualText: "Secret hidden text");
        using PdfDocument doc = PdfDocument.Open(source, leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        RedactionOptions opts = new RedactionOptions
        {
            Rectangles = new List<RedactionRect>
            {
                new RedactionRect(0, new RectangleF(30, 80, 220, 20)),
            },
        };
        Redactor.Apply(output, doc, opts);

        using PdfDocument result = PdfDocument.Open(output, leaveOpen: true);

        string content = PageContentAscii(result);
        content.Should().Contain("ActualText",
            "replacement text on a non-redacted sequence must be preserved");
    }

    // ── Fixture builders ──────────────────────────────────────────────────

    private static MemoryStream BuildTaggedSingleSpan(string line, string actualText, string alt)
    {
        string content =
            $"/Span <</MCID 0>> BDC BT /F1 14 Tf 40 700 Td ({line}) Tj ET EMC";

        PdfObjectId structRootId = new PdfObjectId(6, 0);
        PdfObjectId elementId = new PdfObjectId(7, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);

        PdfDictionary element = new PdfDictionary();
        element.Set(PdfName.Type, PdfName.Intern("StructElem"));
        element.Set(PdfName.Intern("S"), PdfName.Intern("Span"));
        element.Set(PdfName.Parent, new PdfReference(structRootId));
        element.Set(PdfName.Intern("Pg"), new PdfReference(pageId));
        element.Set(PdfName.Intern("K"), 0);
        element.Set(PdfName.Intern("ActualText"), new PdfString(actualText));
        element.Set(PdfName.Intern("Alt"), new PdfString(alt));

        PdfDictionary structRoot = new PdfDictionary();
        structRoot.Set(PdfName.Type, PdfName.Intern("StructTreeRoot"));
        structRoot.Set(PdfName.Intern("K"), new PdfReference(elementId));

        return Assemble(
            content,
            new List<PdfIndirectObject>
            {
                new PdfIndirectObject(structRootId, structRoot),
                new PdfIndirectObject(elementId, element),
            },
            new PdfReference(structRootId));
    }

    private static MemoryStream BuildTaggedTwoSpans()
    {
        string content =
            "/Span <</MCID 0>> BDC BT /F1 14 Tf 40 700 Td (SecretOne) Tj ET EMC "
            + "/Span <</MCID 1>> BDC BT /F1 14 Tf 40 600 Td (PublicTwo) Tj ET EMC";

        PdfObjectId structRootId = new PdfObjectId(6, 0);
        PdfObjectId firstId = new PdfObjectId(7, 0);
        PdfObjectId secondId = new PdfObjectId(8, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);

        PdfDictionary first = new PdfDictionary();
        first.Set(PdfName.Type, PdfName.Intern("StructElem"));
        first.Set(PdfName.Intern("S"), PdfName.Intern("Span"));
        first.Set(PdfName.Parent, new PdfReference(structRootId));
        first.Set(PdfName.Intern("Pg"), new PdfReference(pageId));
        first.Set(PdfName.Intern("K"), 0);
        first.Set(PdfName.Intern("ActualText"), new PdfString("secret one"));

        PdfDictionary second = new PdfDictionary();
        second.Set(PdfName.Type, PdfName.Intern("StructElem"));
        second.Set(PdfName.Intern("S"), PdfName.Intern("Span"));
        second.Set(PdfName.Parent, new PdfReference(structRootId));
        second.Set(PdfName.Intern("Pg"), new PdfReference(pageId));
        second.Set(PdfName.Intern("K"), 1);
        second.Set(PdfName.Intern("ActualText"), new PdfString("public two"));

        PdfDictionary structRoot = new PdfDictionary();
        structRoot.Set(PdfName.Type, PdfName.Intern("StructTreeRoot"));
        structRoot.Set(PdfName.Intern("K"), new PdfArray(new PdfPrimitive[]
        {
            new PdfReference(firstId),
            new PdfReference(secondId),
        }));

        return Assemble(
            content,
            new List<PdfIndirectObject>
            {
                new PdfIndirectObject(structRootId, structRoot),
                new PdfIndirectObject(firstId, first),
                new PdfIndirectObject(secondId, second),
            },
            new PdfReference(structRootId));
    }

    private static MemoryStream BuildInlineActualTextSpan(string line, string actualText)
    {
        string content =
            $"/Span <</MCID 0 /ActualText ({actualText})>> BDC "
            + $"BT /F1 14 Tf 40 700 Td ({line}) Tj ET EMC";

        return Assemble(content, structObjects: null, structTreeRoot: null);
    }

    // Builds a single-page PDF whose content is the supplied operator string,
    // optionally tagged with a structure tree (objects 6+). Pages/page/content/
    // font are objects 1-5 so structure objects never collide.
    private static MemoryStream Assemble(
        string content,
        IReadOnlyList<PdfIndirectObject>? structObjects,
        PdfReference? structTreeRoot)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);
        PdfObjectId contentId = new PdfObjectId(4, 0);
        PdfObjectId fontId = new PdfObjectId(5, 0);

        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(pagesId));
        if (structTreeRoot is not null)
        {
            catalog.Set(PdfName.Intern("StructTreeRoot"), structTreeRoot);
            PdfDictionary markInfo = new PdfDictionary();
            markInfo.Set(PdfName.Intern("Marked"), true);
            catalog.Set(PdfName.Intern("MarkInfo"), markInfo);
        }

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

        byte[] contentBytes = Encoding.ASCII.GetBytes(content);
        PdfDictionary contentDict = new PdfDictionary();
        contentDict.Set(PdfName.Length, contentBytes.Length);

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(catalogId, catalog),
            new PdfIndirectObject(pagesId, pages),
            new PdfIndirectObject(pageId, page),
            new PdfIndirectObject(contentId, new PdfStream(contentDict, contentBytes)),
            new PdfIndirectObject(fontId, font),
        };

        if (structObjects is not null)
        {
            objects.AddRange(structObjects);
        }

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        ms.Position = 0;
        return ms;
    }

    // ── Result inspection ─────────────────────────────────────────────────

    private static PdfDictionary FindStructElement(PdfDocument document, int mcid)
    {
        PdfObjectStore store = document.Objects;
        PdfDictionary? catalog = null;
        foreach (PdfIndirectObject obj in store.Objects)
        {
            if (obj.Value is PdfDictionary dict
                && dict.TryGetValue(PdfName.Type, out PdfPrimitive? type)
                && type is PdfName name
                && name.Value == "Catalog")
            {
                catalog = dict;
                break;
            }
        }

        if (catalog is null
            || !catalog.TryGetValue(PdfName.Intern("StructTreeRoot"), out PdfPrimitive? rootRef)
            || store.Resolve(rootRef) is not PdfDictionary root
            || !root.TryGetValue(PdfName.Intern("K"), out PdfPrimitive? kids))
        {
            throw new Xunit.Sdk.XunitException("Structure tree root not found.");
        }

        List<PdfPrimitive> children = store.Resolve(kids) is PdfArray array
            ? new List<PdfPrimitive>(array)
            : new List<PdfPrimitive> { kids };

        foreach (PdfPrimitive child in children)
        {
            if (store.Resolve(child) is PdfDictionary element
                && element.TryGetValue(PdfName.Intern("K"), out PdfPrimitive? k)
                && store.Resolve(k) is PdfInteger leaf
                && leaf.Value == mcid)
            {
                return element;
            }
        }

        throw new Xunit.Sdk.XunitException($"No StructElem with MCID {mcid} found.");
    }

    private static string PageContentAscii(PdfDocument document)
    {
        PdfDictionary pageDict = document.Pages[0].Dictionary;
        StringBuilder builder = new StringBuilder();
        if (pageDict.TryGetValue(PdfName.Contents, out PdfPrimitive? contents))
        {
            PdfPrimitive resolved = document.Objects.Resolve(contents);
            if (resolved is PdfArray array)
            {
                foreach (PdfPrimitive item in array)
                {
                    AppendStream(document, item, builder);
                }
            }
            else
            {
                AppendStream(document, contents, builder);
            }
        }

        return builder.ToString();
    }

    private static void AppendStream(PdfDocument document, PdfPrimitive reference, StringBuilder builder)
    {
        if (document.Objects.Resolve(reference) is PdfStream stream)
        {
            builder.Append(Encoding.Latin1.GetString(stream.RawBytes));
            builder.Append('\n');
        }
    }
}
