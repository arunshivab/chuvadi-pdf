// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.5.7 — Object streams
// PHASE: Phase 1 — Chuvadi.Pdf.IO
//
// Reads a single object stored inside a PDF object stream (§7.5.7), the
// container format used by xref-stream PDFs (PDF 1.5+) for compressed
// objects. An object stream is a regular indirect object whose value is a
// PdfStream with /Type /ObjStm; its content holds N bare PDF values
// preceded by an integer-pair header that maps each contained object
// number to a byte offset within the payload.
//
// Per spec, a compressed xref entry (type 2) names the containing object
// stream's object number plus the zero-based index of the requested
// object within that stream. The current class resolves that to the
// actual PdfPrimitive.
//
// Currently internal. Will be promoted to public when a stable external
// use case emerges (e.g. third-party tooling that needs to inspect raw
// object streams without going through PdfReader).

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Filters;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.IO;

/// <summary>
/// Materialises objects compressed inside PDF object streams (§7.5.7).
/// Caches each decoded object stream so repeated lookups against the
/// same container are cheap.
/// </summary>
internal sealed class ObjectStreamReader
{
    // Cache of decoded object streams, keyed by container object number.
    private readonly Dictionary<int, ObjectStream> _cache = new();

    /// <summary>
    /// Materialises the object stored at <paramref name="indexInStream"/>
    /// inside the object stream identified by
    /// <paramref name="streamObjectNumber"/>. Returns null when the
    /// container cannot be resolved, is malformed, or does not contain
    /// the requested index.
    /// </summary>
    /// <param name="streamObjectNumber">
    /// Object number of the containing object stream (xref entry's
    /// <see cref="XrefEntry.StreamObjectNumber"/>).
    /// </param>
    /// <param name="indexInStream">
    /// Zero-based index of the requested object within the stream
    /// (xref entry's <see cref="XrefEntry.IndexInStream"/>).
    /// </param>
    /// <param name="containerLoader">
    /// Callback that loads the container object by object number. The
    /// caller (typically <c>PdfReader</c>) implements this to avoid a
    /// hard dependency on <c>PdfReader</c> internals from this class.
    /// Must return null if the container does not exist.
    /// </param>
    internal PdfPrimitive? TryRead(
        int streamObjectNumber,
        int indexInStream,
        Func<int, PdfIndirectObject?> containerLoader)
    {
        ArgumentNullException.ThrowIfNull(containerLoader);

        if (!_cache.TryGetValue(streamObjectNumber, out ObjectStream? objStm))
        {
            objStm = TryParse(streamObjectNumber, containerLoader);
            if (objStm is null) { return null; }
            _cache[streamObjectNumber] = objStm;
        }

        return objStm.GetByIndex(indexInStream);
    }

    /// <summary>
    /// Loads, decompresses, and parses an object stream into its
    /// constituent <see cref="PdfPrimitive"/> values.
    /// </summary>
    private static ObjectStream? TryParse(
        int streamObjectNumber,
        Func<int, PdfIndirectObject?> containerLoader)
    {
        PdfIndirectObject? container = containerLoader(streamObjectNumber);
        if (container is null) { return null; }
        if (container.Value is not PdfStream stream) { return null; }

        PdfDictionary dict = stream.Dictionary;

        // /Type /ObjStm is required per §7.5.7. We don't reject on its
        // absence here because some producers omit it; if /N and /First
        // parse cleanly, we trust the structure.
        if (!dict.TryGetValue(PdfName.Intern("N"), out PdfPrimitive? nPrim)
            || nPrim is not PdfInteger nInt)
        {
            return null;
        }
        int n = nInt.Value;
        if (n < 0) { return null; }

        if (!dict.TryGetValue(PdfName.Intern("First"), out PdfPrimitive? firstPrim)
            || firstPrim is not PdfInteger firstInt)
        {
            return null;
        }
        int first = firstInt.Value;
        if (first < 0) { return null; }

        byte[] decoded;
        try
        {
            decoded = Decode(dict, stream.RawBytes);
        }
        catch (Exception)
        {
            return null;
        }

        if (first > decoded.Length) { return null; }

        // The header is the leading `first` bytes: N whitespace-separated
        // integer pairs `objNum offsetFromFirst`. We parse them via
        // PdfObjectParser to tolerate any ASCII whitespace pattern, which
        // is what §7.5.7 allows.
        List<int> objectNumbers = new(n);
        List<int> payloadOffsets = new(n);

        using MemoryStream headerStream = new(decoded, 0, first, writable: false);
        PdfObjectParser headerParser = new(headerStream);

        for (int i = 0; i < n; i++)
        {
            PdfPrimitive numVal = headerParser.ReadValue();
            PdfPrimitive offVal = headerParser.ReadValue();
            if (numVal is not PdfInteger numInt || offVal is not PdfInteger offInt)
            {
                return null;
            }
            objectNumbers.Add(numInt.Value);
            payloadOffsets.Add(offInt.Value);
        }

        return new ObjectStream(decoded, first, objectNumbers, payloadOffsets);
    }

