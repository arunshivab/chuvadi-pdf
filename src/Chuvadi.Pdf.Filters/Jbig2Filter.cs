// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.4.7 — JBIG2Decode; ITU-T T.88 (JBIG2).
// PHASE: Phase 2 — item 22, JBIG2 decode.

using System;
using System.IO;
using Chuvadi.Pdf.Filters.Jbig2;

namespace Chuvadi.Pdf.Filters;

/// <summary>
/// The <c>JBIG2Decode</c> filter (PDF 32000-1:2008 §7.4.7). Decodes an embedded
/// JBIG2 stream's page bitmap and emits packed 1-bit-per-pixel image data, one
/// byte-aligned row at a time. JBIG2's native sense is 1 = black; PDF image data
/// expects 0 = black for a 1-bpp DeviceGray sample, so the packed bits are
/// inverted on output.
/// </summary>
/// <remarks>
/// This release decodes arithmetic-coded generic regions, symbol dictionaries, and
/// text regions. Shared segments named by the image's <c>/JBIG2Globals</c> entry are
/// supplied through <see cref="FilterParameters.Jbig2Globals"/> by the decoding call
/// site. Huffman-coded segments, refinement/aggregate coding, transposed text
/// regions, and MMR-coded generic regions are not yet supported and raise a
/// <see cref="FilterException"/> where encountered.
/// </remarks>
public sealed class Jbig2Filter : IStreamFilter
{
    /// <inheritdoc />
    public string FilterName => "JBIG2Decode";

    /// <inheritdoc />
    public void Encode(Stream input, Stream output, FilterParameters? encodeParms = null)
    {
        throw new FilterException(
            "JBIG2Decode", "JBIG2 encoding is not yet supported.");
    }

    /// <inheritdoc />
    public void Decode(Stream input, Stream output, FilterParameters? decodeParms = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        byte[] data = ReadAllBytes(input);

        Jbig2Decoder decoder = new Jbig2Decoder();
        Jbig2Bitmap page = decoder.Decode(data, decodeParms?.Jbig2Globals);

        WritePackedRows(page, output);
    }

    private static byte[] ReadAllBytes(Stream input)
    {
        using MemoryStream buffer = new MemoryStream();
        input.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static void WritePackedRows(Jbig2Bitmap page, Stream output)
    {
        if (page.Width == 0 || page.Height == 0)
        {
            return;
        }

        int rowBytes = (page.Width + 7) / 8;
        byte[] row = new byte[rowBytes];

        for (int y = 0; y < page.Height; y++)
        {
            Array.Clear(row);

            for (int x = 0; x < page.Width; x++)
            {
                if (page.Get(x, y) != 0)
                {
                    row[x >> 3] |= (byte)(0x80 >> (x & 7));
                }
            }

            for (int i = 0; i < rowBytes; i++)
            {
                row[i] = (byte)~row[i];
            }

            output.Write(row, 0, rowBytes);
        }
    }
}
