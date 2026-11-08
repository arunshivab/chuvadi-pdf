// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.5.2 — Annotation dictionaries
//        PDF 32000-1:2008 §12.5.6 — Annotation types
// PHASE: Phase 1.1 — Chuvadi.Pdf.Annotations
//        v2.0.1 — extended with shape annotation parsing
// Reads annotations from a PDF page's /Annots array.

using System;
using System.Collections.Generic;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Forms;
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

        return GetAnnotations(document, pageIndex, new DestinationResolver(document));
    }

    // One resolver per read: the page map and named destinations are built
    // once, on the first link that needs them (3.31.2).
    private static IReadOnlyList<PdfAnnotation> GetAnnotations(
        PdfDocument document, int pageIndex, DestinationResolver destinations)
    {
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

            PdfAnnotation? annotation = DecodeAnnotation(annotDict, store, pageIndex, destinations);

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
        DestinationResolver destinations = new DestinationResolver(document);

        for (int i = 0; i < document.PageCount; i++)
        {
            all.AddRange(GetAnnotations(document, i, destinations));
        }

        return all;
    }

    // ── Subtype dispatch ──────────────────────────────────────────────────

    private static PdfAnnotation? DecodeAnnotation(
        PdfDictionary dict, PdfObjectStore store, int pageIndex, DestinationResolver destinations)
    {
        // Common fields
        RectangleF rect = ReadRect(dict);
        string? contents = ReadString(dict, PdfName.Intern("Contents"));
        string? author = ReadString(dict, PdfName.Intern("T"));
        ColorF? color = ReadColor(dict, PdfName.Intern("C"));
        float opacity = ReadOpacity(dict);

        // Subtype dispatch
        if (!dict.TryGetValue(PdfName.Intern("Subtype"), out PdfPrimitive? subPrim) ||
            subPrim is not PdfName subName)
        {
            return null;
        }

        return subName.Value switch
        {
            "Text" => ReadText(pageIndex, rect, contents, color, author, opacity, dict),
            "Link" => ReadLink(pageIndex, rect, contents, dict, store, destinations),
            "FreeText" => ReadFreeText(pageIndex, rect, contents, color, author, opacity, dict),
            "Highlight" => ReadMarkup(AnnotationType.Highlight, pageIndex, rect, contents, color, author, opacity, dict),
            "Underline" => ReadMarkup(AnnotationType.Underline, pageIndex, rect, contents, color, author, opacity, dict),
            "Squiggly" => ReadMarkup(AnnotationType.Squiggly, pageIndex, rect, contents, color, author, opacity, dict),
            "StrikeOut" => ReadMarkup(AnnotationType.StrikeOut, pageIndex, rect, contents, color, author, opacity, dict),
            "Stamp" => ReadStamp(pageIndex, rect, contents, color, author, opacity, dict),
            "Ink" => ReadInk(pageIndex, rect, contents, color, author, opacity, dict),
            "Square" => ReadSquare(pageIndex, rect, contents, color, author, opacity, dict),
            "Circle" => ReadCircle(pageIndex, rect, contents, color, author, opacity, dict),
            "Line" => ReadLineAnnotation(pageIndex, rect, contents, color, author, opacity, dict),
            "Polygon" => ReadPolygon(pageIndex, rect, contents, color, author, opacity, dict),
            "PolyLine" => ReadPolyLine(pageIndex, rect, contents, color, author, opacity, dict),
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
        PdfDictionary dict, PdfObjectStore store, DestinationResolver destinations)
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
                    if (destinations.ResolveDestination(destPrim) is PdfDestination dest)
                    {
                        return new LinkAnnotation(pageIndex, rect, dest.PageIndex, contents)
                        {
                            Destination = dest,
                        };
                    }
                }
            }
        }

        // /Dest: an explicit destination or, since 3.31.2, a named one
        if (dict.TryGetValue(PdfName.Intern("Dest"), out PdfPrimitive? directDest))
        {
            if (destinations.ResolveDestination(directDest) is PdfDestination dest)
            {
                return new LinkAnnotation(pageIndex, rect, dest.PageIndex, contents)
                {
                    Destination = dest,
                };
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

    private static SquareAnnotation ReadSquare(
        int pageIndex, RectangleF rect, string? contents, ColorF? color,
        string? author, float opacity, PdfDictionary dict)
    {
        BorderStyle? bs = ReadBorderStyle(dict);
        ColorF? ic = ReadColor(dict, PdfName.Intern("IC"));
        return new SquareAnnotation(pageIndex, rect, bs, ic, contents, color, author, opacity);
    }

    private static CircleAnnotation ReadCircle(
        int pageIndex, RectangleF rect, string? contents, ColorF? color,
        string? author, float opacity, PdfDictionary dict)
    {
        BorderStyle? bs = ReadBorderStyle(dict);
        ColorF? ic = ReadColor(dict, PdfName.Intern("IC"));
        return new CircleAnnotation(pageIndex, rect, bs, ic, contents, color, author, opacity);
    }

    private static LineAnnotation? ReadLineAnnotation(
        int pageIndex, RectangleF rect, string? contents, ColorF? color,
        string? author, float opacity, PdfDictionary dict)
    {
        if (!dict.TryGetValue(PdfName.Intern("L"), out PdfPrimitive? lPrim) ||
            lPrim is not PdfArray lArr || lArr.Count < 4)
        {
            return null;
        }

        PointF start = new PointF(ToDouble(lArr[0]), ToDouble(lArr[1]));
        PointF end = new PointF(ToDouble(lArr[2]), ToDouble(lArr[3]));

        BorderStyle? bs = ReadBorderStyle(dict);
        (LineEnding s, LineEnding e) = ReadLineEndings(dict);

        return new LineAnnotation(
            pageIndex, rect, start, end, bs, s, e,
            contents, color, author, opacity);
    }

    private static PolygonAnnotation? ReadPolygon(
        int pageIndex, RectangleF rect, string? contents, ColorF? color,
        string? author, float opacity, PdfDictionary dict)
    {
        IReadOnlyList<PointF>? vertices = ReadVertices(dict);

        if (vertices is null || vertices.Count < 3)
        {
            return null;
        }

        BorderStyle? bs = ReadBorderStyle(dict);
        ColorF? ic = ReadColor(dict, PdfName.Intern("IC"));

        return new PolygonAnnotation(
            pageIndex, rect, vertices, bs, ic,
            contents, color, author, opacity);
    }

    private static PolyLineAnnotation? ReadPolyLine(
        int pageIndex, RectangleF rect, string? contents, ColorF? color,
        string? author, float opacity, PdfDictionary dict)
    {
        IReadOnlyList<PointF>? vertices = ReadVertices(dict);

        if (vertices is null || vertices.Count < 2)
        {
            return null;
        }

        BorderStyle? bs = ReadBorderStyle(dict);
        (LineEnding s, LineEnding e) = ReadLineEndings(dict);

        return new PolyLineAnnotation(
            pageIndex, rect, vertices, bs, s, e,
            contents, color, author, opacity);
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

    private static BorderStyle? ReadBorderStyle(PdfDictionary dict)
    {
        if (!dict.TryGetValue(PdfName.Intern("BS"), out PdfPrimitive? bsPrim) ||
            bsPrim is not PdfDictionary bs)
        {
            return null;
        }

        float width = 1f;

        if (bs.TryGetValue(PdfName.Intern("W"), out PdfPrimitive? wPrim))
        {
            width = (float)ToDouble(wPrim);
        }

        BorderStyleType style = BorderStyleType.Solid;

        if (bs.TryGetValue(PdfName.Intern("S"), out PdfPrimitive? sPrim) && sPrim is PdfName sn)
        {
            style = sn.Value switch
            {
                "S" => BorderStyleType.Solid,
                "D" => BorderStyleType.Dashed,
                "B" => BorderStyleType.Beveled,
                "I" => BorderStyleType.Inset,
                "U" => BorderStyleType.Underline,
                _ => BorderStyleType.Solid,
            };
        }

        IReadOnlyList<float>? dashPattern = null;

        if (bs.TryGetValue(PdfName.Intern("D"), out PdfPrimitive? dPrim) && dPrim is PdfArray dArr)
        {
            List<float> pattern = new List<float>(dArr.Count);

            for (int i = 0; i < dArr.Count; i++)
            {
                pattern.Add((float)ToDouble(dArr[i]));
            }

            if (pattern.Count > 0)
            {
                dashPattern = pattern;
            }
        }

        return new BorderStyle(width, style, dashPattern);
    }

    private static (LineEnding Start, LineEnding End) ReadLineEndings(PdfDictionary dict)
    {
        if (!dict.TryGetValue(PdfName.Intern("LE"), out PdfPrimitive? lePrim) ||
            lePrim is not PdfArray leArr || leArr.Count < 2)
        {
            return (LineEnding.None, LineEnding.None);
        }

        return (ParseLineEnding(leArr[0]), ParseLineEnding(leArr[1]));
    }

    private static LineEnding ParseLineEnding(PdfPrimitive p)
    {
        if (p is not PdfName n)
        {
            return LineEnding.None;
        }

        return n.Value switch
        {
            "None" => LineEnding.None,
            "Square" => LineEnding.Square,
            "Circle" => LineEnding.Circle,
            "Diamond" => LineEnding.Diamond,
            "OpenArrow" => LineEnding.OpenArrow,
            "ClosedArrow" => LineEnding.ClosedArrow,
            "Butt" => LineEnding.Butt,
            "ROpenArrow" => LineEnding.ROpenArrow,
            "RClosedArrow" => LineEnding.RClosedArrow,
            "Slash" => LineEnding.Slash,
            _ => LineEnding.None,
        };
    }

    private static IReadOnlyList<PointF>? ReadVertices(PdfDictionary dict)
    {
        if (!dict.TryGetValue(PdfName.Intern("Vertices"), out PdfPrimitive? vPrim) ||
            vPrim is not PdfArray vArr)
        {
            return null;
        }

        List<PointF> vertices = new List<PointF>(vArr.Count / 2);

        for (int i = 0; i + 1 < vArr.Count; i += 2)
        {
            double x = ToDouble(vArr[i]);
            double y = ToDouble(vArr[i + 1]);
            vertices.Add(new PointF(x, y));
        }

        return vertices.Count > 0 ? vertices : null;
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
}
