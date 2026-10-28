// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.4 — Filters, §7.4.1 — General
// PHASE: Phase 1 — Chuvadi.Pdf.Filters
// Applies and removes chains of PDF stream filters.

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Filters;

/// <summary>
/// Applies and removes chains of PDF stream filters.
/// PDF 32000-1:2008 §7.4.1.
/// </summary>
public sealed class FilterPipeline
{
    private readonly Dictionary<string, IStreamFilter> _filters;

    /// <summary>Creates a pipeline with DeflateFilter pre-registered.</summary>
    public FilterPipeline()
    {
        _filters = new Dictionary<string, IStreamFilter>(StringComparer.Ordinal);
        Register(new DeflateFilter());
    }

    /// <summary>Creates an empty pipeline. Use <see cref="Register"/> to add filters.</summary>
    public static FilterPipeline Empty()
    {
        return new FilterPipeline(empty: true);
    }

    private FilterPipeline(bool empty)
    {
        _filters = new Dictionary<string, IStreamFilter>(StringComparer.Ordinal);
        _ = empty;
    }

    // ── Registration ──────────────────────────────────────────────────────

    /// <summary>Registers a filter. Replaces any existing registration for the same name.</summary>
    public void Register(IStreamFilter filter)
    {
        if (filter is null)
        {
            throw new ArgumentNullException(nameof(filter));
        }

        _filters[filter.FilterName] = filter;
    }

    /// <summary>
    /// Registers an alias pointing to an already-registered filter.
    /// </summary>
    public void RegisterAlias(string alias, string canonicalName)
    {
        if (alias is null)
        {
            throw new ArgumentNullException(nameof(alias));
        }

        if (canonicalName is null)
        {
            throw new ArgumentNullException(nameof(canonicalName));
        }

        if (!_filters.TryGetValue(canonicalName, out IStreamFilter? filter))
        {
            throw new FilterException(canonicalName,
                $"Cannot register alias '{alias}': canonical filter '{canonicalName}' is not registered.");
        }

        _filters[alias] = filter;
    }

    /// <summary>Returns true if a filter with the given name is registered.</summary>
    public bool IsRegistered(string filterName)
    {
        if (filterName is null)
        {
            throw new ArgumentNullException(nameof(filterName));
        }

        return _filters.ContainsKey(filterName);
    }

    // ── Decode ────────────────────────────────────────────────────────────

    /// <summary>Decodes <paramref name="encoded"/> by removing the named filter.</summary>
    public byte[] Decode(string filterName, byte[] encoded, FilterParameters? parms = null)
    {
        if (filterName is null)
        {
            throw new ArgumentNullException(nameof(filterName));
        }

        if (encoded is null)
        {
            throw new ArgumentNullException(nameof(encoded));
        }

        IStreamFilter filter = GetFilter(filterName);

        using (MemoryStream input = new MemoryStream(encoded))
        {
            using (GuardedOutputStream output = new GuardedOutputStream())
            {
                filter.Decode(input, output, parms);
                PdfWorkScope.CheckDecodedSize(output.Length);
                PdfWorkScope.ChargeMemory(output.Length - output.Charged);
                return output.ToArray();
            }
        }
    }

    /// <summary>
    /// Decodes <paramref name="encoded"/> through a chain of filters, first to last.
    /// </summary>
    public byte[] DecodeChain(
        IReadOnlyList<string> filterNames,
        byte[] encoded,
        IReadOnlyList<FilterParameters?>? parmsArray = null)
    {
        if (filterNames is null)
        {
            throw new ArgumentNullException(nameof(filterNames));
        }

        if (encoded is null)
        {
            throw new ArgumentNullException(nameof(encoded));
        }

        byte[] current = encoded;

        for (int i = 0; i < filterNames.Count; i++)
        {
            FilterParameters? parms = parmsArray is not null && i < parmsArray.Count
                ? parmsArray[i]
                : null;

            current = Decode(filterNames[i], current, parms);
        }

        return current;
    }

    // ── Encode ────────────────────────────────────────────────────────────

    /// <summary>Encodes <paramref name="raw"/> by applying the named filter.</summary>
    public byte[] Encode(string filterName, byte[] raw, FilterParameters? parms = null)
    {
        if (filterName is null)
        {
            throw new ArgumentNullException(nameof(filterName));
        }

        if (raw is null)
        {
            throw new ArgumentNullException(nameof(raw));
        }

        IStreamFilter filter = GetFilter(filterName);

        using (MemoryStream input = new MemoryStream(raw))
        {
            using (MemoryStream output = new MemoryStream())
            {
                filter.Encode(input, output, parms);
                return output.ToArray();
            }
        }
    }

    // ── Private ───────────────────────────────────────────────────────────

    private IStreamFilter GetFilter(string filterName)
    {
        if (!_filters.TryGetValue(filterName, out IStreamFilter? filter))
        {
            throw new FilterException(filterName,
                $"No filter registered for '{filterName}'.");
        }

        return filter;
    }
}
