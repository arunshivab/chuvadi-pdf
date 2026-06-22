// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ITU-T T.88 (JBIG2) §7 (segments), §7.4.1 (region info),
//        §7.4.6 (generic region), §7.4.8 (page information).
// PHASE: Phase 2 — item 22, JBIG2 decode (generic-region path).
//
// Embedded-organisation decoder: parses a sequence of segments, builds the page
// bitmap from the page-information segment, decodes generic regions, and composites
// them onto the page. Symbol-dictionary and text-region segments (PR-2) and MMR-coded
// generic regions are not yet handled and raise a clear FilterException.

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Filters.Jbig2;

/// <summary>
/// Decodes an embedded-organisation JBIG2 stream into its page bitmap, handling
/// page-information, arithmetic generic-region, symbol-dictionary, and text-region
/// segments (ITU-T T.88 §7).
/// </summary>
internal sealed class Jbig2Decoder
{
    private const uint UnknownLength = 0xFFFFFFFF;
    private const string FilterName = "JBIG2Decode";

    private readonly Dictionary<uint, List<Jbig2Bitmap>> _symbolDictionaries = new();
    private Jbig2Bitmap? _page;
    private int _pageDefaultPixel;

    /// <summary>
    /// Decodes <paramref name="data"/> (optionally preceded by shared
    /// <paramref name="globals"/> segments) into the page bitmap.
    /// </summary>
    /// <param name="data">The page segments of the embedded JBIG2 stream.</param>
    /// <param name="globals">Shared global segments, or null.</param>
    /// <returns>The assembled page bitmap (1 = black, JBIG2 convention).</returns>
    internal Jbig2Bitmap Decode(byte[] data, byte[]? globals)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (globals is not null && globals.Length > 0)
        {
            ProcessSegments(globals);
        }

        ProcessSegments(data);

