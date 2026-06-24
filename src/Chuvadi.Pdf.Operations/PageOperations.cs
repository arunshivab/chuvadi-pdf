// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.7.3 — Page tree
//        PDF 32000-1:2008 §7.7.3.3 — Page objects
// PHASE: Phase 1 — Chuvadi.Pdf.Operations
// Merge, split, delete, rotate and reorder PDF pages.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Forms;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// Provides static methods for high-level PDF page operations:
/// merge, split, delete, rotate, and reorder.
/// </summary>
/// <remarks>
/// All operations work at the PDF object-graph level — they copy and
/// reassemble page dictionaries without modifying content streams.
///
/// Each method writes a new PDF to the supplied output stream using
/// <see cref="PdfWriter"/>. The input documents are not modified.
///
/// PDF 32000-1:2008 §7.7.3 — Page tree nodes and page objects.
/// </remarks>
public static class PageOperations
{
    // ── Merge ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Merges two or more PDF documents into a single output stream.
    /// Pages appear in the order of the input documents.
    /// </summary>
    /// <param name="output">The stream to write the merged PDF to.</param>
    /// <param name="documents">The documents to merge, in order.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="output"/> or <paramref name="documents"/> is null.
    /// </exception>
    /// <exception cref="OperationsException">
    /// Thrown when any document has no pages or an invalid structure.
    /// </exception>
    public static void Merge(Stream output, params PdfDocument[] documents)
    {
        if (output is null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        if (documents is null)
        {
            throw new ArgumentNullException(nameof(documents));
        }

        if (documents.Length == 0)
        {
            throw new OperationsException("At least one document is required for merge.");
        }

        PageBuilder builder = new PageBuilder();

        for (int d = 0; d < documents.Length; d++)
        {
            PdfDocument doc = documents[d]
                ?? throw new OperationsException("Null document in merge list.");

            for (int i = 0; i < doc.PageCount; i++)
            {
                builder.AddPage(doc.Pages[i], doc.Objects, d);
            }
        }

        builder.Write(output);
    }

    /// <summary>
    /// Merges two or more PDF documents into a single output stream, optionally
    /// carrying each input's outline (bookmarks) into the result with page
    /// indices re-based to the merged offsets. Pages appear in the order of the
    /// input documents.
    /// </summary>
    /// <param name="output">The stream to write the merged PDF to.</param>
    /// <param name="documents">The documents to merge, in order.</param>
    /// <param name="options">Options controlling outline preservation.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="output"/>, <paramref name="documents"/>, or
    /// <paramref name="options"/> is null.
    /// </exception>
    /// <exception cref="OperationsException">
    /// Thrown when the document list is empty or contains a null document.
    /// </exception>
    public static void Merge(Stream output, IReadOnlyList<PdfDocument> documents, MergeOptions options)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(options);

        if (documents.Count == 0)
        {
            throw new OperationsException("At least one document is required for merge.");
        }

        PageBuilder builder = new PageBuilder();
        int[] pageOffsets = new int[documents.Count];
        int runningOffset = 0;

        for (int d = 0; d < documents.Count; d++)
        {
            PdfDocument doc = documents[d]
                ?? throw new OperationsException("Null document in merge list.");

            pageOffsets[d] = runningOffset;

            for (int i = 0; i < doc.PageCount; i++)
            {
                builder.AddPage(doc.Pages[i], doc.Objects, d);
            }

            runningOffset += doc.PageCount;
        }

        if (options.PreserveOutlines)
        {
            List<OutlineNode> roots = BuildMergedOutline(documents, pageOffsets, options);

            if (roots.Count > 0)
            {
                builder.SetOutline(roots);
            }
        }

        builder.Write(output);
    }

    // ── Assemble ──────────────────────────────────────────────────────────

    /// <summary>
    /// Assembles a new PDF from an ordered list of source pages, each identified
    /// by a <see cref="PageSelector"/>. Unlike <see cref="ReorderPages"/> (a
    /// single-document permutation), the same page may appear any number of times
    /// and selectors may interleave pages from different source documents, all in
    /// one write. Output page order is exactly the order of
    /// <paramref name="pages"/>.
    /// </summary>
    /// <param name="output">The stream to write the assembled PDF to.</param>
    /// <param name="pages">The ordered source pages; duplicates are allowed.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="output"/> or <paramref name="pages"/> is null.
    /// </exception>
    /// <exception cref="OperationsException">
    /// Thrown when the list is empty, a selector has a null document, or a page
    /// index is out of range for its source document.
    /// </exception>
    public static void Assemble(Stream output, IReadOnlyList<PageSelector> pages)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(pages);

