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
using Chuvadi.Pdf.Fonts.Rendering.Hinting;
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

    // Parsed hinting-table offsets and lengths (cvt , fpgm, prep). Captured in
    // Stage 1 only; the bytecode interpreter consumes them in a later stage.
    private uint _cvtOffset;
    private uint _cvtLength;
    private uint _fpgmOffset;
    private uint _fpgmLength;
    private uint _prepOffset;
    private uint _prepLength;

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
            uint tableLength = ReadUInt32(entryOffset + 12);

            switch (tag)
            {
                case "head": _headOffset = tableOffset; break;
                case "hhea": _hheaOffset = tableOffset; break;
                case "maxp": _maxpOffset = tableOffset; break;
                case "loca": _locaOffset = tableOffset; break;
                case "glyf": _glyfOffset = tableOffset; break;
                case "hmtx": _hmtxOffset = tableOffset; break;
                case "cmap": _cmapOffset = tableOffset; break;
                case "cvt ":
                    _cvtOffset = tableOffset;
                    _cvtLength = tableLength;
                    break;
                case "fpgm":
                    _fpgmOffset = tableOffset;
                    _fpgmLength = tableLength;
                    break;
                case "prep":
                    _prepOffset = tableOffset;
                    _prepLength = tableLength;
                    break;
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
        double[] dx = new double[xCoords.Length];
        double[] dy = new double[yCoords.Length];
        for (int i = 0; i < xCoords.Length; i++)
        {
            dx[i] = xCoords[i];
        }

        for (int i = 0; i < yCoords.Length; i++)
        {
            dy[i] = yCoords[i];
        }

        return ConvertContoursToPath(endPts, flags, dx, dy);
    }

    // Core contour->cubic converter operating on fractional (double)
    // coordinates. The int[] overload above widens font-unit coords to
    // this; the hinted path passes fractional device pixels so that
    // grid-fitted glyphs are not destroyed by premature integer rounding.
    private static Path ConvertContoursToPath(
        int[] endPts, byte[] flags, double[] xCoords, double[] yCoords)
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

    // ── Hinted composite glyphs ───────────────────────────────────────────

    // Prepares (or reuses) the cached hinting interpreter for a device size:
    // runs fpgm once, then prep for the size. Shared by the simple-glyph and
    // composite-glyph hinted paths.
    private HintingInterpreter EnsureHintingInterpreter(int ppem)
    {
        if (_hintInterp is null || _hintInterpPpem != ppem)
        {
            HintingInterpreter interp = new HintingInterpreter(GetHintingLimits());

            byte[]? fontProgram = GetFontProgram();

            if (fontProgram is { Length: > 0 })
            {
                interp.RunProgram(fontProgram);
            }

            interp.PrepareSize(ppem, _unitsPerEm, GetControlValueTable(), GetControlValueProgram());
            _hintInterp = interp;
            _hintInterpPpem = ppem;
            return interp;
        }

        return _hintInterp;
    }

    // One parsed composite component for the hinted path: the child glyph and
    // its XY offset in font design units, plus whether the offset rounds to
    // the device grid (ROUND_XY_TO_GRID).
    private readonly struct HintedComponent
    {
        internal HintedComponent(int glyphId, int dx, int dy, bool roundToGrid)
        {
            GlyphId = glyphId;
            Dx = dx;
            Dy = dy;
            RoundToGrid = roundToGrid;
        }

        internal int GlyphId { get; }

        internal int Dx { get; }

        internal int Dy { get; }

        internal bool RoundToGrid { get; }
    }

    // Parses a composite glyph's component records for the hinted path, and
    // its trailing instruction stream when WE_HAVE_INSTRUCTIONS is set.
    // Returns null - so the caller falls back to the unhinted outline - when
    // the composite uses features outside the hinted scope: scaled components
    // or anchor-point (point-matching) placement.
    private List<HintedComponent>? ParseHintedComponents(uint offset, out byte[] instructions)
    {
        instructions = Array.Empty<byte>();
        List<HintedComponent> components = new List<HintedComponent>();
        uint pos = offset + 10;
        bool haveInstructions = false;

        while (true)
        {
            int flags = ReadUInt16(pos);
            int componentGlyphId = ReadUInt16(pos + 2);
            pos += 4;

            bool argsAreWords = (flags & 0x0001) != 0;
            bool argsAreXY = (flags & 0x0002) != 0;
            bool roundToGrid = (flags & 0x0004) != 0;
            bool hasScale = (flags & 0x0008) != 0;
            bool moreComponents = (flags & 0x0020) != 0;
            bool hasXYScale = (flags & 0x0040) != 0;
            bool has2x2 = (flags & 0x0080) != 0;
            haveInstructions = haveInstructions || (flags & 0x0100) != 0;

            if (!argsAreXY || hasScale || hasXYScale || has2x2)
            {
                return null;
            }

            int dx;
            int dy;
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

            if (componentGlyphId >= 0 && componentGlyphId < _numGlyphs)
            {
                components.Add(new HintedComponent(componentGlyphId, dx, dy, roundToGrid));
            }

            if (!moreComponents)
            {
                break;
            }
        }

        if (haveInstructions)
        {
            int count = ReadUInt16(pos);
            instructions = ReadBytes(pos + 2, count);
        }

        return components;
    }

    // Assembles a composite glyph for hinting: each component is hinted as its
    // own glyph, translated by its (optionally grid-rounded) device offset, and
    // merged into one zone; the composite's phantom points are appended; the
    // composite's own instruction stream (if any) then runs over the assembly.
    // Returns null when the composite is outside the hinted scope or any
    // component cannot be hinted.
    private RawGlyph? BuildHintedComposite(int glyphId, int depth, out Zone? zone)
    {
        zone = null;

        HintingInterpreter? interp = _hintInterp;

        if (depth > 3 || interp is null)
        {
            return null;
        }

        uint offset = GetGlyfOffset(glyphId);
        if (offset == 0)
        {
            return null;
        }

        int numberOfContours = ReadInt16(offset);
        if (numberOfContours >= 0)
        {
            return null;
        }

        List<HintedComponent>? components = ParseHintedComponents(offset, out byte[] compositeInstructions);
        if (components is null || components.Count == 0)
        {
            return null;
        }

        List<int> xs = new List<int>();
        List<int> ys = new List<int>();
        List<bool> onCurve = new List<bool>();
        List<int> contourEnds = new List<int>();
        List<int> currentX = new List<int>();
        List<int> currentY = new List<int>();
        List<int> originalX = new List<int>();
        List<int> originalY = new List<int>();
        List<bool> touchedX = new List<bool>();
        List<bool> touchedY = new List<bool>();

        foreach (HintedComponent component in components)
        {
            RawGlyph? childRaw = BuildRawGlyph(component.GlyphId);
            Zone? childZone;

            if (childRaw is not null)
            {
                childZone = interp.HintGlyph(childRaw);
            }
            else
            {
                childRaw = BuildHintedComposite(component.GlyphId, depth + 1, out childZone);
            }

            if (childRaw is null || childZone is null)
            {
                return null;
            }

            int dxDevice = interp.ScaleToDevice(component.Dx);
            int dyDevice = interp.ScaleToDevice(component.Dy);
            int dxCurrent = component.RoundToGrid ? RoundToGrid26(dxDevice) : dxDevice;
            int dyCurrent = component.RoundToGrid ? RoundToGrid26(dyDevice) : dyDevice;

            int pointBase = xs.Count;
            int childRealCount = childRaw.RealPointCount;

            for (int i = 0; i < childRealCount; i++)
            {
                xs.Add(childRaw.X[i] + component.Dx);
                ys.Add(childRaw.Y[i] + component.Dy);
                onCurve.Add(childRaw.OnCurve[i]);
                currentX.Add(childZone.CurrentX[i] + dxCurrent);
                currentY.Add(childZone.CurrentY[i] + dyCurrent);
                originalX.Add(childZone.OriginalX[i] + dxDevice);
                originalY.Add(childZone.OriginalY[i] + dyDevice);
                touchedX.Add(childZone.TouchedX[i]);
                touchedY.Add(childZone.TouchedY[i]);
            }

            foreach (int end in childRaw.ContourEnds)
            {
                contourEnds.Add(end + pointBase);
            }
        }

        int realCount = xs.Count;
        int total = realCount + 4;
        int[] xArr = new int[total];
        int[] yArr = new int[total];
        bool[] onCurveArr = new bool[total];

        for (int i = 0; i < realCount; i++)
        {
            xArr[i] = xs[i];
            yArr[i] = ys[i];
            onCurveArr[i] = onCurve[i];
        }

        AppendPhantomPoints(glyphId, xArr, yArr, onCurveArr, realCount);

        Zone assembled = new Zone(total, contourEnds.ToArray(), onCurveArr);
        for (int i = 0; i < realCount; i++)
        {
            assembled.CurrentX[i] = currentX[i];
            assembled.CurrentY[i] = currentY[i];
            assembled.OriginalX[i] = originalX[i];
            assembled.OriginalY[i] = originalY[i];
            assembled.TouchedX[i] = touchedX[i];
            assembled.TouchedY[i] = touchedY[i];
        }

        for (int i = realCount; i < total; i++)
        {
            int px = interp.ScaleToDevice(xArr[i]);
            int py = interp.ScaleToDevice(yArr[i]);
            assembled.CurrentX[i] = px;
            assembled.OriginalX[i] = px;
            assembled.CurrentY[i] = py;
            assembled.OriginalY[i] = py;
        }

        RawGlyph carrier = new RawGlyph(
            xArr,
            yArr,
            onCurveArr,
            contourEnds.ToArray(),
            compositeInstructions,
            realCount);

        if (compositeInstructions.Length > 0)
        {
            // Reference-interpreter semantics: a composite's instruction
            // stream sees the assembled, component-hinted positions as its
            // original coordinates (org <- cur), so cut-ins and shift/
            // interpolation displacements measure from the assembly rather
            // than the unhinted design. The natural originals are restored
            // afterwards so Light mode can still extract the unfitted X.
            int[] savedOriginalX = (int[])assembled.OriginalX.Clone();
            int[] savedOriginalY = (int[])assembled.OriginalY.Clone();
            Array.Copy(assembled.CurrentX, assembled.OriginalX, total);
            Array.Copy(assembled.CurrentY, assembled.OriginalY, total);

            interp.RunCompositeProgram(assembled, compositeInstructions);

            Array.Copy(savedOriginalX, assembled.OriginalX, total);
            Array.Copy(savedOriginalY, assembled.OriginalY, total);
        }

        zone = assembled;
        return carrier;
    }

    // Rounds a 26.6 device coordinate to the nearest whole pixel.
    private static int RoundToGrid26(int value)
    {
        return (value + 32) & ~63;
    }

    // Hinted-outline entry point for composite glyphs: mirrors
    // GetHintedGlyphOutline, but assembles the zone from hinted components.
    // Unlike simple glyphs, a composite with no instruction stream of its own
    // is still returned hinted, because its components carry the hinting.
    private GlyphOutline? GetHintedCompositeOutline(int glyphId, int ppem, bool light)
    {
        try
        {
            _ = EnsureHintingInterpreter(ppem);

            RawGlyph? carrier = BuildHintedComposite(glyphId, depth: 1, out Zone? zone);
            if (carrier is null || zone is null)
            {
                return null;
            }

            Path outline = BuildHintedPath(carrier, zone, light);

            double scale = (double)ppem / _unitsPerEm;
            GlyphMetrics fontMetrics = GetGlyphMetrics(glyphId);

            int originPhantomX = zone.CurrentX[carrier.RealPointCount + 0];
            int advancePhantomX = zone.CurrentX[carrier.RealPointCount + 1];
            int hintedAdvancePx = (int)Math.Round((advancePhantomX - originPhantomX) / 64.0);
            GlyphMetrics deviceMetrics = new GlyphMetrics(
                advanceWidth: hintedAdvancePx,
                leftSideBearing: (int)Math.Round(fontMetrics.LeftSideBearing * scale),
                unitsPerEm: 1,
                bounds: new RectangleF(
                    (float)(fontMetrics.Bounds.X * scale),
                    (float)(fontMetrics.Bounds.Y * scale),
                    (float)(fontMetrics.Bounds.Width * scale),
                    (float)(fontMetrics.Bounds.Height * scale)));

            return new GlyphOutline(outline, deviceMetrics);
        }
        catch (FontRenderingException)
        {
            _hintInterp = null;
            _hintInterpPpem = -1;
            return null;
        }
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

    // ── Hinting foundation (Stage 1) ──────────────────────────────────────
    // The members below feed the TrueType bytecode hinting interpreter. They
    // are not used by the default (non-hinted) rendering pipeline; the cubic
    // GlyphOutline path above remains the rendering path until hinting is
    // wired in. Output is therefore identical to the pre-hinting build.

    /// <summary>
    /// Returns a copy of the raw bytes of the <c>cvt </c> (Control Value) table,
    /// or <c>null</c> when the font has none. The bytes are an array of
    /// big-endian <c>int16</c> FUnit values; they are neither parsed nor scaled
    /// here. Consumed by the bytecode hinting interpreter in a later stage.
    /// </summary>
    internal byte[]? GetControlValueTable()
    {
        if (_cvtOffset == 0 || _cvtLength == 0)
        {
            return null;
        }

        return ReadBytes(_cvtOffset, (int)_cvtLength);
    }

    /// <summary>
    /// Returns a copy of the raw bytes of the <c>fpgm</c> (Font Program) table,
    /// or <c>null</c> when absent. The font program runs once before any glyph
    /// is hinted. Not interpreted here.
    /// </summary>
    internal byte[]? GetFontProgram()
    {
        if (_fpgmOffset == 0 || _fpgmLength == 0)
        {
            return null;
        }

        return ReadBytes(_fpgmOffset, (int)_fpgmLength);
    }

    /// <summary>
    /// Returns a copy of the raw bytes of the <c>prep</c> (Control Value Program)
    /// table, or <c>null</c> when absent. It runs once per point size, after
    /// <c>fpgm</c>, to prepare the CVT for that size. Not interpreted here.
    /// </summary>
    internal byte[]? GetControlValueProgram()
    {
        if (_prepOffset == 0 || _prepLength == 0)
        {
            return null;
        }

        return ReadBytes(_prepOffset, (int)_prepLength);
    }

    /// <summary>
    /// Reads the hinting-relevant maximums from the <c>maxp</c> table. Version 1.0
    /// tables carry the function/storage/stack/twilight limits; version 0.5
    /// tables (and any unreadable version) carry none, so
    /// <see cref="HintingLimits.Default"/> is returned. Consumed by the bytecode
    /// interpreter to size its tables.
    /// </summary>
    internal HintingLimits GetHintingLimits()
    {
        uint version = ReadUInt32(_maxpOffset);

        if (version != 0x00010000u)
        {
            return HintingLimits.Default;
        }

        int maxTwilightPoints = ReadUInt16(_maxpOffset + 16);
        int maxStorage = ReadUInt16(_maxpOffset + 18);
        int maxFunctionDefs = ReadUInt16(_maxpOffset + 20);
        int maxInstructionDefs = ReadUInt16(_maxpOffset + 22);
        int maxStackElements = ReadUInt16(_maxpOffset + 24);

        return new HintingLimits(
            maxFunctionDefs,
            maxInstructionDefs,
            maxStorage,
            maxStackElements,
            maxTwilightPoints);
    }

    // ── Hinted outline path (Stage 7) ─────────────────────────────────────
    //
    // Grid-fits a glyph with the TrueType bytecode interpreter at a given ppem
    // and returns a device-space cubic outline. Reuses the same quadratic-to-
    // cubic converter as the unhinted path, but feeds it fractional device
    // pixels (not integer-rounded), so grid-fitted shapes keep their precision.
    // Returns null when the glyph cannot be hinted (composite glyph, or the font
    // carries no instructions) and on any interpreter fault, so callers fall
    // back to the scaled unhinted outline — the FreeType policy.

    private HintingInterpreter? _hintInterp;
    private int _hintInterpPpem = -1;

    /// <summary>
    /// Returns the glyph outline grid-fitted at the given pixels-per-em, in
    /// device space (Y up, one unit = one pixel), or <c>null</c> when the glyph
    /// cannot be hinted or an interpreter fault occurs.
    /// </summary>
    /// <param name="glyphId">Zero-based glyph index.</param>
    /// <param name="ppem">Target size in pixels per em; must be positive.</param>
    /// <param name="light">When true, grid-fit the Y axis only (horizontal positions stay naturally scaled).</param>
    public GlyphOutline? GetHintedGlyphOutline(int glyphId, int ppem, bool light)
    {
        if (ppem <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ppem), "Pixels-per-em must be positive.");
        }

        RawGlyph? raw = BuildRawGlyph(glyphId);

        if (raw is null)
        {
            // Composite glyph: assemble from hinted components.
            return GetHintedCompositeOutline(glyphId, ppem, light);
        }

        if (raw.Instructions.Length == 0 && raw.ContourCount > 0)
        {
            return null;
        }

        try
        {
            HintingInterpreter hintInterp = EnsureHintingInterpreter(ppem);

            Zone zone = hintInterp.HintGlyph(raw);

            Path outline = BuildHintedPath(raw, zone, light);

            double scale = (double)ppem / _unitsPerEm;
            GlyphMetrics fontMetrics = GetGlyphMetrics(glyphId);

            // Advance from the hinted horizontal phantom points (pp2 - pp1),
            // in 26.6 device units, rounded to whole device pixels. The glyph
            // program grid-fits the advance phantom, so the hinted advance can
            // differ from the merely scaled hmtx value; using it keeps the
            // advance consistent with grid-fitted ink (full hinting).
            int originPhantomX = zone.CurrentX[raw.RealPointCount + 0];
            int advancePhantomX = zone.CurrentX[raw.RealPointCount + 1];
            int hintedAdvancePx = (int)Math.Round((advancePhantomX - originPhantomX) / 64.0);
            GlyphMetrics deviceMetrics = new GlyphMetrics(
                advanceWidth: hintedAdvancePx,
                leftSideBearing: (int)Math.Round(fontMetrics.LeftSideBearing * scale),
                unitsPerEm: 1,
                bounds: new RectangleF(
                    (float)(fontMetrics.Bounds.X * scale),
                    (float)(fontMetrics.Bounds.Y * scale),
                    (float)(fontMetrics.Bounds.Width * scale),
                    (float)(fontMetrics.Bounds.Height * scale)));

            return new GlyphOutline(outline, deviceMetrics);
        }
        catch (FontRenderingException)
        {
            _hintInterp = null;
            _hintInterpPpem = -1;
            return null;
        }
    }

    // Re-cubicizes the fitted zone's real (non-phantom) points into a device-
    // space Path. Coordinates are the zone's 26.6 fixed-point values converted
    // to FRACTIONAL pixels (value / 64.0) — never integer-rounded — so the
    // cubicizer receives the same precision the unhinted path enjoys.
    private static Path BuildHintedPath(RawGlyph raw, Zone zone, bool light)
    {
        int realPoints = raw.RealPointCount;

        if (realPoints <= 0 || raw.ContourCount == 0)
        {
            return new Path();
        }

        double[] xs = new double[realPoints];
        double[] ys = new double[realPoints];
        byte[] flags = new byte[realPoints];

        for (int i = 0; i < realPoints; i++)
        {
            // Light mode: keep the naturally scaled X (no horizontal grid-fit),
            // take only the grid-fitted Y. Full mode: both axes grid-fitted.
            xs[i] = (light ? zone.OriginalX[i] : zone.CurrentX[i]) / 64.0;
            ys[i] = zone.CurrentY[i] / 64.0;
            flags[i] = zone.OnCurve[i] ? (byte)0x01 : (byte)0x00;
        }

        return ConvertContoursToPath(raw.ContourEnds, flags, xs, ys);
    }

    /// <summary>
    /// Parses a glyph into its raw, un-cubicized TrueType point set: on/off-curve
    /// points in font design units, contour end indices, the glyph's instruction
    /// bytecode, and four appended phantom points. This is the input the bytecode
    /// hinting interpreter requires; the existing
    /// <see cref="GetGlyphOutline(int)"/> cubic path remains the rendering path.
    /// </summary>
    /// <param name="glyphId">Zero-based glyph index.</param>
    /// <returns>
    /// The raw glyph, or <c>null</c> for composite glyphs (composite hinting is
    /// added in a later stage). Empty glyphs return a <see cref="RawGlyph"/> with
    /// no contours but with the four phantom points populated.
    /// </returns>
    /// <exception cref="FontRenderingException">
    /// Thrown when <paramref name="glyphId"/> is out of range.
    /// </exception>
    internal RawGlyph? BuildRawGlyph(int glyphId)
    {
        if (glyphId < 0 || glyphId >= _numGlyphs)
        {
            throw new FontRenderingException(
                $"Glyph index {glyphId} is out of range [0, {_numGlyphs}).");
        }

        uint offset = GetGlyfOffset(glyphId);

        if (offset == 0)
        {
            // Empty / whitespace glyph: no contours, phantoms still carry advance.
            return BuildRawGlyphPhantomsOnly(glyphId);
        }

        int numberOfContours = ReadInt16(offset);

        if (numberOfContours < 0)
        {
            // Composite glyph — deferred to the composite-hinting stage.
            return null;
        }

        if (numberOfContours == 0)
        {
            return BuildRawGlyphPhantomsOnly(glyphId);
        }

        int[] endPtsOfContours = new int[numberOfContours];

        for (int i = 0; i < numberOfContours; i++)
        {
            endPtsOfContours[i] = ReadUInt16(offset + 10 + (uint)(i * 2));
        }

        int numPoints = endPtsOfContours[numberOfContours - 1] + 1;

        int instructionLength = ReadUInt16(offset + 10 + (uint)(numberOfContours * 2));
        uint instructionOffset = offset + 10 + (uint)(numberOfContours * 2) + 2;
        byte[] instructions = ReadBytes(instructionOffset, instructionLength);

        uint flagsOffset = instructionOffset + (uint)instructionLength;

        byte[] flags = ParseFlags(flagsOffset, numPoints, out uint afterFlags);
        int[] xCoords = ParseCoordinates(afterFlags, flags, numPoints, true, out uint afterX);
        int[] yCoords = ParseCoordinates(afterX, flags, numPoints, false, out uint _);

        int total = numPoints + 4;
        int[] xs = new int[total];
        int[] ys = new int[total];
        bool[] onCurve = new bool[total];

        for (int i = 0; i < numPoints; i++)
        {
            xs[i] = xCoords[i];
            ys[i] = yCoords[i];
            onCurve[i] = (flags[i] & 0x01) != 0;
        }

        AppendPhantomPoints(glyphId, xs, ys, onCurve, numPoints);

        return new RawGlyph(xs, ys, onCurve, endPtsOfContours, instructions, numPoints);
    }

    private RawGlyph BuildRawGlyphPhantomsOnly(int glyphId)
    {
        int[] xs = new int[4];
        int[] ys = new int[4];
        bool[] onCurve = new bool[4];
        AppendPhantomPoints(glyphId, xs, ys, onCurve, 0);
        return new RawGlyph(xs, ys, onCurve, Array.Empty<int>(), Array.Empty<byte>(), 0);
    }

    private void AppendPhantomPoints(
        int glyphId, int[] xs, int[] ys, bool[] onCurve, int realPointCount)
    {
        GlyphMetrics metrics = GetGlyphMetrics(glyphId);
        RectangleF bounds = GetGlyfBounds(glyphId);

        int xMin = (int)bounds.X;
        int yMax = (int)(bounds.Y + bounds.Height);
        int advanceWidth = metrics.AdvanceWidth;
        int leftSideBearing = metrics.LeftSideBearing;

        // Horizontal phantom points (well-defined by hmtx):
        //   pp1 = glyph origin  = (xMin - lsb, 0)
        //   pp2 = advance point = (pp1.x + advanceWidth, 0)
        int pp1x = xMin - leftSideBearing;

        // Vertical phantom points: this loader parses no vmtx/vhea, so the
        // vertical metrics are SYNTHESISED for now — pp3 at the glyph top and
        // pp4 one em below. Vertical hinting is not yet a target; revisit when
        // vmtx parsing lands. Horizontal hinting (the common case) uses pp1/pp2.
        xs[realPointCount + 0] = pp1x;
        ys[realPointCount + 0] = 0;
        xs[realPointCount + 1] = pp1x + advanceWidth;
        ys[realPointCount + 1] = 0;
        xs[realPointCount + 2] = 0;
        ys[realPointCount + 2] = yMax;
        xs[realPointCount + 3] = 0;
        ys[realPointCount + 3] = yMax - _unitsPerEm;

        // Phantom points are not contour points; the on-curve flag is meaningless
        // for them. Marked true so they are never mistaken for Bézier controls.
        onCurve[realPointCount + 0] = true;
        onCurve[realPointCount + 1] = true;
        onCurve[realPointCount + 2] = true;
        onCurve[realPointCount + 3] = true;
    }

    private byte[] ReadBytes(uint offset, int length)
    {
        if (length < 0)
        {
            throw new FontRenderingException(
                $"Cannot read a negative number of bytes ({length}).");
        }

        if (length == 0)
        {
            return Array.Empty<byte>();
        }

        EnsureReadable(offset, (uint)length);
        byte[] result = new byte[length];
        Array.Copy(_data, (int)offset, result, 0, length);
        return result;
    }
}
