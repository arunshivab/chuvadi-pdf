// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008
// PHASE: Phase 2.1 — SVG renderer over display list
//        v2.1.1 — image counter-flip fix
//        v2.1.4 — preserveAspectRatio="none" on emitted images (PDF places
//                 images in a unit square; the CTM carries the destination
//                 aspect ratio, so the default xMidYMid meet would
//                 letterbox and produce horizontally compressed output)
//        v2.1.2 — per-glyph X positions; bold/italic style hints;
//                 snap-tolerance for kerning gaps before space characters,
//                 resilient to interleaved graphics-state ops (Word q/Q);
//                 sub-pixel filled-rectangle thickening for Word borders;
//                 ClipOp application via nested <g clip-path> groups;
//                 font program embedding as @font-face data URLs so the
//                 PDF's exact glyph metrics drive browser rendering (fixes
//                 the splayed "Developed India's First..." bold-italic
//                 caused by browser font substitution and unblocks
//                 Wingdings/symbol-font rendering)

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Fonts;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Rendering.DisplayList;
using ColorF = Chuvadi.Pdf.Graphics.ColorF;

namespace Chuvadi.Pdf.Svg;

/// <summary>
/// Renders a <see cref="PageDisplayList"/> to SVG.
/// </summary>
/// <remarks>
/// <para>
/// Phase 2.1 architectural pivot: SVG output no longer walks the PDF content
/// stream directly. Instead, <see cref="DisplayListBuilder"/> produces a
/// neutral <see cref="PageDisplayList"/>, and this renderer turns it into
/// SVG. The same display list also feeds the WPF renderer and any future
/// output adapters.
/// </para>
/// <para>
/// Coordinate system: PDF uses bottom-left origin, SVG uses top-left. The
/// output wraps content in a single <c>&lt;g transform="matrix(1 0 0 -1 0 H)"&gt;</c>
/// outer group so PDF-native coordinates flow through directly. Text elements
/// and images both receive a local counter-flip to read upright after the
/// outer flip is applied.
/// </para>
/// <para>
/// v2.1.2/v2.1.3 text positioning: per-glyph X attributes on SVG
/// <c>&lt;text&gt;</c> override the rendering font's natural advance widths.
/// When an <c>@font-face</c> is registered for a run's BaseFont (see font
/// embedding below), the embedded program's hmtx already encodes the
/// correct glyph advances, and per-glyph X is suppressed so the font
/// drives layout. When no embedded font is available, per-glyph X
/// positions from PDF /Widths preserve inter-character spacing through
/// the generic CSS fallback. The renderer also tracks per-line position
/// across consecutive TextOps and shrinks excess gaps that appear before
/// a space-starting run; with embedded fonts that shrink is suppressed,
/// because the rendered run extent (hmtx-driven) can disagree with the
/// gap calculation (/Widths-driven) and over-shrinking would visibly
/// swallow the space. The DisplayList fold collapses Word's
/// kern-before-space idiom into the surrounding run, so most TextOps no
/// longer start with a bare space in the first place — making the
/// suppressed shrink branch a rare path.
/// </para>
/// <para>
/// v2.1.2 path/border handling: filled rectangles with one dimension below
/// <see cref="MinVisibleThickness"/> are expanded symmetrically about their
/// midline so Word's 0.48-unit table borders remain visible at all zooms.
/// </para>
/// <para>
/// v2.1.2 clip application: each PDF graphics-state scope is wrapped in a
/// <c>&lt;g&gt;</c>; ClipOps open additional <c>&lt;g clip-path&gt;</c>
/// wrappers inside the scope that close when the matching Pop arrives. The
/// SVG renderer naturally intersects nested clips.
/// </para>
/// <para>
/// v2.1.2 font embedding: at the start of rendering, every distinct font
/// dictionary referenced on the page is offered to <see cref="FontEmbedder"/>
/// which extracts the embedded font program (FontFile2/FontFile3), base64-
/// encodes it, and emits a CSS <c>@font-face</c> rule. The renderer builds a
/// mapping from each font's BaseFont name to the assigned CSS family and
/// consults it during text emission. Without embedding, browsers substitute
/// the PDF's subsetted fonts with system Times/Arial, whose glyph advance
/// widths differ; the result is visible inter-character drift on multi-run
/// lines (e.g. "Developed India's First..."). Embedding makes the PDF's own
/// metrics authoritative.
/// </para>
/// </remarks>
public sealed class SvgRenderer
{
    private readonly SvgExportOptions _opts;

    // v2.1.2 snap-tolerance parameters.
    private const double KerningGapMinFraction = 0.2;
    private const double KerningGapMaxFactor = 2.0;
    private const double SameLineYTolerance = 0.5;

