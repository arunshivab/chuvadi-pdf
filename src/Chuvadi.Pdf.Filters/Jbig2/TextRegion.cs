// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ITU-T T.88 (JBIG2) §6.4 (text region decoding), §7.4.4 (segment).
// PHASE: Phase 2 — item 22, JBIG2 decode.
//
// Arithmetic text-region decoding: symbol instances are placed strip by strip at
// arithmetic-decoded (S, T) coordinates and composited onto the region bitmap. The
// reference corner and the S advance follow §6.4.5: right-reference corners advance
// S before drawing, left-reference corners after, so S ends at the symbol's far edge
// either way. Huffman coding, symbol refinement, and the transposed layout are not
// yet supported.

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Filters.Jbig2;

/// <summary>The result of decoding a region segment: its bitmap and placement.</summary>
/// <param name="Bitmap">The decoded region bitmap.</param>
/// <param name="X">Region X on the page.</param>
/// <param name="Y">Region Y on the page.</param>
/// <param name="CombinationOperator">External combination operator (§7.4.1).</param>
internal readonly record struct RegionResult(Jbig2Bitmap Bitmap, int X, int Y, int CombinationOperator);

/// <summary>
/// Decodes a JBIG2 text-region segment (ITU-T T.88 §6.4): it places instances of
/// the symbols exported by referred-to symbol dictionaries.
/// </summary>
internal static class TextRegion
{
    private const string FilterName = "JBIG2Decode";

    /// <summary>
    /// Decodes the text-region segment in [<paramref name="start"/>,
    /// <paramref name="dataEnd"/>), placing instances of <paramref name="symbols"/>.
    /// </summary>
    /// <param name="data">The buffer containing the segment data.</param>
    /// <param name="start">Offset of the segment data.</param>
    /// <param name="dataEnd">Exclusive end offset of the segment data.</param>
    /// <param name="symbols">All symbols available from referred-to dictionaries.</param>
    /// <returns>The decoded region bitmap and its placement.</returns>
    internal static RegionResult Decode(
        byte[] data, int start, int dataEnd, IReadOnlyList<Jbig2Bitmap> symbols)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(symbols);

        Jbig2Reader reader = new Jbig2Reader(data) { Position = start };

        // Region segment information (§7.4.1).
        int regionWidth = checked((int)reader.ReadUInt32());
        int regionHeight = checked((int)reader.ReadUInt32());
        int regionX = checked((int)reader.ReadUInt32());
        int regionY = checked((int)reader.ReadUInt32());
        int externalCombOp = reader.ReadByte() & 0x07;

        // Text region flags (§7.4.4.1.1).
        int flags = reader.ReadUInt16();
        bool sbHuff = (flags & 0x0001) != 0;
        bool sbRefine = (flags & 0x0002) != 0;
        int logStrips = (flags >> 2) & 0x03;
        int refCorner = (flags >> 4) & 0x03;
        bool transposed = (flags & 0x0040) != 0;
        int sbCombOp = (flags >> 7) & 0x03;
        int sbDefPixel = (flags >> 9) & 0x01;
        int sbDsOffset = (flags >> 10) & 0x1F;
        if (sbDsOffset > 15)
        {
            sbDsOffset -= 32; // 5-bit signed.
        }

        if (sbHuff)
        {
            throw new FilterException(FilterName, "Huffman-coded text regions are not yet supported.");
        }

        if (sbRefine)
        {
            throw new FilterException(FilterName, "Refinement in text regions is not yet supported.");
        }

        if (transposed)
        {
            throw new FilterException(FilterName, "Transposed text regions are not yet supported.");
        }

        int strips = 1 << logStrips;
        int symCodeLen = Math.Max(1, CeilLog2(symbols.Count));

        // SBNUMINSTANCES follows the flags (the Huffman/refinement fields that would
        // otherwise precede it are rejected above).
        int numInstances = checked((int)reader.ReadUInt32());

        Jbig2Bitmap region = new Jbig2Bitmap(regionWidth, regionHeight);
        if (sbDefPixel == 1)
        {
            for (int i = 0; i < region.Data.Length; i++)
            {
                region.Data[i] = 1;
            }
        }

