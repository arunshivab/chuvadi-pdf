// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.3.6 (text rendering mode), §8.5.4 (clipping)
// PHASE: 3.35.0 (D-384 G2, G3) — stroked text modes stroke the outline, and
//        clipping modes 4 to 7 clip at ET instead of painting.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.Tests;

public sealed class TextRenderModes3350Tests
{
    private const string Helvetica = "/Font<</F1<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>>>";

    // A 200 pt "I" at x 100: its stem runs about 118-145 (device x) over
    // device y 156-300 (page height 400).
    [Fact]
    public void ClipMode7_ClipsALaterFillToTheGlyphs()
    {
        PixelBuffer px = Render("BT /F1 200 Tf 7 Tr 100 100 Td (I) Tj ET 0 0 1 rg 0 0 400 400 re f", Helvetica);

        Blue(px, 131, 230).Should().BeTrue("inside the glyph the fill shows");
        White(px, 50, 230).Should().BeTrue("outside the glyph it is clipped away");
        White(px, 300, 50).Should().BeTrue();
    }

    [Fact]
    public void ClipMode7_PaintsNothingItself()
    {
        PixelBuffer px = Render("BT /F1 200 Tf 7 Tr 100 100 Td (I) Tj ET", Helvetica);

        White(px, 131, 230).Should().BeTrue("mode 7 adds to the clip and paints nothing");
    }

    [Fact]
    public void TheTextClip_EndsWithTheGraphicsState()
    {
        PixelBuffer px = Render("q BT /F1 200 Tf 7 Tr 100 100 Td (I) Tj ET Q 0 0 1 rg 0 0 400 400 re f", Helvetica);

        Blue(px, 50, 230).Should().BeTrue("Q restores the clip");
    }

    [Fact]
    public void StrokeMode1_StrokesTheOutline()
    {
        PixelBuffer px = Render("BT /F1 200 Tf 1 Tr 2 w 100 100 Td (I) Tj ET", Helvetica);

        Dark(px, 131, 230).Should().BeFalse("the middle of the stem is not filled");
        int edges = 0;
        for (int x = 105; x < 160; x++)
        {
            if (Dark(px, x, 230))
            {
                edges++;
            }
        }

        edges.Should().BeInRange(2, 8, "only the outline's two sides are inked");
    }

    [Fact]
    public void FillMode0_IsUnchanged()
    {
        PixelBuffer px = Render("BT /F1 200 Tf 100 100 Td (I) Tj ET", Helvetica);

        Dark(px, 131, 230).Should().BeTrue();
    }

    private static bool Blue(PixelBuffer px, int x, int y)
    {
        (byte b, byte g, byte r, byte _) = px.GetPixelBgra(x, y);
        return b > 200 && r < 60 && g < 60;
    }

    private static bool White(PixelBuffer px, int x, int y)
    {
        (byte b, byte g, byte r, byte _) = px.GetPixelBgra(x, y);
        return r > 240 && g > 240 && b > 240;
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
