// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.5 — File structure, §7.5.7 — Object streams,
//        §7.5.8 — Cross-reference streams
// PHASE: Phase 1 — Chuvadi.Pdf.IO
// Reconstructs a damaged PDF by scanning the raw bytes for every object,
// rebuilding the trailer, and re-emitting a clean classic-xref file.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.IO;

/// <summary>
/// Repairs structurally damaged PDFs that standard readers reject — broken or
/// missing cross-reference tables, wrong <c>startxref</c> offsets, missing or
/// corrupt trailers, leading junk before the header, truncated files, and
/// duplicate objects from incremental updates. The original byte offsets are
/// ignored; every <c>N G obj … endobj</c> is located by scanning, objects inside
/// compressed object streams (/ObjStm) and cross-reference streams are recovered,
/// and a clean file with a freshly built classic cross-reference table is written.
/// Repair is best-effort: it always emits the best file it can and reports what
/// could not be salvaged via <see cref="RepairReport"/> rather than throwing.
/// </summary>
public static class PdfRepairer
{
    private static readonly byte[] HeaderMarker = Encoding.ASCII.GetBytes("%PDF-");
    private static readonly byte[] TrailerMarker = Encoding.ASCII.GetBytes("trailer");
    private static readonly byte[] StreamMarker = Encoding.ASCII.GetBytes("stream");
    private static readonly byte[] EndstreamMarker = Encoding.ASCII.GetBytes("endstream");

    /// <summary>
    /// Reconstructs <paramref name="input"/> and writes a repaired PDF to
    /// <paramref name="output"/>.
    /// </summary>
    /// <param name="input">The damaged PDF. Read in full.</param>
    /// <param name="output">Destination for the repaired PDF.</param>
    /// <returns>A report describing what was recovered and rebuilt.</returns>
    public static RepairReport Repair(Stream input, Stream output)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        byte[] bytes = ReadAllBytes(input);
        List<string> warnings = new List<string>();

        int headerOffset = IndexOf(bytes, HeaderMarker, 0);
        bool headerRelocated = headerOffset > 0;
        if (headerOffset < 0)
        {
            headerOffset = 0;
            warnings.Add("PDF header (%PDF-) not found; scanning from the start of the file.");
        }

        // Scan for every object definition and parse it; the latest definition of
        // a given number wins, which flattens incremental updates.
        Dictionary<int, PdfIndirectObject> objects = new Dictionary<int, PdfIndirectObject>();
        int duplicates = 0;
        bool truncated = false;

        List<long> offsets = ScanObjectOffsets(bytes, headerOffset);
        int oi = 0;
        while (oi < offsets.Count)
        {
            int start = (int)offsets[oi];
            int nextOffset = (oi + 1 < offsets.Count) ? (int)offsets[oi + 1] : bytes.Length;
            int consumedEnd = nextOffset;
            try
            {
                PdfIndirectObject? obj = ParseObject(bytes, start, nextOffset, out bool objTruncated, out consumedEnd);
                if (objTruncated)
                {
                    truncated = true;
                }
                if (obj is not null)
                {
                    int number = obj.Id.ObjectNumber;
                    if (objects.ContainsKey(number))
                    {
                        duplicates++;
                    }
                    objects[number] = obj;
                }
            }
            catch (Exception)
            {
                // Stay permissive: one bad object must not abort the whole repair.
            }

            // Skip any header matches that fell inside a stream just consumed —
            // binary stream data can contain byte runs that look like object headers.
            int next = oi + 1;
            while (next < offsets.Count && offsets[next] < consumedEnd)
            {
                next++;
            }
            oi = next;
        }

        // Recover candidate /Root and /Info from any classic trailer and from any
        // cross-reference stream dictionary.
        PdfPrimitive? rootCandidate = null;
        PdfPrimitive? infoCandidate = null;
        ScanClassicTrailers(bytes, ref rootCandidate, ref infoCandidate);