        MQDecoder mq = new MQDecoder(data, reader.Position, dataEnd);
        byte[] iadt = new byte[ArithmeticIntegerCoder.IntegerContextSize];
        byte[] iafs = new byte[ArithmeticIntegerCoder.IntegerContextSize];
        byte[] iads = new byte[ArithmeticIntegerCoder.IntegerContextSize];
        byte[] iait = new byte[ArithmeticIntegerCoder.IntegerContextSize];
        byte[] iaid = new byte[1 << (symCodeLen + 1)];

        int? initialDt = ArithmeticIntegerCoder.Decode(mq, iadt);
        int stripT = -(initialDt ?? 0);
        int firstS = 0;
        int instances = 0;

        while (instances < numInstances)
        {
            int? dt = ArithmeticIntegerCoder.Decode(mq, iadt);
            if (dt is null)
            {
                break;
            }

            stripT += dt.Value;

            int? dfs = ArithmeticIntegerCoder.Decode(mq, iafs);
            if (dfs is null)
            {
                break;
            }

            firstS += dfs.Value;
            int curS = firstS;
            bool firstInStrip = true;

            while (true)
            {
                if (!firstInStrip)
                {
                    int? ids = ArithmeticIntegerCoder.Decode(mq, iads);
                    if (ids is null)
                    {
                        break; // End of strip.
                    }

                    curS += ids.Value + sbDsOffset;
                }

                firstInStrip = false;

                if (instances >= numInstances)
                {
                    break;
                }

                int curT = strips == 1 ? 0 : (ArithmeticIntegerCoder.Decode(mq, iait) ?? 0);
                int t = (stripT * strips) + curT;

                int id = ArithmeticIntegerCoder.DecodeId(mq, iaid, symCodeLen);
                if (id < 0 || id >= symbols.Count)
                {
                    throw new FilterException(FilterName, "Text-region symbol id out of range.");
                }

                Jbig2Bitmap symbol = symbols[id];
                curS = PlaceSymbol(region, symbol, curS, t, refCorner, sbCombOp);

                instances++;
            }
        }

        return new RegionResult(region, regionX, regionY, externalCombOp);
    }

    // Places one symbol for the non-transposed layout and returns the advanced S.
    private static int PlaceSymbol(
        Jbig2Bitmap region, Jbig2Bitmap symbol, int curS, int t, int refCorner, int combOp)
    {
        int width = symbol.Width;
        int height = symbol.Height;
        bool right = refCorner == 2 || refCorner == 3;   // BOTTOMRIGHT or TOPRIGHT.
        bool bottom = refCorner == 0 || refCorner == 2;  // BOTTOMLEFT or BOTTOMRIGHT.

        if (right)
        {
            curS += width - 1;
        }

        int left = right ? curS - width + 1 : curS;
        int top = bottom ? t - height + 1 : t;

        Composite(region, symbol, left, top, combOp);

        if (!right)
        {
            curS += width - 1;
        }

        return curS;
    }

    private static void Composite(Jbig2Bitmap target, Jbig2Bitmap source, int x, int y, int op)
    {
        for (int sy = 0; sy < source.Height; sy++)
        {
            int ty = y + sy;
            if (ty < 0 || ty >= target.Height)
            {
                continue;
            }

            for (int sx = 0; sx < source.Width; sx++)
            {
                int tx = x + sx;
                if (tx < 0 || tx >= target.Width)
                {
                    continue;
                }

                int s = source.Get(sx, sy);
                int existing = target.Get(tx, ty);
                int result = op switch
                {
                    0 => existing | s,
                    1 => existing & s,
                    2 => existing ^ s,
                    3 => (existing ^ s) ^ 1,
                    _ => s,
                };
                target.Set(tx, ty, result);
            }
        }
    }

    private static int CeilLog2(int value)
    {
        int bits = 0;
        int remaining = value - 1;
        while (remaining > 0)
        {
            bits++;
            remaining >>= 1;
        }

        return bits;
    }
}
