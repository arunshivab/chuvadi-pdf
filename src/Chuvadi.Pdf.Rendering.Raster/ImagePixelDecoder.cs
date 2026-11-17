// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.9 — Images
// PHASE: 3.35.0 (D-391 I1) — the raster route's image decoding, moved out of
//        the display-list builder so ImageDecoding can share it.

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Content;
using Chuvadi.Pdf.Filters;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Images;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Rendering.Walking;

namespace Chuvadi.Pdf.Rendering.Raster;

/// <summary>
/// Turns an image XObject (or an inline image as a stream) into the frame the
/// raster route paints: codec or raw samples through the colour space,
/// /Decode, /Mask and /SMask, or a stencil in the fill colour. Shared by the
/// raster display-list builder and Chuvadi.Pdf.Rendering.ImageDecoding, so both give
/// the same pixels and the same reasons for skipping.
/// </summary>
internal sealed class ImagePixelDecoder
{
    private readonly PdfObjectStore _objects;
    private readonly Action<PdfWarningKind, string, string> _report;
    private readonly ColorF _stencilFill;

    internal ImagePixelDecoder(PdfObjectStore objects, Action<PdfWarningKind, string, string> report, ColorF stencilFill)
    {
        _objects = objects;
        _report = report;
        _stencilFill = stencilFill;
    }

    // The encoded JPEG bytes of a DCT stream. 3.34.0: filters before
    // DCTDecode in a chain ([/FlateDecode /DCTDecode]) are decoded first;
    // before, the still-compressed bytes were taken as the JPEG.
    private byte[] CodecBytes(PdfStream stream)
    {
        if (stream.Filter is not PdfArray { Count: > 1 } filters)
        {
            return stream.RawBytes;
        }

        PdfDictionary outer = new PdfDictionary();
        foreach (KeyValuePair<PdfName, PdfPrimitive> pair in stream.Dictionary)
        {
            outer.Set(pair.Key, pair.Value);
        }

        outer.Set(PdfName.Filter, new PdfArray(Prefix(filters)));
        if (stream.Dictionary.TryGetValue(PdfName.Intern("DecodeParms"), out PdfPrimitive? parms)
            && _objects.Resolve(parms) is PdfArray { Count: > 1 } parmList)
        {
            outer.Set(PdfName.Intern("DecodeParms"), new PdfArray(Prefix(parmList)));
        }

        return ContentStreamLoader.Decode(new PdfStream(outer, stream.RawBytes), _objects);
    }

    // The gray samples of a one-component JPEG of the given size, or none
    // when it is not one (the caller then leaves the mask off).
    private static byte[] JpegGraySamples(byte[] jpeg, int width, int height)
    {
        if (jpeg.Length < 2 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
        {
            return Array.Empty<byte>();
        }

        ImageFrame gray = JpegDecoder.Decode(jpeg);
        if (gray.Width != width || gray.Height != height || gray.OriginalFormat != ImageColorFormat.Gray8)
        {
            return Array.Empty<byte>();
        }

        byte[] samples = new byte[(long)width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                samples[((long)y * width) + x] = gray.Pixels.GetPixelBgra(x, y).B;
            }
        }

        return samples;
    }

    private static List<PdfPrimitive> Prefix(PdfArray array)
    {
        List<PdfPrimitive> items = new List<PdfPrimitive>(array.Count - 1);
        for (int i = 0; i < array.Count - 1; i++)
        {
            items.Add(array[i]);
        }

        return items;
    }

    // Image-format filters (DCTDecode/JPXDecode) carry the encoded image
    // bytes directly; they must not go through the sample-filter pipeline.
    private static bool StreamIsJpegOrJpx(PdfStream stream)
    {
        if (!stream.IsFiltered)
        {
            return false;
        }

        PdfPrimitive? filter = stream.Filter;

        if (filter is PdfName name)
        {
            string r = FilterRegistry.ResolveAlias(name.Value);
            return r == "DCTDecode" || r == "JPXDecode";
        }

        if (filter is PdfArray array && array.Count > 0)
        {
            PdfName? last = array.GetAs<PdfName>(array.Count - 1);
            if (last is null)
            {
                return false;
            }

            string r = FilterRegistry.ResolveAlias(last.Value);
            return r == "DCTDecode" || r == "JPXDecode";
        }

        return false;
    }

