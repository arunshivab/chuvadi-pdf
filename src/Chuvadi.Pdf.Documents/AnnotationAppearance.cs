// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.5.5 — Appearance streams
// PHASE: Chuvadi.Pdf.Documents — annotation appearance resolution.
//
// Resolves each page annotation's normal appearance stream (/AP /N, honouring
// the /AS state selector) and computes the appearance-to-page placement
// transform defined by §12.5.5: the appearance form's /BBox is mapped through
// its /Matrix, and the resulting bounding box is scaled and translated onto the
// annotation's /Rect. Renderers, text extraction, and flattening all consume
// this one implementation so an annotation paints, extracts, and flattens at
// exactly the same place on the page.

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Documents;

/// <summary>
/// A page annotation's resolved normal appearance and its placement on the
/// page, per PDF 32000-1:2008 §12.5.5.
/// </summary>
/// <remarks>
/// The placement maps a point already transformed by the appearance form's
/// own <c>/Matrix</c> into page space:
/// <c>x' = ScaleX * x + OffsetX</c>, <c>y' = ScaleY * y + OffsetY</c>.
/// Consumers that replay the appearance content stream should first apply
/// this placement to the current transformation matrix and then invoke the
/// form (whose own <c>/Matrix</c> and <c>/Resources</c> apply as for any
/// form XObject).
/// </remarks>
public sealed class AnnotationAppearance
{
    internal AnnotationAppearance(
        PdfDictionary annotation,
        PdfStream appearance,
        PdfRectangle rect,
        double scaleX,
        double scaleY,
        double offsetX,
        double offsetY,
        double matrixA,
        double matrixB,
        double matrixC,
        double matrixD,
        double matrixE,
        double matrixF,
        PdfDictionary? resources)
    {
        Annotation = annotation;
        Appearance = appearance;
        Rect = rect;
        ScaleX = scaleX;
        ScaleY = scaleY;
        OffsetX = offsetX;
        OffsetY = offsetY;
        MatrixA = matrixA;
        MatrixB = matrixB;
        MatrixC = matrixC;
        MatrixD = matrixD;
        MatrixE = matrixE;
        MatrixF = matrixF;
        Resources = resources;
    }

    /// <summary>The annotation dictionary the appearance belongs to.</summary>
    public PdfDictionary Annotation { get; }

    /// <summary>
    /// The resolved normal appearance stream (<c>/AP /N</c>). When the normal
    /// appearance is a state dictionary, the stream selected by the
    /// annotation's <c>/AS</c> entry.
    /// </summary>
    public PdfStream Appearance { get; }

    /// <summary>The annotation rectangle (<c>/Rect</c>) in page space.</summary>
    public PdfRectangle Rect { get; }

    /// <summary>Horizontal placement scale (§12.5.5 algorithm).</summary>
    public double ScaleX { get; }

    /// <summary>Vertical placement scale (§12.5.5 algorithm).</summary>
    public double ScaleY { get; }

    /// <summary>Horizontal placement translation (§12.5.5 algorithm).</summary>
    public double OffsetX { get; }

    /// <summary>Vertical placement translation (§12.5.5 algorithm).</summary>
    public double OffsetY { get; }

    /// <summary>The appearance form's <c>/Matrix</c> component a (default 1).</summary>
    public double MatrixA { get; }

    /// <summary>The appearance form's <c>/Matrix</c> component b (default 0).</summary>
    public double MatrixB { get; }

    /// <summary>The appearance form's <c>/Matrix</c> component c (default 0).</summary>
    public double MatrixC { get; }

    /// <summary>The appearance form's <c>/Matrix</c> component d (default 1).</summary>
    public double MatrixD { get; }

    /// <summary>The appearance form's <c>/Matrix</c> component e (default 0).</summary>
    public double MatrixE { get; }

    /// <summary>The appearance form's <c>/Matrix</c> component f (default 0).</summary>
    public double MatrixF { get; }

    /// <summary>
    /// The appearance form's resolved <c>/Resources</c> dictionary, or null
    /// when the form declares none.
    /// </summary>
    public PdfDictionary? Resources { get; }
}

/// <summary>
/// Collects the drawable annotation appearances of a page: every annotation
/// with a resolvable normal appearance stream that is not hidden and not a
/// popup, together with its §12.5.5 placement.
/// </summary>
public static class PageAnnotationAppearances
{
    private const int FlagHidden = 0x2;
    private const int FlagNoView = 0x20;

