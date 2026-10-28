// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.7.4.5.4 — Radial (type 3) shadings
// PHASE: 3.23 — radial shadings under unequal scaling or skew
//
// The circles of a radial shading live in shading space; a CTM that scales x
// and y differently (or skews) turns them into ellipses on the page. Before
// 3.23 the raster builder mapped the centres and scaled both radii by one
// factor, so such a shading painted the wrong circle at most pixels.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Rendering.Raster;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.Tests;

public sealed class RadialShadingTransformTests
{
    [Fact]
    public void RadialShadingUnderUnequalScaling_PaintsTheEllipseItMapsTo()
    {
        // Circles about the origin, radius 0 to 100 (red to blue), drawn
        // under x*2, y*0.5. Page point (100, 25) is shading point (50, 50):
        // s = 70.7 / 100, so red is about 255 * (1 - 0.707) = 75. Treating
        // the circles as page-space circles of radius 200 gave s = 0.52
        // (red 124).
        using PdfDocument doc = Open(Radial("2 0 0 0.5 0 0 cm"));
        PixelBuffer pixels = new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72 }).Rasterize(doc.Pages[0]);

        (byte b, byte _, byte r, byte _) = pixels.GetPixelBgra(100, 75);
        ((int)r).Should().BeInRange(60, 90);
        ((int)b).Should().BeInRange(165, 195);
    }

    [Fact]
    public void RadialShadingUnderUnequalScaling_KeepsShadingSpaceGeometryAndTheTransform()
    {
        using PdfDocument doc = Open(Radial("2 0 0 0.5 0 0 cm"));
        PageDisplayList list = DisplayListBuilder.Build(doc.Pages[0], doc.Objects);

        ShadeOp op = list.Ops.OfType<ShadeOp>().Single();
        op.ShadingToPage.Should().NotBeNull();
        op.R1.Should().BeApproximately(100, 1e-9);
    }

    [Fact]
    public void RadialShadingUnderRotationAndUniformScale_StaysInPageSpace()
    {
        using PdfDocument doc = Open(Radial("0 2 -2 0 100 0 cm"));
        PageDisplayList list = DisplayListBuilder.Build(doc.Pages[0], doc.Objects);

        ShadeOp op = list.Ops.OfType<ShadeOp>().Single();
        op.ShadingToPage.Should().BeNull();
        op.R1.Should().BeApproximately(200, 1e-4);
    }

    private static PdfDocument Open(byte[] pdf) => PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);

    private static byte[] Radial(string cm)
    {
        List<string> bodies = new List<string>
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 100]/Resources<</Shading<</Sh0 5 0 R>>>>/Contents 4 0 R>>",
            null!,
            "<</ShadingType 3/ColorSpace/DeviceRGB/Coords[0 0 0 0 0 100]/Extend[true true]"
                + "/Function<</FunctionType 2/Domain[0 1]/C0[1 0 0]/C1[0 0 1]/N 1>>>>",
        };
        string content = "q " + cm + " /Sh0 sh Q";
        bodies[3] = "<</Length " + content.Length + ">>\nstream\n" + content + "\nendstream";

        StringBuilder sb = new StringBuilder("%PDF-1.7\n");
        List<int> offsets = new List<int>();
        for (int i = 0; i < bodies.Count; i++)
        {
            offsets.Add(sb.Length);
            sb.Append(i + 1).Append(" 0 obj\n").Append(bodies[i]).Append("\nendobj\n");
        }

        int xref = sb.Length;
        sb.Append("xref\n0 ").Append(bodies.Count + 1).Append("\n0000000000 65535 f \n");
        foreach (int off in offsets)
        {
            sb.Append(off.ToString("D10", System.Globalization.CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        sb.Append("trailer\n<</Size ").Append(bodies.Count + 1).Append("/Root 1 0 R>>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        return Encoding.Latin1.GetBytes(sb.ToString());
    }
}
