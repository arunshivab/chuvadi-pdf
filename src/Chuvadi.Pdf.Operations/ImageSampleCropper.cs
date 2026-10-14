// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.9.5 (image dictionaries), §8.9.6 (masked images),
//        §8.9.7 (inline images), §11.6.5.3 (soft-mask images)
// PHASE: Scrub crop — byte-level cropping of images that cross the crop box.

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Content;
using Chuvadi.Pdf.Filters;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Operations;

/// <summary>How an image relates to the crop box, and what cropping produced.</summary>
internal enum ImageCropOutcome
{
    /// <summary>Entirely inside the crop box: keep the original bytes.</summary>
    Inside,

    /// <summary>Entirely outside the crop box: remove it.</summary>
    Outside,

    /// <summary>Crosses the crop box and was cropped to a new image.</summary>
    Cropped,

    /// <summary>Crosses the crop box but could not be decoded for cropping.</summary>
    Failed,
}

/// <summary>
/// Decoded-image description used for sample-level cropping. Built from an
/// image XObject or an inline image; colour space and decode semantics are
/// carried through unchanged so the cropped image paints identically.
/// </summary>
internal sealed class ImageSource
{
    internal int Width;
    internal int Height;
    internal int BitsPerComponent = 8;
    internal int Components = 1;
    internal bool ImageMask;
    internal PdfPrimitive? ColorSpace;
    internal PdfArray? Decode;
    internal PdfPrimitive? Interpolate;
    internal PdfPrimitive? Intent;
    internal List<string> Filters = new List<string>();
    internal List<FilterParameters?> Parms = new List<FilterParameters?>();
    internal byte[] Data = Array.Empty<byte>();
    internal ImageSource? SMask;
    internal ImageSource? MaskImage;
    internal PdfArray? ColorKeyMask;
    internal PdfPrimitive? SMaskInData;
}

/// <summary>
/// Crops images to the in-box region at the sample level: the encoded data is
/// decoded, the pixel sub-rectangle covering the crop box is copied bit for
/// bit (any bits per component, any colour space), pixels whose cell lies
/// wholly outside the crop box are blanked when the placement is rotated or
/// skewed, and the result is re-encoded (Flate). No byte of an off-box pixel
/// survives. Soft masks and stencil masks are cropped with the same region.
/// </summary>
internal static class ImageSampleCropper
{
    /// <summary>
    /// Classifies and, when the image crosses the crop box, crops it.
    /// <paramref name="place"/> maps the unit square onto the cropped region in
    /// image space (concatenate it before <c>Do</c>, relative to the image CTM).
    /// </summary>
    internal static ImageCropOutcome Crop(
        Func<ImageSource?> load,
        Matrix3x3 ctm,
        double x0, double y0, double x1, double y1,
        Func<PdfStream, PdfReference>? allocate,
        out PdfStream? cropped,
        out Matrix3x3 place)
    {
        cropped = null;
        place = Matrix3x3.Identity;

        List<(double X, double Y)> imgQuad = ScrubGeometry.TransformRectCorners(ctm, 0, 0, 1, 1);
        List<(double X, double Y)> cropQuad = new List<(double X, double Y)>
        {
            (x0, y0), (x1, y0), (x1, y1), (x0, y1),
        };

        bool allInside = true;
        foreach ((double X, double Y) c in imgQuad)
        {
            if (!ScrubGeometry.PointInside(cropQuad, c.X, c.Y))
            {
                allInside = false;
                break;
            }
        }

        if (allInside)
        {
            return ImageCropOutcome.Inside;
        }

        if (ScrubGeometry.ClipPolygon(imgQuad, cropQuad).Count < 3)
        {
            return ImageCropOutcome.Outside;
        }

        // The crop rectangle in image (unit-square) space, clipped to the image.
        if (!ScrubGeometry.TryInvert(ctm, out Matrix3x3 inv))
        {
            return ImageCropOutcome.Failed;
        }

        List<(double X, double Y)> cropInImage = ScrubGeometry.TransformRectCorners(inv, x0, y0, x1, y1);
        List<(double X, double Y)> unit = new List<(double X, double Y)>
        {
            (0, 0), (1, 0), (1, 1), (0, 1),
        };
        List<(double X, double Y)> region = ScrubGeometry.ClipPolygon(cropInImage, unit);
        if (region.Count < 3)
        {
            return ImageCropOutcome.Outside;
        }

        double u0 = double.PositiveInfinity, v0 = double.PositiveInfinity;
        double u1 = double.NegativeInfinity, v1 = double.NegativeInfinity;
        foreach ((double u, double v) in region)
        {
            u0 = Math.Min(u0, u);
            v0 = Math.Min(v0, v);
            u1 = Math.Max(u1, u);
            v1 = Math.Max(v1, v);
        }

        ImageSource? source;
        try
        {
            source = load();
        }
        catch (Exception)
        {
            source = null;
        }

        if (source is null)
        {
            return ImageCropOutcome.Failed;
        }

        bool axisAligned = Math.Abs(ctm.B) < 1e-9 && Math.Abs(ctm.C) < 1e-9;
        PdfStream? result = CropImage(source, u0, v0, u1, v1, axisAligned ? null : region, allocate, out double fu0, out double fv0, out double fu1, out double fv1);
        if (result is null)
        {
            return ImageCropOutcome.Failed;
        }

        cropped = result;
        place = new Matrix3x3(fu1 - fu0, 0, 0, fv1 - fv0, fu0, fv0);
        return ImageCropOutcome.Cropped;
    }

