// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.4 (filters); image recompression quality search.

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// Options for <see cref="PdfCompressor.CompressToTarget"/>. The compressor
/// binary-searches JPEG quality between <see cref="MinQuality"/> and
/// <see cref="MaxQuality"/> for the highest quality whose output fits the
/// target size; <see cref="BaseOptions"/> supplies all other compression knobs
/// (stripping, rewrite hazards). Image recompression is always enabled during
/// the search, since quality only affects size when images are re-encoded.
/// </summary>
public sealed record CompressToTargetOptions
{
    /// <summary>The lowest JPEG quality (1-100) the search will try. Default 30.</summary>
    public int MinQuality { get; init; } = 30;

    /// <summary>The highest JPEG quality (1-100) the search will try. Default 90.</summary>
    public int MaxQuality { get; init; } = 90;

    /// <summary>
    /// The base compression options (stripping flags, rewrite-hazard opt-ins).
    /// <see cref="CompressionOptions.RecompressImages"/> and
    /// <see cref="CompressionOptions.JpegQuality"/> are overridden per search step.
    /// </summary>
    public CompressionOptions BaseOptions { get; init; } = new CompressionOptions();

    /// <summary>
    /// Opt-in: when even the lowest JPEG quality cannot meet the target (the
    /// vector-only floor — content streams and fonts dominate and there are no
    /// images left to shrink), rasterize every page to a JPEG image and write
    /// an image-only PDF, descending from <see cref="RasterizeDpi"/> to
    /// <see cref="MinRasterizeDpi"/> until the target is met. Default
    /// <see langword="false"/>: rasterizing destroys text selection, search
    /// and accessibility, so it never happens silently.
    /// </summary>
    public bool AllowRasterizeFallback { get; init; }

    /// <summary>
    /// The resolution of the rasterize fallback's first attempt. Default 150 DPI.
    /// </summary>
    public double RasterizeDpi { get; init; } = 150;

    /// <summary>
    /// The floor of the rasterize fallback's DPI descent. Default 72 DPI.
    /// </summary>
    public double MinRasterizeDpi { get; init; } = 72;
}
