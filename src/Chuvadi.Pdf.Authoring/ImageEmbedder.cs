// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.9 — Images, §7.4.4 — FlateDecode,
//        §7.4.8 — DCTDecode, §11.6.5.2 — Soft-mask images (SMask)
// PHASE: Phase 2.7 — Image → PDF
// Turns raw image bytes (JPEG, PNG, TIFF, BMP) into PDF image XObject parts.

using System;
using System.IO;
using Chuvadi.Pdf.Filters;
using Chuvadi.Pdf.Images;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Authoring;

/// <summary>
/// The assembled parts of a PDF image XObject: the image dictionary and its
/// (already filtered) data, plus an optional soft-mask image carrying the
/// source's alpha channel.
/// </summary>
internal sealed class EmbeddedImage
{
    /// <summary>The image XObject dictionary (without /SMask; the emitter adds it).</summary>
    internal required PdfDictionary ImageDictionary { get; init; }

    /// <summary>The filtered image stream data.</summary>
    internal required byte[] ImageData { get; init; }

    /// <summary>The soft-mask dictionary when the source has an alpha channel; otherwise null.</summary>
    internal PdfDictionary? MaskDictionary { get; init; }

    /// <summary>The filtered soft-mask stream data; empty when <see cref="MaskDictionary"/> is null.</summary>
    internal byte[] MaskData { get; init; } = Array.Empty<byte>();

    /// <summary>Image width in pixels.</summary>
    internal required int Width { get; init; }

    /// <summary>Image height in pixels.</summary>
    internal required int Height { get; init; }
}

