// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.27 — warnings for fallbacks (D-62, D-63); 3.34.0 ImageSkipped

namespace Chuvadi.Pdf.Primitives;

/// <summary>What a <see cref="PdfWarning"/> reports.</summary>
public enum PdfWarningKind
{
    /// <summary>
    /// An embedded font program could not be read; the text is drawn with
    /// the substitute used for a font that is not embedded.
    /// </summary>
    FontSubstituted,

    /// <summary>A glyph outline in an embedded font is damaged; that glyph draws nothing.</summary>
    GlyphSkipped,

    /// <summary>A shading could not be used (unsupported type or damaged entries); its paint is skipped.</summary>
    ShadingSkipped,

    /// <summary>
    /// An annotation needed an appearance made from its own entries (a form
    /// field with <c>/NeedAppearances</c>, or an annotation with no
    /// <c>/AP</c>), and it could not be made; the annotation draws nothing
    /// (3.31).
    /// </summary>
    AppearanceNotGenerated,

    /// <summary>
    /// A stream's data could not be decoded (for example an invalid Flate
    /// stream); it is treated as damaged and what it holds (an image, a form,
    /// the objects of an object stream) is skipped. The reason says why
    /// (3.32.0).
    /// </summary>
    StreamDamaged,

    /// <summary>
    /// A content stream operator could not be used and is skipped, for
    /// example a path operator with no current point (no <c>m</c> before
    /// it); the subject names the operator (3.33.0).
    /// </summary>
    ContentOperatorSkipped,

    /// <summary>
    /// An image could not be drawn and is skipped: its format is not
    /// supported yet (JPEG 2000), its data could not be decoded as an image,
    /// its colour space or bit depth is not supported, or its <c>/Mask</c>
    /// could not be read (the image is then left out rather than drawn
    /// unmasked). The reason says which (3.34.0).
    /// </summary>
    ImageSkipped,
}
