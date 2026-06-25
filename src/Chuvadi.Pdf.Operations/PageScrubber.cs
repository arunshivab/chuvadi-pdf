// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.5 (paths); §9.4 (text objects/showing); §9.2.4 (glyph metrics)
// Region-aware content-stream scrubber backing PageCropMode.Scrub.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Content;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// Rewrites a single page content stream so that geometry, text, and images
/// outside the crop rectangle are physically removed (byte level), content
/// crossing the boundary is clipped to its in-box portion, and in-box content
/// is preserved verbatim. Vector paths are geometrically clipped; off-box text
/// glyphs are dropped (simple fonts, glyph level); CID/Type0 shows are kept or
/// dropped as a unit. Image cropping is layered on top.
/// </summary>
internal static class PageScrubber
{
    private const int BezierSegments = 16;

    /// <summary>Per-font metrics needed to advance and test glyphs.</summary>
    private sealed class FontMetrics
    {
        internal bool IsCid;
        internal int FirstChar;
        internal double[] Widths = Array.Empty<double>();
        internal double MissingWidth;
        internal double DefaultWidth = 500;
    }

    /// <summary>Mutable text-object state (PDF 32000-1 §9.4.1).</summary>
    private sealed class TextState
    {
        internal Matrix3x3 Tm = Matrix3x3.Identity;
        internal Matrix3x3 Tlm = Matrix3x3.Identity;
        internal double FontSize;
        internal double CharSpacing;
        internal double WordSpacing;
        internal double HScale = 1.0;
        internal double Leading;
        internal double Rise;
        internal FontMetrics? Font;
    }

