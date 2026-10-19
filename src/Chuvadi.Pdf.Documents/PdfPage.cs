// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.7.3.3 — Page objects
// PHASE: Phase 1 — Chuvadi.Pdf.Documents
// Represents a single page in a PDF document.

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Documents;

/// <summary>
/// Represents a single page in a PDF document.
/// </summary>
/// <remarks>
/// A page is defined by a page dictionary in the PDF file.
/// <see cref="PdfPage"/> exposes the commonly needed entries:
/// bounding boxes, rotation, resources, and the raw page dictionary
/// for advanced access.
///
/// Inherited entries (from ancestor /Pages nodes) are resolved by
/// walking up the page tree when a key is absent from this page's
/// dictionary.
///
/// PDF 32000-1:2008 §7.7.3.3 — Page objects, Table 30.
/// </remarks>
public sealed class PdfPage
{
    private readonly PdfDictionary _dict;
    private readonly IPdfObjectResolver _resolver;
    private readonly int _pageIndex;
    private readonly Func<OptionalContentVisibility>? _optionalContent;

    internal PdfPage(
        PdfDictionary dict, IPdfObjectResolver resolver, int pageIndex,
        Func<OptionalContentVisibility>? optionalContent = null)
    {
        _dict = dict ?? throw new ArgumentNullException(nameof(dict));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _pageIndex = pageIndex;
        _optionalContent = optionalContent;
    }

    /// <summary>
    /// Gets the document's default layer visibility (its <c>/OCProperties /D</c>
    /// configuration), which the renderers and text extraction apply to this
    /// page unless told otherwise. <see cref="OptionalContentVisibility.AllVisible"/>
    /// when the document has no optional content.
    /// </summary>
    public OptionalContentVisibility OptionalContent =>
        _optionalContent?.Invoke() ?? OptionalContentVisibility.AllVisible;

    // ── Identity ──────────────────────────────────────────────────────────

    /// <summary>Gets the zero-based index of this page in the document.</summary>
    public int Index => _pageIndex;

    /// <summary>Gets the one-based page number of this page.</summary>
    public int PageNumber => _pageIndex + 1;

    // ── Bounding boxes ────────────────────────────────────────────────────

    /// <summary>
    /// Gets or sets the MediaBox — the full extent of the page in points.
    /// Required. The getter falls back to the parent /Pages node if absent
    /// from this page; the setter writes a direct <c>/MediaBox</c> entry on
    /// this page's dictionary, overriding any inherited value.
    /// PDF 32000-1:2008 §7.7.3.3, Table 30 — MediaBox.
    /// </summary>
    public PdfRectangle MediaBox
    {
        get => GetInheritedBox(PdfName.MediaBox);
        set => SetBox(PdfName.MediaBox, value);
    }

    /// <summary>
    /// Gets or sets the CropBox — the visible region of the page.
    /// The getter defaults to MediaBox when absent; the setter writes a direct
    /// <c>/CropBox</c> entry on this page's dictionary, overriding any inherited
    /// value. PDF 32000-1:2008 §7.7.3.3, Table 30 — CropBox.
    /// </summary>
    public PdfRectangle CropBox
    {
        get
        {
            PdfArray? box = GetInheritedArray(PdfName.CropBox);
            return box is not null ? RectangleFromArray(box) : MediaBox;
        }

        set => SetBox(PdfName.CropBox, value);
    }

    /// <summary>
    /// Gets the width of the page's MediaBox in points (1/72 inch).
    /// </summary>
    public double Width => MediaBox.Width;

    /// <summary>
    /// Gets the height of the page's MediaBox in points (1/72 inch).
    /// </summary>
    public double Height => MediaBox.Height;

    // ── Page attributes ───────────────────────────────────────────────────

    /// <summary>
    /// Gets the size of one default user-space unit in multiples of 1/72 inch
    /// (<c>/UserUnit</c>, PDF 1.6; not inherited). 1.0 when absent or not a
    /// positive number. The page's physical width in inches is
    /// <c>Width * UserUnit / 72</c>; large-format drawings use it to exceed
    /// the 14,400-unit page limit.
    /// PDF 32000-1:2008 §8.3.2.3, Table 30 — UserUnit.
    /// </summary>
    public double UserUnit
    {
        get
        {
            if (!_dict.TryGetValue(PdfName.Intern("UserUnit"), out PdfPrimitive? value))
            {
                return 1.0;
            }

            double unit = _resolver.Resolve(value) switch
            {
                PdfInteger i => i.Value,
                PdfReal r => r.Value,
                _ => 1.0,
            };
            return unit > 0 && double.IsFinite(unit) ? unit : 1.0;
        }
    }

