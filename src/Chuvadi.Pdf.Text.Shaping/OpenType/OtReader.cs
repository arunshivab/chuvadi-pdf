// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

namespace Chuvadi.Pdf.Text.Shaping.OpenType;

/// <summary>
/// Low-level helpers for reading OpenType/sfnt binary data.
/// All reads are big-endian, as per the OpenType specification.
/// </summary>
internal static class OtReader
{
    // Reads outside the font return 0, so a malformed table reads as empty
    // (HarfBuzz's Null objects) instead of throwing.

    /// <summary>Reads a byte, or 0 outside the data.</summary>
    internal static int U8(byte[] data, int offset)
        => (uint)offset < (uint)data.Length ? data[offset] : 0;

    /// <summary>Reads an unsigned 16-bit big-endian integer (0 outside the data).</summary>
    internal static int U16(byte[] data, int offset)
        => offset >= 0 && offset <= data.Length - 2 ? (data[offset] << 8) | data[offset + 1] : 0;

    /// <summary>Reads a signed 16-bit big-endian integer (0 outside the data).</summary>
    internal static int I16(byte[] data, int offset)
    {
        int raw = U16(data, offset);
        return raw >= 0x8000 ? raw - 0x10000 : raw;
    }

    /// <summary>Reads an unsigned 24-bit big-endian integer (0 outside the data).</summary>
    internal static int U24(byte[] data, int offset)
        => offset >= 0 && offset <= data.Length - 3 ? (data[offset] << 16) | (data[offset + 1] << 8) | data[offset + 2] : 0;

    /// <summary>Reads a 32-bit big-endian integer (0 outside the data).</summary>
    internal static int U32(byte[] data, int offset)
        => offset >= 0 && offset <= data.Length - 4
            ? (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]
            : 0;

    /// <summary>
    /// Locates the byte offset of a named table in an sfnt font.
    /// Returns -1 when the table is absent.
    /// </summary>
    internal static int FindTable(byte[] sfnt, string tag)
    {
        if (sfnt.Length < 12)
        {
            return -1;
        }

        int numTables = U16(sfnt, 4);
        byte b0 = (byte)tag[0];
        byte b1 = (byte)tag[1];
        byte b2 = (byte)tag[2];
        byte b3 = (byte)tag[3];

        for (int i = 0; i < numTables && 12 + (i * 16) + 16 <= sfnt.Length; i++)
        {
            int entry = 12 + i * 16;
            if (sfnt[entry] == b0 && sfnt[entry + 1] == b1
             && sfnt[entry + 2] == b2 && sfnt[entry + 3] == b3)
            {
                int offset = U32(sfnt, entry + 8);   // offset field
                return offset >= 0 && offset < sfnt.Length ? offset : -1;
            }
        }

        return -1;
    }
}
