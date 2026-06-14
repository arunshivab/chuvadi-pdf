// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.5.4 — Cross-reference table
// PHASE: Phase 1 — Chuvadi.Pdf.Objects
// Classic PDF cross-reference table (subsection-based).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Objects;

/// <summary>
/// Represents a classic PDF cross-reference table.
/// </summary>
/// <remarks>
/// The classic xref table is the original PDF cross-reference format,
/// used in PDF 1.0 through 1.4 and still common in PDF 1.5+ for
/// compatibility.
///
/// Format in the PDF file:
/// <code>
/// xref
/// 0 6
/// 0000000000 65535 f
/// 0000000015 00000 n
/// 0000000108 00000 n
/// ...
/// </code>
///
/// Each section starts with an object number and count. Each entry is
/// exactly 20 bytes: 10-digit offset, space, 5-digit generation, space,
/// one-character type ('n' or 'f'), carriage-return or space, line-feed.
///
/// PDF 32000-1:2008 §7.5.4 — Cross-reference table.
/// </remarks>
public sealed class XrefTable
{
    private readonly Dictionary<int, XrefEntry> _entries;

    /// <summary>Creates an empty <see cref="XrefTable"/>.</summary>
    public XrefTable()
    {
        _entries = new Dictionary<int, XrefEntry>();

        // PDF 32000-1:2008 §7.5.4: object 0 is always the head of the free list.
        // Generation 65535 marks the permanent head of the free list.
        _entries[0] = XrefEntry.Free(0, 65535, 0);
    }

    /// <summary>Gets the number of entries in the table.</summary>
    public int Count => _entries.Count;

    // ── Mutation ──────────────────────────────────────────────────────────

    /// <summary>
    /// Adds or replaces an entry in the table.
    /// </summary>
    public void Set(XrefEntry entry)
    {
        _entries[entry.ObjectNumber] = entry;
    }

    /// <summary>
    /// Removes an entry from the table.
    /// The entry is NOT replaced with a free entry — use <see cref="Free"/>
    /// to mark an object as free.
    /// </summary>
    public bool Remove(int objectNumber)
    {
        return _entries.Remove(objectNumber);
    }

    /// <summary>
    /// Marks the given object as free, adding it to the free list.
    /// </summary>
    public void Free(int objectNumber, int generation)
    {
        _entries[objectNumber] = XrefEntry.Free(objectNumber, generation, 0);
    }

    // ── Lookup ────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to look up an entry by object number.
    /// </summary>
    public bool TryGet(int objectNumber, out XrefEntry entry)
    {
        return _entries.TryGetValue(objectNumber, out entry);
    }

    /// <summary>
    /// Returns true when the object number has an in-use entry.
    /// </summary>
    public bool Contains(int objectNumber)
    {
        return _entries.TryGetValue(objectNumber, out XrefEntry entry) && entry.IsInUse;
    }

    /// <summary>
    /// Gets the byte offset for an in-use object.
    /// Returns -1 when the object is not in the table or is free.
    /// </summary>
    public long GetOffset(int objectNumber)
    {
        if (_entries.TryGetValue(objectNumber, out XrefEntry entry) && entry.IsInUse)
        {
            return entry.ByteOffset;
        }

        return -1;
    }

    /// <summary>Gets all entries in the table.</summary>
    public IEnumerable<XrefEntry> Entries => _entries.Values;

    // ── Serialisation ─────────────────────────────────────────────────────

    /// <summary>
    /// Writes the xref table to <paramref name="output"/> in PDF classic format.
    /// Returns the byte offset of the xref keyword in the stream.
    /// </summary>
    /// <remarks>
    /// Entries are written in ascending object number order.
    /// Contiguous ranges are grouped into subsections as required by the spec.
    /// Each entry is exactly 20 bytes including the line ending.
    /// PDF 32000-1:2008 §7.5.4.
    /// </remarks>
    public long Write(Stream output)
    {
        if (output is null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        long xrefOffset = output.Position;

        // Collect and sort object numbers.
        List<int> objectNumbers = new List<int>(_entries.Keys);
        objectNumbers.Sort();

        // Write xref keyword.
        byte[] xrefLine = Encoding.ASCII.GetBytes("xref\n");
        output.Write(xrefLine, 0, xrefLine.Length);

        // Group into contiguous subsections.
        List<List<int>> subsections = BuildSubsections(objectNumbers);

        foreach (List<int> subsection in subsections)
        {
            // Write subsection header: first_object count\n
            string header = $"{subsection[0]} {subsection.Count}\n";
            byte[] headerBytes = Encoding.ASCII.GetBytes(header);
            output.Write(headerBytes, 0, headerBytes.Length);

            foreach (int objectNumber in subsection)
            {
                XrefEntry entry = _entries[objectNumber];
                byte[] entryBytes = FormatEntry(entry);
                output.Write(entryBytes, 0, entryBytes.Length);
            }
        }

        return xrefOffset;
    }

    /// <summary>
    /// Parses a classic xref table from <paramref name="input"/>.
    /// The stream must be positioned immediately after the 'xref' keyword.
    /// </summary>
    /// <returns>
    /// A populated <see cref="XrefTable"/>.
    /// </returns>
    /// <exception cref="PdfParseException">
    /// Thrown when the xref table is malformed.
    /// </exception>
    public static XrefTable Parse(Stream input)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        XrefTable table = new XrefTable();

        using (StreamReader reader = new StreamReader(input, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true))
        {
            while (true)
            {
                string? line = ReadLine(reader);

                if (line is null || line.StartsWith("trailer", StringComparison.Ordinal))
                {
                    break;
                }

                if (line.Length == 0)
                {
                    continue;
                }

                // Skip the "xref" keyword itself — the caller may pass the stream
                // positioned at the keyword OR after it. Both are accepted.
                // PDF 32000-1:2008 §7.5.4.
                if (line.Equals("xref", StringComparison.Ordinal))
                {
                    continue;
                }

                // Parse subsection header: first_object count
                string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length != 2)
                {
                    throw new PdfParseException(
                        $"Invalid xref subsection header: '{line}'");
                }

                if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int firstObject))
                {
                    throw new PdfParseException(
                        $"Invalid xref subsection first object: '{parts[0]}'");
                }

