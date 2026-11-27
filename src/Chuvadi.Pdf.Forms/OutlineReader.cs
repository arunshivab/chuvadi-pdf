// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.3.3 — Document outline
//        PDF 32000-1:2008 §12.3.2 — Destinations
//        PDF 32000-1:2008 §7.9.6  — Name trees (named destinations)
// PHASE: Phase 2 — Chuvadi.Pdf.Forms
// Reads the document outline (bookmark) tree.

using System.Collections.Generic;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Forms;

/// <summary>
/// Reads the document outline (bookmark) tree from a PDF.
/// </summary>
/// <remarks>
/// Walks from <c>/Catalog/Outlines/First</c> through each item's
/// <c>/Next</c> and <c>/First</c> pointers, building a tree of
/// <see cref="OutlineItem"/> values. Titles are decoded per the PDF text-string
/// rules (UTF-16BE / UTF-16LE / UTF-8 BOM, else PDFDocEncoding) and
/// destinations — explicit arrays, <c>/GoTo</c> actions, and named
/// destinations resolved through the <c>/Names /Dests</c> name tree or the
/// legacy catalog <c>/Dests</c> dictionary — are resolved to zero-based page
/// indices where possible.
///
/// PDF 32000-1:2008 §12.3.3 — Document outline.
/// </remarks>
public static class OutlineReader
{
    /// <summary>
    /// Returns the top-level outline items. Empty when the document has
    /// no bookmarks.
    /// </summary>
    public static IReadOnlyList<OutlineItem> GetOutlines(PdfDocument document)
    {
        if (document is null)
        {
            throw new System.ArgumentNullException(nameof(document));
        }

        PdfDictionary catalog = document.Catalog;
        PdfObjectStore store = document.Objects;

        if (!catalog.TryGetValue(PdfName.Outlines, out PdfPrimitive? outlinesPrim))
        {
            return new List<OutlineItem>();
        }

        PdfDictionary? outlinesRoot = store.ResolveAs<PdfDictionary>(outlinesPrim ?? PdfNull.Value);

        if (outlinesRoot is null)
        {
            return new List<OutlineItem>();
        }

        // One resolver for the whole walk: the page map (from the page tree,
        // since 3.31.2) and the named destinations are each built once.
        DestinationResolver destinations = new DestinationResolver(document);

        List<OutlineItem> items = new List<OutlineItem>();
        HashSet<int> visited = new HashSet<int>();

        if (outlinesRoot.TryGetValue(PdfName.Intern("First"), out PdfPrimitive? firstPrim))
        {
            WalkSiblings(document, firstPrim, store, destinations, visited, items);
        }

        return items;
    }

    // ── Outline tree traversal ────────────────────────────────────────────

    // Deepest bookmark nesting read (3.35.0, DOC-01).
    private const int MaxDepth = 256;