    internal ImageFrame? Decode(PdfStream xobjStream, string imageSubject)
    {
        byte[] imageBytes;

        // 3.34.0 (Phase B N7): an image format not supported yet is
        // reported, not skipped in silence.
        if (LastFilterIs(xobjStream, "JPXDecode"))
        {
            _report(PdfWarningKind.ImageSkipped, imageSubject, "JPEG 2000 (JPXDecode) images are not supported yet; the image is skipped.");
            return null;
        }

        bool codec = StreamIsJpegOrJpx(xobjStream);
        try
        {
            imageBytes = codec ? CodecBytes(xobjStream) : ContentStreamLoader.Decode(xobjStream, _objects);
        }
        catch (FilterException damaged)
        {
            // 3.32.0 (D-271): say why the image is missing.
            _report(PdfWarningKind.StreamDamaged, imageSubject, damaged.Message + " The image is skipped.");
            return null;
        }
        catch (Exception abortCheck) when (!PdfWorkScope.IsAbort(abortCheck))
        {
            return null;
        }

        // Stencil mask (/ImageMask true, PDF 32000-1:2008 §8.9.6.2): a
        // 1-bpc image whose painted samples take the current fill colour
        // and whose masked samples paint nothing. Composed as an RGBA
        // frame so the ordinary image path (clip, alpha, blend, soft
        // mask) applies unchanged.
        if (ReadBoolEntry(xobjStream.Dictionary, "ImageMask"))
        {
            return StencilFrame(xobjStream, imageBytes);
        }

        ImageFrame? frame = null;

        try
        {
            if (imageBytes.Length > 2 &&
                imageBytes[0] == 0xFF && imageBytes[1] == 0xD8)
            {
                frame = CmykJpegFrame(xobjStream, imageBytes) ?? JpegDecoder.Decode(imageBytes);
            }
            else if (imageBytes.Length > 8 &&
                     imageBytes[0] == 137 && imageBytes[1] == 80)
            {
                frame = PngDecoder.Decode(imageBytes);
            }
        }
        catch (ImageException bad)
        {
            _report(PdfWarningKind.ImageSkipped, imageSubject, bad.Message + " The image is skipped.");
            return null;
        }

        // Not a self-describing codec stream: interpret the bytes as raw
        // PDF image samples (Flate/CCITT/LZW output) using the image
        // dictionary's geometry and colour space.
        // 3.34.0: JPEG bytes that do not decode are never read as raw
        // samples (they drew as noise once short data was padded, N13).
        if (frame is null && codec)
        {
            _report(PdfWarningKind.ImageSkipped, imageSubject, "The JPEG data could not be read; the image is skipped.");
            return null;
        }

        bool raw = frame is null;
        frame ??= FrameFromRawSamples(xobjStream, imageBytes);

        if (frame is null)
        {
            _report(PdfWarningKind.ImageSkipped, imageSubject, "The image's colour space, bit depth or size is not supported; the image is skipped.");
            return null;
        }

        // 3.34.0 (Phase B N7): explicit masking (/Mask, §8.9.6.3, §8.9.6.4).
        // Before, /Mask was ignored and the image was drawn unmasked.
        if (!ApplyExplicitMask(xobjStream, frame, raw ? imageBytes : null, imageSubject))
        {
            return null;
        }

        ApplySoftMask(xobjStream, frame);

        return frame;
    }

    // Converts decoded raw samples into an ImageFrame for the cases the
    // raster pipeline supports: 1-bpc DeviceGray (scanned bilevel, e.g.
    // CCITTFaxDecode output), 8-bpc DeviceGray, and 8-bpc DeviceRGB,
    // including ICCBased streams with 1 or 3 components, honouring a
    // /Decode [1 0] inversion for the gray cases. Stencil masks
    // (/ImageMask true) and other colour spaces return null and the
    // image is skipped, as before.
    // 3.34.0 (Phase B N3): a CMYK (or YCCK) JPEG is drawn from its stored
    // channel values read through the image dictionary's /Decode array,
    // like any other CMYK image. Before, the Adobe inverted-channel rule
    // was applied as well, so a PDF that does not invert with /Decode drew
    // as a negative. Null when the JPEG is not four-component, its size
    // does not match the dictionary, or the dictionary's colour space is
    // not a four-component one; the JPEG then decodes as before.
    private ImageFrame? CmykJpegFrame(PdfStream stream, byte[] jpeg)
    {
        PdfDictionary dict = stream.Dictionary;
        if (ReadBoolEntry(dict, "ImageMask") || IndexedSpace(dict) is not null || ResolveComponentCount(dict) != 4)
        {
            return null;
        }

        byte[]? cmyk = JpegDecoder.DecodeCmykSamples(jpeg, out int width, out int height);
        if (cmyk is null || width != ReadIntEntry(dict, "Width") || height != ReadIntEntry(dict, "Height"))
        {
            return null;
        }

        return FrameFromCmyk(dict, cmyk, width, height);
    }

