// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2.0 — SVG export
//        v2.1.1 — attribute-value escaping audit
//        v2.1.2 — xml:space="preserve" + per-glyph X positions + style hints
//        v2.1.4 — optional preserveAspectRatio attribute on emitted images;
//                 PDF images live in a unit-square mapped via the CTM, so
//                 callers want "none" (don't preserve the JPEG's intrinsic
//                 aspect ratio — the CTM already encodes the destination
//                 shape).

using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Chuvadi.Pdf.Svg;

/// <summary>
/// Builds an SVG document incrementally. Tracks open elements for proper closing.
/// </summary>
/// <remarks>
/// Caller-supplied string values are XML-escaped at every attribute boundary
/// so that font family lists like <c>Times, "Times New Roman", serif</c> (which
/// contain embedded double quotes) cannot break the output. Numeric values
/// produced via <see cref="F(double)"/> are guaranteed safe and are not
/// passed through the escaper. The <c>extraAttrs</c> overloads on
/// <see cref="OpenGroup"/> and <see cref="EmitPath"/> are intentionally
/// raw — they take a complete <c>name="value"</c> snippet rather than a
/// single value, and so trust the caller to have escaped each value already.
/// </remarks>
internal sealed class SvgWriter
{
    private readonly StringBuilder _body = new();
    private readonly StringBuilder _defs = new();
    private readonly StringBuilder _styles = new();
    private readonly Stack<string> _openElements = new();
    private readonly string _coordFormat;

    internal SvgWriter(int precision)
    {
        _coordFormat = "0." + new string('#', precision);
    }

