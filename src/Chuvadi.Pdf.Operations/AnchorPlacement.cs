// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.4.2 (text space), §8.3.3 (CTM)
// PHASE: Document operations — anchor placement math for stamps.

using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// Computes the text matrix (Tm) that places a measured line of text at a given
/// <see cref="StampAnchor"/> within a page's media box, honouring inward
/// margins. Edge anchors rotate the text 90°: left edge reads upward, right
/// edge reads downward.
/// </summary>
internal static class AnchorPlacement
{
    // Approximate cap-height fraction of font size, used to vertically seat
    // text within the top/bottom margins so the glyphs sit inside the band.
    private const double AscentFraction = 0.72;

    internal static Transform ComputePlacement(
        StampAnchor anchor,
        PdfRectangle mediaBox,
        double textWidth,
        double fontSize,
        double marginX,
        double marginY)
    {
        double left = mediaBox.X1;
        double bottom = mediaBox.Y1;
        double right = mediaBox.X2;
        double top = mediaBox.Y2;
        double pageW = mediaBox.Width;
        double pageH = mediaBox.Height;
        double ascent = fontSize * AscentFraction;

        switch (anchor)
        {
            case StampAnchor.TopLeft:
                return Horizontal(left + marginX, top - marginY - ascent);

            case StampAnchor.TopCenter:
                return Horizontal(left + (pageW - textWidth) / 2.0, top - marginY - ascent);

            case StampAnchor.TopRight:
                return Horizontal(right - marginX - textWidth, top - marginY - ascent);

            case StampAnchor.BottomLeft:
                return Horizontal(left + marginX, bottom + marginY);

            case StampAnchor.BottomCenter:
                return Horizontal(left + (pageW - textWidth) / 2.0, bottom + marginY);

            case StampAnchor.BottomRight:
                return Horizontal(right - marginX - textWidth, bottom + marginY);

            // Left edge, text reading upward (rotate +90°). The text baseline
            // runs up the page; x is fixed near the left margin.
            case StampAnchor.LeftEdgeBottom:
                return RotatedCcw(left + marginX + ascent, bottom + marginY);

            case StampAnchor.LeftEdgeMiddle:
                return RotatedCcw(left + marginX + ascent, bottom + (pageH - textWidth) / 2.0);

            case StampAnchor.LeftEdgeTop:
                return RotatedCcw(left + marginX + ascent, top - marginY - textWidth);

            // Right edge, text reading downward (rotate -90°).
            case StampAnchor.RightEdgeTop:
                return RotatedCw(right - marginX - ascent, top - marginY);

            case StampAnchor.RightEdgeMiddle:
                return RotatedCw(right - marginX - ascent, top - (pageH - textWidth) / 2.0);

            case StampAnchor.RightEdgeBottom:
                return RotatedCw(right - marginX - ascent, bottom + marginY + textWidth);

            default:
                return Horizontal(left + marginX, bottom + marginY);
        }
    }

    private static Transform Horizontal(double x, double y)
    {
        return new Transform(1, 0, 0, 1, x, y);
    }

    // +90° rotation: [0 1 -1 0 x y]. Text advances in +y (upward).
    private static Transform RotatedCcw(double x, double y)
    {
        return new Transform(0, 1, -1, 0, x, y);
    }

    // -90° rotation: [0 -1 1 0 x y]. Text advances in -y (downward).
    private static Transform RotatedCw(double x, double y)
    {
        return new Transform(0, -1, 1, 0, x, y);
    }
}
