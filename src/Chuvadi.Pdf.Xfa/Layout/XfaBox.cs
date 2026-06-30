// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: LA-23b Phase B — positioned layout.

using Chuvadi.Pdf.Xfa.Model;

namespace Chuvadi.Pdf.Xfa.Layout;

/// <summary>
/// A single positioned box produced by the layout engine, expressed in
/// device space (PDF points, origin at the page's top-left, y increasing
/// downward — matching the authoring layer's top-left drawing API).
/// </summary>
public sealed class XfaBox
{
    /// <summary>Initializes a box at the given device-space rectangle.</summary>
    /// <param name="x">Left edge in points from the page left.</param>
    /// <param name="y">Top edge in points from the page top.</param>
    /// <param name="width">Box width in points.</param>
    /// <param name="height">Box height in points.</param>
    public XfaBox(double x, double y, double width, double height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    /// <summary>Gets the left edge in points from the page left.</summary>
    public double X { get; }

    /// <summary>Gets the top edge in points from the page top.</summary>
    public double Y { get; }

    /// <summary>Gets the box width in points.</summary>
    public double Width { get; }

    /// <summary>Gets the box height in points.</summary>
    public double Height { get; }

    /// <summary>Gets the right edge (X + Width) in points.</summary>
    public double Right => X + Width;

    /// <summary>Gets the bottom edge (Y + Height) in points.</summary>
    public double Bottom => Y + Height;

    /// <summary>Gets or sets the text content to render in this box, if any.</summary>
    public string? Text { get; set; }

    /// <summary>Gets or sets the font applied to <see cref="Text"/>, if any.</summary>
    public XfaFont? Font { get; set; }

    /// <summary>Gets or sets the horizontal alignment of the text.</summary>
    public XfaHAlign HAlign { get; set; } = XfaHAlign.Left;

    /// <summary>Gets or sets the vertical alignment of the text.</summary>
    public XfaVAlign VAlign { get; set; } = XfaVAlign.Top;

    /// <summary>Gets or sets the border to stroke and/or fill, if any.</summary>
    public XfaBorder? Border { get; set; }

    /// <summary>Gets or sets the kind of widget this box represents, when it is a field.</summary>
    public XfaUiKind? Widget { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the widget is in the "on" state
    /// (used by check buttons and radios). Null for non-toggle widgets.
    /// </summary>
    public bool? WidgetChecked { get; set; }
}
