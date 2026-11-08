// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.3.2.3 — Named destinations
// PHASE: 3.32.0 (D-208 item 4) — public access to named destinations

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Forms;

/// <summary>
/// Reads a document's named destinations (3.32.0).
/// </summary>
public static class DestinationReader
{
    /// <summary>
    /// Returns the document's named destinations, keyed by name: the
    /// <c>/Names /Dests</c> name tree first, then the catalog's legacy
    /// <c>/Dests</c> dictionary (a name in both keeps the name-tree entry).
    /// Names whose destination cannot be resolved to a page are left out.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The named destinations; empty when there are none.</returns>
    /// <remarks>
    /// Keys are the names as raw bytes read as Latin-1, the form
    /// <c>/GoTo</c> actions use to refer to them.
    /// </remarks>
    public static IReadOnlyDictionary<string, PdfDestination> GetNamedDestinations(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        DestinationResolver resolver = new DestinationResolver(document);
        Dictionary<string, PdfDestination> result = new Dictionary<string, PdfDestination>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, PdfPrimitive> entry in resolver.NamedDestinations)
        {
            if (resolver.ResolveDestination(entry.Value) is PdfDestination destination)
            {
                result[entry.Key] = destination;
            }
        }

        return result;
    }
}
