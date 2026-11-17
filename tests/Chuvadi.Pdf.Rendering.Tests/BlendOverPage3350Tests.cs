// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §11.4.7 — page group; §11.3.5 — blend mode
// PHASE: 3.35.0 (backlog 67) — blending over the unpainted page.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.Tests;

public sealed class BlendOverPage3350Tests
{
    // A yellow square, then a red square in Difference mode that half covers
    // it. Where the page is unpainted the red shows as itself (the page
    // group's backdrop is transparent); over the yellow it gives green.
    private const string Content =
        "1 1 0 rg 100 100 100 100 re f /GS1 gs 1 0 0 rg 150 100 150 100 re f";

    [Fact]
    public void Difference_OverUnpaintedPage_KeepsTheColour()
    {
        PixelBuffer px = Render(Content);

        (byte b, byte g, byte r, byte _) = px.GetPixelBgra(250, 250);
        r.Should().BeGreaterThan(200);
        g.Should().BeLessThan(60);
        b.Should().BeLessThan(60);
    }

    [Fact]
    public void Difference_OverPaint_Blends()
    {
        PixelBuffer px = Render(Content);

        (byte b, byte g, byte r, byte _) = px.GetPixelBgra(175, 250);
        r.Should().BeLessThan(60);
        g.Should().BeGreaterThan(200);
        b.Should().BeLessThan(60);
    }

    [Fact]
    public void UnpaintedArea_StaysWhite()
    {
        PixelBuffer px = Render(Content);

        (byte b, byte g, byte r, byte a) = px.GetPixelBgra(350, 50);
        r.Should().Be(255);
        g.Should().Be(255);
        b.Should().Be(255);
        a.Should().Be(255);
    }

    private static string Stream(string data) =>
        "<</Length " + data.Length.ToString(CultureInfo.InvariantCulture) + ">>\nstream\n" + data + "\nendstream";

    private static PixelBuffer Render(string content)
    {
        List<string> bodies = new List<string>
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 400 400]/Resources<</ExtGState<</GS1<</BM/Difference>>>>>>/Contents 4 0 R>>",
            Stream(content),
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