                if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int count))
                {
                    throw new PdfParseException(
                        $"Invalid xref subsection count: '{parts[1]}'");
                }

                for (int i = 0; i < count; i++)
                {
                    string? entryLine = ReadLine(reader);

                    if (entryLine is null || entryLine.Length < 18)
                    {
                        throw new PdfParseException(
                            $"Truncated xref entry for object {firstObject + i}.");
                    }

                    XrefEntry entry = ParseEntry(firstObject + i, entryLine);
                    table.Set(entry);
                }
            }
        }

        return table;
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private static List<List<int>> BuildSubsections(List<int> sortedNumbers)
    {
        List<List<int>> subsections = new List<List<int>>();

        if (sortedNumbers.Count == 0)
        {
            return subsections;
        }

        List<int> current = new List<int> { sortedNumbers[0] };

        for (int i = 1; i < sortedNumbers.Count; i++)
        {
            if (sortedNumbers[i] == sortedNumbers[i - 1] + 1)
            {
                current.Add(sortedNumbers[i]);
            }
            else
            {
                subsections.Add(current);
                current = new List<int> { sortedNumbers[i] };
            }
        }

        subsections.Add(current);
        return subsections;
    }

    private static byte[] FormatEntry(XrefEntry entry)
    {
        // PDF 32000-1:2008 §7.5.4: each entry is exactly 20 bytes.
        // Format: nnnnnnnnnn ggggg n\r\n  (exactly 20 bytes per ISO 32000-1
        // §7.5.4: 10-digit offset, space, 5-digit generation, space, type,
        // 2-byte EOL). The EOL is a bare CRLF — no space before it, or the
        // entry would be 21 bytes and strict readers (e.g. Acrobat) reject the
        // table and rebuild it, marking the file modified.
        string offsetOrNext = entry.IsFree
            ? ((long)entry.ByteOffset).ToString("D10", CultureInfo.InvariantCulture)
            : entry.ByteOffset.ToString("D10", CultureInfo.InvariantCulture);

        char type = entry.IsInUse ? 'n' : 'f';
        string formatted = $"{offsetOrNext} {entry.Generation:D5} {type}\r\n";
        return Encoding.ASCII.GetBytes(formatted);
    }

    private static XrefEntry ParseEntry(int objectNumber, string line)
    {
        // Format: nnnnnnnnnn ggggg n/f
        // The line may have various line endings; we just need the first 18+ chars.
        if (!long.TryParse(line[..10].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long offset))
        {
            throw new PdfParseException(
                $"Invalid xref offset in entry for object {objectNumber}: '{line}'");
        }

        if (!int.TryParse(line[11..16].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int generation))
        {
            throw new PdfParseException(
                $"Invalid xref generation in entry for object {objectNumber}: '{line}'");
        }

        char type = line[17];

        if (type == 'n')
        {
            return new XrefEntry(objectNumber, generation, offset);
        }

        if (type == 'f')
        {
            return XrefEntry.Free(objectNumber, generation, (int)offset);
        }

        throw new PdfParseException(
            $"Invalid xref entry type '{type}' for object {objectNumber}.");
    }

    private static string? ReadLine(StreamReader reader)
    {
        StringBuilder sb = new StringBuilder();

        while (true)
        {
            int ch = reader.Read();

            if (ch == -1)
            {
                return sb.Length > 0 ? sb.ToString().Trim() : null;
            }

            if (ch == '\n')
            {
                break;
            }

            if (ch == '\r')
            {
                int next = reader.Peek();

                if (next == '\n')
                {
                    reader.Read();
                }

                break;
            }

            sb.Append((char)ch);
        }

        return sb.ToString().Trim();
    }
}
