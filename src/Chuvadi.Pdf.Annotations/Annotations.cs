// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.5.2 — Annotation dictionaries
//        PDF 32000-1:2008 §12.5.6 — Annotation types
// PHASE: Phase 1.1 — Chuvadi.Pdf.Annotations (v2.0.0 R3 adds ShapeAnnotation)
// Immutable annotation model classes.

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Annotations;

/// <summary>
/// Base class for all modelled annotations. Carries the fields shared by every
/// annotation subtype per PDF 32000-1:2008 §12.5.2.
/// </summary>
public abstract class PdfAnnotation
{
    /// <summary>Initialises base annotation fields.</summary>
    protected PdfAnnotation(
        AnnotationType type,
        int pageIndex,
        RectangleF rect,
        string? contents,
        ColorF? color,
        string? author,
        float opacity)
    {
        if (pageIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageIndex));
        }

        Type = type;
        PageIndex = pageIndex;
        Rect = rect;
        Contents = contents;
        Color = color;
        Author = author;
        Opacity = opacity;
    }

    /// <summary>Gets the annotation subtype.</summary>
    public AnnotationType Type { get; }

    /// <summary>Gets the zero-based page index the annotation lives on.</summary>
    public int PageIndex { get; }

    /// <summary>Gets the rectangle on the page in PDF user space.</summary>
    public RectangleF Rect { get; }

    /// <summary>
    /// Gets the contents string (the text for Text and FreeText annotations;
    /// the alternative description for graphical annotations).
    /// </summary>
    public string? Contents { get; }

    /// <summary>Gets the annotation colour, or null for the viewer default.</summary>
    public ColorF? Color { get; }

    /// <summary>Gets the annotation author / title (PDF /T).</summary>
    public string? Author { get; }

    /// <summary>Gets the opacity 0..1 (PDF /CA). Default 1.0.</summary>
    public float Opacity { get; }
}

// ── Modelled subtypes ─────────────────────────────────────────────────────

/// <summary>Sticky-note text annotation (§12.5.6.4).</summary>
public sealed class TextAnnotation : PdfAnnotation
{
    /// <summary>Initialises a sticky-note annotation.</summary>
    public TextAnnotation(
        int pageIndex,
        RectangleF rect,
        string contents,
        string iconName = "Note",
        bool isOpen = false,
        ColorF? color = null,
        string? author = null,
        float opacity = 1f)
        : base(AnnotationType.Text, pageIndex, rect, contents, color, author, opacity)
    {
        IconName = iconName ?? throw new ArgumentNullException(nameof(iconName));
        IsOpen = isOpen;
    }

    /// <summary>
    /// Gets the icon name. Standard values: Comment, Key, Note, Help, NewParagraph,
    /// Paragraph, Insert. Default: Note.
    /// </summary>
    public string IconName { get; }

    /// <summary>Gets whether the annotation pops open by default.</summary>
    public bool IsOpen { get; }
}

/// <summary>
/// Hyperlink annotation (§12.5.6.5). Targets either a URI or a page in the
/// same document.
/// </summary>
public sealed class LinkAnnotation : PdfAnnotation
{
    /// <summary>Initialises a link to an external URI.</summary>
    public LinkAnnotation(int pageIndex, RectangleF rect, Uri uri, string? contents = null)
        : base(AnnotationType.Link, pageIndex, rect, contents, color: null, author: null, opacity: 1f)
    {
        Uri = uri ?? throw new ArgumentNullException(nameof(uri));
        DestinationPageIndex = -1;
    }

    /// <summary>Initialises a link to a destination page in the same document.</summary>
    public LinkAnnotation(int pageIndex, RectangleF rect, int destinationPageIndex, string? contents = null)
        : base(AnnotationType.Link, pageIndex, rect, contents, color: null, author: null, opacity: 1f)
    {
        if (destinationPageIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(destinationPageIndex));
        }

        DestinationPageIndex = destinationPageIndex;
        Uri = null;
    }

    /// <summary>Gets the external URI target, or null when the link is internal.</summary>
    public Uri? Uri { get; }

    /// <summary>
    /// Gets the zero-based destination page index for an internal link,
    /// or -1 when the link is external.
    /// </summary>
    public int DestinationPageIndex { get; }
}

/// <summary>Free-text annotation drawn directly on the page (§12.5.6.6).</summary>
public sealed class FreeTextAnnotation : PdfAnnotation
{
    /// <summary>Initialises a free-text annotation.</summary>
    public FreeTextAnnotation(
        int pageIndex,
        RectangleF rect,
        string contents,
        ColorF? color = null,
        string? author = null,
        float opacity = 1f,
        string defaultAppearance = "/Helvetica 12 Tf 0 0 0 rg")
        : base(AnnotationType.FreeText, pageIndex, rect, contents, color, author, opacity)
    {
        DefaultAppearance = defaultAppearance
            ?? throw new ArgumentNullException(nameof(defaultAppearance));
    }