        if (pages.Count == 0)
        {
            throw new OperationsException("At least one page is required for assembly.");
        }

        PageBuilder builder = new PageBuilder();

        // Distinct source documents get a stable per-document index (by reference
        // identity), so the object-number remap keeps each source's objects
        // separate and a repeated document's shared objects are copied once.
        Dictionary<PdfDocument, int> docIndices = new Dictionary<PdfDocument, int>();

        for (int i = 0; i < pages.Count; i++)
        {
            PageSelector selector = pages[i];

            if (selector.Document is null)
            {
                throw new OperationsException("Null document in assembly list.");
            }

            PdfDocument doc = selector.Document;

            if (selector.PageIndex < 0 || selector.PageIndex >= doc.PageCount)
            {
                throw new OperationsException(
                    $"Page index {selector.PageIndex} is out of range " +
                    $"[0, {doc.PageCount}) for an assembly source.");
            }

            if (!docIndices.TryGetValue(doc, out int docIndex))
            {
                docIndex = docIndices.Count;
                docIndices[doc] = docIndex;
            }

            builder.AddPage(doc.Pages[selector.PageIndex], doc.Objects, docIndex);
        }

        builder.Write(output);
    }

    // ── Merged outline construction ───────────────────────────────────────

    // Reads each input's outline (titles + resolved destination page indices),
    // re-bases the destinations to the merged page offsets, and — when
    // WrapPerDocument is set — nests each input's items under a synthetic parent
    // pointing at that document's first merged page. Inputs with no bookmarks
    // contribute nothing (no empty parent node).
    private static List<OutlineNode> BuildMergedOutline(
        IReadOnlyList<PdfDocument> documents,
        int[] pageOffsets,
        MergeOptions options)
    {
        List<OutlineNode> roots = new List<OutlineNode>();

        for (int d = 0; d < documents.Count; d++)
        {
            PdfDocument doc = documents[d];
            IReadOnlyList<OutlineItem> items = OutlineReader.GetOutlines(doc);
            List<OutlineNode> rebased = RebaseOutlineItems(items, pageOffsets[d]);

            if (rebased.Count == 0)
            {
                continue;
            }

            if (options.WrapPerDocument)
            {
                string title = ResolveDocumentTitle(options.DocumentTitles, d, doc);
                roots.Add(new OutlineNode(title, pageOffsets[d], rebased));
            }
            else
            {
                roots.AddRange(rebased);
            }
        }

        return roots;
    }

    // Recursively re-bases a read-side outline subtree: each resolved page index
    // is shifted by the document's merged offset; an unresolved index (-1) stays
    // -1 and is written as a title-only bookmark.
    private static List<OutlineNode> RebaseOutlineItems(IReadOnlyList<OutlineItem> items, int offset)
    {
        List<OutlineNode> result = new List<OutlineNode>(items.Count);

        for (int i = 0; i < items.Count; i++)
        {
            OutlineItem item = items[i];
            int mergedIndex = item.DestinationPageIndex >= 0
                ? item.DestinationPageIndex + offset
                : -1;
            List<OutlineNode> children = RebaseOutlineItems(item.Children, offset);
            result.Add(new OutlineNode(item.Title, mergedIndex, children));
        }

        return result;
    }

    // Per-document parent node title: caller-supplied, else the document /Title,
    // else "Document N" (one-based).
    private static string ResolveDocumentTitle(
        IReadOnlyList<string?>? titles,
        int index,
        PdfDocument document)
    {
        if (titles is not null && index < titles.Count && !string.IsNullOrEmpty(titles[index]))
        {
            return titles[index]!;
        }

        if (!string.IsNullOrEmpty(document.Title))
        {
            return document.Title;
        }

        return "Document " +
            (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    // ── Split ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Splits a document into individual single-page PDFs.
    /// </summary>
    /// <param name="document">The document to split.</param>
    /// <returns>
    /// A list of <see cref="MemoryStream"/> objects, one per page,
    /// each containing a valid single-page PDF.
    /// </returns>
    public static List<MemoryStream> SplitPages(PdfDocument document)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        List<MemoryStream> results = new List<MemoryStream>(document.PageCount);

        for (int i = 0; i < document.PageCount; i++)
        {
            MemoryStream ms = new MemoryStream();
            PageBuilder builder = new PageBuilder();
            builder.AddPage(document.Pages[i], document.Objects);
            builder.Write(ms);
            results.Add(ms);
        }

        return results;
    }

    /// <summary>
    /// Extracts a contiguous range of pages from a document into a new PDF.
    /// </summary>
    /// <param name="output">The stream to write the extracted pages to.</param>
    /// <param name="document">The source document.</param>
    /// <param name="startIndex">Zero-based index of the first page to include.</param>
    /// <param name="count">The number of pages to include.</param>
    public static void ExtractPages(
        Stream output,
        PdfDocument document,
        int startIndex,
        int count)
    {
        if (output is null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (startIndex < 0 || startIndex >= document.PageCount)
        {
            throw new OperationsException(
                $"startIndex {startIndex} is out of range [0, {document.PageCount}).");
        }

        if (count <= 0 || startIndex + count > document.PageCount)
        {
            throw new OperationsException(
                $"count {count} is invalid for startIndex {startIndex} " +
                $"with {document.PageCount} pages.");
        }

        PageBuilder builder = new PageBuilder();

        for (int i = startIndex; i < startIndex + count; i++)
        {
            builder.AddPage(document.Pages[i], document.Objects);
        }

        builder.Write(output);
    }

    // ── Delete ────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes a new PDF containing all pages except those at the given indices.
    /// </summary>
    /// <param name="output">The stream to write the result to.</param>
    /// <param name="document">The source document.</param>
    /// <param name="pageIndices">Zero-based indices of pages to remove.</param>
    public static void DeletePages(
        Stream output,
        PdfDocument document,
        IEnumerable<int> pageIndices)
    {
        if (output is null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (pageIndices is null)
        {
            throw new ArgumentNullException(nameof(pageIndices));
        }

        HashSet<int> toDelete = new HashSet<int>(pageIndices);
        PageBuilder builder = new PageBuilder();

        for (int i = 0; i < document.PageCount; i++)
        {
            if (!toDelete.Contains(i))
            {
                builder.AddPage(document.Pages[i], document.Objects);
            }
        }

        if (builder.PageCount == 0)
        {
            throw new OperationsException(
                "All pages were deleted. A PDF must contain at least one page.");
        }

        builder.Write(output);
    }

    // ── Rotate ────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes a new PDF with the specified pages rotated by the given angle.
    /// </summary>
    /// <param name="output">The stream to write the result to.</param>
    /// <param name="document">The source document.</param>
    /// <param name="degrees">
    /// Rotation in degrees. Must be 0, 90, 180, or 270.
    /// Applied in addition to any existing /Rotate value on the page.
    /// </param>
    /// <param name="pageIndices">
    /// Zero-based indices of pages to rotate.
    /// Pass null or empty to rotate all pages.
    /// </param>
    public static void RotatePages(
        Stream output,
        PdfDocument document,
        int degrees,
        IEnumerable<int>? pageIndices = null)
    {
        if (output is null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (degrees != 0 && degrees != 90 && degrees != 180 && degrees != 270)
        {
            throw new OperationsException(
                $"Rotation must be 0, 90, 180, or 270 degrees. Got {degrees}.");
        }

        HashSet<int>? rotateSet = pageIndices is null
            ? null
            : new HashSet<int>(pageIndices);

        PageBuilder builder = new PageBuilder();

        for (int i = 0; i < document.PageCount; i++)
        {
            PdfPage page = document.Pages[i];

            if (rotateSet is null || rotateSet.Contains(i))
            {
                int existing = page.Rotate;
                int newRotate = (existing + degrees) % 360;
                builder.AddPageWithRotation(page, document.Objects, newRotate);
            }
            else
            {
                builder.AddPage(page, document.Objects);
            }
        }

        builder.Write(output);
    }

    // ── Reorder ───────────────────────────────────────────────────────────

    /// <summary>
    /// Writes a new PDF with pages in the order specified by
    /// <paramref name="newOrder"/>.
    /// </summary>
    /// <param name="output">The stream to write the result to.</param>
    /// <param name="document">The source document.</param>
    /// <param name="newOrder">
    /// A permutation of zero-based page indices specifying the new order.
    /// Must contain exactly one entry per page.
    /// </param>
    public static void ReorderPages(
        Stream output,
        PdfDocument document,
        IReadOnlyList<int> newOrder)
    {
        if (output is null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (newOrder is null)
        {
            throw new ArgumentNullException(nameof(newOrder));
        }

        if (newOrder.Count != document.PageCount)
        {
            throw new OperationsException(
                $"newOrder has {newOrder.Count} entries but document has " +
                $"{document.PageCount} pages.");
        }

        PageBuilder builder = new PageBuilder();

        foreach (int idx in newOrder)
        {
            if (idx < 0 || idx >= document.PageCount)
            {
                throw new OperationsException(
                    $"Page index {idx} in newOrder is out of range [0, {document.PageCount}).");
            }

            builder.AddPage(document.Pages[idx], document.Objects);
        }

        builder.Write(output);
    }

    // ── PageBuilder (private helper) ──────────────────────────────────────

    /// <summary>
    /// Accumulates page dictionaries and their referenced objects,
    /// then writes a complete self-contained PDF.
    /// </summary>
    private sealed class PageBuilder
    {
        private readonly List<PageEntry> _pages;

        // Re-based merged outline (top-level nodes), or null when no outline is
        // carried. Set by the outline-preserving Merge overload before Write.
        private List<OutlineNode>? _outlineRoots;

        internal PageBuilder()
        {
            _pages = new List<PageEntry>();
        }

        internal int PageCount => _pages.Count;

        internal void SetOutline(List<OutlineNode> roots)
        {
            _outlineRoots = roots;
        }

        internal void AddPage(PdfPage page, IPdfObjectResolver resolver, int docIndex = 0)
        {
            AddPageWithRotation(page, resolver, page.Rotate, docIndex);
        }

        internal void AddPageWithRotation(
            PdfPage page,
            IPdfObjectResolver resolver,
            int rotate,
            int docIndex = 0)
        {
            // Deep-copy the page dictionary, stripping /Parent (will be rewritten).
            PdfDictionary pageCopy = ObjectImporter.CopyDictionary(page.Dictionary);
            pageCopy.Set(PdfName.Type, PdfName.Page);
            pageCopy.Set(PdfName.Intern("Rotate"), rotate);

            // Remove /Parent — we will set it when building the page tree.
            if (pageCopy.ContainsKey(PdfName.Parent))
            {
                pageCopy.Set(PdfName.Parent, PdfNull.Value);
            }

            // Copy referenced objects (Resources, Contents).
            List<PdfIndirectObject> referencedObjects = new List<PdfIndirectObject>();
            ObjectImporter.CollectReferences(page.Dictionary, resolver, referencedObjects,
                new HashSet<int>());

            _pages.Add(new PageEntry(pageCopy, referencedObjects, docIndex));
        }

        internal void Write(Stream output)
        {
            // Assign object numbers.
            // Layout:
            //   1 = Catalog
            //   2 = Pages root
            //   3..N = page objects
            //   N+1.. = referenced objects (Resources, Contents streams, etc.)
            List<PdfIndirectObject> allObjects = new List<PdfIndirectObject>();

            PdfObjectId catalogId = new PdfObjectId(1, 0);
            PdfObjectId pagesId = new PdfObjectId(2, 0);

            int nextId = 3;

            // Assign IDs to page dicts.
            List<PdfObjectId> pageIds = new List<PdfObjectId>();

            foreach (PageEntry entry in _pages)
            {
                PdfObjectId id = new PdfObjectId(nextId++, 0);
                pageIds.Add(id);
            }

            // Assign IDs to all referenced objects, building a remap table.
            // Keyed by (source document, original object number): object
            // numbers are per-document, so two inputs can legitimately reuse
            // the same number for different objects. Keying on the bare number
            // collapsed those distinct objects into one — corrupting pages
            // whose content streams happened to share a number across inputs.
            Dictionary<(int Doc, int Num), int> idRemap =
                new Dictionary<(int Doc, int Num), int>();

            foreach (PageEntry entry in _pages)
            {
                foreach (PdfIndirectObject refObj in entry.ReferencedObjects)
                {
                    (int, int) key = (entry.DocIndex, refObj.Id.ObjectNumber);
                    if (!idRemap.ContainsKey(key))
                    {
                        idRemap[key] = nextId++;
                    }
                }
            }

            // Build Pages root.
            PdfArray kidsArray = new PdfArray([]);

            foreach (PdfObjectId pid in pageIds)
            {
                kidsArray.Add(new PdfReference(pid));
            }

            PdfDictionary pagesDict = new PdfDictionary();
            pagesDict.Set(PdfName.Type, PdfName.Pages);
            pagesDict.Set(PdfName.Kids, kidsArray);
            pagesDict.Set(PdfName.Count, _pages.Count);
            allObjects.Add(new PdfIndirectObject(pagesId, pagesDict));

            // Build page objects, fixing /Parent and remapping references.
            for (int i = 0; i < _pages.Count; i++)
            {
                // _pages[i].PageDict is already detached from the source
                // (deep-copied in AddPage). Deep-copy again with the remap to
                // produce the final renumbered page, then set /Parent.
                PdfDictionary pageDict =
                    (PdfDictionary)ObjectImporter.DeepCopyPrimitive(
                        _pages[i].PageDict, _pages[i].DocIndex, idRemap);
                pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
                allObjects.Add(new PdfIndirectObject(pageIds[i], pageDict));
            }

            // Add referenced objects with remapped IDs.
            HashSet<(int Doc, int Num)> addedOriginals = new HashSet<(int Doc, int Num)>();

            foreach (PageEntry entry in _pages)
            {
                foreach (PdfIndirectObject refObj in entry.ReferencedObjects)
                {
                    (int, int) key = (entry.DocIndex, refObj.Id.ObjectNumber);
                    if (addedOriginals.Contains(key))
                    {
                        continue;
                    }

                    addedOriginals.Add(key);
                    int newId = idRemap[key];
                    PdfPrimitive valueCopy =
                        ObjectImporter.DeepCopyPrimitive(refObj.Value, entry.DocIndex, idRemap);
                    allObjects.Add(new PdfIndirectObject(new PdfObjectId(newId, 0), valueCopy));
                }
            }

            // Build catalog.
            PdfDictionary catalogDict = new PdfDictionary();
            catalogDict.Set(PdfName.Type, PdfName.Catalog);
            catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

            // Carry merged outlines (already re-based to merged page offsets).
            // Allocated last so outline object numbers follow the page and
            // referenced objects.
            if (_outlineRoots is not null && _outlineRoots.Count > 0)
            {
                PdfObjectId outlineRootId =
                    BuildOutlineObjects(_outlineRoots, pageIds, allObjects, ref nextId);
                catalogDict.Set(PdfName.Outlines, new PdfReference(outlineRootId));
            }

            allObjects.Add(new PdfIndirectObject(catalogId, catalogDict));

            // Build trailer.
            PdfDictionary trailer = new PdfDictionary();
            trailer.Set(PdfName.Root, new PdfReference(catalogId));

            PdfWriter.Write(output, allObjects, trailer);
        }

        // ── Object collection ──────────────────────────────────────────────

    }

    private sealed class PageEntry
    {
        internal PageEntry(
            PdfDictionary pageDict,
            List<PdfIndirectObject> referencedObjects,
            int docIndex)
        {
            PageDict = pageDict;
            ReferencedObjects = referencedObjects;
            DocIndex = docIndex;
        }

        internal PdfDictionary PageDict { get; }
        internal List<PdfIndirectObject> ReferencedObjects { get; }
        internal int DocIndex { get; }
    }

    // A node in the re-based merged outline tree: a title, the merged (global)
    // destination page index (-1 when none), and nested children. Ids are
    // assigned at write time so sibling/child links can be wired before the
    // item dictionaries are built.
    private sealed class OutlineNode
    {
        internal OutlineNode(string title, int pageIndex, List<OutlineNode> children)
        {
            Title = title;
            PageIndex = pageIndex;
            Children = children;
        }

        internal string Title { get; }
        internal int PageIndex { get; }
        internal List<OutlineNode> Children { get; }
        internal PdfObjectId Id { get; set; }
    }

    // Allocates the root /Outlines id and one id per node, builds every item
    // dictionary against the merged page ids, appends them to allObjects, and
    // returns the root id. Mirrors OutlineWriter's First/Last/Next/Prev/Parent/
    // Count wiring (PDF 32000-1:2008 §12.3.3).
    private static PdfObjectId BuildOutlineObjects(
        List<OutlineNode> roots,
        List<PdfObjectId> pageIds,
        List<PdfIndirectObject> allObjects,
        ref int nextId)
    {
        PdfObjectId rootId = new PdfObjectId(nextId++, 0);
        AssignOutlineIds(roots, ref nextId);
        int topVisible = BuildOutlineLevel(roots, rootId, pageIds, allObjects);

        PdfDictionary outlines = new PdfDictionary();
        outlines.Set(PdfName.Type, PdfName.Outlines);

        if (roots.Count > 0)
        {
            outlines.Set(PdfName.Intern("First"), new PdfReference(roots[0].Id));
            outlines.Set(PdfName.Intern("Last"), new PdfReference(roots[^1].Id));
        }

        outlines.Set(PdfName.Count, topVisible);
        allObjects.Add(new PdfIndirectObject(rootId, outlines));
        return rootId;
    }

    // Pre-order id assignment so every Next/Prev/First/Last reference resolves.
    private static void AssignOutlineIds(List<OutlineNode> siblings, ref int nextId)
    {
        for (int i = 0; i < siblings.Count; i++)
        {
            siblings[i].Id = new PdfObjectId(nextId++, 0);
            AssignOutlineIds(siblings[i].Children, ref nextId);
        }
    }

    // Emits the item dictionaries for one sibling list; returns the count of
    // visible items at this level for the parent's /Count.
    private static int BuildOutlineLevel(
        List<OutlineNode> siblings,
        PdfObjectId parentId,
        List<PdfObjectId> pageIds,
        List<PdfIndirectObject> allObjects)
    {
        for (int i = 0; i < siblings.Count; i++)
        {
            OutlineNode node = siblings[i];
            PdfDictionary item = new PdfDictionary();

            item.Set(PdfName.Intern("Title"), EncodeOutlineTitle(node.Title));
            item.Set(PdfName.Parent, new PdfReference(parentId));

            if (i > 0)
            {
                item.Set(PdfName.Intern("Prev"), new PdfReference(siblings[i - 1].Id));
            }

            if (i < siblings.Count - 1)
            {
                item.Set(PdfName.Intern("Next"), new PdfReference(siblings[i + 1].Id));
            }

            // Explicit [pageRef /Fit] destination; skipped (title-only) when the
            // re-based index is unresolved or out of range, so a bad entry never
            // produces a dangling reference.
            if (node.PageIndex >= 0 && node.PageIndex < pageIds.Count)
            {
                PdfArray dest = new PdfArray([
                    new PdfReference(pageIds[node.PageIndex]),
                    PdfName.Intern("Fit"),
                ]);
                item.Set(PdfName.Intern("Dest"), dest);
            }

            if (node.Children.Count > 0)
            {
                int childVisible = BuildOutlineLevel(node.Children, node.Id, pageIds, allObjects);
                item.Set(PdfName.Intern("First"), new PdfReference(node.Children[0].Id));
                item.Set(PdfName.Intern("Last"), new PdfReference(node.Children[^1].Id));

                // Negative /Count means the sub-tree is closed (collapsed).
                item.Set(PdfName.Count, -childVisible);
            }

            allObjects.Add(new PdfIndirectObject(node.Id, item));
        }

        return siblings.Count;
    }

    // Encodes an outline title as a PDF text string: PDFDocEncoding (Latin-1)
    // when every character fits in a byte, otherwise UTF-16BE with a leading
    // FE FF byte-order mark (PDF 32000-1:2008 §7.9.2.2). This preserves Indic /
    // CJK / other non-Latin1 titles, which a plain Latin-1 string would lose.
    private static PdfString EncodeOutlineTitle(string title)
    {
        bool needsUtf16 = false;

        for (int i = 0; i < title.Length; i++)
        {
            if (title[i] > 0xFF)
            {
                needsUtf16 = true;
                break;
            }
        }

        if (!needsUtf16)
        {
            return new PdfString(title);
        }

        byte[] utf16 = Encoding.BigEndianUnicode.GetBytes(title);
        byte[] bytes = new byte[utf16.Length + 2];
        bytes[0] = 0xFE;
        bytes[1] = 0xFF;
        Array.Copy(utf16, 0, bytes, 2, utf16.Length);
        return new PdfString(bytes);
    }
}
