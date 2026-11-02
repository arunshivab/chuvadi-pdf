// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.31 — CFF advance widths: the optional width operand is found for
//        every first stack-clearing operator (Type 2 §3.1), including hmoveto
//        and vmoveto, which take one operand, and an opening hintmask.

using System;
using Chuvadi.Pdf.Graphics;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Fonts.Rendering.Tests;

public sealed class CffWidthTests
{
    // A Type1C font made with fontTools: defaultWidthX 364, nominalWidthX 586.
    // Glyph order and the width each charstring declares (fontTools agrees):
    // .notdef (default 364), hm 833 (width before hmoveto), vm 552 (before
    // vmoveto), mask 600 (before implicit stems and hintmask), rm 541 (before
    // rmoveto), dflt (no width, default 364).
    private const string WidthCffBase64 =
        "AQAEAQABAQEKV2lkdGhUZXN0AAEBARP4IAKLi/fA9/IF1w+R9zUS2xEABgEBAwUJCw8YaG12bW1hc2tybWRmbHRXaWR0aFRlc3QAAAEBhwQABgEBAhAdLjxIDveL7xb3XAb3wAf7XAYOab0E91wG98AH+1wGDpmfsxOAqRb3XAb3wAf7XAYOXpWfFfdcBvfAB/tcBg6VFvdcBvfAB/tcBg74ABT43hU=";

    [Theory]
    [InlineData("hm", 833)]
    [InlineData("vm", 552)]
    [InlineData("mask", 600)]
    [InlineData("rm", 541)]
    [InlineData("dflt", 364)]
    [InlineData(".notdef", 364)]
    public void TheAdvanceWidth_IsTheOneTheCharstringDeclares(string glyphName, int expected)
    {
        CffLoader loader = new(Convert.FromBase64String(WidthCffBase64));

        int gid = loader.GlyphNameToGid[glyphName];

        loader.GetGlyphMetrics(gid).AdvanceWidth.Should().Be(expected);
    }

    [Fact]
    public void AWidthBeforeHmoveto_DoesNotMoveTheOutline()
    {
        // Before 3.31 the width operand of "hm" was taken as nothing and the
        // move used the last operand only, so the outline was right but the
        // advance fell back to defaultWidthX. The outline must stay put.
        CffLoader loader = new(Convert.FromBase64String(WidthCffBase64));

        GlyphOutline hm = loader.GetGlyphOutline(loader.GlyphNameToGid["hm"]);
        GlyphOutline dflt = loader.GetGlyphOutline(loader.GlyphNameToGid["dflt"]);

        RectangleF a = hm.Outline.EndpointBounds();
        RectangleF b = dflt.Outline.EndpointBounds();
        a.Width.Should().BeApproximately(b.Width, 0.001);
        a.Height.Should().BeApproximately(b.Height, 0.001);
    }
}
