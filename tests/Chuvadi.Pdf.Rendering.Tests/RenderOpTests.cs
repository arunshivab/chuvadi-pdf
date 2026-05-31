// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: v2.0.0 R1 D3c-1 — DisplayList tests

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Images;
using Chuvadi.Pdf.Rendering.Raster;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.Tests;

public sealed class ClipPathTests
{
    [Fact]
    public void Constructor_SetsPathAndRule()
    {
        Path p = new Path();
        ClipPath clip = new ClipPath(p, FillRule.EvenOdd);
        clip.Path.Should().BeSameAs(p);
        clip.Rule.Should().Be(FillRule.EvenOdd);
    }

    [Fact]
    public void Constructor_NullPath_Throws()
    {
        Action act = () => new ClipPath(null!, FillRule.NonZeroWinding);
        act.Should().Throw<ArgumentNullException>();
    }
}

public sealed class FillPathOpTests
{
    [Fact]
    public void Constructor_SetsProperties()
    {
        Path p = new Path();
        FillPathOp op = new FillPathOp(p, ColorF.FromRgb(1, 0, 0), FillRule.EvenOdd);

        op.Path.Should().BeSameAs(p);
        op.Color.Should().Be(ColorF.FromRgb(1, 0, 0));
        op.Rule.Should().Be(FillRule.EvenOdd);
    }

    [Fact]
    public void Constructor_NullPath_Throws()
    {
        Action act = () => new FillPathOp(null!, ColorF.Black, FillRule.NonZeroWinding);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NoClipsProvided_HasEmptyClipList()
    {
        FillPathOp op = new FillPathOp(new Path(), ColorF.Black, FillRule.NonZeroWinding);
        op.Clips.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_NullClips_TreatedAsEmpty()
    {
        FillPathOp op = new FillPathOp(new Path(), ColorF.Black, FillRule.NonZeroWinding, clips: null);
        op.Clips.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_ClipsProvided_StoredOnOp()
    {
        ClipPath clip = new ClipPath(new Path(), FillRule.EvenOdd);
        FillPathOp op = new FillPathOp(new Path(), ColorF.Black, FillRule.NonZeroWinding, [clip]);

        op.Clips.Should().HaveCount(1);
        op.Clips[0].Rule.Should().Be(FillRule.EvenOdd);
    }

    [Fact]
    public void Constructor_DefensiveCopy_ClipMutationDoesNotAffectOp()
    {
        ClipPath clip1 = new ClipPath(new Path(), FillRule.NonZeroWinding);
        ClipPath clip2 = new ClipPath(new Path(), FillRule.EvenOdd);
        List<ClipPath> source = [clip1];

        FillPathOp op = new FillPathOp(new Path(), ColorF.Black, FillRule.NonZeroWinding, source);
        source.Add(clip2);

        op.Clips.Should().HaveCount(1, "the op should defensively copy the clip list");
    }
}

public sealed class StrokePathOpTests
{
    [Fact]
    public void Constructor_SetsProperties()
    {
        Path p = new Path();
        StrokeStyle style = StrokeStyle.Default.WithWidth(2.5);
        StrokePathOp op = new StrokePathOp(p, style);

        op.Path.Should().BeSameAs(p);
        op.Style.Should().BeSameAs(style);
    }

    [Fact]
    public void Constructor_NullPath_Throws()
    {
        Action act = () => new StrokePathOp(null!, StrokeStyle.Default);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullStyle_Throws()
    {
        Action act = () => new StrokePathOp(new Path(), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NoClips_HasEmptyClipList()
    {
        StrokePathOp op = new StrokePathOp(new Path(), StrokeStyle.Default);
        op.Clips.Should().BeEmpty();
    }
}

public sealed class DrawGlyphOpTests
{
    [Fact]
    public void Constructor_SetsProperties()
    {
        Path p = new Path();
        DrawGlyphOp op = new DrawGlyphOp(p, ColorF.Black);

        op.Path.Should().BeSameAs(p);
        op.Color.Should().Be(ColorF.Black);
    }

    [Fact]
    public void Constructor_NullPath_Throws()
    {
        Action act = () => new DrawGlyphOp(null!, ColorF.Black);
        act.Should().Throw<ArgumentNullException>();
    }
}

public sealed class DrawImageOpTests
{
    [Fact]
    public void Constructor_SetsProperties()
    {
        ImageFrame img = ImageFrame.Create(2, 2, ImageColorFormat.Rgb24);
        Transform t = Transform.CreateScale(100);
        DrawImageOp op = new DrawImageOp(img, t);

        op.Image.Should().BeSameAs(img);
        op.DeviceTransform.Should().Be(t);
    }

    [Fact]
    public void Constructor_NullImage_Throws()
    {
        Action act = () => new DrawImageOp(null!, Transform.Identity);
        act.Should().Throw<ArgumentNullException>();
    }
}

public sealed class NestedDisplayListOpTests
{
    [Fact]
    public void Constructor_SetsProperties()
    {
        PageDisplayList inner = PageDisplayList.Empty(100, 100);
        Transform t = Transform.CreateTranslation(10, 20);
        NestedDisplayListOp op = new NestedDisplayListOp(inner, t);

        op.Inner.Should().BeSameAs(inner);
        op.CtmComposition.Should().Be(t);
    }

    [Fact]
    public void Constructor_NullInner_Throws()
    {
        Action act = () => new NestedDisplayListOp(null!, Transform.Identity);
        act.Should().Throw<ArgumentNullException>();
    }
}