    // v2.1.2 minimum visible thickness for axis-aligned rectangular fills.
    private const double MinVisibleThickness = 0.75;

    // v2.1.3 — peephole merge tolerance for adjacent same-fill rectangles.
    // Word emits a single row background as multiple abutting rectangles
    // (one per table cell); the sub-pixel overlaps and exact touches at
    // their joins produce double-anti-aliased hairline seams in the SVG.
    // Consecutive fill-only PathOps whose Y ranges agree within this
    // tolerance and whose X ranges abut or overlap within this tolerance
    // are merged into a single rectangle covering the X union — yielding
    // one path element with one anti-aliasing pass and no visible seam.
    // Value is in user-space points; 0.5pt is well under any meaningful
    // intentional gap a PDF author would draw, while being large enough
    // to cover Word's sub-pixel emission noise.
    private const double RectMergeTolerance = 0.5;

    /// <summary>Initialises a renderer with default options.</summary>
    public SvgRenderer() : this(new SvgExportOptions()) { }

    /// <summary>Initialises a renderer with the given options.</summary>
    public SvgRenderer(SvgExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _opts = options;
    }

    /// <summary>Renders one page of <paramref name="document"/> to an SVG string.</summary>
    public string RenderPage(PdfDocument document, int pageIndex)
    {
        ArgumentNullException.ThrowIfNull(document);
        PageDisplayList list = DisplayListBuilder.Build(document, pageIndex);
        return Render(list, document.Objects);
    }

    /// <summary>Renders one page of <paramref name="document"/> to UTF-8 bytes.</summary>
    public byte[] RenderPageBytes(PdfDocument document, int pageIndex)
        => Encoding.UTF8.GetBytes(RenderPage(document, pageIndex));

