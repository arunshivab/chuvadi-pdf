// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.4.8 (DCTDecode), §11.6.5.3 (soft-mask images)
// PHASE: 3.35.0 (backlog 62) — a JPEG behind another filter, a JPEG with a
//        soft mask, and a soft mask stored as a JPEG reach the SVG output.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Images;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Svg.Tests;

public sealed class JpegChainAndMask3350Tests
{
    private const int Size = 16;

    [Fact]
    public void AJpegBehindFlate_IsEmbedded()
    {
        byte[] jpeg = Jpeg(ImageColorFormat.Rgb24, (x, _) => x < Size / 2 ? (byte)255 : (byte)0);
        string image = Dict("/Filter [/FlateDecode /DCTDecode] /ColorSpace /DeviceRGB", Zlib(jpeg));

        string svg = Svg(image);

        Match match = Regex.Match(svg, "data:image/jpeg;base64,([A-Za-z0-9+/=]+)");
        match.Success.Should().BeTrue("the JPEG is embedded once its Flate layer is decoded");
        Convert.FromBase64String(match.Groups[1].Value).Should().StartWith(new byte[] { 0xFF, 0xD8 });
    }

    [Fact]
    public void AJpegWithASoftMask_IsEmbeddedWithAlpha()
    {
        byte[] jpeg = Jpeg(ImageColorFormat.Rgb24, (_, _) => 200);
        byte[] alpha = new byte[Size * Size];
        for (int i = 0; i < alpha.Length; i++)
        {
            alpha[i] = (i % Size) < Size / 2 ? (byte)0 : (byte)255;
        }

        string image = Dict("/Filter /DCTDecode /ColorSpace /DeviceRGB /SMask 6 0 R", jpeg);
        string mask = Dict("/ColorSpace /DeviceGray", alpha);

        byte[] rgba = Png(Svg(image, mask));

        rgba[3].Should().Be(0, "the left half is masked out");
        rgba[((Size - 1) * 4) + 3].Should().Be(255, "the right half is opaque");
    }

    [Fact]
    public void ASoftMaskStoredAsAJpeg_IsApplied()
    {
        byte[] colour = new byte[Size * Size * 3];
        Array.Fill(colour, (byte)128);
        byte[] maskJpeg = Jpeg(ImageColorFormat.Gray8, (x, _) => x < Size / 2 ? (byte)0 : (byte)255);

        string image = Dict("/ColorSpace /DeviceRGB /SMask 6 0 R", colour);
        string mask = Dict("/Filter /DCTDecode /ColorSpace /DeviceGray", maskJpeg);

        byte[] rgba = Png(Svg(image, mask));

        rgba[3].Should().BeLessThan(30, "the mask's dark half is transparent");
        rgba[((Size - 1) * 4) + 3].Should().BeGreaterThan(225, "its light half is opaque");
    }

    private static byte[] Jpeg(ImageColorFormat format, Func<int, int, byte> value)
    {
        ImageFrame frame = ImageFrame.Create(Size, Size, format);
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                byte v = value(x, y);
                frame.Pixels.SetPixelBgra(x, y, v, v, v, 255);
            }
        }

        using MemoryStream output = new MemoryStream();
        JpegEncoder.Encode(frame, output, 95);
        return output.ToArray();
    }

    private static byte[] Zlib(byte[] data)
    {
        using MemoryStream output = new MemoryStream();
        using (ZLibStream z = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            z.Write(data, 0, data.Length);
        }

        return output.ToArray();
    }

    // An image XObject dictionary and its data, as one "dict stream" body.
    private static string Dict(string entries, byte[] data) =>
        "<</Type/XObject/Subtype/Image/Width " + Size.ToString(CultureInfo.InvariantCulture)
        + "/Height " + Size.ToString(CultureInfo.InvariantCulture) + "/BitsPerComponent 8 " + entries
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
