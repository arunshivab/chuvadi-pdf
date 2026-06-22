// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.4 — Filters
// PHASE: Phase 1 — Chuvadi.Pdf.Filters
// Central registry mapping PDF filter names to IStreamFilter implementations.

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Filters;

/// <summary>
/// Central registry of PDF stream filter implementations.
/// Call <see cref="CreateDefaultPipeline"/> to get a fully configured pipeline.
/// PDF 32000-1:2008 §7.4.
/// </summary>
public static class FilterRegistry
{
    private static readonly IStreamFilter[] Phase1Filters =
    [
        new DeflateFilter(),
        new AsciiHexFilter(),
        new Ascii85Filter(),
        new RunLengthFilter(),
        new LzwFilter(),
        new CcittFaxFilter(),
        new Jbig2Filter(),
    ];

    private static readonly Dictionary<string, string> Aliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Fl",  "FlateDecode" },
            { "AHx", "ASCIIHexDecode" },
            { "A85", "ASCII85Decode" },
            { "RL",  "RunLengthDecode" },
            { "LZW", "LZWDecode" },
            { "CCF", "CCITTFaxDecode" },
        };

    /// <summary>
    /// Creates a <see cref="FilterPipeline"/> with all Phase 1 filters
    /// and aliases pre-registered.
    /// </summary>
    public static FilterPipeline CreateDefaultPipeline()
    {
        FilterPipeline pipeline = FilterPipeline.Empty();

        foreach (IStreamFilter filter in Phase1Filters)
        {
            pipeline.Register(filter);
        }

        foreach (KeyValuePair<string, string> alias in Aliases)
        {
            pipeline.RegisterAlias(alias.Key, alias.Value);
        }

        return pipeline;
    }

    /// <summary>
    /// Returns the canonical filter name for a given name or alias.
    /// </summary>
    public static string ResolveAlias(string nameOrAlias)
    {
        if (nameOrAlias is null)
        {
            throw new ArgumentNullException(nameof(nameOrAlias));
        }

        return Aliases.TryGetValue(nameOrAlias, out string? canonical)
            ? canonical
            : nameOrAlias;
    }
}
