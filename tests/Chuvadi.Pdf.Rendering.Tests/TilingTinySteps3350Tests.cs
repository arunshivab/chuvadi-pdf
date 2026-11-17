// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.7.3.1 (tiling patterns)
// PHASE: 3.35.0 (backlog 56) — a tiling pattern whose step is thousands of
//        times smaller than its cell renders at once (pdf.js
//        tiling-pattern-tiny-steps), filled with the colour its content covers.

using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.Tests;

public sealed class TilingTinySteps3350Tests
{
    [Fact]
    public void ATinyStep_FillsTheRegionQuickly()
    {
        const string cell = "q 0 0 10 10 re f Q";
        string pattern = "<</Type/Pattern/PatternType 1/PaintType 1/TilingType 1/BBox[0 0 10 10]/XStep 0.001/YStep 0.001/Resources<<>>/Length "
            + cell.Length.ToString(CultureInfo.InvariantCulture) + ">>\nstream\n" + cell + "\nendstream";

        Stopwatch watch = Stopwatch.StartNew();
        PixelBuffer px = Render("q /Pattern cs /P1 scn 100 100 200 200 re f Q", "/Pattern<</P1 5 0 R>>", pattern);
        watch.Stop();

        watch.Elapsed.TotalSeconds.Should().BeLessThan(10, "the cell is not drawn millions of times");
        Dark(px, 200, 200).Should().BeTrue("the black cell covers every point of the dense lattice");
        Dark(px, 50, 50).Should().BeFalse("outside the filled rectangle");
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
