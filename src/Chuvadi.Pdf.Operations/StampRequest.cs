// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.10.1 (form XObjects), §11.6.4.4 (constant alpha)
// PHASE: Page composition — one stamp in a multi-stamp pass.

using System;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// One stamp for <see cref="PageStamper.PlaceMany"/>: a source page drawn onto
/// a target page under an affine transform, over or under the existing
/// content, at a constant opacity.
/// </summary>
public sealed class StampRequest
{
    /// <summary>Initializes a stamp request.</summary>
    /// <param name="targetPageIndex">Zero-based page of the target document to stamp.</param>
    /// <param name="source">The document holding the stamp page.</param>
    /// <param name="sourcePageIndex">Zero-based page of <paramref name="source"/> to stamp.</param>
    /// <param name="transform">Maps the source page's space onto the target page.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is null.</exception>
    public StampRequest(int targetPageIndex, PdfDocument source, int sourcePageIndex, Transform transform)
    {
        ArgumentNullException.ThrowIfNull(source);
        TargetPageIndex = targetPageIndex;
        Source = source;
        SourcePageIndex = sourcePageIndex;
        Transform = transform;
    }

    /// <summary>Gets the zero-based target page index.</summary>
    public int TargetPageIndex { get; }

    /// <summary>Gets the source document.</summary>
    public PdfDocument Source { get; }

    /// <summary>Gets the zero-based source page index.</summary>
    public int SourcePageIndex { get; }

    /// <summary>Gets the transform from source page space to target page space.</summary>
    public Transform Transform { get; }

    /// <summary>
    /// Gets whether the stamp is drawn over (default) or under the existing content.
    /// </summary>
    public StampPlacement Placement { get; init; } = StampPlacement.Overlay;

    /// <summary>
    /// Gets the constant opacity applied to the whole stamp (fill and stroke),
    /// 0 fully transparent to 1 fully opaque (default). It multiplies with any
    /// transparency already inside the stamp, such as an image's soft mask.
    /// </summary>
    public double Opacity { get; init; } = 1.0;
}
