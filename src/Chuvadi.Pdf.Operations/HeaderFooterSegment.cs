// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Document operations — resolved header/footer text for previews.

namespace Chuvadi.Pdf.Operations;

/// <summary>Which running band a header/footer segment belongs to.</summary>
public enum HeaderFooterBand
{
    /// <summary>The header band at the top of the page.</summary>
    Header,

    /// <summary>The footer band at the bottom of the page.</summary>
    Footer,
}

/// <summary>Horizontal alignment of a header/footer segment within its band.</summary>
public enum HeaderFooterAlignment
{
    /// <summary>Left segment, inset by the horizontal margin.</summary>
    Left,

    /// <summary>Centre segment, centred on the page width.</summary>
    Center,

    /// <summary>Right segment, inset by the horizontal margin.</summary>
    Right,
}

/// <summary>
/// One header/footer segment exactly as <see cref="HeaderFooter.Apply"/> draws
/// it on a page: token-substituted text plus placement in PDF page
/// coordinates (origin bottom-left, points). Produced by
/// <see cref="HeaderFooter.ResolveSegments"/>.
/// </summary>
public sealed class HeaderFooterSegment
{
    internal HeaderFooterSegment(
        HeaderFooterBand band,
        HeaderFooterAlignment alignment,
        string text,
        double x,
        double baselineY,
        double width,
        double fontSize)
    {
        Band = band;
        Alignment = alignment;
        Text = text;
        X = x;
        BaselineY = baselineY;
        Width = width;
        FontSize = fontSize;
    }

    /// <summary>Gets the band (header or footer).</summary>
    public HeaderFooterBand Band { get; }

    /// <summary>Gets the alignment slot (left, centre, right).</summary>
    public HeaderFooterAlignment Alignment { get; }

    /// <summary>Gets the fully token-substituted text.</summary>
    public string Text { get; }

    /// <summary>Gets the x of the text origin, in page points.</summary>
    public double X { get; }

    /// <summary>Gets the baseline y, in page points (origin bottom-left).</summary>
    public double BaselineY { get; }

    /// <summary>Gets the measured advance width of the text, in points.</summary>
    public double Width { get; }

    /// <summary>Gets the font size, in points.</summary>
    public double FontSize { get; }
}
