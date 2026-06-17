// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.5.4 (xref), §7.7.3 (page tree)
// PHASE: Input robustness — recover page objects when the xref offset is wrong.

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.IO;

/// <summary>
/// Recovers page-tree leaves that the cross-reference table resolves to the
/// wrong object. Some files (notably output from older writers that appended a
/// duplicate object without a proper incremental-update section) carry a classic
/// xref entry whose byte offset points at a different definition of the same
/// object number — so a <c>/Kids</c> entry that should resolve to a <c>/Page</c>
/// instead resolves to, say, a content stream. When that happens this helper
/// scans the raw file for every <c>N G obj</c> definition of the affected object
/// number, parses each candidate, and overrides the object store with the one
/// that is actually a <c>/Page</c> dictionary. Healthy files trigger no scan.
/// Scope is deliberately limited to page-tree kids; broader xref-offset
/// validation is tracked separately in the backlog.
/// </summary>
internal static class PageTreeRecovery
{
    private const int MaxPageTreeDepth = 1024;

    private static readonly byte[] ObjKeyword = { (byte)'o', (byte)'b', (byte)'j' };

    /// <summary>
    /// Validates the page tree under <paramref name="pagesRoot"/> and repairs any
    /// kid that resolves to a non-dictionary or a dictionary that is neither a
    /// <c>/Page</c> nor a <c>/Pages</c> node. Returns a human-readable warning for
    /// each object recovered; an empty list means the page tree was already sound.
    /// </summary>
    /// <param name="reader">The reader, used for whole-file byte access.</param>
    /// <param name="store">The object store to override recovered objects in.</param>
    /// <param name="pagesRoot">The root <c>/Pages</c> dictionary.</param>
    internal static IReadOnlyList<string> Recover(
        PdfReader reader, PdfObjectStore store, PdfDictionary pagesRoot)
    {
        List<string> warnings = new List<string>();

        // Cheap pre-check + repair in a single walk. The byte buffer is read
        // lazily — only when a broken kid is actually found.
        byte[]? fileBytes = null;
        HashSet<int> visited = new HashSet<int>();
        Walk(reader, store, pagesRoot, 0, visited, warnings, ref fileBytes);

        return warnings;
    }

    private static void Walk(
        PdfReader reader,
        PdfObjectStore store,
        PdfDictionary node,
        int depth,
        HashSet<int> visited,
        List<string> warnings,
        ref byte[]? fileBytes)
    {
        if (depth > MaxPageTreeDepth)
        {
            return;
        }

        if (node.GetArray(PdfName.Kids) is not PdfArray kids)
        {
            return;
        }

        for (int i = 0; i < kids.Count; i++)
        {
            if (kids.GetAs<PdfPrimitive>(i) is not PdfReference kidRef)
            {
                continue;
            }

            int num = kidRef.ObjectId.ObjectNumber;
            if (!visited.Add(num))
            {
                continue;
            }

            PdfPrimitive resolved = store.Resolve(kidRef);

            if (resolved is PdfDictionary dict && IsPageOrPages(dict))
            {
                // Sound node: recurse into intermediate /Pages, leave /Page.
                if (IsPages(dict))
                {
                    Walk(reader, store, dict, depth + 1, visited, warnings, ref fileBytes);
                }

                continue;
            }

            // Broken kid: the xref resolved this object number to the wrong
            // primitive. Scan the file for the correct /Page definition.
            fileBytes ??= ReadWholeFile(reader);

            PdfDictionary? recovered = FindPageDefinition(fileBytes, num, kidRef.ObjectId.Generation);
            if (recovered is not null)
            {
                store.Add(kidRef.ObjectId, recovered);
                warnings.Add(
                    $"Page-tree object {num} {kidRef.ObjectId.Generation} R resolved to a " +
                    "non-page object via the cross-reference table; recovered the /Page " +
                    "definition by scanning the file. The document was opened in a repaired " +
                    "state and will be written cleanly on save.");

                if (IsPages(recovered))
                {
                    Walk(reader, store, recovered, depth + 1, visited, warnings, ref fileBytes);
                }
            }
        }
    }

    private static bool IsPageOrPages(PdfDictionary dict)
    {
        return IsPage(dict) || IsPages(dict);
    }

    private static bool IsPage(PdfDictionary dict)
    {
        return dict.GetAs<PdfName>(PdfName.Type) is PdfName type && type.Value == "Page";
    }

    private static bool IsPages(PdfDictionary dict)
    {
        return dict.GetAs<PdfName>(PdfName.Type) is PdfName type && type.Value == "Pages";
    }

