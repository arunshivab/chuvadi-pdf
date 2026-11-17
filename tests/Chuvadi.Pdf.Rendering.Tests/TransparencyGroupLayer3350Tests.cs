// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §11.4 (transparency groups), §11.4.6 (knockout)
// PHASE: 3.35.0 (backlog 64, D-370) — transparency groups drawn off-screen
//        when their alpha, blend mode or soft mask covers more than one
//        object, and knockout groups.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.Tests;

public sealed class TransparencyGroupLayer3350Tests
{
    // Red 100..250 and blue 150..300 overlap in 150..250 (device y 150..250).
    private const string TwoRects = "1 0 0 rg 100 150 150 100 re f 0 0 1 rg 150 150 150 100 re f";

    [Fact]
    public void GroupAlpha_AppliesToTheGroupAsAWhole()
    {
        PixelBuffer px = Render("/GS0 gs /Fm0 Do", "/ExtGState<</GS0<</ca 0.5>>>>", Group(TwoRects, knockout: false));

        Near(px, 200, 200, 127, 127, 255).Should().BeTrue("the overlap shows only blue at half strength");
        Near(px, 120, 200, 255, 127, 127).Should().BeTrue("red alone at half strength");
    }

    [Fact]
    public void Knockout_EachObjectReplacesTheEarlierOnes()
    {
        PixelBuffer px = Render("/Fm0 Do", string.Empty, Group("/GA gs " + TwoRects, knockout: true, resources: "/ExtGState<</GA<</ca 0.5>>>>"));

        Near(px, 200, 200, 127, 127, 255).Should().BeTrue("blue knocks out red: half blue over white");
        Near(px, 120, 200, 255, 127, 127).Should().BeTrue();
    }

    [Fact]
    public void WithoutKnockout_ObjectsStack()
    {
        PixelBuffer px = Render("/Fm0 Do", string.Empty, Group("/GA gs " + TwoRects, knockout: false, resources: "/ExtGState<</GA<</ca 0.5>>>>"));

        Near(px, 200, 200, 127, 127, 255).Should().BeFalse("half blue over half red is not half blue over white");
    }

    // A non-isolated knockout group's objects blend with what is under the
    // group: cyan multiplied over yellow is green (pdf.js
    // knockout_blend_multiply, as Poppler and MuPDF draw it).
    [Fact]
    public void NonIsolatedKnockout_BlendsWithTheBackdrop()
    {
        PixelBuffer px = Render(
            "1 1 0 rg 0 0 400 400 re f /Fm0 Do",
            string.Empty,
            Group("/M gs 0 1 1 rg 100 100 200 200 re f 1 0 0 rg 350 350 20 20 re f", knockout: true, resources: "/ExtGState<</M<</BM/Multiply>>>>"));

        Near(px, 200, 200, 0, 255, 0).Should().BeTrue();
        Near(px, 50, 50, 0, 255, 0).Should().BeFalse("outside the group the backdrop is kept");
        Near(px, 50, 350, 255, 255, 0).Should().BeTrue();
    }

    // An isolated knockout group starts from transparency: the object's
    // Multiply has nothing to blend with inside the group.
    [Fact]
    public void IsolatedKnockout_StartsFromTransparency()
    {
        PixelBuffer px = Render(
            "1 1 0 rg 0 0 400 400 re f /Fm0 Do",
            string.Empty,
            Group("/M gs 0 1 1 rg 100 100 200 200 re f 1 0 0 rg 350 350 20 20 re f", knockout: true, resources: "/ExtGState<</M<</BM/Multiply>>>>", isolated: true));

        Near(px, 200, 200, 0, 255, 255).Should().BeTrue("cyan, composited Normal onto the page");
    }

    // Where the objects do not overlap, the layer and the per-object path
    // must agree.
    [Fact]
    public void LayerAndPerObjectPaths_AgreeWithoutOverlap()
    {
        const string apart = "1 0 0 rg 50 150 100 100 re f 0 0 1 rg 250 150 100 100 re f";
        PixelBuffer layered = Render("/GS0 gs /Fm0 Do", "/ExtGState<</GS0<</ca 0.5>>>>", Group(apart, knockout: false));
        PixelBuffer direct = Render("/GS0 gs " + apart, "/ExtGState<</GS0<</ca 0.5>>>>");

        foreach ((int x, int y) in new[] { (100, 200), (300, 200), (200, 200), (55, 155), (345, 245) })
        {
            (byte b1, byte g1, byte r1, byte _) = layered.GetPixelBgra(x, y);
            (byte b2, byte g2, byte r2, byte _) = direct.GetPixelBgra(x, y);
            Math.Abs(r1 - r2).Should().BeLessThanOrEqualTo(1);
            Math.Abs(g1 - g2).Should().BeLessThanOrEqualTo(1);
            Math.Abs(b1 - b2).Should().BeLessThanOrEqualTo(1);
        }
    }

    private static string Group(string content, bool knockout, string resources = "", bool isolated = false) =>
        "<</Type/XObject/Subtype/Form/BBox[0 0 400 400]/Group<</S/Transparency" + (knockout ? "/K true" : string.Empty) + (isolated ? "/I true" : string.Empty)
        + ">>/Resources<<" + resources + ">>/Length " + content.Length.ToString(CultureInfo.InvariantCulture)
        + ">>\nstream\n" + content + "\nendstream";

    private static bool Near(PixelBuffer px, int x, int y, int r, int g, int b)
    {
        (byte pb, byte pg, byte pr, byte _) = px.GetPixelBgra(x, y);
        return Math.Abs(pr - r) <= 3 && Math.Abs(pg - g) <= 3 && Math.Abs(pb - b) <= 3;
    }

    // A 400 x 400 pt page; the group, when given, is object 5 named /Fm0.
    private static PixelBuffer Render(string content, string resources, string? group = null)
    {
        List<string> bodies = new List<string>
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 400 400]/Resources<<" + resources
                + (group is null ? string.Empty : "/XObject<</Fm0 5 0 R>>") + ">>/Contents 4 0 R>>",
            "<</Length " + content.Length.ToString(CultureInfo.InvariantCulture) + ">>\nstream\n" + content + "\nendstream",
        };
        if (group is not null)
        {
            bodies.Add(group);
        }

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
