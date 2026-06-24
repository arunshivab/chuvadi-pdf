// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.6.6.3 (Indexed colour spaces)
// Regression coverage (LA-30): an 8-bit Indexed (palette) image decodes to one
// index byte per pixel. The colour-space enum cannot represent a palette, so
// the indices must be expanded to DeviceRGB before encoding; otherwise the
// encoder reads three times the data and throws ArgumentOutOfRangeException.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Svg.Tests;

public sealed class ImageIndexedTests
{
    [Fact]
    public void IndexedImage_ExpandsPaletteToRgb_DoesNotThrow()
    {
        using MemoryStream pdf = BuildIndexedPdf(withSoftMask: false);
        using PdfDocument doc = PdfDocument.Open(pdf, leaveOpen: true);

        string svg = new SvgRenderer().RenderPage(doc, 0);

        Match match = Regex.Match(svg, "data:image/png;base64,([A-Za-z0-9+/=]+)");
        match.Success.Should().BeTrue("the indexed image must embed as a PNG, not be dropped");

        byte[] png = Convert.FromBase64String(match.Groups[1].Value);
        ReadBigEndian32(png, 16).Should().Be(2);  // width
        ReadBigEndian32(png, 20).Should().Be(2);  // height
        png[25].Should().Be(2, "a plain indexed image expands to an RGB PNG (colour type 2)");

        // Palette: index 0 = red, index 1 = blue. Index data [0,1,1,0].
        byte[] rgb = DecodePng(png, 3);
        Pixel(rgb, 2, 3, 0, 0).Should().Be((255, 0, 0));   // red
        Pixel(rgb, 2, 3, 1, 0).Should().Be((0, 0, 255));   // blue
        Pixel(rgb, 2, 3, 0, 1).Should().Be((0, 0, 255));   // blue
        Pixel(rgb, 2, 3, 1, 1).Should().Be((255, 0, 0));   // red
    }

    [Fact]
    public void IndexedImage_WithSoftMask_ProducesRgba()
    {
        using MemoryStream pdf = BuildIndexedPdf(withSoftMask: true);
        using PdfDocument doc = PdfDocument.Open(pdf, leaveOpen: true);

        string svg = new SvgRenderer().RenderPage(doc, 0);

        Match match = Regex.Match(svg, "data:image/png;base64,([A-Za-z0-9+/=]+)");
        match.Success.Should().BeTrue();
        byte[] png = Convert.FromBase64String(match.Groups[1].Value);
        png[25].Should().Be(6, "an indexed image with an /SMask expands to RGBA (colour type 6)");

        byte[] rgba = DecodePng(png, 4);
        // index 0 = red at full/zero alpha per mask [0,255,255,0]
        (rgba[0], rgba[1], rgba[2]).Should().Be(((byte)255, (byte)0, (byte)0));
        rgba[3].Should().Be(0);                 // top-left alpha 0
        rgba[(1 * 4) + 3].Should().Be(255);     // top-right alpha 255
    }

    private static (byte, byte, byte) Pixel(byte[] buf, int width, int channels, int x, int y)
    {
        int i = ((y * width) + x) * channels;
        return (buf[i], buf[i + 1], buf[i + 2]);
    }