    // 8-bpc CMYK samples (C, M, Y, K per pixel) to RGB with the standard
    // subtractive formula, honouring a /Decode [1 0 ...] inversion.
    private ImageFrame? FrameFromCmyk(PdfDictionary dict, byte[] samples, int width, int height)
    {
        bool invert = GrayDecodeInverted(dict);
        samples = PadShortSamples(samples, (long)width * height * 4);
        if (samples.Length < (long)width * height * 4)
        {
            return null;
        }

        ImageFrame frame = ImageFrame.Create(width, height, ImageColorFormat.Rgb24);
        for (int y = 0; y < height; y++)
        {
            PdfWorkScope.ThrowIfCancellationRequested();
            for (int x = 0; x < width; x++)
            {
                int si = ((y * width) + x) * 4;
                double c = samples[si] / 255.0;
                double m = samples[si + 1] / 255.0;
                double yc = samples[si + 2] / 255.0;
                double k = samples[si + 3] / 255.0;
                if (invert)
                {
                    c = 1 - c;
                    m = 1 - m;
                    yc = 1 - yc;
                    k = 1 - k;
                }

                byte r = (byte)Math.Round(255.0 * (1 - c) * (1 - k));
                byte g = (byte)Math.Round(255.0 * (1 - m) * (1 - k));
                byte b = (byte)Math.Round(255.0 * (1 - yc) * (1 - k));
                frame.Pixels.SetPixelBgra(x, y, b, g, r, 255);
            }
        }

        return frame;
    }

    private ImageFrame? FrameFromRawSamples(PdfStream stream, byte[] samples)
    {
        PdfDictionary dict = stream.Dictionary;

        if (ReadBoolEntry(dict, "ImageMask"))
        {
            return null;
        }

        int width = ReadIntEntry(dict, "Width");
        int height = ReadIntEntry(dict, "Height");
        int bpc = ReadIntEntry(dict, "BitsPerComponent");

        if (width <= 0 || height <= 0)
        {
            return null;
        }

        // 3.32.0 (D-268): Indexed (palette) images of 1, 2, 4 or 8 bits
        // per sample. Before, they were skipped (nothing drawn).
        if (IndexedSpace(dict) is ResolvedColorSpace palette)
        {
            return IndexedFrame(dict, palette, samples, width, height, bpc);
        }

        int components = ResolveComponentCount(dict);
        bool invert = GrayDecodeInverted(dict);

        if (components == 1 && bpc == 1)
        {
            int stride = (width + 7) / 8;
            samples = PadShortSamples(samples, (long)stride * height);
            if (samples.Length < stride * height)
            {
                return null;
            }

            ImageFrame frame = ImageFrame.Create(width, height, ImageColorFormat.Gray8);
            for (int y = 0; y < height; y++)
            {
                PdfWorkScope.ThrowIfCancellationRequested();
                for (int x = 0; x < width; x++)
                {
                    int bit = (samples[(y * stride) + (x >> 3)] >> (7 - (x & 7))) & 1;
                    bool white = invert ? bit == 0 : bit == 1;
                    byte v = white ? (byte)255 : (byte)0;
                    frame.Pixels.SetPixelBgra(x, y, v, v, v, 255);
                }
            }
            return frame;
        }

        if (components == 1 && bpc == 8)
        {
            samples = PadShortSamples(samples, (long)width * height);
            if (samples.Length < width * height)
            {
                return null;
            }

            ImageFrame frame = ImageFrame.Create(width, height, ImageColorFormat.Gray8);
            for (int y = 0; y < height; y++)
            {
                PdfWorkScope.ThrowIfCancellationRequested();
                for (int x = 0; x < width; x++)
                {
                    byte v = samples[(y * width) + x];
                    if (invert)
                    {
                        v = (byte)(255 - v);
                    }
                    frame.Pixels.SetPixelBgra(x, y, v, v, v, 255);
                }
            }
            return frame;
        }

        if (components == 3 && bpc == 8)
        {
            samples = PadShortSamples(samples, (long)width * height * 3);
            if (samples.Length < width * height * 3)
            {
                return null;
            }

            ImageFrame frame = ImageFrame.Create(width, height, ImageColorFormat.Rgb24);
            for (int y = 0; y < height; y++)
            {
                PdfWorkScope.ThrowIfCancellationRequested();
                for (int x = 0; x < width; x++)
                {
                    int si = ((y * width) + x) * 3;
                    frame.Pixels.SetPixelBgra(
                        x, y, samples[si + 2], samples[si + 1], samples[si], 255);
                }
            }
            return frame;
        }

        // 8-bpc DeviceCMYK (or ICCBased with N=4): converted to RGB with
        // the standard subtractive formula so CMYK images display. A
        // /Decode [1 0 …] inversion (Adobe CMYK convention) is honoured.
        if (components == 4 && bpc == 8)
        {
            return FrameFromCmyk(dict, samples, width, height);
        }

        return null;
    }

