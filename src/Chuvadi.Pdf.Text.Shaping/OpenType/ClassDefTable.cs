// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

namespace Chuvadi.Pdf.Text.Shaping.OpenType;

/// <summary>
/// Parses and queries an OpenType ClassDef table (formats 1 and 2).
/// Glyphs not in any class return class 0 (default class), as per the spec.
/// </summary>
internal sealed class ClassDefTable
{
    private readonly byte[] _data;
    private readonly int _base;
    private readonly int _format;

    internal ClassDefTable(byte[] data, int baseOffset)
    {
        _data = data;
        _base = baseOffset;
        _format = OtReader.U16(data, baseOffset);
    }

    /// <summary>Returns the class value for <paramref name="glyphId"/> (0 = default).</summary>
    internal int ClassOf(int glyphId)
    {
        if (_format == 1)
        {
            return ClassOfFormat1(glyphId);
        }

        if (_format == 2)
        {
            return ClassOfFormat2(glyphId);
        }

        return 0;
    }

    private int ClassOfFormat1(int glyphId)
    {
        int startGlyph = OtReader.U16(_data, _base + 2);
        int count = OtReader.U16(_data, _base + 4);
        int idx = glyphId - startGlyph;
        if (idx < 0 || idx >= count)
        {
            return 0;
        }

        return OtReader.U16(_data, _base + 6 + idx * 2);
    }

    private int ClassOfFormat2(int glyphId)
    {
        int rangeCount = OtReader.U16(_data, _base + 2);
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
                return OtReader.U16(_data, rBase + 4);
            }
        }

        return 0;
    }
}
