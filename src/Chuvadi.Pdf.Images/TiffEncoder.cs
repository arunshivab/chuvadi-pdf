// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  TIFF 6.0 specification (Aldus / Adobe, June 1992)
// PHASE: Phase 1.1.9 — Chuvadi.Pdf.Images TIFF support
//
// Baseline TIFF 6.0 writer. Writes:
//   - Little-endian byte order ("II")
//   - 8-bit per channel
//   - RGB photometric (2)
//   - PackBits compression (32773) — broadly supported, simple to implement
//   - Single strip per page covering the full image
//   - Multi-page: each frame becomes one IFD chained via NextIFDOffset

using System;
using System.Collections.Generic;
using System.IO;

namespace Chuvadi.Pdf.Images;

/// <summary>
/// Encodes one or more <see cref="ImageFrame"/> objects to a baseline TIFF 6.0
/// byte stream.
/// </summary>
/// <remarks>
/// Output format:
/// - Little-endian.
/// - 8 bits per sample, 3 samples per pixel (RGB photometric).
/// - PackBits compression.
/// - Single strip per page.
///
/// Multi-frame inputs produce a multi-page TIFF.
/// </remarks>
public static class TiffEncoder
{
    /// <summary>Encodes a single image frame to a TIFF byte stream.</summary>
    public static byte[] Encode(ImageFrame frame)
    {
        if (frame is null)
        {
            throw new ArgumentNullException(nameof(frame));
        }

        return EncodeAll(new[] { frame });
    }

