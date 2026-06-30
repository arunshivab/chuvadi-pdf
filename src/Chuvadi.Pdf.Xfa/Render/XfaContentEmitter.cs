// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: LA-23b Phase B — content emission.

using System;
using System.Collections.Generic;
using System.Globalization;
using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Xfa.Layout;
using Chuvadi.Pdf.Xfa.Model;

namespace Chuvadi.Pdf.Xfa.Render;

/// <summary>
/// Emits a list of positioned <see cref="XfaBox"/>es onto a
/// <see cref="PageBuilder"/> using the authoring layer's drawing primitives.
/// </summary>
internal static class XfaContentEmitter
{
    private const string DefaultFont = "Helvetica";
    private const double DefaultFontSize = 10.0;

    internal static void Emit(PageBuilder page, IReadOnlyList<XfaBox> boxes)
    {
        foreach (XfaBox box in boxes)
        {
            EmitFillAndBorder(page, box);

            if (box.Widget == XfaUiKind.CheckButton)
            {
                XfaWidgetPainter.PaintCheckButton(page, box);
                continue;
            }

            if (box.Text is { Length: > 0 })
            {
                EmitText(page, box);
            }
        }
    }

    private static void EmitFillAndBorder(PageBuilder page, XfaBox box)
    {
        if (box.Border is null || box.Width <= 0 || box.Height <= 0)
        {
            return;
        }

        Color? fill = ParseColor(box.Border.FillColor);
        Color? stroke = box.Border.HasEdge ? ParseColor(box.Border.EdgeColor) ?? Colors.Black : null;
        double strokeWidth = box.Border.EdgeThickness.Points > 0 ? box.Border.EdgeThickness.Points : 0.5;

        if (fill is null && stroke is null)
        {
            return;
        }

        page.DrawRectangle(box.X, box.Y, box.Width, box.Height, fill, stroke, strokeWidth);
    }

    private static void EmitText(PageBuilder page, XfaBox box)
    {
        string fontName = ResolveFontName(box.Font);
        double size = box.Font?.Size > 0 ? box.Font.Size : DefaultFontSize;
        Color color = ParseColor(box.Font?.Color) ?? Colors.Black;

        double textWidth = EstimateTextWidth(box.Text!, size);
        double x = box.HAlign switch
        {
            XfaHAlign.Center => box.X + ((box.Width - textWidth) / 2.0),
            XfaHAlign.Right => box.Right - textWidth,
            _ => box.X,
        };

        // Baseline: place text within the box using a simple ascent approximation.
        double ascent = size * 0.8;
        double y = box.VAlign switch
        {
            XfaVAlign.Middle => box.Y + ((box.Height + ascent) / 2.0),
            XfaVAlign.Bottom => box.Bottom - (size * 0.2),
            _ => box.Y + ascent,
        };

        page.DrawText(box.Text!, x, y, fontName, size, color);
    }

    private static string ResolveFontName(XfaFont? font)
    {
        if (font is null || string.IsNullOrEmpty(font.Typeface))
        {
            return DefaultFont;
        }

        string baseName = MapTypeface(font.Typeface!);

        // Times uses distinct Standard-14 names (Times-Bold, Times-Italic,
        // Times-BoldItalic) rather than the Helvetica/Courier suffix scheme.
        if (baseName == "Times-Roman")
        {
            if (font.Bold && font.Italic)
            {
                return "Times-BoldItalic";
            }

            if (font.Bold)
            {
                return "Times-Bold";
            }

            if (font.Italic)
            {
                return "Times-Italic";
            }

            return "Times-Roman";
        }

        if (font.Bold && font.Italic)
        {
            return baseName + "-BoldOblique";
        }

        if (font.Bold)
        {
            return baseName + "-Bold";
        }

        if (font.Italic)
        {
            return baseName + "-Oblique";
        }

        return baseName;
    }

    // Maps common XFA typeface names onto the Standard-14 base families the
    // authoring layer measures. Unknown faces fall back to Helvetica.
    private static string MapTypeface(string typeface)
    {
        string lower = typeface.ToUpperInvariant();
        if (lower.Contains("TIMES", StringComparison.Ordinal)
            || lower.Contains("SERIF", StringComparison.Ordinal)
            || lower.Contains("GEORGIA", StringComparison.Ordinal))
        {
            return "Times-Roman";
        }

        if (lower.Contains("COURIER", StringComparison.Ordinal)
            || lower.Contains("MONO", StringComparison.Ordinal)
            || lower.Contains("CONSOLAS", StringComparison.Ordinal))
        {
            return "Courier";
        }

        return "Helvetica";
    }

    // Approximate text width for alignment within a box. Standard-14 proportional
    // faces average roughly 0.5em per character; this is adequate for positioning
    // text within an explicitly-sized XFA box. Exact metrics are applied by the
    // glyph-level emission in the authoring layer.
    private static double EstimateTextWidth(string text, double size)
    {
        return text.Length * size * 0.5;
    }

    // Parses an XFA "r,g,b" colour triple (each 0-255) into an authoring Color.
    internal static Color? ParseColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string[] parts = value.Split(',');
        if (parts.Length != 3)
        {
            return null;
        }

        if (byte.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out byte r)
            && byte.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out byte g)
            && byte.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out byte b))
        {
            return Color.FromBytes(r, g, b);
        }

        return null;
    }
}
