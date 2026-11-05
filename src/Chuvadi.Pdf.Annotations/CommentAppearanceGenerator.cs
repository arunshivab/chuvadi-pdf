// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.31 — appearances for comments that have no /AP (D-146 gap 6,
//        D-152): drawn from the annotation's own entries, as viewers do. In
//        memory only; the file is never changed. The icons are simple
//        drawings made by this code, not copies of any viewer's artwork.
// SPEC:  PDF 32000-1:2008 §12.5.6 (annotation types), §12.5.4 (border
//        styles), §12.5.6.7 (line endings).

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Forms.Appearance;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using Path = Chuvadi.Pdf.Graphics.Path;

namespace Chuvadi.Pdf.Annotations;

/// <summary>
/// Makes a normal appearance for a comment annotation from its own entries:
/// Highlight, Underline, Squiggly, StrikeOut, Square, Circle, Line, Polygon,
/// PolyLine, Ink, FreeText, Text (the seven standard note icons), Stamp (its
/// name in a box), Caret and FileAttachment (four icons).
/// </summary>
public static class CommentAppearanceGenerator
{
    private static readonly HashSet<string> Supported = new HashSet<string>(StringComparer.Ordinal)
    {
        "Highlight", "Underline", "Squiggly", "StrikeOut", "Square", "Circle", "Line", "Polygon",
        "PolyLine", "Ink", "FreeText", "Text", "Stamp", "Caret", "FileAttachment",
    };

