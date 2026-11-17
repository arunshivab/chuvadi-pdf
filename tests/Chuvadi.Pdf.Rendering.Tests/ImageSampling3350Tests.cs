// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.9.5 (image interpolation), §11.6.5.3 (soft-mask images)
// PHASE: 3.35.0 (item 55, D-400) — an image drawn smaller than its pixels is
//        averaged, and one drawn up to four times larger is interpolated,
//        with colour weighted by alpha; one drawn larger stays sharp.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.Tests;

public sealed class ImageSampling3350Tests
{
    // An 8x8 yellow image whose colour is black where its soft mask is 0
    // (a 1-pixel transparent border), drawn three times larger: the edge
    // fades from yellow to white with no dark or pale fringe.
    [Fact]
    public void SmallUpscale_InterpolatesTheMaskEdge_WithoutAFringe()
    {
        PixelBuffer px = Render("q 24 0 0 24 100 100 cm /Im Do Q", BorderedImage(8));

        List<(byte B, byte G, byte R)> row = Row(px, 400 - 112, 95, 130);
        row.Should().OnlyContain(p => p.R > 240, "no black from the transparent pixels mixes in");
        row.Select(p => p.B).Distinct().Count().Should().BeGreaterThan(3, "the edge is a gradient, not a step");
    }

    [Fact]
    public void Downscale_AveragesTheMask()
    {
        // A red image whose mask alternates 255 and 0 pixel by pixel, drawn
        // a quarter of its size: half-covered red over white.
        StringBuilder colour = new StringBuilder();
        StringBuilder mask = new StringBuilder();
        for (int i = 0; i < 64; i++)
        {
            colour.Append("FF0000");
            mask.Append(((i / 8) + (i % 8)) % 2 == 0 ? "FF" : "00");
        }

        PixelBuffer px = Render("q 2 0 0 2 100 100 cm /Im Do Q", Image(8, colour.ToString(), mask.ToString()));

        (byte _, byte g, byte r, byte _) = px.GetPixelBgra(100, 299);
        r.Should().BeGreaterThan(240);
        g.Should().BeInRange(96, 160);
    }

    [Fact]
    public void LargeUpscale_StaysSharp()
    {
        PixelBuffer px = Render("q 64 0 0 64 100 100 cm /Im Do Q", BorderedImage(8));

        Row(px, 400 - 132, 95, 170).Select(p => p.B).Distinct().Count().Should().Be(2, "white and yellow only");
    }

    private static string BorderedImage(int n)
    {
        StringBuilder colour = new StringBuilder();
        StringBuilder mask = new StringBuilder();
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                bool inside = x > 0 && y > 0 && x < n - 1 && y < n - 1;
                colour.Append(inside ? "FFD000" : "000000");
                mask.Append(inside ? "FF" : "00");
            }
        }

        return Image(n, colour.ToString(), mask.ToString());
    }

    // Resources: /Im (object 5) with its soft mask (object 6).
    private static string Image(int n, string colourHex, string maskHex)
    {
        string size = n.ToString(CultureInfo.InvariantCulture);
        return "|5|<</Type/XObject/Subtype/Image/Width " + size + "/Height " + size
            + "/ColorSpace/DeviceRGB/BitsPerComponent 8/SMask 6 0 R/Filter/ASCIIHexDecode/Length "
            + (colourHex.Length + 1).ToString(CultureInfo.InvariantCulture) + ">>\nstream\n" + colourHex + ">\nendstream"
            + "|6|<</Type/XObject/Subtype/Image/Width " + size + "/Height " + size
            + "/ColorSpace/DeviceGray/BitsPerComponent 8/Filter/ASCIIHexDecode/Length "
            + (maskHex.Length + 1).ToString(CultureInfo.InvariantCulture) + ">>\nstream\n" + maskHex + ">\nendstream";
    }

    private static List<(byte B, byte G, byte R)> Row(PixelBuffer px, int y, int x0, int x1)
    {
        List<(byte B, byte G, byte R)> row = new List<(byte B, byte G, byte R)>();
        for (int x = x0; x < x1; x++)
        {
            (byte b, byte g, byte r, byte _) = px.GetPixelBgra(x, y);
            row.Add((b, g, r));
        }

        return row;
    }

    private static PixelBuffer Render(string content, string image)
    {
        string[] parts = image.Split('|');
        List<string> bodies = new List<string>
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 400 400]/Resources<</XObject<</Im 5 0 R>>>>/Contents 4 0 R>>",
            "<</Length " + content.Length.ToString(CultureInfo.InvariantCulture) + ">>\nstream\n" + content + "\nendstream",
            parts[2],
            parts[4],
        };

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
        return new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72 }).Rasterize(doc.Pages[0]);
    }
}
