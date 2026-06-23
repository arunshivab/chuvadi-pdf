// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 3 — vector extraction accessors.
//
// Axis-aligned bounding boxes for drawing ops, in page space. Path and clip bounds
// come from the flattened geometry; image bounds are the AABB of the unit square
// mapped through the placement transform. TryGetBounds gives a polymorphic view for
// broad-phase spatial culling over a whole display list (null for ops that paint no
// bounded region, such as transform/opacity/blend-mode markers).

using System;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>Axis-aligned bounds accessors for <see cref="RenderOp"/> subtypes.</summary>
public static class RenderOpBounds
{
    private const double DefaultTolerance = 0.25;

    /// <summary>The tight page-space bounds of a path's flattened geometry.</summary>
    /// <param name="op">The path op.</param>
    /// <param name="tolerance">Curve-flattening tolerance.</param>
    /// <returns>The bounding box.</returns>
    public static Rect Bounds(this PathOp op, double tolerance = DefaultTolerance)
    {
        ArgumentNullException.ThrowIfNull(op);
        return op.Geometry.Bounds(tolerance);
    }

    /// <summary>The tight page-space bounds of a clip path's flattened geometry.</summary>
    /// <param name="op">The clip op.</param>
    /// <param name="tolerance">Curve-flattening tolerance.</param>
    /// <returns>The bounding box.</returns>
    public static Rect Bounds(this ClipOp op, double tolerance = DefaultTolerance)
    {
        ArgumentNullException.ThrowIfNull(op);
        return op.Geometry.Bounds(tolerance);
    }

    /// <summary>
    /// The page-space bounds of an image: the AABB of the unit square mapped
    /// through <see cref="ImageOp.Transform"/>.
    /// </summary>
    /// <param name="op">The image op.</param>
    /// <returns>The bounding box.</returns>
    public static Rect Bounds(this ImageOp op)
    {
        ArgumentNullException.ThrowIfNull(op);

        (double X, double Y) c0 = op.Transform.Apply(0, 0);
        (double X, double Y) c1 = op.Transform.Apply(1, 0);
        (double X, double Y) c2 = op.Transform.Apply(1, 1);
        (double X, double Y) c3 = op.Transform.Apply(0, 1);

        double minX = Math.Min(Math.Min(c0.X, c1.X), Math.Min(c2.X, c3.X));
        double maxX = Math.Max(Math.Max(c0.X, c1.X), Math.Max(c2.X, c3.X));
        double minY = Math.Min(Math.Min(c0.Y, c1.Y), Math.Min(c2.Y, c3.Y));
        double maxY = Math.Max(Math.Max(c0.Y, c1.Y), Math.Max(c2.Y, c3.Y));

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>
    /// The page-space bounds of any op that paints a bounded region (path, clip,
    /// or image), or null for ops that do not.
    /// </summary>
    /// <param name="op">The op.</param>
    /// <param name="tolerance">Curve-flattening tolerance for geometry ops.</param>
    /// <returns>The bounding box, or null.</returns>
    public static Rect? TryGetBounds(this RenderOp op, double tolerance = DefaultTolerance)
    {
        ArgumentNullException.ThrowIfNull(op);

        return op switch
        {
            PathOp path => path.Bounds(tolerance),
            ImageOp image => image.Bounds(),
            ClipOp clip => clip.Bounds(tolerance),
            _ => null,
        };
    }
}