        // Pull objects out of object streams; capture xref-stream /Root and /Info.
        int fromObjectStreams = 0;
        List<PdfIndirectObject> extracted = new List<PdfIndirectObject>();
        List<int> containerNumbers = new List<int>();
        foreach (KeyValuePair<int, PdfIndirectObject> entry in objects)
        {
            if (entry.Value.Value is not PdfStream stream)
            {
                continue;
            }

            PdfName? type = stream.Dictionary.GetAs<PdfName>(PdfName.Type);
            string typeName = type is null ? string.Empty : type.Value;

            if (string.Equals(typeName, "ObjStm", StringComparison.Ordinal))
            {
                containerNumbers.Add(entry.Key);
                ExtractObjectStream(stream, extracted, warnings);
            }
            else if (string.Equals(typeName, "XRef", StringComparison.Ordinal))
            {
                containerNumbers.Add(entry.Key);
                CaptureRootInfo(stream.Dictionary, ref rootCandidate, ref infoCandidate);
            }
        }

        // Drop the container objects; a fresh classic xref replaces them.
        for (int i = 0; i < containerNumbers.Count; i++)
        {
            objects.Remove(containerNumbers[i]);
        }

        // Merge extracted objects, preferring any direct definition already present.
        for (int i = 0; i < extracted.Count; i++)
        {
            int number = extracted[i].Id.ObjectNumber;
            if (!objects.ContainsKey(number))
            {
                objects[number] = extracted[i];
                fromObjectStreams++;
            }
        }

        // Resolve the document catalog.
        bool rootFromCandidate = TryResolveCatalog(rootCandidate, objects, out int rootNumber);
        bool rootRecovered = false;
        if (!rootFromCandidate)
        {
            if (TryFindCatalog(objects, out rootNumber))
            {
                rootRecovered = true;
            }
            else
            {
                rootNumber = -1;
                warnings.Add("No document catalog (/Type /Catalog) was found; the output may not open.");
            }
        }

        // Build a fresh trailer.
        PdfDictionary trailer = new PdfDictionary();
        if (rootNumber >= 0)
        {
            trailer.Set(PdfName.Root, new PdfReference(new PdfObjectId(rootNumber, 0)));
        }

        PdfName infoKey = PdfName.Intern("Info");
        if (infoCandidate is PdfReference infoRef && objects.ContainsKey(infoRef.ObjectId.ObjectNumber))
        {
            trailer.Set(infoKey, new PdfReference(new PdfObjectId(infoRef.ObjectId.ObjectNumber, 0)));
        }

        List<PdfIndirectObject> finalObjects = new List<PdfIndirectObject>(objects.Values);

        long outStart = output.Position;
        bool wrote = false;
        if (finalObjects.Count > 0)
        {
            PdfWriter.Write(output, finalObjects, trailer, null, SynthesizedMetadata.All);
            wrote = true;
        }
        else
        {
            warnings.Add("No objects could be recovered from the input.");
        }

