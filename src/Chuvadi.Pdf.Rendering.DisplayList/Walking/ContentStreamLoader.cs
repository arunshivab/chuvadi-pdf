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

    /// <summary>
    /// Runs a stream's filter chain (aliases resolved), honouring per-filter
    /// /DecodeParms entries; raw bytes when unfiltered. When <paramref name="objects"/>
    /// is supplied, a filter's <c>/JBIG2Globals</c> shared-segment stream is resolved
    /// and decoded so JBIG2 images that reference globals decode correctly.
    /// </summary>
    internal static byte[] Decode(PdfStream stream, PdfObjectStore? objects = null)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.IsFiltered)
        {
            return stream.RawBytes;
        }

        PdfPrimitive? filter = stream.Filter;
        PdfPrimitive? decodeParms = ReadDecodeParms(stream);

        if (filter is PdfName filterName)
        {
            string resolved = FilterRegistry.ResolveAlias(filterName.Value);
            return Pipeline.Decode(
                resolved, stream.RawBytes, BuildParameters(decodeParms, 0, objects));
        }

        if (filter is PdfArray filterArray)
        {
            byte[] data = stream.RawBytes;
            for (int i = 0; i < filterArray.Count; i++)
            {
                if (filterArray[i] is PdfName fn)
                {
                    string resolved = FilterRegistry.ResolveAlias(fn.Value);
                    data = Pipeline.Decode(
                        resolved, data, BuildParameters(decodeParms, i, objects));
                }
            }
            return data;
        }

        return stream.RawBytes;
    }

    // Builds one filter's parameters, attaching the resolved /JBIG2Globals
    // shared-segment bytes when the parameter dictionary names them and a
    // resolver is available.
    private static FilterParameters? BuildParameters(
        PdfPrimitive? decodeParms, int filterIndex, PdfObjectStore? objects)
    {
        FilterParameters? parameters = FilterParameters.FromDictionary(decodeParms, filterIndex);
        if (objects is null)
        {
            return parameters;
        }

        byte[]? globals = ResolveJbig2Globals(decodeParms, filterIndex, objects);
        if (globals is null)
        {
            return parameters;
        }

        FilterParameters effective = parameters ?? new FilterParameters();
        return effective with { Jbig2Globals = globals };
    }

    // Resolves the /JBIG2Globals stream named by a filter's parameter dictionary
    // to its decoded bytes (the stream may itself be filtered), or null when
    // absent or unresolvable.
    private static byte[]? ResolveJbig2Globals(
        PdfPrimitive? decodeParms, int filterIndex, PdfObjectStore objects)
    {
        PdfDictionary? parmsDict = decodeParms switch
        {
            PdfDictionary single => single,
            PdfArray array => array.GetAs<PdfDictionary>(filterIndex),
            _ => null,
        };

        if (parmsDict is null
            || !parmsDict.TryGetValue(PdfName.Intern("JBIG2Globals"), out PdfPrimitive? globalsRef))
        {
            return null;
        }

        if (objects.Resolve(globalsRef) is not PdfStream globalsStream)
        {
            return null;
        }

        return Decode(globalsStream, objects);
    }

    // /DecodeParms with its /DP abbreviation (PDF 32000-1:2008 Table 5).
    private static PdfPrimitive? ReadDecodeParms(PdfStream stream)
    {
        if (stream.Dictionary.TryGetValue(PdfName.Intern("DecodeParms"), out PdfPrimitive? parms))
        {
            return parms;
        }
        if (stream.Dictionary.TryGetValue(PdfName.Intern("DP"), out PdfPrimitive? abbreviated))
        {
            return abbreviated;
        }
        return null;
    }
}