    // Applies an image's /SMask (a DeviceGray image giving per-pixel alpha)
    // onto the decoded frame's alpha channel. The soft mask is sampled at
    // the frame's resolution (nearest-neighbour) when the dimensions differ.
    // Without this, soft-masked images (e.g. faded watermarks) render as
    // fully opaque, painting their backing colour where they should be
    // transparent.
    // Whether the stream's last filter is the given one (aliases resolved).
    private static bool LastFilterIs(PdfStream stream, string filterName)
    {
        PdfPrimitive? filter = stream.Filter;
        PdfName? last = filter as PdfName ?? (filter is PdfArray { Count: > 0 } array ? array.GetAs<PdfName>(array.Count - 1) : null);
        return last is not null && FilterRegistry.ResolveAlias(last.Value) == filterName;
    }

    // 3.34.0 (Phase B N7): /Mask as a stencil image (§8.9.6.3): a 1-bit
    // image, scaled to the base image, whose samples of 1 (0 with /Decode
    // [1 0]) leave the base image unpainted; or as an array of colour-key
    // ranges (§8.9.6.4) over the raw samples of an 8-bit image with 1 or
    // 3 components, or of a JPEG decoded to gray or RGB. Returns false
    // when the mask stream cannot be decoded: the image is then skipped
    // with a warning rather than drawn unmasked over what it should only
    // partly cover (a scanned page's colour layer under a text mask).
    private bool ApplyExplicitMask(PdfStream xobjStream, ImageFrame frame, byte[]? rawSamples, string imageSubject)
    {
        if (!xobjStream.Dictionary.TryGetValue(PdfName.Intern("Mask"), out PdfPrimitive? maskRef) || maskRef is null)
        {
            return true;
        }

        PdfPrimitive mask = _objects.Resolve(maskRef);
        if (mask is PdfStream maskStream)
        {
            return ApplyStencilMask(maskStream, frame, imageSubject);
        }

        if (mask is PdfArray ranges)
        {
            ApplyColorKeyMask(xobjStream.Dictionary, ranges, frame, rawSamples, imageSubject);
        }

        return true;
    }