    // Crops one image (and its masks) to the pixel range covering
    // [u0,u1]x[v0,v1] (image space, v up). Returns the exact fractional region
    // the cropped pixels cover, which the placement must use.
    private static PdfStream? CropImage(
        ImageSource src, double u0, double v0, double u1, double v1,
        List<(double X, double Y)>? blankOutside, Func<PdfStream, PdfReference>? allocate,
        out double fu0, out double fv0, out double fu1, out double fv1)
    {
        fu0 = fv0 = fu1 = fv1 = 0;
        if (!TryDecodeSamples(src, out byte[] samples, out int bpc, out int comps, out PdfPrimitive? colorSpace, out PdfArray? decode))
        {
            return null;
        }

        int w = src.Width;
        int h = src.Height;
        (int c0, int c1, int r0, int r1) = PixelRange(w, h, u0, v0, u1, v1);
        if (c1 <= c0 || r1 <= r0)
        {
            return null;
        }

        fu0 = (double)c0 / w;
        fu1 = (double)c1 / w;
        fv0 = 1.0 - ((double)r1 / h);
        fv1 = 1.0 - ((double)r0 / h);

        bool blankBit = src.ImageMask && !DecodeInverted(decode);
        byte[] sub = ExtractSubRect(samples, w, h, comps, bpc, c0, c1, r0, r1, blankOutside, blankBit);

        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Type, PdfName.Intern("XObject"));
        dict.Set(PdfName.Subtype, PdfName.Intern("Image"));
        dict.Set(PdfName.Intern("Width"), c1 - c0);
        dict.Set(PdfName.Intern("Height"), r1 - r0);
        dict.Set(PdfName.Intern("BitsPerComponent"), bpc);
        if (src.ImageMask)
        {
            dict.Set(PdfName.Intern("ImageMask"), true);
        }
        else if (colorSpace is not null)
        {
            dict.Set(PdfName.Intern("ColorSpace"), colorSpace);
        }

        if (decode is not null)
        {
            dict.Set(PdfName.Intern("Decode"), decode);
        }

        if (src.Interpolate is not null)
        {
            dict.Set(PdfName.Intern("Interpolate"), src.Interpolate);
        }

        if (src.Intent is not null)
        {
            dict.Set(PdfName.Intern("Intent"), src.Intent);
        }

        if (src.ColorKeyMask is not null)
        {
            dict.Set(PdfName.Intern("Mask"), src.ColorKeyMask);
        }

        if (src.SMaskInData is not null)
        {
            dict.Set(PdfName.Intern("SMaskInData"), src.SMaskInData);
        }