    /// <summary>Renders one page of <paramref name="document"/> to <paramref name="output"/>.</summary>
    public void RenderPage(PdfDocument document, int pageIndex, Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);
        byte[] bytes = RenderPageBytes(document, pageIndex);
        output.Write(bytes, 0, bytes.Length);
    }

    /// <summary>Enumerates SVG renders for all pages of <paramref name="document"/>.</summary>
    public IEnumerable<string> RenderPages(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        for (int i = 0; i < document.PageCount; i++)
        {
            yield return RenderPage(document, i);
        }
    }

    /// <summary>
    /// Renders a pre-built <see cref="PageDisplayList"/> without embedding
    /// fonts. Used by the WPF rasterizer and other callers that don't need
    /// CSS @font-face support, and by tests that build display lists
    /// synthetically.
    /// </summary>
    public string Render(PageDisplayList list) => Render(list, resolver: null);

    /// <summary>
    /// Renders a pre-built <see cref="PageDisplayList"/>, optionally embedding
    /// font programs as <c>@font-face</c> rules. Pass a non-null
    /// <paramref name="resolver"/> to enable embedding; the resolver is used
    /// to walk into FontDescriptor → FontFile2/FontFile3 streams.
    /// </summary>
    public string Render(PageDisplayList list, IPdfObjectResolver? resolver)
    {
        ArgumentNullException.ThrowIfNull(list);
        SvgWriter w = new(_opts.Precision);
        w.StartSvg(list.MediaWidth, list.MediaHeight);

        // Opaque page sheet behind all content (unflipped coordinates), so the
        // SVG matches the rasteriser's white paper and composites consistently
        // across viewers. Skipped when Background is null (transparent export).
        if (_opts.Background is ColorF background)
        {
            w.EmitPageBackground(list.MediaWidth, list.MediaHeight, ToHexColor(background));
        }

        // Build the per-page font-embedding registry BEFORE the page-flip
        // group opens. AddFontFace inserts <style>@font-face{...}</style>
        // into <defs>, which is written at the top of the SVG by
        // ToSvgString — so the call order here doesn't affect where the
        // rule ends up in the output, but doing it first keeps the body
        // order clean.
        Dictionary<string, string> familyByBaseFont = BuildFontRegistry(list, w, resolver);

        w.OpenPageFlip(list.MediaHeight);

        double accumulatedShift = 0;
        double prevLineY = double.NaN;
        double prevEndX = double.NaN;

        Stack<int> clipsOpenedPerScope = new();
        clipsOpenedPerScope.Push(0);

        // v2.1.3 — peephole buffer for adjacent same-fill rectangles. Held
        // while consecutive mergeable PathOps arrive; flushed before any
        // other op so paint order is strictly preserved.
        PendingFillRect? pendingFillRect = null;

        foreach (RenderOp op in list)
        {
            switch (op)
            {
                case PathOp p:
                    if (TryBuildFillRectCandidate(p, out PendingFillRect cand))
                    {
                        BufferOrEmitFillRect(cand, ref pendingFillRect, w);
                    }
                    else
                    {
                        FlushPendingFillRect(ref pendingFillRect, w);
                        EmitPath(p, w);
                    }
                    break;
                case TextOp t:
                    FlushPendingFillRect(ref pendingFillRect, w);
                    EmitTextWithSnap(t, w, familyByBaseFont,
                        ref accumulatedShift, ref prevLineY, ref prevEndX);
                    break;
                case ImageOp i:
                    FlushPendingFillRect(ref pendingFillRect, w);
                    EmitImage(i, w);
                    break;
                case ClipOp c:
                    FlushPendingFillRect(ref pendingFillRect, w);
                    EmitClip(c, w, clipsOpenedPerScope);
                    break;
                case TransformOp xf:
                    FlushPendingFillRect(ref pendingFillRect, w);
                    if (xf.Push)
                    {
                        w.OpenGroup();
                        clipsOpenedPerScope.Push(0);
                    }
                    else
                    {
                        int clipsToClose = clipsOpenedPerScope.Pop();
                        for (int i = 0; i < clipsToClose; i++) { w.CloseGroup(); }
                        w.CloseGroup();
                    }
                    break;
                case OpacityOp op2:
                    FlushPendingFillRect(ref pendingFillRect, w);
                    if (op2.Push)
                    {
                        string attrs = $"opacity=\"{op2.Alpha.ToString("0.###", CultureInfo.InvariantCulture)}\"";
                        if (op2.Isolated) { attrs += " style=\"isolation:isolate\""; }
                        w.OpenGroup(extraAttrs: attrs);
                        clipsOpenedPerScope.Push(0);
                    }
                    else
                    {
                        int clipsToClose = clipsOpenedPerScope.Pop();
                        for (int i = 0; i < clipsToClose; i++) { w.CloseGroup(); }
                        w.CloseGroup();
                    }
                    break;
                case BlendModeOp bm:
                    FlushPendingFillRect(ref pendingFillRect, w);
                    if (bm.Push)
                    {
                        w.OpenGroup(extraAttrs: $"style=\"mix-blend-mode:{BlendModeCss(bm.Mode)}\"");
                        clipsOpenedPerScope.Push(0);
                    }
                    else
                    {
                        int clipsToClose = clipsOpenedPerScope.Pop();
                        for (int i = 0; i < clipsToClose; i++) { w.CloseGroup(); }
                        w.CloseGroup();
                    }
                    break;
                default: break;
            }
        }

        // Flush any final pending rectangle before closing the root group.
        FlushPendingFillRect(ref pendingFillRect, w);

        int rootClipsToClose = clipsOpenedPerScope.Pop();
        for (int i = 0; i < rootClipsToClose; i++) { w.CloseGroup(); }
        w.CloseGroup();
        return w.ToSvgString();
    }

    // ── Font registry ────────────────────────────────────────────────────

    /// <summary>
    /// For each font dictionary on the page, attempts to embed it as an
    /// <c>@font-face</c> rule and records the resulting CSS family by the
    /// font's BaseFont name (with any subset prefix stripped). Returns a
    /// dictionary that text emission consults to choose font-family.
    /// </summary>
    /// <remarks>
    /// v2.1.5: a <see cref="PdfFont"/> is constructed alongside each font
    /// dictionary and handed to <see cref="FontEmbedder.TryEmbed"/>. The
    /// embedder consults the font's ToUnicode mapping to rewrite the
    /// embedded TrueType font program's cmap so the browser can address
    /// glyphs at semantic Unicode code points rather than the legacy
    /// encoding code points the font program ships with. If PdfFont
    /// construction fails (corrupt dictionary, unparseable ToUnicode),
    /// the embedder receives null and falls back to v2.1.4 behaviour.
    /// </remarks>
    private static Dictionary<string, string> BuildFontRegistry(
        PageDisplayList list, SvgWriter w, IPdfObjectResolver? resolver)
    {
        Dictionary<string, string> familyByBaseFont = new(StringComparer.Ordinal);
        if (resolver is null || list.FontDictsByKey.Count == 0)
        {
            return familyByBaseFont;
        }
        HashSet<string> emittedFamilies = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, PdfDictionary> kv in list.FontDictsByKey)
        {
            string baseFont = ExtractBaseFontName(kv.Value);
            if (familyByBaseFont.ContainsKey(baseFont)) { continue; }

            PdfFont? pdfFont = null;
            try { pdfFont = PdfFont.FromDictionary(kv.Value, resolver); }
            catch (Exception) { /* leave pdfFont null; embed without cmap remap */ }

            string? family = FontEmbedder.TryEmbed(
                kv.Value, baseFont, w, resolver, emittedFamilies, pdfFont);
            if (family is not null)
            {
                familyByBaseFont[baseFont] = family;
            }
        }
        return familyByBaseFont;
    }

    /// <summary>
    /// Returns the font's BaseFont name with any subset prefix stripped.
    /// Subset prefixes are six uppercase letters followed by <c>+</c>
    /// per PDF 32000-1 §9.6.4.
    /// </summary>
    private static string ExtractBaseFontName(PdfDictionary fontDict)
    {
        if (fontDict.TryGetValue(PdfName.Intern("BaseFont"), out PdfPrimitive? bf)
            && bf is PdfName name)
        {
            string raw = name.Value;
            int plus = raw.IndexOf('+');
            return plus >= 0 && plus < raw.Length - 1 ? raw[(plus + 1)..] : raw;
        }
        return "Unknown";
    }

    // ── Op emitters ──────────────────────────────────────────────────────

    /// <summary>
    /// Emits a <see cref="PathOp"/> directly without participating in the
    /// peephole rectangle merge. Axis-aligned rectangular fills whose
    /// smaller dimension is below <see cref="MinVisibleThickness"/> are
    /// re-emitted with that dimension expanded so the fill stays visible
    /// at all zoom levels. The dispatch loop is responsible for routing
    /// fill-only rectangular ops through the merge buffer instead; this
    /// method handles all other paths (strokes, fill+stroke, non-rect
    /// geometries, and the eventual emission of a merged rectangle).
    /// </summary>
    private void EmitPath(PathOp op, SvgWriter w)
    {
        bool fill = op.Mode is PaintMode.Fill or PaintMode.FillAndStroke;
        bool stroke = op.Mode is PaintMode.Stroke or PaintMode.FillAndStroke;
        string? fillCol = fill ? SrgbCss(op.FillColor) : null;
        string? strokeCol = stroke ? SrgbCss(op.StrokeColor) : null;
        double strokeWidth = op.Stroke?.LineWidth ?? 0;
        string fillRule = op.FillRule == FillRule.EvenOdd ? "evenodd" : "nonzero";

        string? extra = null;
        if (stroke && op.Stroke?.DashArray is { Length: > 0 } da)
        {
            StringBuilder ds = new();
            for (int i = 0; i < da.Length; i++)
            {
                if (i > 0) { ds.Append(','); }
                ds.Append(da[i].ToString("0.##", CultureInfo.InvariantCulture));
            }
            extra = $"stroke-dasharray=\"{ds}\"";
        }

        string d;
        if (fill && !stroke
            && TryGetAxisAlignedRectangle(op.Geometry,
                out double minX, out double minY, out double maxX, out double maxY))
        {
            double width = maxX - minX;
            double height = maxY - minY;
            if (width < MinVisibleThickness && width > 0)
            {
                double centre = (minX + maxX) / 2;
                minX = centre - MinVisibleThickness / 2;
                maxX = centre + MinVisibleThickness / 2;
            }
            if (height < MinVisibleThickness && height > 0)
            {
                double centre = (minY + maxY) / 2;
                minY = centre - MinVisibleThickness / 2;
                maxY = centre + MinVisibleThickness / 2;
            }
            d = BuildRectanglePath(minX, minY, maxX, maxY, _opts.Precision);
        }
        else
        {
            d = PathToSvg(op.Geometry, _opts.Precision);
        }

        w.EmitPath(d, fillCol, strokeCol, strokeWidth, fillRule, extra);
    }

    /// <summary>
    /// v2.1.3 — pending fill-only axis-aligned rectangle awaiting merge.
    /// Carries the current X/Y bounds and the CSS fill string. The dispatch
    /// loop holds at most one of these between consecutive same-color
    /// rectangles; any non-mergeable op flushes it first.
    /// </summary>
    private struct PendingFillRect
    {
        public double MinX;
        public double MinY;
        public double MaxX;
        public double MaxY;
        public string FillCss;
        public string FillRule;
    }

    /// <summary>
    /// v2.1.3 — if <paramref name="op"/> is a fill-only axis-aligned
    /// rectangle, returns true with <paramref name="rect"/> populated to
    /// the (possibly thickness-expanded) bounds and CSS fill. Strokes,
    /// fill-and-stroke, and non-rect geometries return false and must be
    /// emitted through the unbuffered path.
    /// </summary>
    private bool TryBuildFillRectCandidate(PathOp op, out PendingFillRect rect)
    {
        rect = default;
        bool fill = op.Mode is PaintMode.Fill or PaintMode.FillAndStroke;
        bool stroke = op.Mode is PaintMode.Stroke or PaintMode.FillAndStroke;
        if (!fill || stroke) { return false; }
        if (!TryGetAxisAlignedRectangle(op.Geometry,
            out double minX, out double minY, out double maxX, out double maxY))
        {
            return false;
        }

        double width = maxX - minX;
        double height = maxY - minY;
        if (width < MinVisibleThickness && width > 0)
        {
            double centre = (minX + maxX) / 2;
            minX = centre - MinVisibleThickness / 2;
            maxX = centre + MinVisibleThickness / 2;
        }
        if (height < MinVisibleThickness && height > 0)
        {
            double centre = (minY + maxY) / 2;
            minY = centre - MinVisibleThickness / 2;
            maxY = centre + MinVisibleThickness / 2;
        }

        rect = new PendingFillRect
        {
            MinX = minX,
            MinY = minY,
            MaxX = maxX,
            MaxY = maxY,
            FillCss = SrgbCss(op.FillColor),
            FillRule = op.FillRule == FillRule.EvenOdd ? "evenodd" : "nonzero",
        };
        return true;
    }

    /// <summary>
    /// v2.1.3 — buffer or merge a fill-only rectangle candidate. The
    /// dispatch loop holds at most one pending rectangle. If the new
    /// candidate shares fill color, Y range (within
    /// <see cref="RectMergeTolerance"/>), and abutting/overlapping X range
    /// (also within tolerance) with the pending one, the two are merged
    /// in place. Otherwise the pending one is flushed and the new one
    /// becomes the new pending.
    /// </summary>
    private void BufferOrEmitFillRect(PendingFillRect candidate,
        ref PendingFillRect? pending, SvgWriter w)
    {
        if (pending is PendingFillRect p
            && p.FillCss == candidate.FillCss
            && p.FillRule == candidate.FillRule
            && Math.Abs(p.MinY - candidate.MinY) <= RectMergeTolerance
            && Math.Abs(p.MaxY - candidate.MaxY) <= RectMergeTolerance
            && candidate.MinX <= p.MaxX + RectMergeTolerance
            && candidate.MaxX >= p.MinX - RectMergeTolerance)
        {
            pending = new PendingFillRect
            {
                MinX = Math.Min(p.MinX, candidate.MinX),
                MinY = Math.Min(p.MinY, candidate.MinY),
                MaxX = Math.Max(p.MaxX, candidate.MaxX),
                MaxY = Math.Max(p.MaxY, candidate.MaxY),
                FillCss = p.FillCss,
                FillRule = p.FillRule,
            };
            return;
        }

        FlushPendingFillRect(ref pending, w);
        pending = candidate;
    }

    /// <summary>
    /// v2.1.3 — emit the buffered fill-rectangle (if any) and clear the
    /// slot. Called before any non-mergeable op is dispatched, so paint
    /// order is preserved.
    /// </summary>
    private void FlushPendingFillRect(ref PendingFillRect? pending, SvgWriter w)
    {
        if (pending is not PendingFillRect p) { return; }
        string d = BuildRectanglePath(p.MinX, p.MinY, p.MaxX, p.MaxY, _opts.Precision);
        w.EmitPath(d, p.FillCss, null, 0, p.FillRule, null);
        pending = null;
    }

    /// <summary>
    /// Emits a text op, applying any accumulated v2.1.2 snap-shift, updating
    /// the per-line tracking state, and consulting the font registry to
    /// pick a CSS family.
    /// </summary>
    private void EmitTextWithSnap(TextOp op, SvgWriter w,
        Dictionary<string, string> familyByBaseFont,
        ref double accumulatedShift, ref double prevLineY, ref double prevEndX)
    {
        if (op.Glyphs.Count == 0) { return; }

        StringBuilder sb = new();
        List<double> xPositions = new(op.Glyphs.Count);
        bool allSingleChar = true;
        foreach (DisplayListGlyph g in op.Glyphs)
        {
            if (g.Unicode.Length == 0) { continue; }
            sb.Append(g.Unicode);
            xPositions.Add(g.X);
            if (g.Unicode.Length > 1) { allSingleChar = false; }
        }
        string text = sb.ToString();
        if (text.Length == 0) { return; }

        double opOriginX = op.Transform.E;
        double opLineY = op.Transform.F;
        bool sameLine = !double.IsNaN(prevLineY)
            && Math.Abs(opLineY - prevLineY) < SameLineYTolerance;

        if (!sameLine)
        {
            accumulatedShift = 0;
        }

        // v2.1.3 — embedded-font check is needed BEFORE the snap block so the
        // firstIsSpace shrink can be suppressed in the embedded-font case
        // (prevEndX is /Widths-derived but visible rendering uses the font's
        // own hmtx; the two can disagree and shrinking by the /Widths gap
        // would put the next TextOp on top of the previous one's natural
        // glyph extent).
        bool hasEmbeddedFont = HasEmbeddedFont(op.BaseFont, familyByBaseFont);

        double effectiveStartX = opOriginX + accumulatedShift
            + (op.Glyphs.Count > 0 ? op.Glyphs[0].X : 0);

        if (sameLine && !double.IsNaN(prevEndX))
        {
            double gap = effectiveStartX - prevEndX;
            double minGap = op.FontSize * KerningGapMinFraction;
            double maxGap = op.FontSize * KerningGapMaxFactor;
            bool firstIsSpace = op.Glyphs[0].Unicode == " ";

            if (gap >= maxGap)
            {
                effectiveStartX -= accumulatedShift;
                accumulatedShift = 0;
            }
            else if (!hasEmbeddedFont && firstIsSpace && gap > minGap)
            {
                double shrink = -gap;
                accumulatedShift += shrink;
                effectiveStartX += shrink;
            }
        }

        AffineMatrix shifted = new(
            op.Transform.A, op.Transform.B,
            op.Transform.C, op.Transform.D,
            op.Transform.E + accumulatedShift, op.Transform.F);
        AffineMatrix localFlip = new(1, 0, 0, -1, 0, 0);
        AffineMatrix combined = localFlip.Multiply(shifted);
        string transform = combined.ToSvgMatrix();
        string family = ResolveFamily(op.BaseFont, familyByBaseFont);
        string fill = SrgbCss(op.FillColor);
        (string? fontWeight, string? fontStyle) = ResolveStyleHints(op.Style);

        // v2.1.3 — embedded fonts carry authoritative glyph advances in hmtx;
        // emitting per-glyph X attributes would force the browser to override
        // them with PDF /Widths positions and produce visible drift when the
        // two disagree. Use the single-anchor path for embedded fonts; keep
        // per-glyph X for generic CSS fallback (where /Widths is the only
        // source of advance information we have).
        if (!hasEmbeddedFont && allSingleChar && xPositions.Count == text.Length)
        {
            w.EmitText(text, xPositions, 0, family, op.FontSize, fill, transform,
                fontWeight: fontWeight, fontStyle: fontStyle);
        }
        else
        {
            w.EmitText(text, 0, 0, family, op.FontSize, fill, transform,
                fontWeight: fontWeight, fontStyle: fontStyle);
        }

        double runLength = 0;
        if (op.Glyphs.Count > 0)
        {
            DisplayListGlyph last = op.Glyphs[op.Glyphs.Count - 1];
            runLength = last.X + last.Advance;
        }
        prevEndX = opOriginX + accumulatedShift + runLength;
        prevLineY = opLineY;
    }

    private void EmitImage(ImageOp op, SvgWriter w)
    {
        string? dataUrl = ImageEncoder.BuildDataUrl(op);
        if (dataUrl is null) { return; }

        AffineMatrix localFlip = new(1, 0, 0, -1, 0, 1);
        AffineMatrix combined = localFlip.Multiply(op.Transform);
        string transform = combined.ToSvgMatrix();

        w.OpenGroup(transform);
        // v2.1.4: PDF places images in a unit square at (0,0)-(1,1); the
        // CTM (already applied via the group transform above) encodes the
        // destination width and height. preserveAspectRatio="none" tells
        // the SVG user agent not to letterbox the bitmap inside that unit
        // square — otherwise non-square images render visibly compressed
        // along the long axis.
        w.EmitImage(dataUrl, 0, 0, 1, 1, preserveAspectRatio: "none");
        w.CloseGroup();
    }

    /// <summary>
    /// Emits a clip op: adds the clip path to <c>&lt;defs&gt;</c> and opens
    /// a nested <c>&lt;g clip-path&gt;</c> wrapper so subsequent content in
    /// the same graphics-state scope is clipped.
    /// </summary>
    private void EmitClip(ClipOp op, SvgWriter w, Stack<int> clipsOpenedPerScope)
    {
        string d = PathToSvg(op.Geometry, _opts.Precision);
        string rule = op.FillRule == FillRule.EvenOdd ? "evenodd" : "nonzero";
        string clipId = w.AddClipPath(d, rule);
        w.OpenGroup(clipPathId: clipId);

        int current = clipsOpenedPerScope.Pop();
        clipsOpenedPerScope.Push(current + 1);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the CSS font-family value for a PDF BaseFont, preferring an
    /// embedded <c>@font-face</c> family if one was registered for this page
    /// and falling back to a generic CSS family stack otherwise.
    /// </summary>
    private static string ResolveFamily(string baseFont,
        Dictionary<string, string> familyByBaseFont)
    {
        // Strip subset prefix here too, since op.BaseFont may carry it in
        // some code paths even though DisplayListBuilder strips it.
        string lookupKey = baseFont;
        int plus = lookupKey.IndexOf('+');
        if (plus >= 0 && plus < lookupKey.Length - 1)
        {
            lookupKey = lookupKey[(plus + 1)..];
        }
        if (familyByBaseFont.TryGetValue(lookupKey, out string? embedded))
        {
            // Compose with generic fallback so missing glyphs (e.g. characters
            // not in the embedded subset's cmap) still render through a system
            // font rather than as .notdef boxes.
            return $"\"{embedded}\", {CssFamilyFor(lookupKey)}";
        }
        return CssFamilyFor(lookupKey);
    }

    /// <summary>
    /// Returns true if an embedded <c>@font-face</c> family was registered
    /// for the given base font on this page. Subset-prefix stripping mirrors
    /// <see cref="ResolveFamily"/> so the two lookups agree.
    /// </summary>
    private static bool HasEmbeddedFont(string baseFont,
        Dictionary<string, string> familyByBaseFont)
    {
        ArgumentNullException.ThrowIfNull(baseFont);
        ArgumentNullException.ThrowIfNull(familyByBaseFont);
        string lookupKey = baseFont;
        int plus = lookupKey.IndexOf('+');
        if (plus >= 0 && plus < lookupKey.Length - 1)
        {
            lookupKey = lookupKey[(plus + 1)..];
        }
        return familyByBaseFont.ContainsKey(lookupKey);
    }

    private static bool TryGetAxisAlignedRectangle(PathGeometry g,
        out double minX, out double minY, out double maxX, out double maxY)
    {
        minX = minY = maxX = maxY = 0;
        if (g.Segments.Count < 4) { return false; }

        List<(double X, double Y)> corners = new(4);
        int segmentsConsidered = 0;
        foreach (PathSegment seg in g.Segments)
        {
            if (segmentsConsidered >= 5) { break; }
            switch (seg.Command)
            {
                case PathCommand.MoveTo:
                case PathCommand.LineTo:
                    corners.Add((seg.X1, seg.Y1));
                    break;
                case PathCommand.Close:
                    break;
                case PathCommand.CubicTo:
                    return false;
                default: return false;
            }
            segmentsConsidered++;
        }

        if (corners.Count != 4) { return false; }

        const double eps = 0.001;
        double x0 = corners[0].X;
        double x1 = corners[0].X;
        double y0 = corners[0].Y;
        double y1 = corners[0].Y;
        foreach ((double cx, double cy) in corners)
        {
            if (Math.Abs(cx - x0) > eps && Math.Abs(cx - x1) > eps)
            {
                if (Math.Abs(x0 - x1) < eps) { x1 = cx; }
                else { return false; }
            }
            if (Math.Abs(cy - y0) > eps && Math.Abs(cy - y1) > eps)
            {
                if (Math.Abs(y0 - y1) < eps) { y1 = cy; }
                else { return false; }
            }
        }
        if (Math.Abs(x0 - x1) < eps || Math.Abs(y0 - y1) < eps) { return false; }

        minX = Math.Min(x0, x1);
        maxX = Math.Max(x0, x1);
        minY = Math.Min(y0, y1);
        maxY = Math.Max(y0, y1);
        return true;
    }

    private static string BuildRectanglePath(double minX, double minY,
        double maxX, double maxY, int precision)
    {
        string fmt = "0." + new string('#', precision);
        StringBuilder sb = new();
        sb.Append("M ").Append(minX.ToString(fmt, CultureInfo.InvariantCulture))
          .Append(' ').Append(minY.ToString(fmt, CultureInfo.InvariantCulture));
        sb.Append(" L ").Append(maxX.ToString(fmt, CultureInfo.InvariantCulture))
          .Append(' ').Append(minY.ToString(fmt, CultureInfo.InvariantCulture));
        sb.Append(" L ").Append(maxX.ToString(fmt, CultureInfo.InvariantCulture))
          .Append(' ').Append(maxY.ToString(fmt, CultureInfo.InvariantCulture));
        sb.Append(" L ").Append(minX.ToString(fmt, CultureInfo.InvariantCulture))
          .Append(' ').Append(maxY.ToString(fmt, CultureInfo.InvariantCulture));
        sb.Append(" Z");
        return sb.ToString();
    }

    /// <summary>
    /// Formats a colour as an SVG <c>#rrggbb</c> string, converting to DeviceRGB
    /// first so gray and CMYK page backgrounds resolve correctly.
    /// </summary>
    private static string ToHexColor(ColorF color)
    {
        ColorF rgb = color.ToRgb();
        int r = (int)Math.Round(Math.Clamp(rgb.R, 0f, 1f) * 255f);
        int g = (int)Math.Round(Math.Clamp(rgb.G, 0f, 1f) * 255f);
        int b = (int)Math.Round(Math.Clamp(rgb.B, 0f, 1f) * 255f);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    private static string PathToSvg(PathGeometry g, int precision)
    {
        string fmt = "0." + new string('#', precision);
        StringBuilder sb = new();
        for (int i = 0; i < g.Segments.Count; i++)
        {
            if (i > 0) { sb.Append(' '); }
            PathSegment seg = g.Segments[i];
            switch (seg.Command)
            {
                case PathCommand.MoveTo:
                    sb.Append("M ").Append(seg.X1.ToString(fmt, CultureInfo.InvariantCulture))
                      .Append(' ').Append(seg.Y1.ToString(fmt, CultureInfo.InvariantCulture));
                    break;
                case PathCommand.LineTo:
                    sb.Append("L ").Append(seg.X1.ToString(fmt, CultureInfo.InvariantCulture))
                      .Append(' ').Append(seg.Y1.ToString(fmt, CultureInfo.InvariantCulture));
                    break;
                case PathCommand.CubicTo:
                    sb.Append("C ")
                      .Append(seg.X1.ToString(fmt, CultureInfo.InvariantCulture)).Append(' ')
                      .Append(seg.Y1.ToString(fmt, CultureInfo.InvariantCulture)).Append(' ')
                      .Append(seg.X2.ToString(fmt, CultureInfo.InvariantCulture)).Append(' ')
                      .Append(seg.Y2.ToString(fmt, CultureInfo.InvariantCulture)).Append(' ')
                      .Append(seg.X3.ToString(fmt, CultureInfo.InvariantCulture)).Append(' ')
                      .Append(seg.Y3.ToString(fmt, CultureInfo.InvariantCulture));
                    break;
                case PathCommand.Close:
                    sb.Append('Z');
                    break;
                default: break;
            }
        }
        return sb.ToString();
    }

    private static string SrgbCss(PdfColor color)
    {
        (double r, double g, double b) = color.ToSrgb();
        return $"rgb({(int)Math.Round(r * 255)},{(int)Math.Round(g * 255)},{(int)Math.Round(b * 255)})";
    }

    private static string CssFamilyFor(string baseFont)
    {
        if (baseFont.StartsWith("Helvetica", StringComparison.OrdinalIgnoreCase)
            || baseFont.StartsWith("Arial", StringComparison.OrdinalIgnoreCase))
        {
            return "Helvetica, Arial, sans-serif";
        }
        if (baseFont.StartsWith("Times", StringComparison.OrdinalIgnoreCase))
        {
            return "Times, \"Times New Roman\", serif";
        }
        if (baseFont.StartsWith("Courier", StringComparison.OrdinalIgnoreCase))
        {
            return "Courier, \"Courier New\", monospace";
        }
        if (baseFont.Equals("Symbol", StringComparison.OrdinalIgnoreCase)) { return "Symbol"; }
        if (baseFont.Equals("ZapfDingbats", StringComparison.OrdinalIgnoreCase))
        {
            return "\"Zapf Dingbats\"";
        }
        return "sans-serif";
    }

    /// <summary>
    /// v2.1.2: derives <c>font-weight</c> and <c>font-style</c> hints from a
    /// PDF BaseFont name. Returning a non-null hint lets the SVG writer emit
    /// the matching CSS attribute on the text element.
    /// </summary>
    private static (string? fontWeight, string? fontStyle) ResolveStyleHints(FontStyle style) =>
        (style.IsBold ? "bold" : null, style.IsItalic ? "italic" : null);

    private static string BlendModeCss(PdfBlendMode mode) => mode switch
    {
        PdfBlendMode.Multiply => "multiply",
        PdfBlendMode.Screen => "screen",
        PdfBlendMode.Overlay => "overlay",
        PdfBlendMode.Darken => "darken",
        PdfBlendMode.Lighten => "lighten",
        PdfBlendMode.ColorDodge => "color-dodge",
        PdfBlendMode.ColorBurn => "color-burn",
        PdfBlendMode.HardLight => "hard-light",
        PdfBlendMode.SoftLight => "soft-light",
        PdfBlendMode.Difference => "difference",
        PdfBlendMode.Exclusion => "exclusion",
        _ => "normal",
    };
}
