// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.3.2 — Destinations
//        PDF 32000-1:2008 §7.7.3  — Page tree
//        PDF 32000-1:2008 §7.9.6  — Name trees (named destinations)
// PHASE: 3.31.2 (D-207, D-219) — shared by outlines and link annotations;
//        3.32.0 (D-208 item 4) — kind and coordinates too
//
// Resolves a destination (explicit array, named destination, or a dictionary
// wrapping either under /D) to a zero-based page index. Page indexes come from
// walking the page tree with the same rule as PdfPageCollection, so they agree
// with PdfDocument.Pages. Before 3.31.2 both readers counted the /Page objects
// in the order the object store happened to hold them, and links ignored
// named destinations altogether.

using System.Collections.Generic;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Forms;

/// <summary>
/// Resolves destinations to page indexes for one document. Build one per
/// read and reuse it for every destination: the page map and the named
/// destinations are each built once, on first use.
/// </summary>
internal sealed class DestinationResolver
{
    // Deeper than any real page tree; a guard against cyclic /Kids.
    private const int MaxPageTreeDepth = 1024;

    // Deeper than any real name tree; a guard against cyclic /Kids.
    private const int MaxNameTreeDepth = 64;

    private readonly PdfDocument _document;
    private readonly PdfObjectStore _store;
    private Dictionary<int, int>? _pageMap;
    private Dictionary<string, PdfPrimitive>? _namedDests;

    /// <summary>Creates a resolver for <paramref name="document"/>.</summary>
    internal DestinationResolver(PdfDocument document)
    {
        _document = document;
        _store = document.Objects;
    }

    /// <summary>Page object number to zero-based page index, in page-tree order.</summary>
    internal Dictionary<int, int> PageMap => _pageMap ??= BuildPageTreeMap(_document);

    /// <summary>The named destinations: <c>/Names /Dests</c> first, then the catalog's <c>/Dests</c>.</summary>
    internal Dictionary<string, PdfPrimitive> NamedDestinations =>
        _namedDests ??= BuildNamedDestinations(_document.Catalog, _store);

    /// <summary>
    /// Returns the zero-based page index <paramref name="destination"/> points
    /// at, or -1 when it cannot be resolved.
    /// </summary>
    internal int ResolvePageIndex(PdfPrimitive destination) =>
        ResolveDestination(destination)?.PageIndex ?? -1;

    /// <summary>
    /// Resolves <paramref name="destination"/> (an explicit array, a named
    /// destination, or a dictionary wrapping either under <c>/D</c>) to its
    /// page, kind and coordinates (3.32.0), or null when the page cannot be
    /// resolved.
    /// </summary>
    internal PdfDestination? ResolveDestination(PdfPrimitive destination)
    {
        PdfPrimitive resolved = _store.Resolve(destination);

        // Named destination: a byte string keys the /Names /Dests name tree; a
        // name keys the legacy catalog /Dests dictionary.
        if (resolved is PdfString nameString)
        {
            string key = System.Text.Encoding.Latin1.GetString(nameString.Bytes);
            if (!NamedDestinations.TryGetValue(key, out PdfPrimitive? mapped))
            {
                return null;
            }

            resolved = _store.Resolve(mapped);
        }
        else if (resolved is PdfName name)
        {
            if (!NamedDestinations.TryGetValue(name.Value, out PdfPrimitive? mapped))
            {
                return null;
            }

            resolved = _store.Resolve(mapped);
        }

        // A named destination may resolve to a dictionary that wraps the array
        // under /D (PDF 32000-1:2008 §12.3.2.3).
        if (resolved is PdfDictionary destDict &&
            destDict.TryGetValue(PdfName.Intern("D"), out PdfPrimitive? innerDest))
        {
            resolved = _store.Resolve(innerDest);
        }

        if (resolved is PdfArray destArray && destArray.Count > 0)
        {
            int pageIndex = PageIndexFromDestArray(destArray);
            return pageIndex < 0 ? null : ReadView(destArray, pageIndex);
        }

        return null;
    }

