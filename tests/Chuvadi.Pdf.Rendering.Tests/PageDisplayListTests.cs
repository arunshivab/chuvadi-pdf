// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: v2.0.0 R1 D3c-1 — DisplayList tests

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Rendering.Raster;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.Tests;

public sealed class PageDisplayListTests
{
    private static FillPathOp MakeOp()
    {
        Path p = new Path();
        p.MoveTo(0, 0);
        p.LineTo(10, 0);
        p.LineTo(10, 10);
        p.LineTo(0, 10);
        p.ClosePath();
        return new FillPathOp(p, ColorF.Black, FillRule.NonZeroWinding);
    }

    [Fact]
    public void Constructor_ValidArguments_SetsProperties()
    {
        FillPathOp op = MakeOp();
        PageDisplayList list = new PageDisplayList([op], pageWidth: 595, pageHeight: 842);

        list.Ops.Should().HaveCount(1);
        list.Ops[0].Should().BeSameAs(op);
        list.PageWidth.Should().Be(595);
        list.PageHeight.Should().Be(842);
    }

    [Fact]
    public void Constructor_EmptyOps_Allowed()
    {
        PageDisplayList list = new PageDisplayList([], 100, 100);
        list.Ops.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_NullOps_Throws()
    {
        Action act = () => new PageDisplayList(null!, 100, 100);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullOpEntry_Throws()
    {
        FillPathOp op = MakeOp();
        Action act = () => new PageDisplayList([op, null!], 100, 100);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_NegativePageWidth_Throws()
    {
        Action act = () => new PageDisplayList([], -1, 100);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_NegativePageHeight_Throws()
    {
        Action act = () => new PageDisplayList([], 100, -1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_ZeroDimensions_Allowed()
    {
        // PDFs can legally have zero-area pages; the display list should not reject.
        PageDisplayList list = new PageDisplayList([], 0, 0);
        list.PageWidth.Should().Be(0);
        list.PageHeight.Should().Be(0);
    }

    [Fact]
    public void Constructor_DefensiveCopy_MutationDoesNotAffectOps()
    {
        FillPathOp op1 = MakeOp();
        FillPathOp op2 = MakeOp();
        List<RenderOp> source = [op1];

        PageDisplayList list = new PageDisplayList(source, 100, 100);
        source.Add(op2);

        list.Ops.Should().HaveCount(1, "the display list should defensively copy the ops");
    }

    [Fact]
    public void Empty_ProducesEmptyList_WithGivenDimensions()
    {
        PageDisplayList list = PageDisplayList.Empty(595, 842);
        list.Ops.Should().BeEmpty();
        list.PageWidth.Should().Be(595);
        list.PageHeight.Should().Be(842);
    }

    [Fact]
    public void Empty_NegativeWidth_Throws()
    {
        Action act = () => PageDisplayList.Empty(-1, 100);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
