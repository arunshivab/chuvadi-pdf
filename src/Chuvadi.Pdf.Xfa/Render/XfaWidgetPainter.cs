// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: LA-23b Phases B + D — widget rendering.

using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Xfa.Layout;

namespace Chuvadi.Pdf.Xfa.Render;

/// <summary>
/// Paints field UI widgets (check buttons, radio buttons, signature fields)
/// onto a page using the authoring layer's drawing primitives.
/// </summary>
internal static class XfaWidgetPainter
{
    internal static void PaintCheckButton(PageBuilder page, XfaBox box)
    {
        Color stroke = XfaContentEmitter.ParseColor(box.Border?.EdgeColor) ?? Colors.Black;
        double strokeWidth = box.Border?.EdgeThickness.Points > 0 ? box.Border.EdgeThickness.Points : 0.75;

        double size = System.Math.Min(box.Width, box.Height);
        if (size <= 0)
        {
            size = 10.0;
        }

        page.DrawRectangle(box.X, box.Y, size, size, fill: null, stroke: stroke, strokeWidth: strokeWidth);

        if (box.WidgetChecked != true)
        {
            return;
        }

        if (box.WidgetRound)
        {
            // Radio button (exclGroup member): a filled inner dot. The authoring
            // layer has no circle primitive, so the dot is a small filled square
            // centred in the outline.
            double dot = size * 0.4;
            double inset = (size - dot) / 2.0;
            page.DrawRectangle(
                box.X + inset, box.Y + inset, dot, dot,
                fill: stroke, stroke: null, strokeWidth: 0.0);
            return;
        }

        // Check box: two strokes forming a tick.
        double pad = size * 0.2;
        double left = box.X + pad;
        double right = box.X + size - pad;
        double mid = box.X + (size * 0.42);
        double top = box.Y + pad;
        double bottom = box.Y + size - pad;
        double midY = box.Y + (size * 0.62);

        page.DrawLine(left, midY, mid, bottom, stroke, strokeWidth);
        page.DrawLine(mid, bottom, right, top, stroke, strokeWidth);
    }

    internal static void PaintSignature(PageBuilder page, XfaBox box)
    {
        Color stroke = XfaContentEmitter.ParseColor(box.Border?.EdgeColor) ?? Colors.Gray;
        double strokeWidth = box.Border?.EdgeThickness.Points > 0 ? box.Border.EdgeThickness.Points : 0.5;

        if (box.Width <= 0 || box.Height <= 0)
        {
            return;
        }

        // Field outline plus a signature baseline near the bottom.
        page.DrawRectangle(box.X, box.Y, box.Width, box.Height,
            fill: null, stroke: stroke, strokeWidth: strokeWidth);

        double baselineY = box.Y + (box.Height * 0.75);
        double inset = System.Math.Min(4.0, box.Width * 0.05);
        page.DrawLine(box.X + inset, baselineY, box.Right - inset, baselineY, stroke, strokeWidth);
    }
}