    private bool ApplyStencilMask(PdfStream maskStream, ImageFrame frame, string imageSubject)
    {
        int mw = ReadIntEntry(maskStream.Dictionary, "Width");
        int mh = ReadIntEntry(maskStream.Dictionary, "Height");
        if (mw <= 0 || mh <= 0)
        {
            _report(PdfWarningKind.ImageSkipped, imageSubject, "The image's /Mask has no size; the image is skipped rather than drawn unmasked.");
            return false;
        }

        byte[] bits;
        try
        {
            bits = ContentStreamLoader.Decode(maskStream, _objects);
        }
        catch (Exception damaged) when (!PdfWorkScope.IsAbort(damaged))
        {
            _report(PdfWarningKind.ImageSkipped, imageSubject, "The image's /Mask could not be decoded (" + damaged.Message + "); the image is skipped rather than drawn unmasked.");
            return false;
        }

        int stride = (mw + 7) / 8;
        bits = PadShortSamples(bits, (long)stride * mh);
        if ((long)bits.Length < (long)stride * mh)
        {
            _report(PdfWarningKind.ImageSkipped, imageSubject, "The image's /Mask holds no data; the image is skipped rather than drawn unmasked.");
            return false;
        }

        // A mask that is not marked /ImageMask (an ordinary 1-bit gray
        // image) is read as Poppler and MuPDF read it: white (1) shows the
        // image, black (0) hides it (found by the pdf.js run, issue6621).
        bool zeroMasks = GrayDecodeInverted(maskStream.Dictionary) != !ReadBoolEntry(maskStream.Dictionary, "ImageMask");
        int fw = frame.Width;
        int fh = frame.Height;
        for (int y = 0; y < fh; y++)
        {
            PdfWorkScope.ThrowIfCancellationRequested();
            int my = Math.Min(mh - 1, (int)((long)y * mh / fh));
            for (int x = 0; x < fw; x++)
            {
                int mx = Math.Min(mw - 1, (int)((long)x * mw / fw));
                int bit = (bits[(my * stride) + (mx >> 3)] >> (7 - (mx & 7))) & 1;
                if ((bit == 1) != zeroMasks)
                {
                    (byte b, byte g, byte r, byte _) = frame.Pixels.GetPixelBgra(x, y);
                    frame.Pixels.SetPixelBgra(x, y, b, g, r, 0);
                }
            }
        }

        return true;
    }

    private void ApplyColorKeyMask(PdfDictionary dict, PdfArray ranges, ImageFrame frame, byte[]? rawSamples, string imageSubject)
    {
        int bpc = ReadIntEntry(dict, "BitsPerComponent");
        int components = rawSamples is null ? (frame.OriginalFormat == ImageColorFormat.Gray8 ? 1 : 3) : ResolveComponentCount(dict);
        if ((rawSamples is not null && bpc != 8) || components is not (1 or 3) || ranges.Count < components * 2)
        {
            _report(PdfWarningKind.ImageSkipped, imageSubject, "The image's colour-key /Mask is not supported for this image format; the image is drawn without it.");
            return;
        }

        int[] lo = new int[components];
        int[] hi = new int[components];
        for (int c = 0; c < components; c++)
        {
            lo[c] = (int)Math.Round(PdfReal.ToDouble(ranges[2 * c]));
            hi[c] = (int)Math.Round(PdfReal.ToDouble(ranges[(2 * c) + 1]));
        }

        int fw = frame.Width;
        int fh = frame.Height;
        for (int y = 0; y < fh; y++)
        {
            PdfWorkScope.ThrowIfCancellationRequested();
            for (int x = 0; x < fw; x++)
            {
                (byte b, byte g, byte r, byte a) = frame.Pixels.GetPixelBgra(x, y);
                bool inside = true;
                for (int c = 0; c < components && inside; c++)
                {
                    int v;
                    if (rawSamples is not null)
                    {
                        long at = (((long)y * fw) + x) * components + c;
                        v = at < rawSamples.Length ? rawSamples[at] : 0;
                    }
                    else
                    {
                        v = components == 1 ? r : c switch { 0 => r, 1 => g, _ => b };
                    }

                    inside = v >= lo[c] && v <= hi[c];
                }

                if (inside && a != 0)
                {
                    frame.Pixels.SetPixelBgra(x, y, b, g, r, 0);
                }
            }
        }
    }

    // The mean of the mask samples under image pixel (x, y), for a mask
    // larger than the image along an axis.
    private static byte AverageMask(byte[] samples, int smWidth, int smHeight, int x, int y, int fw, int fh)
    {
        int x0 = (int)((long)x * smWidth / fw);
        int x1 = Math.Max(x0 + 1, (int)((long)(x + 1) * smWidth / fw));
        int y0 = (int)((long)y * smHeight / fh);
        int y1 = Math.Max(y0 + 1, (int)((long)(y + 1) * smHeight / fh));
        x1 = Math.Min(x1, smWidth);
        y1 = Math.Min(y1, smHeight);
        long sum = 0;
        for (int sy = y0; sy < y1; sy++)
        {
            for (int sx = x0; sx < x1; sx++)
            {
                sum += samples[(sy * smWidth) + sx];
            }
        }

        long count = (long)(x1 - x0) * (y1 - y0);
        return count == 0 ? samples[Math.Min(samples.Length - 1, (y0 * smWidth) + x0)] : (byte)((sum + (count / 2)) / count);
    }