    /// <summary>
    /// Scrubs <paramref name="content"/> to the crop rectangle [x0,y0,x1,y1] (page space),
    /// resolving fonts through <paramref name="resources"/>/<paramref name="resolver"/>.
    /// </summary>
    internal static ScrubResult Scrub(
        byte[] content,
        double x0, double y0, double x1, double y1,
        PdfDictionary? resources,
        IPdfObjectResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(resolver);

        StringBuilder output = new StringBuilder(content.Length + 64);
        List<(string Name, PdfStream Stream)> newXObjects = new List<(string, PdfStream)>();

        // Hard-clip backstop: confines anything echoed verbatim (e.g. images,
        // before image cropping is applied) to the crop box.
        output.Append("q ")
              .Append(F(x0)).Append(' ').Append(F(y0)).Append(' ')
              .Append(F(x1 - x0)).Append(' ').Append(F(y1 - y0))
              .Append(" re W n\n");

        Matrix3x3 ctm = Matrix3x3.Identity;
        Stack<Matrix3x3> ctmStack = new Stack<Matrix3x3>();

        TextState text = new TextState();
        Stack<TextState> textStack = new Stack<TextState>();
        Dictionary<string, FontMetrics?> fontCache = new Dictionary<string, FontMetrics?>();

        List<string> operandTexts = new List<string>();
        List<double> operandNums = new List<double>();

        List<List<(double X, double Y)>> subpaths = new List<List<(double X, double Y)>>();
        StringBuilder rawPath = new StringBuilder();
        double curX = 0, curY = 0, startX = 0, startY = 0;
        bool pendingClip = false;

        using MemoryStream ms = new MemoryStream(content);
        using PdfTokenizer tok = new PdfTokenizer(ms, leaveOpen: false);

        void ResetPath()
        {
            subpaths = new List<List<(double X, double Y)>>();
            rawPath.Clear();
            pendingClip = false;
        }

        while (true)
        {
            PdfToken token = tok.Read();
            if (token.IsEndOfStream)
            {
                break;
            }

            if (token.Type != PdfTokenType.Keyword)
            {
                operandTexts.Add(token.Type == PdfTokenType.Name ? "/" + token.RawText : token.RawText);
                if (token.IsNumeric &&
                    double.TryParse(token.RawText, NumberStyles.Float, CultureInfo.InvariantCulture, out double nv))
                {
                    operandNums.Add(nv);
                }

                continue;
            }

            string op = token.RawText;

            switch (op)
            {
                case "cm":
                    if (operandNums.Count >= 6)
                    {
                        Matrix3x3 m = new Matrix3x3(
                            operandNums[0], operandNums[1], operandNums[2],
                            operandNums[3], operandNums[4], operandNums[5]);
                        ctm = m.Multiply(ctm);
                    }

                    EchoVerbatim(output, operandTexts, op);
                    break;

                case "q":
                    ctmStack.Push(ctm);
                    textStack.Push(Clone(text));
                    EchoVerbatim(output, operandTexts, op);
                    break;

                case "Q":
                    if (ctmStack.Count > 0)
                    {
                        ctm = ctmStack.Pop();
                    }

                    if (textStack.Count > 0)
                    {
                        text = textStack.Pop();
                    }

                    EchoVerbatim(output, operandTexts, op);
                    break;

                // ── Text object / state (echo verbatim + track) ──────────────
                case "BT":
                    text.Tm = Matrix3x3.Identity;
                    text.Tlm = Matrix3x3.Identity;
                    EchoVerbatim(output, operandTexts, op);
                    break;

                case "ET":
                    EchoVerbatim(output, operandTexts, op);
                    break;

                case "Tf":
                    if (operandTexts.Count >= 1)
                    {
                        string fontName = operandTexts[0].TrimStart('/');
                        text.Font = ResolveMetrics(fontName, resources, resolver, fontCache);
                    }

                    if (operandNums.Count >= 1)
                    {
                        text.FontSize = operandNums[^1];
                    }

                    EchoVerbatim(output, operandTexts, op);
                    break;

                case "Td":
                    if (operandNums.Count >= 2)
                    {
                        ApplyTd(text, operandNums[0], operandNums[1]);
                    }

                    EchoVerbatim(output, operandTexts, op);
                    break;

                case "TD":
                    if (operandNums.Count >= 2)
                    {
                        text.Leading = -operandNums[1];
                        ApplyTd(text, operandNums[0], operandNums[1]);
                    }

                    EchoVerbatim(output, operandTexts, op);
                    break;

                case "Tm":
                    if (operandNums.Count >= 6)
                    {
                        Matrix3x3 tm = new Matrix3x3(
                            operandNums[0], operandNums[1], operandNums[2],
                            operandNums[3], operandNums[4], operandNums[5]);
                        text.Tm = tm;
                        text.Tlm = tm;
                    }

                    EchoVerbatim(output, operandTexts, op);
                    break;

                case "T*":
                    ApplyTd(text, 0, -text.Leading);
                    EchoVerbatim(output, operandTexts, op);
                    break;

                case "Tc":
                    if (operandNums.Count >= 1) { text.CharSpacing = operandNums[0]; }
                    EchoVerbatim(output, operandTexts, op);
                    break;

                case "Tw":
                    if (operandNums.Count >= 1) { text.WordSpacing = operandNums[0]; }
                    EchoVerbatim(output, operandTexts, op);
                    break;

                case "Tz":
                    if (operandNums.Count >= 1) { text.HScale = operandNums[0] / 100.0; }
                    EchoVerbatim(output, operandTexts, op);
                    break;

                case "TL":
                    if (operandNums.Count >= 1) { text.Leading = operandNums[0]; }
                    EchoVerbatim(output, operandTexts, op);
                    break;

                case "Ts":
                    if (operandNums.Count >= 1) { text.Rise = operandNums[0]; }
                    EchoVerbatim(output, operandTexts, op);
                    break;

                // ── Text showing (rewrite to drop off-box glyphs) ────────────
                case "Tj":
                    if (operandTexts.Count >= 1)
                    {
                        RewriteShow(output, ParseStringBytes(operandTexts[0]), text, ctm, x0, y0, x1, y1);
                    }

                    break;

                case "TJ":
                    RewriteShowArray(output, operandTexts, text, ctm, x0, y0, x1, y1);
                    break;

                case "'":
                    ApplyTd(text, 0, -text.Leading);
                    output.Append("T*\n");
                    if (operandTexts.Count >= 1)
                    {
                        RewriteShow(output, ParseStringBytes(operandTexts[^1]), text, ctm, x0, y0, x1, y1);
                    }

                    break;

                case "\"":
                    if (operandNums.Count >= 2)
                    {
                        text.WordSpacing = operandNums[0];
                        text.CharSpacing = operandNums[1];
                        output.Append(F(operandNums[0])).Append(" Tw ")
                              .Append(F(operandNums[1])).Append(" Tc\n");
                    }

                    ApplyTd(text, 0, -text.Leading);
                    output.Append("T*\n");
                    if (operandTexts.Count >= 1)
                    {
                        RewriteShow(output, ParseStringBytes(operandTexts[^1]), text, ctm, x0, y0, x1, y1);
                    }

                    break;

                // ── Path construction (buffer; do not echo yet) ──────────────
                case "m":
                    if (operandNums.Count >= 2)
                    {
                        curX = operandNums[0];
                        curY = operandNums[1];
                        startX = curX;
                        startY = curY;
                        subpaths.Add(new List<(double X, double Y)> { (curX, curY) });
                    }

                    AppendRaw(rawPath, operandTexts, op);
                    break;

                case "l":
                    if (operandNums.Count >= 2)
                    {
                        curX = operandNums[0];
                        curY = operandNums[1];
                        AddPoint(subpaths, curX, curY);
                    }

                    AppendRaw(rawPath, operandTexts, op);
                    break;

                case "c":
                    if (operandNums.Count >= 6)
                    {
                        FlattenCubic(subpaths, curX, curY,
                            operandNums[0], operandNums[1], operandNums[2],
                            operandNums[3], operandNums[4], operandNums[5]);
                        curX = operandNums[4];
                        curY = operandNums[5];
                    }

                    AppendRaw(rawPath, operandTexts, op);
                    break;

                case "v":
                    if (operandNums.Count >= 4)
                    {
                        FlattenCubic(subpaths, curX, curY, curX, curY,
                            operandNums[0], operandNums[1], operandNums[2], operandNums[3]);
                        curX = operandNums[2];
                        curY = operandNums[3];
                    }

                    AppendRaw(rawPath, operandTexts, op);
                    break;

                case "y":
                    if (operandNums.Count >= 4)
                    {
                        FlattenCubic(subpaths, curX, curY,
                            operandNums[0], operandNums[1], operandNums[2], operandNums[3],
                            operandNums[2], operandNums[3]);
                        curX = operandNums[2];
                        curY = operandNums[3];
                    }

                    AppendRaw(rawPath, operandTexts, op);
                    break;

                case "re":
                    if (operandNums.Count >= 4)
                    {
                        double rx = operandNums[0];
                        double ry = operandNums[1];
                        double rw = operandNums[2];
                        double rh = operandNums[3];
                        subpaths.Add(new List<(double X, double Y)>
                        {
                            (rx, ry), (rx + rw, ry), (rx + rw, ry + rh), (rx, ry + rh),
                        });
                        curX = rx;
                        curY = ry;
                        startX = rx;
                        startY = ry;
                    }

                    AppendRaw(rawPath, operandTexts, op);
                    break;

                case "h":
                    curX = startX;
                    curY = startY;
                    AppendRaw(rawPath, operandTexts, op);
                    break;

                case "W":
                case "W*":
                    pendingClip = true;
                    AppendRaw(rawPath, operandTexts, op);
                    break;

                // ── Path painting ────────────────────────────────────────────
                case "f":
                case "F":
                case "f*":
                case "B":
                case "B*":
                case "b":
                case "b*":
                    EmitPaintedPath(output, subpaths, rawPath.ToString(), op, ctm,
                        x0, y0, x1, y1, pendingClip, isStroke: false);
                    ResetPath();
                    break;

                case "S":
                case "s":
                    EmitPaintedPath(output, subpaths, rawPath.ToString(), op, ctm,
                        x0, y0, x1, y1, pendingClip, isStroke: true);
                    ResetPath();
                    break;

                case "n":
                    output.Append(rawPath).Append("n\n");
                    ResetPath();
                    break;

                case "Do":
                    HandleDo(output, operandTexts, ctm, resources, resolver,
                        x0, y0, x1, y1, newXObjects);
                    break;

                default:
                    EchoVerbatim(output, operandTexts, op);
                    break;
            }

            operandTexts.Clear();
            operandNums.Clear();
        }

        output.Append("Q\n");
        return new ScrubResult(Encoding.Latin1.GetBytes(output.ToString()), newXObjects);
    }