    internal void StartSvg(double width, double height)
    {
        _body.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" ");
        _body.Append("xmlns:xlink=\"http://www.w3.org/1999/xlink\" ");
        // Width/height carry an explicit "pt" unit. PDF page boxes are measured
        // in points, and the values here are points; emitting them unitless lets
        // some viewers treat them as points and rescale the canvas by 96/72 while
        // leaving the (1 unit = 1px) content alone, which pushes the page off to
        // one side. With an explicit unit the viewBox maps cleanly onto the
        // physical page in every engine.
        _body.AppendFormat(CultureInfo.InvariantCulture,
            "width=\"{0}pt\" height=\"{1}pt\" viewBox=\"0 0 {0} {1}\">",
            F(width), F(height));
    }

    /// <summary>
    /// Emits an opaque page-background rectangle covering the whole media box,
    /// in the unflipped (top-left origin) coordinate system, before any page
    /// content. PDF pages assume an opaque white sheet; without this the SVG is
    /// transparent and composites differently from the rasteriser and across
    /// viewers.
    /// </summary>
    /// <param name="width">Page width in user units (points).</param>
    /// <param name="height">Page height in user units (points).</param>
    /// <param name="fill">SVG colour string for the sheet (e.g. <c>#ffffff</c>).</param>
    internal void EmitPageBackground(double width, double height, string fill)
    {
        _body.AppendFormat(CultureInfo.InvariantCulture,
            "<rect x=\"0\" y=\"0\" width=\"{0}\" height=\"{1}\" fill=\"{2}\"/>",
            F(width), F(height), EscapeXml(fill));
    }

    /// <summary>
    /// Emits the page-level coordinate flip (PDF bottom-left → SVG top-left)
    /// as an outer group. All subsequent content uses PDF-native coordinates.
    /// </summary>
    internal void OpenPageFlip(double pageHeight)
    {
        _body.AppendFormat(CultureInfo.InvariantCulture,
            "<g transform=\"matrix(1 0 0 -1 0 {0})\">", F(pageHeight));
        _openElements.Push("g");
    }

    internal void OpenGroup(string? transform = null, string? clipPathId = null,
        string? extraAttrs = null)
    {
        _body.Append("<g");
        if (transform is not null)
        {
            _body.Append(" transform=\"").Append(EscapeXml(transform)).Append('"');
        }
        if (clipPathId is not null)
        {
            _body.Append(" clip-path=\"url(#").Append(EscapeXml(clipPathId)).Append(")\"");
        }
        if (extraAttrs is not null) { _body.Append(' ').Append(extraAttrs); }
        _body.Append('>');
        _openElements.Push("g");
    }

    internal void CloseGroup()
    {
        if (_openElements.Count == 0 || _openElements.Peek() != "g")
        {
            throw new System.InvalidOperationException("Mismatched group close.");
        }
        _openElements.Pop();
        _body.Append("</g>");
    }

    internal void EmitPath(string d, string? fill, string? stroke,
        double strokeWidth, string? fillRule = null, string? extraAttrs = null)
    {
        _body.Append("<path d=\"").Append(EscapeXml(d)).Append('"');
        _body.Append(" fill=\"").Append(EscapeXml(fill ?? "none")).Append('"');
        if (stroke is not null)
        {
            _body.Append(" stroke=\"").Append(EscapeXml(stroke)).Append('"');
            _body.AppendFormat(CultureInfo.InvariantCulture,
                " stroke-width=\"{0}\"", F(strokeWidth));
        }
        if (fillRule is not null)
        {
            _body.Append(" fill-rule=\"").Append(EscapeXml(fillRule)).Append('"');
        }
        if (extraAttrs is not null) { _body.Append(' ').Append(extraAttrs); }
        _body.Append("/>");
    }

    /// <summary>
    /// Emits a text element at a single (x, y) position. The browser uses
    /// its own font metrics for character positioning within the run.
    /// </summary>
    /// <remarks>
    /// v2.1.2: every emitted text element now carries
    /// <c>xml:space="preserve"</c>. Without this, browsers strip leading and
    /// trailing spaces and collapse runs of spaces, which silently drops
    /// inter-word whitespace that the source PDF placed there deliberately.
    /// The optional <paramref name="fontWeight"/> and
    /// <paramref name="fontStyle"/> parameters carry the bold/italic hints
    /// derived from the PDF font name so embedded variants render correctly.
    /// </remarks>
    internal void EmitText(string content, double x, double y, string fontFamily,
        double fontSize, string fill, string? transform = null,
        string? fontWeight = null, string? fontStyle = null, string? extraAttrs = null)
    {
        _body.Append("<text");
        if (transform is not null)
        {
            _body.Append(" transform=\"").Append(EscapeXml(transform)).Append('"');
        }
        _body.AppendFormat(CultureInfo.InvariantCulture,
            " x=\"{0}\" y=\"{1}\"", F(x), F(y));
        _body.Append(" font-family=\"").Append(EscapeXml(fontFamily)).Append('"');
        _body.AppendFormat(CultureInfo.InvariantCulture,
            " font-size=\"{0}\"", F(fontSize));
        if (fontWeight is not null)
        {
            _body.Append(" font-weight=\"").Append(EscapeXml(fontWeight)).Append('"');
        }
        if (fontStyle is not null)
        {
            _body.Append(" font-style=\"").Append(EscapeXml(fontStyle)).Append('"');
        }
        _body.Append(" fill=\"").Append(EscapeXml(fill)).Append('"');
        if (extraAttrs is not null) { _body.Append(' ').Append(extraAttrs); }
        _body.Append(" xml:space=\"preserve\"");
        _body.Append('>').Append(EscapeXml(content)).Append("</text>");
    }

    /// <summary>
    /// Emits a text element with per-character X positions, allowing the
    /// caller to specify exactly where each character of the text content
    /// should be placed. Y remains a single value (all characters share
    /// the same baseline).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The SVG <c>x</c> attribute on <c>&lt;text&gt;</c> accepts a
    /// space-separated list of positions. Each character of the text
    /// content is placed at the corresponding position. If fewer positions
    /// are supplied than characters, the remaining characters follow the
    /// previous character's natural advance.
    /// </para>
    /// <para>
    /// v2.1.2: this overload is the renderer's primary mechanism for
    /// reproducing the PDF's exact glyph positions. Without it, the
    /// browser's own font metrics determine character spacing, which
    /// drifts from the PDF's positions and makes consecutive runs visually
    /// mis-aligned (the "Addres s" / "SpecialS kills" symptom).
    /// </para>
    /// </remarks>
    internal void EmitText(string content, IReadOnlyList<double> xPositions, double y,
        string fontFamily, double fontSize, string fill, string? transform = null,
        string? fontWeight = null, string? fontStyle = null, string? extraAttrs = null)
    {
        _body.Append("<text");
        if (transform is not null)
        {
            _body.Append(" transform=\"").Append(EscapeXml(transform)).Append('"');
        }
        _body.Append(" x=\"");
        for (int i = 0; i < xPositions.Count; i++)
        {
            if (i > 0) { _body.Append(' '); }
            _body.Append(F(xPositions[i]));
        }
        _body.Append('"');
        _body.AppendFormat(CultureInfo.InvariantCulture, " y=\"{0}\"", F(y));
        _body.Append(" font-family=\"").Append(EscapeXml(fontFamily)).Append('"');
        _body.AppendFormat(CultureInfo.InvariantCulture, " font-size=\"{0}\"", F(fontSize));
        if (fontWeight is not null)
        {
            _body.Append(" font-weight=\"").Append(EscapeXml(fontWeight)).Append('"');
        }
        if (fontStyle is not null)
        {
            _body.Append(" font-style=\"").Append(EscapeXml(fontStyle)).Append('"');
        }
        _body.Append(" fill=\"").Append(EscapeXml(fill)).Append('"');
        if (extraAttrs is not null) { _body.Append(' ').Append(extraAttrs); }
        _body.Append(" xml:space=\"preserve\"");
        _body.Append('>').Append(EscapeXml(content)).Append("</text>");
    }

    /// <summary>
    /// Emits an <c>&lt;image&gt;</c> element. The optional
    /// <paramref name="preserveAspectRatio"/> argument, when non-null,
    /// is written verbatim as the <c>preserveAspectRatio</c> attribute.
    /// PDF callers pass <c>"none"</c> here because PDF places images in a
    /// unit square and encodes the destination aspect ratio in the CTM;
    /// the SVG default of <c>xMidYMid meet</c> would otherwise letterbox
    /// the bitmap inside the unit square and produce visibly compressed
    /// output.
    /// </summary>
    internal void EmitImage(string href, double x, double y, double width, double height,
        string? transform = null, string? preserveAspectRatio = null)
    {
        _body.Append("<image");
        if (transform is not null)
        {
            _body.Append(" transform=\"").Append(EscapeXml(transform)).Append('"');
        }
        _body.AppendFormat(CultureInfo.InvariantCulture,
            " x=\"{0}\" y=\"{1}\" width=\"{2}\" height=\"{3}\"",
            F(x), F(y), F(width), F(height));
        if (preserveAspectRatio is not null)
        {
            _body.Append(" preserveAspectRatio=\"")
                .Append(EscapeXml(preserveAspectRatio)).Append('"');
        }
        _body.Append(" xlink:href=\"").Append(EscapeXml(href)).Append("\"/>");
    }

    /// <summary>Adds a clipPath definition; returns the assigned id.</summary>
    internal string AddClipPath(string pathData, string fillRule = "nonzero")
    {
        string id = $"clip{_defs.Length:X}_{pathData.GetHashCode():X}";
        _defs.Append("<clipPath id=\"").Append(id).Append("\">");
        _defs.Append("<path d=\"").Append(EscapeXml(pathData)).Append("\" clip-rule=\"")
            .Append(EscapeXml(fillRule)).Append("\"/></clipPath>");
        return id;
    }

    /// <summary>Adds an <c>@font-face</c> rule.</summary>
    /// <remarks>
    /// The font family and format strings are escaped for CSS — embedded
    /// double quotes are backslash-escaped per CSS string syntax. The data
    /// URL is emitted raw; data URLs are constrained to a safe character
    /// set by their producer (base64 + scheme) and contain neither quotes
    /// nor parentheses.
    /// </remarks>
    internal void AddFontFace(string family, string dataUrl, string format)
    {
        _styles.Append("@font-face{font-family:\"").Append(EscapeCssString(family))
            .Append("\";src:url(").Append(dataUrl).Append(") format(\"")
            .Append(EscapeCssString(format)).Append("\");}");
    }

    /// <summary>Appends a pre-built gradient (or other) definition into &lt;defs&gt;.</summary>
    /// <param name="defXml">The complete definition element XML.</param>
    internal void AddGradientDef(string defXml)
    {
        _defs.Append(defXml);
    }

    internal string ToSvgString()
    {
        StringBuilder result = new();
        // Output: open svg, then <defs> (with style + clip-paths), then body, then close.
        result.Append(_body.ToString());
        // Insert <defs> after the opening <svg ...>. Look for the first '>'.
        string built = result.ToString();
        int firstClose = built.IndexOf('>');
        StringBuilder finished = new();
        finished.Append(built, 0, firstClose + 1);
        if (_defs.Length > 0 || _styles.Length > 0)
        {
            finished.Append("<defs>");
            if (_styles.Length > 0)
            {
                finished.Append("<style type=\"text/css\">").Append(_styles).Append("</style>");
            }
            finished.Append(_defs);
            finished.Append("</defs>");
        }
        finished.Append(built, firstClose + 1, built.Length - firstClose - 1);
        // Close any still-open groups (defensively).
        while (_openElements.Count > 0)
        {
            string el = _openElements.Pop();
            finished.Append("</").Append(el).Append('>');
        }
        finished.Append("</svg>");
        return finished.ToString();
    }

    /// <summary>Formats a coordinate with configured precision.</summary>
    internal string F(double v)
    {
        if (double.IsNaN(v) || double.IsInfinity(v)) { return "0"; }
        return v.ToString(_coordFormat, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Escapes a string for inclusion in either XML text content or a
    /// double-quoted XML attribute value. Control characters other than
    /// tab, newline, and carriage return are replaced with a space so the
    /// result is always well-formed XML 1.0.
    /// </summary>
    private static string EscapeXml(string text)
    {
        StringBuilder sb = new(text.Length + 8);
        foreach (char ch in text)
        {
            switch (ch)
            {
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '&': sb.Append("&amp;"); break;
                case '"': sb.Append("&quot;"); break;
                case '\'': sb.Append("&apos;"); break;
                default:
                    if (ch < 0x20 && ch != '\t' && ch != '\n' && ch != '\r')
                    {
                        sb.Append(' ');   // Control char → space.
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

    /// <summary>
    /// Escapes a string for inclusion in a CSS double-quoted string literal.
    /// Backslashes and double quotes are escaped with a leading backslash
    /// per CSS Syntax Module Level 3 §4.3.5.
    /// </summary>
    private static string EscapeCssString(string text)
    {
        StringBuilder sb = new(text.Length + 8);
        foreach (char ch in text)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\A "); break;
                case '\r': sb.Append("\\D "); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }
}
