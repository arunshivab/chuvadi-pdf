// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// Tests for v3.19: tiling patterns (PatternType 1) in the SVG pipeline.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Chuvadi.Pdf.Documents;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Svg.Tests;

public sealed class TilingPatternSvgTests
{
    // A 20x20 cell with a 10x10 red square in its corner.
    private const string RedCell = "1 0 0 rg 0 0 10 10 re f";

    [Fact]
    public void ColouredTilingFill_EmitsCellOnceAndOneUsePerTile()
    {
        // Fill (0,0)-(100,100) with a 20x20-step pattern: 5x5 = 25 tiles, plus
        // the boundary tiles the range check includes.
        string svg = Render(
            "/Pattern cs /P0 scn 0 0 100 100 re f",
            PatternObject(paintType: 1, cell: RedCell));

        Regex.Matches(svg, "<g id=\"tcell").Count.Should().Be(1, "the cell is defined once");
        Regex.Matches(svg, "<use xlink:href=\"#tcell0\"").Count.Should().BeGreaterThanOrEqualTo(25);
        svg.Should().Contain("fill=\"rgb(255,0,0)\"", "the cell's own red paints");
        svg.Should().Contain("clip-path=", "tiles are confined to the filled path");
    }

    [Fact]
    public void UncolouredTilingFill_PaintsInTheScnColour()
    {
        string svg = Render(
            "/CS0 cs 0 0 1 /P0 scn 0 0 100 100 re f",
            PatternObject(paintType: 2, cell: RedCell),
            resources: "/ColorSpace<</CS0[/Pattern/DeviceRGB]>>");

        svg.Should().Contain("fill=\"rgb(0,0,255)\"", "an uncoloured cell paints in the colour given with scn");
        svg.Should().NotContain("rgb(255,0,0)", "the cell's own colour operators are ignored");
    }

    [Fact]
    public void PatternMatrix_PlacesTilesInPageSpace()
    {
        // Pattern matrix translates by (5, 7): the first tile's transform
        // carries that offset.
        string svg = Render(
            "/Pattern cs /P0 scn 0 0 30 30 re f",
            PatternObject(paintType: 1, cell: RedCell, matrix: "[1 0 0 1 5 7]"));

        svg.Should().Contain("transform=\"matrix(1 0 0 1 5 7)\"", "tile (0,0) sits at the pattern origin");
        svg.Should().Contain("transform=\"matrix(1 0 0 1 25 27)\"", "tile (1,1) is one step further along both axes");
    }

    [Fact]
    public void SelfReferencingPattern_Terminates()
    {
        // The cell paints with its own pattern; the depth guard stops it.
        string svg = Render(
            "/Pattern cs /P0 scn 0 0 40 40 re f",
            PatternObject(paintType: 1, cell: "/Pattern cs /P0 scn 0 0 10 10 re f",
                cellResources: "/Pattern<</P0 5 0 R>>"));

        svg.Should().StartWith("<svg");
    }

    [Fact]
    public void StrokeOnlyWithTilingFill_StillStrokesNormally()
    {
        string svg = Render(
            "/Pattern cs /P0 scn 0 0 1 RG 4 w 10 10 m 90 90 l S",
            PatternObject(paintType: 1, cell: RedCell));

        svg.Should().Contain("stroke=\"rgb(0,0,255)\"", "a fill pattern does not affect strokes");
        svg.Should().NotContain("<use", "nothing was filled");
    }

    private static byte[] PatternObject(
        int paintType, string cell, string matrix = "[1 0 0 1 0 0]", string cellResources = "")
    {
        byte[] content = Encoding.Latin1.GetBytes(cell);
        string dict = "<</Type/Pattern/PatternType 1/PaintType " + paintType.ToString(CultureInfo.InvariantCulture)
            + "/TilingType 1/BBox[0 0 20 20]/XStep 20/YStep 20/Matrix " + matrix
            + "/Resources<<" + cellResources + ">>/Length " + content.Length.ToString(CultureInfo.InvariantCulture)
            + ">>\nstream\n";
        return Concat(Encoding.Latin1.GetBytes(dict), content, Encoding.Latin1.GetBytes("\nendstream"));
    }

    private static string Render(string pageContent, byte[] pattern, string resources = "")
    {
        byte[] content = Encoding.Latin1.GetBytes(pageContent);
        List<byte[]> bodies = new List<byte[]>
        {
            Encoding.Latin1.GetBytes("<</Type/Catalog/Pages 2 0 R>>"),
            Encoding.Latin1.GetBytes("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Encoding.Latin1.GetBytes(
                "<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]/Resources<</Pattern<</P0 5 0 R>>"
                + resources + ">>/Contents 4 0 R>>"),
            Concat(
                Encoding.Latin1.GetBytes("<</Length " + content.Length.ToString(CultureInfo.InvariantCulture) + ">>\nstream\n"),
                content,
                Encoding.Latin1.GetBytes("\nendstream")),
            pattern,
        };

        using MemoryStream ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.Latin1.GetBytes(s));
        Write("%PDF-1.7\n");
        List<long> offsets = new List<long>();
        for (int i = 0; i < bodies.Count; i++)
        {
            offsets.Add(ms.Length);
            Write((i + 1).ToString(CultureInfo.InvariantCulture) + " 0 obj\n");
            ms.Write(bodies[i]);
            Write("\nendobj\n");
        }

        long xref = ms.Length;
        Write("xref\n0 " + (bodies.Count + 1).ToString(CultureInfo.InvariantCulture) + "\n0000000000 65535 f \n");
        foreach (long off in offsets)
        {
            Write(off.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        }

        Write("trailer <</Size " + (bodies.Count + 1).ToString(CultureInfo.InvariantCulture)
            + "/Root 1 0 R>>\nstartxref\n" + xref.ToString(CultureInfo.InvariantCulture) + "\n%%EOF");
        ms.Position = 0;

        using PdfDocument document = PdfDocument.Open(ms, leaveOpen: true);
        return new SvgRenderer(new SvgExportOptions { Background = null }).RenderPage(document, 0);
    }

    private static byte[] Concat(params byte[][] parts)
    {
        using MemoryStream ms = new MemoryStream();
        foreach (byte[] p in parts)
        {
            ms.Write(p);
        }

        return ms.ToArray();
    }
}