    /// <summary>
    /// Gets the page's viewports (<c>/VP</c>): regions with their own name
    /// and measurement scale, for example each drawing on a sheet. Empty when
    /// the page has none. PDF 32000-1:2008 §12.9.2.
    /// </summary>
    public IReadOnlyList<PdfViewport> Viewports =>
        PdfViewport.ReadArray(_dict.TryGetValue(PdfName.Intern("VP"), out PdfPrimitive? vp) ? vp : null, _resolver);

    /// <summary>
    /// Returns the viewport that governs a point (default user space): the
    /// last one in <see cref="Viewports"/> whose region contains it, as the
    /// specification directs (later viewports are on top), or null.
    /// </summary>
    /// <param name="x">Point x.</param>
    /// <param name="y">Point y.</param>
    /// <returns>The viewport, or null when none contains the point.</returns>
    public PdfViewport? FindViewport(double x, double y)
    {
        IReadOnlyList<PdfViewport> viewports = Viewports;
        for (int i = viewports.Count - 1; i >= 0; i--)
        {
            if (viewports[i].Contains(x, y))
            {
                return viewports[i];
            }
        }

        return null;
    }

    /// <summary>
    /// Measures the distance between two points (default user space) in the
    /// real-world units of the viewport containing both, or null when no
    /// single viewport with a rectilinear measure contains them.
    /// </summary>
    /// <param name="x1">First point x.</param>
    /// <param name="y1">First point y.</param>
    /// <param name="x2">Second point x.</param>
    /// <param name="y2">Second point y.</param>
    /// <returns>The measured distance, or null.</returns>
    public PdfMeasurement? MeasureDistance(double x1, double y1, double x2, double y2)
    {
        PdfViewport? viewport = FindViewport(x1, y1);
        if (viewport?.Measure is not PdfMeasure measure || !measure.IsRectilinear || !viewport.Contains(x2, y2))
        {
            return null;
        }

        return measure.Distance(x1, y1, x2, y2);
    }

    /// <summary>
    /// Gets the page rotation in degrees (0, 90, 180, or 270).
    /// PDF 32000-1:2008 §7.7.3.3, Table 30 — Rotate.
    /// </summary>
    public int Rotate => GetInheritedInteger(PdfName.Rotate, 0);

    /// <summary>
    /// The page's displayed size — its crop box dimensions with width and
    /// height swapped when <see cref="Rotate"/> is 90° or 270°. Use this to
    /// compute fit/scale transforms against the page as the viewer shows it.
    /// </summary>
    /// <remarks>PDF 32000-1:2008 §7.7.3.3, Table 30 — Rotate.</remarks>
    public (double Width, double Height) EffectiveSize
    {
        get
        {
            PdfRectangle box = CropBox;
            int rotation = ((Rotate % 360) + 360) % 360;
            return rotation is 90 or 270
                ? (box.Height, box.Width)
                : (box.Width, box.Height);
        }
    }

    /// <summary>
    /// Gets the Resources dictionary for this page, or null when absent.
    /// PDF 32000-1:2008 §7.7.3.3, Table 30 — Resources.
    /// </summary>
    public PdfDictionary? Resources => GetInheritedDictionary(PdfName.Resources);

    /// <summary>
    /// Gets the raw page dictionary for advanced access.
    /// </summary>
    public PdfDictionary Dictionary => _dict;

    /// <summary>
    /// Gets the /Contents entry as a primitive (may be a reference,
    /// an array of references, or null when the page has no content).
    /// </summary>
    public PdfPrimitive? Contents => _dict.GetAs<PdfPrimitive>(PdfName.Contents);

    /// <summary>
    /// Returns true when the page has at least one non-empty content stream.
    /// This is a cheap structural check — a <c>/Contents</c> stream is present
    /// with non-zero raw length — and does not decode the stream or verify that
    /// it paints any visible marks.
    /// </summary>
    public bool HasContent { get => HasNonEmptyContent(); }

