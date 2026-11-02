// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §14.7 — Logical structure; §14.8 — Tagged PDF
// PHASE: 3.29 — reading order from the structure tree (D-104)

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Chuvadi.Pdf.Content;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Text;

/// <summary>
/// Reads the order in which a Tagged PDF's structure tree visits each page's
/// marked content, so layout extraction can follow the author's reading order.
/// </summary>
internal static class TaggedOrder
{
    // The tree is used for a page only when the text it orders covers at
    // least this share of the page's characters.
    private const double MinCoverage = 0.8;

    // Structure trees larger than this are not walked.
    private const int MaxNodes = 1_000_000;

    // One walk per structure tree: for each page dictionary, MCID -> rank.
    private static readonly ConditionalWeakTable<PdfDictionary, Dictionary<PdfDictionary, Dictionary<int, int>>> Cache =
        new ConditionalWeakTable<PdfDictionary, Dictionary<PdfDictionary, Dictionary<int, int>>>();

    /// <summary>
    /// The rank of each of the page's marked-content identifiers in the
    /// structure tree's order, or null when the page has no usable tree or
    /// the tree orders too little of the page's text.
    /// </summary>
    internal static Dictionary<int, int>? Ranks(PdfPage page, IPdfObjectResolver objects, List<TextFragment> fragments)
    {
        PdfDictionary? root = page.StructTreeRoot;
        if (root is null)
        {
            return null;
        }

        Dictionary<PdfDictionary, Dictionary<int, int>> byPage;
        lock (Cache)
        {
            if (!Cache.TryGetValue(root, out Dictionary<PdfDictionary, Dictionary<int, int>>? walked))
            {
                walked = Walk(root, objects);
                Cache.AddOrUpdate(root, walked);
            }

            byPage = walked;
        }

        if (!byPage.TryGetValue(page.Dictionary, out Dictionary<int, int>? ranks) || ranks.Count == 0)
        {
            return null;
        }

        int total = 0;
        int ordered = 0;
        foreach (TextFragment f in fragments)
        {
            int chars = f.Text.Trim().Length;
            total += chars;
            if (f.MarkedContentId is int id && ranks.ContainsKey(id))
            {
                ordered += chars;
            }
        }

        return total > 0 && ordered >= MinCoverage * total ? ranks : null;
    }

    // Depth-first, in /K order: every marked-content reference with the
    // page it belongs to (its own /Pg, or the nearest element's).
    private static Dictionary<PdfDictionary, Dictionary<int, int>> Walk(PdfDictionary root, IPdfObjectResolver objects)
    {
        Dictionary<PdfDictionary, Dictionary<int, int>> byPage =
            new Dictionary<PdfDictionary, Dictionary<int, int>>(ReferenceEqualityComparer.Instance);
        HashSet<PdfDictionary> visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        Stack<(PdfPrimitive Node, PdfDictionary? Page)> stack = new Stack<(PdfPrimitive, PdfDictionary?)>();
        int rank = 0;
        int nodes = 0;

        if (root.TryGetValue(PdfName.Intern("K"), out PdfPrimitive? top))
        {
            stack.Push((top, null));
        }

        while (stack.Count > 0 && nodes++ < MaxNodes)
        {
            if ((nodes & 4095) == 0)
            {
                PdfWorkScope.ThrowIfCancellationRequested();
            }

            (PdfPrimitive node, PdfDictionary? page) = stack.Pop();
            PdfPrimitive value = objects.Resolve(node);
            switch (value)
            {
                case PdfInteger mcid:
                    Add(byPage, page, mcid.Value, ref rank);
                    break;

                case PdfArray array:
                    for (int i = array.Count - 1; i >= 0; i--)
                    {
                        stack.Push((array[i], page));
                    }

                    break;

                case PdfDictionary dict:
                    if (!visited.Add(dict))
                    {
                        break;
                    }

                    PdfDictionary? own = dict.TryGetValue(PdfName.Intern("Pg"), out PdfPrimitive? pg)
                        ? objects.Resolve(pg) as PdfDictionary
                        : null;
                    PdfDictionary? where = own ?? page;
                    string? type = (dict.TryGetValue(PdfName.Type, out PdfPrimitive? t) ? objects.Resolve(t) : null) is PdfName name
                        ? name.Value
                        : null;

                    if (type == "MCR")
                    {
                        // Content in a form XObject (/Stm) is not page content.
                        if (!dict.ContainsKey(PdfName.Intern("Stm"))
                            && dict.TryGetValue(PdfName.Intern("MCID"), out PdfPrimitive? id)
                            && objects.Resolve(id) is PdfInteger ref1)
                        {
                            Add(byPage, where, ref1.Value, ref rank);
                        }
                    }
                    else if (type != "OBJR" && dict.TryGetValue(PdfName.Intern("K"), out PdfPrimitive? kids))
                    {
                        stack.Push((kids, where));
                    }

                    break;
            }
        }

        return byPage;
    }

    private static void Add(Dictionary<PdfDictionary, Dictionary<int, int>> byPage, PdfDictionary? page, int mcid, ref int rank)
    {
        if (page is null || mcid < 0)
        {
            return;
        }

        if (!byPage.TryGetValue(page, out Dictionary<int, int>? ranks))
        {
            ranks = new Dictionary<int, int>();
            byPage[page] = ranks;
        }

        if (!ranks.ContainsKey(mcid))
        {
            ranks[mcid] = rank++;
        }
    }
}
