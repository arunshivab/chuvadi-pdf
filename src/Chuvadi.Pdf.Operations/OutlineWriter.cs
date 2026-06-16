// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.3.3 — Document outline; §12.3.2.2 — Explicit destinations
// PHASE: Document operations — write an outline (bookmarks) onto a document.

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// Writes a document outline (bookmark tree) onto an existing document,
/// replacing any existing outline. Each entry targets a page by zero-based
/// index using an explicit <c>/Fit</c> destination. Nested children are
/// supported to any depth. The rest of the document is preserved unchanged.
/// PDF 32000-1:2008 §12.3.3 — Document outline.
/// </summary>
public static class OutlineWriter
{
    /// <summary>
    /// Writes <paramref name="entries"/> as the document outline and emits the
    /// result to <paramref name="output"/>. An empty list writes no outline
    /// (and removes any existing one from the catalog).
    /// </summary>
    /// <param name="output">The stream to write the updated PDF to.</param>
    /// <param name="document">The source document.</param>
    /// <param name="entries">The top-level bookmarks, in order.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when any argument is null.
    /// </exception>
    public static void Apply(
        Stream output,
        PdfDocument document,
        IReadOnlyList<OutlineEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(entries);

        DocumentRewriter rewriter = new DocumentRewriter(document);
        Dictionary<int, PdfObjectId> pageIds = PageTree.BuildIndexToIdMap(document);

        PdfDictionary catalog = rewriter.CopyCatalog();

        if (entries.Count == 0)
        {
            catalog.Remove(PdfName.Outlines);
            rewriter.ReplaceObject(rewriter.CatalogId(), catalog);
            rewriter.Write(output);
            return;
        }

        // Allocate the root /Outlines id and an id for every entry up-front so
        // sibling/child links can reference them before the dicts are built.
        PdfObjectId rootId = rewriter.AllocateId();
        List<EntryNode> nodes = new List<EntryNode>();
        AssignIds(rewriter, entries, nodes);

        // Build each item dictionary with First/Last/Next/Prev/Parent/Count.
        int topVisible = BuildLevel(rewriter, document, pageIds, nodes, rootId);

        // Root /Outlines dictionary.
        PdfDictionary outlines = new PdfDictionary();
        outlines.Set(PdfName.Type, PdfName.Outlines);
        if (nodes.Count > 0)
        {
            outlines.Set(PdfName.Intern("First"), new PdfReference(nodes[0].Id));
            outlines.Set(PdfName.Intern("Last"), new PdfReference(nodes[^1].Id));
        }

        outlines.Set(PdfName.Count, topVisible);
        rewriter.AddObject(rootId, outlines);

        catalog.Set(PdfName.Outlines, new PdfReference(rootId));
        rewriter.ReplaceObject(rewriter.CatalogId(), catalog);

        rewriter.Write(output);
    }

    // Recursively allocates an id for each entry in a sibling list.
    private static void AssignIds(
        DocumentRewriter rewriter,
        IReadOnlyList<OutlineEntry> entries,
        List<EntryNode> siblings)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            EntryNode node = new EntryNode(entries[i], rewriter.AllocateId());
            AssignIds(rewriter, entries[i].Children, node.Children);
            siblings.Add(node);
        }
    }

    // Emits item dictionaries for one sibling list, returns the count of
    // visible (this level) items for the parent's /Count.
    private static int BuildLevel(
        DocumentRewriter rewriter,
        PdfDocument document,
        Dictionary<int, PdfObjectId> pageIds,
        List<EntryNode> siblings,
        PdfObjectId parentId)
    {
        for (int i = 0; i < siblings.Count; i++)
        {
            EntryNode node = siblings[i];
            PdfDictionary item = new PdfDictionary();

            item.Set(PdfName.Intern("Title"), new PdfString(node.Entry.Title));
            item.Set(PdfName.Parent, new PdfReference(parentId));

            if (i > 0)
            {
                item.Set(PdfName.Intern("Prev"), new PdfReference(siblings[i - 1].Id));
            }

            if (i < siblings.Count - 1)
            {
                item.Set(PdfName.Intern("Next"), new PdfReference(siblings[i + 1].Id));
            }

            // Explicit destination: [pageRef /Fit]. Skip /Dest when the page
            // index is out of range so a bad entry never produces a dangling
            // reference (the bookmark still appears, just without a target).
            if (pageIds.TryGetValue(node.Entry.PageIndex, out PdfObjectId pageId))
            {
                PdfArray dest = new PdfArray([
                    new PdfReference(pageId),
                    PdfName.Intern("Fit")
                ]);
                item.Set(PdfName.Intern("Dest"), dest);
            }

            if (node.Children.Count > 0)
            {
                int childVisible = BuildLevel(rewriter, document, pageIds, node.Children, node.Id);
                item.Set(PdfName.Intern("First"), new PdfReference(node.Children[0].Id));
                item.Set(PdfName.Intern("Last"), new PdfReference(node.Children[^1].Id));

                // Negative /Count means the sub-tree is closed (collapsed).
                item.Set(PdfName.Count, -childVisible);
            }

            rewriter.AddObject(node.Id, item);
        }

        return siblings.Count;
    }

    private sealed class EntryNode
    {
        internal EntryNode(OutlineEntry entry, PdfObjectId id)
        {
            Entry = entry;
            Id = id;
        }

        internal OutlineEntry Entry { get; }

        internal PdfObjectId Id { get; }

        internal List<EntryNode> Children { get; } = new List<EntryNode>();
    }
}