        // Masks are cropped to the same fractional region of the unit square
        // (they are stretched over it), on their own pixel grids.
        if (src.SMask is not null || src.MaskImage is not null)
        {
            if (allocate is null)
            {
                return null;
            }

            if (src.SMask is not null)
            {
                PdfStream? mask = CropImage(src.SMask, fu0, fv0, fu1, fv1, blankOutside, allocate, out _, out _, out _, out _);
                if (mask is null)
                {
                    return null;
                }

                dict.Set(PdfName.Intern("SMask"), allocate(mask));
            }

            if (src.MaskImage is not null)
            {
                PdfStream? mask = CropImage(src.MaskImage, fu0, fv0, fu1, fv1, blankOutside, allocate, out _, out _, out _, out _);
                if (mask is null)
                {
                    return null;
                }

                dict.Set(PdfName.Intern("Mask"), allocate(mask));
            }
        }

        byte[] flate = FlateEncode(sub);
        dict.Set(PdfName.Filter, PdfName.Intern("FlateDecode"));
        dict.Set(PdfName.Length, flate.Length);
        return new PdfStream(dict, flate);
    }

    private static (int C0, int C1, int R0, int R1) PixelRange(
        int w, int h, double u0, double v0, double u1, double v1)
    {
        const double Eps = 1e-9;
        int c0 = Math.Clamp((int)Math.Floor((u0 * w) + Eps), 0, w);
        int c1 = Math.Clamp((int)Math.Ceiling((u1 * w) - Eps), 0, w);
        int r0 = Math.Clamp((int)Math.Floor(((1.0 - v1) * h) + Eps), 0, h);
        int r1 = Math.Clamp((int)Math.Ceiling(((1.0 - v0) * h) - Eps), 0, h);
        if (c1 <= c0 && c0 < w) { c1 = c0 + 1; }
        if (r1 <= r0 && r0 < h) { r1 = r0 + 1; }
        return (c0, c1, r0, r1);
    }

    private static bool DecodeInverted(PdfArray? decode)
    {
        return decode is not null && decode.Count >= 2 && Num(decode[0]) > Num(decode[1]);
    }

    private static double Num(PdfPrimitive p) => p switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0.0,
    };

    // Applies every filter; a trailing DCTDecode is decoded to 8-bit RGB or
    // gray samples (the colour space follows the decoded form).
    private static bool TryDecodeSamples(
        ImageSource src, out byte[] samples, out int bpc, out int comps,
        out PdfPrimitive? colorSpace, out PdfArray? decode)
    {
        samples = src.Data;
        bpc = src.ImageMask ? 1 : src.BitsPerComponent;
        comps = src.ImageMask ? 1 : src.Components;
        colorSpace = src.ColorSpace;
        decode = src.Decode;

        if (src.Width <= 0 || src.Height <= 0 || comps <= 0
            || bpc is not (1 or 2 or 4 or 8 or 16))
        {
            return false;
        }

        FilterPipeline pipeline = FilterRegistry.CreateDefaultPipeline();
        for (int i = 0; i < src.Filters.Count; i++)
        {
            string name = src.Filters[i];
            if (name == "DCTDecode")
            {
                if (i != src.Filters.Count - 1 || src.ImageMask)
                {
                    return false;
                }

                Chuvadi.Pdf.Images.ImageFrame frame = Chuvadi.Pdf.Images.JpegDecoder.Decode(samples);
                if (frame.Pixels.Width != src.Width || frame.Pixels.Height != src.Height)
                {
                    return false;
                }

                bool gray = frame.OriginalFormat == Chuvadi.Pdf.Images.ImageColorFormat.Gray8;
                comps = gray ? 1 : 3;
                bpc = 8;
                colorSpace = PdfName.Intern(gray ? "DeviceGray" : "DeviceRGB");
                decode = null; // the decoder already applied any Adobe inversion
                samples = new byte[src.Width * src.Height * comps];
                int p = 0;
                for (int y = 0; y < src.Height; y++)
                {
                    for (int x = 0; x < src.Width; x++)
                    {
                        (byte b, byte g, byte r, byte _) = frame.Pixels.GetPixelBgra(x, y);
                        if (gray)
                        {
                            samples[p++] = r;
                        }
                        else
                        {
                            samples[p++] = r;
                            samples[p++] = g;
                            samples[p++] = b;
                        }
                    }
                }

                return true;
            }

            if (!pipeline.IsRegistered(name))
            {
                return false;
            }

            samples = pipeline.Decode(name, samples, i < src.Parms.Count ? src.Parms[i] : null);
        }

        long rowBytes = (((long)src.Width * comps * bpc) + 7) / 8;
        return samples.LongLength >= rowBytes * src.Height;
    }

    // Copies the pixel sub-rectangle [c0,c1)x[r0,r1) at the bit level. When a
    // region polygon is supplied (rotated or skewed placement), pixels whose
    // cell does not intersect it are blanked so no off-box sample survives.
    private static byte[] ExtractSubRect(
        byte[] data, int w, int h, int comps, int bpc,
        int c0, int c1, int r0, int r1,
        List<(double X, double Y)>? region, bool blankBit)
    {
        int pixelBits = comps * bpc;
        int rowBytesIn = (int)((((long)w * pixelBits) + 7) / 8);
        int cw = c1 - c0;
        int rowBytesOut = (int)((((long)cw * pixelBits) + 7) / 8);
        byte[] output = new byte[rowBytesOut * (r1 - r0)];

        for (int row = r0; row < r1; row++)
        {
            int inRow = row * rowBytesIn;
            int outRow = (row - r0) * rowBytesOut;

            if (pixelBits % 8 == 0)
            {
                int bytesPerPixel = pixelBits / 8;
                Buffer.BlockCopy(data, inRow + (c0 * bytesPerPixel), output, outRow, cw * bytesPerPixel);
            }
            else
            {
                for (int col = c0; col < c1; col++)
                {
                    long inBit = ((long)inRow * 8) + ((long)col * pixelBits);
                    long outBit = ((long)outRow * 8) + ((long)(col - c0) * pixelBits);
                    for (int b = 0; b < pixelBits; b++)
                    {
                        long ib = inBit + b;
                        int bit = (data[ib >> 3] >> (7 - (int)(ib & 7))) & 1;
                        if (bit != 0)
                        {
                            long ob = outBit + b;
                            output[ob >> 3] |= (byte)(0x80 >> (int)(ob & 7));
                        }
                    }
                }
            }

            if (region is null)
            {
                continue;
            }

            for (int col = c0; col < c1; col++)
            {
                if (!CellIntersects(region, (double)col / w, 1.0 - ((double)(row + 1) / h), (double)(col + 1) / w, 1.0 - ((double)row / h)))
                {
                    BlankPixel(output, outRow, (col - c0) * pixelBits, pixelBits, blankBit);
                }
            }
        }

        return output;
    }

    private static void BlankPixel(byte[] output, int rowStart, int bitOffset, int pixelBits, bool one)
    {
        for (int b = 0; b < pixelBits; b++)
        {
            long ob = ((long)rowStart * 8) + bitOffset + b;
            byte mask = (byte)(0x80 >> (int)(ob & 7));
            if (one)
            {
                output[ob >> 3] |= mask;
            }
            else
            {
                output[ob >> 3] &= (byte)~mask;
            }
        }
    }

    // Separating-axis test between an axis-aligned cell and a convex polygon.
    private static bool CellIntersects(
        List<(double X, double Y)> poly, double cx0, double cy0, double cx1, double cy1)
    {
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        foreach ((double x, double y) in poly)
        {
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }

        if (cx1 < minX || cx0 > maxX || cy1 < minY || cy0 > maxY)
        {
            return false;
        }

        double area = 0;
        for (int i = 0; i < poly.Count; i++)
        {
            (double ax, double ay) = poly[i];
            (double bx, double by) = poly[(i + 1) % poly.Count];
            area += (ax * by) - (bx * ay);
        }

        double orient = area >= 0 ? 1.0 : -1.0;
        Span<(double X, double Y)> corners = stackalloc (double, double)[4]
        {
            (cx0, cy0), (cx1, cy0), (cx1, cy1), (cx0, cy1),
        };

        for (int i = 0; i < poly.Count; i++)
        {
            (double ax, double ay) = poly[i];
            (double bx, double by) = poly[(i + 1) % poly.Count];

            // Outward normal for a counter-clockwise polygon is (dy, -dx).
            double nx = (by - ay) * orient;
            double ny = -(bx - ax) * orient;
            bool allOutside = true;
            foreach ((double px, double py) in corners)
            {
                if (((px - ax) * nx) + ((py - ay) * ny) <= 1e-12)
                {
                    allOutside = false;
                    break;
                }
            }

            if (allOutside)
            {
                return false;
            }
        }

        return true;
    }

    private static byte[] FlateEncode(byte[] data)
    {
        using MemoryStream ms = new MemoryStream();
        using (System.IO.Compression.ZLibStream zs =
            new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            zs.Write(data, 0, data.Length);
        }

        return ms.ToArray();
    }

    // ── Sources ─────────────────────────────────────────────────────────────

    /// <summary>Describes an image XObject (resolving indirect values and masks).</summary>
    internal static ImageSource? FromXObject(PdfStream xobj, IPdfObjectResolver resolver)
    {
        PdfDictionary d = xobj.Dictionary;
        ImageSource src = new ImageSource
        {
            Width = IntOf(resolver, d, "Width"),
            Height = IntOf(resolver, d, "Height"),
            BitsPerComponent = IntOf(resolver, d, "BitsPerComponent", 8),
            ImageMask = BoolOf(resolver, d, "ImageMask"),
            Data = xobj.RawBytes,
        };

        src.ColorSpace = d.TryGetValue(PdfName.Intern("ColorSpace"), out PdfPrimitive? cs) ? cs : null;
        src.Decode = d.TryGetValue(PdfName.Intern("Decode"), out PdfPrimitive? dec) ? resolver.Resolve(dec) as PdfArray : null;
        src.Interpolate = d.TryGetValue(PdfName.Intern("Interpolate"), out PdfPrimitive? ip) ? resolver.Resolve(ip) : null;
        src.Intent = d.TryGetValue(PdfName.Intern("Intent"), out PdfPrimitive? it) ? resolver.Resolve(it) : null;
        src.SMaskInData = d.TryGetValue(PdfName.Intern("SMaskInData"), out PdfPrimitive? sid) ? resolver.Resolve(sid) : null;
        src.Components = src.ImageMask ? 1 : ComponentsOf(resolver.Resolve(src.ColorSpace ?? PdfNull.Value), resolver);
        if (!src.ImageMask && src.Components <= 0)
        {
            return null;
        }

        if (!ReadFilters(resolver, d.GetAs<PdfPrimitive>(PdfName.Filter), d.GetAs<PdfPrimitive>(PdfName.Intern("DecodeParms")), src))
        {
            return null;
        }

        if (d.TryGetValue(PdfName.Intern("SMask"), out PdfPrimitive? sm) && resolver.Resolve(sm) is PdfStream smask)
        {
            src.SMask = FromXObject(smask, resolver);
            if (src.SMask is null)
            {
                return null;
            }
        }

        if (d.TryGetValue(PdfName.Intern("Mask"), out PdfPrimitive? mk))
        {
            PdfPrimitive mask = resolver.Resolve(mk);
            if (mask is PdfStream maskStream)
            {
                src.MaskImage = FromXObject(maskStream, resolver);
                if (src.MaskImage is null)
                {
                    return null;
                }
            }
            else if (mask is PdfArray colorKey)
            {
                src.ColorKeyMask = colorKey;
            }
        }

        return src;
    }

    /// <summary>
    /// Describes an inline image <c>BI … ID data EI</c> starting at
    /// <paramref name="biStart"/>, expanding abbreviations and resolving named
    /// colour spaces through <paramref name="resources"/>.
    /// </summary>
    internal static ImageSource? FromInline(
        byte[] content, int biStart, int eiEnd, PdfDictionary? resources, IPdfObjectResolver resolver)
    {
        using MemoryStream ms = new MemoryStream(content);
        using PdfTokenizer tok = new PdfTokenizer(ms, leaveOpen: true);
        tok.Seek(biStart);
        PdfToken bi = tok.Read();
        if (bi.RawText != "BI")
        {
            return null;
        }

        PdfDictionary dict = new PdfDictionary();
        long idEnd = -1;
        while (true)
        {
            PdfToken key = tok.Read();
            if (key.IsEndOfStream)
            {
                return null;
            }

            if (key.RawText == "ID")
            {
                idEnd = tok.Position;
                break;
            }

            if (key.Type != PdfTokenType.Name)
            {
                return null;
            }

            PdfPrimitive? value = ReadValue(tok);
            if (value is null)
            {
                return null;
            }

            dict.Set(PdfName.Intern(ExpandKey(DecodeName(key.RawText))), ExpandValue(value));
        }

        int dataStart = (int)idEnd;
        if (dataStart < content.Length && IsWhite(content[dataStart]))
        {
            dataStart++;
        }

        // The data ends before the whitespace that precedes EI.
        int dataEnd = eiEnd - 2;
        while (dataEnd > dataStart && IsWhite(content[dataEnd - 1]))
        {
            dataEnd--;
        }

        ImageSource src = new ImageSource
        {
            Width = IntOf(resolver, dict, "Width"),
            Height = IntOf(resolver, dict, "Height"),
            BitsPerComponent = IntOf(resolver, dict, "BitsPerComponent", 8),
            ImageMask = BoolOf(resolver, dict, "ImageMask"),
        };

        PdfPrimitive? cs = dict.TryGetValue(PdfName.Intern("ColorSpace"), out PdfPrimitive? csv) ? csv : null;
        if (cs is PdfName csName && !IsDeviceSpaceName(csName.Value))
        {
            // A named colour space from /Resources /ColorSpace.
            cs = ResolveResourceColorSpace(csName.Value, resources, resolver);
            if (cs is null)
            {
                return null;
            }
        }

        src.ColorSpace = cs;
        src.Decode = dict.TryGetValue(PdfName.Intern("Decode"), out PdfPrimitive? dec) ? dec as PdfArray : null;
        src.Interpolate = dict.TryGetValue(PdfName.Intern("Interpolate"), out PdfPrimitive? ip) ? ip : null;
        src.Intent = dict.TryGetValue(PdfName.Intern("Intent"), out PdfPrimitive? it) ? it : null;
        src.Components = src.ImageMask ? 1 : ComponentsOf(resolver.Resolve(cs ?? PdfNull.Value), resolver);
        if (!src.ImageMask && src.Components <= 0)
        {
            return null;
        }

        if (!ReadFilters(resolver, dict.GetAs<PdfPrimitive>(PdfName.Filter), dict.GetAs<PdfPrimitive>(PdfName.Intern("DecodeParms")), src))
        {
            return null;
        }

        int length = dataEnd - dataStart;
        if (src.Filters.Count == 0)
        {
            // Unfiltered: exactly rowBytes * height (whitespace bytes may be data).
            long exact = ((((long)src.Width * (src.ImageMask ? 1 : src.Components) * (src.ImageMask ? 1 : src.BitsPerComponent)) + 7) / 8) * src.Height;
            if (dataStart + exact > content.Length)
            {
                return null;
            }

            length = (int)exact;
        }

        if (length < 0)
        {
            return null;
        }

        src.Data = new byte[length];
        Array.Copy(content, dataStart, src.Data, 0, length);
        return src;
    }

    private static bool ReadFilters(IPdfObjectResolver resolver, PdfPrimitive? filter, PdfPrimitive? parms, ImageSource src)
    {
        PdfPrimitive? f = filter is null ? null : resolver.Resolve(filter);
        PdfPrimitive? p = parms is null ? null : resolver.Resolve(parms);
        List<PdfPrimitive> names = new List<PdfPrimitive>();
        if (f is PdfName single)
        {
            names.Add(single);
        }
        else if (f is PdfArray arr)
        {
            for (int i = 0; i < arr.Count; i++)
            {
                names.Add(resolver.Resolve(arr[i]));
            }
        }

        for (int i = 0; i < names.Count; i++)
        {
            if (names[i] is not PdfName n)
            {
                return false;
            }

            string canonical = FilterRegistry.ResolveAlias(n.Value);
            if (canonical == "DCT")
            {
                canonical = "DCTDecode";
            }

            src.Filters.Add(canonical);
            PdfPrimitive? itemParms = p is PdfArray parmArr
                ? (i < parmArr.Count ? resolver.Resolve(parmArr[i]) : null)
                : (i == 0 ? p : null);
            FilterParameters? fp = FilterParameters.FromDictionary(itemParms, 0);
            if (canonical == "JBIG2Decode" && itemParms is PdfDictionary jd
                && jd.TryGetValue(PdfName.Intern("JBIG2Globals"), out PdfPrimitive? gRef)
                && resolver.Resolve(gRef) is PdfStream globals)
            {
                byte[] g = ObjectImporter.DecodeStreamContent(globals);

                fp = (fp ?? new FilterParameters()) with { Jbig2Globals = g };
            }

            src.Parms.Add(fp);
        }

        return true;
    }

    // Components per sample for an image colour space (§8.6); -1 when unknown.
    private static int ComponentsOf(PdfPrimitive cs, IPdfObjectResolver resolver)
    {
        if (cs is PdfName name)
        {
            return name.Value switch
            {
                "DeviceGray" or "CalGray" or "G" => 1,
                "DeviceRGB" or "CalRGB" or "Lab" or "RGB" => 3,
                "DeviceCMYK" or "CMYK" => 4,
                _ => -1,
            };
        }

        if (cs is not PdfArray arr || arr.Count == 0 || resolver.Resolve(arr[0]) is not PdfName family)
        {
            return -1;
        }

        switch (family.Value)
        {
            case "Indexed":
            case "I":
            case "Separation":
                return 1;
            case "CalGray":
                return 1;
            case "CalRGB":
            case "Lab":
                return 3;
            case "DeviceN":
                return arr.Count > 1 && resolver.Resolve(arr[1]) is PdfArray names ? names.Count : -1;
            case "ICCBased":
                if (arr.Count > 1 && resolver.Resolve(arr[1]) is PdfStream icc
                    && icc.Dictionary.TryGetValue(PdfName.Intern("N"), out PdfPrimitive? n)
                    && resolver.Resolve(n) is PdfInteger count)
                {
                    return (int)count.Value;
                }

                return -1;
            default:
                return -1;
        }
    }

    private static bool IsDeviceSpaceName(string name) => name is
        "DeviceGray" or "DeviceRGB" or "DeviceCMYK" or "Indexed" or "Pattern";

    private static PdfPrimitive? ResolveResourceColorSpace(
        string name, PdfDictionary? resources, IPdfObjectResolver resolver)
    {
        if (resources is null
            || !resources.TryGetValue(PdfName.Intern("ColorSpace"), out PdfPrimitive? csv)
            || resolver.Resolve(csv) is not PdfDictionary spaces
            || !spaces.TryGetValue(PdfName.Intern(name), out PdfPrimitive? entry))
        {
            return null;
        }

        return entry;
    }

    private static string ExpandKey(string key) => key switch
    {
        "BPC" => "BitsPerComponent",
        "CS" => "ColorSpace",
        "D" => "Decode",
        "DP" => "DecodeParms",
        "F" => "Filter",
        "H" => "Height",
        "IM" => "ImageMask",
        "I" => "Interpolate",
        "W" => "Width",
        _ => key,
    };

    // Expands colour-space abbreviations inside inline-image values
    // (including inside an [/I /RGB ...] array).
    private static PdfPrimitive ExpandValue(PdfPrimitive value)
    {
        if (value is PdfName n)
        {
            return n.Value switch
            {
                "G" => PdfName.Intern("DeviceGray"),
                "RGB" => PdfName.Intern("DeviceRGB"),
                "CMYK" => PdfName.Intern("DeviceCMYK"),
                "I" => PdfName.Intern("Indexed"),
                _ => n,
            };
        }

        if (value is PdfArray arr)
        {
            PdfArray copy = new PdfArray([]);
            for (int i = 0; i < arr.Count; i++)
            {
                copy.Add(ExpandValue(arr[i]));
            }

            return copy;
        }

        return value;
    }

    private static PdfPrimitive? ReadValue(PdfTokenizer tok)
    {
        PdfToken t = tok.Read();
        return ReadValue(tok, t);
    }

    private static PdfPrimitive? ReadValue(PdfTokenizer tok, PdfToken t)
    {
        switch (t.Type)
        {
            case PdfTokenType.Integer:
                return long.TryParse(t.RawText, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long l)
                    ? new PdfInteger((int)l)
                    : null;
            case PdfTokenType.Real:
                return double.TryParse(t.RawText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d)
                    ? new PdfReal(d)
                    : null;
            case PdfTokenType.Name:
                return PdfName.Intern(DecodeName(t.RawText));
            case PdfTokenType.True:
                return PdfBoolean.True;
            case PdfTokenType.False:
                return PdfBoolean.False;
            case PdfTokenType.Null:
                return PdfNull.Value;
            case PdfTokenType.LiteralString:
            case PdfTokenType.HexString:
                return new PdfString(PageScrubber.ParseStringBytes(t.RawText));
            case PdfTokenType.ArrayStart:
                PdfArray arr = new PdfArray([]);
                while (true)
                {
                    PdfToken inner = tok.Read();
                    if (inner.IsEndOfStream)
                    {
                        return null;
                    }

                    if (inner.Type == PdfTokenType.ArrayEnd)
                    {
                        return arr;
                    }

                    PdfPrimitive? item = ReadValue(tok, inner);
                    if (item is null)
                    {
                        return null;
                    }

                    arr.Add(item);
                }

            case PdfTokenType.DictionaryStart:
                PdfDictionary dict = new PdfDictionary();
                while (true)
                {
                    PdfToken key = tok.Read();
                    if (key.IsEndOfStream)
                    {
                        return null;
                    }

                    if (key.Type == PdfTokenType.DictionaryEnd)
                    {
                        return dict;
                    }

                    if (key.Type != PdfTokenType.Name)
                    {
                        return null;
                    }

                    PdfPrimitive? v = ReadValue(tok);
                    if (v is null)
                    {
                        return null;
                    }

                    dict.Set(PdfName.Intern(DecodeName(key.RawText)), v);
                }

            default:
                // true/false may also surface as keywords.
                return t.RawText switch
                {
                    "true" => PdfBoolean.True,
                    "false" => PdfBoolean.False,
                    "null" => PdfNull.Value,
                    _ => null,
                };
        }
    }

    private static string DecodeName(string raw)
    {
        string name = raw.StartsWith('/') ? raw.Substring(1) : raw;
        if (!name.Contains('#'))
        {
            return name;
        }

        System.Text.StringBuilder sb = new System.Text.StringBuilder(name.Length);
        for (int i = 0; i < name.Length; i++)
        {
            if (name[i] == '#' && i + 2 < name.Length
                && int.TryParse(name.AsSpan(i + 1, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out int code))
            {
                sb.Append((char)code);
                i += 2;
            }
            else
            {
                sb.Append(name[i]);
            }
        }

        return sb.ToString();
    }

    private static bool IsWhite(byte b) =>
        b == 0x00 || b == 0x09 || b == 0x0A || b == 0x0C || b == 0x0D || b == 0x20;

    private static int IntOf(IPdfObjectResolver resolver, PdfDictionary d, string key, int fallback = 0)
    {
        if (d.TryGetValue(PdfName.Intern(key), out PdfPrimitive? v))
        {
            PdfPrimitive r = resolver.Resolve(v);
            if (r is PdfInteger i) { return (int)i.Value; }
            if (r is PdfReal re) { return (int)re.Value; }
        }

        return fallback;
    }

    private static bool BoolOf(IPdfObjectResolver resolver, PdfDictionary d, string key)
    {
        return d.TryGetValue(PdfName.Intern(key), out PdfPrimitive? v)
            && resolver.Resolve(v) is PdfBoolean b && b.Value;
    }
}
