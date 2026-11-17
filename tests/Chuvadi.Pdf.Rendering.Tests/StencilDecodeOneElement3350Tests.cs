// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.9.6.2 (stencil masks), §8.9.5.2 (Decode)
// PHASE: 3.35.0 (52h) — a one-element /Decode on a stencil mask ([1], as
//        Poppler's mask.pdf writes) is read as [1 0], as Poppler and MuPDF do.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.Tests;

public sealed class StencilDecodeOneElement3350Tests
{
    // A 1 x 1 stencil whose only sample is 0, drawn over the whole page.
    private const string Draw = "0 0 1 rg 400 0 0 400 0 0 cm /M Do";
    private const string Resources = "/XObject<</M 5 0 R>>";

    [Fact]
    public void WithoutDecode_SampleZeroPaints()
    {
        PixelBuffer px = Render(Draw, Resources, Stencil(string.Empty));

        Dark(px, 200, 200).Should().BeFalse();
        px.GetPixelBgra(200, 200).B.Should().BeGreaterThan(200, "the fill colour (blue) is painted");
    }

    [Fact]
    public void AOneElementDecode_InvertsTheMask()
    {
        PixelBuffer px = Render(Draw, Resources, Stencil("/Decode[1]"));

        (byte b, byte g, byte r, byte _) = px.GetPixelBgra(200, 200);
        (r > 240 && g > 240 && b > 240).Should().BeTrue("[1] reads as [1 0], so sample 0 paints nothing");
    }

    private static string Stencil(string decode) =>
        "<</Type/XObject/Subtype/Image/Width 1/Height 1/ImageMask true/BitsPerComponent 1" + decode
        + "/Length 1>>\nstream\n\u0000\nendstream";

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
