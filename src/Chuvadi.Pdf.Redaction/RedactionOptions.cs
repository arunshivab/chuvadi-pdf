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
        DrawOverlay = true;
        PatternPadding = 1.0;
        MaxDegreeOfParallelism = 1;
        VectorMode = VectorRedactionMode.SubtractPrecise;
    }

    /// <summary>
    /// Gets the list of explicit rectangles to redact, by page.
    /// </summary>
    public IList<RedactionRect> Rectangles { get; init; }

    /// <summary>
    /// Gets the list of regex patterns to redact. Each pattern is matched on
    /// the text a viewer shows on each page it applies to, joined across text
    /// operators and including form XObjects, tiling-pattern cells, Type 3
    /// glyphs and annotations (3.35.0). Each match becomes boxes inside the
    /// matched glyphs, removed as rectangles are; patterns with no page list
    /// are also applied outside the pages (bookmarks, metadata, scripts,
    /// addresses, alternate texts, attachments). See
    /// <see cref="Redactor.ApplyWithReport"/>.
    /// </summary>
    public IList<PatternRule> Patterns { get; init; }

    /// <summary>
    /// Gets or initialises the colour painted over each redacted rectangle.
    /// Default: opaque black.
    /// </summary>
    public ColorF OverlayColor { get; init; }

    /// <summary>
    /// Gets or initialises whether an overlay box is painted over each redacted
    /// region. Default: <see langword="true"/>. Set to <see langword="false"/>
    /// (or supply a fully transparent <see cref="OverlayColor"/>) for boxless
    /// redaction: the matched glyphs are physically removed and no box is drawn,
    /// so the page reads as clean where the text was.
    /// </summary>
    public bool DrawOverlay { get; init; }

    /// <summary>
    /// Gets or initialises a padding in PDF points. Default: 1.0.
    /// </summary>
    /// <remarks>
    /// Not used: pattern matches are placed from the font's own glyph
    /// positions, inside the matched glyphs, so no padding is needed and none
    /// is applied. (Documented in 3.35.0; the value had no effect before
    /// either.)
    /// </remarks>
    public double PatternPadding { get; init; }

    /// <summary>
    /// Gets or initialises how painted vector paths (fills and strokes) that
    /// intersect a redaction region are removed. Default:
    /// <see cref="VectorRedactionMode.SubtractPrecise"/> — geometry whose ink
    /// provably misses every redaction rectangle survives, and only the
    /// intersecting geometry is removed (byte-level). Use
    /// <see cref="VectorRedactionMode.DropIntersecting"/> for the pre-3.18
    /// whole-path drop.
    /// </summary>
    public VectorRedactionMode VectorMode { get; init; }

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

    /// <summary>
    /// Gets or initialises whether, in a pattern redaction, attachments
    /// (embedded files) that cannot be checked against the patterns are kept
    /// (3.35.0). Default: <see langword="false"/>: an attachment is kept only
    /// when every file in it reads as text and neither the text nor its name
    /// matches a document-wide pattern; any other attachment is removed,
    /// because it can hold the very data being redacted, and is listed in
    /// <see cref="RedactionReport.AttachmentsRemoved"/>. Set to
    /// <see langword="true"/> to keep the attachments that could not be
    /// checked; they are listed in <see cref="RedactionReport.AttachmentsNotScanned"/>.
    /// An attachment that matches a pattern is removed either way. With no
    /// patterns at all (rectangles only) attachments are always kept and
    /// listed in <see cref="RedactionReport.AttachmentsNotScanned"/>, as there
    /// is nothing to check them against.
    /// </summary>
    public bool KeepAttachments { get; init; }
}
