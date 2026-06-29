// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

namespace Chuvadi.Pdf.Text.Shaping.OpenType;

/// <summary>
/// Parses and queries an OpenType Coverage table (formats 1 and 2) from raw
/// font bytes. Returns the coverage index for a glyph id, or -1 when not covered.
/// </summary>
internal sealed class CoverageTable
{
    private readonly byte[] _data;
    private readonly int _base;
    private readonly int _format;

    internal CoverageTable(byte[] data, int baseOffset)
    {
        _data = data;
        _base = baseOffset;
        _format = OtReader.U16(data, baseOffset);
    }

    /// <summary>
    /// Returns the coverage index of <paramref name="glyphId"/>, or -1 if not covered.
    /// </summary>
    internal int IndexOf(int glyphId)
    {
        if (_format == 1)
        {
            return IndexOfFormat1(glyphId);
        }

        if (_format == 2)
        {
            return IndexOfFormat2(glyphId);
        }

        return -1;
    }

    private int IndexOfFormat1(int glyphId)
    {
        int count = OtReader.U16(_data, _base + 2);
        // Binary search on sorted glyph array
        int lo = 0;
        int hi = count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            int gid = OtReader.U16(_data, _base + 4 + mid * 2);
            if (gid == glyphId)
            {
                return mid;
            }

            if (gid < glyphId)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return -1;
    }

    private int IndexOfFormat2(int glyphId)
    {
        int rangeCount = OtReader.U16(_data, _base + 2);
        // Binary search on range records: startGlyph(2) endGlyph(2) startCoverageIndex(2)
        int lo = 0;
        int hi = rangeCount - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            int rBase = _base + 4 + mid * 6;
            int start = OtReader.U16(_data, rBase);
            int end = OtReader.U16(_data, rBase + 2);
            if (glyphId < start)
            {
                hi = mid - 1;
            }
            else if (glyphId > end)
            {
                lo = mid + 1;
            }
            else
            {
                int startIndex = OtReader.U16(_data, rBase + 4);
                return startIndex + (glyphId - start);
            }
        }

        return -1;
    }
}
