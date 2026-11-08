// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.5.5 (appearance streams), §12.7.2 (AcroForm)
// PHASE: Document operations — annotation/form flattening.

using Chuvadi.Pdf.Documents;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// Options controlling <see cref="AnnotationFlattener"/>: which annotation kinds
/// to bake, whether to strip the AcroForm field tree once its widgets are baked,
/// whether to skip invisible annotations, and whether to drop any annotations
/// left live after baking.
/// </summary>
public sealed class AnnotationFlattenOptions
{
    /// <summary>
    /// Which annotation kinds to bake into page content. Defaults to
    /// <see cref="AnnotationFlattenKinds.All"/>.
    /// </summary>
    public AnnotationFlattenKinds Kinds { get; init; } = AnnotationFlattenKinds.All;

    /// <summary>
    /// When <see cref="AnnotationFlattenKinds.FormFields"/> are flattened and no
    /// widget had to be left live, removes the catalog's <c>/AcroForm</c> entry so
    /// the output is no longer an interactive form. Defaults to <c>true</c>.
    /// </summary>
    public bool RemoveAcroForm { get; init; } = true;

    /// <summary>
    /// Skips baking annotations flagged Hidden or NoView (they paint nothing), and
    /// removes them from the page's <c>/Annots</c>. Defaults to <c>true</c>.
    /// </summary>
    public bool SkipHiddenAndNoView { get; init; } = true;

    /// <summary>
    /// Bakes only the annotations a viewer or a printer would show (3.32.0).
    /// Null (the default) keeps the rule of <see cref="SkipHiddenAndNoView"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="AnnotationMode.Print"/>: only annotations with the Print flag
    /// are baked, as <see cref="AnnotationMode.Print"/> renders them. Hidden and
    /// Popup annotations are not; NoView is ignored, so print-only stamps are
    /// baked. <see cref="AnnotationMode.View"/>: every annotation except Hidden,
    /// NoView and Popup annotations, as the page shows on screen.
    /// </para>
    /// <para>
    /// Annotations of a selected <see cref="Kinds"/> that the mode leaves out are
    /// removed from the page, so the output holds only what was baked. A form
    /// field removed this way loses its value with it. When set, this replaces
    /// <see cref="SkipHiddenAndNoView"/>, which is then ignored.
    /// </para>
    /// <para>
    /// <see cref="AnnotationMode.FormFieldsOnly"/> and
    /// <see cref="AnnotationMode.None"/> have no meaning for flattening;
    /// <see cref="AnnotationFlattener.Flatten(System.IO.Stream, PdfDocument, AnnotationFlattenOptions)"/>
    /// throws <see cref="System.ArgumentException"/> for them.
    /// </para>
    /// </remarks>
    public AnnotationMode? Mode { get; init; }

    /// <summary>
    /// After baking, removes every annotation still live — unselected kinds and
    /// any selected annotation that could not be baked (e.g. links with no
    /// appearance) — leaving a fully static page. Defaults to <c>false</c>, which
    /// keeps those annotations interactive.
    /// </summary>
    public bool DropRemainingAnnotations { get; init; }

    /// <summary>Gets the default options: bake all kinds, strip a fully-baked AcroForm, skip invisible, keep un-baked.</summary>
    public static AnnotationFlattenOptions Default { get; } = new AnnotationFlattenOptions();
}
