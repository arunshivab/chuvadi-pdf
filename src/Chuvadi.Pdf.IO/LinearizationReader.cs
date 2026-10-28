// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ISO 32000-1:2008 §F.2 — Linearization parameter dictionary
// PHASE: Phase 1.1.6 — Chuvadi.Pdf.IO linearization
//
// Detects whether a document is linearized and parses the parameter dictionary.

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.IO;

/// <summary>
/// Detects linearization and parses the parameter dictionary.
/// </summary>
public static class LinearizationReader
{
    /// <summary>
    /// Attempts to read the linearization parameter dictionary from an object store.
    /// </summary>
    /// <param name="store">The object store to scan.</param>
    /// <returns>
    /// The parsed <see cref="LinearizationInfo"/>, or null when the document is
    /// not linearized.
    /// </returns>
    public static LinearizationInfo? TryRead(PdfObjectStore store)
    {
        return TryRead(store, maxObjectNumberToScan: 5);
    }

    /// <summary>
    /// Attempts to read the linearization parameter dictionary, scanning object
    /// numbers up to the given limit. Use this overload when the document may
    /// have many objects ahead of the parameter dict.
    /// </summary>
    public static LinearizationInfo? TryRead(PdfObjectStore store, int maxObjectNumberToScan)
    {
        ArgumentNullException.ThrowIfNull(store);

        // The parameter dictionary appears early in the file but may have any
        // object number. Scan up to the given limit.
        for (int objNum = 1; objNum <= maxObjectNumberToScan; objNum++)
        {
            PdfPrimitive resolved;
            try
            {
                resolved = store.ResolveById(new PdfObjectId(objNum, 0));
            }
            catch (Exception abortCheck) when (!Chuvadi.Pdf.Primitives.PdfWorkScope.IsAbort(abortCheck))
            {
                continue;
            }

            if (resolved is not PdfDictionary dict)
            {
                continue;
            }

            if (!dict.TryGetValue(PdfName.Intern("Linearized"), out PdfPrimitive? linPrim))
            {
                continue;
            }

            // Parse the entries
            double version = 1.0;
            if (linPrim is PdfReal r)
            {
                version = r.Value;
            }
            else if (linPrim is PdfInteger i)
            {
                version = i.Value;
            }

            long fileLength = GetLong(dict, "L", -1);
            int firstPageObjectNumber = (int)GetLong(dict, "O", 0);
            long endOfFirstPage = GetLong(dict, "E", 0);
            int pageCount = (int)GetLong(dict, "N", 0);
            long mainXrefOffset = GetLong(dict, "T", 0);

            // /H is a flat array of integers
            List<long> hints = new();
            if (dict.TryGetValue(PdfName.Intern("H"), out PdfPrimitive? hPrim) &&
                hPrim is PdfArray hArr)
            {
                for (int k = 0; k < hArr.Count; k++)
                {
                    if (hArr[k] is PdfInteger hi)
                    {
                        hints.Add(hi.Value);
                    }
                }
            }

            return new LinearizationInfo(
                version, fileLength, hints, firstPageObjectNumber,
                endOfFirstPage, pageCount, mainXrefOffset);
        }

        return null;
    }

    private static long GetLong(PdfDictionary dict, string key, long defaultValue)
    {
        if (dict.TryGetValue(PdfName.Intern(key), out PdfPrimitive? p) && p is PdfInteger i)
        {
            return i.Value;
        }
        return defaultValue;
    }
}
