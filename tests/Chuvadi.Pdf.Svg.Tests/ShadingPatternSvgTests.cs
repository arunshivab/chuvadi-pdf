// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.7.3.3 — Shading patterns (PatternType 2)
// PHASE: 3.18 — shading-pattern fills in the SVG pipeline.
//
// A path filled with a shading pattern paints the pattern's gradient clipped
// to the path. Before 3.18 the scn pattern name left the previous flat colour
// in place. (Tiling patterns in the SVG pipeline remain deferred.)

using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Svg.Tests;

public sealed class ShadingPatternSvgTests
{
    [Fact]
    public void RenderPage_ShadingPatternFill_EmitsClippedGradient()
    {
        byte[] pdf = BuildShadingPatternPdf();
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);

        string svg = new SvgRenderer().RenderPage(doc, 0);

        svg.Should().Contain("Gradient",
            "the pattern's shading renders through the gradient infrastructure");
        svg.Should().Contain("clip-path",
            "the gradient is confined to the filled path");
    }

    [Theory]
    [InlineData("2 0 0 0.5 0 0", true)]
    [InlineData("1 0.5 0 1 0 0", true)]
    [InlineData("0 2 -2 0 300 0", false)]
    public void RenderPage_RadialShadingUnderUnequalScaling_UsesAGradientTransform(string cm, bool expected)
    {
        // Unequal scaling or skew turns the circles into ellipses: the SVG
        // gradient keeps shading-space circles under a gradientTransform.
        byte[] content = Encoding.Latin1.GetBytes("q " + cm + " cm /Sh0 sh Q\n");
        List<byte[]> bodies = new List<byte[]>
        {
            Encoding.Latin1.GetBytes("<</Type/Catalog/Pages 2 0 R>>"),
            Encoding.Latin1.GetBytes("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Encoding.Latin1.GetBytes(
                "<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]"
                + "/Resources<</Shading<</Sh0 5 0 R>>>>/Contents 4 0 R>>"),
            Concat(
                Encoding.Latin1.GetBytes("<</Length " + content.Length + ">>\nstream\n"),
                content,
                Encoding.Latin1.GetBytes("\nendstream")),
            Encoding.Latin1.GetBytes(
                "<</ShadingType 3/ColorSpace/DeviceRGB/Coords[0 0 0 0 0 100]/Extend[true true]"
                + "/Function<</FunctionType 2/Domain[0 1]/C0[1 0 0]/C1[0 0 1]/N 1>>>>"),
        };
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(Assemble(bodies)), leaveOpen: false);

        string svg = new SvgRenderer().RenderPage(doc, 0);

        svg.Should().Contain("radialGradient");
        svg.Contains("gradientTransform", System.StringComparison.Ordinal).Should().Be(expected);
    }

    private static byte[] BuildShadingPatternPdf()
    {
        byte[] content = Encoding.Latin1.GetBytes(
            "/Pattern cs /P0 scn\n100 600 200 100 re f\n");

        List<byte[]> bodies = new List<byte[]>
        {
            Encoding.Latin1.GetBytes("<</Type/Catalog/Pages 2 0 R>>"),
            Encoding.Latin1.GetBytes("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Encoding.Latin1.GetBytes(
                "<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]"
                + "/Resources<</Pattern<</P0 5 0 R>>>>/Contents 4 0 R>>"),
            Concat(
                Encoding.Latin1.GetBytes("<</Length " + content.Length + ">>\nstream\n"),
                content,
                Encoding.Latin1.GetBytes("\nendstream")),
            Encoding.Latin1.GetBytes(
                "<</PatternType 2/Shading<</ShadingType 2/ColorSpace/DeviceRGB"
                + "/Coords[100 600 300 600]"
                + "/Function<</FunctionType 2/Domain[0 1]/C0[1 0 0]/C1[0 0 1]/N 1>>>>>>"),
        };

        return Assemble(bodies);
    }

    private static byte[] Assemble(List<byte[]> bodies)
    {
        using MemoryStream ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.Latin1.GetBytes(s));

        Write("%PDF-1.7\n");
        List<long> offsets = new List<long>();
        for (int i = 0; i < bodies.Count; i++)
        {
            offsets.Add(ms.Length);
            Write((i + 1) + " 0 obj ");
            ms.Write(bodies[i]);
            Write(" endobj\n");
        }

        long xref = ms.Length;
        Write("xref\n0 " + (bodies.Count + 1) + "\n0000000000 65535 f \n");
        foreach (long off in offsets)
        {
            Write(off.ToString("D10", System.Globalization.CultureInfo.InvariantCulture)
                + " 00000 n \n");
        }

        Write("trailer <</Size " + (bodies.Count + 1)
            + "/Root 1 0 R>>\nstartxref\n" + xref + "\n%%EOF");
        return ms.ToArray();
    }

    private static byte[] Concat(params byte[][] parts)
    {
        using MemoryStream ms = new MemoryStream();
        foreach (byte[] part in parts)
        {
            ms.Write(part);
        }

        return ms.ToArray();
    }
}
