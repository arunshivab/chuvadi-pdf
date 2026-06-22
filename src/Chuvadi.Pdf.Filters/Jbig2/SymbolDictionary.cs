// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ITU-T T.88 (JBIG2) §6.5 (symbol dictionary decoding), §7.4.3 (segment).
// PHASE: Phase 2 — item 22, JBIG2 decode.
//
// Arithmetic symbol-dictionary decoding: each new symbol is a small bitmap decoded
// by the generic-region procedure, organised into height classes whose heights and
// widths are arithmetic integers (IADH, IADW). An export run (IAEX) selects which of
// the input + new symbols the dictionary exports. Huffman-coded dictionaries,
// refinement/aggregate coding, and Huffman tables are not yet supported.

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Filters.Jbig2;

/// <summary>
/// Decodes a JBIG2 symbol-dictionary segment (ITU-T T.88 §6.5) into the list of
/// symbol bitmaps it exports.
/// </summary>
internal static class SymbolDictionary
{
    private const string FilterName = "JBIG2Decode";

    /// <summary>
    /// Decodes the symbol-dictionary segment whose data occupies
    /// [<paramref name="start"/>, <paramref name="dataEnd"/>) in
    /// <paramref name="data"/>, given the symbols exported by referred-to
    /// dictionaries.
    /// </summary>
    /// <param name="data">The buffer containing the segment data.</param>
    /// <param name="start">Offset of the segment data.</param>
    /// <param name="dataEnd">Exclusive end offset of the segment data.</param>
    /// <param name="inputSymbols">Symbols from referred-to dictionaries.</param>
    /// <returns>The exported symbol bitmaps, in export order.</returns>
    internal static List<Jbig2Bitmap> Decode(
        byte[] data, int start, int dataEnd, IReadOnlyList<Jbig2Bitmap> inputSymbols)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(inputSymbols);

        Jbig2Reader reader = new Jbig2Reader(data) { Position = start };

        int flags = reader.ReadUInt16();
        bool sdHuff = (flags & 0x0001) != 0;
        bool sdRefAgg = (flags & 0x0002) != 0;
        int sdTemplate = (flags >> 10) & 0x03;
        bool sdrTemplate = (flags & 0x1000) != 0;

        if (sdHuff)
        {
            throw new FilterException(FilterName, "Huffman-coded symbol dictionaries are not yet supported.");
        }

        if (sdRefAgg)
        {
            throw new FilterException(FilterName, "Refinement/aggregate symbol dictionaries are not yet supported.");
        }

        // Adaptive template pixels (one pair for templates 1-3, four for template 0).
        int atCount = sdTemplate == 0 ? 4 : 1;
        TemplatePixel[] at = new TemplatePixel[atCount];
        for (int i = 0; i < atCount; i++)
        {
            int ax = reader.ReadSByte();
            int ay = reader.ReadSByte();
            at[i] = new TemplatePixel(ax, ay);
        }

        // SDRAT would follow here when SDREFAGG is set; unsupported, so skip.
        _ = sdrTemplate;

        uint numExSyms = reader.ReadUInt32();
        uint numNewSyms = reader.ReadUInt32();

        int newCount = checked((int)numNewSyms);
        Jbig2Bitmap[] newSymbols = new Jbig2Bitmap[newCount];

        MQDecoder mq = new MQDecoder(data, reader.Position, dataEnd);
        byte[] iadh = new byte[ArithmeticIntegerCoder.IntegerContextSize];
        byte[] iadw = new byte[ArithmeticIntegerCoder.IntegerContextSize];
        byte[] iaex = new byte[ArithmeticIntegerCoder.IntegerContextSize];
        byte[] gb = new byte[GenericRegion.ContextSize(sdTemplate, at)];

        int hcHeight = 0;
        int decoded = 0;

        while (decoded < newCount)
        {
            int hcdh = ArithmeticIntegerCoder.Decode(mq, iadh)
                ?? throw new FilterException(FilterName, "Unexpected OOB decoding symbol height class.");

            hcHeight += hcdh;
            int symWidth = 0;

            while (true)
            {
                int? dw = ArithmeticIntegerCoder.Decode(mq, iadw);
                if (dw is null)
                {
                    break; // End of this height class.
                }

                symWidth += dw.Value;

                if (decoded >= newCount || symWidth <= 0 || hcHeight <= 0)
                {
                    throw new FilterException(FilterName, "Malformed symbol dictionary dimensions.");
                }

                newSymbols[decoded] = GenericRegion.Decode(
                    mq, gb, symWidth, hcHeight, sdTemplate, at, tpgdon: false);
                decoded++;
            }
        }

        return SelectExportedSymbols(mq, iaex, inputSymbols, newSymbols, checked((int)numExSyms));
    }

    private static List<Jbig2Bitmap> SelectExportedSymbols(
        MQDecoder mq,
        byte[] iaex,
        IReadOnlyList<Jbig2Bitmap> inputSymbols,
        Jbig2Bitmap[] newSymbols,
        int expectedExportCount)
    {
        int total = inputSymbols.Count + newSymbols.Length;
        List<Jbig2Bitmap> exported = new List<Jbig2Bitmap>(expectedExportCount);

        int index = 0;
        bool exportFlag = false;
        while (index < total)
        {
            int? runLength = ArithmeticIntegerCoder.Decode(mq, iaex);
            if (runLength is null || runLength.Value < 0)
            {
                throw new FilterException(FilterName, "Invalid symbol-dictionary export run length.");
            }

            for (int i = 0; i < runLength.Value && index < total; i++, index++)
            {
                if (exportFlag)
                {
                    exported.Add(index < inputSymbols.Count
                        ? inputSymbols[index]
                        : newSymbols[index - inputSymbols.Count]);
                }
            }

            exportFlag = !exportFlag;
        }

        return exported;
    }
}
