// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  OpenType spec (https://docs.microsoft.com/typography/opentype/spec)
//        §head — Font Header; §hhea — Horizontal Header
//        §maxp — Maximum Profile; §loca — Index to Location
//        §glyf — Glyph Data; §hmtx — Horizontal Metrics
//        §cmap — Character to Glyph Index Mapping (format 4)
// PHASE: Phase 2 — Chuvadi.Pdf.Fonts.Rendering
// Parses a TrueType/OpenType font from raw bytes and extracts glyph outlines.

using System;
using System.Collections.Generic;
using System.Text;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Fonts.Rendering;

/// <summary>
/// Loads a TrueType or OpenType font from raw bytes and provides access
/// to glyph outlines and metrics.
/// </summary>
/// <remarks>
/// Parses the following required tables:
/// head (font header, unitsPerEm, loca format),
/// hhea (numberOfHMetrics),
/// maxp (numGlyphs),
/// loca (glyph offsets),
/// glyf (glyph contour data),
/// hmtx (advance widths and left side bearings),
/// cmap (character → glyph index mapping, format 4 preferred).
///
/// Supports simple glyphs and composite glyphs (one level deep).
/// Quadratic Bezier curves (TrueType) are converted to cubic for
/// compatibility with the Graphics Path layer.
///
/// OpenType specification — https://docs.microsoft.com/typography/opentype/spec/
/// </remarks>
public sealed class TrueTypeLoader
{
    private readonly byte[] _data;

    // Parsed table offsets
    private uint _headOffset;
    private uint _hheaOffset;
    private uint _maxpOffset;
    private uint _locaOffset;
    private uint _glyfOffset;
    private uint _hmtxOffset;
    private uint _cmapOffset;

    // Parsed header values
    private int _unitsPerEm;
    private int _numGlyphs;
    private int _numberOfHMetrics;
    private bool _longLoca; // false = short (uint16 * 2), true = long (uint32)

    // cmap: Unicode BMP → glyph index (format 4)
    private Dictionary<int, int>? _cmapF4;

    /// <summary>
    /// Loads a font from raw TTF/OTF bytes.
    /// </summary>
    /// <param name="fontData">The raw font file bytes.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="fontData"/> is null.
    /// </exception>
    /// <exception cref="FontRenderingException">
    /// Thrown when the font data is invalid or missing required tables.
    /// </exception>
    public TrueTypeLoader(byte[] fontData)
    {
        _data = fontData ?? throw new ArgumentNullException(nameof(fontData));
        ParseOffsetTable();
        ParseHead();
        ParseHhea();
        ParseMaxp();
        ParseCmap();
    }

    /// <summary>Gets the number of font design units per em square.</summary>
    public int UnitsPerEm => _unitsPerEm;

    /// <summary>Gets the total number of glyphs in the font.</summary>
    public int NumGlyphs => _numGlyphs;

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Maps a Unicode code point to its glyph index.
    /// Returns 0 (the .notdef glyph) when the character is not present.
    /// </summary>
    public int GetGlyphIndex(int codePoint)
    {
        if (_cmapF4 is null)
        {
            return 0;
        }

        if (_cmapF4.TryGetValue(codePoint, out int glyphId))
        {
            return glyphId;
        }

        return 0;
    }

    /// <summary>
    /// Extracts the outline and metrics for a glyph by glyph index.
    /// Returns an empty outline for whitespace or missing glyphs.
    /// </summary>
    /// <param name="glyphId">Zero-based glyph index.</param>
    public GlyphOutline GetGlyphOutline(int glyphId)
    {
        if (glyphId < 0 || glyphId >= _numGlyphs)
        {
            throw new FontRenderingException(
                $"Glyph index {glyphId} is out of range [0, {_numGlyphs}).");
        }

        GlyphMetrics metrics = GetGlyphMetrics(glyphId);
        Path path = BuildGlyphPath(glyphId);
        return new GlyphOutline(path, metrics);
    }

