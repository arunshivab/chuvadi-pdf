// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §11.6.5.3 (soft-mask images)
// Regression coverage: an image XObject with an /SMask must render with the
// mask applied as alpha. Previously the mask was dropped and the conventionally
// black colour bytes of transparent regions rendered as a solid black box.

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

public sealed class ImageSoftMaskTests
{
    [Fact]
    public void SoftMask_IsAppliedAsAlpha_NotRenderedBlack()
    {
        using MemoryStream pdf = BuildSoftMaskPdf();
        using PdfDocument doc = PdfDocument.Open(pdf, leaveOpen: true);

        string svg = new SvgRenderer().RenderPage(doc, 0);

        Match match = Regex.Match(svg, "data:image/png;base64,([A-Za-z0-9+/=]+)");
        match.Success.Should().BeTrue("the page embeds the image as a PNG data URL");

        byte[] png = Convert.FromBase64String(match.Groups[1].Value);
        png[25].Should().Be(6, "an /SMask must produce an RGBA PNG (colour type 6), not RGB");

        byte[] rgba = DecodeRgbaPng(png);

        // Pixel order matches the 2x2 image: px0..px3. SMask alpha = [0,255,255,0].
        rgba[3].Should().Be(0, "px0 is masked out (transparent)");
        rgba[7].Should().Be(255, "px1 is fully opaque");
        rgba[11].Should().Be(255, "px2 is fully opaque");
        rgba[15].Should().Be(0, "px3 is masked out (transparent)");

        // Colour bytes survive: px1 was green.
        rgba[4].Should().Be(0);
        rgba[5].Should().Be(255);
        rgba[6].Should().Be(0);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    // 2x2 DeviceRGB image (red/green/blue/yellow) with a DeviceGray /SMask
    // (alpha 0/255/255/0), drawn once on a single page.
    private static MemoryStream BuildSoftMaskPdf()
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
            new PdfInteger(0), new PdfInteger(0),
            new PdfInteger(200), new PdfInteger(200)
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

        byte[] rgb =
        {
            255, 0, 0,
            0, 255, 0,
            0, 0, 255,
            255, 255, 0,
        };
        PdfDictionary imageDict = new PdfDictionary();
        imageDict.Set(PdfName.Type, PdfName.Intern("XObject"));
        imageDict.Set(PdfName.Intern("Subtype"), PdfName.Intern("Image"));
        imageDict.Set(PdfName.Intern("Width"), 2);
        imageDict.Set(PdfName.Intern("Height"), 2);
        imageDict.Set(PdfName.Intern("BitsPerComponent"), 8);
        imageDict.Set(PdfName.Intern("ColorSpace"), PdfName.Intern("DeviceRGB"));
        imageDict.Set(PdfName.Intern("SMask"), new PdfReference(smaskId));
        imageDict.Set(PdfName.Length, rgb.Length);
        PdfStream imageStream = new PdfStream(imageDict, rgb);

        byte[] alpha = { 0, 255, 255, 0 };
        PdfDictionary smaskDict = new PdfDictionary();
        smaskDict.Set(PdfName.Type, PdfName.Intern("XObject"));
        smaskDict.Set(PdfName.Intern("Subtype"), PdfName.Intern("Image"));
        smaskDict.Set(PdfName.Intern("Width"), 2);
        smaskDict.Set(PdfName.Intern("Height"), 2);
        smaskDict.Set(PdfName.Intern("BitsPerComponent"), 8);
        smaskDict.Set(PdfName.Intern("ColorSpace"), PdfName.Intern("DeviceGray"));
        smaskDict.Set(PdfName.Length, alpha.Length);
        PdfStream smaskStream = new PdfStream(smaskDict, alpha);

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(catalogId, catalogDict),
            new PdfIndirectObject(pagesId, pagesDict),
            new PdfIndirectObject(pageId, pageDict),
            new PdfIndirectObject(contentId, contentStream),
            new PdfIndirectObject(imageId, imageStream),
            new PdfIndirectObject(smaskId, smaskStream),
        };

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        ms.Seek(0, SeekOrigin.Begin);
        return ms;
    }

    // Decodes an 8-bit RGBA PNG (colour type 6, filter 0 per row, as emitted by
    // ImageEncoder) into a flat width*height*4 RGBA buffer.
    private static byte[] DecodeRgbaPng(byte[] png)
    {
        int width = ReadBigEndian32(png, 16);
        int height = ReadBigEndian32(png, 20);

        using MemoryStream idat = new MemoryStream();
        int pos = 8;
        while (pos + 8 <= png.Length)
        {
            int length = ReadBigEndian32(png, pos);
            string type = Encoding.ASCII.GetString(png, pos + 4, 4);
            if (type == "IDAT")
            {
                idat.Write(png, pos + 8, length);
            }

            if (type == "IEND")
            {
                break;
            }

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

        int stride = (width * 4) + 1;
        byte[] rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            int rowStart = (y * stride) + 1; // skip the per-row filter byte (0)
            for (int x = 0; x < width * 4; x++)
            {
                rgba[(y * width * 4) + x] = raw[rowStart + x];
            }
        }

        return rgba;
    }

    private static int ReadBigEndian32(byte[] buf, int offset)
    {
        return (buf[offset] << 24) | (buf[offset + 1] << 16)
            | (buf[offset + 2] << 8) | buf[offset + 3];
    }
}