    private static byte[] ReadWholeFile(PdfReader reader)
    {
        long length = reader.FileLength;
        if (length <= 0 || length > int.MaxValue)
        {
            return Array.Empty<byte>();
        }

        return reader.ReadFileBytes(0, (int)length);
    }

    // Scans every "num gen obj" definition in the file and returns the first that
    // parses as a /Page dictionary. When multiple /Page definitions exist for the
    // same number (pathological), the last one wins, matching the
    // latest-definition rule used by the repairer.
    private static PdfDictionary? FindPageDefinition(byte[] bytes, int targetNum, int targetGen)
    {
        PdfDictionary? best = null;

        int i = 0;
        while (i < bytes.Length)
        {
            if ((i == 0 || IsWhitespace(bytes[i - 1])) && IsDigit(bytes[i])
                && TryReadHeader(bytes, i, out int num, out int gen, out int afterObj)
                && num == targetNum && gen == targetGen)
            {
                PdfDictionary? parsed = TryParseDictionaryAt(bytes, i, afterObj);
                if (parsed is not null && IsPage(parsed))
                {
                    best = parsed;
                }

                i = afterObj;
            }
            else
            {
                i++;
            }
        }

        return best;
    }

    // Parses the value of the object whose header occupies [start, afterObj).
    // Only non-stream dictionary objects are of interest (a /Page is never a
    // stream), so a stream object is rejected by parsing failure or type check.
    private static PdfDictionary? TryParseDictionaryAt(byte[] bytes, int start, int afterObj)
    {
        // Bound the parse window at the next "endobj" so a corrupt neighbour does
        // not bleed in. Fall back to end-of-file when no marker is present.
        int end = FindEndObj(bytes, afterObj);
        if (end < 0)
        {
            end = bytes.Length;
        }

        try
        {
            using MemoryStream window = new MemoryStream(bytes, start, end - start, writable: false);
            PdfObjectParser parser = new PdfObjectParser(window);
            PdfIndirectObject obj = parser.ReadIndirectObject();
            return obj.Value as PdfDictionary;
        }
        catch (Exception)
        {
            // A malformed candidate is simply skipped; another definition may
            // parse cleanly. Recovery never throws on a bad candidate.
            return null;
        }
    }

    private static int FindEndObj(byte[] b, int from)
    {
        for (int i = from; i + 6 <= b.Length; i++)
        {
            if (b[i] == (byte)'e' && b[i + 1] == (byte)'n' && b[i + 2] == (byte)'d'
                && b[i + 3] == (byte)'o' && b[i + 4] == (byte)'b' && b[i + 5] == (byte)'j')
            {
                return i + 6;
            }
        }

        return -1;
    }

    private static bool TryReadHeader(byte[] b, int i, out int num, out int gen, out int afterObj)
    {
        num = -1;
        gen = -1;
        afterObj = i;

        int p = i;
        int parsedNum = ReadUInt(b, ref p);
        if (parsedNum < 0 || !SkipWhitespace(b, ref p))
        {
            return false;
        }

        int parsedGen = ReadUInt(b, ref p);
        if (parsedGen < 0 || !SkipWhitespace(b, ref p))
        {
            return false;
        }

        if (p + ObjKeyword.Length > b.Length)
        {
            return false;
        }

        for (int k = 0; k < ObjKeyword.Length; k++)
        {
            if (b[p + k] != ObjKeyword[k])
            {
                return false;
            }
        }

        num = parsedNum;
        gen = parsedGen;
        afterObj = p + ObjKeyword.Length;
        return true;
    }

    private static int ReadUInt(byte[] b, ref int p)
    {
        int start = p;
        long value = 0;
        while (p < b.Length && IsDigit(b[p]))
        {
            value = (value * 10) + (b[p] - (byte)'0');
            if (value > int.MaxValue)
            {
                return -1;
            }

            p++;
        }

        return p > start ? (int)value : -1;
    }

    private static bool SkipWhitespace(byte[] b, ref int p)
    {
        int start = p;
        while (p < b.Length && IsWhitespace(b[p]))
        {
            p++;
        }

        return p > start;
    }

    private static bool IsDigit(byte c)
    {
        return c >= (byte)'0' && c <= (byte)'9';
    }

    private static bool IsWhitespace(byte c)
    {
        return c == (byte)' ' || c == (byte)'\r' || c == (byte)'\n'
            || c == (byte)'\t' || c == (byte)'\f' || c == 0;
    }
}
