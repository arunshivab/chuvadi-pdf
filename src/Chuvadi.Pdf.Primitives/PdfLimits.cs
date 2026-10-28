// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.24 — embedding API (A4)

namespace Chuvadi.Pdf.Primitives;

/// <summary>
/// Resource limits for opening, rendering and extracting untrusted PDFs.
/// Every limit is off (null) by default. A limit that would be passed throws
/// <see cref="PdfLimitExceededException"/> naming it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="MaxInputBytes"/> and <see cref="MaxPageCount"/> are checked when
/// a document is opened with them (<c>PdfOpenOptions.Limits</c>). The others
/// are checked while an operation runs inside a <see cref="PdfWorkScope"/>:
/// <c>PageRasterizer</c> and <c>TextExtractor</c> open one per call with the
/// limits the document was opened with, and callers may open their own around
/// any other work.
/// </para>
/// <para>
/// <see cref="MaxMemoryBytes"/> is an accounting ceiling over the large
/// buffers Chuvadi allocates during one operation (decoded streams, decoded
/// images, output pixel buffers), not a cap on the process: small objects,
/// parsed object graphs and caches are not counted. Choose it with headroom.
/// </para>
/// </remarks>
public sealed class PdfLimits
{
    /// <summary>Gets the largest input file accepted, in bytes. Null: no limit.</summary>
    public long? MaxInputBytes { get; init; }

    /// <summary>Gets the most pages a document may have. Null: no limit.</summary>
    public int? MaxPageCount { get; init; }

    /// <summary>
    /// Gets the most bytes of large buffers (decoded streams, decoded images,
    /// output pixels) one operation may allocate in total. Null: no limit.
    /// </summary>
    public long? MaxMemoryBytes { get; init; }

    /// <summary>
    /// Gets the largest size any one stream may decode to, in bytes (guards
    /// against decompression bombs). Null: no limit.
    /// </summary>
    public long? MaxDecodedStreamBytes { get; init; }

    /// <summary>
    /// Gets the most content-stream operators one operation may process,
    /// counting forms, patterns, Type 3 glyphs and annotation appearances it
    /// draws (page complexity). Null: no limit.
    /// </summary>
    public long? MaxContentOperators { get; init; }

    /// <summary>
    /// Gets the most pixels one output raster may have (width times height).
    /// Null: no limit.
    /// </summary>
    public long? MaxOutputPixels { get; init; }
}
