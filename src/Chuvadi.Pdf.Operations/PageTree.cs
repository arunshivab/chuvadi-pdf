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

    private static void Walk(
        PdfObjectStore objects,
        PdfDictionary node,
        Dictionary<int, PdfObjectId> map,
        ref int counter,
        HashSet<int> visited)
    {
        if (!node.TryGetValue(PdfName.Kids, out PdfPrimitive? kidsPrim))
        {
            return;
        }

        if (objects.Resolve(kidsPrim) is not PdfArray kids)
        {
            return;
        }

        for (int i = 0; i < kids.Count; i++)
        {
            if (kids[i] is not PdfReference kidRef)
            {
                continue;
            }

            int num = kidRef.ObjectId.ObjectNumber;
            if (!visited.Add(num))
            {
                continue;
            }

            PdfPrimitive resolved = objects.Resolve(kidRef);
            if (resolved is not PdfDictionary kid)
            {
                continue;
            }

            if (kid.TryGetValue(PdfName.Type, out PdfPrimitive? typePrim)
                && typePrim is PdfName typeName && typeName.Value == "Pages")
            {
                Walk(objects, kid, map, ref counter, visited);
            }
            else
            {
                map[counter++] = kidRef.ObjectId;
            }
        }
    }
}