    /// <summary>
    /// Returns the typographic metrics for a glyph without building its path.
    /// Useful for text advance width calculations.
    /// </summary>
    public GlyphMetrics GetGlyphMetrics(int glyphId)
    {
        if (glyphId < 0 || glyphId >= _numGlyphs)
        {
            throw new FontRenderingException(
                $"Glyph index {glyphId} is out of range [0, {_numGlyphs}).");
        }

        // hmtx: if glyphId >= numberOfHMetrics, use the last advance width
        int hmtxIdx = glyphId < _numberOfHMetrics ? glyphId : _numberOfHMetrics - 1;
        uint hmtxEntry = _hmtxOffset + (uint)(hmtxIdx * 4);
        int advanceWidth = ReadUInt16(_hmtxOffset + (uint)(hmtxIdx * 4));
        int lsb;

        if (glyphId < _numberOfHMetrics)
        {
            lsb = ReadInt16(hmtxEntry + 2);
        }
        else
        {
            // Extra LSBs follow the advance-width array
            uint lsbOffset = _hmtxOffset + (uint)(_numberOfHMetrics * 4)
                           + (uint)((glyphId - _numberOfHMetrics) * 2);
            lsb = ReadInt16(lsbOffset);
        }

        // Glyph bounding box from glyf table header (if glyph exists)
        RectangleF bounds = GetGlyfBounds(glyphId);

        return new GlyphMetrics(advanceWidth, lsb, _unitsPerEm, bounds);
    }

    // ── Offset table and table directory ─────────────────────────────────

    private void ParseOffsetTable()
    {
        // sfVersion (4 bytes): 0x00010000 = TrueType, 0x4F54544F = OTF/CFF
        uint sfVersion = ReadUInt32(0);

        if (sfVersion != 0x00010000 && sfVersion != 0x4F54544F &&
            sfVersion != 0x74727565 && sfVersion != 0x74797031)
        {
            throw new FontRenderingException(
                $"Not a valid TrueType/OpenType font. sfVersion = 0x{sfVersion:X8}.");
        }

        int numTables = ReadUInt16(4);

        for (int i = 0; i < numTables; i++)
        {
            uint entryOffset = 12 + (uint)(i * 16);
            string tag = ReadTag(entryOffset);
            uint tableOffset = ReadUInt32(entryOffset + 8);

            switch (tag)
            {
                case "head": _headOffset = tableOffset; break;
                case "hhea": _hheaOffset = tableOffset; break;
                case "maxp": _maxpOffset = tableOffset; break;
                case "loca": _locaOffset = tableOffset; break;
                case "glyf": _glyfOffset = tableOffset; break;
                case "hmtx": _hmtxOffset = tableOffset; break;
                case "cmap": _cmapOffset = tableOffset; break;
            }
        }

        if (_headOffset == 0) { throw new FontRenderingException("Font missing required 'head' table."); }
        if (_maxpOffset == 0) { throw new FontRenderingException("Font missing required 'maxp' table."); }
        if (_hmtxOffset == 0) { throw new FontRenderingException("Font missing required 'hmtx' table."); }
    }

    private void ParseHead()
    {
        // head table: offset 18 = unitsPerEm (uint16), offset 50 = indexToLocFormat (int16)
        _unitsPerEm = ReadUInt16(_headOffset + 18);
        int indexToLocFormat = ReadInt16(_headOffset + 50);
        _longLoca = indexToLocFormat == 1;
    }

    private void ParseHhea()
    {
        // hhea table: offset 34 = numberOfHMetrics (uint16)
        _numberOfHMetrics = ReadUInt16(_hheaOffset + 34);
    }

    private void ParseMaxp()
    {
        // maxp table: offset 4 = numGlyphs (uint16)
        _numGlyphs = ReadUInt16(_maxpOffset + 4);
    }

    // ── cmap — Character to glyph index mapping ───────────────────────────

