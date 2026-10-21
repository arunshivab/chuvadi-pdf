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
        IPdfObjectResolver resolver,
        Func<PdfStream, PdfReference>? allocate = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(resolver);
        return ScrubCore(content, x0, y0, x1, y1, resources, resolver, allocate, Matrix3x3.Identity, 0);
    }

    /// <summary>
    /// Scrubs a form-like content stream (an annotation appearance) whose
    /// content space maps to page space through <paramref name="placement"/>,
    /// without the page-level hard-clip backstop.
    /// </summary>
    internal static ScrubResult ScrubPlaced(
        byte[] content,
        double x0, double y0, double x1, double y1,
        PdfDictionary? resources,
        IPdfObjectResolver resolver,
        Func<PdfStream, PdfReference> allocate,
        Matrix3x3 placement)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(resolver);
        return ScrubCore(content, x0, y0, x1, y1, resources, resolver, allocate, placement, 1);
    }

    // Nested form XObjects are scrubbed recursively up to this depth; deeper
    // forms (or cycles) are removed rather than kept unscrubbed (B15).
    private const int MaxFormDepth = 8;

    // Scrubs one content stream. The page level (depth 0) starts from the
    // identity CTM and wraps the output in the hard-clip backstop; a form
    // XObject is scrubbed in its own space by seeding its placement CTM
    // (form /Matrix x CTM at Do) — the crop box stays in page space and the
    // page-level backstop already confines the form's paint.
    private static ScrubResult ScrubCore(
        byte[] content,
        double x0, double y0, double x1, double y1,
        PdfDictionary? resources,
        IPdfObjectResolver resolver,
        Func<PdfStream, PdfReference>? allocate,
        Matrix3x3 initialCtm,
        int depth)
    {
        StringBuilder output = new StringBuilder(content.Length + 64);
        List<(string Name, PdfStream Stream)> newXObjects = new List<(string, PdfStream)>();
        HashSet<string> referenced = new HashSet<string>(StringComparer.Ordinal);

        // Pattern names selected as a colour (scn/SCN), and each shading
        // painted by sh with the CTM it was painted under: the resources
        // keep only these, trimmed to the box.
        HashSet<string> patternNames = new HashSet<string>(StringComparer.Ordinal);
        Dictionary<string, List<Matrix3x3>> shadingUses = new Dictionary<string, List<Matrix3x3>>(StringComparer.Ordinal);

        // The current fill and stroke pattern names (saved by q, restored by
        // Q), and for each pattern the page-space extent it is painted over
        // (a filled path's bounds; the whole box for strokes, text, forms and
        // stencil images, whose painted extent is not modelled here).
        string? fillPattern = null;
        string? strokePattern = null;
        Stack<(string? Fill, string? Stroke)> patternStack = new Stack<(string? Fill, string? Stroke)>();
        Dictionary<string, (double X0, double Y0, double X1, double Y1)> patternExtents =
            new Dictionary<string, (double X0, double Y0, double X1, double Y1)>(StringComparer.Ordinal);

        // Hard-clip backstop: confines anything echoed verbatim to the crop box.
        if (depth == 0)
        {
            output.Append("q ")
                  .Append(F(x0)).Append(' ').Append(F(y0)).Append(' ')
                  .Append(F(x1 - x0)).Append(' ').Append(F(y1 - y0))
                  .Append(" re W n\n");
        }

        Matrix3x3 ctm = initialCtm;
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

        // Approximate active clip region: the intersection of the device-space
        // bounding boxes of every clip path set so far (infinite until a W/W*
        // takes effect), saved/restored with q/Q. Used to drop `sh` operators
        // whose paintable region lies entirely outside the crop box.
        double clipMinX = double.NegativeInfinity, clipMinY = double.NegativeInfinity;
        double clipMaxX = double.PositiveInfinity, clipMaxY = double.PositiveInfinity;
        Stack<(double MinX, double MinY, double MaxX, double MaxY)> clipStack =
            new Stack<(double MinX, double MinY, double MaxX, double MaxY)>();

        using MemoryStream ms = new MemoryStream(content);
        using PdfTokenizer tok = new PdfTokenizer(ms, leaveOpen: false);

        void ResetPath()
        {
            subpaths = new List<List<(double X, double Y)>>();
            rawPath.Clear();
            pendingClip = false;
        }

        // Intersects the tracked clip box with the pending clip path's
        // device-space bounding box. Called at the paint operator that makes a
        // W/W* take effect. An empty clip path is left untracked (conservative:
        // the region stays as-is, so nothing is dropped on its account).
        void ApplyPendingClip(Matrix3x3 currentCtm)
        {
            if (!pendingClip || subpaths.Count == 0)
            {
                return;
            }

            double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
            foreach (List<(double X, double Y)> subpath in subpaths)
            {
                foreach ((double px, double py) in subpath)
                {
                    (double dx, double dy) = ScrubGeometry.Transform(currentCtm, px, py);
                    minX = Math.Min(minX, dx);
                    minY = Math.Min(minY, dy);
                    maxX = Math.Max(maxX, dx);
                    maxY = Math.Max(maxY, dy);
                }
            }

            clipMinX = Math.Max(clipMinX, minX);
            clipMinY = Math.Max(clipMinY, minY);
            clipMaxX = Math.Min(clipMaxX, maxX);
            clipMaxY = Math.Min(clipMaxY, maxY);
        }

        void ExtendPattern(string? name, double ex0, double ey0, double ex1, double ey1)
        {
            if (name is null)
            {
                return;
            }

            patternExtents[name] = patternExtents.TryGetValue(name, out (double X0, double Y0, double X1, double Y1) e)
                ? (Math.Min(e.X0, ex0), Math.Min(e.Y0, ey0), Math.Max(e.X1, ex1), Math.Max(e.Y1, ey1))
                : (ex0, ey0, ex1, ey1);
        }

        // Updates the current pattern names and records where a painting
        // operator paints with them. Runs before the operator is handled,
        // while the path being painted is still buffered.
        void TrackPatternPaint(string op)
        {
            string? lastName = operandTexts.Count > 0 && operandTexts[^1].StartsWith('/')
                ? DecodeNameToken(operandTexts[^1])
                : null;
            switch (op)
            {
                case "q":
                    patternStack.Push((fillPattern, strokePattern));
                    return;
                case "Q":
                    if (patternStack.Count > 0)
                    {
                        (fillPattern, strokePattern) = patternStack.Pop();
                    }

                    return;
                case "scn":
                    fillPattern = lastName;
                    return;
                case "SCN":
                    strokePattern = lastName;
                    return;
                case "cs" or "sc" or "g" or "rg" or "k":
                    fillPattern = null;
                    return;
                case "CS" or "SC" or "G" or "RG" or "K":
                    strokePattern = null;
                    return;
                case "f" or "F" or "f*" or "B" or "B*" or "b" or "b*":
                    if (fillPattern is not null && !pendingClip)
                    {
                        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
                        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
                        foreach (List<(double X, double Y)> subpath in subpaths)
                        {
                            foreach ((double px, double py) in subpath)
                            {
                                (double dx, double dy) = ScrubGeometry.Transform(ctm, px, py);
                                minX = Math.Min(minX, dx);
                                minY = Math.Min(minY, dy);
                                maxX = Math.Max(maxX, dx);
                                maxY = Math.Max(maxY, dy);
                            }
                        }

                        if (minX <= maxX)
                        {
                            ExtendPattern(fillPattern, minX, minY, maxX, maxY);
                        }
                    }
                    else if (fillPattern is not null)
                    {
                        ExtendPattern(fillPattern, x0, y0, x1, y1);
                    }

                    if (op is "B" or "B*" or "b" or "b*")
                    {
                        ExtendPattern(strokePattern, x0, y0, x1, y1);
                    }

                    return;
                case "S" or "s":
                    ExtendPattern(strokePattern, x0, y0, x1, y1);
                    return;
                case "Tj" or "TJ" or "'" or "\"":
                    ExtendPattern(fillPattern, x0, y0, x1, y1);
                    ExtendPattern(strokePattern, x0, y0, x1, y1);
                    return;
                case "Do" or "BI":
                    ExtendPattern(fillPattern, x0, y0, x1, y1);
                    ExtendPattern(strokePattern, x0, y0, x1, y1);
                    return;
            }
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
            TrackPatternPaint(op);

            switch (op)
            {
                case "cm":
                    // Last-6 operands (stack semantics): crafted extras must
                    // not shift the modelled matrix away from the viewer's.
                    if (operandNums.Count >= 6)
                    {
                        Matrix3x3 m = new Matrix3x3(
                            operandNums[^6], operandNums[^5], operandNums[^4],
                            operandNums[^3], operandNums[^2], operandNums[^1]);
                        ctm = m.Multiply(ctm);
                    }

                    EchoVerbatim(output, operandTexts, op);
                    break;

                case "q":
                    ctmStack.Push(ctm);
                    textStack.Push(Clone(text));
                    clipStack.Push((clipMinX, clipMinY, clipMaxX, clipMaxY));
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

                    if (clipStack.Count > 0)
                    {
                        (clipMinX, clipMinY, clipMaxX, clipMaxY) = clipStack.Pop();
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
                    for (int ti = operandTexts.Count - 1; ti >= 0; ti--)
                    {
                        if (operandTexts[ti].StartsWith('/'))
                        {
                            string fontName = operandTexts[ti].TrimStart('/');
                            text.Font = ResolveMetrics(fontName, resources, resolver, fontCache);
                            break;
                        }
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
                        ApplyTd(text, operandNums[^2], operandNums[^1]);
                    }

                    EchoVerbatim(output, operandTexts, op);
                    break;

                case "TD":
                    if (operandNums.Count >= 2)
                    {
                        text.Leading = -operandNums[^1];
                        ApplyTd(text, operandNums[^2], operandNums[^1]);
                    }

                    EchoVerbatim(output, operandTexts, op);
                    break;

                case "Tm":
                    if (operandNums.Count >= 6)
                    {
                        Matrix3x3 tm = new Matrix3x3(
                            operandNums[^6], operandNums[^5], operandNums[^4],
                            operandNums[^3], operandNums[^2], operandNums[^1]);
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
                    if (operandNums.Count >= 1) { text.CharSpacing = operandNums[^1]; }
                    EchoVerbatim(output, operandTexts, op);
                    break;

                case "Tw":
                    if (operandNums.Count >= 1) { text.WordSpacing = operandNums[^1]; }
                    EchoVerbatim(output, operandTexts, op);
                    break;

                case "Tz":
                    if (operandNums.Count >= 1) { text.HScale = operandNums[^1] / 100.0; }
                    EchoVerbatim(output, operandTexts, op);
                    break;

                case "TL":
                    if (operandNums.Count >= 1) { text.Leading = operandNums[^1]; }
                    EchoVerbatim(output, operandTexts, op);
                    break;

                case "Ts":
                    if (operandNums.Count >= 1) { text.Rise = operandNums[^1]; }
                    EchoVerbatim(output, operandTexts, op);
                    break;

                // ── Text showing (rewrite to drop off-box glyphs) ────────────
                case "Tj":
                    if (operandTexts.Count >= 1)
                    {
                        RewriteShow(output, ParseStringBytes(operandTexts[^1]), text, ctm, x0, y0, x1, y1);
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
                        text.WordSpacing = operandNums[^2];
                        text.CharSpacing = operandNums[^1];
                        output.Append(F(operandNums[^2])).Append(" Tw ")
                              .Append(F(operandNums[^1])).Append(" Tc\n");
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
                        curX = operandNums[^2];
                        curY = operandNums[^1];
                        startX = curX;
                        startY = curY;
                        subpaths.Add(new List<(double X, double Y)> { (curX, curY) });
                    }

                    AppendRaw(rawPath, operandTexts, op);
                    break;

                case "l":
                    if (operandNums.Count >= 2)
                    {
                        curX = operandNums[^2];
                        curY = operandNums[^1];
                        AddPoint(subpaths, curX, curY);
                    }

                    AppendRaw(rawPath, operandTexts, op);
                    break;

                case "c":
                    if (operandNums.Count >= 6)
                    {
                        FlattenCubic(subpaths, curX, curY,
                            operandNums[^6], operandNums[^5], operandNums[^4],
                            operandNums[^3], operandNums[^2], operandNums[^1]);
                        curX = operandNums[^2];
                        curY = operandNums[^1];
                    }

                    AppendRaw(rawPath, operandTexts, op);
                    break;

                case "v":
                    if (operandNums.Count >= 4)
                    {
                        FlattenCubic(subpaths, curX, curY, curX, curY,
                            operandNums[^4], operandNums[^3], operandNums[^2], operandNums[^1]);
                        curX = operandNums[^2];
                        curY = operandNums[^1];
                    }

                    AppendRaw(rawPath, operandTexts, op);
                    break;

                case "y":
                    if (operandNums.Count >= 4)
                    {
                        FlattenCubic(subpaths, curX, curY,
                            operandNums[^4], operandNums[^3], operandNums[^2], operandNums[^1],
                            operandNums[^2], operandNums[^1]);
                        curX = operandNums[^2];
                        curY = operandNums[^1];
                    }

                    AppendRaw(rawPath, operandTexts, op);
                    break;

                case "re":
                    if (operandNums.Count >= 4)
                    {
                        double rx = operandNums[^4];
                        double ry = operandNums[^3];
                        double rw = operandNums[^2];
                        double rh = operandNums[^1];
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
                    ApplyPendingClip(ctm);
                    EmitPaintedPath(output, subpaths, rawPath.ToString(), op, ctm,
                        x0, y0, x1, y1, pendingClip, isStroke: false);
                    ResetPath();
                    break;

                case "S":
                case "s":
                    ApplyPendingClip(ctm);
                    EmitPaintedPath(output, subpaths, rawPath.ToString(), op, ctm,
                        x0, y0, x1, y1, pendingClip, isStroke: true);
                    ResetPath();
                    break;

                case "n":
                    ApplyPendingClip(ctm);
                    output.Append(rawPath).Append("n\n");
                    ResetPath();
                    break;

                case "Do":
                    HandleDo(output, operandTexts, ctm, resources, resolver,
                        x0, y0, x1, y1, newXObjects, referenced, allocate, depth);
                    break;

                // A shading paints the full active clip region. When that
                // region (approximated by the tracked clip box) lies entirely
                // outside the crop box, the operator is dead bytes and is
                // dropped; otherwise it is kept — the hard-clip backstop
                // confines its paint to the crop box.
                case "sh":
                    if (Math.Min(clipMaxX, x1) > Math.Max(clipMinX, x0)
                        && Math.Min(clipMaxY, y1) > Math.Max(clipMinY, y0))
                    {
                        EchoVerbatim(output, operandTexts, op);
                        if (operandTexts.Count > 0 && operandTexts[^1].StartsWith('/'))
                        {
                            string shadingName = DecodeNameToken(operandTexts[^1]);
                            if (!shadingUses.TryGetValue(shadingName, out List<Matrix3x3>? uses))
                            {
                                uses = new List<Matrix3x3>();
                                shadingUses[shadingName] = uses;
                            }

                            uses.Add(ctm);
                        }
                    }

                    break;

                // Inline image: BI <dict> ID <binary> EI. The binary data is
                // not tokenizable, so the whole run is consumed as a unit and
                // dropped when the unit square it paints (under the CTM) lies
                // entirely outside the crop box.
                case "BI":
                    HandleInlineImage(output, tok, content, ctm, x0, y0, x1, y1, token.ByteOffset,
                        resources, resolver, newXObjects, allocate);
                    break;

                default:
                    if ((op == "scn" || op == "SCN") && operandTexts.Count > 0 && operandTexts[^1].StartsWith('/'))
                    {
                        patternNames.Add(DecodeNameToken(operandTexts[^1]));
                    }

                    EchoVerbatim(output, operandTexts, op);
                    break;
            }

            operandTexts.Clear();
            operandNums.Clear();
        }

        if (depth == 0)
        {
            output.Append("Q\n");
        }

        return new ScrubResult(Encoding.Latin1.GetBytes(output.ToString()), newXObjects, referenced)
        {
            Patterns = patternNames,
            PatternExtents = patternExtents,
            ShadingUses = shadingUses,
            BaseCtm = initialCtm,
            Box = (x0, y0, x1, y1),
            Depth = depth,
        };
    }

    /// <summary>Result of a scrub: rewritten content plus any newly created image XObjects.</summary>
    internal sealed class ScrubResult
    {
        internal ScrubResult(
            byte[] content, List<(string Name, PdfStream Stream)> newXObjects, HashSet<string> referencedXObjects)
        {
            Content = content;
            NewXObjects = newXObjects;
            ReferencedXObjects = referencedXObjects;
        }

        internal byte[] Content { get; }

        internal List<(string Name, PdfStream Stream)> NewXObjects { get; }

        /// <summary>
        /// Original XObject resource names the scrubbed content still invokes
        /// (verbatim <c>Do</c>s). Every other original XObject entry is no
        /// longer used and must be pruned, or its full content stays in the file.
        /// </summary>
        internal HashSet<string> ReferencedXObjects { get; }

        /// <summary>Pattern names the scrubbed content selects as a colour.</summary>
        internal HashSet<string> Patterns { get; init; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// For each pattern, the page-space rectangle it is painted over (not
        /// yet limited to the box); a pattern absent here paints nothing.
        /// </summary>
        internal Dictionary<string, (double X0, double Y0, double X1, double Y1)> PatternExtents { get; init; } =
            new Dictionary<string, (double X0, double Y0, double X1, double Y1)>(StringComparer.Ordinal);

        /// <summary>Shading names the scrubbed content paints with sh, and the CTM of each use.</summary>
        internal Dictionary<string, List<Matrix3x3>> ShadingUses { get; init; } =
            new Dictionary<string, List<Matrix3x3>>(StringComparer.Ordinal);

        /// <summary>
        /// Maps the content's own space to the space of <see cref="Box"/>
        /// (identity for a page; a form's placement for a form).
        /// </summary>
        internal Matrix3x3 BaseCtm { get; init; } = Matrix3x3.Identity;

        /// <summary>The crop rectangle the content was scrubbed to.</summary>
        internal (double X0, double Y0, double X1, double Y1) Box { get; init; }

        /// <summary>Form nesting depth of the scrubbed content (0 for a page).</summary>
        internal int Depth { get; init; }
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

    internal static byte[] ParseStringBytes(string token)
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

    // Consumes an inline image (BI <dict> ID <binary> EI) as one unit — the
    // binary data is not tokenizable — and drops it when the unit square it
    // paints (mapped by the CTM) lies entirely outside the crop box. An image
    // overlapping the box is kept verbatim: the hard-clip backstop confines
    // its visible paint, and inline images are small by construction (§8.9.7
    // recommends at most 4 KB), so the residual off-box bytes are bounded.
    private static void HandleInlineImage(
        StringBuilder output, PdfTokenizer tok, byte[] content, Matrix3x3 ctm,
        double x0, double y0, double x1, double y1, long biStart,
        PdfDictionary? resources, IPdfObjectResolver resolver,
        List<(string Name, PdfStream Stream)> newXObjects, Func<PdfStream, PdfReference>? allocate)
    {
        // Exact-extent consumption (3.18): the dictionary's /W /H /BPC /CS
        // give the data length for unfiltered images, so a byte pair that
        // merely looks like "EI" inside the binary cannot end the image early
        // (which would re-tokenise — and leak — the image's tail).
        int eiEnd = InlineImageExtent.FindEnd(tok, content);
        if (eiEnd < 0)
        {
            // No terminating EI (malformed): the rest of the stream is
            // unknowable — keep it verbatim rather than guessing what to drop.
            output.Append(Encoding.Latin1.GetString(
                content, (int)biStart, content.Length - (int)biStart));
            tok.Seek(content.Length);
            return;
        }

        // Inside: kept byte for byte. Outside: removed. Crossing: cropped at
        // the sample level into a new image XObject so no off-box pixel
        // survives; an image that cannot be decoded for cropping is removed
        // (over-redaction in doubt, B15).
        ImageCropOutcome outcome = ImageSampleCropper.Crop(
            () => ImageSampleCropper.FromInline(content, (int)biStart, eiEnd, resources, resolver),
            ctm, x0, y0, x1, y1, allocate, out PdfStream? cropped, out Matrix3x3 place);

        if (outcome == ImageCropOutcome.Inside)
        {
            output.Append(Encoding.Latin1.GetString(content, (int)biStart, eiEnd - (int)biStart));
            output.Append('\n');
        }
        else if (outcome == ImageCropOutcome.Cropped && cropped is not null)
        {
            EmitPlacedXObject(output, newXObjects, cropped, place, "ScrubIm");
        }

        tok.Seek(eiEnd);
    }

    private static void HandleDo(
        StringBuilder output, List<string> operandTexts, Matrix3x3 ctm,
        PdfDictionary? resources, IPdfObjectResolver resolver,
        double x0, double y0, double x1, double y1,
        List<(string Name, PdfStream Stream)> newXObjects,
        HashSet<string> referenced,
        Func<PdfStream, PdfReference>? allocate,
        int depth)
    {
        if (operandTexts.Count < 1)
        {
            return;
        }

        // Last operand is the name (stack semantics).
        string name = DecodeNameToken(operandTexts[^1]);
        PdfStream? xobj = ResolveXObjectStream(name, resources, resolver);

        // Unresolvable XObject: paints nothing in any viewer; keep the
        // operator (harmless) and its resource name.
        if (xobj is null)
        {
            EchoVerbatim(output, operandTexts, "Do");
            referenced.Add(name);
            return;
        }

        string subtype = xobj.Dictionary.Subtype?.Value ?? string.Empty;
        if (subtype == "Image")
        {
            ImageCropOutcome outcome = ImageSampleCropper.Crop(
                () => ImageSampleCropper.FromXObject(xobj, resolver),
                ctm, x0, y0, x1, y1, allocate, out PdfStream? cropped, out Matrix3x3 place);

            switch (outcome)
            {
                case ImageCropOutcome.Inside:
                    EchoVerbatim(output, operandTexts, "Do");
                    referenced.Add(name);
                    break;
                case ImageCropOutcome.Cropped when cropped is not null:
                    EmitPlacedXObject(output, newXObjects, cropped, place, "ScrubIm");
                    break;
                default:
                    // Outside, or crossing but undecodable: removed (B15).
                    break;
            }

            return;
        }

        if (subtype == "Form")
        {
            HandleForm(output, operandTexts, name, xobj, ctm, resources, resolver,
                x0, y0, x1, y1, newXObjects, referenced, allocate, depth);
            return;
        }

        // PostScript or unknown XObject subtypes paint nothing in conforming
        // viewers; drop them.
    }

    // Scrubs a form XObject recursively: entirely inside → kept verbatim;
    // entirely outside its BBox → removed; otherwise its content is scrubbed
    // in the form's own space and replaced by a new form whose resources keep
    // only what the scrubbed content still uses.
    private static void HandleForm(
        StringBuilder output, List<string> operandTexts, string name, PdfStream form, Matrix3x3 ctm,
        PdfDictionary? resources, IPdfObjectResolver resolver,
        double x0, double y0, double x1, double y1,
        List<(string Name, PdfStream Stream)> newXObjects,
        HashSet<string> referenced,
        Func<PdfStream, PdfReference>? allocate,
        int depth)
    {
        PdfDictionary dict = form.Dictionary;
        Matrix3x3 formMatrix = Matrix3x3.Identity;
        if (Res(resolver, dict.GetAs<PdfPrimitive>(PdfName.Intern("Matrix"))) is PdfArray m && m.Count >= 6)
        {
            formMatrix = new Matrix3x3(
                NumOf(resolver, m[0]), NumOf(resolver, m[1]), NumOf(resolver, m[2]),
                NumOf(resolver, m[3]), NumOf(resolver, m[4]), NumOf(resolver, m[5]));
        }

        Matrix3x3 formCtm = formMatrix.Multiply(ctm);

        // Classify by the form's BBox mapped to page space.
        if (Res(resolver, dict.GetAs<PdfPrimitive>(PdfName.Intern("BBox"))) is PdfArray bbox && bbox.Count >= 4)
        {
            double bx0 = NumOf(resolver, bbox[0]);
            double by0 = NumOf(resolver, bbox[1]);
            double bx1 = NumOf(resolver, bbox[2]);
            double by1 = NumOf(resolver, bbox[3]);
            List<(double X, double Y)> quad = ScrubGeometry.TransformRectCorners(
                formCtm, Math.Min(bx0, bx1), Math.Min(by0, by1), Math.Max(bx0, bx1), Math.Max(by0, by1));
            List<(double X, double Y)> cropQuad = new List<(double X, double Y)>
            {
                (x0, y0), (x1, y0), (x1, y1), (x0, y1),
            };

            if (ScrubGeometry.ClipPolygon(quad, cropQuad).Count < 3)
            {
                return; // entirely outside: removed
            }

            bool allInside = true;
            foreach ((double X, double Y) c in quad)
            {
                if (!ScrubGeometry.PointInside(cropQuad, c.X, c.Y))
                {
                    allInside = false;
                    break;
                }
            }

            if (allInside)
            {
                EchoVerbatim(output, operandTexts, "Do");
                referenced.Add(name);
                return;
            }
        }

        if (allocate is null || depth + 1 >= MaxFormDepth)
        {
            return; // cannot rebuild the form here: removed (B15)
        }

        byte[] formContent;
        try
        {
            formContent = DecodeFormContent(form);
        }
        catch (Exception)
        {
            return; // undecodable crossing form: removed (B15)
        }

        PdfDictionary? formResources = resources;
        if (Res(resolver, dict.GetAs<PdfPrimitive>(PdfName.Resources)) is PdfDictionary own)
        {
            formResources = own;
        }

        ScrubResult nested = ScrubCore(
            formContent, x0, y0, x1, y1, formResources, resolver, allocate, formCtm, depth + 1);

        PdfDictionary newDict = new PdfDictionary();
        foreach (PdfName key in dict.Keys)
        {
            if (key.Value is "Filter" or "DecodeParms" or "Length" or "Resources" or "PieceInfo")
            {
                continue;
            }

            newDict.Set(key, dict[key]);
        }

        newDict.Set(PdfName.Resources, BuildScrubbedResources(formResources, nested, resolver, allocate));
        newDict.Set(PdfName.Length, nested.Content.Length);
        PdfStream scrubbedForm = new PdfStream(newDict, nested.Content);

        string newName = "ScrubFm" + newXObjects.Count.ToString(CultureInfo.InvariantCulture);
        newXObjects.Add((newName, scrubbedForm));
        output.Append('/').Append(newName).Append(" Do\n");
    }

    /// <summary>
    /// Copies <paramref name="resources"/>, keeping only the original XObject
    /// entries the scrubbed content still invokes plus the newly created ones
    /// (registered through <paramref name="allocate"/>).
    /// </summary>
    internal static PdfDictionary BuildScrubbedResources(
        PdfDictionary? resources, ScrubResult result, IPdfObjectResolver resolver,
        Func<PdfStream, PdfReference> allocate)
    {
        PdfDictionary copy = new PdfDictionary();
        if (resources is not null)
        {
            foreach (PdfName key in resources.Keys)
            {
                copy.Set(key, resources[key]);
            }
        }

        PdfDictionary xobjects = new PdfDictionary();
        if (Res(resolver, copy.GetAs<PdfPrimitive>(PdfName.Intern("XObject"))) is PdfDictionary original)
        {
            foreach (PdfName key in original.Keys)
            {
                if (result.ReferencedXObjects.Contains(key.Value))
                {
                    xobjects.Set(key, original[key]);
                }
            }
        }

        foreach ((string newName, PdfStream stream) in result.NewXObjects)
        {
            xobjects.Set(PdfName.Intern(newName), allocate(stream));
        }

        copy.Set(PdfName.Intern("XObject"), xobjects);

        // Patterns and shadings: unused entries are pruned (their content
        // would stay in the file), used tiling-pattern cells are scrubbed to
        // the part that can show in the box, and mesh shadings keep only
        // the triangles or patches that can paint in it.
        if (Res(resolver, copy.GetAs<PdfPrimitive>(PdfName.Intern("Pattern"))) is PdfDictionary patterns)
        {
            PdfDictionary keptPatterns = new PdfDictionary();
            foreach (PdfName key in patterns.Keys)
            {
                if (result.Patterns.Contains(key.Value))
                {
                    keptPatterns.Set(key, ScrubPattern(patterns[key], PaintedRegion(result, key.Value), result, resolver, allocate));
                }
            }

            copy.Set(PdfName.Intern("Pattern"), keptPatterns);
        }

        if (Res(resolver, copy.GetAs<PdfPrimitive>(PdfName.Intern("Shading"))) is PdfDictionary shadings)
        {
            PdfDictionary keptShadings = new PdfDictionary();
            foreach (PdfName key in shadings.Keys)
            {
                if (!result.ShadingUses.TryGetValue(key.Value, out List<Matrix3x3>? uses))
                {
                    continue;
                }

                PdfPrimitive value = shadings[key];
                (double bx0, double by0, double bx1, double by1) = result.Box;
                if (MeshShadingTrimmer.Trim(value, uses, bx0, by0, bx1, by1, resolver, out PdfStream? trimmed)
                    == MeshShadingTrimmer.Outcome.Trimmed && trimmed is not null)
                {
                    value = allocate(trimmed);
                }

                keptShadings.Set(key, value);
            }

            copy.Set(PdfName.Intern("Shading"), keptShadings);
        }

        return copy;
    }

    // A pattern kept by scrubbed content: a tiling pattern's cell is
    // scrubbed to what can show in the box; a shading pattern's mesh is
    // trimmed. Anything else is kept as is.
    // The part of the box a pattern is painted over, or null when it paints
    // nothing there.
    private static (double X0, double Y0, double X1, double Y1)? PaintedRegion(ScrubResult owner, string name)
    {
        if (!owner.PatternExtents.TryGetValue(name, out (double X0, double Y0, double X1, double Y1) extent))
        {
            return null;
        }

        (double bx0, double by0, double bx1, double by1) = owner.Box;
        double rx0 = Math.Max(bx0, extent.X0);
        double ry0 = Math.Max(by0, extent.Y0);
        double rx1 = Math.Min(bx1, extent.X1);
        double ry1 = Math.Min(by1, extent.Y1);
        return rx1 > rx0 && ry1 > ry0 ? (rx0, ry0, rx1, ry1) : null;
    }

    private static PdfPrimitive ScrubPattern(
        PdfPrimitive value, (double X0, double Y0, double X1, double Y1)? region, ScrubResult owner,
        IPdfObjectResolver resolver, Func<PdfStream, PdfReference> allocate)
    {
        PdfPrimitive? resolved = Res(resolver, value);
        if (resolved is PdfStream tiling && NumOf(resolver, tiling.Dictionary.GetAs<PdfPrimitive>(PdfName.Intern("PatternType")) ?? PdfNull.Value) == 1)
        {
            return ScrubTilingPattern(tiling, region, owner, resolver, allocate) ?? value;
        }

        if (resolved is PdfDictionary shadingPattern
            && NumOf(resolver, shadingPattern.GetAs<PdfPrimitive>(PdfName.Intern("PatternType")) ?? PdfNull.Value) == 2
            && shadingPattern.TryGetValue(PdfName.Intern("Shading"), out PdfPrimitive? shading))
        {
            Matrix3x3 toPage = ReadMatrix(shadingPattern, resolver).Multiply(owner.BaseCtm);
            (double bx0, double by0, double bx1, double by1) = region ?? owner.Box;
            Matrix3x3[] uses = region is null ? Array.Empty<Matrix3x3>() : new[] { toPage };
            if (MeshShadingTrimmer.Trim(shading, uses, bx0, by0, bx1, by1, resolver, out PdfStream? trimmed)
                == MeshShadingTrimmer.Outcome.Trimmed && trimmed is not null)
            {
                PdfDictionary copy = new PdfDictionary();
                foreach (PdfName key in shadingPattern.Keys)
                {
                    copy.Set(key, shadingPattern[key]);
                }

                copy.Set(PdfName.Intern("Shading"), allocate(trimmed));
                return copy;
            }
        }

        return value;
    }

    // Most tile copies a cell is checked against before it is kept whole.
    private const int MaxTileCopies = 4096;

    // Scrubs a tiling pattern's cell (PDF 8.7.3.1). The cell repeats every
    // XStep x YStep in pattern space; only the parts of the cell that some
    // tile copy brings into the box can show. One such copy: the cell is
    // scrubbed exactly against the box through that copy's placement.
    // Several: it is scrubbed to the bounding box (in cell space) of their
    // visible parts — kept whole when that is the whole cell. No copy: the
    // cell is emptied. Returns null to keep the pattern unchanged.
    private static PdfPrimitive? ScrubTilingPattern(
        PdfStream pattern, (double X0, double Y0, double X1, double Y1)? region, ScrubResult owner,
        IPdfObjectResolver resolver, Func<PdfStream, PdfReference> allocate)
    {
        PdfDictionary dict = pattern.Dictionary;
        if (owner.Depth + 1 >= MaxFormDepth)
        {
            // Too deep (or a pattern cycle): the cell is emptied (B15).
            PdfDictionary emptied = new PdfDictionary();
            foreach (PdfName key in dict.Keys)
            {
                if (key.Value is not ("Filter" or "DecodeParms" or "Length" or "Resources" or "PieceInfo"))
                {
                    emptied.Set(key, dict[key]);
                }
            }

            emptied.Set(PdfName.Resources, new PdfDictionary());
            emptied.Set(PdfName.Length, 0);
            return allocate(new PdfStream(emptied, Array.Empty<byte>()));
        }

        Matrix3x3 toPage = ReadMatrix(dict, resolver).Multiply(owner.BaseCtm);
        if (!ScrubGeometry.TryInvert(toPage, out Matrix3x3 inverse)
            || Res(resolver, dict.GetAs<PdfPrimitive>(PdfName.Intern("BBox"))) is not PdfArray bbox || bbox.Count < 4)
        {
            return null;
        }

        double cx0 = Math.Min(NumOf(resolver, bbox[0]), NumOf(resolver, bbox[2]));
        double cx1 = Math.Max(NumOf(resolver, bbox[0]), NumOf(resolver, bbox[2]));
        double cy0 = Math.Min(NumOf(resolver, bbox[1]), NumOf(resolver, bbox[3]));
        double cy1 = Math.Max(NumOf(resolver, bbox[1]), NumOf(resolver, bbox[3]));
        double xs = Math.Abs(NumOf(resolver, dict.GetAs<PdfPrimitive>(PdfName.Intern("XStep")) ?? PdfNull.Value));
        double ys = Math.Abs(NumOf(resolver, dict.GetAs<PdfPrimitive>(PdfName.Intern("YStep")) ?? PdfNull.Value));
        if (xs < 1e-9 || ys < 1e-9 || cx1 - cx0 < 1e-9 || cy1 - cy0 < 1e-9)
        {
            return null;
        }

        // Only the painted part of the box matters; painted nowhere in the
        // box, the cell is emptied.
        (double X0, double Y0, double X1, double Y1) box = region ?? owner.Box;
        List<(double X, double Y)> boxInPattern = ScrubGeometry.TransformRectCorners(inverse, box.X0, box.Y0, box.X1, box.Y1);
        double bx0 = double.PositiveInfinity, by0 = double.PositiveInfinity;
        double bx1 = double.NegativeInfinity, by1 = double.NegativeInfinity;
        foreach ((double x, double y) in boxInPattern)
        {
            bx0 = Math.Min(bx0, x);
            by0 = Math.Min(by0, y);
            bx1 = Math.Max(bx1, x);
            by1 = Math.Max(by1, y);
        }

        double iMin = Math.Floor((bx0 - cx1) / xs);
        double iMax = Math.Ceiling((bx1 - cx0) / xs);
        double jMin = Math.Floor((by0 - cy1) / ys);
        double jMax = Math.Ceiling((by1 - cy0) / ys);
        if ((iMax - iMin + 1) * (jMax - jMin + 1) > MaxTileCopies)
        {
            return null;
        }

        List<(double X, double Y)> cell = new List<(double X, double Y)>
        {
            (cx0, cy0), (cx1, cy0), (cx1, cy1), (cx0, cy1),
        };
        List<(long I, long J, List<(double X, double Y)> Piece)> pieces =
            new List<(long I, long J, List<(double X, double Y)> Piece)>();
        for (long i = (long)iMin; region is not null && i <= (long)iMax; i++)
        {
            for (long j = (long)jMin; j <= (long)jMax; j++)
            {
                List<(double X, double Y)> shifted = new List<(double X, double Y)>(4);
                foreach ((double x, double y) in boxInPattern)
                {
                    shifted.Add((x - (i * xs), y - (j * ys)));
                }

                // A copy that only touches the cell's edge shows none of it.
                List<(double X, double Y)> piece = ScrubGeometry.ClipPolygon(shifted, cell);
                if (piece.Count >= 3 && Math.Abs(PolygonArea(piece)) > 1e-9 * (cx1 - cx0) * (cy1 - cy0))
                {
                    pieces.Add((i, j, piece));
                }
            }
        }

        PdfDictionary? cellResources = Res(resolver, dict.GetAs<PdfPrimitive>(PdfName.Resources)) as PdfDictionary;
        byte[] content;
        try
        {
            content = pieces.Count == 0 ? Array.Empty<byte>() : DecodeFormContent(pattern);
        }
        catch (Exception)
        {
            content = Array.Empty<byte>(); // undecodable cell: emptied (B15)
            pieces.Clear();
        }

        ScrubResult? nested = null;
        if (pieces.Count == 1)
        {
            Matrix3x3 copyPlacement = new Matrix3x3(1, 0, 0, 1, pieces[0].I * xs, pieces[0].J * ys).Multiply(toPage);
            nested = ScrubCore(content, box.X0, box.Y0, box.X1, box.Y1, cellResources, resolver, allocate, copyPlacement, owner.Depth + 1);
        }
        else if (pieces.Count > 1)
        {
            // Visible parts whose bounding boxes overlap are merged into one
            // group; separate groups keep the gaps between them (a cell
            // with spacing between tiles shows only strips of itself).
            List<(double X0, double Y0, double X1, double Y1, int Piece)> groups = GroupBounds(pieces);
            const double Eps = 1e-6;
            if (groups.Count == 1)
            {
                (double ux0, double uy0, double ux1, double uy1, int _) = groups[0];
                if (ux0 <= cx0 + Eps && uy0 <= cy0 + Eps && ux1 >= cx1 - Eps && uy1 >= cy1 - Eps)
                {
                    return null;
                }

                nested = ScrubCore(content, ux0, uy0, ux1, uy1, cellResources, resolver, allocate, Matrix3x3.Identity, owner.Depth + 1);
            }
            else
            {
                // One form per group, bounded by its group's box; the cell
                // draws them in turn. A group of one visible part is scrubbed
                // exactly against the box through its tile copy's placement
                // (a rotated pattern's parts are not axis-aligned in the
                // cell); a merged group is scrubbed to its box in the cell.
                StringBuilder cellContent = new StringBuilder();
                PdfDictionary groupForms = new PdfDictionary();
                for (int g = 0; g < groups.Count; g++)
                {
                    (double gx0, double gy0, double gx1, double gy1, int single) = groups[g];
                    ScrubResult part = single >= 0
                        ? ScrubCore(
                            content, box.X0, box.Y0, box.X1, box.Y1, cellResources, resolver, allocate,
                            new Matrix3x3(1, 0, 0, 1, pieces[single].I * xs, pieces[single].J * ys).Multiply(toPage),
                            owner.Depth + 1)
                        : ScrubCore(content, gx0, gy0, gx1, gy1, cellResources, resolver, allocate, Matrix3x3.Identity, owner.Depth + 1);
                    PdfDictionary formDict = new PdfDictionary();
                    formDict.Set(PdfName.Type, PdfName.Intern("XObject"));
                    formDict.Set(PdfName.Subtype, PdfName.Intern("Form"));
                    formDict.Set(PdfName.Intern("BBox"), new PdfArray(new PdfPrimitive[]
                    {
                        new PdfReal(gx0), new PdfReal(gy0), new PdfReal(gx1), new PdfReal(gy1),
                    }));
                    formDict.Set(PdfName.Resources, BuildScrubbedResources(cellResources, part, resolver, allocate));
                    formDict.Set(PdfName.Length, part.Content.Length);
                    string formName = "ScrubCell" + g.ToString(CultureInfo.InvariantCulture);
                    groupForms.Set(PdfName.Intern(formName), allocate(new PdfStream(formDict, part.Content)));
                    cellContent.Append('/').Append(formName).Append(" Do\n");
                }

                PdfDictionary groupDict = new PdfDictionary();
                foreach (PdfName key in dict.Keys)
                {
                    if (key.Value is not ("Filter" or "DecodeParms" or "Length" or "Resources" or "PieceInfo"))
                    {
                        groupDict.Set(key, dict[key]);
                    }
                }

                PdfDictionary groupResources = new PdfDictionary();
                groupResources.Set(PdfName.Intern("XObject"), groupForms);
                groupDict.Set(PdfName.Resources, groupResources);
                byte[] groupContent = Encoding.Latin1.GetBytes(cellContent.ToString());
                groupDict.Set(PdfName.Length, groupContent.Length);
                return allocate(new PdfStream(groupDict, groupContent));
            }
        }

        PdfDictionary newDict = new PdfDictionary();
        foreach (PdfName key in dict.Keys)
        {
            if (key.Value is "Filter" or "DecodeParms" or "Length" or "Resources" or "PieceInfo")
            {
                continue;
            }

            newDict.Set(key, dict[key]);
        }

        byte[] newContent = nested?.Content ?? Array.Empty<byte>();
        newDict.Set(
            PdfName.Resources,
            nested is null ? new PdfDictionary() : BuildScrubbedResources(cellResources, nested, resolver, allocate));
        newDict.Set(PdfName.Length, newContent.Length);
        return allocate(new PdfStream(newDict, newContent));
    }

    // Bounding boxes of the visible cell parts, merged while any two
    // overlap; each carries the index of its part when it holds just one
    // (-1 once merged).
    private static List<(double X0, double Y0, double X1, double Y1, int Piece)> GroupBounds(
        List<(long I, long J, List<(double X, double Y)> Piece)> pieces)
    {
        List<(double X0, double Y0, double X1, double Y1, int Piece)> boxes =
            new List<(double X0, double Y0, double X1, double Y1, int Piece)>();
        for (int index = 0; index < pieces.Count; index++)
        {
            List<(double X, double Y)> piece = pieces[index].Piece;
            double x0 = double.PositiveInfinity, y0 = double.PositiveInfinity;
            double x1 = double.NegativeInfinity, y1 = double.NegativeInfinity;
            foreach ((double x, double y) in piece)
            {
                x0 = Math.Min(x0, x);
                y0 = Math.Min(y0, y);
                x1 = Math.Max(x1, x);
                y1 = Math.Max(y1, y);
            }

            boxes.Add((x0, y0, x1, y1, index));
        }

        bool merged = true;
        while (merged)
        {
            merged = false;
            for (int a = 0; a < boxes.Count && !merged; a++)
            {
                for (int b = a + 1; b < boxes.Count; b++)
                {
                    (double ax0, double ay0, double ax1, double ay1, int _) = boxes[a];
                    (double bx0, double by0, double bx1, double by1, int _) = boxes[b];
                    if (ax0 < bx1 && bx0 < ax1 && ay0 < by1 && by0 < ay1)
                    {
                        boxes[a] = (Math.Min(ax0, bx0), Math.Min(ay0, by0), Math.Max(ax1, bx1), Math.Max(ay1, by1), -1);
                        boxes.RemoveAt(b);
                        merged = true;
                        break;
                    }
                }
            }
        }

        return boxes;
    }

    private static double PolygonArea(List<(double X, double Y)> polygon)
    {
        double area = 0;
        for (int i = 0; i < polygon.Count; i++)
        {
            (double ax, double ay) = polygon[i];
            (double bx, double by) = polygon[(i + 1) % polygon.Count];
            area += (ax * by) - (bx * ay);
        }

        return area / 2;
    }

    private static Matrix3x3 ReadMatrix(PdfDictionary dict, IPdfObjectResolver resolver)
    {
        if (Res(resolver, dict.GetAs<PdfPrimitive>(PdfName.Intern("Matrix"))) is PdfArray m && m.Count >= 6)
        {
            return new Matrix3x3(
                NumOf(resolver, m[0]), NumOf(resolver, m[1]), NumOf(resolver, m[2]),
                NumOf(resolver, m[3]), NumOf(resolver, m[4]), NumOf(resolver, m[5]));
        }

        return Matrix3x3.Identity;
    }

    private static byte[] DecodeFormContent(PdfStream form)
    {
        byte[] data = form.RawBytes;
        PdfPrimitive? filter = form.Filter;
        if (filter is null)
        {
            return data;
        }

        Chuvadi.Pdf.Filters.FilterPipeline pipe = Chuvadi.Pdf.Filters.FilterRegistry.CreateDefaultPipeline();
        PdfPrimitive? parms = form.Dictionary.GetAs<PdfPrimitive>(PdfName.Intern("DecodeParms"));
        if (filter is PdfName single)
        {
            return pipe.Decode(Chuvadi.Pdf.Filters.FilterRegistry.ResolveAlias(single.Value), data,
                Chuvadi.Pdf.Filters.FilterParameters.FromDictionary(parms, 0));
        }

        if (filter is PdfArray chain)
        {
            for (int i = 0; i < chain.Count; i++)
            {
                if (chain[i] is PdfName n)
                {
                    data = pipe.Decode(Chuvadi.Pdf.Filters.FilterRegistry.ResolveAlias(n.Value), data,
                        Chuvadi.Pdf.Filters.FilterParameters.FromDictionary(parms, i));
                }
            }
        }

        return data;
    }

    // Writes "q <place> cm /ScrubImN Do Q" for a newly created XObject.
    private static void EmitPlacedXObject(
        StringBuilder output, List<(string Name, PdfStream Stream)> newXObjects,
        PdfStream stream, Matrix3x3 place, string prefix)
    {
        string newName = prefix + newXObjects.Count.ToString(CultureInfo.InvariantCulture);
        newXObjects.Add((newName, stream));
        output.Append("q ")
              .Append(F(place.A)).Append(' ').Append(F(place.B)).Append(' ')
              .Append(F(place.C)).Append(' ').Append(F(place.D)).Append(' ')
              .Append(F(place.E)).Append(' ').Append(F(place.F)).Append(" cm /")
              .Append(newName).Append(" Do Q\n");
    }

    private static double NumOf(IPdfObjectResolver resolver, PdfPrimitive p) => Res(resolver, p) switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0.0,
    };

    // Decodes a name operand ("/Im#20A") to its resource key ("Im A").
    private static string DecodeNameToken(string token)
    {
        string name = token.StartsWith('/') ? token.Substring(1) : token;
        if (!name.Contains('#'))
        {
            return name;
        }

        StringBuilder sb = new StringBuilder(name.Length);
        for (int i = 0; i < name.Length; i++)
        {
            if (name[i] == '#' && i + 2 < name.Length
                && int.TryParse(name.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
            {
                sb.Append((char)code);
                i += 2;
            }
            else
            {
                sb.Append(name[i]);
            }
        }

        return sb.ToString();
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
