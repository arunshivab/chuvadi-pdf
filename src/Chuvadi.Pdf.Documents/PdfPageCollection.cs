// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.7.3 — Page tree
// PHASE: Phase 1 — Chuvadi.Pdf.Documents
// Lazy, index-based access to a PDF document's pages.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Documents;

/// <summary>
/// Provides lazy, random-access to the pages of a PDF document.
/// </summary>
/// <remarks>
/// The PDF page tree is a balanced tree of /Pages nodes with /Page leaves.
/// <see cref="PdfPageCollection"/> traverses this tree on demand, caching
/// resolved pages after the first access.
///
/// <see cref="Count"/> is read directly from the root /Pages node's /Count
/// entry — it does not require traversing the tree.
///
/// PDF 32000-1:2008 §7.7.3 — Page tree.
/// </remarks>
public sealed class PdfPageCollection : IReadOnlyList<PdfPage>
{
    /// <summary>
    /// Maximum depth of recursion through the page tree. PDF 32000-1 does
    /// not mandate a maximum, but real-world documents use depth 1–5; even
    /// large balanced trees stay well under this limit. Any document that
    /// exceeds it is either malformed (cyclic /Kids reference) or pathological
    /// (deliberately deep to exhaust the stack). Either way we reject it.
    /// </summary>
    private const int MaxPageTreeDepth = 1024;

    private readonly PdfDictionary _pagesRoot;
    private readonly IPdfObjectResolver _resolver;
    private readonly Dictionary<int, PdfPage> _cache = new Dictionary<int, PdfPage>();
    private readonly int _count;
    private readonly Func<OptionalContentVisibility>? _optionalContent;
    private readonly Func<PdfDictionary?>? _structTreeRoot;
    private readonly Func<PdfDictionary?>? _acroForm;
    private readonly Action<string>? _warn;

    // 3.34.0 (#52g): the pages found by walking a page tree that loops back
    // on itself, when the document was opened with RepairIfDamaged; null
    // otherwise (pages are found through /Count).
    private readonly List<PdfDictionary>? _walked;

    // 3.34.0 (#52g): the warning for a /Kids entry that leads back to one of
    // its own ancestors.
    private const string LoopWarning =
        "The page tree loops back on itself: a /Kids entry leads to one of its own ancestors. That entry was skipped.";

    private const string NoKidsWarning =
        "A page tree node has no /Kids array; it holds no pages.";

    private static string BadKidWarning(int index) =>
        string.Create(CultureInfo.InvariantCulture, $"Page tree /Kids[{index}] is not a dictionary; that entry was skipped.");