    private void ParseCmap()
    {
        if (_cmapOffset == 0)
        {
            return;
        }

        int numTables = ReadUInt16(_cmapOffset + 2);

        // Prefer platform 3 (Windows) encoding 1 (BMP Unicode), then platform 0 (Unicode)
        uint bestOffset = 0;
        int bestScore = -1;

        for (int i = 0; i < numTables; i++)
        {
            uint recordOffset = _cmapOffset + 4 + (uint)(i * 8);
            int platformId = ReadUInt16(recordOffset);
            int encodingId = ReadUInt16(recordOffset + 2);
            uint subtableOffset = _cmapOffset + ReadUInt32(recordOffset + 4);
            int format = ReadUInt16(subtableOffset);

            int score = -1;

            if (platformId == 3 && encodingId == 1 && format == 4)
            {
                score = 10; // Best: Windows Unicode BMP format 4
            }
            else if (platformId == 0 && format == 4)
            {
                score = 5; // Good: Unicode platform format 4
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestOffset = subtableOffset;
            }
        }

        if (bestOffset == 0 || bestScore < 0)
        {
            return; // No supported cmap found
        }

        ParseCmapFormat4(bestOffset);
    }

    private void ParseCmapFormat4(uint offset)
    {
        // format 4: segmented mapping to delta values
        // OpenType spec §cmap, format 4
        int segCountX2 = ReadUInt16(offset + 6);
        int segCount = segCountX2 / 2;

        uint endCodesOffset = offset + 14;
        uint startCodesOffset = endCodesOffset + (uint)(segCount * 2) + 2;
        uint deltaOffset = startCodesOffset + (uint)(segCount * 2);
        uint rangeOffset = deltaOffset + (uint)(segCount * 2);
        uint glyphIdArrayBase = rangeOffset + (uint)(segCount * 2);

        _cmapF4 = new Dictionary<int, int>(segCount * 32);

        for (int seg = 0; seg < segCount; seg++)
        {
            int endCode = ReadUInt16(endCodesOffset + (uint)(seg * 2));
            int startCode = ReadUInt16(startCodesOffset + (uint)(seg * 2));
            int delta = ReadInt16(deltaOffset + (uint)(seg * 2));
            int rangeOff = ReadUInt16(rangeOffset + (uint)(seg * 2));

            if (startCode == 0xFFFF)
            {
                break; // End of segment list
            }

            for (int c = startCode; c <= endCode; c++)
            {
                int glyphId;

                if (rangeOff == 0)
                {
                    glyphId = (c + delta) & 0xFFFF;
                }
                else
                {
                    // idRangeOffset is a byte offset from the rangeOffset field itself
                    uint glyphIdOffset = rangeOffset + (uint)(seg * 2)
                                       + (uint)rangeOff
                                       + (uint)((c - startCode) * 2);
                    glyphId = ReadUInt16(glyphIdOffset);

                    if (glyphId != 0)
                    {
                        glyphId = (glyphId + delta) & 0xFFFF;
                    }
                }

                if (glyphId != 0 && !_cmapF4.ContainsKey(c))
                {
                    _cmapF4[c] = glyphId;
                }
            }
        }
    }

    // ── glyf table — Glyph outline extraction ─────────────────────────────

    private uint GetGlyfOffset(int glyphId)
    {
        if (_locaOffset == 0 || _glyfOffset == 0)
        {
            return 0;
        }

        uint locaEntry;
        uint locaNext;

        if (_longLoca)
        {
            locaEntry = ReadUInt32(_locaOffset + (uint)(glyphId * 4));
            locaNext = ReadUInt32(_locaOffset + (uint)((glyphId + 1) * 4));
        }
        else
        {
            locaEntry = (uint)(ReadUInt16(_locaOffset + (uint)(glyphId * 2)) * 2);
            locaNext = (uint)(ReadUInt16(_locaOffset + (uint)((glyphId + 1) * 2)) * 2);
        }

        if (locaEntry == locaNext)
        {
            return 0; // Empty glyph (whitespace)
        }

        return _glyfOffset + locaEntry;
    }

    private RectangleF GetGlyfBounds(int glyphId)
    {
        uint offset = GetGlyfOffset(glyphId);

        if (offset == 0)
        {
            return RectangleF.Zero;
        }

        int xMin = ReadInt16(offset + 2);
        int yMin = ReadInt16(offset + 4);
        int xMax = ReadInt16(offset + 6);
        int yMax = ReadInt16(offset + 8);

        return RectangleF.FromCorners(xMin, yMin, xMax, yMax);
    }

