// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.5.2 — Annotation dictionaries
//        PDF 32000-1:2008 §12.5.6 — Annotation types
// PHASE: Phase 1.1 — Chuvadi.Pdf.Annotations (v2.0.0 R3 adds shape decoders)
// Reads annotations from a PDF page's /Annots array.

using System;
using System.Collections.Generic;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Annotations;

/// <summary>
/// Reads annotations from a PDF document.
/// </summary>
/// <remarks>
/// For each page's <c>/Annots</c> array, resolves every entry and decodes the
/// subtype into one of the modelled <see cref="PdfAnnotation"/> derivatives.
/// Subtypes not modelled by Chuvadi are returned as <see cref="GenericAnnotation"/>
/// with their raw <c>/Subtype</c> name preserved.
/// </remarks>
public static class AnnotationReader
{
    /// <summary>
    /// Returns all annotations on the given page. Empty when the page has
    /// no <c>/Annots</c> entry.
    /// </summary>
    public static IReadOnlyList<PdfAnnotation> GetAnnotations(PdfDocument document, int pageIndex)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (pageIndex < 0 || pageIndex >= document.PageCount)
        {
            throw new ArgumentOutOfRangeException(nameof(pageIndex));
        }

        PdfPage page = document.Pages[pageIndex];
        PdfObjectStore store = document.Objects;

        if (!page.Dictionary.TryGetValue(PdfName.Intern("Annots"), out PdfPrimitive? annotsPrim))
        {
            return Array.Empty<PdfAnnotation>();
        }

        PdfArray? annotsArray = store.ResolveAs<PdfArray>(annotsPrim ?? PdfNull.Value);

        if (annotsArray is null)
        {
            return Array.Empty<PdfAnnotation>();
        }

        List<PdfAnnotation> result = new List<PdfAnnotation>();