    private void ApplySoftMask(PdfStream xobjStream, ImageFrame frame)
    {
        if (!xobjStream.Dictionary.TryGetValue(PdfName.Intern("SMask"), out PdfPrimitive? smRef))
        {
            return;
        }

        if (_objects.Resolve(smRef) is not PdfStream smStream)
        {
            return;
        }

        int smWidth = ReadIntEntry(smStream.Dictionary, "Width");
        int smHeight = ReadIntEntry(smStream.Dictionary, "Height");
        int smBpc = ReadIntEntry(smStream.Dictionary, "BitsPerComponent");

        if (smWidth <= 0 || smHeight <= 0 || smBpc != 8)
        {
            return;
        }

        byte[] smSamples;

        try
        {
            // 3.34.0: a soft mask may itself be a (gray) JPEG; before, its
            // decode failed and the image was drawn without its mask.
            smSamples = StreamIsJpegOrJpx(smStream)
                ? JpegGraySamples(CodecBytes(smStream), smWidth, smHeight)
                : ContentStreamLoader.Decode(smStream, _objects);
        }
        catch (Exception abortCheck) when (!PdfWorkScope.IsAbort(abortCheck))
        {
            return;
        }

        if (smSamples.Length < smWidth * smHeight)
        {
            return;
        }

        bool invert = GrayDecodeInverted(smStream.Dictionary);
        int fw = frame.Width;
        int fh = frame.Height;

        // /Matte: base colour samples are pre-multiplied against this colour
        // (PDF 32000-1:2008 §11.6.5.3). Recover c = (c' - m)/alpha + m.
        double[]? matte = ReadMatteEntry(smStream.Dictionary);

        for (int y = 0; y < fh; y++)
        {
            int sy = smHeight == fh ? y : (int)((long)y * smHeight / fh);
            if (sy >= smHeight)
            {
                sy = smHeight - 1;
            }

            for (int x = 0; x < fw; x++)
            {
                int sx = smWidth == fw ? x : (int)((long)x * smWidth / fw);
                if (sx >= smWidth)
                {
                    sx = smWidth - 1;
                }

                // 3.35.0 (item 55): a mask with more samples than the image
                // is averaged over the samples each image pixel covers.
                byte alpha = smWidth > fw || smHeight > fh
                    ? AverageMask(smSamples, smWidth, smHeight, x, y, fw, fh)
                    : smSamples[(sy * smWidth) + sx];
                if (invert)
                {
                    alpha = (byte)(255 - alpha);
                }

                (byte b, byte g, byte r, byte _) = frame.Pixels.GetPixelBgra(x, y);

                if (matte is not null && alpha > 0 && matte.Length >= 3)
                {
                    double a = alpha / 255.0;
                    r = Unpremultiply(r, matte[0], a);
                    g = Unpremultiply(g, matte[1], a);
                    b = Unpremultiply(b, matte[2], a);
                }

                frame.Pixels.SetPixelBgra(x, y, b, g, r, alpha);
            }
        }
    }

    // Recovers a single matte-premultiplied colour channel:
    // c = (c' - m)/alpha + m, clamped to [0, 1] and scaled back to a byte.
    private static byte Unpremultiply(byte sample, double matte, double alpha)
    {
        double recovered = (((sample / 255.0) - matte) / alpha) + matte;
        if (recovered < 0.0) { recovered = 0.0; }
        if (recovered > 1.0) { recovered = 1.0; }
        return (byte)Math.Round(recovered * 255.0);
    }

    // Reads a soft mask's /Matte colour as components in [0, 1], or null.
    private static double[]? ReadMatteEntry(PdfDictionary smDict)
    {
        if (!smDict.TryGetValue(PdfName.Intern("Matte"), out PdfPrimitive? value)
            || value is not PdfArray array || array.Count == 0)
        {
            return null;
        }

        double[] matte = new double[array.Count];
        for (int i = 0; i < array.Count; i++)
        {
            matte[i] = PdfReal.ToDouble(array[i]);
        }
        return matte;
    }