    /// <summary>
    /// Collects the drawable annotation appearances of <paramref name="page"/>.
    /// </summary>
    /// <remarks>
    /// Skipped: annotations without a resolvable <c>/AP /N</c> stream,
    /// annotations whose <c>/F</c> flags include Hidden or NoView, popup
    /// annotations (drawn only via their parent markup), and annotations whose
    /// <c>/Rect</c> or appearance <c>/BBox</c> is degenerate. This mirrors how
    /// interactive viewers decide what to paint, so hybrid XFA/AcroForm
    /// documents — whose field values live in widget appearance streams —
    /// render, extract, and flatten with their values visible.
    /// </remarks>
    /// <param name="page">The page whose annotations are collected.</param>
    /// <param name="objects">The object store for resolving indirect references.</param>
    /// <returns>The drawable appearances in <c>/Annots</c> order.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="page"/> or <paramref name="objects"/> is null.
    /// </exception>
    public static IReadOnlyList<AnnotationAppearance> Collect(PdfPage page, PdfObjectStore objects)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(objects);

        List<AnnotationAppearance> result = new List<AnnotationAppearance>();

        if (!page.Dictionary.TryGetValue(PdfName.Intern("Annots"), out PdfPrimitive? annotsPrim))
        {
            return result;
        }

        PdfArray? annots = objects.ResolveAs<PdfArray>(annotsPrim ?? PdfNull.Value);

        if (annots is null)
        {
            return result;
        }

        for (int i = 0; i < annots.Count; i++)
        {
            PdfDictionary? annot = objects.ResolveAs<PdfDictionary>(annots[i]);

            if (annot is null)
            {
                continue;
            }

            PdfName? subtype = annot.GetName(PdfName.Subtype);

            if (subtype is not null && subtype.Value == "Popup")
            {
                continue;
            }

            int flags = annot.GetInteger(PdfName.Intern("F"), 0);

            if ((flags & FlagHidden) != 0 || (flags & FlagNoView) != 0)
            {
                continue;
            }

            PdfStream? appearance = ResolveNormalAppearance(annot, objects);

            if (appearance is null)
            {
                continue;
            }

            if (!TryReadRect(annot, objects, out PdfRectangle rect))
            {
                continue;
            }

            if (!TryComputePlacement(
                appearance, objects, rect,
                out double scaleX, out double scaleY, out double offsetX, out double offsetY,
                out double ma, out double mb, out double mc, out double md, out double me, out double mf))
            {
                continue;
            }

            PdfDictionary? resources = null;

            if (appearance.Dictionary.TryGetValue(PdfName.Intern("Resources"), out PdfPrimitive? resPrim))
            {
                resources = objects.ResolveAs<PdfDictionary>(resPrim ?? PdfNull.Value);
            }

            result.Add(new AnnotationAppearance(
                annot, appearance, rect,
                scaleX, scaleY, offsetX, offsetY,
                ma, mb, mc, md, me, mf,
                resources));
        }

