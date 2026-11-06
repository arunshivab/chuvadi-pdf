// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.2.6 — shared helper for incremental signing operations
//
// Internal helper shared by PdfSigner (full-document signing), PdfCounterSigner
// (adding a second signature via incremental update), PdfDocumentTimestamper
// (/DocTimeStamp via incremental update), and PdfLtvUpdater (VRI emission via
// incremental update). Centralises the byte-range placeholder locating and
// patching logic so the four call sites can stay in lockstep.
// 3.31.2: the placeholders are located inside the signature dictionary's own
// object, found through the written cross-reference table.

using System;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Signatures.Signing;

/// <summary>Byte-range placeholder utilities shared across signing operations.</summary>
internal static class SignatureContentsHelper
{
    internal const int ByteRangeSlotWidth = 10;
    internal const int ByteRangePlaceholderValue = 999_999_999;

    /// <summary>
    /// Returns the byte offset of indirect object <paramref name="objectNumber"/>
    /// (generation 0) in <paramref name="bytes"/>, found through the file's last
    /// classic cross-reference table, or -1 when it cannot be found.
    /// </summary>
    /// <remarks>
    /// 3.31.2 (D-207 item 1): the signers find their own signature dictionary
    /// this way. Before, they took the first <c>/ByteRange</c> and
    /// <c>/Contents</c> in the file, which on any document whose pages have a
    /// <c>/Contents</c> entry is a page, not the signature.
    /// </remarks>
    internal static int LocateObjectStart(byte[] bytes, int objectNumber)
    {
        long xrefOffset = ReadLastStartXref(bytes);
        if (xrefOffset < 0 || xrefOffset > bytes.Length - 4 || !StartsWith(bytes, (int)xrefOffset, "xref"u8))
        {
            return -1;
        }

        XrefTable table;
        try
        {
            using MemoryStream stream = new MemoryStream(bytes, writable: false);
            stream.Position = xrefOffset + 4;
            table = XrefTable.Parse(stream);
        }
        catch (PdfParseException)
        {
            return -1;
        }

        if (!table.TryGet(objectNumber, out XrefEntry entry)
            || entry.Type != XrefEntryType.InUse
            || entry.ByteOffset < 0
            || entry.ByteOffset >= bytes.Length)
        {
            return -1;
        }

        int offset = (int)entry.ByteOffset;
        byte[] header = Encoding.ASCII.GetBytes(
            objectNumber.ToString(CultureInfo.InvariantCulture) + " " + entry.Generation.ToString(CultureInfo.InvariantCulture) + " obj");
        return StartsWith(bytes, offset, header) ? offset : -1;
    }

    /// <summary>
    /// Locates the opening bracket of the <c>/ByteRange</c> array inside the
    /// object that starts at <paramref name="objectStart"/>, or -1.
    /// </summary>
    internal static int LocateByteRangeArrayStart(byte[] bytes, int objectStart) =>
        LocateValueStart(bytes, objectStart, "/ByteRange"u8, (byte)'[');

    /// <summary>
    /// Locates the opening <c>&lt;</c> of the <c>/Contents</c> hex string inside
    /// the object that starts at <paramref name="objectStart"/>, or -1.
    /// </summary>
    internal static int LocateContentsHexStart(byte[] bytes, int objectStart) =>
        LocateValueStart(bytes, objectStart, "/Contents"u8, (byte)'<');

    // Finds key inside [objectStart, endobj) and returns the position of the
    // delimiter that opens its value, allowing only whitespace in between.
    private static int LocateValueStart(byte[] bytes, int objectStart, ReadOnlySpan<byte> key, byte opener)
    {
        if (objectStart < 0 || objectStart >= bytes.Length)
        {
            return -1;
        }

        ReadOnlySpan<byte> rest = bytes.AsSpan(objectStart);
        int end = rest.IndexOf("endobj"u8);
        ReadOnlySpan<byte> body = end < 0 ? rest : rest.Slice(0, end);
        int idx = body.IndexOf(key);
        if (idx < 0)
        {
            return -1;
        }

        for (int p = idx + key.Length; p < body.Length; p++)
        {
            byte b = body[p];
            if (b == opener)
            {
                return objectStart + p;
            }

            if (b != (byte)' ' && b != (byte)'\t' && b != (byte)'\r' && b != (byte)'\n')
            {
                return -1;
            }
        }

        return -1;
    }

