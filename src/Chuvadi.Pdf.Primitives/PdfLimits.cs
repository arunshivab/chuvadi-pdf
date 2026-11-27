// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.24 — embedding API (A4); 3.36.0 (D-410 A) — safe defaults on by
//        default, MaxGraphicsStateDepth, and the Unlimited opt-out.

namespace Chuvadi.Pdf.Primitives;

/// <summary>
/// Resource limits for opening, rendering, extracting from and writing
/// untrusted PDFs.
/// </summary>
/// <remarks>
/// <para>
/// Since 3.36.0 the memory, decoded-stream, operator, output-pixel and
/// graphics-state-depth limits are <b>on by default</b> at the safe values of
/// <see cref="Default"/>: an operation run with no limits specified still gets
/// them. Set <c>PdfOpenOptions.Limits</c> (or open a <see cref="PdfWorkScope"/>)
/// to a <see cref="PdfLimits"/> to raise or lower any of them — an instance you
/// build keeps the safe default for every limit you do not set — or to
/// <see cref="Unlimited"/> to turn them all off. A limit that would be passed
/// throws <see cref="PdfLimitExceededException"/> naming it.
/// </para>
/// <para>
/// <see cref="MaxInputBytes"/> and <see cref="MaxPageCount"/> stay off by
/// default (the caller knows its own files) and are checked when a document is
/// opened with them (<c>PdfOpenOptions.Limits</c>). The others are checked
/// while an operation runs inside a <see cref="PdfWorkScope"/>: opening,
/// rendering (raster and SVG), text extraction, and every write/operations
/// path (save, merge, flatten, stamp, redact, PDF/A, ...) open one; callers may
/// open their own around any other work.
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
    /// <summary>256 MiB — the default <see cref="MaxDecodedStreamBytes"/>.</summary>
    public const long DefaultMaxDecodedStreamBytes = 256L << 20;

    /// <summary>2 GiB — the default <see cref="MaxMemoryBytes"/>.</summary>
    public const long DefaultMaxMemoryBytes = 2L << 30;

    /// <summary>200 megapixels — the default <see cref="MaxOutputPixels"/>.</summary>
    public const long DefaultMaxOutputPixels = 200L * 1000 * 1000;

    /// <summary>50 million — the default <see cref="MaxContentOperators"/>.</summary>
    public const long DefaultMaxContentOperators = 50L * 1000 * 1000;

    /// <summary>256 — the default <see cref="MaxGraphicsStateDepth"/>.</summary>
    public const int DefaultMaxGraphicsStateDepth = 256;

    /// <summary>
    /// Gets the safe default limits applied when none are specified (3.36.0):
    /// <see cref="MaxDecodedStreamBytes"/> 256 MiB, <see cref="MaxMemoryBytes"/>
    /// 2 GiB, <see cref="MaxOutputPixels"/> 200 MP, <see cref="MaxContentOperators"/>
    /// 50 million, <see cref="MaxGraphicsStateDepth"/> 256; input size and page
    /// count unlimited.
    /// </summary>
    public static PdfLimits Default { get; } = new PdfLimits();

    /// <summary>
    /// Gets a <see cref="PdfLimits"/> with every limit off — the explicit
    /// opt-out of the 3.36.0 safe defaults. Use it only for input you trust.
    /// </summary>
    public static PdfLimits Unlimited { get; } = new PdfLimits
    {
        MaxMemoryBytes = null,
        MaxDecodedStreamBytes = null,
        MaxContentOperators = null,
        MaxOutputPixels = null,
        MaxGraphicsStateDepth = null,
    };

    /// <summary>Gets the largest input file accepted, in bytes. Null (default): no limit.</summary>
    public long? MaxInputBytes { get; init; }

    /// <summary>Gets the most pages a document may have. Null (default): no limit.</summary>
    public int? MaxPageCount { get; init; }

    /// <summary>
    /// Gets the most bytes of large buffers (decoded streams, decoded images,
    /// output pixels) one operation may allocate in total. Defaults to
    /// <see cref="DefaultMaxMemoryBytes"/> (2 GiB); null for no limit.
    /// </summary>
    public long? MaxMemoryBytes { get; init; } = DefaultMaxMemoryBytes;

    /// <summary>
    /// Gets the largest size any one stream may decode to, in bytes (guards
    /// against decompression bombs). Defaults to
    /// <see cref="DefaultMaxDecodedStreamBytes"/> (256 MiB); null for no limit.
    /// </summary>
    public long? MaxDecodedStreamBytes { get; init; } = DefaultMaxDecodedStreamBytes;

    /// <summary>
    /// Gets the most content-stream operators one operation may process,
    /// counting forms, patterns, Type 3 glyphs and annotation appearances it
    /// draws (page complexity). Defaults to
    /// <see cref="DefaultMaxContentOperators"/> (50 million); null for no limit.
    /// </summary>
    public long? MaxContentOperators { get; init; } = DefaultMaxContentOperators;

    /// <summary>
    /// Gets the most pixels one output raster may have (width times height).
    /// Defaults to <see cref="DefaultMaxOutputPixels"/> (200 MP); null for no
    /// limit.
    /// </summary>
    public long? MaxOutputPixels { get; init; } = DefaultMaxOutputPixels;

    /// <summary>
    /// Gets the deepest the graphics-state stack may nest, counting every
    /// <c>q</c> not yet balanced by a <c>Q</c>, including across the forms,
    /// patterns and Type 3 glyphs a content stream draws (3.36.0, guards
    /// against a stack of a million <c>q</c>s). Defaults to
    /// <see cref="DefaultMaxGraphicsStateDepth"/> (256); null for no limit.
    /// </summary>
    public int? MaxGraphicsStateDepth { get; init; } = DefaultMaxGraphicsStateDepth;
}
