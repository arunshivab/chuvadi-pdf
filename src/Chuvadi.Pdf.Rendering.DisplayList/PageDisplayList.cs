// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2.1 — display-list intermediate
//        v2.1.2 — FontDictsByKey allows downstream renderers to extract
//                 font program bytes for embedding (e.g. SVG @font-face)
//                 without re-resolving resources from the source document.

using System;
using System.Collections;
using System.Collections.Generic;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>
/// A page's content as a neutral, ordered sequence of <see cref="RenderOp"/>s.
/// </summary>
/// <remarks>
/// <para>
/// Built by <see cref="DisplayListBuilder.Build(PdfDocument, int, bool)"/>; consumed by output adapters
/// such as <c>SvgRenderer</c>, <c>WpfRenderer</c>, or future software rasterizers.
/// </para>
/// <para>
/// Pure value-like type: same page bytes, same display list. No rendering side
/// effects.
/// </para>
/// <para>
/// v2.1.2: also exposes the page's font dictionaries keyed by the resource
/// name used in <c>TextOp.FontKey</c>. This allows downstream renderers that
/// want to embed font programs (e.g. <c>SvgRenderer</c> emitting CSS
/// <c>@font-face</c> rules with base64-encoded TrueType data URLs) to access
/// the source font dictionaries without re-walking the page resources or
/// holding a reference to the original <c>PdfDocument</c>.
/// </para>
/// </remarks>
public sealed class PageDisplayList : IReadOnlyList<RenderOp>
{
    private static readonly IReadOnlyDictionary<string, PdfDictionary> EmptyFonts
        = new Dictionary<string, PdfDictionary>(0);

    private static readonly IReadOnlyList<RenderingDiagnostic> EmptyDiagnostics
        = Array.Empty<RenderingDiagnostic>();

    private static readonly IReadOnlyList<OptionalContentGroup> EmptyOcgs
        = Array.Empty<OptionalContentGroup>();

    private readonly IReadOnlyList<RenderOp> _ops;

    /// <summary>
    /// Initialises a display list with the given ops and page metadata, with
    /// no font dictionaries attached. Equivalent to passing an empty
    /// dictionary for the font registry overload.
    /// </summary>
    public PageDisplayList(
        IReadOnlyList<RenderOp> ops,
        double mediaWidth,
        double mediaHeight,
        int rotation)
        : this(ops, mediaWidth, mediaHeight, rotation, EmptyFonts)
    {
    }

    /// <summary>
    /// Initialises a display list with the given ops, page metadata, and the
    /// page's font dictionaries keyed by the resource-name used in
    /// <c>TextOp.FontKey</c> (e.g. <c>"F1"</c>, <c>"TT2"</c>).
    /// </summary>
    public PageDisplayList(
        IReadOnlyList<RenderOp> ops,
        double mediaWidth,
        double mediaHeight,
        int rotation,
        IReadOnlyDictionary<string, PdfDictionary> fontDictsByKey)
        : this(ops, mediaWidth, mediaHeight, rotation, fontDictsByKey, EmptyDiagnostics)
    {
    }

    /// <summary>
    /// Initialises a display list with the given ops, page metadata, font
    /// dictionaries, and the diagnostics accumulated during build
    /// (graceful-degradation events such as a font that could not be
    /// resolved). New in v2.1.8.
    /// </summary>
    public PageDisplayList(
        IReadOnlyList<RenderOp> ops,
        double mediaWidth,
        double mediaHeight,
        int rotation,
        IReadOnlyDictionary<string, PdfDictionary> fontDictsByKey,
        IReadOnlyList<RenderingDiagnostic> diagnostics)
        : this(ops, mediaWidth, mediaHeight, rotation, fontDictsByKey, diagnostics, EmptyOcgs)
    {
    }

    /// <summary>
    /// Initialises a display list with the given ops, page metadata, font
    /// dictionaries, diagnostics, and the document's optional-content groups
    /// (layers) declared in /OCProperties/OCGs. New in v2.3. Individual ops
    /// reference these layers by name through <see cref="RenderOp.Layers"/>.
    /// </summary>
    public PageDisplayList(
        IReadOnlyList<RenderOp> ops,
        double mediaWidth,
        double mediaHeight,
        int rotation,
        IReadOnlyDictionary<string, PdfDictionary> fontDictsByKey,
        IReadOnlyList<RenderingDiagnostic> diagnostics,
        IReadOnlyList<OptionalContentGroup> optionalContentGroups)
    {
        _ops = ops ?? throw new ArgumentNullException(nameof(ops));
        ArgumentNullException.ThrowIfNull(fontDictsByKey);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(optionalContentGroups);
        MediaWidth = mediaWidth;
        MediaHeight = mediaHeight;
        Rotation = rotation;
        FontDictsByKey = fontDictsByKey;
        Diagnostics = diagnostics;
        OptionalContentGroups = optionalContentGroups;
    }

