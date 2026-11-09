// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ISO 32000-1 §12.7.4.5 (signature fields) + §12.5.2 (annotations)
// PHASE: Phase 1.2.6 — signature appearances


namespace Chuvadi.Pdf.Signatures.Signing;

/// <summary>
/// Visible appearance for a signature field. When supplied on
/// <see cref="PdfSigningOptions.Appearance"/>, the signature field is
/// also marked as a Widget annotation, placed on a specific page within
/// the given rectangle, and gets an appearance stream that PDF readers
/// render in the page view.
/// </summary>
/// <remarks>
/// <para>
/// A signature is cryptographically complete without an appearance,
/// and most automated workflows don't need one. Visible appearances
/// matter for human-reviewed documents (contracts, agreements) where a
/// reader expects to see "signed by …" on the page.
/// </para>
/// <para>
/// Since 3.32.0 Chuvadi draws a default appearance: the lines of
/// <see cref="Text"/> (by default "Digitally signed by {signer's common
/// name}" and "Date: {signing time}", in UTC, ISO 8601), sized to fit the
/// box, with <see cref="Image"/> on the left third when one is given, and a
/// thin black border. Text in a script Helvetica cannot write is drawn with
/// the matching LiPi Sans face, embedded as a subset. Before 3.32.0 the
/// default was the border alone.
/// </para>
/// <para>
/// When <see cref="PreRenderedAppearanceStream"/> is supplied, those bytes are
/// used verbatim as the Form XObject content stream instead, and the caller is
/// responsible for whatever fonts / colors / images they reference;
/// <see cref="Text"/> and <see cref="Image"/> are then not used.
/// </para>
/// </remarks>
public sealed class SignatureAppearance
{
    /// <summary>Zero-based index of the page on which to place the widget.</summary>
    public required int PageIndex { get; init; }

    /// <summary>
    /// Rectangle in default user space: [llx, lly, urx, ury].
    /// </summary>
    public required double[] Rectangle { get; init; }

    /// <summary>
    /// Optional pre-rendered Form XObject content stream. When null, the
    /// default appearance is generated (see the class remarks). When given,
    /// it wins: <see cref="Text"/> and <see cref="Image"/> are not used.
    /// </summary>
    public byte[]? PreRenderedAppearanceStream { get; init; }

    /// <summary>
    /// The text of the default appearance (3.32.0); <c>\n</c> separates lines.
    /// Null (the default) gives "Digitally signed by {signer's common name}"
    /// and "Date: {signing time}" (UTC, ISO 8601, e.g. 2026-11-07T10:30:00Z).
    /// An empty string draws no text, for an image-only appearance.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// A JPEG or PNG image for the default appearance (3.32.0): drawn on the
    /// left third of the box, beside the text, or filling the box when
    /// <see cref="Text"/> is empty. It keeps its aspect ratio; PNG
    /// transparency is kept.
    /// </summary>
    /// <remarks>
    /// Signing throws <see cref="System.ArgumentException"/> when the bytes are
    /// not a JPEG or PNG, or are a PNG that cannot be embedded.
    /// </remarks>
    public byte[]? Image { get; init; }
}
