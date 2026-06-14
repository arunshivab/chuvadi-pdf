// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2.1 — glyph-level text positioning

using System.Collections.Generic;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>Reading direction of a <see cref="TextRun"/>.</summary>
public enum TextDirection
{
    /// <summary>Left-to-right (Latin, etc.).</summary>
    LeftToRight = 0,
    /// <summary>Right-to-left (Arabic, Hebrew).</summary>
    RightToLeft = 1,
    /// <summary>Top-to-bottom (CJK vertical).</summary>
    TopToBottom = 2,
}

/// <summary>An axis-aligned bounding rectangle in PDF user-space coords.</summary>
public readonly record struct Rect(double X, double Y, double Width, double Height)
{
    /// <summary>The right edge (X + Width).</summary>
    public double Right => X + Width;
    /// <summary>The top edge (Y + Height).</summary>
    public double Top => Y + Height;
}

/// <summary>The position of a single glyph in a <see cref="TextRun"/>.</summary>
public readonly record struct GlyphPosition(double X, double Y, double Advance, string Unicode);

/// <summary>
/// A contiguous run of text on a page, with glyph-level positions for
/// selection-overlay use cases.
/// </summary>
public sealed class TextRun
{
    /// <summary>Initialises a text run.</summary>
    public TextRun(
        string unicode,
        Rect boundingBox,
        IReadOnlyList<GlyphPosition> glyphs,
        TextDirection direction,
        int readingOrderIndex,
        string fontFamily,
        int fontWeight,
        FontSlant slant,
        double fontSize)
    {
        Unicode = unicode;
        BoundingBox = boundingBox;
        Glyphs = glyphs;
        Direction = direction;
        ReadingOrderIndex = readingOrderIndex;
        FontFamily = fontFamily;
        FontWeight = fontWeight;
        Slant = slant;
        FontSize = fontSize;
    }

    /// <summary>The logical character sequence (concatenation of glyph Unicodes).</summary>
    public string Unicode { get; }

    /// <summary>Bounding box of the run in PDF user-space coords.</summary>
    public Rect BoundingBox { get; }

    /// <summary>Per-glyph positions.</summary>
    public IReadOnlyList<GlyphPosition> Glyphs { get; }

    /// <summary>Reading direction.</summary>
    public TextDirection Direction { get; }

    /// <summary>Monotonic 0-based reading-order index within the page.</summary>
    public int ReadingOrderIndex { get; }

    /// <summary>Resolved font family (subset tag and style suffix stripped).</summary>
    public string FontFamily { get; }

    /// <summary>CSS-style numeric weight (400 normal, 700 bold).</summary>
    public int FontWeight { get; }

    /// <summary>Slant classification (normal, italic, or oblique).</summary>
    public FontSlant Slant { get; }

    /// <summary>Effective font size of the run in user-space points.</summary>
    public double FontSize { get; }
}