    // The standard stamp names (§12.5.6.12) shown as upper-case words.
    private static readonly Dictionary<string, string> StampWords = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Approved"] = "APPROVED",
        ["Experimental"] = "EXPERIMENTAL",
        ["NotApproved"] = "NOT APPROVED",
        ["AsIs"] = "AS IS",
        ["Expired"] = "EXPIRED",
        ["NotForPublicRelease"] = "NOT FOR PUBLIC RELEASE",
        ["Confidential"] = "CONFIDENTIAL",
        ["Final"] = "FINAL",
        ["Sold"] = "SOLD",
        ["Departmental"] = "DEPARTMENTAL",
        ["ForComment"] = "FOR COMMENT",
        ["TopSecret"] = "TOP SECRET",
        ["Draft"] = "DRAFT",
        ["ForPublicRelease"] = "FOR PUBLIC RELEASE",
    };

    /// <summary>True when <paramref name="subtype"/> is a comment type this generator draws.</summary>
    /// <param name="subtype">The annotation <c>/Subtype</c>.</param>
    /// <returns>True when supported.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="subtype"/> is null.</exception>
    public static bool IsSupported(string subtype)
    {
        ArgumentNullException.ThrowIfNull(subtype);
        return Supported.Contains(subtype);
    }

    /// <summary>
    /// Returns a generated normal appearance for <paramref name="annotation"/>,
    /// or null when its type is not supported or it has no drawable size.
    /// </summary>
    /// <param name="annotation">The annotation dictionary.</param>
    /// <param name="objects">The document's objects.</param>
    /// <param name="acroForm">The document's <c>/AcroForm</c>, for FreeText's default <c>/DR</c>; may be null.</param>
    /// <param name="onWarning">Receives warnings; may be null.</param>
    /// <param name="pageNumber">The 1-based page number for warnings, or null.</param>
    /// <returns>A form XObject the size of the annotation's <c>/Rect</c>, or null.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="annotation"/> or <paramref name="objects"/> is null.</exception>
    public static PdfStream? Generate(PdfDictionary annotation, PdfObjectStore objects, PdfDictionary? acroForm, Action<PdfWarning>? onWarning, int? pageNumber)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        ArgumentNullException.ThrowIfNull(objects);
        string subtype = objects.ResolveAs<PdfName>(Get(annotation, "Subtype"))?.Value ?? string.Empty;
        if (!Supported.Contains(subtype) || objects.ResolveAs<PdfArray>(Get(annotation, "Rect")) is not PdfArray rectArray || rectArray.Count < 4)
        {
            return null;
        }

        double x0 = Math.Min(Num(objects, rectArray[0]), Num(objects, rectArray[2]));
        double y0 = Math.Min(Num(objects, rectArray[1]), Num(objects, rectArray[3]));
        double w = Math.Abs(Num(objects, rectArray[2]) - Num(objects, rectArray[0]));
        double h = Math.Abs(Num(objects, rectArray[3]) - Num(objects, rectArray[1]));
        if (w <= 0 || h <= 0)
        {
            return null;
        }

        Ctx c = new Ctx(annotation, objects, w, h, x0, y0);
        switch (subtype)
        {
            case "Highlight":
                Markup(c, MarkupKind.Highlight);
                break;
            case "Underline":
                Markup(c, MarkupKind.Underline);
                break;
            case "Squiggly":
                Markup(c, MarkupKind.Squiggly);
                break;
            case "StrikeOut":
                Markup(c, MarkupKind.StrikeOut);
                break;
            case "Square":
            case "Circle":
                Shape(c, subtype == "Circle");
                break;
            case "Line":
                Line(c);
                break;
            case "Polygon":
            case "PolyLine":
                Poly(c, subtype == "Polygon");
                break;
            case "Ink":
                Ink(c);
                break;
            case "FreeText":
                if (!FreeText(c, acroForm, onWarning, pageNumber))
                {
                    return null;
                }

                break;
            case "Text":
                NoteIcon(c);
                break;
            case "Stamp":
                Stamp(c);
                break;
            case "Caret":
                Caret(c);
                break;
            case "FileAttachment":
                AttachmentIcon(c);
                break;
        }

        return c.Content.ToFormXObject(w, h, c.Resources);
    }

    private enum MarkupKind
    {
        Highlight,
        Underline,
        Squiggly,
        StrikeOut,
    }

    // ── Text markup (§12.5.6.10) ────────────────────────────────────────────
    private static void Markup(Ctx c, MarkupKind kind)
    {
        double[]? q = c.Numbers("QuadPoints");
        if (q is null || q.Length < 8)
        {
            // No quads: the whole rectangle.
            q = new[] { c.X0, c.Y0 + c.H, c.X0 + c.W, c.Y0 + c.H, c.X0, c.Y0, c.X0 + c.W, c.Y0 };
        }

        PdfColorSpec color = c.Color("C", new PdfColorSpec(new[] { 1.0, 1.0, 0.0 }));
        c.Content.Op("q");
        c.Opacity(kind == MarkupKind.Highlight ? "Multiply" : null);
        c.Content.Op("cm", 1, 0, 0, 1, -c.X0, -c.Y0);
        for (int i = 0; i + 7 < q.Length; i += 8)
        {
            // Acrobat's order: upper-left, upper-right, lower-left, lower-right.
            (double ulx, double uly, double urx, double ury, double llx, double lly, double lrx, double lry) =
                (q[i], q[i + 1], q[i + 2], q[i + 3], q[i + 4], q[i + 5], q[i + 6], q[i + 7]);
            double height = Math.Abs(uly - lly);
            switch (kind)
            {
                case MarkupKind.Highlight:
                    c.Content.Fill(color).Op("m", ulx, uly).Op("l", urx, ury).Op("l", lrx, lry).Op("l", llx, lly).Op("h").Op("f");
                    break;
                case MarkupKind.Underline:
                    {
                        double lw = Math.Max(0.5, height / 16);
                        double y = lly + (height / 10);
                        c.Content.Stroke(color).Op("w", lw).Op("m", llx, y).Op("l", lrx, lry + (height / 10)).Op("S");
                        break;
                    }

                case MarkupKind.StrikeOut:
                    {
                        double lw = Math.Max(0.5, height / 16);
                        c.Content.Stroke(color).Op("w", lw).Op("m", llx, (lly + uly) / 2).Op("l", lrx, (lry + ury) / 2).Op("S");
                        break;
                    }

                case MarkupKind.Squiggly:
                    {
                        double lw = Math.Max(0.5, height / 18);
                        double amp = height / 12;
                        double step = Math.Max(1, height / 6);
                        double baseY = lly + amp;
                        c.Content.Stroke(color).Op("w", lw).Op("m", llx, baseY);
                        bool up = true;
                        for (double x = llx + step; x <= lrx + 0.001; x += step)
                        {
                            c.Content.Op("l", x, baseY + (up ? amp : -amp));
                            up = !up;
                        }

                        c.Content.Op("S");
                        break;
                    }
            }
        }

        c.Content.Op("Q");
    }

    // ── Square and Circle (§12.5.6.8) ───────────────────────────────────────
    private static void Shape(Ctx c, bool circle)
    {
        double bw = c.BorderWidth();
        double[] rd = c.Numbers("RD") ?? new double[4];
        double left = (rd.Length > 0 ? rd[0] : 0) + (bw / 2);
        double top = (rd.Length > 1 ? rd[1] : 0) + (bw / 2);
        double right = (rd.Length > 2 ? rd[2] : 0) + (bw / 2);
        double bottom = (rd.Length > 3 ? rd[3] : 0) + (bw / 2);
        double x = left;
        double y = bottom;
        double w = c.W - left - right;
        double h = c.H - top - bottom;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        PdfColorSpec stroke = c.Color("C", PdfColorSpec.Black);
        PdfColorSpec fill = c.Color("IC", default);
        c.Content.Op("q");
        c.Opacity(null);
        c.Dash();
        if (circle)
        {
            Ellipse(c.Content, x, y, w, h);
        }
        else
        {
            c.Content.Rect(x, y, w, h);
        }

        c.PaintPath(stroke, fill, bw, closed: true);
        c.Content.Op("Q");
    }

    private static void Ellipse(AppearanceContent content, double x, double y, double w, double h)
    {
        const double K = 0.5522847498;
        double rx = w / 2;
        double ry = h / 2;
        double cx = x + rx;
        double cy = y + ry;
        content.Op("m", cx + rx, cy)
            .Op("c", cx + rx, cy + (K * ry), cx + (K * rx), cy + ry, cx, cy + ry)
            .Op("c", cx - (K * rx), cy + ry, cx - rx, cy + (K * ry), cx - rx, cy)
            .Op("c", cx - rx, cy - (K * ry), cx - (K * rx), cy - ry, cx, cy - ry)
            .Op("c", cx + (K * rx), cy - ry, cx + rx, cy - (K * ry), cx + rx, cy)
            .Op("h");
    }

    // ── Line, Polygon, PolyLine, Ink (§12.5.6.7, .9, .13) ───────────────────
    private static void Line(Ctx c)
    {
        double[]? l = c.Numbers("L");
        if (l is null || l.Length < 4)
        {
            return;
        }

        string[] endings = c.Endings();
        DrawPolyline(c, new[] { (l[0], l[1]), (l[2], l[3]) }, false, endings.Length > 0 ? endings[0] : "None", endings.Length > 1 ? endings[1] : "None");
    }

    private static void Poly(Ctx c, bool closed)
    {
        double[]? v = c.Numbers("Vertices");
        if (v is null || v.Length < 4)
        {
            return;
        }

        List<(double, double)> points = new List<(double, double)>();
        for (int i = 0; i + 1 < v.Length; i += 2)
        {
            points.Add((v[i], v[i + 1]));
        }

        string[] endings = closed ? Array.Empty<string>() : c.Endings();
        DrawPolyline(c, points.ToArray(), closed, endings.Length > 0 ? endings[0] : "None", endings.Length > 1 ? endings[1] : "None");
    }

    private static void Ink(Ctx c)
    {
        if (c.Objects.ResolveAs<PdfArray>(Get(c.Annotation, "InkList")) is not PdfArray inkList)
        {
            return;
        }

        PdfColorSpec stroke = c.Color("C", PdfColorSpec.Black);
        c.Content.Op("q");
        c.Opacity(null);
        c.Content.Op("cm", 1, 0, 0, 1, -c.X0, -c.Y0).Op("w", c.BorderWidth()).Op("J", 1).Op("j", 1).Stroke(stroke);
        foreach (PdfPrimitive strokeRef in inkList)
        {
            if (c.Objects.ResolveAs<PdfArray>(strokeRef) is not PdfArray pts || pts.Count < 2)
            {
                continue;
            }

            c.Content.Op("m", Num(c.Objects, pts[0]), Num(c.Objects, pts[1]));
            for (int i = 2; i + 1 < pts.Count; i += 2)
            {
                c.Content.Op("l", Num(c.Objects, pts[i]), Num(c.Objects, pts[i + 1]));
            }

            c.Content.Op("S");
        }

        c.Content.Op("Q");
    }

    private static void DrawPolyline(Ctx c, (double X, double Y)[] points, bool closed, string startEnding, string endEnding)
    {
        PdfColorSpec stroke = c.Color("C", PdfColorSpec.Black);
        PdfColorSpec fill = c.Color("IC", default);
        double bw = c.BorderWidth();
        c.Content.Op("q");
        c.Opacity(null);
        c.Dash();
        c.Content.Op("cm", 1, 0, 0, 1, -c.X0, -c.Y0).Op("m", points[0].X, points[0].Y);
        for (int i = 1; i < points.Length; i++)
        {
            c.Content.Op("l", points[i].X, points[i].Y);
        }

        if (closed)
        {
            c.Content.Op("h");
        }

        c.PaintPath(stroke, closed ? fill : default, bw, closed);
        c.Content.Op("[] 0 d");
        if (!closed && points.Length >= 2)
        {
            Ending(c, startEnding, points[0], points[1], stroke, fill, bw);
            Ending(c, endEnding, points[^1], points[^2], stroke, fill, bw);
        }

        c.Content.Op("Q");
    }

    // A line ending at `at`, pointing away from `from` (§12.5.6.7, Table 176).
    private static void Ending(Ctx c, string kind, (double X, double Y) at, (double X, double Y) from, PdfColorSpec stroke, PdfColorSpec fill, double bw)
    {
        double dx = at.X - from.X;
        double dy = at.Y - from.Y;
        double len = Math.Sqrt((dx * dx) + (dy * dy));
        if (len == 0 || kind == "None")
        {
            return;
        }

        double ux = dx / len;
        double uy = dy / len;
        double s = Math.Max(3, bw * 3);
        (double X, double Y) P(double along, double across) => (at.X + (ux * along) - (uy * across), at.Y + (uy * along) + (ux * across));
        AppearanceContent k = c.Content;
        switch (kind)
        {
            case "Square":
                {
                    (double X, double Y) a = P(-s, -s);
                    (double X, double Y) b = P(s, -s);
                    (double X, double Y) d = P(s, s);
                    (double X, double Y) e = P(-s, s);
                    k.Op("m", a.X, a.Y).Op("l", b.X, b.Y).Op("l", d.X, d.Y).Op("l", e.X, e.Y).Op("h");
                    c.PaintPath(stroke, fill, bw, true);
                    break;
                }

            case "Circle":
                Ellipse(k, at.X - s, at.Y - s, 2 * s, 2 * s);
                c.PaintPath(stroke, fill, bw, true);
                break;
            case "Diamond":
                {
                    (double X, double Y) a = P(-s, 0);
                    (double X, double Y) b = P(0, -s);
                    (double X, double Y) d = P(s, 0);
                    (double X, double Y) e = P(0, s);
                    k.Op("m", a.X, a.Y).Op("l", b.X, b.Y).Op("l", d.X, d.Y).Op("l", e.X, e.Y).Op("h");
                    c.PaintPath(stroke, fill, bw, true);
                    break;
                }

            case "OpenArrow":
            case "ClosedArrow":
            case "ROpenArrow":
            case "RClosedArrow":
                {
                    double dir = kind.StartsWith('R') ? 1 : -1;
                    (double X, double Y) a = P(dir * 2 * s, -s);
                    (double X, double Y) b = P(dir * 2 * s, s);
                    k.Op("m", a.X, a.Y).Op("l", at.X, at.Y).Op("l", b.X, b.Y);
                    if (kind.EndsWith("ClosedArrow", StringComparison.Ordinal))
                    {
                        k.Op("h");
                        c.PaintPath(stroke, fill, bw, true);
                    }
                    else
                    {
                        k.Stroke(stroke).Op("w", bw).Op("S");
                    }

                    break;
                }

            case "Butt":
                {
                    (double X, double Y) a = P(0, -s);
                    (double X, double Y) b = P(0, s);
                    k.Stroke(stroke).Op("w", bw).Op("m", a.X, a.Y).Op("l", b.X, b.Y).Op("S");
                    break;
                }

            case "Slash":
                {
                    (double X, double Y) a = P(-s * 0.5, -s);
                    (double X, double Y) b = P(s * 0.5, s);
                    k.Stroke(stroke).Op("w", bw).Op("m", a.X, a.Y).Op("l", b.X, b.Y).Op("S");
                    break;
                }
        }
    }

    // ── FreeText (§12.5.6.6) ────────────────────────────────────────────────
    private static bool FreeText(Ctx c, PdfDictionary? acroForm, Action<PdfWarning>? onWarning, int? pageNumber)
    {
        string contents = Text(c.Objects, Get(c.Annotation, "Contents")) ?? string.Empty;
        string da = Text(c.Objects, Get(c.Annotation, "DA")) ?? Text(c.Objects, Get(acroForm, "DA")) ?? "/Helv 12 Tf 0 g";
        (string fontName, double size, PdfColorSpec color) = ParseDa(da);
        if (size <= 0)
        {
            size = 12;
        }

        PdfDictionary? dr = c.Objects.ResolveAs<PdfDictionary>(Get(acroForm, "DR"));
        PdfDictionary? drFonts = dr is null ? null : c.Objects.ResolveAs<PdfDictionary>(Get(dr, "Font"));
        AppearanceText text = new AppearanceText(c.Objects, c.Resources, drFonts, fontName, size);
        double[] rd = c.Numbers("RD") ?? new double[4];
        double left = rd.Length > 0 ? rd[0] : 0;
        double top = rd.Length > 1 ? rd[1] : 0;
        double right = rd.Length > 2 ? rd[2] : 0;
        double bottom = rd.Length > 3 ? rd[3] : 0;
        double w = c.W - left - right;
        double h = c.H - top - bottom;
        PdfColorSpec background = c.Color("C", default);
        double bw = c.Annotation.ContainsKey(PdfName.Intern("BS")) ? c.BorderWidth() : 0;
        c.Content.Op("q");
        c.Opacity(null);
        if (!background.IsEmpty)
        {
            c.Content.Fill(background).Rect(left, bottom, w, h).Op("f");
        }

        if (bw > 0)
        {
            c.Content.Stroke(PdfColorSpec.Black).Op("w", bw).Rect(left + (bw / 2), bottom + (bw / 2), w - bw, h - bw).Op("S");
        }

        c.Content.Rect(left, bottom, w, h).Op("W").Op("n");
        int q = (int)Num(c.Objects, Get(c.Annotation, "Q"));
        double pad = bw + 2;
        double lineHeight = size * 1.15;
        double y = bottom + h - pad - (text.Ascent * size);
        foreach (string line in Wrap(text, contents, w - (2 * pad)))
        {
            double lw = text.Measure(line);
            double x = q switch
            {
                1 => left + ((w - lw) / 2),
                2 => left + w - pad - lw,
                _ => left + pad,
            };
            text.Show(c.Content, color, line, x, y);
            y -= lineHeight;
        }

        c.Content.Op("Q");
        text.Finish();
        if (onWarning is not null)
        {
            foreach (string face in text.Substitutions)
            {
                onWarning(new PdfWarning(PdfWarningKind.FontSubstituted, pageNumber, "FreeText annotation", "its font /" + fontName + " cannot show some characters; they are drawn with " + face + " from Chuvadi.Fonts"));
            }
        }

        return true;
    }

    private static List<string> Wrap(AppearanceText text, string value, double room)
    {
        List<string> lines = new List<string>();
        foreach (string paragraph in value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            string current = string.Empty;
            foreach (string word in paragraph.Split(' '))
            {
                string candidate = current.Length == 0 ? word : current + " " + word;
                if (current.Length > 0 && text.Measure(candidate) > room)
                {
                    lines.Add(current);
                    current = word;
                }
                else
                {
                    current = candidate;
                }
            }

            lines.Add(current);
        }

        return lines;
    }

    // ── Icons: Text notes (§12.5.6.4) and file attachments (§12.5.6.15) ─────
    // Each icon is drawn in a 20 x 20 box, scaled to fit the /Rect, filled
    // with /C and outlined in black.
    private static void NoteIcon(Ctx c)
    {
        string name = c.Objects.ResolveAs<PdfName>(Get(c.Annotation, "Name"))?.Value ?? "Note";
        PdfColorSpec fill = c.Color("C", new PdfColorSpec(new[] { 1.0, 0.82, 0.0 }));
        c.IconBox(out AppearanceContent k);
        k.Fill(fill).Stroke(PdfColorSpec.Black).Op("w", 1).Op("j", 1);
        switch (name)
        {
            case "Comment":
                k.Op("m", 2, 6).Op("l", 18, 6).Op("l", 18, 18).Op("l", 2, 18).Op("h").Op("B");
                k.Op("m", 5, 6).Op("l", 4, 2).Op("l", 9, 6).Op("h").Op("B");
                Lines(k, 4, 15, 16, 3, 3);
                break;
            case "Key":
                k.Op("m", 14, 13).Op("c", 14, 16, 16, 18, 13.5, 18).Op("c", 11, 18, 9, 16, 9, 13).Op("c", 9, 10, 11, 8, 13.5, 8).Op("c", 16, 8, 14, 10, 14, 13).Op("h").Op("B");
                k.Op("m", 10, 11).Op("l", 3, 4).Op("l", 3, 2).Op("l", 5, 2).Op("l", 5, 4).Op("l", 7, 4).Op("l", 7, 6).Op("l", 12, 10).Op("h").Op("B");
                break;
            case "Help":
                Ellipse(k, 2, 2, 16, 16);
                k.Op("B");
                Glyph(k, "Helvetica-Bold", '?', 6.5, 5, 13, PdfColorSpec.Black);
                break;
            case "NewParagraph":
                k.Op("m", 10, 19).Op("l", 17, 10).Op("l", 3, 10).Op("h").Op("B");
                Glyph(k, "Helvetica-Bold", 'N', 2, 1, 8, PdfColorSpec.Black);
                Glyph(k, "Helvetica-Bold", 'P', 11, 1, 8, PdfColorSpec.Black);
                break;
            case "Paragraph":
                Ellipse(k, 2, 2, 16, 16);
                k.Op("B");
                Glyph(k, "Helvetica-Bold", '¶', 5.5, 5, 12, PdfColorSpec.Black);
                break;
            case "Insert":
                k.Op("m", 2, 3).Op("l", 10, 18).Op("l", 18, 3).Op("l", 14, 3).Op("l", 10, 11).Op("l", 6, 3).Op("h").Op("B");
                break;
            default:
                // Note: a page with a folded corner and three lines.
                k.Op("m", 3, 2).Op("l", 17, 2).Op("l", 17, 14).Op("l", 13, 18).Op("l", 3, 18).Op("h").Op("B");
                k.Op("m", 13, 18).Op("l", 13, 14).Op("l", 17, 14).Op("S");
                Lines(k, 6, 12, 14, 3, 3);
                break;
        }

        k.Op("Q");
    }

    private static void AttachmentIcon(Ctx c)
    {
        string name = c.Objects.ResolveAs<PdfName>(Get(c.Annotation, "Name"))?.Value ?? "PushPin";
        PdfColorSpec fill = c.Color("C", new PdfColorSpec(new[] { 0.6, 0.6, 0.6 }));
        c.IconBox(out AppearanceContent k);
        k.Fill(fill).Stroke(PdfColorSpec.Black).Op("w", 1).Op("j", 1).Op("J", 1);
        switch (name)
        {
            case "Paperclip":
                k.Op("w", 1.6).Op("m", 13, 6).Op("l", 13, 15).Op("c", 13, 18.5, 7, 18.5, 7, 15).Op("l", 7, 4.5)
                    .Op("c", 7, 1.5, 11, 1.5, 11, 4.5).Op("l", 11, 13).Op("c", 11, 14.5, 9, 14.5, 9, 13).Op("l", 9, 6).Op("S");
                break;
            case "Graph":
                k.Op("m", 3, 18).Op("l", 3, 3).Op("l", 18, 3).Op("S");
                k.Rect(5, 3, 3, 6).Op("B").Rect(9.5, 3, 3, 10).Op("B").Rect(14, 3, 3, 13).Op("B");
                break;
            case "Tag":
                k.Op("m", 2, 10).Op("l", 9, 17).Op("l", 18, 17).Op("l", 18, 3).Op("l", 9, 3).Op("h").Op("B");
                Ellipse(k, 13, 8.5, 3, 3);
                k.Op("S");
                break;
            default:
                // PushPin: a round head on a needle, tilted.
                k.Op("m", 4, 2).Op("l", 9.5, 9).Op("S");
                Ellipse(k, 8, 8, 9, 9);
                k.Op("B");
                k.Op("m", 6.5, 11).Op("l", 11, 6.5).Op("S");
                break;
        }

        k.Op("Q");
    }

    private static void Lines(AppearanceContent k, double x0, double yTop, double x1, double gap, int count)
    {
        for (int i = 0; i < count; i++)
        {
            double y = yTop - (i * gap);
            k.Op("m", x0, y).Op("l", x1, y);
        }

        k.Op("S");
    }

    // ── Stamp (§12.5.6.12) and Caret (§12.5.6.11) ───────────────────────────
    private static void Stamp(Ctx c)
    {
        string name = c.Objects.ResolveAs<PdfName>(Get(c.Annotation, "Name"))?.Value ?? "Draft";
        string words = StampWords.TryGetValue(name, out string? w) ? w : name;

        // O8: a stamp without /C is red, as most viewers draw stamps.
        PdfColorSpec color = c.Color("C", new PdfColorSpec(new[] { 0.8, 0.0, 0.0 }));
        double bw = Math.Clamp(Math.Min(c.W, c.H) / 18, 1, 4);
        c.Content.Op("q");
        c.Opacity(null);
        double r = Math.Min(c.W, c.H) / 8;
        RoundRect(c.Content, bw / 2, bw / 2, c.W - bw, c.H - bw, r);
        c.Content.Stroke(color).Op("w", bw).Op("S");
        double textWidth1000 = 0;
        foreach (char ch in words)
        {
            textWidth1000 += Chuvadi.Pdf.Fonts.Rendering.Standard14Widths.GetWidth("Helvetica-Bold", ch < 256 ? ch : '?');
        }

        double size = Math.Min(c.H * 0.6, textWidth1000 > 0 ? (c.W - (4 * bw)) * 0.92 * 1000 / textWidth1000 : c.H * 0.6);
        double x = (c.W - (textWidth1000 * size / 1000)) / 2;
        double baseline = (c.H - (0.718 * size)) / 2;
        foreach (char ch in words)
        {
            Glyph(c.Content, "Helvetica-Bold", ch, x, baseline, size, color);
            x += Chuvadi.Pdf.Fonts.Rendering.Standard14Widths.GetWidth("Helvetica-Bold", ch < 256 ? ch : '?') * size / 1000;
        }

        c.Content.Op("Q");
    }

    private static void RoundRect(AppearanceContent k, double x, double y, double w, double h, double r)
    {
        const double K = 0.5522847498;
        r = Math.Min(r, Math.Min(w, h) / 2);
        double kr = K * r;
        k.Op("m", x + r, y).Op("l", x + w - r, y).Op("c", x + w - r + kr, y, x + w, y + r - kr, x + w, y + r)
            .Op("l", x + w, y + h - r).Op("c", x + w, y + h - r + kr, x + w - r + kr, y + h, x + w - r, y + h)
            .Op("l", x + r, y + h).Op("c", x + r - kr, y + h, x, y + h - r + kr, x, y + h - r)
            .Op("l", x, y + r).Op("c", x, y + r - kr, x + r - kr, y, x + r, y).Op("h");
    }

    private static void Caret(Ctx c)
    {
        double[] rd = c.Numbers("RD") ?? new double[4];
        double left = rd.Length > 0 ? rd[0] : 0;
        double top = rd.Length > 1 ? rd[1] : 0;
        double right = rd.Length > 2 ? rd[2] : 0;
        double bottom = rd.Length > 3 ? rd[3] : 0;
        double w = c.W - left - right;
        double h = c.H - top - bottom;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        bool paragraph = c.Objects.ResolveAs<PdfName>(Get(c.Annotation, "Sy"))?.Value == "P";
        double cw = paragraph ? w / 2 : w;
        PdfColorSpec color = c.Color("C", new PdfColorSpec(new[] { 0.0, 0.0, 1.0 }));
        c.Content.Op("q");
        c.Opacity(null);
        c.Content.Fill(color)
            .Op("m", left, bottom)
            .Op("c", left + (cw * 0.3), bottom + (h * 0.15), left + (cw * 0.45), bottom + (h * 0.6), left + (cw / 2), bottom + h)
            .Op("c", left + (cw * 0.55), bottom + (h * 0.6), left + (cw * 0.7), bottom + (h * 0.15), left + cw, bottom)
            .Op("h").Op("f");
        if (paragraph)
        {
            double size = Math.Min(h, w / 2) * 1.1;
            Glyph(c.Content, "Helvetica", '¶', left + cw + ((w - cw - (0.537 * size)) / 2), bottom, size, color);
        }

        c.Content.Op("Q");
    }

    // A standard 14 glyph drawn as an outline (not text, so it adds nothing to
    // text extraction).
    private static void Glyph(AppearanceContent k, string font, char ch, double x, double y, double size, PdfColorSpec color)
    {
        Path p = Chuvadi.Pdf.Fonts.Rendering.Standard14Outlines.GetGlyphPath(font, ch);
        if (p.IsEmpty)
        {
            return;
        }

        double s = size / 1000;
        k.Fill(color);
        foreach (PathSegment seg in p.Segments)
        {
            switch (seg.Kind)
            {
                case PathSegmentKind.MoveTo:
                    k.Op("m", x + (seg.P0.X * s), y + (seg.P0.Y * s));
                    break;
                case PathSegmentKind.LineTo:
                    k.Op("l", x + (seg.P0.X * s), y + (seg.P0.Y * s));
                    break;
                case PathSegmentKind.CubicBezierTo:
                    k.Op("c", x + (seg.P0.X * s), y + (seg.P0.Y * s), x + (seg.P1.X * s), y + (seg.P1.Y * s), x + (seg.P2.X * s), y + (seg.P2.Y * s));
                    break;
                case PathSegmentKind.ClosePath:
                    k.Op("h");
                    break;
            }
        }

        k.Op("f");
    }

    private static (string Font, double Size, PdfColorSpec Color) ParseDa(string da)
    {
        string font = "Helv";
        double size = 0;
        PdfColorSpec color = PdfColorSpec.Black;
        List<string> operands = new List<string>();
        foreach (string token in da.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            switch (token)
            {
                case "Tf" when operands.Count >= 2:
                    font = operands[^2].TrimStart('/');
                    size = ParseNum(operands[^1]);
                    break;
                case "g" when operands.Count >= 1:
                    color = new PdfColorSpec(new[] { ParseNum(operands[^1]) });
                    break;
                case "rg" when operands.Count >= 3:
                    color = new PdfColorSpec(new[] { ParseNum(operands[^3]), ParseNum(operands[^2]), ParseNum(operands[^1]) });
                    break;
                default:
                    operands.Add(token);
                    continue;
            }

            operands.Clear();
        }

        return (font, size, color);
    }

    private static double ParseNum(string s) =>
        double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0;

    private static PdfPrimitive Get(PdfDictionary? dict, string key) =>
        dict is not null && dict.TryGetValue(PdfName.Intern(key), out PdfPrimitive? v) && v is not null ? v : PdfNull.Value;

    private static double Num(PdfObjectStore objects, PdfPrimitive value) => objects.Resolve(value) switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0,
    };

    private static string? Text(PdfObjectStore objects, PdfPrimitive value) => objects.Resolve(value) is PdfString s ? s.ToTextString() : null;

    // The annotation being drawn and the stream being built.
    private sealed class Ctx
    {
        private int _gsCount;

        internal Ctx(PdfDictionary annotation, PdfObjectStore objects, double w, double h, double x0, double y0)
        {
            Annotation = annotation;
            Objects = objects;
            W = w;
            H = h;
            X0 = x0;
            Y0 = y0;
        }

        internal PdfDictionary Annotation { get; }

        internal PdfObjectStore Objects { get; }

        internal double W { get; }

        internal double H { get; }

        internal double X0 { get; }

        internal double Y0 { get; }

        internal AppearanceContent Content { get; } = new AppearanceContent();

        internal PdfDictionary Resources { get; } = new PdfDictionary();

        internal PdfColorSpec Color(string key, PdfColorSpec fallback)
        {
            if (!Annotation.ContainsKey(PdfName.Intern(key)))
            {
                return fallback;
            }

            return PdfColorSpec.FromArray(Objects.ResolveAs<PdfArray>(Get(Annotation, key)), Objects);
        }

        internal double[]? Numbers(string key)
        {
            if (Objects.ResolveAs<PdfArray>(Get(Annotation, key)) is not PdfArray a)
            {
                return null;
            }

            double[] values = new double[a.Count];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = Num(Objects, a[i]);
            }

            return values;
        }

        internal string[] Endings()
        {
            if (Objects.ResolveAs<PdfArray>(Get(Annotation, "LE")) is not PdfArray a)
            {
                return Array.Empty<string>();
            }

            string[] names = new string[a.Count];
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = Objects.ResolveAs<PdfName>(a[i])?.Value ?? "None";
            }

            return names;
        }

        // /BS /W, else /Border [h v w], else 1.
        internal double BorderWidth()
        {
            if (Objects.ResolveAs<PdfDictionary>(Get(Annotation, "BS")) is PdfDictionary bs && bs.ContainsKey(PdfName.Intern("W")))
            {
                return Num(Objects, Get(bs, "W"));
            }

            if (Objects.ResolveAs<PdfArray>(Get(Annotation, "Border")) is PdfArray border && border.Count >= 3)
            {
                return Num(Objects, border[2]);
            }

            return 1;
        }

        internal void Dash()
        {
            if (Objects.ResolveAs<PdfDictionary>(Get(Annotation, "BS")) is PdfDictionary bs
                && Objects.ResolveAs<PdfName>(Get(bs, "S"))?.Value == "D")
            {
                double[] dash = Objects.ResolveAs<PdfArray>(Get(bs, "D")) is PdfArray d && d.Count > 0
                    ? new[] { Num(Objects, d[0]), d.Count > 1 ? Num(Objects, d[1]) : Num(Objects, d[0]) }
                    : new[] { 3.0, 3.0 };
                Content.Raw("[" + AppearanceContent.Num(dash[0]) + " " + AppearanceContent.Num(dash[1]) + "] 0 d\n");
            }
        }

        // Stroke and/or fill the current path.
        internal void PaintPath(PdfColorSpec stroke, PdfColorSpec fill, double width, bool closed)
        {
            bool doFill = closed && !fill.IsEmpty && fill.Components is not null;
            bool doStroke = width > 0 && !stroke.IsEmpty && stroke.Components is not null;
            if (doFill)
            {
                Content.Fill(fill);
            }

            if (doStroke)
            {
                Content.Stroke(stroke).Op("w", width);
            }

            Content.Op(doFill && doStroke ? "B" : doFill ? "f" : doStroke ? "S" : "n");
        }

        // /CA opacity (and a blend mode) through an ExtGState.
        internal void Opacity(string? blend)
        {
            double ca = Annotation.ContainsKey(PdfName.Intern("CA")) ? Num(Objects, Get(Annotation, "CA")) : 1;
            if (ca >= 1 && blend is null)
            {
                return;
            }

            PdfDictionary gs = new PdfDictionary();
            gs.Set(PdfName.Intern("Type"), PdfName.Intern("ExtGState"));
            gs.Set(PdfName.Intern("CA"), new PdfReal(ca));
            gs.Set(PdfName.Intern("ca"), new PdfReal(ca));
            if (blend is not null)
            {
                gs.Set(PdfName.Intern("BM"), PdfName.Intern(blend));
            }

            if (Objects.ResolveAs<PdfDictionary>(Get(Resources, "ExtGState")) is not PdfDictionary states)
            {
                states = new PdfDictionary();
                Resources.Set(PdfName.Intern("ExtGState"), states);
            }

            string name = "GS" + (++_gsCount).ToString(System.Globalization.CultureInfo.InvariantCulture);
            states.Set(PdfName.Intern(name), gs);
            Content.Raw("/" + name + " gs\n");
        }

        // q, opacity and a transform onto a 20 x 20 icon box at the top-left of the /Rect.
        internal void IconBox(out AppearanceContent content)
        {
            content = Content;
            double scale = Math.Min(W, H) / 20;
            Content.Op("q");
            Opacity(null);
            Content.Op("cm", scale, 0, 0, scale, 0, H - (20 * scale));
        }
    }
}
