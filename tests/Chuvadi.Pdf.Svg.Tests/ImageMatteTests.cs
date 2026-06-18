// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §11.6.5.3 — Soft-mask images with /Matte
// PHASE: Phase 2 — rendering conformance (image transparency)
//
// Regression: when a soft mask carries /Matte, the base image's colour samples
// are pre-multiplied against the matte colour. The renderer must recover the
// true colour c = (c' - m)/alpha + m, otherwise watermark-style images render
// with washed/shifted colours. Here a pixel stored as (100,100,100) under matte
// [0 0 0] at alpha 0.5 must come back out near (200,200,200), not (100,100,100).

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Svg.Tests;

public sealed class ImageMatteTests
{
    [Fact]
    public void SMaskWithMatte_UnpremultipliesColour()
    {
        using MemoryStream pdf = BuildPremultipliedImagePdf();
        using PdfDocument doc = PdfDocument.Open(pdf, leaveOpen: true);

        string svg = new SvgRenderer().RenderPage(doc, 0);

        Match match = Regex.Match(svg, "data:image/png;base64,([A-Za-z0-9+/=]+)");
        match.Success.Should().BeTrue("the page embeds the image as a PNG data URL");
        byte[] rgba = DecodeRgbaPng(Convert.FromBase64String(match.Groups[1].Value));

        // Stored premultiplied (100,100,100) at alpha ~0.5 -> recovered ~200.
        rgba[0].Should().BeInRange((byte)193, (byte)205, "red recovered c'/alpha");
        rgba[1].Should().BeInRange((byte)193, (byte)205, "green recovered");
        rgba[2].Should().BeInRange((byte)193, (byte)205, "blue recovered");
        rgba[3].Should().Be(128, "the soft-mask alpha is preserved");
        rgba[0].Should().BeGreaterThan(150, "colour must not remain premultiplied (was 100)");
    }

    private static MemoryStream BuildPremultipliedImagePdf()
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

        PdfArray kids = new PdfArray(new PdfPrimitive[] { new PdfReference(pageId) });
        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, kids);
        pagesDict.Set(PdfName.Count, 1);
        pagesDict.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(100), new PdfInteger(100),
        }));

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

        // 1x1 RGB, premultiplied against black: true (200,200,200) * 0.5 = (100,100,100).
        byte[] rgb = { 100, 100, 100 };
        PdfDictionary imageDict = new PdfDictionary();
        imageDict.Set(PdfName.Type, PdfName.Intern("XObject"));
        imageDict.Set(PdfName.Intern("Subtype"), PdfName.Intern("Image"));
        imageDict.Set(PdfName.Intern("Width"), 1);
        imageDict.Set(PdfName.Intern("Height"), 1);
        imageDict.Set(PdfName.Intern("BitsPerComponent"), 8);
        imageDict.Set(PdfName.Intern("ColorSpace"), PdfName.Intern("DeviceRGB"));
        imageDict.Set(PdfName.Intern("SMask"), new PdfReference(smaskId));
        imageDict.Set(PdfName.Length, rgb.Length);
        PdfStream imageStream = new PdfStream(imageDict, rgb);

        byte[] alpha = { 128 };
        PdfDictionary smaskDict = new PdfDictionary();
        smaskDict.Set(PdfName.Type, PdfName.Intern("XObject"));
        smaskDict.Set(PdfName.Intern("Subtype"), PdfName.Intern("Image"));
        smaskDict.Set(PdfName.Intern("Width"), 1);
        smaskDict.Set(PdfName.Intern("Height"), 1);
        smaskDict.Set(PdfName.Intern("BitsPerComponent"), 8);
        smaskDict.Set(PdfName.Intern("ColorSpace"), PdfName.Intern("DeviceGray"));
        smaskDict.Set(PdfName.Intern("Matte"), new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(0),
        }));
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

        MemoryStream output = new MemoryStream();
        PdfWriter.Write(output, objects, trailer);
        output.Position = 0;
        return output;
    }

    // Decodes an 8-bit RGBA PNG (colour type 6) into a flat RGBA buffer.
    private static byte[] DecodeRgbaPng(byte[] png)
    {
        int pos = 8;
        int width = 0;
        int height = 0;
        using MemoryStream idat = new MemoryStream();
        while (pos < png.Length)
        {
            int len = (png[pos] << 24) | (png[pos + 1] << 16) | (png[pos + 2] << 8) | png[pos + 3];
            string type = Encoding.ASCII.GetString(png, pos + 4, 4);
            int dataStart = pos + 8;
            if (type == "IHDR")
            {
                width = (png[dataStart] << 24) | (png[dataStart + 1] << 16)
                    | (png[dataStart + 2] << 8) | png[dataStart + 3];
                height = (png[dataStart + 4] << 24) | (png[dataStart + 5] << 16)
                    | (png[dataStart + 6] << 8) | png[dataStart + 7];
            }
            else if (type == "IDAT")
            {
                idat.Write(png, dataStart, len);
            }

            pos = dataStart + len + 4;
        }

        byte[] raw = Inflate(idat.ToArray());
        int stride = width * 4;
        byte[] outBuf = new byte[stride * height];
        byte[] prev = new byte[stride];
        int rp = 0;
        for (int y = 0; y < height; y++)
        {
            int filter = raw[rp++];
            byte[] cur = new byte[stride];
            Array.Copy(raw, rp, cur, 0, stride);
            rp += stride;
            for (int x = 0; x < stride; x++)
            {
                int a = x >= 4 ? cur[x - 4] : 0;
                int b = prev[x];
                int c = x >= 4 ? prev[x - 4] : 0;
                int add = filter switch
                {
                    1 => a,
                    2 => b,
                    3 => (a + b) / 2,
                    4 => Paeth(a, b, c),
                    _ => 0,
                };
                cur[x] = (byte)((cur[x] + add) & 0xFF);
            }

            Array.Copy(cur, 0, outBuf, y * stride, stride);
            prev = cur;
        }

        return outBuf;
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a);
        int pb = Math.Abs(p - b);
        int pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc) { return a; }
        return pb <= pc ? b : c;
    }

    private static byte[] Inflate(byte[] zlib)
    {
        // Skip the 2-byte zlib header; use raw DEFLATE.
        using MemoryStream input = new MemoryStream(zlib, 2, zlib.Length - 2);
        using System.IO.Compression.DeflateStream deflate =
            new System.IO.Compression.DeflateStream(input, System.IO.Compression.CompressionMode.Decompress);
        using MemoryStream output = new MemoryStream();
        deflate.CopyTo(output);
        return output.ToArray();
    }
}
