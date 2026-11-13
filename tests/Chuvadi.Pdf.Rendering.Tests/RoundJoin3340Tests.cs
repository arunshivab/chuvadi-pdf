// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.4.3.4 — line join style
// PHASE: 3.34.0 (#22, D-353) — round joins in the raster stroker.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.Tests;

public sealed class RoundJoin3340Tests
{
    // A 40-unit stroke right then up, joined at (300, 100) (device 300, 300).
    // A point 18 units out at 45 degrees is inside the round join but outside
    // a bevel; a point 25 units out is outside the round join but inside the
    // mitre.
    [Fact]
    public void RoundJoin_FollowsTheCircle()
    {
        PixelBuffer px = Render("40 w 1 j 100 100 m 300 100 l 300 300 l S");

        Dark(px, 312, 312).Should().BeTrue("inside the round join");
        Dark(px, 318, 318).Should().BeFalse("outside the round join, inside where a mitre would reach");
    }

    [Fact]
    public void MitreJoin_IsUnchanged()
    {
        PixelBuffer px = Render("40 w 0 j 100 100 m 300 100 l 300 300 l S");

        Dark(px, 318, 318).Should().BeTrue();
    }

    [Fact]
    public void RoundJoin_TurningTheOtherWay_FollowsTheCircle()
    {
        // Right then down: the outside of the turn is above and to the right.
        PixelBuffer px = Render("40 w 1 j 100 300 m 300 300 l 300 100 l S");

        Dark(px, 312, 88).Should().BeTrue();
        Dark(px, 318, 82).Should().BeFalse();
    }

    // A round join adds its arc to one side only, so the two sides differ in
    // length; a square cap at the end must still extend both (pdf.js
    // perf_paths_curves.pdf threw before this was fixed).
    [Fact]
    public void RoundJoin_WithSquareCap_ExtendsTheEnd()
    {
        PixelBuffer px = Render("40 w 1 j 2 J 100 100 m 300 100 l 300 300 l S");

        Dark(px, 300, 90).Should().BeTrue("inside the square cap");
        Dark(px, 300, 75).Should().BeFalse("past the square cap");
        Dark(px, 312, 312).Should().BeTrue("inside the round join");
    }

    private static bool Dark(PixelBuffer px, int x, int y)
    {
        (byte b, byte g, byte r, byte _) = px.GetPixelBgra(x, y);
        return r < 100 && g < 100 && b < 100;
    }

    private static string Stream(string data) =>
        "<</Length " + data.Length.ToString(CultureInfo.InvariantCulture) + ">>\nstream\n" + data + "\nendstream";

    // A 400 x 400 pt page with the content and resources; extra objects
    // are numbered from 5.
    private static PixelBuffer Render(string content, string resources = "", params string[] extra)
    {
        List<string> bodies = new List<string>
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 400 400]/Resources<<" + resources + ">>/Contents 4 0 R>>",
            Stream(content),
        };
        bodies.AddRange(extra);

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