    private static long ReadLastStartXref(byte[] bytes)
    {
        int idx = bytes.AsSpan().LastIndexOf("startxref"u8);
        if (idx < 0)
        {
            return -1;
        }

        int p = idx + "startxref".Length;
        while (p < bytes.Length && (bytes[p] == (byte)' ' || bytes[p] == (byte)'\t' || bytes[p] == (byte)'\r' || bytes[p] == (byte)'\n'))
        {
            p++;
        }

        long value = 0;
        int digits = 0;
        while (p < bytes.Length && bytes[p] >= (byte)'0' && bytes[p] <= (byte)'9' && digits < 18)
        {
            value = (value * 10) + (bytes[p] - (byte)'0');
            p++;
            digits++;
        }

        return digits == 0 ? -1 : value;
    }

    private static bool StartsWith(byte[] bytes, int offset, ReadOnlySpan<byte> prefix) =>
        offset >= 0 && offset <= bytes.Length - prefix.Length && bytes.AsSpan(offset, prefix.Length).SequenceEqual(prefix);

    /// <summary>Replaces the four placeholder integers in the <c>/ByteRange</c> array.</summary>
    /// <remarks>
    /// Layout written by the array emitter: <c>[v0 v1 v2 v3]</c> where each
    /// vN is a zero-padded ByteRangeSlotWidth-character integer and the
    /// separators are single spaces. We assume the placeholder was emitted by
    /// the array writer with <see cref="PdfPaddedInteger"/>
    /// at this width.
    /// </remarks>
    internal static void PatchByteRange(
        byte[] bytes, int arrayStart, int v0, int v1, int v2, int v3)
    {
        if (bytes[arrayStart] != (byte)'[')
        {
            throw new InvalidOperationException(
                $"Expected '[' at byte {arrayStart}, got 0x{bytes[arrayStart]:X2}.");
        }
        int p = arrayStart + 1;
        WriteIntPadded(bytes, p, v0, ByteRangeSlotWidth); p += ByteRangeSlotWidth;
        p++; // separator
        WriteIntPadded(bytes, p, v1, ByteRangeSlotWidth); p += ByteRangeSlotWidth;
        p++;
        WriteIntPadded(bytes, p, v2, ByteRangeSlotWidth); p += ByteRangeSlotWidth;
        p++;
        WriteIntPadded(bytes, p, v3, ByteRangeSlotWidth);
    }

    private static void WriteIntPadded(byte[] bytes, int offset, int value, int width)
    {
        if (value < 0) { throw new ArgumentOutOfRangeException(nameof(value)); }
        for (int i = width - 1; i >= 0; i--)
        {
            bytes[offset + i] = (byte)('0' + (value % 10));
            value /= 10;
        }
        if (value != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value), $"Value does not fit in {width} digits.");
        }
    }

    /// <summary>
    /// Writes <paramref name="data"/> as ASCII hex at the given byte position.
    /// </summary>
    internal static void WriteHexAt(byte[] bytes, int offset, byte[] data)
    {
        const string Digits = "0123456789ABCDEF";
        for (int i = 0; i < data.Length; i++)
        {
            byte b = data[i];
            bytes[offset + (2 * i)] = (byte)Digits[b >> 4];
            bytes[offset + (2 * i) + 1] = (byte)Digits[b & 0x0F];
        }
    }

    /// <summary>Formats a <see cref="DateTimeOffset"/> as a PDF date string.</summary>
    internal static string FormatPdfDate(DateTimeOffset time)
    {
        StringBuilder sb = new();
        sb.Append("D:");
        sb.Append(time.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture));
        TimeSpan offset = time.Offset;
        if (offset == TimeSpan.Zero) { sb.Append('Z'); }
        else
        {
            sb.Append(offset.Ticks >= 0 ? '+' : '-');
            sb.Append(Math.Abs(offset.Hours).ToString("D2", CultureInfo.InvariantCulture));
            sb.Append('\'');
            sb.Append(Math.Abs(offset.Minutes).ToString("D2", CultureInfo.InvariantCulture));
            sb.Append('\'');
        }
        return sb.ToString();
    }
}
