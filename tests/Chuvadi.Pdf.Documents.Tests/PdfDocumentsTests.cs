// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.7.2, §7.7.3, §14.3.3
// PHASE: Phase 1 — Chuvadi.Pdf.Documents tests

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Documents.Tests;

public sealed class PdfDocumentTests
{
    // ── Helper: build a minimal in-memory PDF ─────────────────────────────

    private static MemoryStream BuildMinimalPdf(int pageCount = 0)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);

        PdfArray kidsArray = new PdfArray([]);
        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, kidsArray);
        pagesDict.Set(PdfName.Count, pageCount);

        PdfDictionary catalogDict = new PdfDictionary();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

        PdfIndirectObject[] objects =
        [
            new PdfIndirectObject(catalogId, catalogDict),
            new PdfIndirectObject(pagesId, pagesDict),
        ];

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        return ms;
    }

    private static MemoryStream BuildPdfWithPages(int pageCount)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);

        PdfArray kidsArray = new PdfArray([]);
        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, kidsArray);
        pagesDict.Set(PdfName.Count, pageCount);

        // MediaBox inherited by all pages: A4 in points (595 x 842)
        PdfArray mediaBox = new PdfArray([
            new PdfInteger(0),
            new PdfInteger(0),
            new PdfInteger(595),
            new PdfInteger(842)
        ]);
        pagesDict.Set(PdfName.MediaBox, mediaBox);

        PdfDictionary catalogDict = new PdfDictionary();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

        // Build pages as indirect objects starting at object 3
        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(catalogId, catalogDict),
            new PdfIndirectObject(pagesId, pagesDict),
        };

        for (int i = 0; i < pageCount; i++)
        {
            PdfObjectId pageId = new PdfObjectId(3 + i, 0);
            PdfDictionary pageDict = new PdfDictionary();
            pageDict.Set(PdfName.Type, PdfName.Page);
            pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
            objects.Add(new PdfIndirectObject(pageId, pageDict));
            kidsArray.Add(new PdfReference(pageId));
        }

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        return ms;
    }

    // ── PdfCorruptionException ──────────────────────────────────────────────

    [Fact]
    public void PdfCorruptionException_DefaultConstructor_HasMessage()
    {
        PdfCorruptionException ex = new PdfCorruptionException();
        ex.Message.Should().NotBeEmpty();
    }

    [Fact]
    public void PdfCorruptionException_MessagePreserved()
    {
        PdfCorruptionException ex = new PdfCorruptionException("test error");
        ex.Message.Should().Be("test error");
    }

    [Fact]
    public void PdfCorruptionException_InnerExceptionPreserved()
    {
        InvalidOperationException inner = new InvalidOperationException("inner");
        PdfCorruptionException ex = new PdfCorruptionException("outer", inner);
        ex.InnerException.Should().BeSameAs(inner);
    }

    // ── PdfDocument.Open ──────────────────────────────────────────────────

    [Fact]
    public void Open_NullStream_Throws()
    {
        Action act = () => PdfDocument.Open((Stream)null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Open_NullPath_Throws()
    {
        Action act = () => PdfDocument.Open((string)null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Open_ValidMinimalPdf_Succeeds()
    {
        using (MemoryStream ms = BuildMinimalPdf())
        {
            ms.Seek(0, SeekOrigin.Begin);

            using (PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true))
            {
                doc.Should().NotBeNull();
            }
        }
    }

    // ── PdfDocument.OpenAsync ─────────────────────────────────────────────

    [Fact]
    public async Task OpenAsync_ValidMinimalPdf_Succeeds()
    {
        // Round-trip via the async path: the document's pages and trailer
        // should be the same as what the sync Open produces.
        using MemoryStream ms = BuildPdfWithPages(2);
        ms.Seek(0, SeekOrigin.Begin);

        using PdfDocument doc = await PdfDocument.OpenAsync(ms);

        doc.PageCount.Should().Be(2);
        doc.Catalog.Type.Should().Be(PdfName.Catalog);
    }

    [Fact]
    public async Task OpenAsync_NullStream_Throws()
    {
        Func<Task> act = async () => await PdfDocument.OpenAsync((Stream)null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task OpenAsync_CancelledToken_Throws()
    {
        using MemoryStream ms = BuildMinimalPdf();
        ms.Seek(0, SeekOrigin.Begin);

        using CancellationTokenSource cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = async () => await PdfDocument.OpenAsync(ms, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── PdfDocument.Catalog and Trailer ───────────────────────────────────

    [Fact]
    public void Catalog_IsTypeCatalog()
    {
        using (MemoryStream ms = BuildMinimalPdf())
        {
            ms.Seek(0, SeekOrigin.Begin);

            using (PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true))
            {
                doc.Catalog.Type.Should().Be(PdfName.Catalog);
            }
        }
    }

    [Fact]
    public void Trailer_HasRootEntry()
    {
        using (MemoryStream ms = BuildMinimalPdf())
        {
            ms.Seek(0, SeekOrigin.Begin);

            using (PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true))
            {
                doc.Trailer.ContainsKey(PdfName.Root).Should().BeTrue();
            }
        }
    }

    // ── PdfDocument.Pages ─────────────────────────────────────────────────

    [Fact]
    public void PageCount_EmptyDocument_IsZero()
    {
        using (MemoryStream ms = BuildMinimalPdf(0))
        {
            ms.Seek(0, SeekOrigin.Begin);

            using (PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true))
            {
                doc.PageCount.Should().Be(0);
            }
        }
    }

    [Fact]
    public void PageCount_ThreePages_IsThree()
    {
        using (MemoryStream ms = BuildPdfWithPages(3))
        {
            ms.Seek(0, SeekOrigin.Begin);

            using (PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true))
            {
                doc.PageCount.Should().Be(3);
            }
        }
    }

    [Fact]
    public void EnumerateStreaming_YieldsEveryPageInOrder()
    {
        using MemoryStream ms = BuildPdfWithPages(3);
        ms.Seek(0, SeekOrigin.Begin);
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true);

        List<int> indices = new List<int>();
        foreach (PdfPage page in doc.Pages.EnumerateStreaming())
        {
            indices.Add(page.Index);
        }

        indices.Should().Equal(0, 1, 2);
    }

    [Fact]
    public void EnumerateStreaming_MatchesIndexedAccess()
    {
        using MemoryStream ms = BuildPdfWithPages(3);
        ms.Seek(0, SeekOrigin.Begin);
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true);

        int streamed = 0;
        foreach (PdfPage page in doc.Pages.EnumerateStreaming())
        {
            streamed++;
        }

        streamed.Should().Be(3);
        // The indexer still works afterwards (independent construction).
        doc.Pages[2].Index.Should().Be(2);
    }

    [Fact]
    public void Pages_OutOfRange_Throws()
    {
        using (MemoryStream ms = BuildPdfWithPages(2))
        {
            ms.Seek(0, SeekOrigin.Begin);

            using (PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true))
            {
                Action act = () => { PdfPage _ = doc.Pages[5]; };
                act.Should().Throw<ArgumentOutOfRangeException>();
            }
        }
    }

    // ── Page tree depth guard (regression: fuzzer-found stack overflow) ───

    [Fact]
    public void Pages_CyclicKidsReference_ThrowsInsteadOfStackOverflow()
    {
        // The /Pages node lists itself as one of its own /Kids — a malformed
        // input that previously caused unbounded recursion in FindPage and
        // a StackOverflowException at process level. Now caught by the depth
        // guard and rejected as PdfCorruptionException.
        using (MemoryStream ms = BuildPdfWithCyclicPageTree())
        {
            ms.Seek(0, SeekOrigin.Begin);

            using (PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true))
            {
                Action act = () => { PdfPage _ = doc.Pages[0]; };
                act.Should().Throw<PdfCorruptionException>()
                    .WithMessage("*Page tree depth*");
            }
        }
    }

    [Fact]
    public void Pages_DeepPageTree_ThrowsInsteadOfStackOverflow()
    {
        // A non-cyclic but pathologically deep page tree — a chain of /Pages
        // nodes each containing exactly one /Pages kid, repeated far beyond
        // any legitimate depth. Same defense, same outcome.
        using (MemoryStream ms = BuildPdfWithDeepPageTree(depth: 2000))
        {
            ms.Seek(0, SeekOrigin.Begin);

            using (PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true))
            {
                Action act = () => { PdfPage _ = doc.Pages[0]; };
                act.Should().Throw<PdfCorruptionException>()
                    .WithMessage("*Page tree depth*");
            }
        }
    }

    private static MemoryStream BuildPdfWithCyclicPageTree()
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);

        // /Pages node whose /Kids contains a reference back to itself.
        // /Count = 1 ensures FindPage's "targetIndex < localOffset + subtreeCount"
        // branch is taken so recursion actually fires.
        PdfArray kidsArray = new PdfArray([new PdfReference(pagesId)]);
        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, kidsArray);
        pagesDict.Set(PdfName.Count, 1);

        PdfDictionary catalogDict = new PdfDictionary();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

        PdfIndirectObject[] objects =
        [
            new PdfIndirectObject(catalogId, catalogDict),
            new PdfIndirectObject(pagesId, pagesDict),
        ];

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        return ms;
    }

    private static MemoryStream BuildPdfWithDeepPageTree(int depth)
    {
        // Builds a linear chain: catalog → /Pages(1) → /Pages(2) → ... → /Pages(depth)
        // with no leaf /Page node. Each intermediate /Pages claims Count=1 so
        // FindPage descends. The traversal will hit the depth limit before
        // reaching the (non-existent) leaf.
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId rootPagesId = new PdfObjectId(2, 0);

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>();

        PdfDictionary catalogDict = new PdfDictionary();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(rootPagesId));
        objects.Add(new PdfIndirectObject(catalogId, catalogDict));

        for (int i = 0; i < depth; i++)
        {
            PdfObjectId thisId = new PdfObjectId(2 + i, 0);
            PdfObjectId nextId = new PdfObjectId(3 + i, 0);

            PdfDictionary node = new PdfDictionary();
            node.Set(PdfName.Type, PdfName.Pages);
            node.Set(PdfName.Kids, new PdfArray([new PdfReference(nextId)]));
            node.Set(PdfName.Count, 1);
            objects.Add(new PdfIndirectObject(thisId, node));
        }

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        return ms;
    }

    // ── PdfPage ───────────────────────────────────────────────────────────

    [Fact]
    public void Page_Index_IsZeroBased()
    {
        using (MemoryStream ms = BuildPdfWithPages(3))
        {
            ms.Seek(0, SeekOrigin.Begin);

            using (PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true))
            {
                doc.Pages[0].Index.Should().Be(0);
                doc.Pages[1].Index.Should().Be(1);
                doc.Pages[2].Index.Should().Be(2);
            }
        }
    }

    [Fact]
    public void Page_PageNumber_IsOneBased()
    {
        using (MemoryStream ms = BuildPdfWithPages(2))
        {
            ms.Seek(0, SeekOrigin.Begin);

            using (PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true))
            {
                doc.Pages[0].PageNumber.Should().Be(1);
                doc.Pages[1].PageNumber.Should().Be(2);
            }
        }
    }

    [Fact]
    public void Page_MediaBox_InheritedFromPagesNode()
    {
        using (MemoryStream ms = BuildPdfWithPages(1))
        {
            ms.Seek(0, SeekOrigin.Begin);

            using (PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true))
            {
                PdfPage page = doc.Pages[0];
                page.Width.Should().BeApproximately(595, 1);
                page.Height.Should().BeApproximately(842, 1);
            }
        }
    }

    [Fact]
    public void Page_Rotate_DefaultsToZero()
    {
        using (MemoryStream ms = BuildPdfWithPages(1))
        {
            ms.Seek(0, SeekOrigin.Begin);

            using (PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true))
            {
                doc.Pages[0].Rotate.Should().Be(0);
            }
        }
    }

    [Fact]
    public void Pages_EnumerateAll_YieldsCorrectCount()
    {
        int expected = 3;

        using (MemoryStream ms = BuildPdfWithPages(expected))
        {
            ms.Seek(0, SeekOrigin.Begin);

            using (PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true))
            {
                int count = 0;

                foreach (PdfPage page in doc.Pages)
                {
                    count++;
                }

                count.Should().Be(expected);
            }
        }
    }

    // ── PdfRectangle ──────────────────────────────────────────────────────

    [Fact]
    public void PdfRectangle_Width_IsAbsoluteDifference()
    {
        PdfRectangle rect = new PdfRectangle(0, 0, 595, 842);
        rect.Width.Should().BeApproximately(595, 0.001);
        rect.Height.Should().BeApproximately(842, 0.001);
    }

    // ── Metadata ──────────────────────────────────────────────────────────

    [Fact]
    public void Metadata_AbsentInfoDict_ReturnsNulls()
    {
        using (MemoryStream ms = BuildMinimalPdf())
        {
            ms.Seek(0, SeekOrigin.Begin);

            using (PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true))
            {
                doc.Title.Should().BeNull();
                doc.Author.Should().BeNull();
                doc.Subject.Should().BeNull();
            }
        }
    }
}
