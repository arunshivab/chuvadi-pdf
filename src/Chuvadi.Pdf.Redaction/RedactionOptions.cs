// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2 — Chuvadi.Pdf.Redaction; extended Phase 1.1.2 with Patterns
// Top-level redaction configuration.

using System.Collections.Generic;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Redaction;

/// <summary>
/// Top-level configuration for a redaction operation.
/// </summary>
public sealed class RedactionOptions
{
    /// <summary>Initialises <see cref="RedactionOptions"/> with default values.</summary>
    public RedactionOptions()
    {
        Rectangles = new List<RedactionRect>();
        Patterns = new List<PatternRule>();
        OverlayColor = ColorF.Black;
        PatternPadding = 1.0;
        MaxDegreeOfParallelism = 1;
    }

    /// <summary>
    /// Gets the list of explicit rectangles to redact, by page.
    /// </summary>
    public IList<RedactionRect> Rectangles { get; init; }

    /// <summary>
    /// Gets the list of regex patterns to redact. Each matching span across
    /// extracted text on a targeted page is resolved to a device-space rectangle
    /// and added to the redaction set.
    /// </summary>
    public IList<PatternRule> Patterns { get; init; }

    /// <summary>
    /// Gets or initialises the colour painted over each redacted rectangle.
    /// Default: opaque black.
    /// </summary>
    public ColorF OverlayColor { get; init; }

    /// <summary>
    /// Gets or initialises the padding (PDF points) added around each pattern-derived
    /// rectangle to compensate for font-metric approximation. Default: 1.0.
    /// </summary>
    public double PatternPadding { get; init; }

    /// <summary>
    /// Gets or initialises the maximum number of threads used for the
    /// per-page content-rewrite stage. Default: <c>1</c> (sequential).
    /// </summary>
    /// <remarks>
    /// Only the pure per-page transforms (the redaction interpreter and overlay
    /// generation) run in parallel; loading and the final object assembly stay
    /// sequential, so the output is byte-for-byte identical to the sequential
    /// path regardless of this value. Use <c>1</c> for deterministic
    /// single-threaded behaviour, a value &gt; 1 to cap the thread count, or
    /// <c>-1</c> to use all available cores.
    /// </remarks>
    public int MaxDegreeOfParallelism { get; init; }
}