/// <summary>
/// Builds PDF image XObjects from encoded image bytes or decoded frames.
/// </summary>
/// <remarks>
/// <para>
/// Fast paths keep the source's own compression: baseline JPEG is embedded
/// as-is under DCTDecode, and 8-bit truecolour non-interlaced PNG is embedded
/// as its raw zlib IDAT stream under FlateDecode with a PNG predictor.
/// </para>
/// <para>
/// Everything else — palette / grayscale / alpha / 16-bit PNG, TIFF, BMP —
/// is decoded with the Chuvadi.Pdf.Images codecs and re-embedded as
/// Flate-compressed raw samples (DeviceRGB or DeviceGray), with the alpha
/// channel, when present, carried as a DeviceGray soft-mask image.
/// </para>
/// </remarks>
internal static class ImageEmbedder
{
    /// <summary>Builds the XObject parts for an encoded image (JPEG, PNG, TIFF, or BMP).</summary>
    /// <exception cref="ArgumentException">The bytes are not a recognised image format.</exception>
    /// <exception cref="ImageException">The image is malformed or uses an unsupported variant.</exception>
    internal static EmbeddedImage Build(byte[] imageBytes)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);

        ImageFormat format = Sniff(imageBytes);
        switch (format)
        {
            case ImageFormat.Jpeg:
                return BuildJpeg(imageBytes);
            case ImageFormat.Png:
                return BuildPng(imageBytes);
            case ImageFormat.Tiff:
                return BuildFromFrame(TiffDecoder.Decode(imageBytes)[0]);
            case ImageFormat.Bmp:
                return BuildFromFrame(BmpDecoder.Decode(imageBytes));
            default:
                throw new ArgumentException(
                    "Unsupported image format. Supported: JPEG, PNG, TIFF, BMP.",
                    nameof(imageBytes));
        }
    }

    /// <summary>Builds the XObject parts from an already-decoded frame.</summary>
    internal static EmbeddedImage BuildFromFrame(ImageFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        int w = frame.Width;
        int h = frame.Height;

        bool hasAlpha = false;
        for (int y = 0; y < h && !hasAlpha; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (frame.Pixels.GetPixelBgra(x, y).A < 255)
                {
                    hasAlpha = true;
                    break;
                }
            }
        }

        bool gray = !hasAlpha && frame.OriginalFormat == ImageColorFormat.Gray8;
        int channels = gray ? 1 : 3;
        byte[] samples = new byte[w * h * channels];
        byte[]? alpha = hasAlpha ? new byte[w * h] : null;

        int si = 0;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                (byte b, byte g, byte r, byte a) = frame.Pixels.GetPixelBgra(x, y);
                if (gray)
                {
                    samples[si++] = r;
                }
                else
                {
                    samples[si++] = r;
                    samples[si++] = g;
                    samples[si++] = b;
                }
                alpha?[(y * w) + x] = a;
            }
        }

        byte[] compressed = FlateCompress(samples);
        PdfDictionary dict = NewImageDictionary(w, h, gray ? "DeviceGray" : "DeviceRGB", "FlateDecode");
        dict.Set(PdfName.Length, compressed.Length);

        if (alpha is null)
        {
            return new EmbeddedImage
            {
                ImageDictionary = dict,
                ImageData = compressed,
                Width = w,
                Height = h,
            };
        }

        byte[] maskCompressed = FlateCompress(alpha);
        PdfDictionary mask = NewImageDictionary(w, h, "DeviceGray", "FlateDecode");
        mask.Set(PdfName.Length, maskCompressed.Length);

        return new EmbeddedImage
        {
            ImageDictionary = dict,
            ImageData = compressed,
            MaskDictionary = mask,
            MaskData = maskCompressed,
            Width = w,
            Height = h,
        };
    }

    /// <summary>
    /// Measures an encoded image's pixel dimensions from its header without a
    /// full decode (TIFF reads only the first IFD).
    /// </summary>
    /// <exception cref="ArgumentException">The bytes are not a recognised image format.</exception>
    /// <exception cref="ImageException">The header is malformed.</exception>
    internal static (int Width, int Height) Measure(byte[] imageBytes)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);

        ImageFormat format = Sniff(imageBytes);
        switch (format)
        {
            case ImageFormat.Jpeg:
                {
                    (int w, int h, _, _) = JpegHeader(imageBytes);
                    return (w, h);
                }
            case ImageFormat.Png:
                {
                    PngHeader header = ReadPngHeader(imageBytes);
                    return (header.Width, header.Height);
                }
            case ImageFormat.Tiff:
                return TiffDimensions(imageBytes);
            case ImageFormat.Bmp:
                return BmpDimensions(imageBytes);
            default:
                throw new ArgumentException(
                    "Unsupported image format. Supported: JPEG, PNG, TIFF, BMP.",
                    nameof(imageBytes));
        }
    }

    /// <summary>Identifies the image container from its magic bytes.</summary>
    internal static ImageFormat Sniff(byte[] d)
    {
        ArgumentNullException.ThrowIfNull(d);

        if (d.Length >= 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF)
        {
            return ImageFormat.Jpeg;
        }
        if (d.Length >= 8 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47)
        {
            return ImageFormat.Png;
        }
        if (d.Length >= 4 &&
            ((d[0] == 0x49 && d[1] == 0x49 && d[2] == 42 && d[3] == 0) ||
             (d[0] == 0x4D && d[1] == 0x4D && d[2] == 0 && d[3] == 42)))
        {
            return ImageFormat.Tiff;
        }
        if (d.Length >= 2 && d[0] == (byte)'B' && d[1] == (byte)'M')
        {
            return ImageFormat.Bmp;
        }
        return ImageFormat.Unknown;
    }

    // ── JPEG ──────────────────────────────────────────────────────────────

    private static EmbeddedImage BuildJpeg(byte[] jpegBytes)
    {
        (int w, int h, int components, int adobeTransform) = JpegHeader(jpegBytes);

        // 1-component (grayscale), 3-component (YCbCr -> RGB) and 4-component
        // (CMYK / YCCK) baseline JPEG all embed directly under DCTDecode, keeping
        // the original DCT compression instead of decoding to samples and
        // re-deflating. For 4 components the DCTDecode filter performs any
        // YCCK -> CMYK conversion from the stream's Adobe APP14 marker; that same
        // marker also signals Adobe's inverted-channel convention, which a Decode
        // array corrects. Other component counts go through the decoder so the
        // embedded samples are unambiguous.
        if (components != 1 && components != 3 && components != 4)
        {
            return BuildFromFrame(JpegDecoder.Decode(jpegBytes));
        }

        string colorSpace = components switch
        {
            1 => "DeviceGray",
            4 => "DeviceCMYK",
            _ => "DeviceRGB",
        };

        PdfDictionary dict = NewImageDictionary(w, h, colorSpace, "DCTDecode");
        dict.Set(PdfName.Length, jpegBytes.Length);

        if (components == 4 && adobeTransform >= 0)
        {
            // Adobe-marked CMYK / YCCK stores every channel inverted; remap each
            // sample from [0,1] back through 1 - sample so colours are correct.
            dict.Set(PdfName.Intern("Decode"), new PdfArray(new PdfPrimitive[]
            {
                new PdfInteger(1), new PdfInteger(0),
                new PdfInteger(1), new PdfInteger(0),
                new PdfInteger(1), new PdfInteger(0),
                new PdfInteger(1), new PdfInteger(0),
            }));
        }

        return new EmbeddedImage
        {
            ImageDictionary = dict,
            ImageData = jpegBytes,
            Width = w,
            Height = h,
        };
    }

    private static (int W, int H, int Components, int AdobeTransform) JpegHeader(byte[] bytes)
    {
        int w = 0;
        int h = 0;
        int components = 0;
        int adobeTransform = -1;
        bool sofFound = false;
        int i = 2;

        while (i < bytes.Length - 1)
        {
            if (bytes[i] != 0xFF)
            {
                i++;
                continue;
            }

            byte marker = bytes[i + 1];
            i += 2;

            // Markers without a length segment.
            if (marker == 0xD8 || marker == 0xD9 || (marker >= 0xD0 && marker <= 0xD7))
            {
                continue;
            }

            // Start of scan: the header is complete.
            if (marker == 0xDA || i + 1 >= bytes.Length)
            {
                break;
            }

            int len = (bytes[i] << 8) | bytes[i + 1];
            int segStart = i + 2;

            // SOF0-SOF3: baseline and extended sequential / progressive / lossless.
            if (marker >= 0xC0 && marker <= 0xC3)
            {
                h = (bytes[segStart + 1] << 8) | bytes[segStart + 2];
                w = (bytes[segStart + 3] << 8) | bytes[segStart + 4];
                components = bytes[segStart + 5];
                sofFound = true;
            }
            else if (marker == 0xEE && len >= 14
                && bytes[segStart] == (byte)'A' && bytes[segStart + 1] == (byte)'d'
                && bytes[segStart + 2] == (byte)'o' && bytes[segStart + 3] == (byte)'b'
                && bytes[segStart + 4] == (byte)'e')
            {
                // APP14 Adobe marker: the colour-transform byte is at offset 11 of
                // the segment data (matching the JPEG decoder's reading).
                adobeTransform = bytes[segStart + 11];
            }

            i += len;
        }

        if (!sofFound)
        {
            throw new ImageException("JPEG SOF marker not found.");
        }

        return (w, h, components, adobeTransform);
    }

    // ── PNG ───────────────────────────────────────────────────────────────

    private readonly record struct PngHeader(
        int Width, int Height, byte BitDepth, byte ColorType, byte Interlace);

    private static EmbeddedImage BuildPng(byte[] pngBytes)
    {
        PngHeader header = ReadPngHeader(pngBytes);

        // Fast path: 8-bit truecolour, non-interlaced, no alpha — the IDAT
        // zlib stream is exactly a FlateDecode stream with PNG predictors
        // over 3-component rows, so it embeds without recompression.
        if (header.ColorType == 2 && header.BitDepth == 8 && header.Interlace == 0)
        {
            byte[] idat = ConcatIdat(pngBytes);
            PdfDictionary dict = NewImageDictionary(
                header.Width, header.Height, "DeviceRGB", "FlateDecode");
            PdfDictionary parms = new();
            parms.Set(PdfName.Intern("Predictor"), 15);
            parms.Set(PdfName.Intern("Colors"), 3);
            parms.Set(PdfName.Intern("BitsPerComponent"), 8);
            parms.Set(PdfName.Intern("Columns"), header.Width);
            dict.Set(PdfName.Intern("DecodeParms"), parms);
            dict.Set(PdfName.Length, idat.Length);

            return new EmbeddedImage
            {
                ImageDictionary = dict,
                ImageData = idat,
                Width = header.Width,
                Height = header.Height,
            };
        }

        // Everything else — palette, grayscale, alpha, 16-bit — decodes to a
        // frame and re-embeds, carrying alpha as a soft mask.
        return BuildFromFrame(PngDecoder.Decode(pngBytes));
    }

    private static PngHeader ReadPngHeader(byte[] bytes)
    {
        if (bytes.Length < 33)
        {
            throw new ImageException("PNG truncated: IHDR chunk missing.");
        }
        int w = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
        int h = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
        return new PngHeader(w, h, bytes[24], bytes[25], bytes[28]);
    }

    private static byte[] ConcatIdat(byte[] bytes)
    {
        int p = 8;
        using MemoryStream idat = new();
        while (p + 8 <= bytes.Length)
        {
            int len = (bytes[p] << 24) | (bytes[p + 1] << 16) | (bytes[p + 2] << 8) | bytes[p + 3];
            bool isIdat = bytes[p + 4] == (byte)'I' && bytes[p + 5] == (byte)'D' &&
                          bytes[p + 6] == (byte)'A' && bytes[p + 7] == (byte)'T';
            bool isIend = bytes[p + 4] == (byte)'I' && bytes[p + 5] == (byte)'E' &&
                          bytes[p + 6] == (byte)'N' && bytes[p + 7] == (byte)'D';
            if (isIdat)
            {
                idat.Write(bytes, p + 8, len);
            }
            else if (isIend)
            {
                break;
            }
            p += 12 + len;
        }
        return idat.ToArray();
    }

    // ── TIFF / BMP dimension probes ───────────────────────────────────────

    private static (int Width, int Height) TiffDimensions(byte[] d)
    {
        bool little = d[0] == 0x49;
        uint ifdOffset = ReadU32(d, 4, little);
        if (ifdOffset + 2 > d.Length)
        {
            throw new ImageException("TIFF truncated: first IFD missing.");
        }

        int count = ReadU16(d, (int)ifdOffset, little);
        int width = 0;
        int height = 0;
        for (int i = 0; i < count; i++)
        {
            int entry = (int)ifdOffset + 2 + (i * 12);
            if (entry + 12 > d.Length)
            {
                throw new ImageException("TIFF truncated inside IFD.");
            }
            int tag = ReadU16(d, entry, little);
            int type = ReadU16(d, entry + 2, little);
            if (tag != 256 && tag != 257)
            {
                continue;
            }
            int value = type == 3
                ? ReadU16(d, entry + 8, little)
                : (int)ReadU32(d, entry + 8, little);
            if (tag == 256)
            {
                width = value;
            }
            else
            {
                height = value;
            }
        }

        if (width <= 0 || height <= 0)
        {
            throw new ImageException("TIFF IFD does not declare image dimensions.");
        }
        return (width, height);
    }

    private static (int Width, int Height) BmpDimensions(byte[] d)
    {
        if (d.Length < 26)
        {
            throw new ImageException("BMP truncated: header missing.");
        }
        uint headerSize = ReadU32(d, 14, little: true);
        if (headerSize == 12)
        {
            return (ReadU16(d, 18, little: true), ReadU16(d, 20, little: true));
        }
        int w = (int)ReadU32(d, 18, little: true);
        int h = (int)ReadU32(d, 22, little: true);
        return (w, Math.Abs(h));
    }

    // ── Shared helpers ────────────────────────────────────────────────────

    private static PdfDictionary NewImageDictionary(
        int width, int height, string colorSpace, string filter)
    {
        PdfDictionary dict = new();
        dict.Set(PdfName.Type, PdfName.Intern("XObject"));
        dict.Set(PdfName.Subtype, PdfName.Intern("Image"));
        dict.Set(PdfName.Intern("Width"), width);
        dict.Set(PdfName.Intern("Height"), height);
        dict.Set(PdfName.Intern("ColorSpace"), PdfName.Intern(colorSpace));
        dict.Set(PdfName.Intern("BitsPerComponent"), 8);
        dict.Set(PdfName.Intern("Filter"), PdfName.Intern(filter));
        return dict;
    }

    private static byte[] FlateCompress(byte[] raw)
    {
        DeflateFilter deflate = new();
        using MemoryStream input = new(raw);
        using MemoryStream output = new();
        deflate.Encode(input, output);
        return output.ToArray();
    }

    private static int ReadU16(byte[] d, int p, bool little)
        => little ? d[p] | (d[p + 1] << 8) : (d[p] << 8) | d[p + 1];

    private static uint ReadU32(byte[] d, int p, bool little)
        => little
            ? (uint)(d[p] | (d[p + 1] << 8) | (d[p + 2] << 16) | (d[p + 3] << 24))
            : (uint)((d[p] << 24) | (d[p + 1] << 16) | (d[p + 2] << 8) | d[p + 3]);
}

/// <summary>The image container formats the authoring pipeline recognises.</summary>
internal enum ImageFormat
{
    /// <summary>Not a recognised image container.</summary>
    Unknown = 0,

    /// <summary>JPEG / JFIF.</summary>
    Jpeg = 1,

    /// <summary>Portable Network Graphics.</summary>
    Png = 2,

    /// <summary>Tagged Image File Format.</summary>
    Tiff = 3,

    /// <summary>Windows Bitmap.</summary>
    Bmp = 4,
}