    // Number of colour components implied by /ColorSpace: DeviceGray and
    // CalGray are 1, DeviceRGB and CalRGB are 3, and ICCBased defers to
    // its stream's /N. Unsupported spaces return 0.
    // The image's colour space when it is /Indexed and parses, else null.
    private ResolvedColorSpace? IndexedSpace(PdfDictionary dict)
    {
        if (!dict.TryGetValue(PdfName.Intern("ColorSpace"), out PdfPrimitive? csRef)
            || _objects.Resolve(csRef) is not PdfArray array
            || array.Count == 0
            || _objects.Resolve(array[0]) is not PdfName family
            || family.Value != "Indexed")
        {
            return null;
        }

        ResolvedColorSpace? space = ResolvedColorSpace.Parse(array, _objects);
        return space is not null && space.Kind == ResolvedColorSpace.Family.Indexed ? space : null;
    }

    // Expands a palette image through its colour space into an RGB frame.
    private ImageFrame? IndexedFrame(PdfDictionary dict, ResolvedColorSpace palette, byte[] samples, int width, int height, int bpc)
    {
        dict.TryGetValue(PdfName.Intern("Decode"), out PdfPrimitive? decode);
        int hival = (1 << Math.Clamp(bpc, 1, 8)) - 1;
        if (dict.TryGetValue(PdfName.Intern("ColorSpace"), out PdfPrimitive? csRef)
            && _objects.Resolve(csRef) is PdfArray array && array.Count >= 3
            && _objects.Resolve(array[2]) is PdfInteger hi)
        {
            hival = hi.Value;
        }

        byte[]? indices = IndexedSamples.ToIndices(samples, width, height, bpc, decode, _objects, hival);
        if (indices is null)
        {
            return null;
        }

        byte[] table = new byte[256 * 3];
        double[] component = new double[1];
        for (int i = 0; i < 256; i++)
        {
            component[0] = i;
            double[] rgb = palette.ToRgb(component);
            table[i * 3] = ToByte(rgb.Length > 0 ? rgb[0] : 0);
            table[(i * 3) + 1] = ToByte(rgb.Length > 1 ? rgb[1] : 0);
            table[(i * 3) + 2] = ToByte(rgb.Length > 2 ? rgb[2] : 0);
        }

        ImageFrame frame = ImageFrame.Create(width, height, ImageColorFormat.Rgb24);
        for (int y = 0; y < height; y++)
        {
            PdfWorkScope.ThrowIfCancellationRequested();
            for (int x = 0; x < width; x++)
            {
                int t = indices[((long)y * width) + x] * 3;
                frame.Pixels.SetPixelBgra(x, y, table[t + 2], table[t + 1], table[t], 255);
            }
        }

        return frame;
    }

