// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.5.8 — Cross-reference streams
// PHASE: Phase 1 — Chuvadi.Pdf.Objects
// PDF 1.5+ binary cross-reference stream format.

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Objects;

/// <summary>
/// Reads and writes PDF 1.5+ cross-reference streams.
/// </summary>
/// <remarks>
/// Cross-reference streams replace (or supplement) the classic xref table
/// in PDF 1.5 and later. They are regular PDF stream objects whose content
/// encodes xref entries as binary integers.
///
/// The stream dictionary must contain:
/// <list type="bullet">
///   <item><c>/Type /XRef</c></item>
///   <item><c>/Size</c> — highest object number + 1</item>
///   <item><c>/W</c> — array of three field widths in bytes</item>
///   <item><c>/Index</c> — optional array of subsection ranges</item>
/// </list>
///
/// The <c>/W</c> array [w1, w2, w3] gives the byte widths of the three
/// fields in each entry:
/// <list type="bullet">
///   <item>Field 1: entry type (0=free, 1=in-use, 2=compressed)</item>
///   <item>Field 2: offset/object-number/next-free</item>
///   <item>Field 3: generation/index-in-stream</item>
/// </list>
/// A width of 0 means the field is absent and defaults to 0.
///
/// PDF 32000-1:2008 §7.5.8 — Cross-reference streams.
/// </remarks>
public sealed class XrefStreamTable
{
    private readonly List<XrefEntry> _entries;

    /// <summary>Creates an empty <see cref="XrefStreamTable"/>.</summary>
    public XrefStreamTable()
    {
        _entries = new List<XrefEntry>();
    }

    /// <summary>Gets all entries in this xref stream.</summary>
    public IReadOnlyList<XrefEntry> Entries => _entries;

    /// <summary>Gets the number of entries.</summary>
    public int Count => _entries.Count;

    /// <summary>Adds an entry to this xref stream.</summary>
    public void Add(XrefEntry entry)
    {
        _entries.Add(entry);
    }

    /// <summary>
    /// Parses a cross-reference stream from its decoded byte content and
    /// stream dictionary.
    /// </summary>
    /// <param name="dictionary">The xref stream dictionary.</param>
    /// <param name="decodedBytes">
    /// The decompressed stream content (after filter removal).
    /// </param>
    /// <returns>A populated <see cref="XrefStreamTable"/>.</returns>
    /// <exception cref="PdfParseException">
    /// Thrown when the stream is malformed.
    /// </exception>
    public static XrefStreamTable Parse(PdfDictionary dictionary, byte[] decodedBytes)
    {
        if (dictionary is null)
        {
            throw new ArgumentNullException(nameof(dictionary));
        }

        if (decodedBytes is null)
        {
            throw new ArgumentNullException(nameof(decodedBytes));
        }

        // Read /W field widths. PDF 32000-1:2008 Table 17.
        PdfArray? wArray = dictionary.GetArray(PdfName.Intern("W"));

        if (wArray is null || wArray.Count != 3)
        {
            throw new PdfParseException(
                "XRef stream dictionary missing or invalid /W array.");
        }

        // 3.34.0: each width is checked (an integer of 0 to 8 bytes), so a
        // damaged /W is a PdfParseException rather than an invalid cast or a
        // silent overflow.
        int w1 = Checked(wArray, 0, "/W", 0, MaxFieldWidth); // type field width
        int w2 = Checked(wArray, 1, "/W", 0, MaxFieldWidth); // offset/objnum field width
        int w3 = Checked(wArray, 2, "/W", 0, MaxFieldWidth); // generation/index field width
        int entrySize = w1 + w2 + w3;

        if (entrySize == 0)
        {
            throw new PdfParseException("XRef stream /W array sums to zero.");
        }

        // Read /Index array (subsection ranges). Default: [0 Size].
        int size = dictionary.GetInteger(PdfName.Size);
        PdfArray? indexArray = dictionary.GetArray(PdfName.Intern("Index"));
        List<(int First, int Count)> ranges = ParseIndexArray(indexArray, size);

        // Parse entries.
        XrefStreamTable table = new XrefStreamTable();
        int pos = 0;

        foreach ((int first, int count) in ranges)
        {
            for (int i = 0; i < count; i++)
            {
                if (pos + entrySize > decodedBytes.Length)
                {
                    throw new PdfParseException(
                        $"XRef stream truncated at object {first + i}.");
                }

                int objectNumber = first + i;
                long type = w1 > 0 ? ReadInt(decodedBytes, pos, w1) : 1; // default type=1
                pos += w1;

                long field2 = ReadInt(decodedBytes, pos, w2);
                pos += w2;

                long field3 = ReadInt(decodedBytes, pos, w3);
                pos += w3;

                XrefEntry entry;

                switch (type)
                {
                    case 0:
                        entry = XrefEntry.Free(objectNumber, ToInt(field3), ToInt(field2));
                        break;
                    case 1:
                        entry = new XrefEntry(objectNumber, ToInt(field3), field2);
                        break;
                    case 2:
                        entry = XrefEntry.Compressed(objectNumber, ToInt(field2), ToInt(field3));
                        break;
                    default:
                        // Unknown type: skip per spec (§7.5.8.2).
                        continue;
                }

                table.Add(entry);
            }
        }

        return table;
    }

