// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: v2.0.0 R1 D3c-2 — DisplayList builder tests

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Rendering.Raster;
using FluentAssertions;
using Xunit;


namespace Chuvadi.Pdf.Rendering.Tests;

/// <summary>
/// Tests for DisplayListBuilder operator interpretation.
///
/// Strategy: feed raw PDF content-stream bytes directly to the public
/// content-bytes overload of DisplayListBuilder.Build. This isolates the
/// operator interpreter from PdfPage construction, file I/O, and the
/// PDF object resolver.
/// </summary>
public sealed class DisplayListBuilderTests
{
    // ── Test fixture helpers ──────────────────────────────────────────────

    private static PageDisplayList Build(string contentOperators)
    {
        return Build(contentOperators, resources: null);
    }

    private static PageDisplayList Build(string contentOperators, PdfDictionary? resources)
    {
        byte[] content = Encoding.ASCII.GetBytes(contentOperators);
        PdfObjectStore store = new PdfObjectStore();
        return DisplayListBuilder.Build(content, resources, store, 612, 792);
    }

    // ── Smoke tests ───────────────────────────────────────────────────────

    [Fact]
    public void Build_NullContent_Throws()
    {
        PdfObjectStore store = new PdfObjectStore();
        Action act = () => DisplayListBuilder.Build(null!, null, store, 100, 100);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Build_NullStore_Throws()
    {
        Action act = () => DisplayListBuilder.Build(Array.Empty<byte>(), null, null!, 100, 100);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Build_EmptyContent_ReturnsEmptyList()
    {
        PageDisplayList list = Build("");

        list.Ops.Should().BeEmpty();
        list.PageWidth.Should().Be(612);
        list.PageHeight.Should().Be(792);
    }

    [Fact]
    public void Build_PageDimensions_PropagatedToList()
    {
        byte[] content = Array.Empty<byte>();
        PdfObjectStore store = new PdfObjectStore();
        PageDisplayList list = DisplayListBuilder.Build(content, null, store, 200, 400);

        list.PageWidth.Should().Be(200);
        list.PageHeight.Should().Be(400);
    }

    // ── Path painting ─────────────────────────────────────────────────────

    [Fact]
    public void Fill_RectanglePath_EmitsFillPathOp()
    {
        PageDisplayList list = Build("10 20 30 40 re f");

        list.Ops.Should().HaveCount(1);
        list.Ops[0].Should().BeOfType<FillPathOp>();

        FillPathOp op = (FillPathOp)list.Ops[0];
        op.Rule.Should().Be(FillRule.NonZeroWinding);
        op.Color.Should().Be(ColorF.Black);
        op.Path.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void Fill_EvenOdd_EmitsCorrectFillRule()
    {
        PageDisplayList list = Build("10 10 20 20 re f*");

        list.Ops.Should().HaveCount(1);
        FillPathOp op = (FillPathOp)list.Ops[0];
        op.Rule.Should().Be(FillRule.EvenOdd);
    }

    [Fact]
    public void Stroke_LinePath_EmitsStrokePathOp()
    {
        PageDisplayList list = Build("10 10 m 20 20 l S");

        list.Ops.Should().HaveCount(1);
        list.Ops[0].Should().BeOfType<StrokePathOp>();
    }

    [Fact]
    public void FillStroke_B_EmitsBothOps()
    {
        PageDisplayList list = Build("0 0 10 10 re B");

        list.Ops.Should().HaveCount(2);
        list.Ops[0].Should().BeOfType<FillPathOp>();
        list.Ops[1].Should().BeOfType<StrokePathOp>();
    }

    [Fact]
    public void EndPathOp_n_EmitsNoOps()
    {
        PageDisplayList list = Build("10 10 m 20 20 l n");
        list.Ops.Should().BeEmpty();
    }

    [Fact]
    public void EmptyPathFill_EmitsNoOp()
    {
        // f with no preceding path produces nothing
        PageDisplayList list = Build("f");
        list.Ops.Should().BeEmpty();
    }

    // ── Colour operators ──────────────────────────────────────────────────

    [Fact]
    public void FillColor_g_AppliedToFillOp()
    {
        PageDisplayList list = Build("0.5 g 0 0 10 10 re f");

        FillPathOp op = (FillPathOp)list.Ops[0];
        op.Color.Space.Should().Be(ColorSpace.Gray);
        op.Color.Gray.Should().BeApproximately(0.5f, 0.001f);
    }

    [Fact]
    public void FillColor_rg_AppliedToFillOp()
    {
        PageDisplayList list = Build("1 0 0 rg 0 0 10 10 re f");

        FillPathOp op = (FillPathOp)list.Ops[0];
        op.Color.Space.Should().Be(ColorSpace.Rgb);
        op.Color.R.Should().BeApproximately(1.0f, 0.001f);
        op.Color.G.Should().BeApproximately(0.0f, 0.001f);
        op.Color.B.Should().BeApproximately(0.0f, 0.001f);
    }

    [Fact]
    public void StrokeColor_RG_AppliedToStrokeOp()
    {
        PageDisplayList list = Build("0 1 0 RG 10 10 m 20 20 l S");

        StrokePathOp op = (StrokePathOp)list.Ops[0];
        op.Style.Color.Space.Should().Be(ColorSpace.Rgb);
        op.Style.Color.G.Should().BeApproximately(1.0f, 0.001f);
    }

    [Fact]
    public void FillColor_k_CmykAccepted()
    {
        PageDisplayList list = Build("0 1 1 0 k 0 0 10 10 re f");

        list.Ops.Should().HaveCount(1);
        FillPathOp op = (FillPathOp)list.Ops[0];
        op.Color.Space.Should().Be(ColorSpace.Cmyk);
    }

    [Fact]
    public void ColorSpace_DeviceRGB_KeepsFillValid()
    {
        PageDisplayList list = Build("/DeviceRGB cs 0.5 0.5 0.5 sc 0 0 10 10 re f");

        list.Ops.Should().HaveCount(1);
        FillPathOp op = (FillPathOp)list.Ops[0];
        op.Color.Space.Should().Be(ColorSpace.Rgb);
        op.Color.R.Should().BeApproximately(0.5f, 0.001f);
    }

    [Fact]
    public void ColorSpace_Pattern_SuppressesFillEmission()
    {
        // After /Pattern cs, fill is invalid. The fill op should be suppressed.
        PageDisplayList list = Build("/Pattern cs 0 0 10 10 re f");
        list.Ops.Should().BeEmpty();
    }

    // ── Stroke parameters ─────────────────────────────────────────────────

    [Fact]
    public void StrokeWidth_w_AppliedToStrokeOp()
    {
        PageDisplayList list = Build("2.5 w 10 10 m 20 20 l S");

        StrokePathOp op = (StrokePathOp)list.Ops[0];
        op.Style.Width.Should().BeApproximately(2.5, 0.001);
    }

    [Fact]
    public void LineCap_J_AppliedToStrokeOp()
    {
        PageDisplayList list = Build("2 J 10 10 m 20 20 l S");

        StrokePathOp op = (StrokePathOp)list.Ops[0];
        op.Style.Cap.Should().Be(LineCap.Square);
    }

    [Fact]
    public void LineJoin_j_AppliedToStrokeOp()
    {
        PageDisplayList list = Build("1 j 10 10 m 20 20 l S");

        StrokePathOp op = (StrokePathOp)list.Ops[0];
        op.Style.Join.Should().Be(LineJoin.Round);
    }

    // ── Graphics state q/Q ────────────────────────────────────────────────

    [Fact]
    public void QQ_StatePushPop_PreservesColorAcrossPop()
    {
        // Set red, push, set blue, fill (should be blue), pop, fill (should be red)
        PageDisplayList list = Build(
            "1 0 0 rg " +              // outer red
            "q " +                     // push
            "0 0 1 rg " +              // inner blue
            "0 0 10 10 re f " +        // fill with blue
            "Q " +                     // pop
            "0 0 10 10 re f");         // fill with red again

        list.Ops.Should().HaveCount(2);
        FillPathOp inner = (FillPathOp)list.Ops[0];
        FillPathOp outer = (FillPathOp)list.Ops[1];

        inner.Color.B.Should().BeApproximately(1.0f, 0.001f);
        outer.Color.R.Should().BeApproximately(1.0f, 0.001f);
    }

    [Fact]
    public void QQ_UnmatchedPop_DoesNotCrash()
    {
        // More Q than q — should be tolerant
        PageDisplayList list = Build("Q Q 0 0 10 10 re f");

        list.Ops.Should().HaveCount(1);
    }

    // ── CTM ───────────────────────────────────────────────────────────────

    [Fact]
    public void Cm_TranslatesPathCoordinates()
    {
        // Translate by (100, 200), then fill a 10x10 rect at origin.
        // The path coordinates in the emitted op should be at (100, 200).
        PageDisplayList list = Build("1 0 0 1 100 200 cm 0 0 10 10 re f");

        FillPathOp op = (FillPathOp)list.Ops[0];
        List<PathSegment> segs = op.Path.Segments.ToList();
        segs.Should().NotBeEmpty();

        // The MoveTo from "re" should be at (100, 200) after CTM
        segs[0].Kind.Should().Be(PathSegmentKind.MoveTo);
        segs[0].P0.X.Should().BeApproximately(100, 0.001);
        segs[0].P0.Y.Should().BeApproximately(200, 0.001);
    }

    [Fact]
    public void Cm_ScalesPathCoordinates()
    {
        // Scale by 2x, draw 10x10 rect, expect 20x20 in op coordinates.
        PageDisplayList list = Build("2 0 0 2 0 0 cm 0 0 10 10 re f");

        FillPathOp op = (FillPathOp)list.Ops[0];
        List<PathSegment> segs = op.Path.Segments.ToList();

        // Walk to find the line to (10, 10) — should be at (20, 20) after CTM
        PathSegment lineRight = segs[1];
        lineRight.Kind.Should().Be(PathSegmentKind.LineTo);
        lineRight.P0.X.Should().BeApproximately(20, 0.001);
    }

    // ── Clipping ──────────────────────────────────────────────────────────

    [Fact]
    public void W_ClipPath_AppliedToSubsequentOps()
    {
        // Build a clip rect, then fill another rect — the fill op should
        // carry the clip.
        PageDisplayList list = Build(
            "0 0 100 100 re W n " +    // clip to 100x100
            "0 0 50 50 re f");         // fill 50x50

        list.Ops.Should().HaveCount(1);
        FillPathOp op = (FillPathOp)list.Ops[0];
        op.Clips.Should().HaveCount(1);
        op.Clips[0].Rule.Should().Be(FillRule.NonZeroWinding);
    }

    [Fact]
    public void WStar_EvenOddClipRule_RecordedOnOp()
    {
        PageDisplayList list = Build(
            "0 0 100 100 re W* n " +
            "0 0 50 50 re f");

        FillPathOp op = (FillPathOp)list.Ops[0];
        op.Clips[0].Rule.Should().Be(FillRule.EvenOdd);
    }

    [Fact]
    public void W_ClipNotAppliedToPaintingOpThatTriggeredIt()
    {
        // W followed by f — the f op paints first with no clip, then clip is established.
        PageDisplayList list = Build("0 0 100 100 re W f 0 0 10 10 re f");

        list.Ops.Should().HaveCount(2);
        FillPathOp first = (FillPathOp)list.Ops[0];
        FillPathOp second = (FillPathOp)list.Ops[1];
        first.Clips.Should().BeEmpty("the W clip applies AFTER the f that triggered it");
        second.Clips.Should().HaveCount(1, "subsequent paints carry the clip");
    }

    [Fact]
    public void QQ_RestoresClipState()
    {
        // Set clip inside q...Q; the clip should not persist after Q.
        PageDisplayList list = Build(
            "q " +
            "0 0 100 100 re W n " +
            "0 0 50 50 re f " +     // op #0 — has clip
            "Q " +
            "0 0 10 10 re f");      // op #1 — no clip (restored)

        list.Ops.Should().HaveCount(2);
        ((FillPathOp)list.Ops[0]).Clips.Should().HaveCount(1);
        ((FillPathOp)list.Ops[1]).Clips.Should().BeEmpty();
    }

    // ── Text ──────────────────────────────────────────────────────────────

    [Fact]
    public void Tj_WithoutFont_EmitsNoGlyphs()
    {
        // No /Font resource — text shows nothing
        PageDisplayList list = Build("BT 100 200 Td (Hello) Tj ET");
        list.Ops.Should().BeEmpty();
    }

    [Fact]
    public void Tj_WithMissingFontResource_DoesNotCrash()
    {
        // Tf references /F1 which isn't in resources — graceful no-op
        PageDisplayList list = Build("BT /F1 12 Tf 100 200 Td (Hello) Tj ET");
        list.Ops.Should().BeEmpty();
    }

    [Fact]
    public void TextOperatorsParseWithoutCrashing()
    {
        // All text state operators on a single line, no font available
        PageDisplayList list = Build(
            "BT " +
            "/F1 12 Tf " +
            "2 Tc " +
            "1 Tw " +
            "100 Tz " +
            "14 TL " +
            "0 Ts " +
            "0 Tr " +
            "10 20 Td " +
            "10 20 TD " +
            "1 0 0 1 100 200 Tm " +
            "T* " +
            "ET");

        list.Ops.Should().BeEmpty();
    }

    [Fact]
    public void TJ_OperatorParsesArrayContents()
    {
        // No font, but operands should be consumed without crashing
        PageDisplayList list = Build("BT [(Hello) -100 (World)] TJ ET");
        list.Ops.Should().BeEmpty();
    }

    // ── Marked content / compatibility — silent no-ops ────────────────────

    [Fact]
    public void MarkedContent_BMCEMC_NoCrash()
    {
        PageDisplayList list = Build("/Tag BMC 0 0 10 10 re f EMC");

        list.Ops.Should().HaveCount(1);
        list.Ops[0].Should().BeOfType<FillPathOp>();
    }

    [Fact]
    public void MarkedContent_BDC_Parses()
    {
        PageDisplayList list = Build("/Tag /Props BDC 0 0 10 10 re f EMC");

        list.Ops.Should().HaveCount(1);
    }

    [Fact]
    public void CompatibilityOperators_BXEX_NoCrash()
    {
        PageDisplayList list = Build("BX someUnknownOp EX 0 0 10 10 re f");

        list.Ops.Should().HaveCount(1);
    }

    // ── Multiple operations ───────────────────────────────────────────────

    [Fact]
    public void MultipleFills_EmitInOrder()
    {
        PageDisplayList list = Build(
            "1 0 0 rg 0 0 10 10 re f " +
            "0 1 0 rg 20 20 10 10 re f " +
            "0 0 1 rg 40 40 10 10 re f");

        list.Ops.Should().HaveCount(3);

        FillPathOp red = (FillPathOp)list.Ops[0];
        FillPathOp green = (FillPathOp)list.Ops[1];
        FillPathOp blue = (FillPathOp)list.Ops[2];

        red.Color.R.Should().BeApproximately(1.0f, 0.001f);
        green.Color.G.Should().BeApproximately(1.0f, 0.001f);
        blue.Color.B.Should().BeApproximately(1.0f, 0.001f);
    }

    [Fact]
    public void Determinism_SameInputProducesSameOutput()
    {
        const string content =
            "q 1 0 0 rg 0 0 100 100 re f Q " +
            "q 0 1 0 rg 50 50 100 100 re f Q";

        PageDisplayList a = Build(content);
        PageDisplayList b = Build(content);

        a.Ops.Should().HaveSameCount(b.Ops);

        for (int i = 0; i < a.Ops.Count; i++)
        {
            a.Ops[i].GetType().Should().Be(b.Ops[i].GetType());
        }
    }

    // ── Curves ────────────────────────────────────────────────────────────

    [Fact]
    public void CubicBezier_c_EmitsBezierSegment()
    {
        PageDisplayList list = Build("0 0 m 10 0 10 10 20 10 c S");

        StrokePathOp op = (StrokePathOp)list.Ops[0];
        op.Path.Segments.Any(s => s.Kind == PathSegmentKind.CubicBezierTo).Should().BeTrue();
    }

    [Fact]
    public void CubicBezier_v_EmitsBezierSegment()
    {
        PageDisplayList list = Build("0 0 m 10 10 20 10 v S");

        StrokePathOp op = (StrokePathOp)list.Ops[0];
        op.Path.Segments.Any(s => s.Kind == PathSegmentKind.CubicBezierTo).Should().BeTrue();
    }

    [Fact]
    public void CubicBezier_y_EmitsBezierSegment()
    {
        PageDisplayList list = Build("0 0 m 5 5 15 10 y S");

        StrokePathOp op = (StrokePathOp)list.Ops[0];
        op.Path.Segments.Any(s => s.Kind == PathSegmentKind.CubicBezierTo).Should().BeTrue();
    }

    // ── Path closing ──────────────────────────────────────────────────────

    [Fact]
    public void ClosePath_h_AddsClosePathSegment()
    {
        PageDisplayList list = Build("0 0 m 10 0 l 10 10 l 0 10 l h f");

        FillPathOp op = (FillPathOp)list.Ops[0];
        op.Path.Segments.Any(s => s.Kind == PathSegmentKind.ClosePath).Should().BeTrue();
    }

    [Fact]
    public void CloseAndStroke_s_ClosesAndStrokes()
    {
        PageDisplayList list = Build("0 0 m 10 0 l 10 10 l s");

        StrokePathOp op = (StrokePathOp)list.Ops[0];
        op.Path.Segments.Any(s => s.Kind == PathSegmentKind.ClosePath).Should().BeTrue();
    }
}
