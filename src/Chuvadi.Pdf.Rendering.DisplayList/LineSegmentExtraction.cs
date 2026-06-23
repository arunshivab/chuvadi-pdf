// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.5 (path construction & painting)
//
// Flat line-segment view of a page's vector content. Composes the optional-
// content layer membership (RenderOp.Layers) and the raw geometry + CTM
// (PathOp.RawGeometry / PathOp.Ctm) into a single list of straight segments —
// the workhorse accessor for geometry extraction (wall / dimension / center-
// line detection). Domain semantics stay in the consumer; this is generic.

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>
/// A single straight line segment extracted from a page's path content,
/// carrying both page-space and user (model) space endpoints together with the
/// stroke attributes and optional-content layers of the path it came from.
/// </summary>
/// <param name="X0">Page-space start X (CTM applied).</param>
/// <param name="Y0">Page-space start Y (CTM applied).</param>
/// <param name="X1">Page-space end X (CTM applied).</param>
/// <param name="Y1">Page-space end Y (CTM applied).</param>
/// <param name="RawX0">User-space (pre-CTM) start X, as authored.</param>
/// <param name="RawY0">User-space (pre-CTM) start Y, as authored.</param>
/// <param name="RawX1">User-space (pre-CTM) end X, as authored.</param>
/// <param name="RawY1">User-space (pre-CTM) end Y, as authored.</param>
/// <param name="Width">
/// Page-space stroke width of the source path (CTM-scaled), or 0 when the path
/// is fill-only.
/// </param>
/// <param name="Color">
/// Stroke colour for stroked paths, otherwise the fill colour.
/// </param>
/// <param name="Layers">
/// Optional-content (OCG) layer names the source path belongs to, outermost
/// first; empty when the path is not inside any layer. Never null.
/// </param>
/// <param name="Dash">
/// The source path's dash pattern (user-space units) when stroked and dashed,
/// otherwise null. Distinguishes dashed center lines from solid walls.
/// </param>
/// <param name="Mode">Paint mode of the source path (fill / stroke / both).</param>
public readonly record struct LineSegment(
    double X0, double Y0, double X1, double Y1,
    double RawX0, double RawY0, double RawX1, double RawY1,
    double Width, PdfColor Color,
    IReadOnlyList<string> Layers,
    double[]? Dash,
    PaintMode Mode);

/// <summary>
/// Extension accessor that presents a page's path content as a flat list of
/// <see cref="LineSegment"/>s, flattening cubic curves to polylines.
/// </summary>
public static class LineSegmentExtraction
{
    /// <summary>Default curve-flattening tolerance, in user-space units.</summary>
    public const double DefaultFlattenTolerance = 0.25;

    /// <summary>
    /// Extracts every straight segment of every <see cref="PathOp"/> on the page
    /// as a flat list, in draw order. Cubic curves are subdivided to within
    /// <paramref name="flattenTolerance"/>; closed subpaths contribute their
    /// closing segment, so wall loops close. Each segment carries the source
    /// path's page-space and user-space endpoints, stroke width/colour/dash,
    /// optional-content layers, and paint mode.
    /// </summary>
    /// <param name="list">The page display list to read.</param>
    /// <param name="flattenTolerance">
    /// Maximum chord deviation when flattening curves, in user-space units.
    /// Non-positive values fall back to <see cref="DefaultFlattenTolerance"/>.
    /// </param>
    /// <returns>All line segments on the page, in draw order. Never null.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="list"/> is null.</exception>
    public static IReadOnlyList<LineSegment> ExtractLineSegments(
        this PageDisplayList list, double flattenTolerance = DefaultFlattenTolerance)
    {
        ArgumentNullException.ThrowIfNull(list);
        double tolerance = flattenTolerance > 0 ? flattenTolerance : DefaultFlattenTolerance;

        List<LineSegment> segments = new List<LineSegment>();
        foreach (RenderOp op in list)
        {
            if (op is PathOp path)
            {
                AppendSegments(path, tolerance, segments);
            }
        }

        return segments;
    }

    // Flattens one path and appends its segments. When raw geometry is retained
    // the path is flattened in user space and each point mapped through the CTM
    // for the page-space endpoint (Ctm.Apply(raw) == baked); otherwise only the
    // baked page-space geometry is available and raw == page.
    private static void AppendSegments(PathOp path, double tolerance, List<LineSegment> output)
    {
        bool hasRaw = path.RawGeometry is not null;
        PathGeometry source = hasRaw ? path.RawGeometry! : path.Geometry;
        AffineMatrix ctm = path.Ctm;

        bool stroked = path.Mode != PaintMode.Fill;
        double width = stroked && path.Stroke is not null ? path.Stroke.LineWidth : 0.0;
        PdfColor color = stroked ? path.StrokeColor : path.FillColor;
        double[]? dash = path.Stroke?.DashArray;
        IReadOnlyList<string> layers = path.Layers;
        PaintMode mode = path.Mode;

        foreach (IReadOnlyList<(double X, double Y)> polyline in source.Flatten(tolerance))
        {
            for (int i = 0; i + 1 < polyline.Count; i++)
            {
                (double rx0, double ry0) = polyline[i];
                (double rx1, double ry1) = polyline[i + 1];
                (double px0, double py0) = hasRaw ? ctm.Apply(rx0, ry0) : (rx0, ry0);
                (double px1, double py1) = hasRaw ? ctm.Apply(rx1, ry1) : (rx1, ry1);

                output.Add(new LineSegment(
                    px0, py0, px1, py1,
                    rx0, ry0, rx1, ry1,
                    width, color, layers, dash, mode));
            }
        }
    }
}
