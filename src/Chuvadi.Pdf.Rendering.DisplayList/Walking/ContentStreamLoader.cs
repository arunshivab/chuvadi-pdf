// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.8.2 — Content streams (Contents entry),
//        §7.4 — Filters
// PHASE: Phase 2.8 — DisplayList consolidation (one walker, two sinks)
// Loads and filter-decodes a page's content stream(s) into one byte buffer.

using System;
using System.IO;
using Chuvadi.Pdf.Filters;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Rendering.Walking;

/// <summary>
/// Resolves a page's /Contents entry — a single stream or an array of
/// streams — and returns the decoded operator bytes, with a separating space
/// between array parts as §7.8.2 requires.
/// </summary>
internal static class ContentStreamLoader
{
    private static readonly FilterPipeline Pipeline = FilterRegistry.CreateDefaultPipeline();

    /// <summary>
    /// Loads and decodes the content bytes for a /Contents value; empty when
    /// the entry is missing, null, or not a stream/array.
    /// </summary>
    internal static byte[] Load(PdfPrimitive? contents, PdfObjectStore objects)
    {
        ArgumentNullException.ThrowIfNull(objects);

        if (contents is null || contents is PdfNull)
        {
            return Array.Empty<byte>();
        }

        PdfPrimitive resolved = objects.Resolve(contents);

        if (resolved is PdfStream single)
        {
            return Decode(single);
        }

        if (resolved is PdfArray array)
        {
            using MemoryStream merged = new();
            for (int i = 0; i < array.Count; i++)
            {
                if (objects.Resolve(array[i]) is PdfStream part)
                {
                    byte[] decoded = Decode(part);
                    merged.Write(decoded, 0, decoded.Length);
                    if (i < array.Count - 1)
                    {
                        merged.WriteByte((byte)' ');
                    }
                }
            }
            return merged.ToArray();
        }

        return Array.Empty<byte>();
    }

    /// <summary>Runs a stream's filter chain (aliases resolved); raw bytes when unfiltered.</summary>
    internal static byte[] Decode(PdfStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.IsFiltered)
        {
            return stream.RawBytes;
        }

        PdfPrimitive? filter = stream.Filter;

        if (filter is PdfName filterName)
        {
            string resolved = FilterRegistry.ResolveAlias(filterName.Value);
            return Pipeline.Decode(resolved, stream.RawBytes, null);
        }

        if (filter is PdfArray filterArray)
        {
            byte[] data = stream.RawBytes;
            for (int i = 0; i < filterArray.Count; i++)
            {
                if (filterArray[i] is PdfName fn)
                {
                    string resolved = FilterRegistry.ResolveAlias(fn.Value);
                    data = Pipeline.Decode(resolved, data, null);
                }
            }
            return data;
        }

        return stream.RawBytes;
    }
}
