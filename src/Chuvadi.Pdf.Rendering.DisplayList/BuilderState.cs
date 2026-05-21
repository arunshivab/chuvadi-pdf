// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2.1 — display-list intermediate

using System.Collections.Generic;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>
/// Internal graphics state tracked while a future Phase 2.1
/// display-list builder walks a content stream.
/// </summary>
/// <remarks>
/// <para>
/// This is forward-looking scaffolding for the Phase 2.1 builder
/// redesign. The shipping v2.0.0 builder uses
/// <see cref="BuilderGraphicsState"/>; <see cref="BuilderState"/> is not
/// yet wired into any code path. It compiles against the existing
/// graphics types (<see cref="Transform"/>, <see cref="ColorF"/>,
/// <see cref="Path"/>) so the project builds while the broader Phase
/// 2.1 work proceeds.
/// </para>
/// </remarks>
internal sealed class BuilderState
{
    internal Transform Ctm { get; set; } = Transform.Identity;

    internal ColorF FillColor { get; set; } = ColorF.FromRgb(0f, 0f, 0f);

    internal ColorF StrokeColor { get; set; } = ColorF.FromRgb(0f, 0f, 0f);

    internal double LineWidth { get; set; } = 1.0;

    internal LineCap LineCap { get; set; }

    internal LineJoin LineJoin { get; set; }

    internal double MiterLimit { get; set; } = 10.0;

    internal double[]? DashArray { get; set; }

    internal double DashPhase { get; set; }

    // Text state
    internal Transform TextMatrix { get; set; } = Transform.Identity;

    internal Transform TextLineMatrix { get; set; } = Transform.Identity;

    internal string? FontKey { get; set; }

    internal string? BaseFont { get; set; }

    internal double FontSize { get; set; } = 12.0;

    internal double CharSpacing { get; set; }

    internal double WordSpacing { get; set; }

    internal double HorizontalScaling { get; set; } = 100.0;

    internal double Leading { get; set; }

    internal TextRenderingMode RenderingMode { get; set; } = TextRenderingMode.Fill;

    internal double TextRise { get; set; }

    // Path under construction
    internal Path CurrentPath { get; set; } = new Path();

    internal double CurX { get; set; }

    internal double CurY { get; set; }

    internal bool HasCurrentPath { get; set; }

    internal void AppendMoveTo(double x, double y)
    {
        CurrentPath.MoveTo(x, y);
        CurX = x;
        CurY = y;
        HasCurrentPath = true;
    }

    internal void AppendLineTo(double x, double y)
    {
        CurrentPath.LineTo(x, y);
        CurX = x;
        CurY = y;
        HasCurrentPath = true;
    }

    internal void AppendCubicTo(
        double x1, double y1,
        double x2, double y2,
        double x3, double y3)
    {
        CurrentPath.CubicBezierTo(x1, y1, x2, y2, x3, y3);
        CurX = x3;
        CurY = y3;
        HasCurrentPath = true;
    }

    internal void AppendClose()
    {
        CurrentPath.ClosePath();
    }

    internal void ResetPath()
    {
        CurrentPath = new Path();
        HasCurrentPath = false;
    }

    internal BuilderState Clone()
    {
        return new BuilderState
        {
            Ctm = Ctm,
            FillColor = FillColor,
            StrokeColor = StrokeColor,
            LineWidth = LineWidth,
            LineCap = LineCap,
            LineJoin = LineJoin,
            MiterLimit = MiterLimit,
            DashArray = DashArray,
            DashPhase = DashPhase,
            TextMatrix = TextMatrix,
            TextLineMatrix = TextLineMatrix,
            FontKey = FontKey,
            BaseFont = BaseFont,
            FontSize = FontSize,
            CharSpacing = CharSpacing,
            WordSpacing = WordSpacing,
            HorizontalScaling = HorizontalScaling,
            Leading = Leading,
            RenderingMode = RenderingMode,
            TextRise = TextRise,
        };
    }
}

/// <summary>Stack of builder states for q/Q (save/restore).</summary>
/// <remarks>
/// Companion to <see cref="BuilderState"/>; see that type for the
/// shipping-status caveat.
/// </remarks>
internal sealed class BuilderStateStack
{
    private readonly Stack<BuilderState> _stack = new Stack<BuilderState>();

    internal BuilderState Current { get; private set; } = new BuilderState();

    internal void Push()
    {
        _stack.Push(Current.Clone());
    }

    internal void Pop()
    {
        if (_stack.Count > 0)
        {
            Current = _stack.Pop();
        }
    }
}