    // 2x2 Indexed image (palette red/blue, indices [0,1,1,0]); optionally with a
    // DeviceGray /SMask (alpha 0/255/255/0).
    private static MemoryStream BuildIndexedPdf(bool withSoftMask)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);
        PdfObjectId contentId = new PdfObjectId(4, 0);
        PdfObjectId imageId = new PdfObjectId(5, 0);
        PdfObjectId smaskId = new PdfObjectId(6, 0);

        PdfDictionary catalogDict = new PdfDictionary();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

        PdfArray kids = new PdfArray([]);
        kids.Add(new PdfReference(pageId));
        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, kids);
        pagesDict.Set(PdfName.Count, 1);
        pagesDict.Set(PdfName.MediaBox, new PdfArray([
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(200), new PdfInteger(200)
        ]));

        PdfDictionary xobjects = new PdfDictionary();
        xobjects.Set(PdfName.Intern("Im0"), new PdfReference(imageId));
        PdfDictionary resources = new PdfDictionary();
        resources.Set(PdfName.Intern("XObject"), xobjects);

        PdfDictionary pageDict = new PdfDictionary();
        pageDict.Set(PdfName.Type, PdfName.Page);
        pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
        pageDict.Set(PdfName.Contents, new PdfReference(contentId));
        pageDict.Set(PdfName.Intern("Resources"), resources);

        byte[] contentBytes = Encoding.ASCII.GetBytes("q 100 0 0 100 0 0 cm /Im0 Do Q");
        PdfDictionary contentDict = new PdfDictionary();
        contentDict.Set(PdfName.Length, contentBytes.Length);
        PdfStream contentStream = new PdfStream(contentDict, contentBytes);

        // Indexed colour space: [/Indexed /DeviceRGB 1 <red,blue palette>].
        byte[] palette = { 255, 0, 0, 0, 0, 255 };
        PdfArray indexed = new PdfArray([]);
        indexed.Add(PdfName.Intern("Indexed"));
        indexed.Add(PdfName.Intern("DeviceRGB"));
        indexed.Add(new PdfInteger(1));
        indexed.Add(new PdfString(palette));

        byte[] indices = { 0, 1, 1, 0 };
        PdfDictionary imageDict = new PdfDictionary();
        imageDict.Set(PdfName.Type, PdfName.Intern("XObject"));
        imageDict.Set(PdfName.Intern("Subtype"), PdfName.Intern("Image"));
        imageDict.Set(PdfName.Intern("Width"), 2);
        imageDict.Set(PdfName.Intern("Height"), 2);
        imageDict.Set(PdfName.Intern("BitsPerComponent"), 8);
        imageDict.Set(PdfName.Intern("ColorSpace"), indexed);
        if (withSoftMask) { imageDict.Set(PdfName.Intern("SMask"), new PdfReference(smaskId)); }
        imageDict.Set(PdfName.Length, indices.Length);
        PdfStream imageStream = new PdfStream(imageDict, indices);

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(catalogId, catalogDict),
            new PdfIndirectObject(pagesId, pagesDict),
            new PdfIndirectObject(pageId, pageDict),
            new PdfIndirectObject(contentId, contentStream),
            new PdfIndirectObject(imageId, imageStream),
        };

        if (withSoftMask)
        {
            byte[] alpha = { 0, 255, 255, 0 };
            PdfDictionary smaskDict = new PdfDictionary();
            smaskDict.Set(PdfName.Type, PdfName.Intern("XObject"));
            smaskDict.Set(PdfName.Intern("Subtype"), PdfName.Intern("Image"));
            smaskDict.Set(PdfName.Intern("Width"), 2);
            smaskDict.Set(PdfName.Intern("Height"), 2);
            smaskDict.Set(PdfName.Intern("BitsPerComponent"), 8);
            smaskDict.Set(PdfName.Intern("ColorSpace"), PdfName.Intern("DeviceGray"));
            smaskDict.Set(PdfName.Length, alpha.Length);
            objects.Add(new PdfIndirectObject(smaskId, new PdfStream(smaskDict, alpha)));
        }

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        ms.Seek(0, SeekOrigin.Begin);
        return ms;
    }

    private static byte[] DecodePng(byte[] png, int channels)
    {
        int width = ReadBigEndian32(png, 16);
        int height = ReadBigEndian32(png, 20);

        using MemoryStream idat = new MemoryStream();
        int pos = 8;
        while (pos + 8 <= png.Length)
        {
            int length = ReadBigEndian32(png, pos);
            string type = Encoding.ASCII.GetString(png, pos + 4, 4);
            if (type == "IDAT") { idat.Write(png, pos + 8, length); }
            if (type == "IEND") { break; }
            pos += 12 + length;
        }

        byte[] raw;
        using (MemoryStream compressed = new MemoryStream(idat.ToArray()))
        using (ZLibStream inflate = new ZLibStream(compressed, CompressionMode.Decompress))
        using (MemoryStream output = new MemoryStream())
        {
            inflate.CopyTo(output);
            raw = output.ToArray();
        }

        int stride = (width * channels) + 1;
        byte[] pixels = new byte[width * height * channels];
        for (int y = 0; y < height; y++)
        {
            int rowStart = (y * stride) + 1;
            for (int x = 0; x < width * channels; x++)
            {
                pixels[(y * width * channels) + x] = raw[rowStart + x];
            }
        }

        return pixels;
    }

    private static int ReadBigEndian32(byte[] buf, int offset)
    {
        return (buf[offset] << 24) | (buf[offset + 1] << 16)
            | (buf[offset + 2] << 8) | buf[offset + 3];
    }
}
