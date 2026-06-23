// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.2 — Content streams, §9.4 — Text objects
// PHASE: Phase 2.8 — DisplayList consolidation (one walker, two sinks)
// The operator-event contract between the shared content-stream walker and
// the display-list builders that consume it.

using System.Collections.Generic;

namespace Chuvadi.Pdf.Rendering.Walking;

/// <summary>
/// One element of a TJ show-text array: either a string chunk (bytes) or a
/// numeric position adjustment in thousandths of text-space units.
/// </summary>
internal readonly struct TextArrayElement
{
    private TextArrayElement(byte[]? text, double adjustment)
    {
        Text = text;
        Adjustment = adjustment;
    }

    /// <summary>Creates a string-chunk element.</summary>
    internal static TextArrayElement ForText(byte[] text) => new(text, 0);

    /// <summary>Creates a position-adjustment element.</summary>
    internal static TextArrayElement ForAdjustment(double adjustment) => new(null, adjustment);

    /// <summary>The string bytes; null for adjustment elements.</summary>
    internal byte[]? Text { get; }

    /// <summary>The adjustment in thousandths of text-space units; 0 for string elements.</summary>
    internal double Adjustment { get; }

    /// <summary>True when this element carries string bytes.</summary>
    internal bool IsText => Text is not null;
}

/// <summary>
/// Receives typed operator events from <see cref="ContentStreamWalker"/>.
/// </summary>
/// <remarks>
/// <para>
/// The walker owns tokenisation, operand parsing, and operator dispatch; the
/// sink owns all interpretation state — the graphics-state stack, the current
/// path, text matrices, font resolution, and emission. This split keeps each
/// consumer's numeric behaviour (glyph advances, gap tracking, clipping)
/// exactly where it was before consolidation.
/// </para>
/// <para>
/// Every member has a no-op default so a sink implements only the operators
/// it interprets; unhandled operators are silently ignored, matching both
/// pre-consolidation builders.
/// </para>
/// <para>
/// Numeric operands are parsed tolerantly (malformed numbers read as 0),
/// and events fire only when the operator carried enough operands — the
/// permissive behaviour real-world PDFs require.
/// </para>
/// </remarks>
internal interface IContentOperatorSink
{
    // ── Graphics state ────────────────────────────────────────────────────

    /// <summary>q — push the graphics state.</summary>
    void SaveState()
    {
    }

    /// <summary>Q — pop the graphics state.</summary>
    void RestoreState()
    {
    }

    /// <summary>cm — concatenate a matrix onto the CTM.</summary>
    void ConcatMatrix(double a, double b, double c, double d, double e, double f)
    {
    }

    /// <summary>w — set the line width.</summary>
    void SetLineWidth(double width)
    {
    }

    /// <summary>J — set the line cap style.</summary>
    void SetLineCap(int cap)
    {
    }

    /// <summary>j — set the line join style.</summary>
    void SetLineJoin(int join)
    {
    }

    /// <summary>M — set the miter limit.</summary>
    void SetMiterLimit(double limit)
    {
    }

    /// <summary>d — set the dash pattern (already unwrapped from its array).</summary>
    void SetDashPattern(double[] dashes, double phase)
    {
    }

    // ── Colour ────────────────────────────────────────────────────────────

    /// <summary>g — set the fill colour to a DeviceGray value.</summary>
    void SetFillGray(double gray)
    {
    }

    /// <summary>G — set the stroke colour to a DeviceGray value.</summary>
    void SetStrokeGray(double gray)
    {
    }

    /// <summary>rg — set the fill colour to a DeviceRGB value.</summary>
    void SetFillRgb(double r, double g, double b)
    {
    }

    /// <summary>RG — set the stroke colour to a DeviceRGB value.</summary>
    void SetStrokeRgb(double r, double g, double b)
    {
    }

    /// <summary>k — set the fill colour to a DeviceCMYK value.</summary>
    void SetFillCmyk(double c, double m, double y, double k)
    {
    }

    /// <summary>K — set the stroke colour to a DeviceCMYK value.</summary>
    void SetStrokeCmyk(double c, double m, double y, double k)
    {
    }

    /// <summary>cs / CS — select a colour space by name.</summary>
    void SetColorSpace(string name, bool stroke)
    {
    }

    /// <summary>
    /// sc / scn / SC / SCN — set a colour in the current colour space.
    /// <paramref name="components"/> holds the numeric operands in order;
    /// <paramref name="hasName"/> is true when a name operand (a pattern)
    /// was present.
    /// </summary>
    void SetColorN(double[] components, bool hasName, bool stroke)
    {
    }

    // ── Path construction ─────────────────────────────────────────────────

    /// <summary>m — begin a new subpath.</summary>
    void MoveTo(double x, double y)
    {
    }

    /// <summary>l — append a straight segment.</summary>
    void LineTo(double x, double y)
    {
    }

    /// <summary>c — append a cubic Bézier with two control points.</summary>
    void CurveTo(double x1, double y1, double x2, double y2, double x3, double y3)
    {
    }

    /// <summary>v — append a cubic Bézier whose first control point is the current point.</summary>
    void CurveToV(double x2, double y2, double x3, double y3)
    {
    }

    /// <summary>y — append a cubic Bézier whose second control point is the end point.</summary>
    void CurveToY(double x1, double y1, double x3, double y3)
    {
    }

    /// <summary>h — close the current subpath.</summary>
    void ClosePath()
    {
    }