    private Path BuildGlyphPath(int glyphId)
    {
        uint offset = GetGlyfOffset(glyphId);

        if (offset == 0)
        {
            return new Path(); // Empty / whitespace glyph
        }

        int numberOfContours = ReadInt16(offset);

        if (numberOfContours >= 0)
        {
            return BuildSimpleGlyph(offset, numberOfContours);
        }

        return BuildCompositeGlyph(offset);
    }

    // ── Simple glyph parsing ──────────────────────────────────────────────

    private Path BuildSimpleGlyph(uint offset, int numberOfContours)
    {
        if (numberOfContours == 0)
        {
            return new Path();
        }

        // End-point indices of each contour
        int[] endPtsOfContours = new int[numberOfContours];

        for (int i = 0; i < numberOfContours; i++)
        {
            endPtsOfContours[i] = ReadUInt16(offset + 10 + (uint)(i * 2));
        }

        int numPoints = endPtsOfContours[numberOfContours - 1] + 1;

        // Instruction length (skip instructions)
        int instructionLength = ReadUInt16(offset + 10 + (uint)(numberOfContours * 2));
        uint flagsOffset = offset + 10 + (uint)(numberOfContours * 2) + 2 + (uint)instructionLength;

        // Parse flags
        byte[] flags = ParseFlags(flagsOffset, numPoints, out uint afterFlags);

        // Parse X coordinates
        int[] xCoords = ParseCoordinates(afterFlags, flags, numPoints, true, out uint afterX);

        // Parse Y coordinates
        int[] yCoords = ParseCoordinates(afterX, flags, numPoints, false, out uint _);

        // Build path from contours
        return ConvertContoursToPath(endPtsOfContours, flags, xCoords, yCoords);
    }

    private byte[] ParseFlags(uint offset, int numPoints, out uint nextOffset)
    {
        byte[] flags = new byte[numPoints];
        int i = 0;

        while (i < numPoints)
        {
            byte flag = ReadByte(offset++);
            flags[i++] = flag;

            // Bit 3: repeat flag
            if ((flag & 0x08) != 0)
            {
                byte repeatCount = ReadByte(offset++);

                for (int r = 0; r < repeatCount && i < numPoints; r++)
                {
                    flags[i++] = flag;
                }
            }
        }

        nextOffset = offset;
        return flags;
    }

    private int[] ParseCoordinates(
        uint offset, byte[] flags, int numPoints, bool isX, out uint nextOffset)
    {
        int[] coords = new int[numPoints];
        int current = 0;
        int shortBit = isX ? 0x02 : 0x04; // bit 1 (x-Short) or bit 2 (y-Short)
        int sameBit = isX ? 0x10 : 0x20; // bit 4 (x-Same)  or bit 5 (y-Same)

        for (int i = 0; i < numPoints; i++)
        {
            byte flag = flags[i];

            if ((flag & shortBit) != 0)
            {
                // 1-byte delta; positive if same bit set
                int delta = ReadByte(offset++);

                if ((flag & sameBit) == 0)
                {
                    delta = -delta;
                }

                current += delta;
            }
            else if ((flag & sameBit) != 0)
            {
                // Same as previous (delta = 0)
            }
            else
            {
                // 2-byte signed delta
                current += ReadInt16(offset);
                offset += 2;
            }

            coords[i] = current;
        }

        nextOffset = offset;
        return coords;
    }

    // ── TrueType contour → cubic Path conversion ──────────────────────────

