// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  OpenType 1.9 — Font file structure, TrueType/OpenType Collections
// PHASE: 3.25 — font-face API

using System;
using System.Collections.Generic;
using System.Text;

namespace Chuvadi.Pdf.Fonts.Rendering;

// Table directory reading and collection (.ttc/.otc) face extraction.
internal static class SfntFile
{
    internal const uint TrueTypeTag = 0x00010000;
    internal const uint OttoTag = 0x4F54544F;
    internal const uint TtcfTag = 0x74746366;

    internal static bool IsCollection(byte[] data) => data.Length >= 12 && U32(data, 0) == TtcfTag;

    internal static int FaceCount(byte[] data)
    {
        if (data.Length < 12)
        {
            throw new FontRenderingException("Font data is too short.");
        }

        uint tag = U32(data, 0);
        if (tag == TtcfTag)
        {
            int count = (int)Math.Min(U32(data, 8), int.MaxValue);
            if (count <= 0 || 12 + ((long)count * 4) > data.Length)
            {
                throw new FontRenderingException("Font collection header is invalid.");
            }

            return count;
        }

        if (tag == TrueTypeTag || tag == OttoTag || tag == 0x74727565 /* true */)
        {
            return 1;
        }

        throw new FontRenderingException($"Not a TrueType/OpenType font or collection (tag 0x{tag:X8}).");
    }

    // Returns the face as a standalone sfnt: the data itself for a single
    // font, a rebuilt file (table directory plus copied tables) for a face of
    // a collection.
    internal static byte[] ExtractFace(byte[] data, int faceIndex)
    {
        int count = FaceCount(data);
        if (faceIndex < 0 || faceIndex >= count)
        {
            throw new ArgumentOutOfRangeException(nameof(faceIndex), $"Face index {faceIndex} is outside [0, {count}).");
        }

        if (!IsCollection(data))
        {
            return data;
        }

        int dir = (int)U32(data, 12 + (faceIndex * 4));
        CheckRange(data, dir, 12);
        uint sfntVersion = U32(data, dir);
        int numTables = U16(data, dir + 4);
        CheckRange(data, dir + 12, numTables * 16);

        List<(byte[] Tag, uint Checksum, int Offset, int Length)> tables = new List<(byte[] Tag, uint Checksum, int Offset, int Length)>(numTables);
        for (int i = 0; i < numTables; i++)
        {
            int rec = dir + 12 + (i * 16);
            int offset = (int)U32(data, rec + 8), length = (int)U32(data, rec + 12);
            CheckRange(data, offset, length);
            tables.Add((data.AsSpan(rec, 4).ToArray(), U32(data, rec + 4), offset, length));
        }

        int headerSize = 12 + (numTables * 16);
        int total = headerSize;
        foreach ((byte[] _, uint _, int _, int length) in tables)
        {
            total += (length + 3) & ~3;
        }

        byte[] output = new byte[total];
        Array.Copy(data, dir, output, 0, 12);
        WriteU32(output, 0, sfntVersion);
        int cursor = headerSize;
        for (int i = 0; i < tables.Count; i++)
        {
            int rec = 12 + (i * 16);
            Array.Copy(tables[i].Tag, 0, output, rec, 4);
            WriteU32(output, rec + 4, tables[i].Checksum);
            WriteU32(output, rec + 8, (uint)cursor);
            WriteU32(output, rec + 12, (uint)tables[i].Length);
            Array.Copy(data, tables[i].Offset, output, cursor, tables[i].Length);
            cursor += (tables[i].Length + 3) & ~3;
        }

        return output;
    }

    // Table tag -> (offset, length) of a standalone sfnt.
    internal static Dictionary<string, (int Offset, int Length)> Tables(byte[] sfnt)
    {
        CheckRange(sfnt, 0, 12);
        int numTables = U16(sfnt, 4);
        CheckRange(sfnt, 12, numTables * 16);
        Dictionary<string, (int Offset, int Length)> tables = new Dictionary<string, (int Offset, int Length)>(StringComparer.Ordinal);
        for (int i = 0; i < numTables; i++)
        {
            int rec = 12 + (i * 16);
            int offset = (int)U32(sfnt, rec + 8), length = (int)U32(sfnt, rec + 12);
            if (offset >= 0 && length >= 0 && (long)offset + length <= sfnt.Length)
            {
                tables[Encoding.ASCII.GetString(sfnt, rec, 4)] = (offset, length);
            }
        }

        return tables;
    }

    internal static int U16(byte[] b, int o) => (b[o] << 8) | b[o + 1];

    internal static short S16(byte[] b, int o) => (short)((b[o] << 8) | b[o + 1]);

    internal static uint U32(byte[] b, int o) => (uint)((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]);

    internal static void WriteU32(byte[] b, int o, uint v)
    {
        b[o] = (byte)(v >> 24);
        b[o + 1] = (byte)(v >> 16);
        b[o + 2] = (byte)(v >> 8);
        b[o + 3] = (byte)v;
    }

    private static void CheckRange(byte[] data, int offset, int length)
    {
        if (offset < 0 || length < 0 || (long)offset + length > data.Length)
        {
            throw new FontRenderingException("Font table directory points outside the file.");
        }
    }
}