    /// <summary>re — append a rectangle as a closed subpath.</summary>
    void AppendRectangle(double x, double y, double width, double height)
    {
    }

    // ── Path painting ─────────────────────────────────────────────────────

    /// <summary>f / F / f* — fill the current path.</summary>
    void FillPath(bool evenOdd)
    {
    }

    /// <summary>S / s — stroke the current path (s closes it first).</summary>
    void StrokePath(bool closeFirst)
    {
    }

    /// <summary>B / B* / b / b* — fill then stroke the current path.</summary>
    void FillAndStrokePath(bool evenOdd, bool closeFirst)
    {
    }

    /// <summary>n — end the path without painting (applies a pending clip).</summary>
    void EndPath()
    {
    }

    /// <summary>W / W* — mark the current path as a pending clip.</summary>
    void SetClip(bool evenOdd)
    {
    }

    // ── Text state and positioning ────────────────────────────────────────

    /// <summary>BT — begin a text object.</summary>
    void BeginText()
    {
    }

    /// <summary>ET — end a text object.</summary>
    void EndText()
    {
    }

    /// <summary>Tf — select a font resource and size.</summary>
    void SetFont(string name, double size)
    {
    }

    /// <summary>Td — move the text line origin.</summary>
    void TextMove(double tx, double ty)
    {
    }

    /// <summary>TD — move the text line origin and set the leading to −ty.</summary>
    void TextMoveWithLeading(double tx, double ty)
    {
    }

    /// <summary>Tm — set the text matrix and line matrix.</summary>
    void SetTextMatrix(double a, double b, double c, double d, double e, double f)
    {
    }

    /// <summary>T* — move to the start of the next line.</summary>
    void TextNextLine()
    {
    }

    /// <summary>Tc — set character spacing.</summary>
    void SetCharSpacing(double spacing)
    {
    }

    /// <summary>Tw — set word spacing.</summary>
    void SetWordSpacing(double spacing)
    {
    }

    /// <summary>Tz — set horizontal scaling (percent).</summary>
    void SetHorizontalScaling(double scale)
    {
    }

    /// <summary>TL — set the text leading.</summary>
    void SetLeading(double leading)
    {
    }

    /// <summary>Tr — set the text rendering mode.</summary>
    void SetTextRenderingMode(int mode)
    {
    }

    /// <summary>Ts — set the text rise.</summary>
    void SetTextRise(double rise)
    {
    }

    // ── Text showing ──────────────────────────────────────────────────────

    /// <summary>Tj — show a text string (escape/hex decoding already applied).</summary>
    void ShowText(byte[] text)
    {
    }

    /// <summary>TJ — show text with interleaved position adjustments.</summary>
    void ShowTextArray(IReadOnlyList<TextArrayElement> elements)
    {
    }

    /// <summary>' — move to the next line, then show text.</summary>
    void MoveNextLineShowText(byte[] text)
    {
    }

    /// <summary>" — set word and character spacing, move to the next line, then show text.</summary>
    void SetSpacingMoveNextLineShowText(double wordSpacing, double charSpacing, byte[] text)
    {
    }

    // ── Type 3 font glyph metrics ─────────────────────────────────────────

    /// <summary>
    /// d0 — set the width of the current Type 3 glyph (a coloured glyph that
    /// supplies its own colour). Operands are in glyph space.
    /// </summary>
    void SetGlyphWidth(double wx, double wy)
    {
    }

    /// <summary>
    /// d1 — set the width and bounding box of the current Type 3 glyph (an
    /// uncoloured glyph painted with the text colour; colour operators in the
    /// glyph description are ignored). Operands are in glyph space.
    /// </summary>
    void SetGlyphWidthAndBBox(
        double wx, double wy, double llx, double lly, double urx, double ury)
    {
    }

    // ── XObjects ──────────────────────────────────────────────────────────

    /// <summary>Do — invoke a named XObject (image or form).</summary>
    void InvokeXObject(string name)
    {
    }

    // ── External graphics state ───────────────────────────────────────────

    /// <summary>
    /// gs — apply a named ExtGState from the current resources. The sink
    /// resolves the named dictionary and applies the entries it interprets
    /// (for example /ca and /CA constant alpha). PDF 32000-1:2008 §8.4.5.
    /// </summary>
    void ApplyExtGState(string name)
    {
    }

    /// <summary>
    /// Paints a shading (the <c>sh</c> operator), named in /Resources /Shading,
    /// across the current clip region. The default implementation is a no-op.
    /// </summary>
    /// <param name="name">The shading resource name.</param>
    void PaintShading(string name)
    {
    }

    // ── Marked content ────────────────────────────────────────────────────

    /// <summary>
    /// BDC / BMC — begin a marked-content sequence. <paramref name="tag"/> is
    /// the marked-content tag (for example <c>"OC"</c> for optional content).
    /// <paramref name="propertyName"/> is the name of the /Properties resource
    /// entry carried by BDC; it is null for BMC (which has no property operand)
    /// and for a BDC whose property operand was an inline dictionary rather
    /// than a name. PDF 32000-1:2008 §8.10.2, §8.11.3.2.
    /// </summary>
    void BeginMarkedContent(string tag, string? propertyName)
    {
    }

    /// <summary>EMC — end the most recently begun marked-content sequence.</summary>
    void EndMarkedContent()
    {
    }

    // ── Fallback ──────────────────────────────────────────────────────────

    /// <summary>Any operator the walker does not recognise.</summary>
    void UnknownOperator(string op)
    {
    }
}
