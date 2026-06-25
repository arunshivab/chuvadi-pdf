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
}
