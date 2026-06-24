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

        // Page reference → index map and the named-destination table, both built
        // once and shared across the whole walk.
        Dictionary<int, int> pageRefToIndex = BuildPageReferenceMap(document);
        Dictionary<string, PdfPrimitive> namedDests = BuildNamedDestinations(catalog, store);

        List<OutlineItem> items = new List<OutlineItem>();
        HashSet<int> visited = new HashSet<int>();

        if (outlinesRoot.TryGetValue(PdfName.Intern("First"), out PdfPrimitive? firstPrim))
        {
            WalkSiblings(firstPrim, store, pageRefToIndex, namedDests, visited, items);
        }

        return items;
    }

    // ── Outline tree traversal ────────────────────────────────────────────

    private static void WalkSiblings(
        PdfPrimitive startPrim,
        PdfObjectStore store,
        Dictionary<int, int> pageMap,
        Dictionary<string, PdfPrimitive> namedDests,
        HashSet<int> visited,
        List<OutlineItem> result)
    {
        PdfPrimitive? current = startPrim;

        while (current is not null)
        {
            int objNum = current is PdfReference r ? r.ObjectId.ObjectNumber : -1;

            if (objNum > 0 && !visited.Add(objNum))
            {
                break; // cycle detected
            }

            PdfDictionary? dict = store.ResolveAs<PdfDictionary>(current);

            if (dict is null)
            {
                break;
            }

            string title = ExtractTitle(dict, store);
            int pageIndex = ResolveDestinationPageIndex(dict, store, pageMap, namedDests);

            // Recurse into children
            List<OutlineItem> children = new List<OutlineItem>();

            if (dict.TryGetValue(PdfName.Intern("First"), out PdfPrimitive? firstChild))
            {
                WalkSiblings(firstChild, store, pageMap, namedDests, visited, children);
            }

            result.Add(new OutlineItem(title, pageIndex, children));

            if (!dict.TryGetValue(PdfName.Intern("Next"), out PdfPrimitive? nextPrim))
            {
                break;
            }

            current = nextPrim;
        }
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

    private static int ResolveDestinationPageIndex(
        PdfDictionary outlineDict,
        PdfObjectStore store,
        Dictionary<int, int> pageMap,
        Dictionary<string, PdfPrimitive> namedDests)
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

        if (destPrim is null)
        {
            return -1;
        }

        PdfPrimitive resolved = store.Resolve(destPrim);

        // Named destination: a byte string keys the /Names /Dests name tree; a
        // name keys the legacy catalog /Dests dictionary. Look the dest up and
        // continue with the value it points at.
        if (resolved is PdfString nameString)
        {
            string key = System.Text.Encoding.Latin1.GetString(nameString.Bytes);
            if (!namedDests.TryGetValue(key, out PdfPrimitive? mapped))
            {
                return -1;
            }
            resolved = store.Resolve(mapped);
        }
        else if (resolved is PdfName name)
        {
            if (!namedDests.TryGetValue(name.Value, out PdfPrimitive? mapped))
            {
                return -1;
            }
            resolved = store.Resolve(mapped);
        }

        // A named destination may resolve to a dictionary that wraps the array
        // under /D (PDF 32000-1:2008 §12.3.2.3).
        if (resolved is PdfDictionary destDict &&
            destDict.TryGetValue(PdfName.Intern("D"), out PdfPrimitive? innerDest))
        {
            resolved = store.Resolve(innerDest);
        }

        if (resolved is PdfArray destArray && destArray.Count > 0)
        {
            return PageIndexFromDestArray(destArray, pageMap);
        }

        return -1;
    }

    // First element of a destination array is the target page: an indirect
    // reference (local destination) or, for remote/embedded go-to, an integer
    // page number.
    private static int PageIndexFromDestArray(PdfArray destArray, Dictionary<int, int> pageMap)
    {
        PdfPrimitive first = destArray[0];

        if (first is PdfReference pageRef &&
            pageMap.TryGetValue(pageRef.ObjectId.ObjectNumber, out int idx))
        {
            return idx;
        }

        if (first is PdfInteger pageNumber && pageNumber.Value >= 0)
        {
            return pageNumber.Value;
        }

        return -1;
    }

    // Flattens the catalog's named destinations (modern /Names /Dests name tree
    // and legacy /Dests dictionary) into a single name → destination map. Keys
    // are compared as raw (Latin-1) byte identifiers, matching how they appear
    // in /GoTo actions.
    private static Dictionary<string, PdfPrimitive> BuildNamedDestinations(
        PdfDictionary catalog, PdfObjectStore store)
    {
        Dictionary<string, PdfPrimitive> map = new Dictionary<string, PdfPrimitive>();

        if (catalog.TryGetValue(PdfName.Intern("Names"), out PdfPrimitive? namesPrim)
            && store.ResolveAs<PdfDictionary>(namesPrim) is PdfDictionary namesDict
            && namesDict.TryGetValue(PdfName.Intern("Dests"), out PdfPrimitive? destsTreePrim)
            && store.ResolveAs<PdfDictionary>(destsTreePrim) is PdfDictionary destsTree)
        {
            CollectNameTree(destsTree, store, map, 0);
        }

        if (catalog.TryGetValue(PdfName.Intern("Dests"), out PdfPrimitive? legacyPrim)
            && store.ResolveAs<PdfDictionary>(legacyPrim) is PdfDictionary legacy)
        {
            foreach (PdfName key in legacy.Keys)
            {
                if (!map.ContainsKey(key.Value))
                {
                    map[key.Value] = legacy[key];
                }
            }
        }

        return map;
    }

    // Recursively collects (key → value) pairs from a name-tree node: leaf
    // /Names arrays hold [key1 val1 key2 val2 …]; intermediate nodes hold /Kids.
    private static void CollectNameTree(
        PdfDictionary node,
        PdfObjectStore store,
        Dictionary<string, PdfPrimitive> map,
        int depth)
    {
        if (depth > 64)
        {
            return; // guard against malformed/cyclic trees
        }

        if (node.TryGetValue(PdfName.Intern("Names"), out PdfPrimitive? namesArrPrim)
            && store.ResolveAs<PdfArray>(namesArrPrim) is PdfArray namesArr)
        {
            for (int i = 0; i + 1 < namesArr.Count; i += 2)
            {
                if (store.Resolve(namesArr[i]) is PdfString keyStr)
                {
                    string key = System.Text.Encoding.Latin1.GetString(keyStr.Bytes);
                    if (!map.ContainsKey(key))
                    {
                        map[key] = namesArr[i + 1];
                    }
                }
            }
        }

        if (node.TryGetValue(PdfName.Intern("Kids"), out PdfPrimitive? kidsPrim)
            && store.ResolveAs<PdfArray>(kidsPrim) is PdfArray kids)
        {
            foreach (PdfPrimitive kid in kids)
            {
                if (store.ResolveAs<PdfDictionary>(kid) is PdfDictionary kidDict)
                {
                    CollectNameTree(kidDict, store, map, depth + 1);
                }
            }
        }
    }

    private static Dictionary<int, int> BuildPageReferenceMap(PdfDocument document)
    {
        Dictionary<int, int> map = new Dictionary<int, int>();
        int pageCount = document.PageCount;

        // Find the indirect object IDs for each page by walking the page tree
        // We use the fact that PdfObjectStore.Objects contains all loaded Page objects
        // (PreloadAllObjects must have been called previously, otherwise pages may be missing)
        int idx = 0;

        foreach (PdfIndirectObject obj in document.Objects.Objects)
        {
            if (obj.Value is not PdfDictionary dict)
            {
                continue;
            }

            if (!dict.TryGetValue(PdfName.Type, out PdfPrimitive? typePrim))
            {
                continue;
            }

            if (typePrim is PdfName typeName && typeName.Value == "Page")
            {
                map[obj.Id.ObjectNumber] = idx++;

                if (idx >= pageCount)
                {
                    break;
                }
            }
        }

        return map;
    }
}
