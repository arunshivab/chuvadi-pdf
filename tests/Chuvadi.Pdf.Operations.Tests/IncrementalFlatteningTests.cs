// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.5.6 — Incremental updates; §7.5.4 — Cross-reference
// PHASE: Phase 2 — item 6, garbage collection + incremental-update flattening
//
// A PDF carrying an incremental update holds more than one generation of a
// superseded object plus any orphans the update stranded. PdfCompressor's
// reachability rewrite flattens this: only the latest version of each reachable
// object is carried, renumbered into a single clean generation, and the /Prev
// chain disappears. These tests pin that behaviour end to end.

using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

public sealed class IncrementalFlatteningTests
{
    private static readonly PdfName Marker = PdfName.Intern("TestMarker");

    [Fact]
    public void Compress_IncrementalUpdate_KeepsLatestGenerationAndDropsOrphans()
    {
        byte[] incremental = BuildWithIncrementalUpdate();

        // Sanity: the source genuinely supersedes the page — the latest
        // generation (marker 222) is what the reader resolves.
        using (PdfDocument source = PdfDocument.Open(new MemoryStream(incremental), leaveOpen: false))
        {
            source.Pages[0].Dictionary.GetInteger(Marker).Should().Be(222);
            source.Trailer.TryGetValue(PdfName.Intern("Prev"), out _).Should().BeTrue(
                "the incremental update chains back to the original cross-reference");
        }

        using PdfDocument document = PdfDocument.Open(new MemoryStream(incremental), leaveOpen: false);
        using MemoryStream output = new MemoryStream();
        CompressionResult result = PdfCompressor.Compress(document, output);

        result.SkipReason.Should().Be(CompressionSkipReason.None);
        result.ObjectsRemoved.Should().BeGreaterThanOrEqualTo(1,
            "the orphan stranded by the incremental update is swept");

        using PdfDocument flattened = PdfDocument.Open(new MemoryStream(output.ToArray()), leaveOpen: false);

        // The latest generation survived the flatten.
        flattened.Pages[0].Dictionary.GetInteger(Marker).Should().Be(222);

        // The result is a single clean generation: no incremental chain remains.
        flattened.Trailer.TryGetValue(PdfName.Intern("Prev"), out _).Should().BeFalse(
            "flattening collapses the update sections into one cross-reference");
    }

    // Base single-page document, then an incremental update that (a) supersedes
    // the page object with a new marker value and (b) appends an orphan object
    // that nothing references.
    private static byte[] BuildWithIncrementalUpdate()
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);
        PdfObjectId contentId = new PdfObjectId(4, 0);
        PdfObjectId orphanId = new PdfObjectId(99, 0);

        byte[] content = Encoding.ASCII.GetBytes("0 0 100 100 re f\n");

        PdfDictionary pageDict = NewPage(pagesId, contentId, marker: 100);

        PdfArray kids = new PdfArray(new PdfPrimitive[] { new PdfReference(pageId) });
        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, kids);
        pagesDict.Set(PdfName.Count, 1);

        PdfDictionary catalogDict = new PdfDictionary();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(contentId, new PdfStream(new PdfDictionary(), content)),
            new PdfIndirectObject(pageId, pageDict),
            new PdfIndirectObject(pagesId, pagesDict),
            new PdfIndirectObject(catalogId, catalogDict),
        };

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        byte[] baseBytes;
        using (MemoryStream ms = new MemoryStream())
        {
            PdfWriter.Write(ms, objects, trailer);
            baseBytes = ms.ToArray();
        }

        // Incremental update: supersede the page (marker 222) and strand an orphan.
        PdfDictionary orphan = new PdfDictionary();
        orphan.Set(PdfName.Type, PdfName.Intern("Stranded"));
        orphan.Set(Marker, 777);

        List<PdfIndirectObject> updates = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(pageId, NewPage(pagesId, contentId, marker: 222)),
            new PdfIndirectObject(orphanId, orphan),
        };

        return PdfWriter.WriteIncrementalUpdate(baseBytes, updates);
    }

    private static PdfDictionary NewPage(PdfObjectId pagesId, PdfObjectId contentId, int marker)
    {
        PdfDictionary pageDict = new PdfDictionary();
        pageDict.Set(PdfName.Type, PdfName.Page);
        pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
        pageDict.Set(PdfName.Intern("Contents"), new PdfReference(contentId));
        pageDict.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(595), new PdfInteger(842),
        }));
        pageDict.Set(Marker, marker);
        return pageDict;
    }
}
