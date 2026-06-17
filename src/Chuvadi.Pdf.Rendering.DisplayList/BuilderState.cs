// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2.1 — display-list intermediate

using System.Collections.Generic;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>Internal graphics state tracked while building a display list.</summary>
internal sealed class BuilderState
{
    internal AffineMatrix Ctm { get; set; } = AffineMatrix.Identity;
    internal PdfColor FillColor { get; set; } = PdfColor.Black;
    internal PdfColor StrokeColor { get; set; } = PdfColor.Black;
    internal double LineWidth { get; set; } = 1.0;
    internal LineCap LineCap { get; set; }
    internal LineJoin LineJoin { get; set; }
    internal double MiterLimit { get; set; } = 10.0;
    internal double[]? DashArray { get; set; }
    internal double DashPhase { get; set; }

    // Constant alpha from ExtGState (/ca fill, /CA stroke). 1.0 = opaque.
    internal double FillAlpha { get; set; } = 1.0;
    internal double StrokeAlpha { get; set; } = 1.0;

    // Text state
    internal AffineMatrix TextMatrix { get; set; } = AffineMatrix.Identity;
    internal AffineMatrix TextLineMatrix { get; set; } = AffineMatrix.Identity;
    internal string? FontKey { get; set; }
    internal string? BaseFont { get; set; }
    internal FontStyle Style { get; set; } = FontStyle.Default;
    internal double FontSize { get; set; } = 12.0;
    internal double CharSpacing { get; set; }
    internal double WordSpacing { get; set; }
    internal double HorizontalScaling { get; set; } = 100.0;
    internal double Leading { get; set; }
    internal TextRenderingMode RenderingMode { get; set; } = TextRenderingMode.Fill;
    internal double TextRise { get; set; }

    // Path under construction
    internal PathGeometry CurrentPath { get; set; } = new();
    internal double CurX { get; set; }
    internal double CurY { get; set; }
    internal bool HasCurrentPath { get; set; }

    internal void AppendMoveTo(double x, double y)
    {
        CurrentPath.MoveTo(x, y);
        CurX = x; CurY = y;
        HasCurrentPath = true;
    }

    internal void AppendLineTo(double x, double y)
    {
        CurrentPath.LineTo(x, y);
        CurX = x; CurY = y;
        HasCurrentPath = true;
    }

    internal void AppendCubicTo(double x1, double y1, double x2, double y2, double x3, double y3)
    {
        CurrentPath.CubicTo(x1, y1, x2, y2, x3, y3);
        CurX = x3; CurY = y3;
        HasCurrentPath = true;
    }

    internal void AppendClose()
    {
        CurrentPath.Close();
    }

    internal void ResetPath()
    {
        CurrentPath = new PathGeometry();
        HasCurrentPath = false;
    }

    internal BuilderState Clone() => new()
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
        FillAlpha = FillAlpha,
        StrokeAlpha = StrokeAlpha,
        TextMatrix = TextMatrix,
        TextLineMatrix = TextLineMatrix,
        FontKey = FontKey,
        BaseFont = BaseFont,
        Style = Style,
        FontSize = FontSize,
        CharSpacing = CharSpacing,
        WordSpacing = WordSpacing,
        HorizontalScaling = HorizontalScaling,
        Leading = Leading,
        RenderingMode = RenderingMode,
        TextRise = TextRise,
    };
}

/// <summary>Stack of builder states for q/Q.</summary>
internal sealed class BuilderStateStack
{
    private readonly Stack<BuilderState> _stack = new();
    internal BuilderState Current { get; private set; } = new();

    internal void Push() => _stack.Push(Current.Clone());
    internal void Pop()
    {
        if (_stack.Count > 0) { Current = _stack.Pop(); }
    }
}