    /// <summary>
    /// Gets the PDF default-appearance string (DA), describing font, size, and colour
    /// using PDF content-stream operators.
    /// </summary>
    public string DefaultAppearance { get; }
}

/// <summary>
/// Text-markup annotation (§12.5.6.10): Highlight, Underline, Squiggly, or
/// StrikeOut. Distinguished by <see cref="PdfAnnotation.Type"/>.
/// </summary>
public sealed class MarkupAnnotation : PdfAnnotation
{
    /// <summary>Initialises a text-markup annotation.</summary>
    public MarkupAnnotation(
        AnnotationType markupType,
        int pageIndex,
        RectangleF rect,
        IReadOnlyList<float> quadPoints,
        string? contents = null,
        ColorF? color = null,
        string? author = null,
        float opacity = 1f)
        : base(ValidateMarkupType(markupType), pageIndex, rect, contents, color, author, opacity)
    {
        if (quadPoints is null)
        {
            throw new ArgumentNullException(nameof(quadPoints));
        }

        if (quadPoints.Count % 8 != 0 || quadPoints.Count == 0)
        {
            throw new ArgumentException(
                "QuadPoints length must be a positive multiple of 8 (four x,y pairs per quad).",
                nameof(quadPoints));
        }

        QuadPoints = quadPoints;
    }

    /// <summary>
    /// Gets the quad-point list. Each group of 8 floats defines a quadrilateral
    /// in the order (x1,y1)…(x4,y4). PDF 32000-1:2008 §12.5.6.10.
    /// </summary>
    public IReadOnlyList<float> QuadPoints { get; }

    private static AnnotationType ValidateMarkupType(AnnotationType t)
    {
        if (t != AnnotationType.Highlight && t != AnnotationType.Underline &&
            t != AnnotationType.Squiggly && t != AnnotationType.StrikeOut)
        {
            throw new ArgumentException(
                $"Markup annotation type must be Highlight, Underline, Squiggly, or StrikeOut; got {t}.",
                nameof(t));
        }

        return t;
    }
}

/// <summary>Rubber-stamp annotation (§12.5.6.12), e.g., "Approved", "Confidential".</summary>
public sealed class StampAnnotation : PdfAnnotation
{
    /// <summary>Initialises a stamp annotation.</summary>
    public StampAnnotation(
        int pageIndex,
        RectangleF rect,
        string stampName,
        string? contents = null,
        ColorF? color = null,
        string? author = null,
        float opacity = 1f)
        : base(AnnotationType.Stamp, pageIndex, rect, contents, color, author, opacity)
    {
        StampName = stampName ?? throw new ArgumentNullException(nameof(stampName));
    }

    /// <summary>
    /// Gets the stamp icon name. Standard values include Approved, Experimental,
    /// NotApproved, AsIs, Expired, NotForPublicRelease, Confidential, Final,
    /// Sold, Departmental, ForComment, TopSecret, Draft, ForPublicRelease.
    /// </summary>
    public string StampName { get; }
}

/// <summary>
/// Free-hand ink annotation (§12.5.6.13). Each stroke is a polyline of points
/// in PDF user space.
/// </summary>
public sealed class InkAnnotation : PdfAnnotation
{
    /// <summary>Initialises an ink annotation.</summary>
    public InkAnnotation(
        int pageIndex,
        RectangleF rect,
        IReadOnlyList<IReadOnlyList<PointF>> strokes,
        string? contents = null,
        ColorF? color = null,
        string? author = null,
        float opacity = 1f)
        : base(AnnotationType.Ink, pageIndex, rect, contents, color, author, opacity)
    {
        if (strokes is null)
        {
            throw new ArgumentNullException(nameof(strokes));
        }

        if (strokes.Count == 0)
        {
            throw new ArgumentException("Ink annotation requires at least one stroke.", nameof(strokes));
        }

        Strokes = strokes;
    }

    /// <summary>
    /// Gets the strokes. Each inner list is one continuous polyline of
    /// (X, Y) points in PDF user space.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<PointF>> Strokes { get; }
}

/// <summary>
/// Distinguishes the geometric subtype of a <see cref="ShapeAnnotation"/>.
/// </summary>
public enum ShapeKind
{
    /// <summary>Axis-aligned rectangle (PDF subtype <c>/Square</c>).</summary>
    Square,

    /// <summary>Ellipse fitting the annotation rect (PDF subtype <c>/Circle</c>).</summary>
    Circle,

