// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.4.3.4 — line join style
// PHASE: 3.35.0 (backlog 54) — bevel joins and the mitre limit in the raster stroker.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.Tests;

public sealed class BevelJoinMitreLimit3350Tests
{
    // A 40-unit stroke right then up, joined at (300, 100) (device 300, 300).
    // The 90-degree join's mitre reaches 28.3 units out at 45 degrees; a
    // bevel cuts the corner at 14.1 units.
    [Fact]
    public void BevelJoin_CutsTheCorner()
    {
        PixelBuffer px = Render("40 w 2 j 100 100 m 300 100 l 300 300 l S");

        Dark(px, 308, 308).Should().BeTrue("inside the bevel");
        Dark(px, 312, 312).Should().BeFalse("outside the bevel, inside where a mitre would reach");
    }

    [Fact]
    public void MitreJoin_WithinTheLimit_KeepsTheCorner()
    {
        // A 90-degree join's mitre ratio is 1.414, within the default 10.
        PixelBuffer px = Render("40 w 0 j 100 100 m 300 100 l 300 300 l S");

        Dark(px, 318, 318).Should().BeTrue();
    }

    [Fact]
    public void MitreJoin_BeyondTheLimit_IsBevelled()
    {
        // /ML 1.2 is below the 90-degree join's ratio of 1.414.
        PixelBuffer px = Render("40 w 0 j 1.2 M 100 100 m 300 100 l 300 300 l S");

        Dark(px, 308, 308).Should().BeTrue("inside the bevel");
        Dark(px, 312, 312).Should().BeFalse("the mitre is cut to a bevel");
    }

    [Fact]
    public void SharpMitre_WithinAHighLimit_KeepsItsPoint()
    {
        // A 20-degree turn: ratio 1 / sin(10 degrees) = 5.76. The old fixed
        // limit cut it; the default /ML of 10 keeps the point.
        PixelBuffer sharp = Render("10 w 0 j 100 300 m 300 300 l 100 227 l S");
        PixelBuffer capped = Render("10 w 0 j 4 M 100 300 m 300 300 l 100 227 l S");

        Dark(sharp, 320, 96).Should().BeTrue("the mitre point reaches past the vertex");
        Dark(capped, 320, 96).Should().BeFalse("/ML 4 bevels it");
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