    private bool HasNonEmptyContent()
    {
        PdfPrimitive? contents = _dict.GetAs<PdfPrimitive>(PdfName.Contents);
        if (contents is null)
        {
            return false;
        }

        PdfPrimitive resolved = _resolver.Resolve(contents);
        if (resolved is PdfStream single)
        {
            return single.RawLength > 0;
        }

        if (resolved is PdfArray array)
        {
            for (int i = 0; i < array.Count; i++)
            {
                if (_resolver.Resolve(array[i]) is PdfStream stream && stream.RawLength > 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    // ── Inherited value helpers ───────────────────────────────────────────

    private PdfRectangle GetInheritedBox(PdfName key)
    {
        PdfArray box = GetInheritedArray(key) ??
            throw new PdfCorruptionException(
                $"Required page entry /{key.Value} not found on page {PageNumber} or any ancestor.");

        return RectangleFromArray(box);
    }

    private PdfArray? GetInheritedArray(PdfName key)
    {
        PdfPrimitive? value = GetInherited(key);
        return value is PdfArray arr ? arr : null;
    }

    private int GetInheritedInteger(PdfName key, int defaultValue)
    {
        PdfPrimitive? value = GetInherited(key);

        if (value is PdfInteger i)
        {
            return i.Value;
        }

        return defaultValue;
    }

    private PdfDictionary? GetInheritedDictionary(PdfName key)
    {
        PdfPrimitive? value = GetInherited(key);

        if (value is PdfReference reference)
        {
            return _resolver.Resolve(reference) as PdfDictionary;
        }

        return value as PdfDictionary;
    }

    /// <summary>
    /// Walks the page tree upward to find an inherited value for <paramref name="key"/>.
    /// PDF 32000-1:2008 §7.7.3.4 — Inheritance of page attributes.
    /// </summary>
    private PdfPrimitive? GetInherited(PdfName key)
    {
        // Check this page first.
        if (_dict.TryGetValue(key, out PdfPrimitive? direct))
        {
            return _resolver.Resolve(direct);
        }

        // Walk up the /Parent chain.
        PdfDictionary? current = _dict;

        while (true)
        {
            if (!current.TryGetValue(PdfName.Parent, out PdfPrimitive? parentRef))
            {
                return null;
            }

            PdfPrimitive resolvedParent = _resolver.Resolve(parentRef);

            if (resolvedParent is not PdfDictionary parent)
            {
                return null;
            }

            if (parent.TryGetValue(key, out PdfPrimitive? inherited))
            {
                return _resolver.Resolve(inherited);
            }

            current = parent;
        }
    }

    private void SetBox(PdfName key, PdfRectangle box)
    {
        _dict.Set(key, new PdfArray([
            new PdfReal(box.X1), new PdfReal(box.Y1),
            new PdfReal(box.X2), new PdfReal(box.Y2)
        ]));
    }

    private static PdfRectangle RectangleFromArray(PdfArray arr)
    {
        if (arr.Count < 4)
        {
            return new PdfRectangle(0, 0, 0, 0);
        }

        double x1 = arr.GetNumber(0);
        double y1 = arr.GetNumber(1);
        double x2 = arr.GetNumber(2);
        double y2 = arr.GetNumber(3);
        return new PdfRectangle(x1, y1, x2, y2);
    }
}

/// <summary>
/// An immutable rectangle in PDF user space (points, 1/72 inch).
/// Origin is bottom-left by PDF convention.
/// </summary>
public readonly struct PdfRectangle
{
    /// <summary>Initialises a <see cref="PdfRectangle"/> from four coordinates.</summary>
    public PdfRectangle(double x1, double y1, double x2, double y2)
    {
        X1 = x1;
        Y1 = y1;
        X2 = x2;
        Y2 = y2;
    }

    /// <summary>Left edge (in PDF user space).</summary>
    public double X1 { get; }

    /// <summary>Bottom edge (in PDF user space).</summary>
    public double Y1 { get; }

    /// <summary>Right edge (in PDF user space).</summary>
    public double X2 { get; }

    /// <summary>Top edge (in PDF user space).</summary>
    public double Y2 { get; }

    /// <summary>Width in points.</summary>
    public double Width => Math.Abs(X2 - X1);

    /// <summary>Height in points.</summary>
    public double Height => Math.Abs(Y2 - Y1);

    /// <inheritdoc/>
    public override string ToString() =>
        $"[{X1} {Y1} {X2} {Y2}]";
}