        return _page ?? new Jbig2Bitmap(0, 0);
    }

    private void ProcessSegments(byte[] data)
    {
        Jbig2Reader reader = new Jbig2Reader(data);

        while (reader.HasBytes(11))
        {
            SegmentHeader header = SegmentHeader.Read(reader);

            int dataEnd = header.DataLength == UnknownLength
                ? reader.Length
                : header.DataStart + (int)header.DataLength;
            if (dataEnd > reader.Length)
            {
                dataEnd = reader.Length;
            }

            ProcessSegment(header, data, dataEnd);

            reader.Position = dataEnd;
        }
    }

    private void ProcessSegment(SegmentHeader header, byte[] data, int dataEnd)
    {
        switch (header.Type)
        {
            case SegmentHeader.TypePageInformation:
                ReadPageInformation(data, header.DataStart);
                break;

            case SegmentHeader.TypeIntermediateGenericRegion:
            case SegmentHeader.TypeImmediateGenericRegion:
            case SegmentHeader.TypeImmediateLosslessGenericRegion:
                DecodeGenericRegion(data, header.DataStart, dataEnd);
                break;

            case SegmentHeader.TypeSymbolDictionary:
                DecodeSymbolDictionary(header, data, dataEnd);
                break;

            case SegmentHeader.TypeIntermediateTextRegion:
            case SegmentHeader.TypeImmediateTextRegion:
            case SegmentHeader.TypeImmediateLosslessTextRegion:
                DecodeTextRegion(header, data, dataEnd);
                break;

            default:
                // Page-end / stripe-end / file-end and segments outside the
                // PR-1 scope carry no page geometry; skip them.
                break;
        }
    }

    private void ReadPageInformation(byte[] data, int start)
    {
        Jbig2Reader reader = new Jbig2Reader(data) { Position = start };

        uint width = reader.ReadUInt32();
        uint height = reader.ReadUInt32();
        reader.ReadUInt32(); // X resolution.
        reader.ReadUInt32(); // Y resolution.
        int flags = reader.ReadByte();
        _pageDefaultPixel = (flags >> 2) & 1;

        if (width == 0 || width > int.MaxValue)
        {
            throw new FilterException(FilterName, "Invalid JBIG2 page width.");
        }

        // An unknown page height is resolved by the regions composited onto it; the
        // page bitmap is then created lazily at first region with its full extent.
        if (height == UnknownLength || height == 0)
        {
            _page = null;
            return;
        }

        _page = CreatePage((int)width, (int)height);
    }

    private void DecodeGenericRegion(byte[] data, int start, int dataEnd)
    {
        Jbig2Reader reader = new Jbig2Reader(data) { Position = start };

        // Region segment information (§7.4.1).
        uint regionWidth = reader.ReadUInt32();
        uint regionHeight = reader.ReadUInt32();
        uint regionX = reader.ReadUInt32();
        uint regionY = reader.ReadUInt32();
        int regionFlags = reader.ReadByte();
        int combinationOperator = regionFlags & 0x07;

        // Generic region flags (§7.4.6.2).
        int genericFlags = reader.ReadByte();
        bool mmr = (genericFlags & 0x01) != 0;
        int template = (genericFlags >> 1) & 0x03;
        bool tpgdon = (genericFlags & 0x08) != 0;

        if (mmr)
        {
            throw new FilterException(
                FilterName, "MMR-coded generic regions are not yet supported.");
        }

        // Adaptive template pixels (§7.4.6.3): four pairs for template 0, one otherwise.
        int atCount = template == 0 ? 4 : 1;
        TemplatePixel[] at = new TemplatePixel[atCount];
        for (int i = 0; i < atCount; i++)
        {
            int ax = reader.ReadSByte();
            int ay = reader.ReadSByte();
            at[i] = new TemplatePixel(ax, ay);
        }

        int width = (int)regionWidth;
        int height = (int)regionHeight;

        MQDecoder mq = new MQDecoder(data, reader.Position, dataEnd);
        byte[] cx = new byte[GenericRegion.ContextSize(template, at)];
        Jbig2Bitmap region = GenericRegion.Decode(mq, cx, width, height, template, at, tpgdon);

        Composite(region, (int)regionX, (int)regionY, combinationOperator);
    }

    private void DecodeSymbolDictionary(SegmentHeader header, byte[] data, int dataEnd)
    {
        List<Jbig2Bitmap> input = GatherSymbols(header.ReferredTo);
        List<Jbig2Bitmap> exported = SymbolDictionary.Decode(data, header.DataStart, dataEnd, input);
        _symbolDictionaries[header.Number] = exported;
    }

    private void DecodeTextRegion(SegmentHeader header, byte[] data, int dataEnd)
    {
        List<Jbig2Bitmap> symbols = GatherSymbols(header.ReferredTo);
        RegionResult result = TextRegion.Decode(data, header.DataStart, dataEnd, symbols);
        Composite(result.Bitmap, result.X, result.Y, result.CombinationOperator);
    }

    // Collects, in reference order, the exported symbols of the referred-to
    // symbol-dictionary segments.
    private List<Jbig2Bitmap> GatherSymbols(IReadOnlyList<uint> referredTo)
    {
        List<Jbig2Bitmap> symbols = new List<Jbig2Bitmap>();
        foreach (uint reference in referredTo)
        {
            if (_symbolDictionaries.TryGetValue(reference, out List<Jbig2Bitmap>? dictionary))
            {
                symbols.AddRange(dictionary);
            }
        }

        return symbols;
    }

    private void Composite(Jbig2Bitmap region, int x, int y, int combinationOperator)
    {
        if (_page is null)
        {
            // No (or unknown-height) page information: the region defines the page.
            _page = CreatePage(x + region.Width, y + region.Height);
        }
        else if (x + region.Width > _page.Width || y + region.Height > _page.Height)
        {
            // Grow the page to contain a region that exceeds the declared extent.
            Jbig2Bitmap grown = CreatePage(
                Math.Max(_page.Width, x + region.Width),
                Math.Max(_page.Height, y + region.Height));
            CombineInto(grown, _page, 0, 0, 4);
            _page = grown;
        }

        CombineInto(_page, region, x, y, combinationOperator);
    }

    private Jbig2Bitmap CreatePage(int width, int height)
    {
        Jbig2Bitmap page = new Jbig2Bitmap(width, height);
        if (_pageDefaultPixel == 1)
        {
            for (int i = 0; i < page.Data.Length; i++)
            {
                page.Data[i] = 1;
            }
        }

        return page;
    }

    // External combination operators (§7.4.8.5 / Table 35):
    // 0 OR, 1 AND, 2 XOR, 3 XNOR, 4 REPLACE.
    private static void CombineInto(Jbig2Bitmap target, Jbig2Bitmap source, int x, int y, int op)
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
                int t = target.Get(tx, ty);
                int result = op switch
                {
                    0 => t | s,
                    1 => t & s,
                    2 => t ^ s,
                    3 => (t ^ s) ^ 1,
                    _ => s,
                };
                target.Set(tx, ty, result);
            }
        }
    }
}