    /// <summary>The page's MediaBox (3.31). The default (all zeros) when the list was not built from a page.</summary>
    public PdfRectangle MediaBox { get; init; }

    /// <summary>
    /// The CropBox, the region a viewer shows and a printer prints (3.31),
    /// defaulting to the MediaBox. PDF 32000-1:2008 §14.11.2.
    /// </summary>
    public PdfRectangle CropBox { get; init; }

    /// <summary>The BleedBox (3.31), defaulting to the CropBox.</summary>
    public PdfRectangle BleedBox { get; init; }

    /// <summary>The TrimBox (3.31), defaulting to the CropBox.</summary>
    public PdfRectangle TrimBox { get; init; }

    /// <summary>The ArtBox (3.31), defaulting to the CropBox.</summary>
    public PdfRectangle ArtBox { get; init; }

    /// <summary>
    /// Every font used by text on the page, once each, in first-use order
    /// (3.31). The same objects as <see cref="TextOp.Font"/>.
    /// </summary>
    public IReadOnlyList<DisplayListFont> Fonts { get; init; } = Array.Empty<DisplayListFont>();

    /// <summary>
    /// The page's transparency groups (3.31, D-173), by id: entry <c>i</c> has
    /// <see cref="TransparencyGroup.Id"/> <c>i + 1</c>. Empty when there are none.
    /// </summary>
    public IReadOnlyList<TransparencyGroup> TransparencyGroups { get; init; } = Array.Empty<TransparencyGroup>();

    /// <summary>
    /// True when anything on the page needs transparency (3.31): a blend mode
    /// other than Normal, a soft mask, an opacity below 1, or a transparency
    /// group.
    /// </summary>
    public bool HasTransparency
    {
        get
        {
            foreach (RenderOp op in _ops)
            {
                if (op.BlendMode != PdfBlendMode.Normal || op.SoftMask is not null || op.GroupIds.Count > 0)
                {
                    return true;
                }

                switch (op)
                {
                    case OpacityOp { Push: true } o when o.Alpha < 1 || o.Isolated:
                        return true;
                    case BlendModeOp { Push: true } b when b.Mode != PdfBlendMode.Normal:
                        return true;
                    case TextOp t when t.FillOpacity < 1 || t.StrokeOpacity < 1:
                        return true;
                    case ImageOp img when img.Alpha < 1 || img.SoftMaskAlpha is not null:
                        return true;
                    case PathOp p when p.FillOpacity < 1 || p.StrokeOpacity < 1:
                        return true;
                }
            }

            return false;
        }
    }

    /// <summary>Page width in points.</summary>
    public double MediaWidth { get; }

    /// <summary>Page height in points.</summary>
    public double MediaHeight { get; }

    /// <summary>Clockwise rotation in degrees (0, 90, 180, 270).</summary>
    public int Rotation { get; }

    /// <summary>
    /// Font dictionaries for every font referenced on this page, keyed by
    /// the resource-name used in <c>TextOp.FontKey</c>. Empty when the
    /// builder did not populate it (e.g. legacy callers using the
    /// four-argument constructor). Never null.
    /// </summary>
    public IReadOnlyDictionary<string, PdfDictionary> FontDictsByKey { get; }

    /// <summary>
    /// Graceful-degradation events recorded by the builder during page
    /// construction (e.g. a font that could not be resolved, causing
    /// <see cref="DiagnosticKind.DecodeFallback"/>). Empty when nothing
    /// went wrong. Never null. New in v2.1.8 — older callers using
    /// constructors without this argument see an empty list.
    /// </summary>
    public IReadOnlyList<RenderingDiagnostic> Diagnostics { get; }

    /// <summary>
    /// The optional-content groups (layers) declared in the document's
    /// /OCProperties/OCGs array, in declaration order, with default
    /// visibility resolved. Empty when the document has no optional content or
    /// when a constructor without this argument was used. Never null. New in
    /// v2.3. See <see cref="RenderOp.Layers"/> for per-op membership.
    /// </summary>
    public IReadOnlyList<OptionalContentGroup> OptionalContentGroups { get; }

    /// <inheritdoc />
    public int Count => _ops.Count;

    /// <inheritdoc />
    public RenderOp this[int index] => _ops[index];

    /// <inheritdoc />
    public IEnumerator<RenderOp> GetEnumerator() => _ops.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
