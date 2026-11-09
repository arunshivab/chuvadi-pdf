// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.9.5 (image dictionaries: 1-bit DeviceGray),
//        §7.4.4 (FlateDecode)
// PHASE: 3.32.0 (D-218 PDF-09 P4, Arun 7 Nov) — the black-and-white
//        compression level: every page as one 1-bit image. Also writes the
//        image-only document for the colour image levels (D-268).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Rendering;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// Writes an image-only PDF in which every page of the source is one 1-bit
/// (black and white) image, Flate-compressed. Measured on A1 drawings
/// (7 Nov): at 150 dpi small grey dimension text stays readable only when
/// every pixel that is not near-white becomes black, so that is the rule for
/// lines and text. Large grey fills either become solid black or (the
/// default) a dot pattern, which keeps them visible as fills and keeps the
/// marks drawn over them.
/// </summary>
internal static class BilevelPageWriter
{
    // A pixel darker than this luminance (0-255) is ink. Near-white only, so
    // thin grey and coloured lines and text survive.
    internal const int InkThreshold = 235;

    // Half-size of the window used to tell a filled area from a line.
    private const int FillWindowRadius = 4;

    // A pixel is inside a fill when this share of its window is ink.
    private const double FillDensity = 0.85;

    // Pixels darker than this inside a fill are kept solid (dark fills, lines
    // drawn over a fill).
    private const int SolidBelow = 60;

    // 4 x 4 ordered-dither thresholds (Bayer), scaled to 0-255 below.
    private static readonly int[] Bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

    /// <summary>
    /// Renders every page at <paramref name="dpi"/> with its rotation applied
    /// and writes the black-and-white PDF. Each page keeps the size it is
    /// shown at (its CropBox, rotated).
    /// </summary>
    /// <param name="document">The source.</param>
    /// <param name="dpi">The resolution of the page images.</param>
    /// <param name="dotPatternFills">True for dot-pattern fills, false for solid fills.</param>
    /// <returns>The PDF bytes.</returns>
    internal static byte[] Build(PdfDocument document, double dpi, bool dotPatternFills) =>
        BuildPages(document, dpi, buffer => BilevelImage(buffer, ToBits(buffer, dotPatternFills)));

    /// <summary>
    /// Writes an image-only PDF: every page of <paramref name="document"/> is
    /// rendered at <paramref name="dpi"/> with its rotation applied, and
    /// <paramref name="imageFor"/> turns the render into the page's one image
    /// stream. Each page keeps the size it is shown at (its CropBox, rotated).
    /// Shared by the black-and-white and colour image levels (3.32.0).
    /// </summary>
    internal static byte[] BuildPages(PdfDocument document, double dpi, Func<PixelBuffer, PdfStream> imageFor) =>
        BuildPages(document, dpi, new[] { imageFor })[0];

