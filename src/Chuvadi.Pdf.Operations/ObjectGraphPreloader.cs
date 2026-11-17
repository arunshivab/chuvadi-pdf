// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.3.10 (indirect objects)
// BASELINE B16 — preload the reachable object graph before a rewrite.
//
// PdfObjectStore is lazy: it only contains objects something has already
// resolved. Any writer that re-emits "all objects" without first walking the
// graph silently drops everything the operation never happened to touch —
// annotations, fonts, outlines. Every rewrite entry point in this assembly
// must call Preload before enumerating the store.

using System.Collections.Generic;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// Forces every object reachable from a document's catalog, pages and info
/// dictionary into the (lazy) object store, so a rewrite that enumerates the
/// store sees the whole document.
/// </summary>
internal static class ObjectGraphPreloader
{
    /// <summary>Resolves the full reachable object graph of <paramref name="document"/>.</summary>
    internal static void Preload(PdfDocument document)
    {
        HashSet<int> visited = new HashSet<int>();

        Visit(document.Objects, document.Catalog, visited);

        for (int i = 0; i < document.PageCount; i++)
        {
            Visit(document.Objects, document.Pages[i].Dictionary, visited);
        }

        if (document.Trailer.TryGetValue(PdfName.Intern("Info"), out PdfPrimitive? info))
        {
            Visit(document.Objects, info, visited);
        }
    }

    // Iterative (3.35.0, DOC-01/OPS-08): a long chain of linked objects
    // (bookmarks, annotations through /Next) cannot exhaust the stack.
    private static void Visit(PdfObjectStore store, PdfPrimitive? primitive, HashSet<int> visited)
    {
        if (primitive is null)
        {
            return;
        }

        Stack<PdfPrimitive> pending = new Stack<PdfPrimitive>();
        pending.Push(primitive);
        while (pending.Count > 0)
        {
            switch (pending.Pop())
            {
                case PdfReference reference:
                    if (visited.Add(reference.ObjectId.ObjectNumber))
                    {
                        pending.Push(store.Resolve(reference));
                    }

                    break;
                case PdfArray array:
                    for (int i = array.Count - 1; i >= 0; i--)
                    {
                        pending.Push(array[i]);
                    }

                    break;
                case PdfStream stream:
                    pending.Push(stream.Dictionary);
                    break;
                case PdfDictionary dictionary:
                    foreach (KeyValuePair<PdfName, PdfPrimitive> entry in dictionary)
                    {
                        pending.Push(entry.Value);
                    }

                    break;
            }
        }
    }

}
