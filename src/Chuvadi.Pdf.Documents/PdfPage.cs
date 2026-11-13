// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.7.3.3 — Page objects
// PHASE: Phase 1 — Chuvadi.Pdf.Documents
// Represents a single page in a PDF document.

using System;
using System.Collections.Generic;
using System.Globalization;
using Chuvadi.Pdf.Graphics;
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
    private readonly Func<PdfDictionary?>? _structTreeRoot;
    private readonly Func<PdfDictionary?>? _acroForm;
    private IReadOnlyList<PdfViewport>? _viewports;
    private readonly Action<string>? _warn;

    internal PdfPage(
        PdfDictionary dict, IPdfObjectResolver resolver, int pageIndex,
        Func<OptionalContentVisibility>? optionalContent = null,
        Func<PdfDictionary?>? structTreeRoot = null,
        Func<PdfDictionary?>? acroForm = null,
        Action<string>? warn = null)
    {
        _warn = warn;
        _acroForm = acroForm;
        _dict = dict ?? throw new ArgumentNullException(nameof(dict));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _pageIndex = pageIndex;
        _optionalContent = optionalContent;
        _structTreeRoot = structTreeRoot;
    }

    /// <summary>
    /// Gets the structure-tree root of the document this page belongs to
    /// (<see cref="PdfDocument.StructTreeRoot"/>), or null when the document
    /// has none (since 3.29). Text extraction reads a Tagged PDF's reading
    /// order from it. PDF 32000-1:2008 §14.7.2 — Structure Hierarchy.
    /// </summary>
    public PdfDictionary? StructTreeRoot => _structTreeRoot?.Invoke();

    /// <summary>
    /// Gets the interactive form dictionary (<c>/AcroForm</c>) of the document
    /// this page belongs to, or null when it has none (3.31). Field appearances
    /// are made from its <c>/DA</c>, <c>/DR</c> and <c>/NeedAppearances</c>.
    /// PDF 32000-1:2008 §12.7.2 — Interactive Form Dictionary.
    /// </summary>
    public PdfDictionary? AcroForm => _acroForm?.Invoke();

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
    /// this page's dictionary, overriding any inherited value. Since 3.34.0 a
    /// MediaBox that is missing, or is not four numbers enclosing a non-empty
    /// area, reads as US Letter (0 0 612 792) and adds a line to
    /// <see cref="PdfDocument.Warnings"/>; before, reading it threw
    /// <see cref="PdfCorruptionException"/>.
    /// PDF 32000-1:2008 §7.7.3.3, Table 30 — MediaBox.
    /// </summary>
    public PdfRectangle MediaBox
    {
        get => GetMediaBox();
        set => SetBox(PdfName.MediaBox, value);
    }

    /// <summary>
    /// Gets or sets the CropBox — the visible region of the page.
    /// The getter defaults to MediaBox when absent; the setter writes a direct
    /// <c>/CropBox</c> entry on this page's dictionary, overriding any inherited
    /// value. PDF 32000-1:2008 §7.7.3.3, Table 30 — CropBox.
    /// </summary>
    /// <remarks>
    /// The value as written in the file. What is shown is the CropBox
    /// intersected with the MediaBox: <see cref="GetDisplayBox"/> (3.34.0).
    /// </remarks>
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
    /// Gets the BleedBox — the region printed content is clipped to in
    /// production (3.31). Defaults to <see cref="CropBox"/>; not inherited.
    /// PDF 32000-1:2008 §14.11.2.
    /// </summary>
    public PdfRectangle BleedBox => OwnBoxOrCropBox("BleedBox");

    /// <summary>
    /// Gets the TrimBox — the intended size of the finished page (3.31).
    /// Defaults to <see cref="CropBox"/>; not inherited. PDF 32000-1:2008 §14.11.2.
    /// </summary>
    public PdfRectangle TrimBox => OwnBoxOrCropBox("TrimBox");

    /// <summary>
    /// Gets the ArtBox — the extent of the page's meaningful content (3.31).
    /// Defaults to <see cref="CropBox"/>; not inherited. PDF 32000-1:2008 §14.11.2.
    /// </summary>
    public PdfRectangle ArtBox => OwnBoxOrCropBox("ArtBox");

    private PdfRectangle OwnBoxOrCropBox(string key) =>
        _dict.TryGetValue(PdfName.Intern(key), out PdfPrimitive? v) && _resolver.Resolve(v ?? PdfNull.Value) is PdfArray arr && arr.Count >= 4
            ? RectangleFromArray(arr)
            : CropBox;

    /// <summary>
    /// Gets the width of the page's MediaBox in points (1/72 inch).
    /// </summary>
    /// <remarks>
    /// This is not always the size a viewer or the renderers show: since
    /// 3.32.0 they show the CropBox, and <c>/Rotate</c> can swap width and
    /// height. Use <see cref="GetDisplayBox"/> for the shown box,
    /// <see cref="GetDisplayTransform"/> to map the shown page to PDF
    /// coordinates, and <c>PageRasterizer.GetOutputSize</c> for an image's size.
    /// </remarks>
    public double Width => MediaBox.Width;

    /// <summary>
    /// Gets the height of the page's MediaBox in points (1/72 inch).
    /// </summary>
    /// <remarks>
    /// Not always the shown height; see <see cref="Width"/>.
    /// </remarks>
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
    /// <remarks>
    /// Since 3.32.0 the <c>/VP</c> array is read once, on first use, and the
    /// same list is returned on every call, so <see cref="FindViewport"/> and
    /// <see cref="MeasureDistance"/> no longer parse it each time. A change
    /// made to the page's <c>/VP</c> entry through the low-level dictionary
    /// after the first call is not seen. Like the rest of the page, the list
    /// belongs to one <see cref="PdfDocument"/>, which is not for concurrent
    /// use (see docs/THREAD-SAFETY.md).
    /// </remarks>
    public IReadOnlyList<PdfViewport> Viewports =>
        _viewports ??= PdfViewport.ReadArray(_dict.TryGetValue(PdfName.Intern("VP"), out PdfPrimitive? vp) ? vp : null, _resolver);

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
    /// Returns the box a renderer shows for <paramref name="box"/>, in user
    /// space, normalised so that <see cref="PdfRectangle.X1"/> and
    /// <see cref="PdfRectangle.Y1"/> are the lower-left corner (3.34.0, D-334).
    /// </summary>
    /// <param name="box">
    /// <see cref="PdfDisplayBox.CropBox"/>: the CropBox intersected with the
    /// MediaBox, or the MediaBox when they do not overlap (PDF 32000-1:2008
    /// §14.11.2). <see cref="PdfDisplayBox.MediaBox"/>: the MediaBox.
    /// </param>
    /// <remarks>
    /// The raster, SVG and WPF renderers all show this box (the CropBox by
    /// default since 3.32.0). Unlike <see cref="Width"/> and <see cref="Height"/>,
    /// which report the MediaBox, it gives the size the rendered output has
    /// before any rotation. <see cref="Rotate"/> is not applied; see
    /// <see cref="GetDisplayTransform"/>.
    /// </remarks>
    public PdfRectangle GetDisplayBox(PdfDisplayBox box)
    {
        PdfRectangle media = Normalised(MediaBox);
        if (box == PdfDisplayBox.MediaBox)
        {
            return media;
        }

        PdfRectangle crop = Normalised(CropBox);
        double x1 = Math.Max(media.X1, crop.X1);
        double y1 = Math.Max(media.Y1, crop.Y1);
        double x2 = Math.Min(media.X2, crop.X2);
        double y2 = Math.Min(media.Y2, crop.Y2);
        return x2 > x1 && y2 > y1 ? new PdfRectangle(x1, y1, x2, y2) : media;
    }

    /// <summary>
    /// Returns the transform from display space to user space (3.34.0, D-335).
    /// Display space is the page as a viewer shows it: the
    /// <paramref name="box"/> from <see cref="GetDisplayBox"/>, turned by
    /// <see cref="Rotate"/>, with the origin at its lower-left corner, y up,
    /// in points.
    /// </summary>
    /// <param name="box">The box the page is shown with.</param>
    /// <returns>
    /// A transform whose <see cref="Transform.TransformPoint"/> maps a display
    /// point to the user-space point under it. Use it to place content (a
    /// stamp, a watermark, a redaction box) where the viewer shows a position.
    /// </returns>
    /// <remarks>
    /// The display width and height are the box's, swapped when the rotation
    /// is 90 or 270 degrees. A <c>/Rotate</c> that is not a multiple of 90 is
    /// taken at its nearest lower multiple, as the renderers do.
    /// </remarks>
    public Transform GetDisplayTransform(PdfDisplayBox box)
    {
        PdfRectangle b = GetDisplayBox(box);
        int rotation = (((Rotate - (Rotate % 90)) % 360) + 360) % 360;
        return rotation switch
        {
            90 => new Transform(0, 1, -1, 0, b.X1 + b.Width, b.Y1),
            180 => new Transform(-1, 0, 0, -1, b.X1 + b.Width, b.Y1 + b.Height),
            270 => new Transform(0, -1, 1, 0, b.X1, b.Y1 + b.Height),
            _ => new Transform(1, 0, 0, 1, b.X1, b.Y1),
        };
    }

    private static PdfRectangle Normalised(PdfRectangle r) =>
        new PdfRectangle(Math.Min(r.X1, r.X2), Math.Min(r.Y1, r.Y2), Math.Max(r.X1, r.X2), Math.Max(r.Y1, r.Y2));

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

    // 3.34.0 (#52f): US Letter, the size used for a page whose MediaBox is
    // missing or unusable.
    private static readonly PdfRectangle LetterBox = new PdfRectangle(0, 0, 612, 792);

    // The MediaBox, inherited when absent here. A missing box, or one that is
    // not four finite numbers enclosing a non-empty area, gives US Letter
    // (612 x 792 points) and a warning in PdfDocument.Warnings (3.34.0, #52f;
    // before, reading the box threw PdfCorruptionException).
    private PdfRectangle GetMediaBox()
    {
        PdfArray? box = GetInheritedArray(PdfName.MediaBox);
        if (box is not null)
        {
            PdfRectangle rect = RectangleFromArray(box);
            if (double.IsFinite(rect.X1) && double.IsFinite(rect.Y1) && double.IsFinite(rect.X2) && double.IsFinite(rect.Y2)
                && rect.Width > 0 && rect.Height > 0)
            {
                return rect;
            }
        }

        _warn?.Invoke(string.Create(CultureInfo.InvariantCulture,
            $"Page {PageNumber}: the MediaBox is {(box is null ? "missing" : "invalid")}; US Letter (612 x 792 points) is used."));
        return LetterBox;
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

        // Walk up the /Parent chain, stopping at a cycle (a node that is its
        // own ancestor) instead of looping forever.
        PdfDictionary? current = _dict;
        HashSet<PdfDictionary> visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance) { _dict };

        while (true)
        {
            if (!current.TryGetValue(PdfName.Parent, out PdfPrimitive? parentRef))
            {
                return null;
            }

            PdfPrimitive resolvedParent = _resolver.Resolve(parentRef);

            if (resolvedParent is not PdfDictionary parent || !visited.Add(parent))
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

    private PdfRectangle RectangleFromArray(PdfArray arr)
    {
        if (arr.Count < 4)
        {
            return new PdfRectangle(0, 0, 0, 0);
        }

        // Entries may be indirect; a non-number makes the box unusable, which
        // is treated like a short array (an empty box) rather than failing.
        double[] v = new double[4];
        for (int i = 0; i < 4; i++)
        {
            PdfPrimitive entry = arr[i] is PdfReference reference ? _resolver.Resolve(reference) : arr[i];
            if (entry is PdfInteger integer)
            {
                v[i] = integer.Value;
            }
            else if (entry is PdfReal real)
            {
                v[i] = real.Value;
            }
            else
            {
                return new PdfRectangle(0, 0, 0, 0);
            }
        }

        return new PdfRectangle(v[0], v[1], v[2], v[3]);
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