    /// <summary>
    /// Decodes the raw bytes of a PDF stream, honouring a /Filter entry
    /// that may be a single Name or an Array of Names. Shared between
    /// <see cref="ObjectStreamReader"/> and
    /// <c>PdfReader.DecodeStreamBytes</c> (v2.1.8 onwards).
    /// </summary>
    /// <remarks>
    /// PDF 32000-1:2008 §7.4 allows /Filter to be either a Name (single
    /// filter) or an Array of Names (chain of filters applied in order).
    /// Up to v2.1.7 PdfReader handled only the single-Name case and
    /// silently emitted raw (undecoded) bytes when /Filter was an array;
    /// that was the bug fixed in v2.1.8.
    ///
    /// /DecodeParms (the per-filter parameter sibling of /Filter) is threaded
    /// through here: a single dictionary applies to the sole filter, and an
    /// array of dictionaries pairs positionally with the /Filter array.
    /// Per-filter parameters (e.g. a Predictor on a FlateDecode entry) are
    /// built via <see cref="FilterParameters.FromDictionary(PdfPrimitive?, int)"/>.
    /// </remarks>
    internal static byte[] Decode(PdfDictionary dict, byte[] rawBytes)
    {
        if (!dict.TryGetValue(PdfName.Filter, out PdfPrimitive? filterPrim))
        {
            return rawBytes;
        }

        dict.TryGetValue(PdfName.Intern("DecodeParms"), out PdfPrimitive? decodeParms);

        FilterPipeline pipeline = FilterRegistry.CreateDefaultPipeline();
        byte[] current = rawBytes;

        if (filterPrim is PdfName singleName)
        {
            string resolved = FilterRegistry.ResolveAlias(singleName.Value);
            current = pipeline.Decode(resolved, current, FilterParameters.FromDictionary(decodeParms, 0));
            return current;
        }

        if (filterPrim is PdfArray filterArray)
        {
            for (int i = 0; i < filterArray.Count; i++)
            {
                if (filterArray[i] is not PdfName filterName)
                {
                    throw new PdfParseException(
                        "Stream /Filter array contains a non-Name entry.");
                }
                string resolved = FilterRegistry.ResolveAlias(filterName.Value);
                current = pipeline.Decode(resolved, current, FilterParameters.FromDictionary(decodeParms, i));
            }
            return current;
        }

        throw new PdfParseException(
            $"Stream /Filter must be a Name or Array, got {filterPrim.GetType().Name}.");
    }

    /// <summary>
    /// Decoded, parsed representation of a single object stream.
    /// Reads bare PdfPrimitive values from the payload on demand.
    /// </summary>
    private sealed class ObjectStream
    {
        private readonly byte[] _decoded;
        private readonly int _firstOffset;
        private readonly IReadOnlyList<int> _objectNumbers;
        private readonly IReadOnlyList<int> _payloadOffsets;

        internal ObjectStream(
            byte[] decoded,
            int firstOffset,
            IReadOnlyList<int> objectNumbers,
            IReadOnlyList<int> payloadOffsets)
        {
            _decoded = decoded;
            _firstOffset = firstOffset;
            _objectNumbers = objectNumbers;
            _payloadOffsets = payloadOffsets;
        }

        internal int Count => _objectNumbers.Count;

        /// <summary>
        /// Returns the parsed value at the given zero-based index, or
        /// null when the index is out of range or the value cannot be
        /// parsed.
        /// </summary>
        internal PdfPrimitive? GetByIndex(int index)
        {
            if (index < 0 || index >= _payloadOffsets.Count) { return null; }

            int startInPayload = _payloadOffsets[index];
            int absoluteStart = _firstOffset + startInPayload;
            if (absoluteStart < 0 || absoluteStart >= _decoded.Length) { return null; }

            int payloadLength = _decoded.Length - absoluteStart;

            try
            {
                using MemoryStream ms = new(_decoded, absoluteStart, payloadLength, writable: false);
                PdfObjectParser parser = new(ms);
                return parser.ReadValue();
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Currently unused, but exposes the object-number list for diagnostics
        // and for sanity checks (the index-th object in the stream should
        // have object number equal to the xref entry's ObjectNumber).
        internal int GetObjectNumberAt(int index)
        {
            if (index < 0 || index >= _objectNumbers.Count) { return -1; }
            return _objectNumbers[index];
        }
    }
}