        return new RepairReport
        {
            Repaired = wrote,
            ObjectsRecovered = finalObjects.Count,
            ObjectsFromObjectStreams = fromObjectStreams,
            DuplicateObjectsResolved = duplicates,
            TrailerReconstructed = wrote,
            RootRecovered = rootRecovered,
            CatalogFound = rootNumber >= 0,
            HeaderRelocated = headerRelocated,
            TruncationDetected = truncated,
            OriginalByteCount = bytes.Length,
            OutputByteCount = wrote ? output.Position - outStart : 0,
            Warnings = warnings,
        };
    }

    // ── Object scanning ───────────────────────────────────────────────────

    // Finds the byte offset of every "N G obj" header, anchored at a whitespace
    // boundary so binary stream content does not produce false positives.
    private static List<long> ScanObjectOffsets(byte[] b, int start)
    {
        List<long> offsets = new List<long>();
        int i = Math.Max(0, start);
        while (i < b.Length)
        {
            if ((i == 0 || IsWhitespace(b[i - 1])) && IsDigit(b[i])
                && TryMatchObjectHeader(b, i, out int afterObj))
            {
                offsets.Add(i);
                i = afterObj;
            }
            else
            {
                i++;
            }
        }
        return offsets;
    }

    private static bool TryMatchObjectHeader(byte[] b, int i, out int afterObj)
    {
        afterObj = i;
        int p = i;
        if (!ReadDigits(b, ref p))
        {
            return false;
        }
        if (!SkipWhitespace(b, ref p))
        {
            return false;
        }
        if (!ReadDigits(b, ref p))
        {
            return false;
        }
        if (!SkipWhitespace(b, ref p))
        {
            return false;
        }
        if (p + 3 > b.Length || b[p] != (byte)'o' || b[p + 1] != (byte)'b' || b[p + 2] != (byte)'j')
        {
            return false;
        }
        p += 3;
        if (p < b.Length && !IsWhitespace(b[p]) && !IsDelimiter(b[p]))
        {
            return false;
        }
        afterObj = p;
        return true;
    }

    private static bool ReadDigits(byte[] b, ref int p)
    {
        int startDigits = p;
        while (p < b.Length && IsDigit(b[p]) && p - startDigits < 18)
        {
            p++;
        }
        return p > startDigits;
    }

    private static bool SkipWhitespace(byte[] b, ref int p)
    {
        int startWs = p;
        while (p < b.Length && IsWhitespace(b[p]))
        {
            p++;
        }
        return p > startWs;
    }

    // Parses one object at <paramref name="start"/>. Streams are read independently
    // of their declared /Length: the dictionary is parsed in isolation and the raw
    // bytes are taken from after "stream" up to "endstream", scanned without bound.
    // <paramref name="consumedEnd"/> reports the absolute byte position past the
    // object so the caller can prune header matches that fell inside stream binary.
    private static PdfIndirectObject? ParseObject(
        byte[] bytes, int start, int nextOffset, out bool truncated, out int consumedEnd)
    {
        truncated = false;
        consumedEnd = nextOffset;
        int e = Math.Min(nextOffset, bytes.Length);
        int p = start;

        int num = ReadUInt(bytes, ref p);
        if (num < 0)
        {
            return null;
        }
        if (!SkipWhitespace(bytes, ref p))
        {
            return null;
        }
        int gen = ReadUInt(bytes, ref p);
        if (gen < 0)
        {
            return null;
        }
        if (!SkipWhitespace(bytes, ref p))
        {
            return null;
        }
        if (p + 3 > e || bytes[p] != (byte)'o' || bytes[p + 1] != (byte)'b' || bytes[p + 2] != (byte)'j')
        {
            return null;
        }
        p += 3;

        int streamPos = FindStreamKeyword(bytes, p, e);
        if (streamPos < 0)
        {
            // Non-stream object: parse the whole definition within its span.
            using MemoryStream window = new MemoryStream(bytes, start, e - start, writable: false);
            PdfObjectParser parser = new PdfObjectParser(window);
            PdfIndirectObject obj = parser.ReadIndirectObject();
            consumedEnd = nextOffset;
            return obj;
        }

        // Stream object: parse the dictionary in isolation so the parser cannot
        // auto-consume the stream using a corrupt /Length.
        PdfDictionary dict;
        using (MemoryStream dictWindow = new MemoryStream(bytes, p, streamPos - p, writable: false))
        {
            PdfObjectParser dictParser = new PdfObjectParser(dictWindow);
            PdfPrimitive dictValue = dictParser.ReadValue();
            if (dictValue is not PdfDictionary parsed)
            {
                return null;
            }
            dict = parsed;
        }

        int dataStart = streamPos + StreamMarker.Length;
        if (dataStart < bytes.Length && bytes[dataStart] == (byte)'\r')
        {
            dataStart++;
        }
        if (dataStart < bytes.Length && bytes[dataStart] == (byte)'\n')
        {
            dataStart++;
        }

        int endPos = IndexOf(bytes, EndstreamMarker, dataStart);
        if (endPos < 0)
        {
            // Truncated stream: salvage whatever bytes remain.
            truncated = true;
            endPos = bytes.Length;
            consumedEnd = bytes.Length;
        }
        else
        {
            consumedEnd = endPos + EndstreamMarker.Length;
        }

        int dataEnd = endPos;
        while (dataEnd > dataStart && (bytes[dataEnd - 1] == (byte)'\n' || bytes[dataEnd - 1] == (byte)'\r'))
        {
            dataEnd--;
        }

        byte[] raw = new byte[dataEnd - dataStart];
        Array.Copy(bytes, dataStart, raw, 0, raw.Length);
        return new PdfIndirectObject(new PdfObjectId(num, gen), new PdfStream(dict, raw));
    }

    private static int ReadUInt(byte[] b, ref int p)
    {
        int startDigits = p;
        long value = 0;
        while (p < b.Length && IsDigit(b[p]) && p - startDigits < 18)
        {
            value = (value * 10) + (b[p] - (byte)'0');
            p++;
        }
        if (p == startDigits || value > int.MaxValue)
        {
            return -1;
        }
        return (int)value;
    }

    // Finds a real "stream" keyword: preceded by whitespace or '>' and followed by
    // an end-of-line, which distinguishes it from the word appearing inside a name
    // or string in the dictionary.
    private static int FindStreamKeyword(byte[] b, int from, int end)
    {
        int limit = Math.Min(end, b.Length);
        for (int i = Math.Max(0, from); i + StreamMarker.Length <= limit; i++)
        {
            bool match = true;
            for (int k = 0; k < StreamMarker.Length; k++)
            {
                if (b[i + k] != StreamMarker[k])
                {
                    match = false;
                    break;
                }
            }
            if (!match)
            {
                continue;
            }
            byte prev = i > 0 ? b[i - 1] : (byte)'\n';
            if (!IsWhitespace(prev) && prev != (byte)'>')
            {
                continue;
            }
            int after = i + StreamMarker.Length;
            if (after < b.Length && (b[after] == (byte)'\r' || b[after] == (byte)'\n'))
            {
                return i;
            }
        }
        return -1;
    }

    // ── Trailer / catalog recovery ────────────────────────────────────────

    private static void ScanClassicTrailers(byte[] bytes, ref PdfPrimitive? root, ref PdfPrimitive? info)
    {
        int from = 0;
        using MemoryStream view = new MemoryStream(bytes, writable: false);
        while (true)
        {
            int at = IndexOf(bytes, TrailerMarker, from);
            if (at < 0)
            {
                break;
            }
            from = at + TrailerMarker.Length;
            try
            {
                PdfObjectParser parser = new PdfObjectParser(view);
                parser.Seek(at + TrailerMarker.Length);
                PdfPrimitive value = parser.ReadValue();
                if (value is PdfDictionary dict)
                {
                    CaptureRootInfo(dict, ref root, ref info);
                }
            }
            catch (Exception)
            {
                // Damaged trailer dictionary: ignore and keep scanning.
            }
        }
    }

    private static void CaptureRootInfo(PdfDictionary dict, ref PdfPrimitive? root, ref PdfPrimitive? info)
    {
        if (dict.TryGetValue(PdfName.Root, out PdfPrimitive? rootValue) && rootValue is PdfReference)
        {
            root = rootValue;
        }
        if (dict.TryGetValue(PdfName.Intern("Info"), out PdfPrimitive? infoValue) && infoValue is PdfReference)
        {
            info = infoValue;
        }
    }

    private static bool TryResolveCatalog(
        PdfPrimitive? candidate, Dictionary<int, PdfIndirectObject> objects, out int number)
    {
        number = -1;
        if (candidate is not PdfReference reference)
        {
            return false;
        }
        if (!objects.TryGetValue(reference.ObjectId.ObjectNumber, out PdfIndirectObject? obj))
        {
            return false;
        }
        if (obj.Value is PdfDictionary dict && IsCatalog(dict))
        {
            number = reference.ObjectId.ObjectNumber;
            return true;
        }
        return false;
    }

    private static bool TryFindCatalog(Dictionary<int, PdfIndirectObject> objects, out int number)
    {
        number = -1;
        foreach (KeyValuePair<int, PdfIndirectObject> entry in objects)
        {
            if (entry.Value.Value is PdfDictionary dict && IsCatalog(dict))
            {
                if (entry.Key > number)
                {
                    number = entry.Key;
                }
            }
        }
        return number >= 0;
    }

    private static bool IsCatalog(PdfDictionary dict)
    {
        PdfName? type = dict.GetAs<PdfName>(PdfName.Type);
        return type is not null && string.Equals(type.Value, "Catalog", StringComparison.Ordinal);
    }

    // ── Object-stream recovery ────────────────────────────────────────────

    private static void ExtractObjectStream(
        PdfStream stream, List<PdfIndirectObject> output, List<string> warnings)
    {
        try
        {
            byte[] payload = ObjectStreamReader.Decode(stream.Dictionary, stream.RawBytes);
            PdfInteger? countValue = stream.Dictionary.GetAs<PdfInteger>(PdfName.Intern("N"));
            PdfInteger? firstValue = stream.Dictionary.GetAs<PdfInteger>(PdfName.Intern("First"));
            if (countValue is null || firstValue is null)
            {
                return;
            }

            int count = countValue.Value;
            int first = firstValue.Value;
            if (count <= 0 || first < 0 || first > payload.Length)
            {
                return;
            }

            using MemoryStream view = new MemoryStream(payload, writable: false);
            PdfObjectParser headerParser = new PdfObjectParser(view);
            headerParser.Seek(0);

            int[] numbers = new int[count];
            int[] relativeOffsets = new int[count];
            for (int i = 0; i < count; i++)
            {
                PdfPrimitive numValue = headerParser.ReadValue();
                PdfPrimitive offValue = headerParser.ReadValue();
                if (numValue is not PdfInteger num || offValue is not PdfInteger off)
                {
                    return;
                }
                numbers[i] = num.Value;
                relativeOffsets[i] = off.Value;
            }

            for (int i = 0; i < count; i++)
            {
                long absolute = (long)first + relativeOffsets[i];
                if (absolute < 0 || absolute > payload.Length)
                {
                    continue;
                }
                try
                {
                    PdfObjectParser valueParser = new PdfObjectParser(view);
                    valueParser.Seek(absolute);
                    PdfPrimitive value = valueParser.ReadValue();
                    output.Add(new PdfIndirectObject(new PdfObjectId(numbers[i], 0), value));
                }
                catch (Exception)
                {
                    // Skip an unreadable compressed object; recover the rest.
                }
            }
        }
        catch (Exception)
        {
            warnings.Add("An object stream (/ObjStm) could not be decoded; its objects were not recovered.");
        }
    }

    // ── Byte helpers ──────────────────────────────────────────────────────

    private static byte[] ReadAllBytes(Stream input)
    {
        if (input is MemoryStream existing)
        {
            return existing.ToArray();
        }

        using MemoryStream buffer = new MemoryStream();
        input.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int start)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length)
        {
            return -1;
        }
        int last = haystack.Length - needle.Length;
        for (int i = Math.Max(0, start); i <= last; i++)
        {
            int j = 0;
            while (j < needle.Length && haystack[i + j] == needle[j])
            {
                j++;
            }
            if (j == needle.Length)
            {
                return i;
            }
        }
        return -1;
    }

    private static bool IsDigit(byte b)
    {
        return b >= (byte)'0' && b <= (byte)'9';
    }

    private static bool IsWhitespace(byte b)
    {
        return b == 0x00 || b == 0x09 || b == 0x0A || b == 0x0C || b == 0x0D || b == 0x20;
    }

    private static bool IsDelimiter(byte b)
    {
        return b == (byte)'(' || b == (byte)')' || b == (byte)'<' || b == (byte)'>'
            || b == (byte)'[' || b == (byte)']' || b == (byte)'{' || b == (byte)'}'
            || b == (byte)'/' || b == (byte)'%';
    }
}