        return result;
    }

    // ── Normal appearance resolution (/AP /N, /AS state) ───────────────────

    private static PdfStream? ResolveNormalAppearance(PdfDictionary annot, PdfObjectStore objects)
    {
        if (!annot.TryGetValue(PdfName.Intern("AP"), out PdfPrimitive? apPrim))
        {
            return null;
        }

        PdfDictionary? appearanceDict = objects.ResolveAs<PdfDictionary>(apPrim ?? PdfNull.Value);

        if (appearanceDict is null
            || !appearanceDict.TryGetValue(PdfName.Intern("N"), out PdfPrimitive? normalPrim))
        {
            return null;
        }

        PdfPrimitive normal = objects.Resolve(normalPrim);

        if (normal is PdfStream stream)
        {
            return stream;
        }

        if (normal is PdfDictionary states)
        {
            PdfName? appearanceState = annot.GetName(PdfName.Intern("AS"));

            if (appearanceState is not null
                && states.TryGetValue(appearanceState, out PdfPrimitive? selectedState))
            {
                return objects.ResolveAs<PdfStream>(selectedState);
            }

            if (states.TryGetValue(PdfName.Intern("Off"), out PdfPrimitive? offState))
            {
                return objects.ResolveAs<PdfStream>(offState);
            }

            if (states.Count == 1)
            {
                foreach (PdfPrimitive only in states.Values)
                {
                    return objects.ResolveAs<PdfStream>(only);
                }
            }
        }

        return null;
    }

    // ── Placement (ISO 32000-1 §12.5.5, "Algorithm: appearance streams") ────

    private static bool TryComputePlacement(
        PdfStream appearance,
        PdfObjectStore objects,
        PdfRectangle rect,
        out double scaleX,
        out double scaleY,
        out double offsetX,
        out double offsetY,
        out double ma,
        out double mb,
        out double mc,
        out double md,
        out double me,
        out double mf)
    {
        scaleX = scaleY = offsetX = offsetY = 0;
        ma = md = 1;
        mb = mc = me = mf = 0;

        PdfDictionary form = appearance.Dictionary;

        if (!form.TryGetValue(PdfName.Intern("BBox"), out PdfPrimitive? bboxPrim))
        {
            return false;
        }

        PdfArray? bbox = objects.ResolveAs<PdfArray>(bboxPrim ?? PdfNull.Value);

        if (bbox is null || bbox.Count < 4
            || !TryNumber(bbox[0], objects, out double bx0)
            || !TryNumber(bbox[1], objects, out double by0)
            || !TryNumber(bbox[2], objects, out double bx1)
            || !TryNumber(bbox[3], objects, out double by1))
        {
            return false;
        }

        if (form.TryGetValue(PdfName.Intern("Matrix"), out PdfPrimitive? matrixPrim)
            && objects.ResolveAs<PdfArray>(matrixPrim ?? PdfNull.Value) is PdfArray matrix
            && matrix.Count >= 6
            && TryNumber(matrix[0], objects, out double a)
            && TryNumber(matrix[1], objects, out double b)
            && TryNumber(matrix[2], objects, out double c)
            && TryNumber(matrix[3], objects, out double d)
            && TryNumber(matrix[4], objects, out double e)
            && TryNumber(matrix[5], objects, out double f))
        {
            ma = a;
            mb = b;
            mc = c;
            md = d;
            me = e;
            mf = f;
        }

        // Map the four BBox corners through the form matrix and take the
        // axis-aligned bounds of the result (§12.5.5 step b).
        Span<double> xs = stackalloc double[4];
        Span<double> ys = stackalloc double[4];
        MapPoint(bx0, by0, ma, mb, mc, md, me, mf, out xs[0], out ys[0]);
        MapPoint(bx1, by0, ma, mb, mc, md, me, mf, out xs[1], out ys[1]);
        MapPoint(bx1, by1, ma, mb, mc, md, me, mf, out xs[2], out ys[2]);
        MapPoint(bx0, by1, ma, mb, mc, md, me, mf, out xs[3], out ys[3]);

        double minX = Math.Min(Math.Min(xs[0], xs[1]), Math.Min(xs[2], xs[3]));
        double maxX = Math.Max(Math.Max(xs[0], xs[1]), Math.Max(xs[2], xs[3]));
        double minY = Math.Min(Math.Min(ys[0], ys[1]), Math.Min(ys[2], ys[3]));
        double maxY = Math.Max(Math.Max(ys[0], ys[1]), Math.Max(ys[2], ys[3]));

        double transformedWidth = maxX - minX;
        double transformedHeight = maxY - minY;

        if (transformedWidth <= 0 || transformedHeight <= 0)
        {
            return false;
        }

        double rx0 = Math.Min(rect.X1, rect.X2);
        double ry0 = Math.Min(rect.Y1, rect.Y2);
        double rx1 = Math.Max(rect.X1, rect.X2);
        double ry1 = Math.Max(rect.Y1, rect.Y2);

        scaleX = (rx1 - rx0) / transformedWidth;
        scaleY = (ry1 - ry0) / transformedHeight;
        offsetX = rx0 - (scaleX * minX);
        offsetY = ry0 - (scaleY * minY);
        return true;
    }

    private static void MapPoint(
        double x, double y,
        double a, double b, double c, double d, double e, double f,
        out double outX, out double outY)
    {
        outX = (a * x) + (c * y) + e;
        outY = (b * x) + (d * y) + f;
    }

    private static bool TryReadRect(PdfDictionary annot, PdfObjectStore objects, out PdfRectangle rect)
    {
        rect = default;

        if (!annot.TryGetValue(PdfName.Intern("Rect"), out PdfPrimitive? rectPrim))
        {
            return false;
        }

        PdfArray? array = objects.ResolveAs<PdfArray>(rectPrim ?? PdfNull.Value);

        if (array is null || array.Count < 4
            || !TryNumber(array[0], objects, out double a)
            || !TryNumber(array[1], objects, out double b)
            || !TryNumber(array[2], objects, out double c)
            || !TryNumber(array[3], objects, out double d))
        {
            return false;
        }

        double x0 = Math.Min(a, c);
        double y0 = Math.Min(b, d);
        double x1 = Math.Max(a, c);
        double y1 = Math.Max(b, d);

        if (x1 <= x0 || y1 <= y0)
        {
            return false;
        }

        rect = new PdfRectangle(x0, y0, x1, y1);
        return true;
    }

    private static bool TryNumber(PdfPrimitive? primitive, PdfObjectStore objects, out double value)
    {
        value = 0;

        if (primitive is null)
        {
            return false;
        }

        PdfPrimitive resolved = objects.Resolve(primitive);

        if (resolved is PdfInteger i)
        {
            value = i.Value;
            return true;
        }

        if (resolved is PdfReal r)
        {
            value = r.Value;
            return true;
        }

        return false;
    }
}
