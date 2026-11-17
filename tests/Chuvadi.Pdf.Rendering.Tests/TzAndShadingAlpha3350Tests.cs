// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.4.4 (text rendering matrix), §11.6.4.4 (constant alpha)
// PHASE: 3.35.0 (D-384) — horizontal scaling widens the glyph shapes, and a
//        shading is painted with the constant fill alpha.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.Tests;

public sealed class TzAndShadingAlpha3350Tests
{
    private const string Helvetica = "/Font<</F1<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>>>";

    private const string Shading =
        "/ExtGState<</H<</ca 0.5>>>>/Shading<</S<</ShadingType 2/ColorSpace/DeviceRGB/Coords[0 0 400 0]/Function<</FunctionType 2/Domain[0 1]/C0[0 0 1]/C1[0 0 1]/N 1>>>>>>";

    // A 200 pt "I" (stem about 18 pixels wide at 100 Tz) drawn at 200 Tz is
    // twice as wide; before, only the advance was scaled.
    [Fact]
    public void HorizontalScaling_WidensTheGlyph()
    {
        int normal = InkedWidth(Render("BT /F1 200 Tf 100 100 Td (I) Tj ET", Helvetica));
        int wide = InkedWidth(Render("BT /F1 200 Tf 200 Tz 100 100 Td (I) Tj ET", Helvetica));

        normal.Should().BeInRange(12, 40);
        wide.Should().BeInRange((2 * normal) - 3, (2 * normal) + 3);
    }

    [Fact]
    public void Shading_HonoursConstantFillAlpha()
    {
        PixelBuffer px = Render("/H gs /S sh", Shading);

        (byte b, byte g, byte r, byte _) = px.GetPixelBgra(200, 200);
        b.Should().BeGreaterThan(240);
        r.Should().BeInRange(110, 145, "half-opaque blue over white");
        g.Should().BeInRange(110, 145);
    }

    [Fact]
    public void Shading_WithoutAlpha_IsOpaque()
    {
        PixelBuffer px = Render("/S sh", Shading);

        (byte b, byte g, byte r, byte _) = px.GetPixelBgra(200, 200);
        b.Should().BeGreaterThan(240);
        r.Should().BeLessThan(15);
        g.Should().BeLessThan(15);
    }

    // The ink of row 230 (through the stem), first to last dark pixel.
    private static int InkedWidth(PixelBuffer px)
    {
        int first = -1;
        int last = -1;
        for (int x = 0; x < px.Width; x++)
        {
            (byte b, byte g, byte r, byte _) = px.GetPixelBgra(x, 230);
            if (r < 128 && g < 128 && b < 128)
            {
                if (first < 0)
                {
                    first = x;
                }

                last = x;
            }
        }

        return first < 0 ? 0 : last - first + 1;
    }

    private static string Stream(string data) =>
        "<</Length " + data.Length.ToString(CultureInfo.InvariantCulture) + ">>\nstream\n" + data + "\nendstream";

    private static PixelBuffer Render(string content, string resources)
    {
        List<string> bodies = new List<string>
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 400 400]/Resources<<" + resources + ">>/Contents 4 0 R>>",
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
