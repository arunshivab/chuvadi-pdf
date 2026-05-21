// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: v2.0.0 R2 — SVG renderer

namespace Chuvadi.Pdf.Svg;

/// <summary>
/// Strategy for representing fonts in the emitted SVG.
/// </summary>
public enum FontEmbedding
{
    /// <summary>
    /// Each glyph is emitted as a vector outline (<c>&lt;path&gt;</c> or
    /// <c>&lt;use&gt;</c> referencing a shared <c>&lt;defs&gt;</c> entry).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Renders identically in every SVG viewer because no font file lookup
    /// is involved. Trades file size — every distinct glyph outline costs
    /// a path block — for portability. Glyph deduplication (identical
    /// outlines share a single <c>&lt;defs&gt;</c> entry) keeps the size
    /// reasonable for body text.
    /// </para>
    /// <para>
    /// This is the default and the only mode supported in v2.0.0.
    /// </para>
    /// </remarks>
    GlyphPaths = 0,

    /// <summary>
    /// Fonts are embedded as WOFF2 data URIs in a CSS <c>@font-face</c>
    /// block, and text is emitted as <c>&lt;text&gt;</c> elements.
    /// </summary>
    /// <remarks>
    /// Reserved for a future release. The WOFF2 container packer is on the
    /// v2.0.x roadmap. Selecting this value in v2.0.0 throws
    /// <see cref="System.NotSupportedException"/> from
    /// <see cref="SvgRenderer.RenderPage(Chuvadi.Pdf.Documents.PdfDocument, int)"/>.
    /// </remarks>
    Woff2DataUri = 1,
}
