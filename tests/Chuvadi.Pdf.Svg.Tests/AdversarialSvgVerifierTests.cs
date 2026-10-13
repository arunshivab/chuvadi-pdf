// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// VERIFIER: adversarial tests for the v3.18 SVG same-style path batching and
// shading-pattern fills.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Chuvadi.Pdf.Documents;
using Xunit;

namespace Chuvadi.Pdf.Svg.Tests;

public sealed class AdversarialSvgVerifierTests
{
    // Two overlapping triangles with OPPOSITE winding directions, same opaque
    // fill, nonzero rule. Painted separately both are solid; merged into one
    // <path> under nonzero the overlap winds +1-1 = 0 and becomes a HOLE.
    [Fact]
    public void Batching_OppositeWindingOverlappingFills_MustNotMerge()
    {
        // CCW triangle then CW triangle, overlapping, both default black fill.
        byte[] pdf = BuildPdf(
            "100 100 m 300 100 l 200 300 l h f\n"
            + "150 150 m 350 150 l 250 350 l h f\n");
        // Reverse the second so it is CW: (150,150)->(250,350)->(350,150).
        pdf = BuildPdf(
            "100 100 m 300 100 l 200 300 l h f\n"
            + "150 150 m 250 350 l 350 150 l h f\n");

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        string svg = new SvgRenderer().RenderPage(doc, 0);

        // Count <path> elements whose d contains two subpaths (two 'M').
        MatchCollection paths = Regex.Matches(svg, "<path d=\"([^\"]*)\"");
        bool merged = false;
        foreach (Match m in paths)
        {
            string d = m.Groups[1].Value;
            int moveCount = d.Split('M').Length - 1;
            if (moveCount >= 2 && d.Contains("100") && d.Contains("350"))
            {
                merged = true;
            }
        }

        Assert.False(merged,
            "two overlapping nonzero fills with opposite winding were merged "
            + "into one <path>; under fill-rule nonzero the overlap cancels to "
            + "winding 0 and renders as a hole that separate elements would paint");
    }

    // A shading-pattern fill under a non-identity CTM: the clip path is baked
    // to page space by the builder, but it is then wrapped in a TransformOp
    // group carrying the same CTM, applying the transform twice.
    [Fact]
    public void ShadingPatternFill_UnderScaledCtm_DumpStructure()
    {
        byte[] pdf = BuildShadingPatternPdf(
            "q 0.5 0 0 0.5 0 0 cm /Pattern cs /P0 scn\n100 600 200 100 re f\nQ\n");
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        string svg = new SvgRenderer().RenderPage(doc, 0);

        // The rectangle in page space is (50,300)-(150,350) (y-flip aside).
        // If the clip path coordinates (already in page space) additionally
        // sit inside a <g transform="matrix(0.5,...)"> group, the transform is
        // applied twice and the gradient paints at the wrong place/scale.
        bool hasScaledGroup = svg.Contains("matrix(0.5", StringComparison.Ordinal);
        Match clip = Regex.Match(svg, "<clipPath[^>]*>\\s*<path d=\"([^\"]*)\"");
        string clipData = clip.Success ? clip.Groups[1].Value : "(none)";

        Assert.False(hasScaledGroup && clipData.Contains("M 50", StringComparison.Ordinal),
            $"double transform: page-space clip '{clipData}' is nested inside a "
            + "matrix(0.5) group, so the CTM is applied twice");
    }

    private static byte[] BuildPdf(string content)
    {
        List<byte[]> bodies = new List<byte[]>
        {
            Encoding.Latin1.GetBytes("<</Type/Catalog/Pages 2 0 R>>"),
            Encoding.Latin1.GetBytes("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Encoding.Latin1.GetBytes(
                "<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]"
                + "/Resources<<>>/Contents 4 0 R>>"),
            StreamObj(content),
        };

        return Assemble(bodies);
    }

    private static byte[] BuildShadingPatternPdf(string content)
    {
        List<byte[]> bodies = new List<byte[]>
        {
            Encoding.Latin1.GetBytes("<</Type/Catalog/Pages 2 0 R>>"),
            Encoding.Latin1.GetBytes("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Encoding.Latin1.GetBytes(
                "<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]"
                + "/Resources<</Pattern<</P0 5 0 R>>>>/Contents 4 0 R>>"),
            StreamObj(content),
            Encoding.Latin1.GetBytes(
                "<</PatternType 2/Shading<</ShadingType 2/ColorSpace/DeviceRGB"
                + "/Coords[100 600 300 600]"
                + "/Function<</FunctionType 2/Domain[0 1]/C0[1 0 0]/C1[0 0 1]/N 1>>>>>>"),
        };

        return Assemble(bodies);
    }

    private static byte[] StreamObj(string content)
    {
        byte[] bytes = Encoding.Latin1.GetBytes(content);
        using MemoryStream ms = new MemoryStream();
        ms.Write(Encoding.Latin1.GetBytes("<</Length " + bytes.Length + ">>\nstream\n"));
        ms.Write(bytes);
        ms.Write(Encoding.Latin1.GetBytes("\nendstream"));
        return ms.ToArray();
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
}
