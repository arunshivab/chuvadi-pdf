// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.7.3 — Patterns (tiling PatternType 1, shading
//        PatternType 2)
// PHASE: 3.18 — pattern fills in the raster pipeline.
//
// Before 3.18 a `scn /Name` marked the fill colour invalid and pattern-filled
// regions were suppressed entirely. A shading pattern now fills the path with
// its gradient (a ShadeOp clipped to the path); a tiling pattern replays its
// cell across the filled region (NestedDisplayListOps clipped to the path).

using System.Linq;
using System.Text;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Rendering.Raster;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.Tests;

public sealed class RasterPatternTests
{
    private static PdfDictionary AxialShading()
    {
        PdfDictionary function = new();
        function.Set(PdfName.Intern("FunctionType"), 2);
        function.Set(PdfName.Intern("Domain"), new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(1),
        }));
        function.Set(PdfName.Intern("C0"), new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(1), new PdfInteger(0), new PdfInteger(0),
        }));
        function.Set(PdfName.Intern("C1"), new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(1),
        }));
        function.Set(PdfName.Intern("N"), 1);

        PdfDictionary shading = new();
        shading.Set(PdfName.Intern("ShadingType"), 2);
        shading.Set(PdfName.Intern("ColorSpace"), PdfName.Intern("DeviceRGB"));
        shading.Set(PdfName.Intern("Coords"), new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(200), new PdfInteger(0),
        }));
        shading.Set(PdfName.Intern("Function"), function);
        return shading;
    }

    private static PageDisplayList Build(string content, PdfPrimitive pattern)
    {
        PdfDictionary patterns = new();
        patterns.Set(PdfName.Intern("P0"), pattern);
        PdfDictionary resources = new();
        resources.Set(PdfName.Intern("Pattern"), patterns);

        PdfObjectStore store = new();
        return DisplayListBuilder.Build(
            Encoding.ASCII.GetBytes(content), resources, store, 612, 792);
    }

    [Fact]
    public void ShadingPatternFill_EmitsShadeOpClippedToThePath()
    {
        PdfDictionary pattern = new();
        pattern.Set(PdfName.Intern("PatternType"), 2);
        pattern.Set(PdfName.Intern("Shading"), AxialShading());

        PageDisplayList list = Build(
            "/Pattern cs /P0 scn 0 0 200 100 re f", pattern);

        ShadeOp[] shades = list.Ops.OfType<ShadeOp>().ToArray();
        shades.Should().HaveCount(1, "the pattern-filled path paints its gradient");
        shades[0].Clips.Should().NotBeNullOrEmpty(
            "the gradient is confined to the filled path");
        list.Ops.OfType<FillPathOp>().Should().BeEmpty(
            "no flat fill is painted for a pattern colour");
    }

    [Fact]
    public void TilingPatternFill_ReplaysTheCellAcrossTheRegion()
    {
        // A 10×10 cell with a red square, tiled over a 20×20 fill: 3×3 tile
        // placements cover the region (the edge tiles only touch it).
        PdfDictionary cellDict = new();
        cellDict.Set(PdfName.Intern("PatternType"), 1);
        cellDict.Set(PdfName.Intern("PaintType"), 1);
        cellDict.Set(PdfName.Intern("TilingType"), 1);
        cellDict.Set(PdfName.Intern("BBox"), new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(10), new PdfInteger(10),
        }));
        cellDict.Set(PdfName.Intern("XStep"), 10);
        cellDict.Set(PdfName.Intern("YStep"), 10);
        byte[] cellContent = Encoding.ASCII.GetBytes("1 0 0 rg 0 0 5 5 re f");
        cellDict.Set(PdfName.Intern("Length"), cellContent.Length);
        PdfStream cell = new(cellDict, cellContent);

        PageDisplayList list = Build("/Pattern cs /P0 scn 0 0 20 20 re f", cell);

        NestedDisplayListOp[] tiles = list.Ops.OfType<NestedDisplayListOp>().ToArray();
        tiles.Should().HaveCount(9, "a 20×20 region needs a 3×3 tile cover of 10×10 cells");
        tiles[0].Clips.Should().NotBeNullOrEmpty("tiles are clipped to the filled path");
        tiles[0].Inner.Ops.OfType<FillPathOp>().Should().HaveCount(1,
            "the cell's own content is built once and replayed per tile");
    }

    [Fact]
    public void UncolouredTilingPattern_PaintsWithTheUnderlyingColour()
    {
        // PaintType 2: the cell carries no colour; scn's numeric operands do.
        PdfDictionary cellDict = new();
        cellDict.Set(PdfName.Intern("PatternType"), 1);
        cellDict.Set(PdfName.Intern("PaintType"), 2);
        cellDict.Set(PdfName.Intern("TilingType"), 1);
        cellDict.Set(PdfName.Intern("BBox"), new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(10), new PdfInteger(10),
        }));
        byte[] cellContent = Encoding.ASCII.GetBytes("0 0 5 5 re f");
        cellDict.Set(PdfName.Intern("Length"), cellContent.Length);
        PdfStream cell = new(cellDict, cellContent);

        PageDisplayList list = Build(
            "/Pattern cs 0 1 0 /P0 scn 0 0 10 10 re f", cell);

        NestedDisplayListOp[] tiles = list.Ops.OfType<NestedDisplayListOp>().ToArray();
        tiles.Should().NotBeEmpty();
        FillPathOp cellFill = tiles[0].Inner.Ops.OfType<FillPathOp>().Single();
        cellFill.Color.ToRgb().G.Should().BeApproximately(1f, 1e-3f,
            "the underlying colour (green) from scn paints the uncoloured cell");
    }

    [Fact]
    public void NonPatternColourAfterPattern_ClearsThePattern()
    {
        PdfDictionary pattern = new();
        pattern.Set(PdfName.Intern("PatternType"), 2);
        pattern.Set(PdfName.Intern("Shading"), AxialShading());

        PageDisplayList list = Build(
            "/Pattern cs /P0 scn 1 0 0 rg 0 0 200 100 re f", pattern);

        list.Ops.OfType<ShadeOp>().Should().BeEmpty(
            "rg replaces the pattern with a flat colour");
        list.Ops.OfType<FillPathOp>().Should().HaveCount(1);
    }
}