    /// <summary>Straight line between two endpoints (PDF subtype <c>/Line</c>).</summary>
    Line,

    /// <summary>Open polyline (PDF subtype <c>/PolyLine</c>).</summary>
    Polyline,

    /// <summary>Closed polygon (PDF subtype <c>/Polygon</c>).</summary>
    Polygon,
}

/// <summary>
/// Shape annotation: <c>/Square</c>, <c>/Circle</c>, <c>/Line</c>,
/// <c>/PolyLine</c>, or <c>/Polygon</c> — PDF 32000-1:2008 §12.5.6.7-9.
/// </summary>
/// <remarks>
/// <para>
/// For <see cref="ShapeKind.Square"/> and <see cref="ShapeKind.Circle"/>
/// the geometry is fully described by <see cref="PdfAnnotation.Rect"/>;
/// <see cref="Vertices"/> is empty.
/// </para>
/// <para>
/// For <see cref="ShapeKind.Line"/> the list contains exactly two
/// endpoints. For <see cref="ShapeKind.Polyline"/> and
/// <see cref="ShapeKind.Polygon"/> it contains the vertex list in PDF
/// user space (Y up, points).
/// </para>
/// </remarks>
public sealed class ShapeAnnotation : PdfAnnotation
{
    /// <summary>Initialises a shape annotation.</summary>
    public ShapeAnnotation(
        ShapeKind kind,
        int pageIndex,
        RectangleF rect,
        IReadOnlyList<PointF> vertices,
        string? contents = null,
        ColorF? color = null,
        string? author = null,
        float opacity = 1f,
        float borderWidth = 1f,
        ColorF? interiorColor = null)
        : base(MapKind(kind), pageIndex, rect, contents, color, author, opacity)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ValidateVertices(kind, vertices);

        Kind = kind;
        Vertices = vertices;
        BorderWidth = borderWidth;
        InteriorColor = interiorColor;
    }

    /// <summary>Gets the geometric subtype of the shape.</summary>
    public ShapeKind Kind { get; }

    /// <summary>
    /// Gets the vertex list in PDF user space. Empty for square and circle
    /// (whose geometry is the <see cref="PdfAnnotation.Rect"/>); two
    /// endpoints for line; three or more for polyline / polygon.
    /// </summary>
    public IReadOnlyList<PointF> Vertices { get; }

    /// <summary>Gets the stroke width of the shape outline in points.</summary>
    public float BorderWidth { get; }

    /// <summary>Gets the interior fill colour, or null for a stroke-only shape.</summary>
    public ColorF? InteriorColor { get; }

    private static AnnotationType MapKind(ShapeKind kind)
    {
        switch (kind)
        {
            case ShapeKind.Square:
                return AnnotationType.Square;
            case ShapeKind.Circle:
                return AnnotationType.Circle;
            case ShapeKind.Line:
                return AnnotationType.Line;
            case ShapeKind.Polyline:
                return AnnotationType.Polyline;
            case ShapeKind.Polygon:
                return AnnotationType.Polygon;
            default:
                throw new ArgumentException($"Unknown shape kind: {kind}.", nameof(kind));
        }
    }

    private static void ValidateVertices(ShapeKind kind, IReadOnlyList<PointF> vertices)
    {
        switch (kind)
        {
            case ShapeKind.Square:
            case ShapeKind.Circle:
                // Geometry is the bounding rect; vertices should be empty.
                break;
            case ShapeKind.Line:
                if (vertices.Count != 2)
                {
                    throw new ArgumentException(
                        "Line annotation requires exactly two endpoints.",
                        nameof(vertices));
                }
                break;
            case ShapeKind.Polyline:
            case ShapeKind.Polygon:
                if (vertices.Count < 2)
                {
                    throw new ArgumentException(
                        "Polyline and polygon annotations require at least two vertices.",
                        nameof(vertices));
                }
                break;
        }
    }
}

/// <summary>
/// Catch-all annotation for subtypes not specifically modelled. Preserves
/// the basic fields so callers can at least see what's on the page.
/// </summary>
public sealed class GenericAnnotation : PdfAnnotation
{
    /// <summary>Initialises a generic (unmodelled-subtype) annotation.</summary>
    public GenericAnnotation(
        int pageIndex,
        RectangleF rect,
        string rawSubtype,
        string? contents = null,
        ColorF? color = null,
        string? author = null,
        float opacity = 1f)
        : base(AnnotationType.Unknown, pageIndex, rect, contents, color, author, opacity)
    {
        RawSubtype = rawSubtype ?? throw new ArgumentNullException(nameof(rawSubtype));
    }

    /// <summary>Gets the raw PDF /Subtype name as it appeared in the document.</summary>
    public string RawSubtype { get; }
}