    // Reads the kind and its operands (PDF 32000-1:2008 Table 151). A missing
    // or unknown kind reads as /XYZ with every value null: keep the view.
    private PdfDestination ReadView(PdfArray dest, int pageIndex)
    {
        string kind = dest.Count > 1 && _store.Resolve(dest[1]) is PdfName n ? n.Value : string.Empty;
        return kind switch
        {
            "Fit" => new PdfDestination(pageIndex, PdfDestinationKind.Fit),
            "FitB" => new PdfDestination(pageIndex, PdfDestinationKind.FitB),
            "FitH" => new PdfDestination(pageIndex, PdfDestinationKind.FitH) { Top = Number(dest, 2) },
            "FitBH" => new PdfDestination(pageIndex, PdfDestinationKind.FitBH) { Top = Number(dest, 2) },
            "FitV" => new PdfDestination(pageIndex, PdfDestinationKind.FitV) { Left = Number(dest, 2) },
            "FitBV" => new PdfDestination(pageIndex, PdfDestinationKind.FitBV) { Left = Number(dest, 2) },
            "FitR" => new PdfDestination(pageIndex, PdfDestinationKind.FitR)
            {
                Left = Number(dest, 2),
                Bottom = Number(dest, 3),
                Right = Number(dest, 4),
                Top = Number(dest, 5),
            },
            "XYZ" => new PdfDestination(pageIndex, PdfDestinationKind.XYZ)
            {
                Left = Number(dest, 2),
                Top = Number(dest, 3),
                Zoom = Number(dest, 4) is double z && z != 0 ? z : null,
            },
            _ => new PdfDestination(pageIndex, PdfDestinationKind.XYZ),
        };
    }

    // A number operand, or null for null, a missing operand or a non-number.
    private double? Number(PdfArray dest, int index)
    {
        if (index >= dest.Count)
        {
            return null;
        }

        double? value = _store.Resolve(dest[index]) switch
        {
            PdfInteger i => i.Value,
            PdfReal r => r.Value,
            _ => null,
        };
        return value is double v && double.IsFinite(v) ? v : null;
    }

    // First element of a destination array is the target page: an indirect
    // reference (local destination) or, for remote/embedded go-to, an integer
    // page number.
    private int PageIndexFromDestArray(PdfArray destArray)
    {
        PdfPrimitive first = destArray[0];

        if (first is PdfReference pageRef &&
            PageMap.TryGetValue(pageRef.ObjectId.ObjectNumber, out int idx))
        {
            return idx;
        }

        if (first is PdfInteger pageNumber && pageNumber.Value >= 0)
        {
            return pageNumber.Value;
        }

        return -1;
    }

    // Walks /Pages → /Kids as PdfPageCollection does: a kid of /Type /Page is
    // the next page, a kid of /Type /Pages is descended into, anything else is
    // skipped. A malformed tree ends the walk instead of throwing, keeping the
    // pages found so far; a node already seen is not walked again.
    private static Dictionary<int, int> BuildPageTreeMap(PdfDocument document)
    {
        Dictionary<int, int> map = new Dictionary<int, int>();
        PdfObjectStore store = document.Objects;

        if (!document.Catalog.TryGetValue(PdfName.Pages, out PdfPrimitive? rootPrim)
            || store.ResolveAs<PdfDictionary>(rootPrim) is not PdfDictionary root)
        {
            return map;
        }

        HashSet<int> seen = new HashSet<int>();
        if (rootPrim is PdfReference rootRef)
        {
            seen.Add(rootRef.ObjectId.ObjectNumber);
        }

        int pageCount = 0;
        WalkPageTree(root, store, map, seen, ref pageCount, 0);
        return map;
    }

    private static void WalkPageTree(
        PdfDictionary node,
        PdfObjectStore store,
        Dictionary<int, int> map,
        HashSet<int> seen,
        ref int pageCount,
        int depth)
    {
        if (depth > MaxPageTreeDepth
            || !node.TryGetValue(PdfName.Kids, out PdfPrimitive? kidsPrim)
            || store.ResolveAs<PdfArray>(kidsPrim) is not PdfArray kids)
        {
            return;
        }

        foreach (PdfPrimitive kidPrim in kids)
        {
            if (store.ResolveAs<PdfDictionary>(kidPrim) is not PdfDictionary kid)
            {
                continue;
            }

            PdfName? kidType = kid.GetName(PdfName.Type);
            if (kidType == PdfName.Page)
            {
                // Every page takes the next index, as in PdfPageCollection; a
                // direct (unreferenced) page dictionary takes an index but has
                // no entry, since nothing can refer to it.
                if (kidPrim is PdfReference pageRef)
                {
                    map.TryAdd(pageRef.ObjectId.ObjectNumber, pageCount);
                }

                pageCount++;
            }
            else if (kidType == PdfName.Pages
                && (kidPrim is not PdfReference nodeRef || seen.Add(nodeRef.ObjectId.ObjectNumber)))
            {
                WalkPageTree(kid, store, map, seen, ref pageCount, depth + 1);
            }
        }
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
        if (depth > MaxNameTreeDepth)
        {
            return;
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
}
