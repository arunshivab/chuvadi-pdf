// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §11.3.5.3 (non-separable blend modes)
// PHASE: 3.35.0 (C2, D-385) — Hue, Saturation, Color and Luminosity.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Rendering.DisplayList;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.Tests;

public sealed class NonSeparableBlend3350Tests
{
    [Theory]
    [InlineData("Hue", PdfBlendMode.Hue)]
    [InlineData("Saturation", PdfBlendMode.Saturation)]
    [InlineData("Color", PdfBlendMode.Color)]
    [InlineData("Luminosity", PdfBlendMode.Luminosity)]
    public void Names_MapToTheirModes(string name, PdfBlendMode mode)
    {
        BlendModes.FromName(name).Should().Be(mode);
        BlendModes.IsNonSeparable(mode).Should().BeTrue();
    }

    [Fact]
    public void Formulas_FollowTheSpecification()
    {
        (double R, double G, double B) red = (1, 0, 0);
        (double R, double G, double B) gray = (0.5, 0.5, 0.5);

        // Luminosity: the backdrop's hue and saturation with the source's
        // luminosity; a gray source over red gives a red of luminosity 0.5.
        (double r, double g, double b) = BlendModes.BlendRgb(PdfBlendMode.Luminosity, red, gray);
        ((0.3 * r) + (0.59 * g) + (0.11 * b)).Should().BeApproximately(0.5, 1e-9);
        r.Should().BeGreaterThan(g);

        // Color over gray keeps the gray's luminosity.
        (r, g, b) = BlendModes.BlendRgb(PdfBlendMode.Color, gray, red);
        ((0.3 * r) + (0.59 * g) + (0.11 * b)).Should().BeApproximately(0.5, 1e-9);

        // Saturation of a gray source makes the backdrop gray.
        (r, g, b) = BlendModes.BlendRgb(PdfBlendMode.Saturation, red, gray);
        r.Should().BeApproximately(g, 1e-9);
        g.Should().BeApproximately(b, 1e-9);

        // Hue with a gray backdrop stays gray (no saturation to give).
        (r, g, b) = BlendModes.BlendRgb(PdfBlendMode.Hue, gray, red);
        r.Should().BeApproximately(0.5, 1e-9);
        b.Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void Luminosity_IsDrawn()
    {
        // A red backdrop, then white drawn with Luminosity: the red's hue at
        // full luminosity is white; then black with Luminosity gives black.
        PixelBuffer px = Render("1 0 0 rg 0 0 400 400 re f /GS0 gs 0.5 g 0 0 200 400 re f", "/ExtGState<</GS0<</BM/Luminosity>>>>");

        (byte b, byte g, byte r, byte _) = px.GetPixelBgra(100, 200);
        double lum = ((0.3 * r) + (0.59 * g) + (0.11 * b)) / 255.0;
        lum.Should().BeApproximately(0.5, 0.02, "the luminosity is the source's");
        r.Should().BeGreaterThan(g, "the hue stays red (Normal would paint gray)");
        px.GetPixelBgra(300, 200).R.Should().Be(255);
    }

    private static PixelBuffer Render(string content, string resources)
    {
        List<string> bodies = new List<string>
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 400 400]/Resources<<" + resources + ">>/Contents 4 0 R>>",
            "<</Length " + content.Length.ToString(CultureInfo.InvariantCulture) + ">>\nstream\n" + content + "\nendstream",
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