    /// <summary>
    /// Like <see cref="BuildPages(PdfDocument, double, Func{PixelBuffer, PdfStream})"/>
    /// for several image kinds at once: each page is rendered once and every
    /// encoder in <paramref name="imagesFor"/> makes its own document from it
    /// (3.32.0). Used by target mode, where rendering is most of the time.
    /// </summary>
    internal static byte[][] BuildPages(PdfDocument document, double dpi, Func<PixelBuffer, PdfStream>[] imagesFor)
    {
        List<PdfIndirectObject>[] objects = new List<PdfIndirectObject>[imagesFor.Length];
        PdfArray[] kids = new PdfArray[imagesFor.Length];
        for (int k = 0; k < imagesFor.Length; k++)
        {
            objects[k] = new List<PdfIndirectObject>();
            kids[k] = new PdfArray();
        }

        int nextId = 3;
        PageRasterizer rasterizer = new PageRasterizer(
            document.Objects,
            new RenderOptions
            {
                Dpi = dpi,
                ApplyPageRotation = true,
            });

        for (int i = 0; i < document.PageCount; i++)
        {
            PixelBuffer buffer = rasterizer.Rasterize(document.Pages[i]);
            double widthPts = buffer.Width * 72.0 / dpi;
            double heightPts = buffer.Height * 72.0 / dpi;
            PdfObjectId imageId = new PdfObjectId(nextId++, 0);
            PdfObjectId contentId = new PdfObjectId(nextId++, 0);
            PdfObjectId pageId = new PdfObjectId(nextId++, 0);

            for (int k = 0; k < imagesFor.Length; k++)
            {
                objects[k].Add(new PdfIndirectObject(imageId, imagesFor[k](buffer)));

                byte[] content = Encoding.ASCII.GetBytes(string.Format(
                    CultureInfo.InvariantCulture, "q {0:0.###} 0 0 {1:0.###} 0 0 cm /Im0 Do Q\n", widthPts, heightPts));
                PdfDictionary contentDict = new PdfDictionary();
                contentDict.Set(PdfName.Length, content.Length);
                objects[k].Add(new PdfIndirectObject(contentId, new PdfStream(contentDict, content)));

                PdfDictionary xobjects = new PdfDictionary();
                xobjects.Set(PdfName.Intern("Im0"), new PdfReference(imageId));
                PdfDictionary resources = new PdfDictionary();
                resources.Set(PdfName.XObject, xobjects);

                PdfDictionary page = new PdfDictionary();
                page.Set(PdfName.Type, PdfName.Page);
                page.Set(PdfName.Intern("Parent"), new PdfReference(2, 0));
                page.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[]
                {
                    new PdfInteger(0), new PdfInteger(0), new PdfReal(widthPts), new PdfReal(heightPts),
                }));
                page.Set(PdfName.Resources, resources);
                page.Set(PdfName.Contents, new PdfReference(contentId));
                objects[k].Add(new PdfIndirectObject(pageId, page));
                kids[k].Add(new PdfReference(pageId));
            }
        }

        byte[][] results = new byte[imagesFor.Length][];
        for (int k = 0; k < imagesFor.Length; k++)
        {
            PdfDictionary pages = new PdfDictionary();
            pages.Set(PdfName.Type, PdfName.Pages);
            pages.Set(PdfName.Kids, kids[k]);
            pages.Set(PdfName.Count, document.PageCount);
            PdfDictionary catalog = new PdfDictionary();
            catalog.Set(PdfName.Type, PdfName.Catalog);
            catalog.Set(PdfName.Pages, new PdfReference(2, 0));
            objects[k].Insert(0, new PdfIndirectObject(new PdfObjectId(2, 0), pages));
            objects[k].Insert(0, new PdfIndirectObject(new PdfObjectId(1, 0), catalog));

            PdfDictionary trailer = new PdfDictionary();
            trailer.Set(PdfName.Root, new PdfReference(1, 0));
            using MemoryStream output = new MemoryStream();
            PdfWriter.Write(output, objects[k], trailer);
            results[k] = output.ToArray();
        }

        return results;
    }

    /// <summary>The 1-bit DeviceGray image stream for packed rows from <see cref="ToBits"/>.</summary>
    internal static PdfStream BilevelImage(PixelBuffer buffer, byte[] bits)
    {
        byte[] packed = Deflate(bits);
        PdfDictionary image = new PdfDictionary();
        image.Set(PdfName.Type, PdfName.XObject);
        image.Set(PdfName.Subtype, PdfName.Intern("Image"));
        image.Set(PdfName.Intern("Width"), buffer.Width);
        image.Set(PdfName.Intern("Height"), buffer.Height);
        image.Set(PdfName.Intern("ColorSpace"), PdfName.Intern("DeviceGray"));
        image.Set(PdfName.Intern("BitsPerComponent"), 1);
        image.Set(PdfName.Intern("Filter"), PdfName.Intern("FlateDecode"));
        image.Set(PdfName.Length, packed.Length);
        return new PdfStream(image, packed);
    }

    /// <summary>
    /// Packs a rendered page into 1-bit rows (1 = white, the DeviceGray
    /// default), MSB first, each row padded to a whole byte.
    /// </summary>
    internal static byte[] ToBits(PixelBuffer buffer, bool dotPatternFills)
    {
        int w = buffer.Width;
        int h = buffer.Height;
        ReadOnlySpan<byte> px = buffer.Pixels;
        byte[] lum = new byte[w * h];
        for (int i = 0, o = 0; i < lum.Length; i++, o += 4)
        {
            // BGRA, Rec. 601 luma.
            lum[i] = (byte)(((px[o + 2] * 299) + (px[o + 1] * 587) + (px[o] * 114)) / 1000);
        }

        // Summed-area table of ink pixels, for the fill test.
        int[]? sums = null;
        if (dotPatternFills)
        {
            sums = new int[(w + 1) * (h + 1)];
            for (int y = 0; y < h; y++)
            {
                int row = 0;
                for (int x = 0; x < w; x++)
                {
                    row += lum[(y * w) + x] < InkThreshold ? 1 : 0;
                    sums[((y + 1) * (w + 1)) + x + 1] = sums[(y * (w + 1)) + x + 1] + row;
                }
            }
        }

        int stride = (w + 7) / 8;
        byte[] bits = new byte[stride * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int l = lum[(y * w) + x];
                bool white;
                if (sums is not null && InFill(sums, w, h, x, y) && l > SolidBelow)
                {
                    white = l > (Bayer[((y & 3) * 4) + (x & 3)] * 16) + 8;
                }
                else
                {
                    white = l >= InkThreshold;
                }

                if (white)
                {
                    bits[(y * stride) + (x >> 3)] |= (byte)(0x80 >> (x & 7));
                }
            }
        }

        return bits;
    }

    private static bool InFill(int[] sums, int w, int h, int x, int y)
    {
        int x0 = Math.Max(0, x - FillWindowRadius);
        int y0 = Math.Max(0, y - FillWindowRadius);
        int x1 = Math.Min(w, x + FillWindowRadius + 1);
        int y1 = Math.Min(h, y + FillWindowRadius + 1);
        int ink = sums[(y1 * (w + 1)) + x1] - sums[(y0 * (w + 1)) + x1] - sums[(y1 * (w + 1)) + x0] + sums[(y0 * (w + 1)) + x0];
        return ink > FillDensity * (x1 - x0) * (y1 - y0);
    }

    internal static byte[] Deflate(byte[] data)
    {
        using MemoryStream output = new MemoryStream();
        using (ZLibStream z = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            z.Write(data, 0, data.Length);
        }

        return output.ToArray();
    }
}