    internal PdfPageCollection(
        PdfDictionary pagesRoot, IPdfObjectResolver resolver,
        Func<OptionalContentVisibility>? optionalContent = null,
        Func<PdfDictionary?>? structTreeRoot = null,
        Func<PdfDictionary?>? acroForm = null,
        Action<string>? warn = null,
        bool walkIfLooped = false)
    {
        _structTreeRoot = structTreeRoot;
        _acroForm = acroForm;
        _pagesRoot = pagesRoot ?? throw new ArgumentNullException(nameof(pagesRoot));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _optionalContent = optionalContent;
        _warn = warn;

        // /Count is untrusted input: a negative value counts as no pages, and
        // nothing is allocated per page until it is accessed.
        _count = Math.Max(0, _pagesRoot.GetInteger(PdfName.Count, 0));

        if (walkIfLooped)
        {
            // 3.34.0 (#52g): with RepairIfDamaged the tree is walked once;
            // when it loops, the pages found are the document's pages and
            // /Count is not used.
            bool looped = false;
            List<PdfDictionary> leaves = new List<PdfDictionary>();
            try
            {
                foreach (PdfDictionary leaf in WalkPageDictionaries(_pagesRoot, 0, new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance), () => looped = true))
                {
                    leaves.Add(leaf);
                }
            }
            catch (PdfCorruptionException)
            {
                // Damaged in another way: pages are found through /Count, as
                // without RepairIfDamaged, and fail when they are reached.
                looped = false;
            }

            if (looped)
            {
                _walked = leaves;
                _warn?.Invoke(string.Create(CultureInfo.InvariantCulture,
                    $"The page tree loops back on itself; the looping /Kids entries were skipped and the pages counted by walking the tree: {leaves.Count} page(s), where /Count says {_count}."));
                _count = leaves.Count;
            }
        }
    }

    /// <summary>Gets the total number of pages in the document.</summary>
    public int Count => _count;

    /// <summary>
    /// Gets the page at the given zero-based index.
    /// Pages are loaded lazily from the page tree.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="index"/> is outside [0, Count).
    /// </exception>
    public PdfPage this[int index]
    {
        get
        {
            if (index < 0 || index >= Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index),
                    $"Page index {index} is out of range [0, {Count}).");
            }

            if (!_cache.TryGetValue(index, out PdfPage? page))
            {
                page = _walked is not null
                    ? NewPage(_walked[index], index)
                    : FindPage(_pagesRoot, index, 0, 0, new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance));
                _cache[index] = page;
            }

            return page;
        }
    }

    /// <inheritdoc/>
    public IEnumerator<PdfPage> GetEnumerator()
    {
        for (int i = 0; i < Count; i++)
        {
            yield return this[i];
        }
    }

    /// <summary>
    /// Enumerates every page in document order <b>without retaining them</b>,
    /// for constant-memory traversal of very large documents.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="GetEnumerator"/> and the indexer — which cache each
    /// page so repeated access is cheap — this method walks the page tree once
    /// and yields freshly-constructed <see cref="PdfPage"/> instances that are
    /// not stored in the collection's cache. A full pass over a 10,000-page
    /// document therefore holds only the current page in memory rather than all
    /// of them. Use it for one-shot streaming work (export every page, extract
    /// all text) where you do not need random access afterwards; use the
    /// indexer when you will revisit pages.
    /// </remarks>
    /// <returns>The document's pages, in order, one at a time.</returns>
    public IEnumerable<PdfPage> EnumerateStreaming()
    {
        int index = 0;
        IEnumerable<PdfDictionary> leaves = _walked
            ?? WalkPageDictionaries(_pagesRoot, 0, new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance), () => _warn?.Invoke(LoopWarning));
        foreach (PdfDictionary leaf in leaves)
        {
            yield return NewPage(leaf, index);
            index++;
        }
    }

    private PdfPage NewPage(PdfDictionary dict, int index) =>
        new PdfPage(dict, _resolver, index, _optionalContent, _structTreeRoot, _acroForm, _warn);

    /// <summary>
    /// Yields the leaf /Page dictionaries under <paramref name="node"/> in
    /// document order, recursing through intermediate /Pages nodes. Mirrors the
    /// traversal in <see cref="FindPage"/> but streams every leaf instead of
    /// stopping at one index, and allocates nothing per page.
    /// </summary>
    private IEnumerable<PdfDictionary> WalkPageDictionaries(
        PdfDictionary node, int depth, HashSet<PdfDictionary> ancestors, Action onLoop)
    {
        if (depth > MaxPageTreeDepth)
        {
            throw new PdfCorruptionException(
                $"Page tree depth exceeds maximum ({MaxPageTreeDepth}); " +
                "document may contain a cyclic /Kids reference.");
        }

        // 3.34.0 (Phase B N9): a node without /Kids holds no pages, and a
        // kid that is not a dictionary is skipped, each with a warning; before,
        // either failed the page lookup, as Poppler and MuPDF do not.
        PdfArray? kids = node.GetArray(PdfName.Kids);
        if (kids is null)
        {
            _warn?.Invoke(NoKidsWarning);
            yield break;
        }

        for (int i = 0; i < kids.Count; i++)
        {
            PdfPrimitive kidRef = kids.GetAs<PdfPrimitive>(i) ?? PdfNull.Value;
            PdfPrimitive kid = _resolver.Resolve(kidRef);

            if (kid is not PdfDictionary kidDict)
            {
                _warn?.Invoke(BadKidWarning(i));
                continue;
            }

            PdfName? kidType = kidDict.Type;

            if (kidType == PdfName.Page)
            {
                yield return kidDict;
            }
            else if (kidType == PdfName.Pages)
            {
                // 3.34.0 (#52g): a node that is its own ancestor is skipped.
                if (ReferenceEquals(kidDict, node) || ancestors.Contains(kidDict))
                {
                    onLoop();
                    continue;
                }

                ancestors.Add(node);
                foreach (PdfDictionary leaf in WalkPageDictionaries(kidDict, depth + 1, ancestors, onLoop))
                {
                    yield return leaf;
                }

                ancestors.Remove(node);
            }
        }
    }

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // ── Private: page tree traversal ──────────────────────────────────────

    /// <summary>
    /// Recursively finds the page at document index (<paramref name="targetIndex"/>)
    /// within the subtree rooted at <paramref name="node"/>.
    /// <paramref name="offset"/> is the number of pages before this subtree.
    /// <paramref name="depth"/> is the recursion depth from the page-tree root;
    /// guarded by <see cref="MaxPageTreeDepth"/> to defend against cyclic or
    /// pathologically-deep page trees in malformed input.
    /// PDF 32000-1:2008 §7.7.3 — Page tree nodes.
    /// </summary>
    private PdfPage FindPage(PdfDictionary node, int targetIndex, int offset, int depth, HashSet<PdfDictionary> ancestors)
    {
        if (depth > MaxPageTreeDepth)
        {
            throw new PdfCorruptionException(
                $"Page tree depth exceeds maximum ({MaxPageTreeDepth}); " +
                "document may contain a cyclic /Kids reference.");
        }

        PdfArray? kids = node.GetArray(PdfName.Kids);
        if (kids is null)
        {
            _warn?.Invoke(NoKidsWarning);
            throw new PdfCorruptionException(
                $"Page index {targetIndex} not found in page tree (a page tree node has no /Kids array).");
        }

        int localOffset = offset;

        for (int i = 0; i < kids.Count; i++)
        {
            PdfPrimitive kidRef = kids.GetAs<PdfPrimitive>(i) ?? PdfNull.Value;
            PdfPrimitive kid = _resolver.Resolve(kidRef);

            if (kid is not PdfDictionary kidDict)
            {
                _warn?.Invoke(BadKidWarning(i));
                continue;
            }

            PdfName? kidType = kidDict.Type;

            if (kidType == PdfName.Page)
            {
                if (localOffset == targetIndex)
                {
                    return NewPage(kidDict, targetIndex);
                }

                localOffset++;
            }
            else if (kidType == PdfName.Pages)
            {
                // 3.34.0 (#52g): a node that is its own ancestor is skipped
                // and holds no pages.
                if (ReferenceEquals(kidDict, node) || ancestors.Contains(kidDict))
                {
                    _warn?.Invoke(LoopWarning);
                    continue;
                }

                int subtreeCount = kidDict.GetInteger(PdfName.Count, 0);

                if (targetIndex < localOffset + subtreeCount)
                {
                    ancestors.Add(node);
                    return FindPage(kidDict, targetIndex, localOffset, depth + 1, ancestors);
                }

                localOffset += subtreeCount;
            }
            else
            {
                // Unknown node type — skip.
                localOffset++;
            }
        }

        throw new PdfCorruptionException(
            $"Page index {targetIndex} not found in page tree (offset={offset}).");
    }
}