    private static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value * 255.0), 0, 255);

    private int ResolveComponentCount(PdfDictionary dict)
    {
        if (!dict.TryGetValue(PdfName.Intern("ColorSpace"), out PdfPrimitive? csRef))
        {
            return 0;
        }

        PdfPrimitive cs = _objects.Resolve(csRef);

        if (cs is PdfName name)
        {
            return name.Value switch
            {
                "DeviceGray" or "CalGray" or "G" => 1,
                "DeviceRGB" or "CalRGB" or "RGB" => 3,
                "DeviceCMYK" or "CMYK" => 4,
                _ => 0,
            };
        }

        if (cs is PdfArray array && array.Count >= 2 &&
            array[0] is PdfName family && family.Value == "ICCBased")
        {
            PdfStream? icc = _objects.ResolveAs<PdfStream>(array[1]);
            if (icc is not null &&
                icc.Dictionary.TryGetValue(PdfName.Intern("N"), out PdfPrimitive? n) &&
                n is PdfInteger count)
            {
                return count.Value is 1 or 3 or 4 ? count.Value : 0;
            }
        }

        return 0;
    }

    // True when /Decode is [1 0] for a single-component image (inverted
    // gray, common in scanned PDFs).
    // Builds an RGBA frame for a stencil mask: painted bits take the
    // current fill colour at full alpha; masked bits are fully
    // transparent. Default /Decode [0 1] paints sample 0; [1 0] flips.
    private ImageFrame? StencilFrame(PdfStream stream, byte[] samples)
    {
        PdfDictionary dict = stream.Dictionary;
        int width = ReadIntEntry(dict, "Width");
        int height = ReadIntEntry(dict, "Height");
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        int stride = (width + 7) / 8;
        samples = PadShortSamples(samples, (long)stride * height);
        if ((long)samples.Length < (long)stride * height)
        {
            return null;
        }

        bool onePaints = GrayDecodeInverted(dict);
        ColorF fill = _stencilFill.ToRgb();
        byte r = (byte)Math.Round(Math.Clamp(fill.R, 0f, 1f) * 255f);
        byte g = (byte)Math.Round(Math.Clamp(fill.G, 0f, 1f) * 255f);
        byte b = (byte)Math.Round(Math.Clamp(fill.B, 0f, 1f) * 255f);

        PdfWorkScope.ChargeMemory((long)width * height * 4);
        PixelBuffer buffer = new PixelBuffer(width, height);
        for (int y = 0; y < height; y++)
        {
            PdfWorkScope.ThrowIfCancellationRequested();
            int rowStart = y * stride;
            for (int x = 0; x < width; x++)
            {
                int bit = (samples[rowStart + (x >> 3)] >> (7 - (x & 7))) & 1;
                bool paint = onePaints ? bit == 1 : bit == 0;
                if (paint)
                {
                    buffer.SetPixelBgra(x, y, b, g, r, 255);
                }
                else
                {
                    buffer.SetPixelBgra(x, y, 0, 0, 0, 0);
                }
            }
        }

        return new ImageFrame(buffer, ImageColorFormat.Rgba32);
    }

    private bool GrayDecodeInverted(PdfDictionary dict)
    {
        if (!dict.TryGetValue(PdfName.Intern("Decode"), out PdfPrimitive? decodeRef))
        {
            return false;
        }

        if (_objects.Resolve(decodeRef) is not PdfArray decode || decode.Count == 0)
        {
            return false;
        }

        // 3.35.0: a one-element /Decode ([1], as Poppler's mask.pdf
        // writes) is read as [1 0], as Poppler and MuPDF read it.
        return decode.Count == 1
            ? AsDouble(decode[0]) >= 0.5
            : AsDouble(decode[0]) > AsDouble(decode[1]);
    }

    private int ReadIntEntry(PdfDictionary dict, string key) => ReadInt(_objects, dict, key);

    private bool ReadBoolEntry(PdfDictionary dict, string key) => ReadBool(_objects, dict, key);

    internal static int ReadInt(PdfObjectStore objects, PdfDictionary dict, string key)
    {
        if (!dict.TryGetValue(PdfName.Intern(key), out PdfPrimitive? value))
        {
            return 0;
        }

        return objects.Resolve(value) is PdfInteger i ? i.Value : 0;
    }

    internal static bool ReadBool(PdfObjectStore objects, PdfDictionary dict, string key)
    {
        if (!dict.TryGetValue(PdfName.Intern(key), out PdfPrimitive? value))
        {
            return false;
        }

        return objects.Resolve(value) is PdfBoolean b && b.Value;
    }

    // 3.34.0 (Phase B test bench N13): image data that ends early is
    // completed with zero samples and drawn, with a warning, as Poppler and
    // MuPDF draw it; before, the image was skipped without a word. Data
    // with nothing in it, or a size past what an array can hold, is left
    // as it is (the caller then skips the image).
    private byte[] PadShortSamples(byte[] samples, long needed)
    {
        if (samples.Length == 0 || samples.Length >= needed || needed > Array.MaxLength)
        {
            return samples;
        }

        PdfWorkScope.ChargeMemory(needed);
        _report(
            PdfWarningKind.StreamDamaged,
            "image",
            string.Create(System.Globalization.CultureInfo.InvariantCulture, $"The image data ends early ({samples.Length} of {needed} bytes); the missing samples are drawn as zero."));
        byte[] padded = new byte[needed];
        Buffer.BlockCopy(samples, 0, padded, 0, samples.Length);
        return padded;
    }

    internal static double AsDouble(PdfPrimitive p)
    {
        return p switch
        {
            PdfInteger i => i.Value,
            PdfReal r => r.Value,
            _ => 0.0,
        };
    }
}
