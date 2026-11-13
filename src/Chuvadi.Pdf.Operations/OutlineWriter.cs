// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.3.3 — Document outline; §12.3.2.2 — Explicit destinations
// PHASE: Document operations — write an outline (bookmarks) onto a document.
//        3.33.0 (PDF-17, D-322): exact destinations, and bookmarks kept from
//        the source outline written back with their target and appearance.

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Forms;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// Writes a document outline (bookmark tree) onto an existing document,
/// replacing any existing outline. Nested children are supported to any
/// depth. The rest of the document is preserved unchanged.
/// PDF 32000-1:2008 §12.3.3 — Document outline.
/// </summary>
/// <remarks>
/// Where each bookmark goes depends on how its <see cref="OutlineEntry"/> was
/// made (3.33.0):
/// <list type="bullet">
/// <item>with a page index: the whole page (<c>[page /Fit]</c>), closed, as
/// before 3.33.0;</item>
/// <item>with a <see cref="PdfDestination"/>: exactly that destination;</item>
/// <item>with a source <see cref="OutlineItem"/>: the source bookmark's
/// <c>/Dest</c> or <c>/A</c> (named destinations and every kind of action),
/// colour <c>/C</c>, style <c>/F</c>, structure element <c>/SE</c> and open
/// or closed state, unchanged.</item>
/// </list>
/// </remarks>
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
    /// <exception cref="ArgumentException">
    /// An entry's <see cref="OutlineEntry.Source"/> was not read from <paramref name="document"/>.
    /// </exception>
    public static void Apply(
        Stream output,
        PdfDocument document,
        IReadOnlyList<OutlineEntry> entries)
    {
        PdfWriteGuard.Run(document, "updated", () =>
        {
            ArgumentNullException.ThrowIfNull(output);
            ArgumentNullException.ThrowIfNull(document);
            ArgumentNullException.ThrowIfNull(entries);
            CheckSources(entries, document);

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
        });
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

    // Emits item dictionaries for one sibling list, returns the number of
    // items visible when the parent is open (this level, plus the visible
    // descendants of open items) for the parent's /Count.
    private static int BuildLevel(
        DocumentRewriter rewriter,
        PdfDocument document,
        Dictionary<int, PdfObjectId> pageIds,
        List<EntryNode> siblings,
        PdfObjectId parentId)
    {
        int visible = siblings.Count;
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

            bool open = false;
            if (node.Entry.Source?.SourceDictionary is PdfDictionary source)
            {
                // A kept bookmark: its target and appearance, unchanged. The
                // values keep their references, which DocumentRewriter leaves
                // at the same object numbers.
                foreach (string key in KeptKeys)
                {
                    if (source.TryGetValue(PdfName.Intern(key), out PdfPrimitive? value))
                    {
                        item.Set(PdfName.Intern(key), value);
                    }
                }

                open = source.TryGetValue(PdfName.Count, out PdfPrimitive? count)
                    && document.Objects.Resolve(count) is PdfInteger n
                    && n.Value > 0;
            }
            else if (pageIds.TryGetValue(node.Entry.PageIndex, out PdfObjectId pageId))
            {
                // Explicit destination: the exact one given, else [pageRef
                // /Fit]. Skip /Dest when the page index is out of range so a
                // bad entry never produces a dangling reference (the bookmark
                // still appears, just without a target).
                item.Set(PdfName.Intern("Dest"), node.Entry.Destination is PdfDestination exact
                    ? ExplicitDestination(pageId, exact)
                    : new PdfArray([new PdfReference(pageId), PdfName.Intern("Fit")]));
            }

            if (node.Children.Count > 0)
            {
                int childVisible = BuildLevel(rewriter, document, pageIds, node.Children, node.Id);
                item.Set(PdfName.Intern("First"), new PdfReference(node.Children[0].Id));
                item.Set(PdfName.Intern("Last"), new PdfReference(node.Children[^1].Id));

                // A positive /Count means the sub-tree is open, a negative one
                // that it is closed (collapsed); either way its size is the
                // number of descendants visible when it is open.
                item.Set(PdfName.Count, open ? childVisible : -childVisible);
                if (open)
                {
                    visible += childVisible;
                }
            }

            rewriter.AddObject(node.Id, item);
        }

        return visible;
    }

    // The entries of a kept bookmark written back unchanged (3.33.0, D-322).
    private static readonly string[] KeptKeys = { "Dest", "A", "C", "F", "SE" };

    // [page /Kind values...] (PDF 32000-1:2008 Table 151); a null value is
    // written as null ("keep the current value").
    private static PdfArray ExplicitDestination(PdfObjectId pageId, PdfDestination d)
    {
        List<PdfPrimitive> items = new List<PdfPrimitive> { new PdfReference(pageId) };
        switch (d.Kind)
        {
            case PdfDestinationKind.Fit:
                items.Add(PdfName.Intern("Fit"));
                break;
            case PdfDestinationKind.FitB:
                items.Add(PdfName.Intern("FitB"));
                break;
            case PdfDestinationKind.FitH:
                items.Add(PdfName.Intern("FitH"));
                items.Add(Number(d.Top));
                break;
            case PdfDestinationKind.FitBH:
                items.Add(PdfName.Intern("FitBH"));
                items.Add(Number(d.Top));
                break;
            case PdfDestinationKind.FitV:
                items.Add(PdfName.Intern("FitV"));
                items.Add(Number(d.Left));
                break;
            case PdfDestinationKind.FitBV:
                items.Add(PdfName.Intern("FitBV"));
                items.Add(Number(d.Left));
                break;
            case PdfDestinationKind.FitR:
                items.Add(PdfName.Intern("FitR"));
                items.Add(Number(d.Left));
                items.Add(Number(d.Bottom));
                items.Add(Number(d.Right));
                items.Add(Number(d.Top));
                break;
            default:
                items.Add(PdfName.Intern("XYZ"));
                items.Add(Number(d.Left));
                items.Add(Number(d.Top));
                items.Add(Number(d.Zoom));
                break;
        }

        return new PdfArray(items);
    }

    private static PdfPrimitive Number(double? value) =>
        value is double v ? new PdfReal(v) : PdfNull.Value;

    // Every kept bookmark must come from the document being written: its
    // values refer to that document's objects.
    private static void CheckSources(IReadOnlyList<OutlineEntry> entries, PdfDocument document)
    {
        foreach (OutlineEntry entry in entries)
        {
            if (entry.Source is OutlineItem source && !ReferenceEquals(source.SourceDocument, document))
            {
                throw new ArgumentException(
                    $"The bookmark '{entry.Title}' was kept from an outline item that was not read from this document.",
                    nameof(entries));
            }

            CheckSources(entry.Children, document);
        }
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