    /// <summary>
    /// Converts TrueType quadratic Bezier contours to cubic Bezier Path segments.
    /// TrueType uses on-curve and off-curve (control) points. Two consecutive
    /// off-curve points imply a virtual on-curve point at their midpoint.
    /// Quadratic B→C curves are converted to cubic using the standard formula:
    ///   CP1 = Start + 2/3 * (Control - Start)
    ///   CP2 = End   + 2/3 * (Control - End)
    /// OpenType spec §glyf.
    /// </summary>
    private static Path ConvertContoursToPath(
        int[] endPts, byte[] flags, int[] xCoords, int[] yCoords)
    {
        Path path = new Path();
        int startIdx = 0;

        for (int contour = 0; contour < endPts.Length; contour++)
        {
            int endIdx = endPts[contour];
            int count = endIdx - startIdx + 1;

            if (count < 2)
            {
                startIdx = endIdx + 1;
                continue;
            }

            // Collect points for this contour
            double[] px = new double[count];
            double[] py = new double[count];
            bool[] onCurve = new bool[count];

            for (int i = 0; i < count; i++)
            {
                px[i] = xCoords[startIdx + i];
                py[i] = yCoords[startIdx + i];
                onCurve[i] = (flags[startIdx + i] & 0x01) != 0;
            }

            // Find starting on-curve point
            int start = 0;

            for (int i = 0; i < count; i++)
            {
                if (onCurve[i])
                {
                    start = i;
                    break;
                }
            }

            // MoveTo the starting point
            double startX = onCurve[start]
                ? px[start]
                : (px[start] + px[(start + count - 1) % count]) / 2.0;
            double startY = onCurve[start]
                ? py[start]
                : (py[start] + py[(start + count - 1) % count]) / 2.0;

            path.MoveTo(startX, startY);

            // Current pen position. Updated after EVERY segment (line or curve)
            // so each quadratic is anchored at the actual preceding point, not
            // the contour start. Failing to advance this after a LineTo is what
            // produced spurious spikes on serifs (line-to-curve junctions).
            double curX = startX;
            double curY = startY;

            int idx = start;

            for (int step = 0; step < count; step++)
            {
                int next = (idx + 1) % count;

                if (onCurve[next])
                {
                    // Straight line to on-curve point
                    path.LineTo(px[next], py[next]);
                    curX = px[next];
                    curY = py[next];
                    idx = next;
                }
                else
                {
                    // Off-curve: collect run of off-curve points
                    double qx = px[next];
                    double qy = py[next];
                    int after = (next + 1) % count;

                    while (!onCurve[after] && after != start)
                    {
                        // Implied on-curve at midpoint
                        double midX = (qx + px[after]) / 2.0;
                        double midY = (qy + py[after]) / 2.0;
                        EmitQuadraticAsCubic(path, curX, curY, qx, qy, midX, midY);
                        curX = midX;
                        curY = midY;
                        qx = px[after];
                        qy = py[after];
                        after = (after + 1) % count;
                    }

                    double endX = onCurve[after] ? px[after] : (qx + px[after]) / 2.0;
                    double endY = onCurve[after] ? py[after] : (qy + py[after]) / 2.0;
                    EmitQuadraticAsCubic(path, curX, curY, qx, qy, endX, endY);
                    curX = endX;
                    curY = endY;
                    idx = after;
                    step += (after - next + count) % count;
                }
            }

            path.ClosePath();
            startIdx = endIdx + 1;
        }

        return path;
    }

    private static void EmitQuadraticAsCubic(
        Path path,
        double p0x, double p0y,
        double p1x, double p1y, // quadratic control point
        double p2x, double p2y)
    {
        // Quadratic → cubic: CP1 = P0 + 2/3*(P1-P0), CP2 = P2 + 2/3*(P1-P2)
        double cp1x = p0x + (2.0 / 3.0) * (p1x - p0x);
        double cp1y = p0y + (2.0 / 3.0) * (p1y - p0y);
        double cp2x = p2x + (2.0 / 3.0) * (p1x - p2x);
        double cp2y = p2y + (2.0 / 3.0) * (p1y - p2y);

        path.CubicBezierTo(
            new PointF(cp1x, cp1y),
            new PointF(cp2x, cp2y),
            new PointF(p2x, p2y));
    }

    // ── Composite glyph ───────────────────────────────────────────────────

