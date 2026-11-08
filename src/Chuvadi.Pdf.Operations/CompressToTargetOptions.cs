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

    /// <summary>
    /// Switches the target mode to named levels (3.32.0, D-243, D-269): when
    /// set, the steps up to and including this level are tried in order and
    /// the first output at or below the target is written. Null (the default)
    /// keeps the JPEG-quality search described on this type, unchanged.
    /// </summary>
    /// <remarks>
    /// <para>The steps, in order (the colour image levels resolution first):</para>
    /// <list type="number">
    /// <item><see cref="PdfCompressionLevel.Lossless"/></item>
    /// <item><see cref="PdfCompressionLevel.Medium"/></item>
    /// <item><see cref="PdfCompressionLevel.High"/></item>
    /// <item><see cref="PdfCompressionLevel.ReducedColor"/> at 150 dpi</item>
    /// <item><see cref="PdfCompressionLevel.BlackAndWhiteWithColor"/> at 150 dpi</item>
    /// <item><see cref="PdfCompressionLevel.ReducedColor"/> at 130 dpi</item>
    /// <item><see cref="PdfCompressionLevel.BlackAndWhiteWithColor"/> at 130 dpi</item>
    /// <item><see cref="PdfCompressionLevel.BlackAndWhite"/> at 150 dpi</item>
    /// <item><see cref="PdfCompressionLevel.BlackAndWhite"/> at 130 dpi</item>
    /// </list>
    /// <para>
    /// Steps past <see cref="MaxLevel"/> are left out. A step can be larger
    /// than the one before it (reduced colour at 130 dpi is usually larger
    /// than black and white with colour at 150 dpi); the first that fits is
    /// still the one written.
    /// </para>
    /// <para>
    /// The search never goes below 130 dpi (<see cref="ImageLevelOptions.ReadableDpi"/>)
    /// unless <see cref="ImageOptions"/> allows it; then black and white steps
    /// down from 130 dpi by 15% each time to <see cref="ImageLevelOptions.Dpi"/>,
    /// and an output written below 130 dpi is flagged in
    /// <see cref="CompressToTargetResult.BelowReadableResolution"/>.
    /// When nothing fits, the smallest output is written and
    /// <see cref="CompressToTargetResult.TargetMet"/> is false.
    /// </para>
    /// <para>
    /// With a level set, <see cref="MinQuality"/>, <see cref="MaxQuality"/>,
    /// <see cref="AllowRasterizeFallback"/>, <see cref="RasterizeDpi"/> and
    /// <see cref="MinRasterizeDpi"/> are not used; <see cref="BaseOptions"/>
    /// still supplies stripping and rewrite-hazard opt-ins.
    /// </para>
    /// </remarks>
    public PdfCompressionLevel? MaxLevel { get; init; }

    /// <summary>
    /// Fills and the below-130-dpi leave for the image levels in the level
    /// search (3.32.0, D-270); null uses the defaults (dot-pattern fills,
    /// never below 130 dpi). See <see cref="MaxLevel"/> for how
    /// <see cref="ImageLevelOptions.Dpi"/> is used here.
    /// </summary>
    public ImageLevelOptions? ImageOptions { get; init; }
}