    /// <summary>Encodes a sequence of image frames to a multi-page TIFF byte stream.</summary>
    public static byte[] EncodeAll(IEnumerable<ImageFrame> frames)
    {
        if (frames is null)
        {
            throw new ArgumentNullException(nameof(frames));
        }

        List<ImageFrame> list = new List<ImageFrame>(frames);

        if (list.Count == 0)
        {
            throw new TiffException("EncodeAll requires at least one frame.");
        }

        using MemoryStream ms = new MemoryStream();
        BinaryWriter w = new BinaryWriter(ms);

        // Header: II, 42, offset of first IFD (placeholder, will patch)
        w.Write((byte)'I');
        w.Write((byte)'I');
        WriteU16(w, 42);
        long firstIfdOffsetPos = ms.Position;
        WriteU32(w, 0);

        // For each frame:
        // 1. Write the compressed strip bytes.
        // 2. Write the IFD with a NextIFD pointer that we patch when writing the
        //    following frame's IFD, or to 0 at the end.
        long previousNextIfdPos = firstIfdOffsetPos;

        for (int i = 0; i < list.Count; i++)
        {
            ImageFrame frame = list[i];
            int width = frame.Width;
            int height = frame.Height;

            // Re-pack pixels for the target colour space.
            //   Rgb24/Rgba32/Gray8 → RGB photometric=2, 3 channels
            //   Cmyk32             → Separated photometric=5, 4 channels
            bool isCmyk = frame.OriginalFormat == ImageColorFormat.Cmyk32;
            int samplesPerPixel = isCmyk ? 4 : 3;
            byte[] raw = new byte[width * height * samplesPerPixel];
            ReadOnlySpan<byte> pixels = frame.Pixels.Pixels;
            int dst = 0;

            if (isCmyk)
            {
                // BGRA buffer encodes B=C, G=M, R=Y, A=K (see CmykConverter.ToCmykFrame).
                for (int p = 0; p < pixels.Length; p += 4)
                {
                    raw[dst++] = pixels[p];     // C
                    raw[dst++] = pixels[p + 1]; // M
                    raw[dst++] = pixels[p + 2]; // Y
                    raw[dst++] = pixels[p + 3]; // K
                }
            }
            else
            {
                for (int p = 0; p < pixels.Length; p += 4)
                {
                    raw[dst++] = pixels[p + 2]; // R
                    raw[dst++] = pixels[p + 1]; // G
                    raw[dst++] = pixels[p];     // B
                }
            }

            // PackBits MUST be packed per scanline (TIFF 6.0 §9): rows are
            // compressed independently. Windows' WIC decoder (Photos, Photo
            // Viewer, Paint) and other strict readers reset the algorithm at
            // each row boundary, so whole-image packing shifts the rows and
            // leaves a black band at the bottom. The image is also split into
            // multiple strips of roughly 8 KB, as the format recommends.
            int rowBytes = width * samplesPerPixel;
            int rowsPerStrip = Math.Max(1, 8192 / rowBytes);
            int stripCount = (height + rowsPerStrip - 1) / rowsPerStrip;

            long[] stripOffsets = new long[stripCount];
            int[] stripByteCounts = new int[stripCount];

            for (int s = 0; s < stripCount; s++)
            {
                int firstRow = s * rowsPerStrip;
                int rowsInStrip = Math.Min(rowsPerStrip, height - firstRow);

                List<byte> stripData = new List<byte>(rowsInStrip * rowBytes);
                for (int r = 0; r < rowsInStrip; r++)
                {
                    PackBitsCompressRow(raw, (firstRow + r) * rowBytes, rowBytes, stripData);
                }

                stripOffsets[s] = ms.Position;
                stripByteCounts[s] = stripData.Count;
                w.Write(stripData.ToArray());
            }

            // Pad to even boundary (TIFF convention) before the IFD.
            long ifdOffset = ms.Position;
            if (ifdOffset % 2 != 0)
            {
                w.Write((byte)0);
                ifdOffset++;
            }

            long savedPos = ms.Position;
            ms.Position = previousNextIfdPos;
            WriteU32(w, (uint)ifdOffset);
            ms.Position = savedPos;

            // IFD: 14 entries (adds Orientation 274 and PlanarConfiguration 284).
            ushort numEntries = 14;
            WriteU16(w, numEntries);

            // External blobs follow the IFD body, in tag order: BitsPerSample,
            // then StripOffsets[]/StripByteCounts[] (only when multi-strip),
            // then the two resolution rationals.
            long externalStart = ms.Position + (numEntries * 12) + 4;
            long bitsPerSampleOffset = externalStart;
            long stripOffsetsArrayOffset = bitsPerSampleOffset + (samplesPerPixel * 2);
            long stripByteCountsArrayOffset =
                stripOffsetsArrayOffset + (stripCount > 1 ? stripCount * 4 : 0);
            long xResOffset =
                stripByteCountsArrayOffset + (stripCount > 1 ? stripCount * 4 : 0);
            long yResOffset = xResOffset + 8;

            uint stripOffsetsValue = stripCount == 1
                ? (uint)stripOffsets[0]
                : (uint)stripOffsetsArrayOffset;
            uint stripByteCountsValue = stripCount == 1
                ? (uint)stripByteCounts[0]
                : (uint)stripByteCountsArrayOffset;

            // Entries MUST be in ascending tag order.
            WriteEntry(w, 256, 4, 1, (uint)width);                  // ImageWidth
            WriteEntry(w, 257, 4, 1, (uint)height);                 // ImageLength
            WriteEntry(w, 258, 3, (uint)samplesPerPixel, (uint)bitsPerSampleOffset); // BitsPerSample
            WriteEntry(w, 259, 3, 1, 32773);                        // Compression: PackBits
            WriteEntry(w, 262, 3, 1, isCmyk ? 5u : 2u);             // Photometric
            WriteEntry(w, 273, 4, (uint)stripCount, stripOffsetsValue);    // StripOffsets
            WriteEntry(w, 274, 3, 1, 1);                            // Orientation: top-left
            WriteEntry(w, 277, 3, 1, (uint)samplesPerPixel);        // SamplesPerPixel
            WriteEntry(w, 278, 4, 1, (uint)rowsPerStrip);           // RowsPerStrip
            WriteEntry(w, 279, 4, (uint)stripCount, stripByteCountsValue); // StripByteCounts
            WriteEntry(w, 282, 5, 1, (uint)xResOffset);             // XResolution
            WriteEntry(w, 283, 5, 1, (uint)yResOffset);             // YResolution
            WriteEntry(w, 284, 3, 1, 1);                            // PlanarConfiguration: chunky
            WriteEntry(w, 296, 3, 1, 2);                            // ResolutionUnit: inch

            // NextIFDOffset: 0 for now, patched on the next iteration.
            previousNextIfdPos = ms.Position;
            WriteU32(w, 0);

            // External BitsPerSample (one SHORT per sample, 8 bits each).
            for (int k = 0; k < samplesPerPixel; k++)
            {
                WriteU16(w, 8);
            }

            // External StripOffsets / StripByteCounts arrays (only when > 1 strip).
            if (stripCount > 1)
            {
                for (int s = 0; s < stripCount; s++)
                {
                    WriteU32(w, (uint)stripOffsets[s]);
                }

                for (int s = 0; s < stripCount; s++)
                {
                    WriteU32(w, (uint)stripByteCounts[s]);
                }
            }

            // XResolution / YResolution (RATIONAL 72/1).
            WriteU32(w, 72);
            WriteU32(w, 1);
            WriteU32(w, 72);
            WriteU32(w, 1);
        }

        return ms.ToArray();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static void WriteEntry(BinaryWriter w, ushort tag, ushort type, uint count, uint valueOrOffset)
    {
        WriteU16(w, tag);
        WriteU16(w, type);
        WriteU32(w, count);
        WriteU32(w, valueOrOffset);
    }

    private static void WriteU16(BinaryWriter w, ushort v)
    {
        w.Write((byte)(v & 0xFF));
        w.Write((byte)((v >> 8) & 0xFF));
    }

    private static void WriteU32(BinaryWriter w, uint v)
    {
        w.Write((byte)(v & 0xFF));
        w.Write((byte)((v >> 8) & 0xFF));
        w.Write((byte)((v >> 16) & 0xFF));
        w.Write((byte)((v >> 24) & 0xFF));
    }

    /// <summary>
    /// PackBits-compresses a single scanline of <paramref name="input"/> spanning
    /// <paramref name="count"/> bytes from <paramref name="start"/>, appending the
    /// packed bytes to <paramref name="output"/>. Each row is compressed
    /// independently, which is what strict TIFF readers (including Windows' WIC)
    /// require. Runs of three or more identical bytes are emitted as
    /// <c>(-(n-1), byte)</c>; literal sequences as <c>(n-1, bytes...)</c>. The run
    /// and literal lengths are capped at 128 bytes per packet.
    /// </summary>
    /// <param name="input">The full, row-major pixel buffer.</param>
    /// <param name="start">The offset of the first byte of the row.</param>
    /// <param name="count">The number of bytes in the row.</param>
    /// <param name="output">The buffer that receives the packed bytes.</param>
    private static void PackBitsCompressRow(byte[] input, int start, int count, List<byte> output)
    {
        int end = start + count;
        int i = start;

        while (i < end)
        {
            int runByte = input[i];
            int runLen = 1;

            while (i + runLen < end && input[i + runLen] == runByte && runLen < 128)
            {
                runLen++;
            }

            if (runLen >= 3)
            {
                output.Add((byte)(sbyte)(-(runLen - 1)));
                output.Add((byte)runByte);
                i += runLen;
            }
            else
            {
                int litStart = i;
                int litLen = 0;

                while (i < end && litLen < 128)
                {
                    // Stop the literal when a run of three identical bytes begins.
                    if (i + 2 < end &&
                        input[i] == input[i + 1] && input[i + 1] == input[i + 2])
                    {
                        break;
                    }

                    i++;
                    litLen++;
                }

                output.Add((byte)(sbyte)(litLen - 1));
                for (int k = 0; k < litLen; k++)
                {
                    output.Add(input[litStart + k]);
                }
            }
        }
    }
}
