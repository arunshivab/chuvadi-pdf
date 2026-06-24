// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8 — Graphics, §9 — Text
// PHASE: Phase 1.3 — Authoring module

using System;
using System.Globalization;
using System.Text;

namespace Chuvadi.Pdf.Authoring;

/// <summary>
/// Internal writer that accumulates PDF content stream operators.
/// </summary>
/// <remarks>
/// The page builder uses top-left origin coordinates with Y increasing
/// downward, but PDF native is bottom-left with Y increasing upward.
/// This writer performs the translation: callers supply
/// (x, yFromTop, width, height) and the writer emits (x, pageHeight - yFromTop - height).
/// </remarks>
internal sealed class ContentStreamWriter
{
    private readonly StringBuilder _sb = new();
    private readonly double _pageHeight;

    internal ContentStreamWriter(double pageHeight)
    {
        _pageHeight = pageHeight;
    }

    internal byte[] ToBytes() => Encoding.Latin1.GetBytes(_sb.ToString());

    /// <summary>Translates a top-left Y coordinate to PDF's bottom-left Y.</summary>
    internal double FlipY(double yFromTop) => _pageHeight - yFromTop;

    // ── Graphics state ────────────────────────────────────────────────────

    internal void PushState() => _sb.AppendLine("q");
    internal void PopState() => _sb.AppendLine("Q");

    internal void SetFillRgb(Color c)
        => _sb.AppendLine($"{F(c.R)} {F(c.G)} {F(c.B)} rg");

    internal void SetStrokeRgb(Color c)
        => _sb.AppendLine($"{F(c.R)} {F(c.G)} {F(c.B)} RG");

    internal void SetLineWidth(double w)
        => _sb.AppendLine($"{F(w)} w");

    // ── Path construction ─────────────────────────────────────────────────

    /// <summary>Moves the current point. Y is in top-left space.</summary>
    internal void MoveToTopLeft(double x, double yFromTop)
        => _sb.AppendLine($"{F(x)} {F(FlipY(yFromTop))} m");

    /// <summary>Draws a line to (x, y) in top-left space.</summary>
    internal void LineToTopLeft(double x, double yFromTop)
        => _sb.AppendLine($"{F(x)} {F(FlipY(yFromTop))} l");

    /// <summary>Emits a rectangle. (x, y) is the top-left corner in top-left space.</summary>
    internal void RectTopLeft(double x, double yFromTop, double w, double h)
        => _sb.AppendLine(
            $"{F(x)} {F(FlipY(yFromTop) - h)} {F(w)} {F(h)} re");

    /// <summary>
    /// Appends a cubic Bézier curve to (ex, ey) with control points
    /// (c1x, c1y) and (c2x, c2y). All points are in top-left space.
    /// </summary>
    internal void CurveToTopLeft(
        double c1x, double c1y, double c2x, double c2y, double ex, double ey)
        => _sb.AppendLine(
            $"{F(c1x)} {F(FlipY(c1y))} {F(c2x)} {F(FlipY(c2y))} {F(ex)} {F(FlipY(ey))} c");

    /// <summary>Closes the current sub-path with a straight line to its start.</summary>
    internal void ClosePath() => _sb.AppendLine("h");

    /// <summary>Strokes the current path.</summary>
    internal void Stroke() => _sb.AppendLine("S");

    /// <summary>Fills the current path.</summary>
    internal void Fill() => _sb.AppendLine("f");

    /// <summary>Fills the current path using the even-odd rule.</summary>
    internal void FillEvenOdd() => _sb.AppendLine("f*");

    /// <summary>Fills and strokes the current path.</summary>
    internal void FillAndStroke() => _sb.AppendLine("B");

    /// <summary>Fills (even-odd rule) and strokes the current path.</summary>
    internal void FillAndStrokeEvenOdd() => _sb.AppendLine("B*");

    // ── Text ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Emits a single-line text string at (x, yFromTop). The baseline lands
    /// `fontSize` units below yFromTop.
    /// </summary>
    internal void ShowTextAt(string fontKey, double size, double x, double yFromTop, string text)
    {
        // Place baseline at yFromTop + fontSize (so the top of the text aligns to yFromTop).
        double baselineY = FlipY(yFromTop + size);
        _sb.AppendLine("BT");
        _sb.AppendLine($"/{fontKey} {F(size)} Tf");
        _sb.AppendLine($"1 0 0 1 {F(x)} {F(baselineY)} Tm");
        _sb.AppendLine($"({Escape(text)}) Tj");
        _sb.AppendLine("ET");
    }

    /// <summary>
    /// Emits a single line of text encoded as two-byte big-endian glyph
    /// identifiers, for a composite (Type0 / Identity-H) font.
    /// <paramref name="hexGlyphs"/> is the concatenated four-hex-digit GID
    /// string (e.g. <c>00120015</c>).
    /// </summary>
    internal void ShowGlyphsAt(
        string fontKey, double size, double x, double yFromTop, string hexGlyphs)
    {
        double baselineY = FlipY(yFromTop + size);
        _sb.AppendLine("BT");
        _sb.AppendLine($"/{fontKey} {F(size)} Tf");
        _sb.AppendLine($"1 0 0 1 {F(x)} {F(baselineY)} Tm");
        _sb.AppendLine($"<{hexGlyphs}> Tj");
        _sb.AppendLine("ET");
    }

    // ── Images ────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders an image XObject named <paramref name="imageKey"/>. When
    /// <paramref name="alphaGsKey"/> is non-null, the named ExtGState (constant
    /// alpha) is applied before the image is painted.
    /// </summary>
    internal void DrawImage(
        string imageKey, double x, double yFromTop, double width, double height,
        string? alphaGsKey = null)
    {
        double bottomY = FlipY(yFromTop + height);
        _sb.AppendLine("q");
        if (alphaGsKey is not null)
        {
            _sb.AppendLine($"/{alphaGsKey} gs");
        }

        // Image transform matrix is [w 0 0 h x y] — scales unit square to (w, h) at (x, y).
        _sb.AppendLine($"{F(width)} 0 0 {F(height)} {F(x)} {F(bottomY)} cm");
        _sb.AppendLine($"/{imageKey} Do");
        _sb.AppendLine("Q");
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string F(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

    private static string Escape(string text)
    {
        StringBuilder sb = new(text.Length + 8);
        foreach (char ch in text)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '(': sb.Append("\\("); break;
                case ')': sb.Append("\\)"); break;
                case '\r': sb.Append("\\r"); break;
                case '\n': sb.Append("\\n"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < 0x20 || ch > 0x7E)
                    {
                        // Non-printable / non-ASCII — emit as octal.
                        sb.Append('\\');
                        sb.Append(Convert.ToString(ch & 0xFF, 8).PadLeft(3, '0'));
                    }
                    else
                    {
                        sb.Append(ch);
                    }
                    break;
            }
        }
        return sb.ToString();
    }
}