        for (int i = 0; i < annotsArray.Count; i++)
        {
            PdfDictionary? annotDict = store.ResolveAs<PdfDictionary>(annotsArray[i]);

            if (annotDict is null)
            {
                continue;
            }

            PdfAnnotation? annotation = DecodeAnnotation(annotDict, store, pageIndex, document);

            if (annotation is not null)
            {
                result.Add(annotation);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns annotations from every page in the document, in page order.
    /// </summary>
    public static IReadOnlyList<PdfAnnotation> GetAllAnnotations(PdfDocument document)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        List<PdfAnnotation> all = new List<PdfAnnotation>();

        for (int i = 0; i < document.PageCount; i++)
        {
            all.AddRange(GetAnnotations(document, i));
        }

        return all;
    }

    // ── Subtype dispatch ──────────────────────────────────────────────────

    private static PdfAnnotation? DecodeAnnotation(
        PdfDictionary dict, PdfObjectStore store, int pageIndex, PdfDocument document)
    {
        // Common fields
        RectangleF rect = ReadRect(dict);
        string? contents = ReadString(dict, PdfName.Intern("Contents"));
        string? author = ReadString(dict, PdfName.Intern("T"));
        ColorF? color = ReadColor(dict, PdfName.Intern("C"));
        float opacity = ReadOpacity(dict);
        ColorF? interior = ReadColor(dict, PdfName.Intern("IC"));
        float borderWidth = ReadBorderWidth(dict);

        // Subtype dispatch
        if (!dict.TryGetValue(PdfName.Intern("Subtype"), out PdfPrimitive? subPrim) ||
            subPrim is not PdfName subName)
        {
            return null;
        }

        return subName.Value switch
        {
            "Text" => ReadText(pageIndex, rect, contents, color, author, opacity, dict),
            "Link" => ReadLink(pageIndex, rect, contents, dict, store, document),
            "FreeText" => ReadFreeText(pageIndex, rect, contents, color, author, opacity, dict),
            "Highlight" => ReadMarkup(AnnotationType.Highlight, pageIndex, rect, contents, color, author, opacity, dict),
            "Underline" => ReadMarkup(AnnotationType.Underline, pageIndex, rect, contents, color, author, opacity, dict),
            "Squiggly" => ReadMarkup(AnnotationType.Squiggly, pageIndex, rect, contents, color, author, opacity, dict),
            "StrikeOut" => ReadMarkup(AnnotationType.StrikeOut, pageIndex, rect, contents, color, author, opacity, dict),
            "Stamp" => ReadStamp(pageIndex, rect, contents, color, author, opacity, dict),
            "Ink" => ReadInk(pageIndex, rect, contents, color, author, opacity, dict),
            "Square" => ReadShape(ShapeKind.Square, pageIndex, rect, contents, color, author, opacity, borderWidth, interior, dict),
            "Circle" => ReadShape(ShapeKind.Circle, pageIndex, rect, contents, color, author, opacity, borderWidth, interior, dict),
            "Line" => ReadShape(ShapeKind.Line, pageIndex, rect, contents, color, author, opacity, borderWidth, interior, dict),
            "PolyLine" => ReadShape(ShapeKind.Polyline, pageIndex, rect, contents, color, author, opacity, borderWidth, interior, dict),
            "Polygon" => ReadShape(ShapeKind.Polygon, pageIndex, rect, contents, color, author, opacity, borderWidth, interior, dict),
            _ => new GenericAnnotation(pageIndex, rect, subName.Value, contents, color, author, opacity),
        };
    }

    // ── Subtype-specific decoders ─────────────────────────────────────────

    private static TextAnnotation ReadText(
        int pageIndex, RectangleF rect, string? contents, ColorF? color,
        string? author, float opacity, PdfDictionary dict)
    {
        string iconName = ReadString(dict, PdfName.Intern("Name")) ?? "Note";
        bool isOpen = false;

        if (dict.TryGetValue(PdfName.Intern("Open"), out PdfPrimitive? openPrim) &&
            openPrim is PdfBoolean b)
        {
            isOpen = b.Value;
        }

        return new TextAnnotation(pageIndex, rect, contents ?? string.Empty,
            iconName, isOpen, color, author, opacity);
    }

    private static LinkAnnotation? ReadLink(
        int pageIndex, RectangleF rect, string? contents,
        PdfDictionary dict, PdfObjectStore store, PdfDocument document)
    {
        // /A action dictionary may contain a URI or GoTo destination
        if (dict.TryGetValue(PdfName.Intern("A"), out PdfPrimitive? actionPrim))
        {
            PdfDictionary? actionDict = store.ResolveAs<PdfDictionary>(actionPrim ?? PdfNull.Value);

            if (actionDict is not null)
            {
                PdfName? actionType = actionDict.GetName(PdfName.Intern("S"));

                if (actionType is not null && actionType.Value == "URI")
                {
                    string? uriString = ReadString(actionDict, PdfName.Intern("URI"));

                    if (uriString is not null && Uri.TryCreate(uriString, UriKind.Absolute, out Uri? uri))
                    {
                        return new LinkAnnotation(pageIndex, rect, uri, contents);
                    }
                }

                if (actionType is not null && actionType.Value == "GoTo" &&
                    actionDict.TryGetValue(PdfName.Intern("D"), out PdfPrimitive? destPrim))
                {
                    int dest = ResolveDestinationPage(destPrim, store, document);

                    if (dest >= 0)
                    {
                        return new LinkAnnotation(pageIndex, rect, dest, contents);
                    }
                }
            }
        }

        // /Dest explicit destination
        if (dict.TryGetValue(PdfName.Intern("Dest"), out PdfPrimitive? directDest))
        {
            int dest = ResolveDestinationPage(directDest, store, document);

            if (dest >= 0)
            {
                return new LinkAnnotation(pageIndex, rect, dest, contents);
            }
        }

        return null;
    }

    private static FreeTextAnnotation ReadFreeText(
        int pageIndex, RectangleF rect, string? contents, ColorF? color,
        string? author, float opacity, PdfDictionary dict)
    {
        string da = ReadString(dict, PdfName.Intern("DA")) ?? "/Helvetica 12 Tf 0 0 0 rg";
        return new FreeTextAnnotation(pageIndex, rect, contents ?? string.Empty,
            color, author, opacity, da);
    }

    private static MarkupAnnotation? ReadMarkup(
        AnnotationType type, int pageIndex, RectangleF rect, string? contents,
        ColorF? color, string? author, float opacity, PdfDictionary dict)
    {
        if (!dict.TryGetValue(PdfName.Intern("QuadPoints"), out PdfPrimitive? qpPrim) ||
            qpPrim is not PdfArray qpArr)
        {
            return null;
        }

        List<float> quads = new List<float>(qpArr.Count);

        for (int i = 0; i < qpArr.Count; i++)
        {
            quads.Add((float)ToDouble(qpArr[i]));
        }

        if (quads.Count == 0 || quads.Count % 8 != 0)
        {
            return null;
        }

        return new MarkupAnnotation(type, pageIndex, rect, quads,
            contents, color, author, opacity);
    }

    private static StampAnnotation ReadStamp(
        int pageIndex, RectangleF rect, string? contents, ColorF? color,
        string? author, float opacity, PdfDictionary dict)
    {
        string name = dict.GetName(PdfName.Intern("Name"))?.Value ?? "Draft";
        return new StampAnnotation(pageIndex, rect, name, contents, color, author, opacity);
    }

    private static InkAnnotation? ReadInk(
        int pageIndex, RectangleF rect, string? contents, ColorF? color,
        string? author, float opacity, PdfDictionary dict)
    {
        if (!dict.TryGetValue(PdfName.Intern("InkList"), out PdfPrimitive? ilPrim) ||
            ilPrim is not PdfArray ilArr)
        {
            return null;
        }

        List<IReadOnlyList<PointF>> strokes = new List<IReadOnlyList<PointF>>();

        for (int i = 0; i < ilArr.Count; i++)
        {
            if (ilArr[i] is not PdfArray strokeArr)
            {
                continue;
            }

            List<PointF> points = new List<PointF>();

            for (int j = 0; j + 1 < strokeArr.Count; j += 2)
            {
                double x = ToDouble(strokeArr[j]);
                double y = ToDouble(strokeArr[j + 1]);
                points.Add(new PointF(x, y));
            }

            if (points.Count > 0)
            {
                strokes.Add(points);
            }
        }

        if (strokes.Count == 0)
        {
            return null;
        }

        return new InkAnnotation(pageIndex, rect, strokes, contents, color, author, opacity);
    }

    private static ShapeAnnotation? ReadShape(
        ShapeKind kind, int pageIndex, RectangleF rect, string? contents,
        ColorF? color, string? author, float opacity, float borderWidth,
        ColorF? interior, PdfDictionary dict)
    {
        IReadOnlyList<PointF> vertices = ExtractShapeVertices(kind, dict);

        // Validate vertex shape requirements before constructing — readers
        // must not throw on malformed PDFs; we drop the annotation instead.
        if (kind == ShapeKind.Line && vertices.Count != 2)
        {
            return null;
        }

        if ((kind == ShapeKind.Polyline || kind == ShapeKind.Polygon) && vertices.Count < 2)
        {
            return null;
        }

        return new ShapeAnnotation(
            kind,
            pageIndex,
            rect,
            vertices,
            contents,
            color,
            author,
            opacity,
            borderWidth,
            interior);
    }

    private static IReadOnlyList<PointF> ExtractShapeVertices(ShapeKind kind, PdfDictionary dict)
    {
        if (kind == ShapeKind.Square || kind == ShapeKind.Circle)
        {
            return Array.Empty<PointF>();
        }

        // /Line annotations carry endpoints in /L: [x1 y1 x2 y2].
        if (kind == ShapeKind.Line)
        {
            if (!dict.TryGetValue(PdfName.Intern("L"), out PdfPrimitive? lPrim) ||
                lPrim is not PdfArray lArr || lArr.Count < 4)
            {
                return Array.Empty<PointF>();
            }

            return new[]
            {
                new PointF(ToDouble(lArr[0]), ToDouble(lArr[1])),
                new PointF(ToDouble(lArr[2]), ToDouble(lArr[3])),
            };
        }

        // /PolyLine and /Polygon carry vertex pairs in /Vertices.
        if (!dict.TryGetValue(PdfName.Intern("Vertices"), out PdfPrimitive? vPrim) ||
            vPrim is not PdfArray vArr)
        {
            return Array.Empty<PointF>();
        }

        List<PointF> points = new List<PointF>(vArr.Count / 2);

        for (int i = 0; i + 1 < vArr.Count; i += 2)
        {
            double x = ToDouble(vArr[i]);
            double y = ToDouble(vArr[i + 1]);
            points.Add(new PointF(x, y));
        }

        return points;
    }

    // ── Field helpers ─────────────────────────────────────────────────────

    private static RectangleF ReadRect(PdfDictionary dict)
    {
        if (!dict.TryGetValue(PdfName.Intern("Rect"), out PdfPrimitive? rectPrim) ||
            rectPrim is not PdfArray arr || arr.Count < 4)
        {
            return new RectangleF(0, 0, 0, 0);
        }

        double x1 = ToDouble(arr[0]);
        double y1 = ToDouble(arr[1]);
        double x2 = ToDouble(arr[2]);
        double y2 = ToDouble(arr[3]);
        return RectangleF.FromCorners(x1, y1, x2, y2);
    }

    private static string? ReadString(PdfDictionary dict, PdfName key)
    {
        if (!dict.TryGetValue(key, out PdfPrimitive? p))
        {
            return null;
        }

        return p switch
        {
            PdfString s => Encoding.Latin1.GetString(s.Bytes),
            PdfName n => n.Value,
            _ => null,
        };
    }

    private static ColorF? ReadColor(PdfDictionary dict, PdfName key)
    {
        if (!dict.TryGetValue(key, out PdfPrimitive? cPrim) ||
            cPrim is not PdfArray arr)
        {
            return null;
        }

        if (arr.Count == 1)
        {
            return ColorF.FromGray((float)ToDouble(arr[0]));
        }

        if (arr.Count == 3)
        {
            return ColorF.FromRgb((float)ToDouble(arr[0]), (float)ToDouble(arr[1]), (float)ToDouble(arr[2]));
        }

        if (arr.Count == 4)
        {
            return ColorF.FromCmyk((float)ToDouble(arr[0]), (float)ToDouble(arr[1]),
                (float)ToDouble(arr[2]), (float)ToDouble(arr[3]));
        }

        return null;
    }

    private static float ReadOpacity(PdfDictionary dict)
    {
        if (dict.TryGetValue(PdfName.Intern("CA"), out PdfPrimitive? p))
        {
            return (float)ToDouble(p);
        }

        return 1f;
    }

    private static float ReadBorderWidth(PdfDictionary dict)
    {
        // /BS dictionary takes precedence; otherwise fall back to /Border[2].
        if (dict.TryGetValue(PdfName.Intern("BS"), out PdfPrimitive? bsPrim) &&
            bsPrim is PdfDictionary bs &&
            bs.TryGetValue(PdfName.Intern("W"), out PdfPrimitive? wPrim))
        {
            return (float)ToDouble(wPrim);
        }

        if (dict.TryGetValue(PdfName.Intern("Border"), out PdfPrimitive? borderPrim) &&
            borderPrim is PdfArray borderArr && borderArr.Count >= 3)
        {
            return (float)ToDouble(borderArr[2]);
        }

        return 1f;
    }

    private static double ToDouble(PdfPrimitive p)
    {
        return p switch
        {
            PdfInteger i => i.Value,
            PdfReal r => r.Value,
            _ => 0,
        };
    }

    private static int ResolveDestinationPage(PdfPrimitive destPrim, PdfObjectStore store, PdfDocument document)
    {
        PdfPrimitive resolved = store.Resolve(destPrim);

        if (resolved is PdfArray destArray && destArray.Count > 0 &&
            destArray[0] is PdfReference pageRef)
        {
            int idx = 0;

            foreach (PdfIndirectObject obj in document.Objects.Objects)
            {
                if (obj.Value is not PdfDictionary d ||
                    !d.TryGetValue(PdfName.Type, out PdfPrimitive? t) ||
                    t is not PdfName tn || tn.Value != "Page")
                {
                    continue;
                }

                if (obj.Id.ObjectNumber == pageRef.ObjectId.ObjectNumber)
                {
                    return idx;
                }

                idx++;
            }
        }

        return -1;
    }
}