    /// <summary>Result of a scrub: rewritten content plus any newly created image XObjects.</summary>
    internal sealed class ScrubResult
    {
        internal ScrubResult(byte[] content, List<(string Name, PdfStream Stream)> newXObjects)
        {
            Content = content;
            NewXObjects = newXObjects;
        }

        internal byte[] Content { get; }

        internal List<(string Name, PdfStream Stream)> NewXObjects { get; }
    }

    // ── Text ─────────────────────────────────────────────────────────────────

    private static void ApplyTd(TextState text, double tx, double ty)
    {
        Matrix3x3 t = new Matrix3x3(1, 0, 0, 1, tx, ty);
        text.Tlm = t.Multiply(text.Tlm);
        text.Tm = text.Tlm;
    }

    private static void RewriteShowArray(
        StringBuilder output, List<string> operandTexts, TextState text,
        Matrix3x3 ctm, double x0, double y0, double x1, double y1)
    {
        StringBuilder tj = new StringBuilder("[");
        bool any = false;

        foreach (string t in operandTexts)
        {
            if (t == "[" || t == "]")
            {
                continue;
            }

            if (IsStringToken(t))
            {
                any |= AppendGlyphs(tj, ParseStringBytes(t), text, ctm, x0, y0, x1, y1);
            }
            else if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double adj))
            {
                tj.Append(' ').Append(F(adj)).Append(' ');
                double tx = -adj / 1000.0 * text.FontSize * text.HScale;
                text.Tm = new Matrix3x3(1, 0, 0, 1, tx, 0).Multiply(text.Tm);
            }
        }

        tj.Append(']');
        if (any)
        {
            output.Append(tj).Append(" TJ\n");
        }
    }

    private static void RewriteShow(
        StringBuilder output, byte[] bytes, TextState text,
        Matrix3x3 ctm, double x0, double y0, double x1, double y1)
    {
        StringBuilder tj = new StringBuilder("[");
        bool any = AppendGlyphs(tj, bytes, text, ctm, x0, y0, x1, y1);
        tj.Append(']');
        if (any)
        {
            output.Append(tj).Append(" TJ\n");
        }
    }

    /// <summary>
    /// Appends glyphs from <paramref name="bytes"/> to a TJ builder, keeping
    /// in-box glyphs as string runs and replacing off-box glyphs with positioning
    /// adjustments. Advances the text matrix through every glyph. Returns whether
    /// any glyph was kept.
    /// </summary>
    private static bool AppendGlyphs(
        StringBuilder tj, byte[] bytes, TextState text,
        Matrix3x3 ctm, double x0, double y0, double x1, double y1)
    {
        FontMetrics font = text.Font ?? new FontMetrics();
        bool keptAny = false;
        StringBuilder run = new StringBuilder();

        if (font.IsCid)
        {
            (double cx, double cy) = ScrubGeometry.Transform(
                Scale(text).Multiply(text.Tm).Multiply(ctm), 0, 0.35);
            if (cx >= x0 && cx <= x1 && cy >= y0 && cy <= y1)
            {
                tj.Append(EscapeString(bytes));
                keptAny = true;
            }

            for (int i = 0; i + 1 < bytes.Length; i += 2)
            {
                AdvanceTm(text, font.DefaultWidth, isSpace: false);
            }

            return keptAny;
        }

        foreach (byte b in bytes)
        {
            int code = b;
            double w = GlyphWidth(font, code);
            Matrix3x3 trm = Scale(text).Multiply(text.Tm).Multiply(ctm);
            (double px, double py) = ScrubGeometry.Transform(trm, w / 2000.0, 0.35);
            bool inside = px >= x0 && px <= x1 && py >= y0 && py <= y1;

            if (inside)
            {
                run.Append(EscapeByte(b));
                keptAny = true;
            }
            else
            {
                if (run.Length > 0)
                {
                    tj.Append('(').Append(run).Append(')');
                    run.Clear();
                }

                double adj = -(w + ((text.CharSpacing + (code == 32 ? text.WordSpacing : 0))
                    * 1000.0 / NonZero(text.FontSize)));
                tj.Append(' ').Append(F(adj)).Append(' ');
            }

            AdvanceTm(text, w, code == 32);
        }

        if (run.Length > 0)
        {
            tj.Append('(').Append(run).Append(')');
        }

        return keptAny;
    }

    private static void AdvanceTm(TextState text, double glyphWidth, bool isSpace)
    {
        double tx = ((glyphWidth / 1000.0 * text.FontSize) + text.CharSpacing
            + (isSpace ? text.WordSpacing : 0)) * text.HScale;
        text.Tm = new Matrix3x3(1, 0, 0, 1, tx, 0).Multiply(text.Tm);
    }

    private static Matrix3x3 Scale(TextState text)
    {
        return new Matrix3x3(text.FontSize * text.HScale, 0, 0, text.FontSize, 0, text.Rise);
    }

    private static double GlyphWidth(FontMetrics font, int code)
    {
        int idx = code - font.FirstChar;
        if (idx >= 0 && idx < font.Widths.Length && font.Widths[idx] > 0)
        {
            return font.Widths[idx];
        }

        return font.MissingWidth > 0 ? font.MissingWidth : font.DefaultWidth;
    }

    private static FontMetrics? ResolveMetrics(
        string fontName, PdfDictionary? resources, IPdfObjectResolver resolver,
        Dictionary<string, FontMetrics?> cache)
    {
        if (cache.TryGetValue(fontName, out FontMetrics? cached))
        {
            return cached;
        }

        FontMetrics? metrics = null;
        if (resources is not null &&
            Res(resolver, resources.GetAs<PdfPrimitive>(PdfName.Font)) is PdfDictionary fonts &&
            fonts.TryGetValue(PdfName.Intern(fontName), out PdfPrimitive? fontRef) &&
            resolver.Resolve(fontRef) is PdfDictionary fontDict)
        {
            metrics = new FontMetrics();
            PdfName? subtype = fontDict.Subtype;
            metrics.IsCid = subtype is not null && subtype.Value == "Type0";

            if (Res(resolver, fontDict.GetAs<PdfPrimitive>(PdfName.Intern("FirstChar"))) is PdfInteger first)
            {
                metrics.FirstChar = first.Value;
            }

            if (Res(resolver, fontDict.GetAs<PdfPrimitive>(PdfName.Intern("MissingWidth"))) is PdfInteger mw)
            {
                metrics.MissingWidth = mw.Value;
            }

            if (Res(resolver, fontDict.GetAs<PdfPrimitive>(PdfName.Intern("Widths"))) is PdfArray widths)
            {
                double[] w = new double[widths.Count];
                for (int i = 0; i < widths.Count; i++)
                {
                    w[i] = resolver.Resolve(widths[i]) switch
                    {
                        PdfInteger pi => pi.Value,
                        PdfReal pr => pr.Value,
                        _ => 0,
                    };
                }

                metrics.Widths = w;
            }
        }

        cache[fontName] = metrics;
        return metrics;
    }

    // ── String token parsing ─────────────────────────────────────────────────

    private static bool IsStringToken(string t)
    {
        return t.Length > 0 && (t[0] == '(' || t[0] == '<');
    }

    private static byte[] ParseStringBytes(string token)
    {
        if (token.Length == 0)
        {
            return Array.Empty<byte>();
        }

        return token[0] == '<' ? ParseHex(token) : ParseLiteral(token);
    }

    private static byte[] ParseLiteral(string token)
    {
        List<byte> bytes = new List<byte>();
        int i = token.Length > 0 && token[0] == '(' ? 1 : 0;
        int end = token.Length > 0 && token[^1] == ')' ? token.Length - 1 : token.Length;
        while (i < end)
        {
            char c = token[i];
            if (c == '\\' && i + 1 < end)
            {
                char n = token[i + 1];
                switch (n)
                {
                    case 'n': bytes.Add((byte)'\n'); i += 2; continue;
                    case 'r': bytes.Add((byte)'\r'); i += 2; continue;
                    case 't': bytes.Add((byte)'\t'); i += 2; continue;
                    case 'b': bytes.Add((byte)'\b'); i += 2; continue;
                    case 'f': bytes.Add((byte)'\f'); i += 2; continue;
                    case '(': bytes.Add((byte)'('); i += 2; continue;
                    case ')': bytes.Add((byte)')'); i += 2; continue;
                    case '\\': bytes.Add((byte)'\\'); i += 2; continue;
                    default:
                        if (n >= '0' && n <= '7')
                        {
                            int val = 0;
                            int k = 0;
                            i++;
                            while (k < 3 && i < end && token[i] >= '0' && token[i] <= '7')
                            {
                                val = (val * 8) + (token[i] - '0');
                                i++;
                                k++;
                            }

                            bytes.Add((byte)val);
                            continue;
                        }

                        bytes.Add((byte)n);
                        i += 2;
                        continue;
                }
            }

            bytes.Add((byte)c);
            i++;
        }

        return bytes.ToArray();
    }

    private static byte[] ParseHex(string token)
    {
        List<byte> bytes = new List<byte>();
        StringBuilder hex = new StringBuilder();
        foreach (char c in token)
        {
            if (Uri.IsHexDigit(c))
            {
                hex.Append(c);
            }
        }

        if (hex.Length % 2 == 1)
        {
            hex.Append('0');
        }

        for (int i = 0; i + 1 < hex.Length; i += 2)
        {
            bytes.Add(Convert.ToByte(hex.ToString(i, 2), 16));
        }

        return bytes.ToArray();
    }

    private static string EscapeString(byte[] bytes)
    {
        StringBuilder sb = new StringBuilder("(");
        foreach (byte b in bytes)
        {
            sb.Append(EscapeByte(b));
        }

        sb.Append(')');
        return sb.ToString();
    }

    private static string EscapeByte(byte b)
    {
        return b switch
        {
            (byte)'(' => "\\(",
            (byte)')' => "\\)",
            (byte)'\\' => "\\\\",
            _ => ((char)b).ToString(),
        };
    }

    private static PdfPrimitive? Res(IPdfObjectResolver resolver, PdfPrimitive? p)
    {
        return p is null ? null : resolver.Resolve(p);
    }

    private static double NonZero(double v) => Math.Abs(v) < 1e-6 ? 1.0 : v;

    private static TextState Clone(TextState s)
    {
        return new TextState
        {
            Tm = s.Tm,
            Tlm = s.Tlm,
            FontSize = s.FontSize,
            CharSpacing = s.CharSpacing,
            WordSpacing = s.WordSpacing,
            HScale = s.HScale,
            Leading = s.Leading,
            Rise = s.Rise,
            Font = s.Font,
        };
    }

    // ── Images ───────────────────────────────────────────────────────────────

    private static void HandleDo(
        StringBuilder output, List<string> operandTexts, Matrix3x3 ctm,
        PdfDictionary? resources, IPdfObjectResolver resolver,
        double x0, double y0, double x1, double y1,
        List<(string Name, PdfStream Stream)> newXObjects)
    {
        if (operandTexts.Count < 1)
        {
            return;
        }

        string name = operandTexts[0];
        PdfStream? xobj = ResolveXObjectStream(name.TrimStart('/'), resources, resolver);

        // Forms or unresolved XObjects: keep verbatim (confined by the backstop).
        PdfName? subtype = xobj?.Dictionary.Subtype;
        if (xobj is null || subtype is null || subtype.Value != "Image")
        {
            EchoVerbatim(output, operandTexts, "Do");
            return;
        }

        List<(double X, double Y)> imgQuad = ScrubGeometry.TransformRectCorners(ctm, 0, 0, 1, 1);
        List<(double X, double Y)> cropQuad = new List<(double X, double Y)>
        {
            (x0, y0), (x1, y0), (x1, y1), (x0, y1),
        };

        bool allInside = true;
        foreach ((double X, double Y) c in imgQuad)
        {
            if (!ScrubGeometry.PointInside(cropQuad, c.X, c.Y))
            {
                allInside = false;
            }
        }

        if (allInside)
        {
            EchoVerbatim(output, operandTexts, "Do");
            return;
        }

        if (ScrubGeometry.ClipPolygon(imgQuad, cropQuad).Count < 3)
        {
            return; // fully outside: byte-removed
        }

        // Crossing. Axis-aligned CTM (b == c == 0) supports a lossless pixel crop.
        if (Math.Abs(ctm.B) > 1e-6 || Math.Abs(ctm.C) > 1e-6)
        {
            EchoVerbatim(output, operandTexts, "Do");
            return;
        }

        if (!TryCropImage(output, xobj, resolver, ctm, x0, y0, x1, y1, newXObjects))
        {
            EchoVerbatim(output, operandTexts, "Do");
        }
    }

    private static bool TryCropImage(
        StringBuilder output, PdfStream xobj, IPdfObjectResolver resolver,
        Matrix3x3 ctm, double x0, double y0, double x1, double y1,
        List<(string Name, PdfStream Stream)> newXObjects)
    {
        Chuvadi.Pdf.Images.ImageFrame? frame = DecodeImage(xobj, resolver);
        if (frame is null)
        {
            return false;
        }

        // Page-space extent of the image (b == c == 0): x in [E, E+A], y in [F, F+D].
        double ex0 = Math.Min(ctm.E, ctm.E + ctm.A);
        double ex1 = Math.Max(ctm.E, ctm.E + ctm.A);
        double ey0 = Math.Min(ctm.F, ctm.F + ctm.D);
        double ey1 = Math.Max(ctm.F, ctm.F + ctm.D);

        double ix0 = Math.Max(ex0, x0);
        double iy0 = Math.Max(ey0, y0);
        double ix1 = Math.Min(ex1, x1);
        double iy1 = Math.Min(ey1, y1);
        if (ix1 - ix0 <= 0 || iy1 - iy0 <= 0)
        {
            return false;
        }

        // In-box page rect mapped to image space [0,1]^2.
        double u0 = (ix0 - ex0) / (ex1 - ex0);
        double u1 = (ix1 - ex0) / (ex1 - ex0);
        double v0 = (iy0 - ey0) / (ey1 - ey0);
        double v1 = (iy1 - ey0) / (ey1 - ey0);

        int w = frame.Width;
        int h = frame.Height;

        // Image space v=0 is the bottom; pixel row 0 is the top.
        int col0 = Clamp((int)Math.Floor(u0 * w), 0, w - 1);
        int col1 = Clamp((int)Math.Ceiling(u1 * w), col0 + 1, w);
        int row0 = Clamp((int)Math.Floor((1 - v1) * h), 0, h - 1);
        int row1 = Clamp((int)Math.Ceiling((1 - v0) * h), row0 + 1, h);

        int cw = col1 - col0;
        int ch = row1 - row0;
        if (cw <= 0 || ch <= 0)
        {
            return false;
        }

        byte[] rgb = new byte[cw * ch * 3];
        int p = 0;
        for (int row = row0; row < row1; row++)
        {
            for (int col = col0; col < col1; col++)
            {
                (byte b, byte g, byte r, byte _) = frame.Pixels.GetPixelBgra(col, row);
                rgb[p++] = r;
                rgb[p++] = g;
                rgb[p++] = b;
            }
        }

        byte[] flate = FlateEncode(rgb);
        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Intern("Type"), PdfName.Intern("XObject"));
        dict.Set(PdfName.Subtype, PdfName.Intern("Image"));
        dict.Set(PdfName.Intern("Width"), cw);
        dict.Set(PdfName.Intern("Height"), ch);
        dict.Set(PdfName.Intern("ColorSpace"), PdfName.Intern("DeviceRGB"));
        dict.Set(PdfName.Intern("BitsPerComponent"), 8);
        dict.Set(PdfName.Filter, PdfName.Intern("FlateDecode"));
        dict.Set(PdfName.Length, flate.Length);
        PdfStream cropped = new PdfStream(dict, flate);

        string newName = "ScrubIm" + newXObjects.Count;
        newXObjects.Add((newName, cropped));

        // Placement maps the unit square to the in-box page rect, expressed
        // relative to the current CTM:  place = inBoxRect * inverse(CTM),
        // so that  unit * place * CTM == inBoxRect.
        if (!ScrubGeometry.TryInvert(ctm, out Matrix3x3 invCtm))
        {
            return false;
        }

        Matrix3x3 inBox = new Matrix3x3(ix1 - ix0, 0, 0, iy1 - iy0, ix0, iy0);
        Matrix3x3 place = inBox.Multiply(invCtm);

        output.Append("q ")
              .Append(F(place.A)).Append(' ').Append(F(place.B)).Append(' ')
              .Append(F(place.C)).Append(' ').Append(F(place.D)).Append(' ')
              .Append(F(place.E)).Append(' ').Append(F(place.F)).Append(" cm /")
              .Append(newName).Append(" Do Q\n");
        return true;
    }

    private static Chuvadi.Pdf.Images.ImageFrame? DecodeImage(PdfStream xobj, IPdfObjectResolver resolver)
    {
        PdfDictionary dict = xobj.Dictionary;
        string filterName = (Res(resolver, xobj.Filter) as PdfName)?.Value ?? string.Empty;

        try
        {
            if (filterName == "DCTDecode")
            {
                return Chuvadi.Pdf.Images.JpegDecoder.Decode(xobj.RawBytes);
            }

            int w = (Res(resolver, dict.GetAs<PdfPrimitive>(PdfName.Intern("Width"))) as PdfInteger)?.Value ?? 0;
            int h = (Res(resolver, dict.GetAs<PdfPrimitive>(PdfName.Intern("Height"))) as PdfInteger)?.Value ?? 0;
            int bpc = (Res(resolver, dict.GetAs<PdfPrimitive>(PdfName.Intern("BitsPerComponent"))) as PdfInteger)?.Value ?? 8;
            string cs = (Res(resolver, dict.GetAs<PdfPrimitive>(PdfName.Intern("ColorSpace"))) as PdfName)?.Value ?? string.Empty;
            if (w <= 0 || h <= 0 || bpc != 8)
            {
                return null;
            }

            byte[] samples = xobj.RawBytes;
            if (xobj.Filter is PdfName fn)
            {
                Chuvadi.Pdf.Filters.FilterPipeline pipe = Chuvadi.Pdf.Filters.FilterRegistry.CreateDefaultPipeline();
                samples = pipe.Decode(Chuvadi.Pdf.Filters.FilterRegistry.ResolveAlias(fn.Value), xobj.RawBytes, null);
            }

            int channels = cs == "DeviceRGB" ? 3 : cs == "DeviceGray" ? 1 : 0;
            if (channels == 0 || samples.Length < w * h * channels)
            {
                return null;
            }

            Chuvadi.Pdf.Graphics.PixelBuffer buf = new Chuvadi.Pdf.Graphics.PixelBuffer(w, h);
            int si = 0;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    byte r, g, b;
                    if (channels == 3)
                    {
                        r = samples[si++];
                        g = samples[si++];
                        b = samples[si++];
                    }
                    else
                    {
                        r = g = b = samples[si++];
                    }

                    buf.SetPixelBgra(x, y, b, g, r, 255);
                }
            }

            return new Chuvadi.Pdf.Images.ImageFrame(
                buf, channels == 3 ? Chuvadi.Pdf.Images.ImageColorFormat.Rgb24 : Chuvadi.Pdf.Images.ImageColorFormat.Gray8);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static byte[] FlateEncode(byte[] data)
    {
        using MemoryStream ms = new MemoryStream();
        using (System.IO.Compression.ZLibStream zs =
            new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            zs.Write(data, 0, data.Length);
        }

        return ms.ToArray();
    }

    private static PdfStream? ResolveXObjectStream(string name, PdfDictionary? resources, IPdfObjectResolver resolver)
    {
        if (resources is null || string.IsNullOrEmpty(name))
        {
            return null;
        }

        if (Res(resolver, resources.GetAs<PdfPrimitive>(PdfName.Intern("XObject"))) is PdfDictionary xobjects &&
            xobjects.TryGetValue(PdfName.Intern(name), out PdfPrimitive? xref) &&
            resolver.Resolve(xref) is PdfStream stream)
        {
            return stream;
        }

        return null;
    }

    private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;

    // ── Vector paths ─────────────────────────────────────────────────────────

    private static void EmitPaintedPath(
        StringBuilder output, List<List<(double X, double Y)>> subpaths, string rawPath,
        string paintOp, Matrix3x3 ctm, double x0, double y0, double x1, double y1,
        bool isClip, bool isStroke)
    {
        if (isClip || subpaths.Count == 0)
        {
            output.Append(rawPath).Append(paintOp).Append('\n');
            return;
        }

        if (!ScrubGeometry.TryInvert(ctm, out Matrix3x3 inv))
        {
            output.Append(rawPath).Append(paintOp).Append('\n');
            return;
        }

        List<(double X, double Y)> cropQuad = ScrubGeometry.TransformRectCorners(inv, x0, y0, x1, y1);
        ClassifyPath(subpaths, cropQuad, out bool allInside, out bool allOutside);

        if (allInside)
        {
            output.Append(rawPath).Append(paintOp).Append('\n');
            return;
        }

        if (allOutside)
        {
            return;
        }

        StringBuilder clipped = new StringBuilder();
        if (isStroke)
        {
            foreach (List<(double X, double Y)> sp in subpaths)
            {
                for (int i = 0; i + 1 < sp.Count; i++)
                {
                    if (ScrubGeometry.ClipSegment(cropQuad, sp[i], sp[i + 1],
                        out (double X, double Y) q0, out (double X, double Y) q1))
                    {
                        clipped.Append(F(q0.X)).Append(' ').Append(F(q0.Y)).Append(" m ")
                               .Append(F(q1.X)).Append(' ').Append(F(q1.Y)).Append(" l\n");
                    }
                }
            }

            if (clipped.Length > 0)
            {
                output.Append(clipped).Append("S\n");
            }
        }
        else
        {
            foreach (List<(double X, double Y)> sp in subpaths)
            {
                List<(double X, double Y)> poly = ScrubGeometry.ClipPolygon(sp, cropQuad);
                if (poly.Count >= 3)
                {
                    clipped.Append(F(poly[0].X)).Append(' ').Append(F(poly[0].Y)).Append(" m\n");
                    for (int i = 1; i < poly.Count; i++)
                    {
                        clipped.Append(F(poly[i].X)).Append(' ').Append(F(poly[i].Y)).Append(" l\n");
                    }

                    clipped.Append("h\n");
                }
            }

            if (clipped.Length > 0)
            {
                string fillOp = paintOp.Contains('*') ? "f*" : "f";
                output.Append(clipped).Append(fillOp).Append('\n');
            }
        }
    }

    private static void ClassifyPath(
        List<List<(double X, double Y)>> subpaths, List<(double X, double Y)> cropQuad,
        out bool allInside, out bool allOutside)
    {
        allInside = true;
        allOutside = true;
        foreach (List<(double X, double Y)> sp in subpaths)
        {
            foreach ((double X, double Y) p in sp)
            {
                if (ScrubGeometry.PointInside(cropQuad, p.X, p.Y))
                {
                    allOutside = false;
                }
                else
                {
                    allInside = false;
                }
            }
        }
    }

    private static void AddPoint(List<List<(double X, double Y)>> subpaths, double x, double y)
    {
        if (subpaths.Count == 0)
        {
            subpaths.Add(new List<(double X, double Y)>());
        }

        subpaths[^1].Add((x, y));
    }

    private static void FlattenCubic(
        List<List<(double X, double Y)>> subpaths,
        double x0, double y0, double x1, double y1,
        double x2, double y2, double x3, double y3)
    {
        for (int i = 1; i <= BezierSegments; i++)
        {
            double t = (double)i / BezierSegments;
            double mt = 1 - t;
            double bx = (mt * mt * mt * x0) + (3 * mt * mt * t * x1) + (3 * mt * t * t * x2) + (t * t * t * x3);
            double by = (mt * mt * mt * y0) + (3 * mt * mt * t * y1) + (3 * mt * t * t * y2) + (t * t * t * y3);
            AddPoint(subpaths, bx, by);
        }
    }

    private static void AppendRaw(StringBuilder rawPath, List<string> operands, string op)
    {
        foreach (string o in operands)
        {
            rawPath.Append(o).Append(' ');
        }

        rawPath.Append(op).Append('\n');
    }

    private static void EchoVerbatim(StringBuilder output, List<string> operands, string op)
    {
        foreach (string o in operands)
        {
            output.Append(o).Append(' ');
        }

        output.Append(op).Append('\n');
    }

    private static string F(double v)
    {
        return v.ToString("0.####", CultureInfo.InvariantCulture);
    }
}