    private Path BuildCompositeGlyph(uint offset)
    {
        Path composite = new Path();
        uint pos = offset + 10; // Skip glyph header

        while (true)
        {
            int componentFlags = ReadUInt16(pos);
            int componentGlyphId = ReadUInt16(pos + 2);
            pos += 4;

            bool argsAreWords = (componentFlags & 0x0001) != 0;
            bool argsAreXY = (componentFlags & 0x0002) != 0;
            bool moreComponents = (componentFlags & 0x0020) != 0;
            bool hasScale = (componentFlags & 0x0008) != 0;
            bool hasXYScale = (componentFlags & 0x0040) != 0;
            bool has2x2 = (componentFlags & 0x0080) != 0;

            double dx = 0;
            double dy = 0;

            if (argsAreXY)
            {
                if (argsAreWords)
                {
                    dx = ReadInt16(pos);
                    dy = ReadInt16(pos + 2);
                    pos += 4;
                }
                else
                {
                    dx = (sbyte)ReadByte(pos);
                    dy = (sbyte)ReadByte(pos + 1);
                    pos += 2;
                }
            }
            else
            {
                // Anchor point indices — skip
                pos += argsAreWords ? 4u : 2u;
            }

            // Skip scale/matrix data
            if (hasScale)
            {
                pos += 2;
            }
            else if (hasXYScale)
            {
                pos += 4;
            }
            else if (has2x2)
            {
                pos += 8;
            }

            if (componentGlyphId >= 0 && componentGlyphId < _numGlyphs)
            {
                Path componentPath = BuildGlyphPath(componentGlyphId);
                AppendTranslatedPath(composite, componentPath, dx, dy);
            }

            if (!moreComponents)
            {
                break;
            }
        }

        return composite;
    }

    private static void AppendTranslatedPath(Path target, Path source, double dx, double dy)
    {
        foreach (PathSegment seg in source.Segments)
        {
            switch (seg.Kind)
            {
                case PathSegmentKind.MoveTo:
                    target.MoveTo(seg.P0.X + dx, seg.P0.Y + dy);
                    break;

                case PathSegmentKind.LineTo:
                    target.LineTo(seg.P0.X + dx, seg.P0.Y + dy);
                    break;

                case PathSegmentKind.CubicBezierTo:
                    target.CubicBezierTo(
                        new PointF(seg.P0.X + dx, seg.P0.Y + dy),
                        new PointF(seg.P1.X + dx, seg.P1.Y + dy),
                        new PointF(seg.P2.X + dx, seg.P2.Y + dy));
                    break;

                case PathSegmentKind.ClosePath:
                    target.ClosePath();
                    break;
            }
        }
    }

    // ── Binary reading helpers ─────────────────────────────────────────────

    private string ReadTag(uint offset)
    {
        EnsureReadable(offset, 4);
        return Encoding.ASCII.GetString(_data, (int)offset, 4);
    }

    private uint ReadUInt32(uint offset)
    {
        EnsureReadable(offset, 4);
        return ((uint)_data[offset] << 24)
             | ((uint)_data[offset + 1] << 16)
             | ((uint)_data[offset + 2] << 8)
             | _data[offset + 3];
    }

    private int ReadUInt16(uint offset)
    {
        EnsureReadable(offset, 2);
        return (_data[offset] << 8) | _data[offset + 1];
    }

    private int ReadInt16(uint offset)
    {
        EnsureReadable(offset, 2);
        int raw = (_data[offset] << 8) | _data[offset + 1];
        return raw >= 0x8000 ? raw - 0x10000 : raw;
    }

    private void EnsureReadable(uint offset, uint count)
    {
        // (long) arithmetic so a near-uint.MaxValue offset cannot wrap when
        // count is added. A malformed font that points a table or glyph
        // offset past the end of the data must surface as a typed font error,
        // not an IndexOutOfRangeException.
        if ((long)offset + count > _data.Length)
        {
            throw new FontRenderingException(
                $"TrueType font data is truncated or malformed: attempted to read {count} byte(s) at offset {offset}, but the font is only {_data.Length} byte(s).");
        }
    }

    private byte ReadByte(uint offset)
    {
        EnsureReadable(offset, 1);
        return _data[offset];
    }
}
