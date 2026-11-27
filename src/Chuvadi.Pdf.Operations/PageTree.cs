// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.7.3 — Page tree
// PHASE: Document operations — shared page-tree traversal.

using System.Collections.Generic;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// Shared page-tree traversal: maps a zero-based page index to the object id of
/// the /Page dictionary holding it, by walking /Kids in document order (robust
/// to nested page-tree nodes). Centralised so operations that must reference a
/// page by id (outlines, stamping) share one correct implementation.
/// </summary>
internal static class PageTree
{
    /// <summary>
    /// Builds a map from zero-based page index to the object id of that page,
    /// walking the page tree from the catalog's /Pages root.
    /// </summary>
    internal static Dictionary<int, PdfObjectId> BuildIndexToIdMap(PdfDocument document)
    {
        Dictionary<int, PdfObjectId> map = new Dictionary<int, PdfObjectId>();
        int counter = 0;

        PdfDictionary catalog = document.Catalog;
        if (!catalog.TryGetValue(PdfName.Pages, out PdfPrimitive? pagesPrim))
        {
            return map;
        }

        PdfDictionary? root = document.Objects.ResolveAs<PdfDictionary>(pagesPrim);
        if (root is not null)
        {
            Walk(document.Objects, root, map, ref counter, new HashSet<int>());
        }

        return map;
    }

    // Deepest nesting followed (3.35.0, DOC-01).
    private const int MaxPageTreeDepth = 1024;

    private static void Walk(
        PdfObjectStore objects,
        PdfDictionary node,
        Dictionary<int, PdfObjectId> map,
        ref int counter,
        HashSet<int> visited,
        int depth = 0)
    {
        // 3.36.0 (D-430): iterative pre-order walk with an explicit stack, so a
        // deep page tree cannot exhaust the call stack. Same visit order, cycle
        // guard (visited) and MaxPageTreeDepth bound as the former recursion.
        Stack<WalkFrame> stack = new Stack<WalkFrame>();
        PushNode(stack, objects, node, depth);

        while (stack.Count > 0)
        {
            WalkFrame frame = stack.Peek();
            if (frame.Index >= frame.Kids.Count)
            {
                stack.Pop();
                continue;
            }

            PdfPrimitive kidPrim = frame.Kids[frame.Index];
            frame.Index++;

            if (kidPrim is not PdfReference kidRef)
            {
                continue;
            }

            int num = kidRef.ObjectId.ObjectNumber;
            if (!visited.Add(num))
            {
                continue;
            }

            if (objects.Resolve(kidRef) is not PdfDictionary kid)
            {
                continue;
            }

            if (kid.TryGetValue(PdfName.Type, out PdfPrimitive? typePrim)
                && typePrim is PdfName typeName && typeName.Value == "Pages")
            {
                PushNode(stack, objects, kid, frame.Depth + 1);
            }
            else
            {
                map[counter++] = kidRef.ObjectId;
            }
        }
    }

    // Pushes a /Pages node's /Kids as a new frame, unless it is past the depth
    // bound (3.35.0, DOC-01) or has no resolvable /Kids array.
    private static void PushNode(
        Stack<WalkFrame> stack, PdfObjectStore objects, PdfDictionary node, int depth)
    {
        if (depth > MaxPageTreeDepth
            || !node.TryGetValue(PdfName.Kids, out PdfPrimitive? kidsPrim)
            || objects.Resolve(kidsPrim) is not PdfArray kids)
        {
            return;
        }

        stack.Push(new WalkFrame(kids, depth));
    }

    private sealed class WalkFrame
    {
        internal WalkFrame(PdfArray kids, int depth)
        {
            Kids = kids;
            Depth = depth;
        }

        internal PdfArray Kids { get; }

        internal int Depth { get; }

        internal int Index { get; set; }
    }
}
