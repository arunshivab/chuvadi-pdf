// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.9.6.3 (explicit masking), §8.9.6.4 (colour key masking)
// PHASE: 3.35.0 (backlog 60) — an image's /Mask reaches the SVG output.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Chuvadi.Pdf.Documents;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Svg.Tests;

public sealed class ExplicitMask3350Tests
{
    private const int Size = 16;

    [Fact]
    public void AStencilMask_HidesWhereItsSamplesAreOne()
    {
        string image = Dict("/BitsPerComponent 8 /ColorSpace /DeviceRGB /Mask 6 0 R", Rgb((_, _) => 150));
        string mask = Dict("/ImageMask true /BitsPerComponent 1", LeftHalfBits());

        byte[] rgba = Png(Svg(image, mask));

        rgba[3].Should().Be(0, "the mask's left half is 1");
        rgba[((Size - 1) * 4) + 3].Should().Be(255);
    }

    [Fact]
    public void AnInvertedStencilMask_HidesWhereItsSamplesAreZero()
    {
        string image = Dict("/BitsPerComponent 8 /ColorSpace /DeviceRGB /Mask 6 0 R", Rgb((_, _) => 150));
        string mask = Dict("/ImageMask true /BitsPerComponent 1 /Decode [1 0]", LeftHalfBits());

        byte[] rgba = Png(Svg(image, mask));

        rgba[3].Should().Be(255);
        rgba[((Size - 1) * 4) + 3].Should().Be(0);
    }

    [Fact]
    public void AColourKey_HidesSamplesInItsRanges()
    {
        string image = Dict("/BitsPerComponent 8 /ColorSpace /DeviceRGB /Mask [0 10 0 10 0 10]", Rgb((x, _) => x < Size / 2 ? (byte)0 : (byte)200));

        byte[] rgba = Png(Svg(image));

        rgba[3].Should().Be(0, "black is inside the key");
        rgba[((Size - 1) * 4) + 3].Should().Be(255);
    }

    [Fact]
    public void AColourKeyOnAPalette_ComparesTheIndices()
    {
        byte[] indices = new byte[Size * Size];
        for (int i = 0; i < indices.Length; i++)
        {
            indices[i] = (i % Size) < Size / 2 ? (byte)1 : (byte)0;
        }

        string image = Dict("/BitsPerComponent 8 /ColorSpace [/Indexed /DeviceRGB 1 <FF000000FF00>] /Mask [1 1]", indices);

        byte[] rgba = Png(Svg(image));

        rgba[3].Should().Be(0, "index 1 is the key");
        rgba[((Size - 1) * 4) + 3].Should().Be(255);
        rgba[(Size - 1) * 4].Should().Be(255, "index 0 is red");
    }

    [Fact]
    public void AMaskThatCannotBeRead_SkipsTheImage()
    {
        string image = Dict("/BitsPerComponent 8 /ColorSpace /DeviceRGB /Mask 6 0 R", Rgb((_, _) => 150));
        string mask = Dict("/ImageMask true /BitsPerComponent 1 /Filter /FlateDecode", new byte[] { 1, 2, 3, 4 });

        Svg(image, mask).Should().NotContain("data:image", "an image is not drawn unmasked over what it should partly cover");
    }

    private static byte[] Rgb(Func<int, int, byte> value)
    {
        byte[] rgb = new byte[Size * Size * 3];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                byte v = value(x, y);
                int o = ((y * Size) + x) * 3;
                rgb[o] = v;
                rgb[o + 1] = v;
                rgb[o + 2] = v;
            }
        }

        return rgb;
    }

    // 16 x 16 one-bit samples: 1 in the left half of every row.
    private static byte[] LeftHalfBits()
    {
        byte[] bits = new byte[Size * 2];
        for (int y = 0; y < Size; y++)
        {
            bits[y * 2] = 0xFF;
        }

        return bits;
    }

    // An image XObject dictionary and its data, as one "dict stream" body.
    private static string Dict(string entries, byte[] data) =>
        "<</Type/XObject/Subtype/Image/Width " + Size.ToString(CultureInfo.InvariantCulture)
        + "/Height " + Size.ToString(CultureInfo.InvariantCulture) + "" + entries
        + "/Length " + data.Length.ToString(CultureInfo.InvariantCulture) + ">>\nstream\n"
        + Encoding.Latin1.GetString(data) + "\nendstream";

    // A page that draws the image (object 5) at 100 x 100; a soft mask, when
    // given, is object 6.
    private static string Svg(string image, string? mask = null)
    {
        const string content = "q 100 0 0 100 50 50 cm /Im0 Do Q";
        List<string> bodies = new List<string>
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]/Resources<</XObject<</Im0 5 0 R>>>>/Contents 4 0 R>>",
            "<</Length " + content.Length.ToString(CultureInfo.InvariantCulture) + ">>\nstream\n" + content + "\nendstream",
            image,
        };
        if (mask is not null)
        {
            bodies.Add(mask);
        }

        StringBuilder sb = new StringBuilder("%PDF-1.7\n");
        List<int> offsets = new List<int>();
        for (int i = 0; i < bodies.Count; i++)
        {
            offsets.Add(sb.Length);
            sb.Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append(" 0 obj\n").Append(bodies[i]).Append("\nendobj\n");
        }

        int xref = sb.Length;
        sb.Append("xref\n0 ").Append((bodies.Count + 1).ToString(CultureInfo.InvariantCulture)).Append("\n0000000000 65535 f \n");
        foreach (int off in offsets)
        {
            sb.Append(off.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        sb.Append("trailer <</Size ").Append((bodies.Count + 1).ToString(CultureInfo.InvariantCulture))
            .Append("/Root 1 0 R>>\nstartxref\n").Append(xref.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF");
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(Encoding.Latin1.GetBytes(sb.ToString())), leaveOpen: false);
        return new SvgRenderer().RenderPage(doc, 0);
    }

    // The RGBA samples of the page's first PNG data URL.
    private static byte[] Png(string svg)
    {
        Match match = Regex.Match(svg, "data:image/png;base64,([A-Za-z0-9+/=]+)");
        match.Success.Should().BeTrue("the image is embedded as a PNG");
        byte[] png = Convert.FromBase64String(match.Groups[1].Value);
        png[25].Should().Be(6, "a soft mask makes an RGBA PNG");

        int width = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        int height = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
        using MemoryStream idat = new MemoryStream();
        int pos = 8;
        while (pos + 8 <= png.Length)
        {
            int length = (png[pos] << 24) | (png[pos + 1] << 16) | (png[pos + 2] << 8) | png[pos + 3];
            string type = Encoding.ASCII.GetString(png, pos + 4, 4);
            if (type == "IDAT")
            {
                idat.Write(png, pos + 8, length);
            }

            pos += 12 + length;
        }

        using MemoryStream compressed = new MemoryStream(idat.ToArray());
        using ZLibStream inflate = new ZLibStream(compressed, CompressionMode.Decompress);
        using MemoryStream raw = new MemoryStream();
        inflate.CopyTo(raw);
        byte[] rows = raw.ToArray();
        byte[] rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            Array.Copy(rows, (y * ((width * 4) + 1)) + 1, rgba, y * width * 4, width * 4);
        }

        return rgba;
    }
}