    private static void WalkSiblings(
        PdfDocument document,
        PdfPrimitive startPrim,
        PdfObjectStore store,
        DestinationResolver destinations,
        HashSet<int> visited,
        List<OutlineItem> result,
        int depth = 0)
    {
        // 3.36.0 (D-430): iterative with an explicit continuation stack, so an
        // outline nested up to MaxDepth cannot exhaust the call stack. Siblings
        // are read in order; an item is created only after its children are
        // gathered (as before); the cycle guard, depth bound and item shape are
        // unchanged.
        Stack<SiblingFrame> stack = new Stack<SiblingFrame>();
        stack.Push(new SiblingFrame(startPrim, result, depth));

        while (stack.Count > 0)
        {
            SiblingFrame frame = stack.Peek();

            if (frame.AwaitingChild)
            {
                // The child walk for the sibling we paused on has finished; its
                // items are in frame.PendingChildren. Create that sibling's item
                // and advance to its /Next.
                frame.Result.Add(new OutlineItem(
                    frame.PendingTitle, frame.PendingDestination?.PageIndex ?? -1, frame.PendingChildren)
                {
                    Destination = frame.PendingDestination,
                    SourceDictionary = frame.PendingDict,
                    SourceDocument = document,
                });
                frame.AwaitingChild = false;
                frame.Current = frame.PendingDict!.TryGetValue(PdfName.Intern("Next"), out PdfPrimitive? nextAfter)
                    ? nextAfter
                    : null;
                continue;
            }

            if (frame.Current is null)
            {
                stack.Pop();
                continue;
            }

            PdfPrimitive current = frame.Current;
            int objNum = current is PdfReference r ? r.ObjectId.ObjectNumber : -1;

            if (objNum > 0 && !visited.Add(objNum))
            {
                frame.Current = null; // cycle detected: end this sibling chain
                continue;
            }

            PdfDictionary? dict = store.ResolveAs<PdfDictionary>(current);

            if (dict is null)
            {
                frame.Current = null;
                continue;
            }

            string title = ExtractTitle(dict, store);
            PdfDestination? destination = ResolveDestination(dict, store, destinations);

            // 3.35.0 (DOC-01): bookmarks nested deeper than MaxDepth levels
            // are not read; before, a deep enough nesting ended the process.
            if (frame.Depth < MaxDepth && dict.TryGetValue(PdfName.Intern("First"), out PdfPrimitive? firstChild))
            {
                frame.AwaitingChild = true;
                frame.PendingTitle = title;
                frame.PendingDestination = destination;
                frame.PendingDict = dict;
                frame.PendingChildren = new List<OutlineItem>();
                stack.Push(new SiblingFrame(firstChild, frame.PendingChildren, frame.Depth + 1));
                continue;
            }

            frame.Result.Add(new OutlineItem(title, destination?.PageIndex ?? -1, new List<OutlineItem>())
            {
                Destination = destination,
                SourceDictionary = dict,
                SourceDocument = document,
            });

            frame.Current = dict.TryGetValue(PdfName.Intern("Next"), out PdfPrimitive? nextPrim)
                ? nextPrim
                : null;
        }
    }

    // One sibling chain being walked: the current sibling, the list its items go
    // into, and its nesting depth. When a sibling has children, the frame pauses
    // (AwaitingChild) with that sibling's details until the child walk completes.
    private sealed class SiblingFrame
    {
        internal SiblingFrame(PdfPrimitive? current, List<OutlineItem> result, int depth)
        {
            Current = current;
            Result = result;
            Depth = depth;
        }

        internal PdfPrimitive? Current { get; set; }

        internal List<OutlineItem> Result { get; }

        internal int Depth { get; }

        internal bool AwaitingChild { get; set; }

        internal string PendingTitle { get; set; } = string.Empty;

        internal PdfDestination? PendingDestination { get; set; }

        internal PdfDictionary? PendingDict { get; set; }

        internal List<OutlineItem> PendingChildren { get; set; } = new List<OutlineItem>();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string ExtractTitle(PdfDictionary outlineDict, PdfObjectStore store)
    {
        if (!outlineDict.TryGetValue(PdfName.Intern("Title"), out PdfPrimitive? titlePrim))
        {
            return string.Empty;
        }

        // Decode per PDF text-string rules (FE FF → UTF-16BE, FF FE → UTF-16LE,
        // EF BB BF → UTF-8, else PDFDocEncoding) rather than treating the raw
        // bytes as Latin-1, which renders FE-FF titles as "þÿ"-prefixed garbage.
        if (store.ResolveAs<PdfString>(titlePrim) is PdfString s)
        {
            return s.ToTextString();
        }

        return string.Empty;
    }

    private static PdfDestination? ResolveDestination(
        PdfDictionary outlineDict,
        PdfObjectStore store,
        DestinationResolver destinations)
    {
        // /Dest is an explicit destination ([pageRef /XYZ …]) or a named one (a
        // name/string keying the /Dests tables). /A is an action dictionary
        // whose /D may carry either form.
        PdfPrimitive? destPrim = null;

        if (outlineDict.TryGetValue(PdfName.Intern("Dest"), out PdfPrimitive? d))
        {
            destPrim = d;
        }
        else if (outlineDict.TryGetValue(PdfName.Intern("A"), out PdfPrimitive? actionPrim))
        {
            PdfDictionary? actionDict = store.ResolveAs<PdfDictionary>(actionPrim ?? PdfNull.Value);

            if (actionDict is not null &&
                actionDict.TryGetValue(PdfName.Intern("D"), out PdfPrimitive? actionDest))
            {
                destPrim = actionDest;
            }
        }

        return destPrim is null ? null : destinations.ResolveDestination(destPrim);
    }
}
