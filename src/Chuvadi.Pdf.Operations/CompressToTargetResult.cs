// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.4 (filters).

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// The outcome of a <see cref="PdfCompressor.CompressToTarget"/> call.
/// </summary>
public sealed record CompressToTargetResult
{
    /// <summary>The size, in bytes, of the document written to the output stream.</summary>
    public long FinalSize { get; init; }

    /// <summary>
    /// The JPEG quality used for the written output. Zero when the document was
    /// not recompressed (for example a skipped signed or encrypted document).
    /// </summary>
    public int QualityUsed { get; init; }

    /// <summary>
    /// <see langword="true"/> when the written output is at or below the target
    /// size; <see langword="false"/> when even the lowest quality exceeded it
    /// (the smallest achievable output is written regardless).
    /// </summary>
    public bool TargetMet { get; init; }

    /// <summary>
    /// The reason compression was skipped, or <see cref="CompressionSkipReason.None"/>.
    /// When skipped, the original document is re-serialized to the output unchanged.
    /// </summary>
    public CompressionSkipReason SkipReason { get; init; }
}