    /// <summary>
    /// Encodes this xref stream's entries as binary bytes suitable for
    /// embedding in a PDF stream.
    /// </summary>
    /// <param name="w1">Byte width of the type field (recommend 1).</param>
    /// <param name="w2">Byte width of the offset field (recommend 4 or 8).</param>
    /// <param name="w3">Byte width of the generation field (recommend 2).</param>
    /// <returns>The encoded binary content, ready for compression.</returns>
    public byte[] Encode(int w1 = 1, int w2 = 4, int w3 = 2)
    {
        int entrySize = w1 + w2 + w3;
        byte[] result = new byte[_entries.Count * entrySize];
        int pos = 0;

        foreach (XrefEntry entry in _entries)
        {
            int type = (int)entry.Type;
            long field2 = entry.Type == XrefEntryType.Compressed
                ? entry.StreamObjectNumber
                : entry.ByteOffset;
            int field3 = entry.Type == XrefEntryType.Compressed
                ? entry.IndexInStream
                : entry.Generation;

            WriteInt(result, pos, w1, type);
            pos += w1;
            WriteInt(result, pos, w2, (int)field2);
            pos += w2;
            WriteInt(result, pos, w3, field3);
            pos += w3;
        }

        return result;
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private static List<(int First, int Count)> ParseIndexArray(
        PdfArray? indexArray,
        int size)
    {
        List<(int, int)> ranges = new List<(int, int)>();

        if (indexArray is null)
        {
            // Default: one range covering all objects.
            ranges.Add((0, size));
            return ranges;
        }

        if (indexArray.Count % 2 != 0)
        {
            throw new PdfParseException(
                $"XRef stream /Index array must have an even number of elements, got {indexArray.Count}.");
        }

        for (int i = 0; i < indexArray.Count; i += 2)
        {
            ranges.Add((Checked(indexArray, i, "/Index", 0, int.MaxValue), Checked(indexArray, i + 1, "/Index", 0, int.MaxValue)));
        }

        return ranges;
    }

    // The widest /W field read (an 8-byte offset).
    private const int MaxFieldWidth = 8;

    // An /W or /Index entry: an integer within [min, max], else a parse error.
    private static int Checked(PdfArray array, int index, string key, int min, int max)
    {
        if (array[index] is PdfInteger integer && integer.Value >= min && integer.Value <= max)
        {
            return integer.Value;
        }

        throw new PdfParseException(
            $"XRef stream {key} entry {index} must be an integer from {min} to {max}.");
    }

    // A field value that must fit an object number, generation or index.
    private static int ToInt(long value) =>
        value <= int.MaxValue
            ? (int)value
            : throw new PdfParseException($"XRef stream field value {value} is out of range.");

    // Read a big-endian integer of width bytes (at most 8) from data at offset.
    private static long ReadInt(byte[] data, int offset, int width)
    {
        long value = 0;

        for (int i = 0; i < width; i++)
        {
            value = (value << 8) | data[offset + i];
        }

        return value;
    }

    // Write a big-endian integer of width bytes to data at offset.
    private static void WriteInt(byte[] data, int offset, int width, int value)
    {
        for (int i = width - 1; i >= 0; i--)
        {
            data[offset + i] = (byte)(value & 0xFF);
            value >>= 8;
        }
    }
}
