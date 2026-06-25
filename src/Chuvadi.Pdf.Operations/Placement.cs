// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.3.3-§8.3.4 — transformation matrices.
// PHASE: Page composition — common placement transforms.

using System;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// Builds the affine transforms most often needed when placing a page: scale a
/// source box to fit a destination, centre it, or rotate it by an arbitrary
/// angle. Each returns a <see cref="Transform"/> suitable for
/// <see cref="PageComposer.PlacePage(Chuvadi.Pdf.Documents.PdfDocument, int, Transform)"/> or
/// <see cref="PageStamper"/>; callers
/// who need full control can always supply their own transform instead.
/// </summary>
public static class Placement
{
    /// <summary>
    /// Scales a source box uniformly to fit a destination box (preserving
    /// aspect ratio) and centres it within that destination.
    /// </summary>
    public static Transform ScaleToFit(
        double sourceWidth, double sourceHeight,
        double destinationWidth, double destinationHeight)
    {
        RequirePositive(sourceWidth, nameof(sourceWidth));
        RequirePositive(sourceHeight, nameof(sourceHeight));

        double scale = Math.Min(
            destinationWidth / sourceWidth, destinationHeight / sourceHeight);

        double offsetX = (destinationWidth - sourceWidth * scale) / 2.0;
        double offsetY = (destinationHeight - sourceHeight * scale) / 2.0;

        // Scale first, then translate (this × other applies 'this' first).
        return Transform.CreateScale(scale)
            .Multiply(Transform.CreateTranslation(offsetX, offsetY));
    }

    /// <summary>
    /// Centres a source box within a destination box without scaling.
    /// </summary>
    public static Transform Center(
        double sourceWidth, double sourceHeight,
        double destinationWidth, double destinationHeight)
    {
        return Transform.CreateTranslation(
            (destinationWidth - sourceWidth) / 2.0,
            (destinationHeight - sourceHeight) / 2.0);
    }

    /// <summary>
    /// The bounding-box size of a <paramref name="width"/> ×
    /// <paramref name="height"/> box rotated by <paramref name="degrees"/>.
    /// Pair with <see cref="RotateIntoBox"/> to rotate a page onto a sheet
    /// sized to the result.
    /// </summary>
    public static (double Width, double Height) RotatedSize(
        double degrees, double width, double height)
    {
        double radians = degrees * Math.PI / 180.0;
        double cos = Math.Abs(Math.Cos(radians));
        double sin = Math.Abs(Math.Sin(radians));
        return (width * cos + height * sin, width * sin + height * cos);
    }

    /// <summary>
    /// Rotates a <paramref name="width"/> × <paramref name="height"/> box by
    /// <paramref name="degrees"/> and shifts it so its rotated bounding box
    /// sits at the origin — ready to place on a sheet sized via
    /// <see cref="RotatedSize"/>.
    /// </summary>
    public static Transform RotateIntoBox(double degrees, double width, double height)
    {
        double radians = degrees * Math.PI / 180.0;
        Transform rotate = Transform.CreateRotation(radians);

        // Rotated corners of [0,width] × [0,height]; find the minimum corner.
        double minX = double.MaxValue;
        double minY = double.MaxValue;
        ReadOnlySpan<(double X, double Y)> corners = stackalloc (double, double)[]
        {
            (0, 0), (width, 0), (0, height), (width, height)
        };

        foreach ((double cx, double cy) in corners)
        {
            PointF p = rotate.TransformPoint(new PointF(cx, cy));
            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
        }

        return rotate.Multiply(Transform.CreateTranslation(-minX, -minY));
    }

    /// <summary>
    /// Rotates a box by <paramref name="degrees"/> about its own centre,
    /// keeping the centre fixed (useful for an in-place rotated stamp).
    /// </summary>
    public static Transform RotateAboutCenter(double degrees, double width, double height)
    {
        double cx = width / 2.0;
        double cy = height / 2.0;
        double radians = degrees * Math.PI / 180.0;

        return Transform.CreateTranslation(-cx, -cy)
            .Multiply(Transform.CreateRotation(radians))
            .Multiply(Transform.CreateTranslation(cx, cy));
    }

    private static void RequirePositive(double value, string name)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(name, "Dimension must be positive.");
        }
    }
}
