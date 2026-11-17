// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.3.6 — text rendering mode; §8.7.3 — patterns
// PHASE: 3.35.0 (D-384 G2) — text painted with a fill or stroke pattern.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.Tests;

public sealed class PatternText3350Tests
{
    // A solid red tiling pattern (object 5) and a non-embedded Helvetica (6).
    private const string Resources = "/Font<</F1 6 0 R>>/Pattern<</P1 5 0 R>>";

    private static readonly string[] Extra =
    {
        "<</Type/Pattern/PatternType 1/PaintType 1/TilingType 1/BBox[0 0 10 10]/XStep 10/YStep 10/Resources<<>>/Length 23>>\nstream\n1 0 0 rg 0 0 10 10 re f\nendstream",
        "<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>",
    };

    // pdf.js pattern_text_non_embedded_font.pdf: before, glyphs filled with a
    // pattern drew nothing and glyphs stroked with one were filled black.
    [Fact]
    public void FillPattern_PaintsTheGlyphs()
    {
        PixelBuffer px = Render("BT /Pattern cs /P1 scn /F1 120 Tf 20 150 Td (WWW) Tj ET");

        (int red, int dark) = Count(px);
        red.Should().BeGreaterThan(500);
        dark.Should().Be(0);
    }

    [Fact]
    public void StrokePattern_StrokesTheGlyphs()
    {
        PixelBuffer px = Render("q /Pattern CS /P1 SCN 3 w 1 Tr BT /F1 120 Tf 20 150 Td (WWW) Tj ET Q");

        (int red, int dark) = Count(px);
        red.Should().BeGreaterThan(200);
        dark.Should().Be(0);
    }

    // A pattern whose cell shows a Type 3 glyph that fills text with the
    // same pattern (pdf.js ContentStreamCycleType3insideType3.pdf): the cell
    // is not painted inside itself, so the page finishes. Before, each level
    // repainted the cell for every glyph until the depth limit, and the page
    // never finished.
    [Fact]
    public async Task PatternCellPaintingItself_Finishes()
    {
        // 5: Type 3 font A (glyph "a" shows font B, glyph "b" a triangle);
        // 7: font B (glyph "c" shows Helvetica text filled with pattern P1);
        // 9: P1, whose cell shows font A again; 10: Helvetica.
        string[] extra =
        {
            "<</Type/Font/Subtype/Type3/FontBBox[0 0 750 750]/FontMatrix[0.01 0 0 0.01 0 0]/CharProcs<</rect 6 0 R/tri 11 0 R>>/Encoding<</Differences[97/rect/tri]>>/FirstChar 97/LastChar 98/Widths[1000 1000]/Resources<</Font<</B 7 0 R>>>>>>",
            Stream("1000 0 d0 20 w 1 0 0 RG 0 0 750 750 re s BT /B 50 Tf (ccc) Tj ET"),
            "<</Type/Font/Subtype/Type3/FontBBox[0 0 750 750]/FontMatrix[0.004 0 0 0.004 0 0]/CharProcs<</in 8 0 R>>/Encoding<</Differences[99/in]>>/FirstChar 99/LastChar 99/Widths[900]/Resources<</Font<</F1 10 0 R>>/Pattern<</P1 9 0 R>>>>>>",
            Stream("900 0 d0 2 Tr 10 w 0 0 1 RG /Pattern cs /P1 scn BT /F1 60 Tf 30 0 0 30 15 15 Tm -3 Tc (ab) Tj ET"),
            Stream("BT /A 4 Tf 10 0 0 10 1 1 Tm 1 0 1 rg (ba) Tj ET", "/Type/Pattern/PatternType 1/PaintType 1/TilingType 2/BBox[0 0 60 60]/Matrix[0.9 0 0 0.9 0 0]/XStep 55/YStep 32/Resources<</Font<</A 5 0 R>>>>"),
            "<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>",
            Stream("1000 0 d0 20 w 0 1 0 RG 0 0 m 375 750 l 750 0 l s"),
        };

        Task<PixelBuffer> render = Task.Run(() => Render("BT 1 0 0 1 100 300 Tm 2 Tr /A 20 Tf 20 20 Td 50 Tw (ab) Tj ET", extra, "/Font<</A 5 0 R>>"));
        PixelBuffer px = await render.WaitAsync(TimeSpan.FromSeconds(60));

        px.Width.Should().Be(400);
    }

    private static (int Red, int Dark) Count(PixelBuffer px)
    {
        int red = 0;
        int dark = 0;
        for (int y = 0; y < px.Height; y++)
        {
            for (int x = 0; x < px.Width; x++)
            {
                (byte b, byte g, byte r, byte _) = px.GetPixelBgra(x, y);
                if (r > 200 && g < 60 && b < 60)
                {
                    red++;
                }
                else if (r < 60 && g < 60 && b < 60)
                {
                    dark++;
                }
            }
        }

        return (red, dark);
    }

    private static string Stream(string data, string entries = "") =>
        "<<" + entries + "/Length " + data.Length.ToString(CultureInfo.InvariantCulture) + ">>\nstream\n" + data + "\nendstream";

    private static PixelBuffer Render(string content) => Render(content, Extra, Resources);

    private static PixelBuffer Render(string content, string[] extra, string resources)
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
