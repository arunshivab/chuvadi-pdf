// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.6.6.3 (Indexed colour spaces), §8.9.5.2 (Decode)
// PHASE: 3.32.0 (D-268) — SVG output expands Indexed images of 1, 2 and 4
//        bits per sample (and honours /Decode). Before 3.32.0 only 8-bit
//        palettes were expanded; smaller ones were misread.

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

public sealed class SvgIndexedImages3320Tests
{
    // Red, green, blue, white.
    private static readonly byte[] Palette4 = { 255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255 };

    private static readonly byte[] TwoBit = { 0x1B };

    private static readonly byte[] FourBit = { 0x01, 0x23 };

    private static readonly byte[] OneBit = { 0x50 };

    [Fact]
    public void TwoBitPalette_ExpandsToItsColours()
    {
        Colours(Svg(2, TwoBit, hival: 3)).Should().Equal((255, 0, 0), (0, 255, 0), (0, 0, 255), (255, 255, 255));
    }

    [Fact]
    public void FourBitPalette_ExpandsToItsColours()
    {
        Colours(Svg(4, FourBit, hival: 3)).Should().Equal((255, 0, 0), (0, 255, 0), (0, 0, 255), (255, 255, 255));
    }

    [Fact]
    public void OneBitPalette_ExpandsToItsColours()
    {
        Colours(Svg(1, OneBit, hival: 1)).Should().Equal((255, 0, 0), (0, 255, 0), (255, 0, 0), (0, 255, 0));
    }

    [Fact]
    public void Decode_ReversesTheIndices()
    {
        Colours(Svg(2, TwoBit, hival: 3, reverse: true)).Should().Equal((255, 255, 255), (0, 0, 255), (0, 255, 0), (255, 0, 0));
    }

    private static List<(int R, int G, int B)> Colours(string svg)
    {
        Match match = Regex.Match(svg, "data:image/png;base64,([A-Za-z0-9+/=]+)");
        match.Success.Should().BeTrue("the palette image must embed as a PNG");
        byte[] png = Convert.FromBase64String(match.Groups[1].Value);
        ReadBigEndian32(png, 16).Should().Be(4);
        ReadBigEndian32(png, 20).Should().Be(1);
        png[24].Should().Be(8, "the expanded image is 8 bits per channel");
        png[25].Should().Be(2, "an RGB PNG");

        byte[] row = FirstRow(png, 4 * 3);
        List<(int R, int G, int B)> colours = new List<(int R, int G, int B)>();
        for (int x = 0; x < 4; x++)
        {
            colours.Add((row[x * 3], row[(x * 3) + 1], row[(x * 3) + 2]));
        }

        return colours;
    }

    // The first row of an 8-bit RGB PNG, unfiltered (the first row has no
    // row above, so Up and Paeth reduce to simpler cases).
    private static byte[] FirstRow(byte[] png, int length)
    {
        using MemoryStream idat = new MemoryStream();
        int pos = 8;
        while (pos + 8 <= png.Length)
        {
            int chunk = ReadBigEndian32(png, pos);
            string type = Encoding.ASCII.GetString(png, pos + 4, 4);
            if (type == "IDAT")
            {
                idat.Write(png, pos + 8, chunk);
            }

            pos += 12 + chunk;
        }

        using ZLibStream inflate = new ZLibStream(new MemoryStream(idat.ToArray()), CompressionMode.Decompress);
        using MemoryStream raw = new MemoryStream();
        inflate.CopyTo(raw);
        byte[] data = raw.ToArray();
        int filter = data[0];
        byte[] row = new byte[length];
        for (int i = 0; i < length; i++)
        {
            int left = i >= 3 ? row[i - 3] : 0;
            int value = data[1 + i];
            row[i] = filter switch
            {
                0 or 2 => (byte)value,
                1 or 4 => (byte)(value + left),
                3 => (byte)(value + (left / 2)),
                _ => throw new InvalidDataException("unknown PNG filter"),
            };
        }

        return row;
    }

    private static string Svg(int bpc, byte[] samples, int hival, bool reverse = false)
    {
        PdfDictionary image = new PdfDictionary();
        image.Set(PdfName.Type, PdfName.Intern("XObject"));
        image.Set(PdfName.Intern("Subtype"), PdfName.Intern("Image"));
        image.Set(PdfName.Intern("Width"), 4);
        image.Set(PdfName.Intern("Height"), 1);
        image.Set(PdfName.Intern("BitsPerComponent"), bpc);
        image.Set(PdfName.Intern("ColorSpace"), new PdfArray(new PdfPrimitive[]
        {
            PdfName.Intern("Indexed"), PdfName.Intern("DeviceRGB"), new PdfInteger(hival), new PdfString(Palette4),
        }));
        if (reverse)
        {
            image.Set(PdfName.Intern("Decode"), new PdfArray(new PdfPrimitive[] { new PdfInteger((1 << bpc) - 1), new PdfInteger(0) }));
        }

        image.Set(PdfName.Length, samples.Length);

        PdfDictionary xobjects = new PdfDictionary();
        xobjects.Set(PdfName.Intern("Im0"), new PdfReference(5, 0));
        PdfDictionary resources = new PdfDictionary();
        resources.Set(PdfName.Intern("XObject"), xobjects);

        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(2, 0));
        PdfDictionary pages = new PdfDictionary();
        pages.Set(PdfName.Type, PdfName.Pages);
        pages.Set(PdfName.Kids, new PdfArray(new PdfPrimitive[] { new PdfReference(3, 0) }));
        pages.Set(PdfName.Count, 1);
        PdfDictionary page = new PdfDictionary();
        page.Set(PdfName.Type, PdfName.Page);
        page.Set(PdfName.Parent, new PdfReference(2, 0));
        page.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[] { new PdfInteger(0), new PdfInteger(0), new PdfInteger(200), new PdfInteger(200) }));
        page.Set(PdfName.Intern("Resources"), resources);
        page.Set(PdfName.Contents, new PdfReference(4, 0));
        byte[] content = Encoding.ASCII.GetBytes("q 200 0 0 200 0 0 cm /Im0 Do Q");
        PdfDictionary contentDict = new PdfDictionary();
        contentDict.Set(PdfName.Length, content.Length);

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(new PdfObjectId(1, 0), catalog),
            new PdfIndirectObject(new PdfObjectId(2, 0), pages),
            new PdfIndirectObject(new PdfObjectId(3, 0), page),
            new PdfIndirectObject(new PdfObjectId(4, 0), new PdfStream(contentDict, content)),
            new PdfIndirectObject(new PdfObjectId(5, 0), new PdfStream(image, samples)),
        };
        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(1, 0));
        MemoryStream output = new MemoryStream();
        PdfWriter.Write(output, objects, trailer);
        output.Position = 0;

        using PdfDocument doc = PdfDocument.Open(output, leaveOpen: false);
        return new SvgRenderer().RenderPage(doc, 0);
    }

    private static int ReadBigEndian32(byte[] buf, int offset) =>
        (buf[offset] << 24) | (buf[offset + 1] << 16) | (buf[offset + 2] << 8) | buf[offset + 3];
}
