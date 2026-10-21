// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.7.3 — Patterns used as the stroke colour
// PHASE: 3.22 — a stroke whose colour is a pattern (SCN /Name) paints the
//        pattern through the stroke outline (an SVG mask of the stroked
//        path) instead of falling back to a flat colour.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Rendering.DisplayList;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Svg.Tests;

public sealed class PatternStrokeSvgTests
{
    private const string ShadingPattern =
        "<</PatternType 2/Shading<</ShadingType 2/ColorSpace/DeviceRGB/Coords[100 200 300 200]"
        + "/Function<</FunctionType 2/Domain[0 1]/C0[1 0 0]/C1[0 0 1]/N 1>>>>>>";

    private const string TilingPattern =
        "<</PatternType 1/PaintType 1/TilingType 1/BBox[0 0 40 40]/XStep 40/YStep 40/Resources<<>>/Length 23>>\n"
        + "stream\n0 1 0 rg 0 0 20 40 re f\nendstream";

    [Fact]
    public void ShadingPatternStroke_PaintsTheGradientThroughAStrokeMask()
    {
        using PdfDocument doc = Open(Pdf("/Pattern CS /P0 SCN 8 w 1 J 100 200 m 300 200 l S", ShadingPattern));

        string svg = new SvgRenderer().RenderPage(doc, 0);

        svg.Should().Contain("<mask id=\"smask", "the stroke outline becomes a mask");
        svg.Should().Contain("stroke=\"#fff\" stroke-width=\"8\"");
        svg.Should().Contain("stroke-linecap=\"round\"");
        svg.Should().Contain("mask=\"url(#smask");
        svg.Should().Contain("Gradient", "the shading is painted through the mask");
    }

    [Fact]
    public void TilingPatternStroke_RepeatsTheCellThroughAStrokeMask()
    {
        using PdfDocument doc = Open(Pdf("/Pattern CS /P0 SCN 10 w 50 50 m 350 50 l S", TilingPattern));

        string svg = new SvgRenderer().RenderPage(doc, 0);

        svg.Should().Contain("<mask id=\"smask");
        svg.Should().Contain("<use ", "the cell is replayed under the mask");
    }

    [Fact]
    public void PatternStroke_RecordsAStrokeOutlineClip()
    {
        using PdfDocument doc = Open(Pdf("/Pattern CS /P0 SCN 8 w 100 200 m 300 200 l S", ShadingPattern));

        PageDisplayList list = DisplayListBuilder.Build(doc, 0);

        List<ClipOp> clips = new List<ClipOp>();
        foreach (RenderOp op in list)
        {
            if (op is ClipOp clip)
            {
                clips.Add(clip);
            }
        }

        clips.Should().ContainSingle();
        clips[0].Stroke.Should().NotBeNull();
        clips[0].Stroke!.LineWidth.Should().Be(8);
        list.Should().NotContain(op => op is PathOp, "the stroke is painted by the pattern, not a flat colour");
        clips[0].Bounds().Height.Should().BeGreaterThan(8, "the clip bounds grow by the stroke's reach");
    }

    [Fact]
    public void FlatColourAfterAPattern_StrokesFlatAgain()
    {
        using PdfDocument doc = Open(Pdf(
            "/Pattern CS /P0 SCN 1 0 0 RG 8 w 100 200 m 300 200 l S", ShadingPattern));

        string svg = new SvgRenderer().RenderPage(doc, 0);

        svg.Should().NotContain("<mask");
    }

    [Fact]
    public void PatternFillWithFlatStroke_IsUnchanged()
    {
        using PdfDocument doc = Open(Pdf(
            "/Pattern cs /P0 scn 0 0 1 RG 8 w 100 100 200 200 re B", ShadingPattern));

        string svg = new SvgRenderer().RenderPage(doc, 0);

        svg.Should().NotContain("<mask");
        svg.Should().Contain("clip-path", "the fill is clipped to the path");
        svg.Should().Contain("rgb(0,0,255)", "the outline is still stroked flat");
    }

    private static byte[] Pdf(string content, string pattern)
    {
        List<string> bodies = new List<string>
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 400 400]/Resources<</Pattern<</P0 5 0 R>>>>/Contents 4 0 R>>",
            "<</Length " + Encoding.Latin1.GetByteCount(content).ToString(CultureInfo.InvariantCulture)
                + ">>\nstream\n" + content + "\nendstream",
            pattern,
        };

        StringBuilder sb = new StringBuilder("%PDF-1.7\n");
        List<int> offsets = new List<int>();
        for (int i = 0; i < bodies.Count; i++)
        {
            offsets.Add(sb.Length);
            sb.Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append(" 0 obj\n").Append(bodies[i]).Append("\nendobj\n");
        }

        int xref = sb.Length;
        sb.Append("xref\n0 6\n0000000000 65535 f \n");
        foreach (int off in offsets)
        {
            sb.Append(off.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        sb.Append("trailer <</Size 6/Root 1 0 R>>\nstartxref\n").Append(xref.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF");
        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    private static PdfDocument Open(byte[] pdf) => PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
}
