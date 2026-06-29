// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;

namespace Chuvadi.Pdf.Text.Shaping.OpenType;

/// <summary>
/// Low-level helpers for reading OpenType/sfnt binary data.
/// All reads are big-endian, as per the OpenType specification.
/// </summary>
internal static class OtReader
{
    /// <summary>Reads an unsigned 16-bit big-endian integer.</summary>
    internal static int U16(byte[] data, int offset)
        => (data[offset] << 8) | data[offset + 1];

    /// <summary>Reads a signed 16-bit big-endian integer.</summary>
    internal static int I16(byte[] data, int offset)
    {
        int raw = (data[offset] << 8) | data[offset + 1];
        return raw >= 0x8000 ? raw - 0x10000 : raw;
    }

    /// <summary>Reads an unsigned 32-bit big-endian integer.</summary>
    internal static int U32(byte[] data, int offset)
        => (data[offset] << 24) | (data[offset + 1] << 16)
         | (data[offset + 2] << 8) | data[offset + 3];

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

        for (int i = 0; i < numTables; i++)
        {
            int entry = 12 + i * 16;
            if (sfnt[entry] == b0 && sfnt[entry + 1] == b1
             && sfnt[entry + 2] == b2 && sfnt[entry + 3] == b3)
            {
                return U32(sfnt, entry + 8);   // offset field
            }
        }

        return -1;
    }

    /// <summary>
    /// Builds a map of feature tag → list of lookup-list indices for the given
    /// script and language from a GSUB or GPOS table rooted at <paramref name="tableBase"/>.
    /// If the script or language is not found the default language system is used.
    /// </summary>
    internal static Dictionary<string, List<int>> ReadFeatureMap(
        byte[] data, int tableBase, string scriptTag, string langTag)
    {
        int scriptListOff = tableBase + U16(data, tableBase + 4);
        int featureListOff = tableBase + U16(data, tableBase + 6);

        // Find the script record
        int scriptCount = U16(data, scriptListOff);
        int scriptOff = -1;
        for (int i = 0; i < scriptCount; i++)
        {
            int b = scriptListOff + 2 + i * 6;
            if (MatchTag(data, b, scriptTag))
            {
                scriptOff = scriptListOff + U16(data, b + 4);
                break;
            }
        }

        // Fallback: use 'DFLT' script
        if (scriptOff < 0)
        {
            for (int i = 0; i < scriptCount; i++)
            {
                int b = scriptListOff + 2 + i * 6;
                if (MatchTag(data, b, "DFLT"))
                {
                    scriptOff = scriptListOff + U16(data, b + 4);
                    break;
                }
            }
        }

        if (scriptOff < 0)
        {
            return new Dictionary<string, List<int>>();
        }

        // Find the LangSys record; fall back to DefaultLangSys
        int defaultLangSysOff = U16(data, scriptOff);   // offset from scriptOff, 0 = none
        int langSysOff = -1;
        int langSysCount = U16(data, scriptOff + 2);
        for (int i = 0; i < langSysCount; i++)
        {
            int b = scriptOff + 4 + i * 6;
            if (MatchTag(data, b, langTag))
            {
                langSysOff = scriptOff + U16(data, b + 4);
                break;
            }
        }

        if (langSysOff < 0 && defaultLangSysOff != 0)
        {
            langSysOff = scriptOff + defaultLangSysOff;
        }

        if (langSysOff < 0)
        {
            return new Dictionary<string, List<int>>();
        }

        // LangSys: lookupOrderOff(2 ignored) requiredFeatureIdx(2) featureCount(2) featureIdx[](2)
        int requiredIdx = U16(data, langSysOff + 2);
        int featureCount = U16(data, langSysOff + 4);

        Dictionary<string, List<int>> map = new Dictionary<string, List<int>>();

        // Collect all feature indices (including the required one)
        List<int> featureIndices = new List<int>(featureCount + 1);
        if (requiredIdx != 0xFFFF)
        {
            featureIndices.Add(requiredIdx);
        }

        for (int i = 0; i < featureCount; i++)
        {
            featureIndices.Add(U16(data, langSysOff + 6 + i * 2));
        }

        // Map each feature index to its lookup list
        foreach (int fi in featureIndices)
        {
            int frBase = featureListOff + 2 + fi * 6;
            char c0 = (char)data[frBase];
            char c1 = (char)data[frBase + 1];
            char c2 = (char)data[frBase + 2];
            char c3 = (char)data[frBase + 3];
            string tag = new string(new[] { c0, c1, c2, c3 });
            int fOff = featureListOff + U16(data, frBase + 4);
            // Feature table: featureParamsOff(2 ignored) lookupCount(2) lookupIdx[](2)
            int lcount = U16(data, fOff + 2);
            if (!map.TryGetValue(tag, out List<int>? indices))
            {
                indices = new List<int>();
                map[tag] = indices;
            }

            for (int li = 0; li < lcount; li++)
            {
                indices.Add(U16(data, fOff + 4 + li * 2));
            }
        }

        return map;
    }

    private static bool MatchTag(byte[] data, int offset, string tag)
        => data[offset] == (byte)tag[0] && data[offset + 1] == (byte)tag[1]
        && data[offset + 2] == (byte)tag[2] && data[offset + 3] == (byte)tag[3];

    /// <summary>Counts the number of set bits in a 16-bit value (popcount).</summary>
    internal static int Popcount16(int v)
    {
        v = v - ((v >> 1) & 0x5555);
        v = (v & 0x3333) + ((v >> 2) & 0x3333);
        return ((v + (v >> 4)) & 0x0F0F) * 0x0101 >> 8;
    }
}
