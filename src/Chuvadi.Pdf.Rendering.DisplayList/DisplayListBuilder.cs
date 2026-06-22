// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8 (Graphics), §9 (Text), §9.4.3 (Text-showing operators)
// PHASE: Phase 2.1 — display-list intermediate
//        v2.1.2 — text-run word boundary correctness

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Content;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Fonts;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Rendering.Walking;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>
/// Builds a <see cref="PageDisplayList"/> by walking a page's content stream
/// and translating each PDF operator to a <see cref="RenderOp"/>.
/// </summary>
public static class DisplayListBuilder
{
    /// <summary>Builds a display list for the given page.</summary>
    public static PageDisplayList Build(PdfDocument document, int pageIndex)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (pageIndex < 0 || pageIndex >= document.PageCount)
        {
            throw new ArgumentOutOfRangeException(nameof(pageIndex));
        }
        PdfPage page = document.Pages[pageIndex];
        return new Builder(document).BuildPage(page);
    }

    private sealed class Builder : IContentOperatorSink
    {
        private readonly PdfDocument _doc;
        private readonly BuilderStateStack _stack = new();
        private readonly List<RenderOp> _ops = new();
        private readonly Dictionary<string, FontWidths> _widthsByKey = new();
        private readonly Dictionary<string, bool> _compositeByKey = new();
        // v2.1.2: collected for downstream renderers that embed font programs
        // (e.g. SvgRenderer emits CSS @font-face data URLs from these). Keys
        // match the resource-name used in TextOp.FontKey.
        private readonly Dictionary<string, PdfDictionary> _fontDictsByKey = new();

        // Follow-up item 2 (docs/v2.1.8-filter-array-and-followups.md):
        // resolved PdfFont cache, keyed by the resource-name used in
        // TextOp.FontKey. DecodeText is invoked per character code via
        // DecodeSingleCode; previously each call rebuilt the PdfFont — and
        // therefore re-parsed the ToUnicode CMap — for every glyph. Building
        // the PdfFont once per key and reusing it removes that per-character
        // re-parse. The resolution and diagnostic chain in DecodeText is
        // unchanged; only the PdfFont.FromDictionary build is memoised.
        private readonly Dictionary<string, PdfFont> _pdfFontByKey = new();

        // Resolved presentation style per font resource key (computed once in SetFont).
        private readonly Dictionary<string, FontStyle> _styleByKey = new();

        // v2.1.8: graceful-degradation events accumulated during build,
        // surfaced on PageDisplayList.Diagnostics. Deduplicated by
        // (kind, message) so a single condition that fires per-character
        // (e.g. font resolution failure on every glyph of a 31-char string)
        // emits one diagnostic, not 31.
        private readonly List<RenderingDiagnostic> _diagnostics = new();
        private readonly HashSet<(DiagnosticKind, string)> _diagnosticKeys = new();

        private PdfDictionary? _resources;
        private int _formDepth;
        private const int MaxFormDepth = 12;

        // Type 3 font state: parsed fonts per resource key, cached glyph
        // sub-display-lists, and a d1 colour-suppression flag.
        private readonly Dictionary<string, Type3Font?> _type3FontCache = new();
        private readonly Dictionary<(string Font, int Code, PdfColor Fill), PageDisplayList> _type3GlyphCache = new();
        private bool _suppressColorOps;

        // ── v2.1.2: gap-tracking for word-boundary space insertion ───────────
        //
        // After each text emit on the same line, we record the text-matrix
        // X position that EmitText left things at (post-advance) and whether
        // the run ended with a space character. Before the next emit, we
        // compare the recorded X position to the current text-matrix X
        // position. A gap larger than a fraction of the space-width tells
        // us the PDF intends a word break, and we insert a synthetic space
        // glyph at the start of the next run so the extracted text contains
        // the space character.
        //
        // Guards:
        //   - skip if no previous run on this line
        //   - skip if the previous run ENDED with a space — otherwise we'd
        //     produce double spaces (the "Current  Job" symptom)
        //   - skip if the next run STARTS with a space — same reason
        //
        // The line break operators (Td, TD, Tm, T*, ', ") and BT reset
        // this tracking so we never insert a space across a line boundary.
        private bool _hasPrevRunOnLine;
        private double _prevRunEndX;   // post-emit text-matrix E (X translation)
        private double _prevRunEndY;   // post-emit text-matrix F — sanity check
        private bool _prevRunEndedWithSpace;
        // Gap threshold: a gap exceeding 30% of a space-width is treated as a
        // word boundary. PDFBox uses similar values, determined by trial and
        // error against real PDFs. PDF.js uses 0.1–0.25 depending on context.
        // 0.3 is conservative — we insert fewer spurious spaces and miss a
        // few legitimate breaks. Better than the opposite.
        private const double GapToleranceFraction = 0.3;

        internal Builder(PdfDocument doc) { _doc = doc; }

        internal PageDisplayList BuildPage(PdfPage page)
        {
            _resources = page.Resources;
            byte[] content = ContentStreamLoader.Load(page.Contents, _doc.Objects);
            ContentStreamWalker.Walk(content, this);
            int rotation = 0;
            if (page.Dictionary.TryGetValue(PdfName.Intern("Rotate"), out PdfPrimitive? rv)
                && rv is PdfInteger ri) { rotation = ri.Value; }
            return new PageDisplayList(_ops, page.Width, page.Height, rotation, _fontDictsByKey, _diagnostics);
        }

        // ── IContentOperatorSink — graphics state ─────────────────────────

        /// <inheritdoc />
        public void SaveState()
        {
            BuilderState s = _stack.Current;
            _stack.Push();
            _ops.Add(new TransformOp { Push = true, Ctm = s.Ctm });
        }

        /// <inheritdoc />
        public void RestoreState()
        {
            _stack.Pop();
            _ops.Add(new TransformOp { Push = false, Ctm = _stack.Current.Ctm });
        }

        /// <inheritdoc />
        public void ConcatMatrix(double a, double b, double c, double d, double e, double f)
        {
            BuilderState s = _stack.Current;
            AffineMatrix m = new(a, b, c, d, e, f);
            s.Ctm = m.Multiply(s.Ctm);
        }

        /// <inheritdoc />
        public void SetLineWidth(double width)
        {
            _stack.Current.LineWidth = width;
        }

        /// <inheritdoc />
        public void SetLineCap(int cap)
        {
            _stack.Current.LineCap = (LineCap)cap;
        }

        /// <inheritdoc />
        public void SetLineJoin(int join)
        {
            _stack.Current.LineJoin = (LineJoin)join;
        }

        /// <inheritdoc />
        public void SetMiterLimit(double limit)
        {
            _stack.Current.MiterLimit = limit;
        }

        /// <inheritdoc />
        /// <remarks>
        /// Consolidation note: the pre-2.8 parser kept only the first dash
        /// length and treated later array entries as the phase, so
        /// multi-element dash patterns rendered wrong on this path. The
        /// shared walker parses the full array.
        /// </remarks>
        public void SetDashPattern(double[] dashes, double phase)
        {
            BuilderState s = _stack.Current;
            s.DashArray = dashes.Length > 0 ? dashes : null;
            s.DashPhase = phase;
        }

        // ── IContentOperatorSink — colour ────────────────────────────────

        /// <inheritdoc />
        public void SetFillGray(double gray)
        {
            if (_suppressColorOps) { return; }
            _stack.Current.FillColor = PdfColor.Gray(gray);
            _stack.Current.FillColorSpace = null;
        }

        /// <inheritdoc />
        public void SetStrokeGray(double gray)
        {
            if (_suppressColorOps) { return; }
            _stack.Current.StrokeColor = PdfColor.Gray(gray);
            _stack.Current.StrokeColorSpace = null;
        }

        /// <inheritdoc />
        public void SetFillRgb(double r, double g, double b)
        {
            if (_suppressColorOps) { return; }
            _stack.Current.FillColor = PdfColor.Rgb(r, g, b);
            _stack.Current.FillColorSpace = null;
        }

        /// <inheritdoc />
        public void SetStrokeRgb(double r, double g, double b)
        {
            if (_suppressColorOps) { return; }
            _stack.Current.StrokeColor = PdfColor.Rgb(r, g, b);
            _stack.Current.StrokeColorSpace = null;
        }

        /// <inheritdoc />
        public void SetFillCmyk(double c, double m, double y, double k)
        {
            if (_suppressColorOps) { return; }
            _stack.Current.FillColor = PdfColor.Cmyk(c, m, y, k);
            _stack.Current.FillColorSpace = null;
        }

        /// <inheritdoc />
        public void SetStrokeCmyk(double c, double m, double y, double k)
        {
            if (_suppressColorOps) { return; }
            _stack.Current.StrokeColor = PdfColor.Cmyk(c, m, y, k);
            _stack.Current.StrokeColorSpace = null;
        }

        /// <inheritdoc />
        public void SetColorSpace(string name, bool stroke)
        {
            if (_suppressColorOps) { return; }
            // cs / CS selects the active colour space. Device families resolve by
            // name; other names key into /Resources /ColorSpace (Separation,
            // DeviceN, Indexed, ICCBased, Lab, Cal*). A resolved space is kept so
            // sc / scn can convert through it; an unresolved or Pattern space
            // leaves the previous colour in place.
            ResolvedColorSpace? space = ResolveColorSpace(name);

            if (stroke)
            {
                _stack.Current.StrokeColorSpace = space;
            }
            else
            {
                _stack.Current.FillColorSpace = space;
            }
        }

        /// <inheritdoc />
        public void SetColorN(double[] components, bool hasName, bool stroke)
        {
            if (_suppressColorOps) { return; }
            // sc / scn / SC / SCN sets colour in the current space. A trailing
            // name (pattern) has no directly representable colour, so the current
            // colour is left unchanged.
            if (hasName)
            {
                return;
            }

            ResolvedColorSpace? space = stroke
                ? _stack.Current.StrokeColorSpace
                : _stack.Current.FillColorSpace;

            PdfColor color;
            if (space is not null && !space.IsPattern)
            {
                // Convert through the resolved space (Separation/DeviceN tint
                // transforms, Indexed lookup, ICCBased alternate, Lab, Cal*).
                double[] rgb = space.ToRgb(components);
                color = PdfColor.Rgb(rgb[0], rgb[1], rgb[2]);
            }
            else
            {
                // No explicit space: fall back to the operand count.
                switch (components.Length)
                {
                    case 1:
                        color = PdfColor.Gray(components[0]);
                        break;
                    case 3:
                        color = PdfColor.Rgb(components[0], components[1], components[2]);
                        break;
                    case 4:
                        color = PdfColor.Cmyk(
                            components[0], components[1], components[2], components[3]);
                        break;
                    default:
                        return;
                }
            }

            if (stroke)
            {
                _stack.Current.StrokeColor = color;
            }
            else
            {
                _stack.Current.FillColor = color;
            }
        }

        private ResolvedColorSpace? ResolveColorSpace(string name)
        {
            // Device and Pattern names resolve directly.
            ResolvedColorSpace? direct = ResolvedColorSpace.Parse(PdfName.Intern(name), _doc.Objects);
            if (direct is not null)
            {
                return direct;
            }

            // Otherwise the name keys into /Resources /ColorSpace.
            if (_resources is null
                || !_resources.TryGetValue(PdfName.Intern("ColorSpace"), out PdfPrimitive? csv)
                || csv is null)
            {
                return null;
            }

            PdfDictionary? spaces = _doc.Objects.ResolveAs<PdfDictionary>(csv);
            if (spaces is null
                || !spaces.TryGetValue(PdfName.Intern(name), out PdfPrimitive? entry)
                || entry is null)
            {
                return null;
            }

            return ResolvedColorSpace.Parse(entry, _doc.Objects);
        }

        // ── IContentOperatorSink — path construction ─────────────────────

        /// <inheritdoc />
        public void MoveTo(double x, double y)
        {
            BuilderState s = _stack.Current;
            (double mx, double my) = s.Ctm.Apply(x, y);
            s.AppendMoveTo(mx, my);
        }

        /// <inheritdoc />
        public void LineTo(double x, double y)
        {
            BuilderState s = _stack.Current;
            (double lx, double ly) = s.Ctm.Apply(x, y);
            s.AppendLineTo(lx, ly);
        }

        /// <inheritdoc />
        public void CurveTo(double x1, double y1, double x2, double y2, double x3, double y3)
        {
            BuilderState s = _stack.Current;
            (double cx1, double cy1) = s.Ctm.Apply(x1, y1);
            (double cx2, double cy2) = s.Ctm.Apply(x2, y2);
            (double cx3, double cy3) = s.Ctm.Apply(x3, y3);
            s.AppendCubicTo(cx1, cy1, cx2, cy2, cx3, cy3);
        }

        /// <inheritdoc />
        public void CurveToV(double x2, double y2, double x3, double y3)
        {
            BuilderState s = _stack.Current;
            (double vx2, double vy2) = s.Ctm.Apply(x2, y2);
            (double vx3, double vy3) = s.Ctm.Apply(x3, y3);
            s.AppendCubicTo(s.CurX, s.CurY, vx2, vy2, vx3, vy3);
        }

        /// <inheritdoc />
        public void CurveToY(double x1, double y1, double x3, double y3)
        {
            BuilderState s = _stack.Current;
            (double yx1, double yy1) = s.Ctm.Apply(x1, y1);
            (double yx3, double yy3) = s.Ctm.Apply(x3, y3);
            s.AppendCubicTo(yx1, yy1, yx3, yy3, yx3, yy3);
        }

        /// <inheritdoc />
        public void ClosePath()
        {
            _stack.Current.AppendClose();
        }

        /// <inheritdoc />
        public void AppendRectangle(double x, double y, double w, double h)
        {
            BuilderState s = _stack.Current;
            (double p0x, double p0y) = s.Ctm.Apply(x, y);
            (double p1x, double p1y) = s.Ctm.Apply(x + w, y);
            (double p2x, double p2y) = s.Ctm.Apply(x + w, y + h);
            (double p3x, double p3y) = s.Ctm.Apply(x, y + h);
            s.AppendMoveTo(p0x, p0y);
            s.AppendLineTo(p1x, p1y);
            s.AppendLineTo(p2x, p2y);
            s.AppendLineTo(p3x, p3y);
            s.AppendClose();
        }

        // ── IContentOperatorSink — path painting and clipping ────────────

        /// <inheritdoc />
        public void FillPath(bool evenOdd)
        {
            EmitPath(_stack.Current, PaintMode.Fill, evenOdd ? FillRule.EvenOdd : FillRule.NonZero);
        }

        /// <inheritdoc />
        public void StrokePath(bool closeFirst)
        {
            BuilderState s = _stack.Current;
            if (closeFirst)
            {
                s.AppendClose();
            }
            EmitPath(s, PaintMode.Stroke, FillRule.NonZero);
        }

        /// <inheritdoc />
        public void FillAndStrokePath(bool evenOdd, bool closeFirst)
        {
            BuilderState s = _stack.Current;
            if (closeFirst)
            {
                s.AppendClose();
            }
            EmitPath(s, PaintMode.FillAndStroke, evenOdd ? FillRule.EvenOdd : FillRule.NonZero);
        }

        /// <inheritdoc />
        public void EndPath()
        {
            _stack.Current.ResetPath();
        }

        /// <inheritdoc />
        public void SetClip(bool evenOdd)
        {
            BuilderState s = _stack.Current;
            if (s.HasCurrentPath)
            {
                _ops.Add(new ClipOp
                {
                    Geometry = s.CurrentPath,
                    FillRule = evenOdd ? FillRule.EvenOdd : FillRule.NonZero,
                });
            }
        }

        // ── IContentOperatorSink — text ──────────────────────────────────

        /// <inheritdoc />
        public void BeginText()
        {
            BuilderState s = _stack.Current;
            s.TextMatrix = AffineMatrix.Identity;
            s.TextLineMatrix = AffineMatrix.Identity;
            ResetGapTracking();
        }

        /// <inheritdoc />
        public void EndText()
        {
            ResetGapTracking();
        }

        /// <inheritdoc />
        public void SetFont(string name, double size)
        {
            BuilderState s = _stack.Current;
            s.FontKey = name;
            s.FontSize = size;
            s.BaseFont = ResolveBaseFont(name);
            s.Style = ResolveFontStyle(name, s.BaseFont);
            s.Type3 = GetType3Font(name);
        }

        // Resolves and caches the Type 3 font for a resource name, or null.
        private Type3Font? GetType3Font(string name)
        {
            if (string.IsNullOrEmpty(name) || _resources is null)
            {
                return null;
            }

            if (_type3FontCache.TryGetValue(name, out Type3Font? cached))
            {
                return cached;
            }

            Type3Font? font = null;
            if (_resources.TryGetValue(PdfName.Intern("Font"), out PdfPrimitive? fontsRef)
                && _doc.Objects.ResolveAs<PdfDictionary>(fontsRef ?? PdfNull.Value) is PdfDictionary fonts
                && fonts.TryGetValue(PdfName.Intern(name), out PdfPrimitive? fontRef)
                && _doc.Objects.ResolveAs<PdfDictionary>(fontRef ?? PdfNull.Value) is PdfDictionary fontDict)
            {
                font = Type3Font.FromDictionary(fontDict, _doc.Objects);
            }

            _type3FontCache[name] = font;
            return font;
        }

        /// <inheritdoc />
        public void SetGlyphWidth(double wx, double wy)
        {
            // d0 — coloured glyph; colour comes from the glyph itself.
        }

        /// <inheritdoc />
        public void SetGlyphWidthAndBBox(
            double wx, double wy, double llx, double lly, double urx, double ury)
        {
            // d1 — uncoloured glyph: ignore colour operators; paint with text colour.
            _suppressColorOps = true;
        }

        // Emits a run of single-byte codes in a Type 3 font. Each glyph's CharProc
        // is built once (cached) in glyph space, then placed via a Type3UseOp whose
        // composition maps glyph space to page space: FontMatrix · textScale ·
        // TextMatrix · CTM.
        private void EmitTextType3(byte[] bytes, BuilderState s, Type3Font type3)
        {
            if (s.FontSize <= 0) { return; }

            bool emit = s.RenderingMode != TextRenderingMode.Invisible;
            double th = s.HorizontalScaling / 100.0;

            AffineMatrix fontMatrix = new AffineMatrix(
                type3.FontMatrix[0], type3.FontMatrix[1], type3.FontMatrix[2],
                type3.FontMatrix[3], type3.FontMatrix[4], type3.FontMatrix[5]);

            foreach (byte b in bytes)
            {
                int code = b;
                double advance = 0.0;

                if (type3.TryGetGlyph(code, out Type3Glyph glyph))
                {
                    if (emit && glyph.Content.Length > 0)
                    {
                        PageDisplayList glyphList = GetOrBuildType3GlyphList(type3, code, glyph, s.FillColor);
                        if (glyphList.Count > 0)
                        {
                            AffineMatrix textScale = new AffineMatrix(
                                s.FontSize * th, 0, 0, s.FontSize, 0, s.TextRise);
                            AffineMatrix composition = fontMatrix
                                .Multiply(textScale)
                                .Multiply(s.TextMatrix)
                                .Multiply(s.Ctm);
                            _ops.Add(new Type3UseOp(glyphList, composition)
                            {
                                BlendMode = s.BlendMode,
                                SoftMask = s.SoftMask,
                            });
                        }
                    }

                    advance = glyph.Width * type3.FontMatrix[0] * s.FontSize;
                }

                double extra = s.CharSpacing + (code == 32 ? s.WordSpacing : 0.0);
                double tx = (advance + extra) * th;
                s.TextMatrix = new AffineMatrix(1, 0, 0, 1, tx, 0).Multiply(s.TextMatrix);
            }
        }

        // Builds (or returns a cached) glyph-space sub-display-list for one Type 3
        // glyph. Keyed by font, code, and fill colour (which an uncoloured d1 glyph
        // bakes in). Blend mode and soft mask are applied by the Type3UseOp, not the
        // cached list, so they are not part of the key.
        private PageDisplayList GetOrBuildType3GlyphList(
            Type3Font type3, int code, Type3Glyph glyph, PdfColor fill)
        {
            (string, int, PdfColor) key = (_stack.Current.FontKey ?? string.Empty, code, fill);
            if (_type3GlyphCache.TryGetValue(key, out PageDisplayList? cached))
            {
                return cached;
            }

            Builder sub = new Builder(_doc);
            sub._resources = type3.Resources ?? _resources;
            sub._stack.Current.FillColor = fill;

            ContentStreamWalker.Walk(glyph.Content, sub);

            PageDisplayList glyphList = new PageDisplayList(sub._ops, 0, 0, 0);
            _type3GlyphCache[key] = glyphList;
            return glyphList;
        }

        /// <inheritdoc />
        public void TextMove(double tx, double ty)
        {
            BuilderState s = _stack.Current;
            AffineMatrix t = new(1, 0, 0, 1, tx, ty);
            s.TextLineMatrix = t.Multiply(s.TextLineMatrix);
            s.TextMatrix = s.TextLineMatrix;
            // Line-changing op: don't track gap across this boundary.
            ResetGapTracking();
        }

        /// <inheritdoc />
        public void TextMoveWithLeading(double tx, double ty)
        {
            BuilderState s = _stack.Current;
            s.Leading = -ty;
            AffineMatrix t = new(1, 0, 0, 1, tx, ty);
            s.TextLineMatrix = t.Multiply(s.TextLineMatrix);
            s.TextMatrix = s.TextLineMatrix;
            ResetGapTracking();
        }

        /// <inheritdoc />
        public void SetTextMatrix(double a, double b, double c, double d, double e, double f)
        {
            BuilderState s = _stack.Current;
            AffineMatrix tm = new(a, b, c, d, e, f);
            s.TextMatrix = tm;
            s.TextLineMatrix = tm;
            ResetGapTracking();
        }

        /// <inheritdoc />
        public void TextNextLine()
        {
            BuilderState s = _stack.Current;
            AffineMatrix t = new(1, 0, 0, 1, 0, -s.Leading);
            s.TextLineMatrix = t.Multiply(s.TextLineMatrix);
            s.TextMatrix = s.TextLineMatrix;
            ResetGapTracking();
        }

        /// <inheritdoc />
        public void SetCharSpacing(double spacing)
        {
            _stack.Current.CharSpacing = spacing;
        }

        /// <inheritdoc />
        public void SetWordSpacing(double spacing)
        {
            _stack.Current.WordSpacing = spacing;
        }

        /// <inheritdoc />
        public void SetHorizontalScaling(double scale)
        {
            _stack.Current.HorizontalScaling = scale;
        }

        /// <inheritdoc />
        public void SetLeading(double leading)
        {
            _stack.Current.Leading = leading;
        }

        /// <inheritdoc />
        public void SetTextRenderingMode(int mode)
        {
            _stack.Current.RenderingMode = (TextRenderingMode)mode;
        }

        /// <inheritdoc />
        public void SetTextRise(double rise)
        {
            _stack.Current.TextRise = rise;
        }

        /// <inheritdoc />
        public void ShowText(byte[] text)
        {
            EmitText(text, _stack.Current);
        }

        /// <inheritdoc />
        public void MoveNextLineShowText(byte[] text)
        {
            TextNextLine();
            EmitText(text, _stack.Current);
        }

        /// <inheritdoc />
        public void SetSpacingMoveNextLineShowText(double wordSpacing, double charSpacing, byte[] text)
        {
            BuilderState s = _stack.Current;
            s.WordSpacing = wordSpacing;
            s.CharSpacing = charSpacing;
            TextNextLine();
            EmitText(text, s);
        }

        /// <inheritdoc />
        public void ShowTextArray(IReadOnlyList<TextArrayElement> elements)
        {
            EmitTJ(elements, _stack.Current);
        }

        // ── IContentOperatorSink — XObjects ────────────────────────────

        /// <inheritdoc />
        public void InvokeXObject(string name)
        {
            EmitXObject(name, _stack.Current);
        }

        // ── IContentOperatorSink — ExtGState ───────────────────────────

        /// <inheritdoc />
        public void ApplyExtGState(string name)
        {
            if (_resources is null) { return; }
            if (!_resources.TryGetValue(PdfName.Intern("ExtGState"), out PdfPrimitive? egv)) { return; }
            PdfDictionary? extGStates = _doc.Objects.ResolveAs<PdfDictionary>(egv);
            if (extGStates is null) { return; }
            if (!extGStates.TryGetValue(PdfName.Intern(name), out PdfPrimitive? gsRef)) { return; }
            if (_doc.Objects.Resolve(gsRef) is not PdfDictionary gs) { return; }

            BuilderState s = _stack.Current;

            // /ca — constant non-stroking (fill) alpha; /CA — constant stroking
            // alpha. PDF 32000-1:2008 §8.4.5, Table 58. Values outside 0..1 are
            // clamped. Other ExtGState entries are not interpreted here.
            if (TryReadAlpha(gs, "ca", out double fillAlpha)) { s.FillAlpha = fillAlpha; }
            if (TryReadAlpha(gs, "CA", out double strokeAlpha)) { s.StrokeAlpha = strokeAlpha; }

            // /BM — blend mode (PDF §11.3.5). A name, or an array of names whose
            // first supported entry wins. Unsupported/non-separable names map to
            // Normal (source-over).
            if (TryReadBlendMode(gs, out PdfBlendMode blendMode))
            {
                s.BlendMode = blendMode;
            }

            // /SMask — soft mask (PDF §11.6.5.2). /None clears; a dictionary
            // installs a luminosity or alpha masking group.
            ReadSoftMask(gs);
        }

        private void ReadSoftMask(PdfDictionary gs)
        {
            if (!gs.TryGetValue(PdfName.Intern("SMask"), out PdfPrimitive? smv))
            {
                return;
            }

            PdfPrimitive sm = _doc.Objects.Resolve(smv);
            if (sm is PdfName nm)
            {
                if (nm.Value == "None")
                {
                    _stack.Current.SoftMask = null;
                }

                return;
            }

            if (sm is not PdfDictionary smDict)
            {
                return;
            }

            if (!smDict.TryGetValue(PdfName.Intern("G"), out PdfPrimitive? gp)
                || _doc.Objects.Resolve(gp) is not PdfStream groupStream)
            {
                return;
            }

            bool isLuminosity = smDict.GetName(PdfName.Intern("S"))?.Value != "Alpha";

            double backdrop = 0.0;
            PdfArray? bc = smDict.GetArray(PdfName.Intern("BC"));
            if (bc is not null && bc.Count > 0)
            {
                backdrop = NumberOf(bc[0]);
            }

            PageDisplayList group = BuildSoftMaskGroup(groupStream, out AffineMatrix groupMatrix);
            _stack.Current.SoftMask = new SoftMaskInfo(
                group, groupMatrix.Multiply(_stack.Current.Ctm), isLuminosity, backdrop);
        }

        private PageDisplayList BuildSoftMaskGroup(
            PdfStream groupStream, out AffineMatrix groupMatrix)
        {
            groupMatrix = AffineMatrix.Identity;
            if (groupStream.Dictionary.TryGetValue(PdfName.Intern("Matrix"), out PdfPrimitive? mp)
                && _doc.Objects.ResolveAs<PdfArray>(mp) is PdfArray arr && arr.Count >= 6)
            {
                groupMatrix = new AffineMatrix(
                    NumberOf(arr[0]), NumberOf(arr[1]),
                    NumberOf(arr[2]), NumberOf(arr[3]),
                    NumberOf(arr[4]), NumberOf(arr[5]));
            }

            PdfDictionary? groupResources = _resources;
            if (groupStream.Dictionary.TryGetValue(PdfName.Intern("Resources"), out PdfPrimitive? rp)
                && _doc.Objects.ResolveAs<PdfDictionary>(rp) is PdfDictionary r)
            {
                groupResources = r;
            }

            Builder sub = new Builder(_doc);
            byte[] content;
            try
            {
                content = ContentStreamLoader.Decode(groupStream);
            }
            catch
            {
                return new PageDisplayList(sub._ops, 0, 0, 0);
            }

            if (content.Length > 0)
            {
                sub._resources = groupResources;
                ContentStreamWalker.Walk(content, sub);
            }

            return new PageDisplayList(sub._ops, 0, 0, 0);
        }

        private bool TryReadBlendMode(PdfDictionary extGState, out PdfBlendMode mode)
        {
            mode = PdfBlendMode.Normal;
            if (!extGState.TryGetValue(PdfName.Intern("BM"), out PdfPrimitive? bmValue))
            {
                return false;
            }

            PdfPrimitive resolved = _doc.Objects.Resolve(bmValue);
            if (resolved is PdfName single)
            {
                mode = BlendModes.FromName(single.Value);
                return true;
            }

            if (resolved is PdfArray array)
            {
                for (int i = 0; i < array.Count; i++)
                {
                    if (_doc.Objects.Resolve(array[i]) is PdfName candidate
                        && BlendModes.TryFromName(candidate.Value, out PdfBlendMode m))
                    {
                        mode = m;
                        return true;
                    }
                }

                // No supported name in the array — fall back to Normal.
                mode = PdfBlendMode.Normal;
                return true;
            }

            return false;
        }

        // ── IContentOperatorSink — Shading ─────────────────────────────────

        /// <inheritdoc />
        public void PaintShading(string name)
        {
            if (_resources is null) { return; }
            if (!_resources.TryGetValue(PdfName.Intern("Shading"), out PdfPrimitive? shv)) { return; }
            PdfDictionary? shadings = _doc.Objects.ResolveAs<PdfDictionary>(shv);
            if (shadings is null) { return; }
            if (!shadings.TryGetValue(PdfName.Intern(name), out PdfPrimitive? shadingRef)) { return; }

            PdfShading shading;
            try
            {
                shading = PdfShading.Parse(shadingRef, _doc.Objects);
            }
            catch (ContentException)
            {
                // Unsupported shading type (e.g. mesh shadings 4-7) — skip the
                // paint rather than failing the whole page.
                return;
            }

            // The CTM at the sh operator maps shading space to page space. Path
            // coordinates in this builder are already baked to page space, so the
            // gradient geometry must be too.
            AffineMatrix ctm = _stack.Current.Ctm;
            (double ox, double oy) = ctm.Apply(0.0, 0.0);
            (double ux, double uy) = ctm.Apply(1.0, 0.0);
            double scale = Math.Sqrt(((ux - ox) * (ux - ox)) + ((uy - oy) * (uy - oy)));

            double[] coords = shading.Coords;
            (double x0, double y0) = ctm.Apply(coords[0], coords[1]);

            const int sampleCount = 16;
            List<ShadingStop> stops = new(sampleCount + 1);
            for (int i = 0; i <= sampleCount; i++)
            {
                double t = (double)i / sampleCount;
                (double r, double g, double b) = shading.EvaluateRgb(t);
                stops.Add(new ShadingStop(t, r, g, b));
            }

            if (shading.IsRadial)
            {
                (double rx1, double ry1) = ctm.Apply(coords[3], coords[4]);
                _ops.Add(new ShadingOp
                {
                    BlendMode = _stack.Current.BlendMode,
                    SoftMask = _stack.Current.SoftMask,
                    IsRadial = true,
                    X0 = x0,
                    Y0 = y0,
                    R0 = coords[2] * scale,
                    X1 = rx1,
                    Y1 = ry1,
                    R1 = coords[5] * scale,
                    ExtendStart = shading.ExtendStart,
                    ExtendEnd = shading.ExtendEnd,
                    Stops = stops,
                });
            }
            else
            {
                (double ax1, double ay1) = ctm.Apply(coords[2], coords[3]);
                _ops.Add(new ShadingOp
                {
                    BlendMode = _stack.Current.BlendMode,
                    SoftMask = _stack.Current.SoftMask,
                    IsRadial = false,
                    X0 = x0,
                    Y0 = y0,
                    X1 = ax1,
                    Y1 = ay1,
                    ExtendStart = shading.ExtendStart,
                    ExtendEnd = shading.ExtendEnd,
                    Stops = stops,
                });
            }
        }

        private static bool TryReadAlpha(PdfDictionary extGState, string key, out double value)
        {
            value = 1.0;
            if (!extGState.TryGetValue(PdfName.Intern(key), out PdfPrimitive? prim)) { return false; }

            double raw;
            if (prim is PdfReal r) { raw = r.Value; }
            else if (prim is PdfInteger i) { raw = i.Value; }
            else { return false; }

            value = raw < 0.0 ? 0.0 : (raw > 1.0 ? 1.0 : raw);
            return true;
        }

        private void EmitPath(BuilderState s, PaintMode mode, FillRule rule)
        {
            if (!s.HasCurrentPath) { return; }
            StrokeStyle? stroke = mode != PaintMode.Fill ? new StrokeStyle(
                LineWidth: s.LineWidth * Math.Max(Math.Abs(s.Ctm.A), Math.Abs(s.Ctm.D)),
                Cap: s.LineCap,
                Join: s.LineJoin,
                MiterLimit: s.MiterLimit,
                DashArray: s.DashArray,
                DashPhase: s.DashPhase) : null;
            _ops.Add(new PathOp
            {
                BlendMode = _stack.Current.BlendMode,
                SoftMask = _stack.Current.SoftMask,
                Geometry = s.CurrentPath,
                Mode = mode,
                FillRule = rule,
                FillColor = s.FillColor,
                StrokeColor = s.StrokeColor,
                Stroke = stroke,
                FillOpacity = s.FillAlpha,
                StrokeOpacity = s.StrokeAlpha,
            });
            s.ResetPath();
        }

        private void ResetGapTracking()
        {
            _hasPrevRunOnLine = false;
            _prevRunEndedWithSpace = false;
        }

        /// <summary>
        /// v2.1.2 helper (Bug 2): returns the synthetic glyph for a leading
        /// space to prepend to a run when the gap from the previous run on
        /// the same line exceeds the word-boundary threshold. Returns null
        /// when no space should be inserted (no previous run, line changed,
        /// gap below threshold, or the previous run already ended in space).
        /// </summary>
        private DisplayListGlyph? MaybeBuildLeadingSpace(BuilderState s, FontWidths widths)
        {
            if (!_hasPrevRunOnLine) { return null; }
            // v2.1.2 (issue B): if the previous run on this line ended with
            // a space character, the word boundary is already represented
            // in the extracted text. Inserting another space here produces
            // the double-space symptom ("Current  Job").
            if (_prevRunEndedWithSpace) { return null; }

            double curX = s.TextMatrix.E;
            double curY = s.TextMatrix.F;

            // If the line changed (Y differs significantly), gap tracking would
            // have been reset by Td/TD/Tm/T*/'. Belt-and-braces: also check Y.
            if (Math.Abs(curY - _prevRunEndY) > 0.01) { return null; }

            double gap = curX - _prevRunEndX;
            if (gap <= 0) { return null; }

            // Compute space-width in user-space points. Use the font's space
            // glyph width if available; otherwise fall back to 0.25 × FontSize.
            double spaceWidth1000 = widths.GetWidth(0x20);
            double spaceWidth = spaceWidth1000 > 0
                ? (spaceWidth1000 / 1000.0) * s.FontSize
                : 0.25 * s.FontSize;
            spaceWidth *= s.HorizontalScaling / 100.0;

            if (gap < spaceWidth * GapToleranceFraction) { return null; }

            // X=0, Advance=0: the synthetic space adds the character for
            // text extraction without affecting downstream glyph positioning.
            return new DisplayListGlyph(
                GlyphId: 0x20,
                Unicode: " ",
                X: 0,
                Y: 0,
                Advance: 0);
        }

        private void EmitText(byte[] bytes, BuilderState s)
        {
            if (s.FontKey is null) { return; }
            if (bytes.Length == 0) { return; }

            if (s.Type3 is Type3Font type3)
            {
                EmitTextType3(bytes, s, type3);
                return;
            }

            FontWidths widths = GetWidths(s.FontKey);
            bool composite = _compositeByKey.GetValueOrDefault(s.FontKey, false);

            // Decode all glyphs first so we can inspect the first character
            // before deciding whether to prepend a synthetic leading space.
            List<DisplayListGlyph> glyphs = new();
            double xAdvance = 0;
            int codeStep = composite ? 2 : 1;

            for (int i = 0; i + codeStep <= bytes.Length; i += codeStep)
            {
                int code = composite
                    ? ((bytes[i] << 8) | bytes[i + 1])
                    : bytes[i];

                string unicode = DecodeSingleCode(bytes, i, codeStep, s.FontKey);

                double rawWidth = widths.GetWidth(code);   // font units (1000ths em)
                double advance = (rawWidth / 1000.0) * s.FontSize
                                 + s.CharSpacing
                                 + (unicode == " " ? s.WordSpacing : 0.0);
                advance *= s.HorizontalScaling / 100.0;

                glyphs.Add(new DisplayListGlyph(
                    GlyphId: code,
                    Unicode: unicode,
                    X: xAdvance,
                    Y: 0,
                    Advance: advance));

                xAdvance += advance;
            }

            if (glyphs.Count == 0) { return; }

            // v2.1.2 (Bug 2): if the previous run on this line ended far
            // enough away to constitute a word break, AND the new run does
            // not already begin with a literal space character, prepend a
            // synthetic space so the extracted text has the word boundary.
            // The "already has space" guard prevents double-spacing when
            // the PDF supplied an explicit space at the start of the run.
            bool firstIsAlreadySpace = glyphs[0].Unicode == " ";
            if (!firstIsAlreadySpace)
            {
                DisplayListGlyph? leading = MaybeBuildLeadingSpace(s, widths);
                if (leading is not null)
                {
                    glyphs.Insert(0, leading.Value);
                }
            }

            AffineMatrix combined = s.TextMatrix.Multiply(s.Ctm);
            _ops.Add(new TextOp
            {
                BlendMode = _stack.Current.BlendMode,
                SoftMask = _stack.Current.SoftMask,
                FontKey = s.FontKey,
                BaseFont = s.BaseFont ?? "Helvetica",
                FontSize = s.FontSize,
                Glyphs = glyphs,
                Transform = combined,
                RenderingMode = s.RenderingMode,
                FillColor = s.FillColor,
                StrokeColor = s.StrokeColor,
                Style = s.Style,
                FillOpacity = s.FillAlpha,
                StrokeOpacity = s.StrokeAlpha,
            });

            // Advance text matrix by the total advance of this run.
            AffineMatrix step = new(1, 0, 0, 1, xAdvance, 0);
            s.TextMatrix = step.Multiply(s.TextMatrix);

            // v2.1.2 (Bug 2): record the end position so we can detect a gap
            // before the next emit on the same line. Also record whether this
            // run ended with a space — used to suppress double-space insertion
            // ("Current  Job") on the next run.
            _hasPrevRunOnLine = true;
            _prevRunEndX = s.TextMatrix.E;
            _prevRunEndY = s.TextMatrix.F;
            _prevRunEndedWithSpace = glyphs[glyphs.Count - 1].Unicode == " ";
        }

        /// <summary>
        /// v2.1.3 — TJ-array handler with sub-space-width kerning fold.
        /// </summary>
        /// <remarks>
        /// <para>
        /// PDF §9.4.3: a TJ array alternates between string literals (which
        /// show glyphs) and numeric kerns (which translate the text matrix
        /// horizontally). Word emits TJ arrays where one logical word becomes
        /// many tiny string literals separated by sub-point typographic kerns
        /// like <c>-8</c> or <c>-6</c>. Treating every literal as its own
        /// <see cref="TextOp"/> means downstream renderers see word fragments
        /// at independent anchor positions, and when the SVG renderer trusts
        /// an embedded font's hmtx instead of PDF <c>/Widths</c>, the
        /// fragment anchors and the font's glyph extents disagree by a
        /// fraction of an em — producing visible intra-word gaps.
        /// </para>
        /// <para>
        /// The fold buffers consecutive same-state string literals into a
        /// single <see cref="TextOp"/>. Small kerns (below
        /// <see cref="GapToleranceFraction"/> of the space width) are absorbed
        /// into the running cursor position so per-glyph X offsets within
        /// the fold include them; the renderer is then free to honour or
        /// ignore those offsets depending on whether the font is embedded.
        /// Large kerns flush the fold and start a fresh <see cref="TextOp"/>
        /// after the kern, so real word spaces remain encoded as TextOp
        /// boundaries.
        /// </para>
        /// </remarks>
        private void EmitTJ(IReadOnlyList<TextArrayElement> elements, BuilderState s)
        {
            if (s.FontKey is null) { return; }

            FontWidths widths = GetWidths(s.FontKey);
            bool composite = _compositeByKey.GetValueOrDefault(s.FontKey, false);
            int codeStep = composite ? 2 : 1;

            // Break-the-fold threshold: a kern whose magnitude exceeds this
            // many user-space points starts a new TextOp after the kern.
            // Mirrors the word-boundary heuristic in MaybeBuildLeadingSpace.
            double spaceWidth1000 = widths.GetWidth(0x20);
            double spaceWidthPoints = spaceWidth1000 > 0
                ? (spaceWidth1000 / 1000.0) * s.FontSize
                : 0.25 * s.FontSize;
            spaceWidthPoints *= s.HorizontalScaling / 100.0;
            double breakThreshold = spaceWidthPoints * GapToleranceFraction;

            // Pending fold state.
            List<DisplayListGlyph> pending = new();
            AffineMatrix pendingTransform = AffineMatrix.Identity;
            double cursorX = 0;

            for (int idx = 0; idx < elements.Count; idx++)
            {
                TextArrayElement element = elements[idx];
                if (element.IsText)
                {
                    byte[] bytes = element.Text!;
                    if (bytes.Length == 0) { continue; }

                    bool startingNewFold = pending.Count == 0;
                    if (startingNewFold)
                    {
                        pendingTransform = s.TextMatrix.Multiply(s.Ctm);
                        cursorX = 0;
                    }

                    // Decode glyphs and accumulate into the pending fold.
                    List<DisplayListGlyph> decoded = new();
                    for (int i = 0; i + codeStep <= bytes.Length; i += codeStep)
                    {
                        int code = composite
                            ? ((bytes[i] << 8) | bytes[i + 1])
                            : bytes[i];
                        string unicode = DecodeSingleCode(bytes, i, codeStep, s.FontKey);
                        double rawWidth = widths.GetWidth(code);
                        double advance = (rawWidth / 1000.0) * s.FontSize
                                         + s.CharSpacing
                                         + (unicode == " " ? s.WordSpacing : 0.0);
                        advance *= s.HorizontalScaling / 100.0;

                        decoded.Add(new DisplayListGlyph(
                            GlyphId: code,
                            Unicode: unicode,
                            X: cursorX,
                            Y: 0,
                            Advance: advance));
                        cursorX += advance;
                    }

                    // On a fold START, optionally prepend a synthetic leading
                    // space for text-extraction word boundaries. The same
                    // logic as EmitText's leading-space prepend, but applied
                    // exactly once per fold rather than once per literal.
                    if (startingNewFold && decoded.Count > 0)
                    {
                        bool firstIsAlreadySpace = decoded[0].Unicode == " ";
                        if (!firstIsAlreadySpace)
                        {
                            DisplayListGlyph? leading = MaybeBuildLeadingSpace(s, widths);
                            if (leading is not null)
                            {
                                pending.Add(leading.Value);
                            }
                        }
                    }

                    pending.AddRange(decoded);
                }
                else
                {
                    double n = element.Adjustment;
                    // Negative n shifts text forward (right) in LTR per §9.4.3.
                    double tx = -(n / 1000.0) * s.FontSize * (s.HorizontalScaling / 100.0);

                    // v2.1.3 — lookahead: if this kern is immediately followed
                    // by a string literal whose first character is " ", treat
                    // the kern as small regardless of magnitude. Word emits
                    // a large positive shift before the space glyph to widen
                    // inter-word gaps; combined with the explicit space char
                    // that follows, the visible word break is doubled. By
                    // absorbing the kern, the embedded font's own space-glyph
                    // advance alone provides the visible word break. Without
                    // this rule the kern would flush the fold and produce
                    // either a missing space (when the snap shrink fires and
                    // over-corrects) or a too-wide space (when it doesn't).
                    bool nextIsLeadingSpace = false;
                    if (idx + 1 < elements.Count && elements[idx + 1].IsText)
                    {
                        byte[] nextBytes = elements[idx + 1].Text!;
                        if (nextBytes.Length >= codeStep)
                        {
                            string firstUnicode = DecodeSingleCode(
                                nextBytes, 0, codeStep, s.FontKey);
                            nextIsLeadingSpace = firstUnicode == " ";
                        }
                    }

                    if (pending.Count == 0)
                    {
                        // No fold in progress — apply kern directly to text
                        // matrix, matching the pre-fold behaviour for leading
                        // or post-flush kerns.
                        AffineMatrix step = new(1, 0, 0, 1, tx, 0);
                        s.TextMatrix = step.Multiply(s.TextMatrix);
                    }
                    else if (Math.Abs(tx) >= breakThreshold && !nextIsLeadingSpace)
                    {
                        // Large kern that's NOT Word's kern-before-space
                        // idiom: flush, apply the kern, start a new fold.
                        FlushFold(pending, pendingTransform, cursorX, s);
                        pending = new List<DisplayListGlyph>();
                        AffineMatrix step = new(1, 0, 0, 1, tx, 0);
                        s.TextMatrix = step.Multiply(s.TextMatrix);
                        cursorX = 0;
                    }
                    else
                    {
                        // Small kern, or kern-before-space: absorb into
                        // the cursor so the following glyph(s) sit at the
                        // kerned position within the same fold.
                        cursorX += tx;
                    }
                }
            }

            // Final flush at end of TJ. If pending is empty, any trailing
            // kerns have already been applied directly to the text matrix.
            if (pending.Count > 0)
            {
                FlushFold(pending, pendingTransform, cursorX, s);
            }
        }

        /// <summary>
        /// v2.1.3 — emit a folded TextOp and update gap-tracking state.
        /// Advances <see cref="BuilderState.TextMatrix"/> by
        /// <paramref name="cursorX"/> (the total run width including any
        /// absorbed small kerns).
        /// </summary>
        private void FlushFold(List<DisplayListGlyph> pending,
            AffineMatrix transform, double cursorX, BuilderState s)
        {
            if (pending.Count == 0) { return; }

            _ops.Add(new TextOp
            {
                BlendMode = _stack.Current.BlendMode,
                SoftMask = _stack.Current.SoftMask,
                FontKey = s.FontKey!,
                BaseFont = s.BaseFont ?? "Helvetica",
                FontSize = s.FontSize,
                Glyphs = pending,
                Transform = transform,
                RenderingMode = s.RenderingMode,
                FillColor = s.FillColor,
                StrokeColor = s.StrokeColor,
                Style = s.Style,
                FillOpacity = s.FillAlpha,
                StrokeOpacity = s.StrokeAlpha,
            });

            AffineMatrix step = new(1, 0, 0, 1, cursorX, 0);
            s.TextMatrix = step.Multiply(s.TextMatrix);

            _hasPrevRunOnLine = true;
            _prevRunEndX = s.TextMatrix.E;
            _prevRunEndY = s.TextMatrix.F;
            _prevRunEndedWithSpace = pending[pending.Count - 1].Unicode == " ";
        }

        private FontWidths GetWidths(string fontKey)
        {
            if (_widthsByKey.TryGetValue(fontKey, out FontWidths? cached)) { return cached; }
            PdfDictionary? fontDict = ResolveFontDict(fontKey);
            if (fontDict is null)
            {
                FontWidths fallback = FontWidthsFallback();
                _widthsByKey[fontKey] = fallback;
                _compositeByKey[fontKey] = false;
                return fallback;
            }
            FontWidths fw = FontWidths.FromDictionary(fontDict, _doc.Objects);
            // Enable Standard 14 fallback if BaseFont is one of them — many PDFs
            // (and Chuvadi's Authoring module) omit /Widths for Standard 14 fonts.
            string? baseFont = ResolveBaseFont(fontKey);
            if (baseFont is not null) { fw.EnableStandard14Fallback(baseFont); }
            _widthsByKey[fontKey] = fw;
            _compositeByKey[fontKey] = fw.IsComposite;
            return fw;
        }

        private PdfDictionary? ResolveFontDict(string fontKey)
        {
            if (_fontDictsByKey.TryGetValue(fontKey, out PdfDictionary? cached))
            {
                return cached;
            }
            if (_resources is null) { return null; }
            if (!_resources.TryGetValue(PdfName.Intern("Font"), out PdfPrimitive? fonts))
            {
                return null;
            }
            PdfDictionary? fd = _doc.Objects.ResolveAs<PdfDictionary>(fonts);
            if (fd is null) { return null; }
            if (!fd.TryGetValue(PdfName.Intern(fontKey), out PdfPrimitive? fv)) { return null; }
            PdfDictionary? resolved = _doc.Objects.ResolveAs<PdfDictionary>(fv);
            if (resolved is not null)
            {
                _fontDictsByKey[fontKey] = resolved;
            }
            return resolved;
        }

        // Resolve (and cache) the presentation style for a font resource key,
        // combining the base name with the FontDescriptor /Flags, /ItalicAngle,
        // and /StemV when present.
        private FontStyle ResolveFontStyle(string fontKey, string? baseFont)
        {
            if (_styleByKey.TryGetValue(fontKey, out FontStyle cached))
            {
                return cached;
            }

            int? flags = null;
            double? italicAngle = null;
            int? stemV = null;

            PdfDictionary? font = ResolveFontDict(fontKey);
            PdfDictionary? descriptor = font is null ? null : ResolveFontDescriptor(font);
            if (descriptor is not null)
            {
                if (descriptor.TryGetValue(PdfName.Intern("Flags"), out PdfPrimitive? fv)
                    && _doc.Objects.Resolve(fv) is PdfInteger fi)
                {
                    flags = fi.Value;
                }

                if (descriptor.TryGetValue(PdfName.Intern("ItalicAngle"), out PdfPrimitive? iv))
                {
                    italicAngle = AsDouble(_doc.Objects.Resolve(iv));
                }

                if (descriptor.TryGetValue(PdfName.Intern("StemV"), out PdfPrimitive? sv)
                    && AsDouble(_doc.Objects.Resolve(sv)) is double stem)
                {
                    stemV = (int)stem;
                }
            }

            FontStyle style = FontStyleClassifier.Classify(baseFont ?? string.Empty, flags, italicAngle, stemV);
            _styleByKey[fontKey] = style;
            return style;
        }

        private PdfDictionary? ResolveFontDescriptor(PdfDictionary font)
        {
            // Type0 fonts carry the descriptor on their descendant CIDFont.
            if (font.TryGetValue(PdfName.Intern("Subtype"), out PdfPrimitive? st)
                && st is PdfName stn && stn.Value == "Type0"
                && font.TryGetValue(PdfName.Intern("DescendantFonts"), out PdfPrimitive? dfv)
                && _doc.Objects.ResolveAs<PdfArray>(dfv) is PdfArray descendants
                && descendants.Count > 0
                && _doc.Objects.ResolveAs<PdfDictionary>(descendants[0]) is PdfDictionary cidFont)
            {
                font = cidFont;
            }

            return font.TryGetValue(PdfName.Intern("FontDescriptor"), out PdfPrimitive? fdv)
                ? _doc.Objects.ResolveAs<PdfDictionary>(fdv)
                : null;
        }

        private static double? AsDouble(PdfPrimitive? primitive) => primitive switch
        {
            PdfInteger i => i.Value,
            PdfReal r => r.Value,
            _ => null,
        };

        private static FontWidths FontWidthsFallback()
        {
            // Build a stub FontWidths via reflection-free constructor proxy:
            // a synthetic font dict with no /Widths gives the default 500 width.
            PdfDictionary empty = new();
            return FontWidths.FromDictionary(empty, NullResolver.Instance);
        }

        private string DecodeSingleCode(byte[] bytes, int offset, int codeStep, string fontKey)
        {
            // Decode just the bytes [offset, offset+codeStep) through PdfFont.
            byte[] slice = new byte[codeStep];
            System.Array.Copy(bytes, offset, slice, 0, codeStep);
            return DecodeText(slice, fontKey);
        }

        // Follow-up item 2: build the PdfFont for a resource key once and reuse
        // it on subsequent character decodes. The five resolution guards and
        // their diagnostics remain inline in DecodeText; only the expensive
        // PdfFont.FromDictionary (ToUnicode CMap parse) is memoised here. A
        // throw propagates intentionally — DecodeText's try/catch around the
        // call records the diagnostic and falls back to Latin-1.
        private PdfFont GetOrBuildPdfFont(string fontKey, PdfDictionary font)
        {
            if (_pdfFontByKey.TryGetValue(fontKey, out PdfFont? cached))
            {
                return cached;
            }
            PdfFont pf = PdfFont.FromDictionary(font, _doc.Objects);
            _pdfFontByKey[fontKey] = pf;
            return pf;
        }

        private string DecodeText(byte[] bytes, string fontKey)
        {
            if (_resources is null)
            {
                AddDiagnostic(DiagnosticKind.DecodeFallback,
                    $"Font '{fontKey}' could not be resolved: page has no /Resources. Falling back to Latin-1 decoding.");
                return TryLatin(bytes);
            }
            if (!_resources.TryGetValue(PdfName.Intern("Font"), out PdfPrimitive? fonts))
            {
                AddDiagnostic(DiagnosticKind.DecodeFallback,
                    $"Font '{fontKey}' could not be resolved: /Resources has no /Font entry. Falling back to Latin-1 decoding.");
                return TryLatin(bytes);
            }
            PdfDictionary? fd = _doc.Objects.ResolveAs<PdfDictionary>(fonts);
            if (fd is null)
            {
                AddDiagnostic(DiagnosticKind.DecodeFallback,
                    $"Font '{fontKey}' could not be resolved: /Resources/Font reference did not resolve to a dictionary. Falling back to Latin-1 decoding.");
                return TryLatin(bytes);
            }
            if (!fd.TryGetValue(PdfName.Intern(fontKey), out PdfPrimitive? fv))
            {
                AddDiagnostic(DiagnosticKind.DecodeFallback,
                    $"Font '{fontKey}' could not be resolved: /Font sub-dictionary has no entry for this key. Falling back to Latin-1 decoding.");
                return TryLatin(bytes);
            }
            PdfDictionary? font = _doc.Objects.ResolveAs<PdfDictionary>(fv);
            if (font is null)
            {
                AddDiagnostic(DiagnosticKind.DecodeFallback,
                    $"Font '{fontKey}' could not be resolved: the font reference did not resolve to a dictionary. Falling back to Latin-1 decoding.");
                return TryLatin(bytes);
            }
            try
            {
                PdfFont pf = GetOrBuildPdfFont(fontKey, font);
                return pf.Decode(bytes);
            }
            catch (Exception ex)
            {
                AddDiagnostic(DiagnosticKind.DecodeFallback,
                    $"Font '{fontKey}' could not be resolved: PdfFont.FromDictionary threw {ex.GetType().Name}: {ex.Message}. Falling back to Latin-1 decoding.");
                return TryLatin(bytes);
            }
        }

        // v2.1.8: record a graceful-degradation event for downstream consumers.
        // Dedupes by (kind, message) so a per-character DecodeText fallback
        // emits one diagnostic per page, not one per glyph.
        private void AddDiagnostic(DiagnosticKind kind, string message)
        {
            if (_diagnosticKeys.Add((kind, message)))
            {
                _diagnostics.Add(new RenderingDiagnostic(kind, message));
            }
        }

        private static string TryLatin(byte[] bytes)
            => System.Text.Encoding.Latin1.GetString(bytes);

        private string? ResolveBaseFont(string fontKey)
        {
            if (_resources is null) { return null; }
            if (!_resources.TryGetValue(PdfName.Intern("Font"), out PdfPrimitive? fonts))
            {
                return null;
            }
            PdfDictionary? fd = _doc.Objects.ResolveAs<PdfDictionary>(fonts);
            if (fd is null) { return null; }
            if (!fd.TryGetValue(PdfName.Intern(fontKey), out PdfPrimitive? fv)) { return null; }
            PdfDictionary? font = _doc.Objects.ResolveAs<PdfDictionary>(fv);
            if (font is null) { return null; }
            if (font.TryGetValue(PdfName.Intern("BaseFont"), out PdfPrimitive? bv)
                && bv is PdfName bn)
            {
                string s = bn.Value;
                int plus = s.IndexOf('+');
                if (plus >= 0 && plus < s.Length - 1) { return s[(plus + 1)..]; }
                return s;
            }
            return null;
        }

        // Renders a form XObject by walking its content stream through this
        // builder with the form's /Matrix concatenated onto the CTM and its
        // own /Resources in scope (falling back to the page's when absent).
        // A depth guard stops cyclic references from recursing without bound.
        private void EmitFormXObject(PdfStream formStream)
        {
            if (_formDepth >= MaxFormDepth) { return; }

            byte[] formContent;
            try
            {
                formContent = ContentStreamLoader.Decode(formStream);
            }
            catch
            {
                return;
            }

            if (formContent.Length == 0) { return; }

            double a = 1.0, b = 0.0, c = 0.0, d = 1.0, e = 0.0, f = 0.0;
            if (formStream.Dictionary.TryGetValue(PdfName.Intern("Matrix"), out PdfPrimitive? mp)
                && _doc.Objects.ResolveAs<PdfArray>(mp) is PdfArray arr && arr.Count >= 6)
            {
                a = NumberOf(arr[0]);
                b = NumberOf(arr[1]);
                c = NumberOf(arr[2]);
                d = NumberOf(arr[3]);
                e = NumberOf(arr[4]);
                f = NumberOf(arr[5]);
            }

            PdfDictionary? formResources = _resources;
            if (formStream.Dictionary.TryGetValue(PdfName.Intern("Resources"), out PdfPrimitive? rp)
                && _doc.Objects.ResolveAs<PdfDictionary>(rp) is PdfDictionary resolved)
            {
                formResources = resolved;
            }

            PdfDictionary? savedResources = _resources;
            SaveState();
            ConcatMatrix(a, b, c, d, e, f);
            _resources = formResources;
            _formDepth++;
            ContentStreamWalker.Walk(formContent, this);
            _formDepth--;
            _resources = savedResources;
            RestoreState();
        }

        private void EmitXObject(string name, BuilderState s)
        {
            if (_resources is null) { return; }
            if (!_resources.TryGetValue(PdfName.Intern("XObject"), out PdfPrimitive? xv))
            {
                return;
            }
            PdfDictionary? xobjects = _doc.Objects.ResolveAs<PdfDictionary>(xv);
            if (xobjects is null) { return; }
            if (!xobjects.TryGetValue(PdfName.Intern(name), out PdfPrimitive? imgRef)) { return; }
            if (_doc.Objects.Resolve(imgRef) is not PdfStream stream) { return; }
            if (!stream.Dictionary.TryGetValue(PdfName.Intern("Subtype"), out PdfPrimitive? sub)
                || sub is not PdfName subName)
            {
                return;
            }

            if (subName.Value == "Form")
            {
                EmitFormXObject(stream);
                return;
            }

            if (subName.Value != "Image")
            {
                return;
            }

            int width = IntOf(stream.Dictionary, "Width", 0);
            int height = IntOf(stream.Dictionary, "Height", 0);
            int bpc = IntOf(stream.Dictionary, "BitsPerComponent", 8);
            if (width <= 0 || height <= 0) { return; }

            string? filterName = ExtractFilterName(stream.Dictionary);
            ImageFormat format;
            byte[] pixelData;
            PdfColorSpace cs = ExtractColorSpace(stream.Dictionary);

            if (filterName == "DCTDecode")
            {
                format = ImageFormat.Jpeg;
                pixelData = stream.RawBytes;
            }
            else
            {
                format = ImageFormat.Raw;
                try { pixelData = ContentStreamLoader.Decode(stream); }
                catch { return; }
            }

            byte[]? softMaskAlpha = null;
            int softMaskWidth = 0;
            int softMaskHeight = 0;

            // /SMask: a DeviceGray image whose samples are the base image's alpha
            // channel. Only applied when the base is a decodable raw image (a JPEG
            // base would need decoding before alpha can be attached).
            if (format == ImageFormat.Raw
                && stream.Dictionary.TryGetValue(PdfName.Intern("SMask"), out PdfPrimitive? smRef)
                && _doc.Objects.Resolve(smRef) is PdfStream smStream)
            {
                int smWidth = IntOf(smStream.Dictionary, "Width", 0);
                int smHeight = IntOf(smStream.Dictionary, "Height", 0);
                int smBpc = IntOf(smStream.Dictionary, "BitsPerComponent", 8);

                if (smWidth > 0 && smHeight > 0 && smBpc == 8)
                {
                    try
                    {
                        byte[] smPixels = ContentStreamLoader.Decode(smStream);
                        if (smPixels.Length >= smWidth * smHeight)
                        {
                            if (IsDecodeInverted(smStream.Dictionary))
                            {
                                for (int i = 0; i < smPixels.Length; i++)
                                {
                                    smPixels[i] = (byte)(255 - smPixels[i]);
                                }
                            }

                            softMaskAlpha = smPixels;
                            softMaskWidth = smWidth;
                            softMaskHeight = smHeight;

                            // /Matte: the base image's colour samples are
                            // pre-blended (pre-multiplied) against this matte
                            // colour (PDF 32000-1:2008 §11.6.5.3). Recover the
                            // true colour c = (c' - m)/alpha + m before the
                            // straight-alpha PNG is built, or colours render
                            // wrong (washed/shifted toward the matte).
                            double[]? matte = ReadMatte(smStream.Dictionary);
                            if (matte is not null)
                            {
                                UnpremultiplyMatte(
                                    pixelData, width, height,
                                    softMaskAlpha, smWidth, smHeight, matte);
                            }
                        }
                    }
                    catch
                    {
                        softMaskAlpha = null;
                    }
                }
            }

            _ops.Add(new ImageOp
            {
                BlendMode = _stack.Current.BlendMode,
                SoftMask = _stack.Current.SoftMask,
                PixelData = pixelData,
                Format = format,
                Width = width,
                Height = height,
                BitsPerComponent = bpc,
                ColorSpace = cs,
                SoftMaskAlpha = softMaskAlpha,
                SoftMaskWidth = softMaskWidth,
                SoftMaskHeight = softMaskHeight,
                Alpha = s.FillAlpha,
                Transform = s.Ctm,
            });
        }

        // Reads a soft mask's /Matte colour (the premultiply background) as
        // components in [0, 1], or null when absent.
        private static double[]? ReadMatte(PdfDictionary smDict)
        {
            if (!smDict.TryGetValue(PdfName.Intern("Matte"), out PdfPrimitive? value)
                || value is not PdfArray array || array.Count == 0)
            {
                return null;
            }

            double[] matte = new double[array.Count];
            for (int i = 0; i < array.Count; i++)
            {
                matte[i] = NumberOf(array[i]);
            }
            return matte;
        }

        // Un-premultiplies matte-blended colour samples in place: recovers
        // c = (c' - m)/alpha + m per channel, using the soft-mask alpha
        // (nearest-sampled when its resolution differs from the base image).
        private static void UnpremultiplyMatte(
            byte[] pixelData, int width, int height,
            byte[] alpha, int alphaWidth, int alphaHeight, double[] matte)
        {
            if (width <= 0 || height <= 0 || alphaWidth <= 0 || alphaHeight <= 0)
            {
                return;
            }

            long pixels = (long)width * height;
            int components = (int)(pixelData.Length / pixels);
            if (components <= 0 || matte.Length < components)
            {
                return;
            }

            for (int y = 0; y < height; y++)
            {
                int ay = (int)((long)y * alphaHeight / height);
                for (int x = 0; x < width; x++)
                {
                    int ax = (int)((long)x * alphaWidth / width);
                    int aIndex = (ay * alphaWidth) + ax;
                    if (aIndex < 0 || aIndex >= alpha.Length)
                    {
                        continue;
                    }

                    double a = alpha[aIndex] / 255.0;
                    if (a <= 0.0)
                    {
                        continue;
                    }

                    int baseIndex = (int)(((long)y * width + x) * components);
                    for (int c = 0; c < components; c++)
                    {
                        int idx = baseIndex + c;
                        if (idx >= pixelData.Length)
                        {
                            break;
                        }

                        double cprime = pixelData[idx] / 255.0;
                        double m = matte[c];
                        double recovered = ((cprime - m) / a) + m;
                        if (recovered < 0.0) { recovered = 0.0; }
                        if (recovered > 1.0) { recovered = 1.0; }
                        pixelData[idx] = (byte)Math.Round(recovered * 255.0);
                    }
                }
            }
        }

        // True when /Decode is [1 0] for a single-component image (inverts samples).
        private static bool IsDecodeInverted(PdfDictionary dict)
        {
            if (dict.TryGetValue(PdfName.Intern("Decode"), out PdfPrimitive? d)
                && d is PdfArray arr && arr.Count >= 2
                && arr[0] is PdfReal or PdfInteger && arr[1] is PdfReal or PdfInteger)
            {
                double d0 = NumberOf(arr[0]);
                double d1 = NumberOf(arr[1]);
                return d0 == 1.0 && d1 == 0.0;
            }
            return false;
        }

        private static double NumberOf(PdfPrimitive p) => p switch
        {
            PdfInteger i => i.Value,
            PdfReal r => r.Value,
            _ => 0.0,
        };

        private static int IntOf(PdfDictionary d, string key, int fallback)
        {
            if (d.TryGetValue(PdfName.Intern(key), out PdfPrimitive? v) && v is PdfInteger i)
            {
                return i.Value;
            }
            return fallback;
        }

        private static string? ExtractFilterName(PdfDictionary d)
        {
            if (!d.TryGetValue(PdfName.Intern("Filter"), out PdfPrimitive? f)) { return null; }
            return f switch
            {
                PdfName n => n.Value,
                PdfArray arr when arr.Count > 0 && arr[0] is PdfName n2 => n2.Value,
                _ => null,
            };
        }

        private static PdfColorSpace ExtractColorSpace(PdfDictionary d)
        {
            if (!d.TryGetValue(PdfName.Intern("ColorSpace"), out PdfPrimitive? cs)) { return PdfColorSpace.DeviceRgb; }
            if (cs is PdfName n)
            {
                return n.Value switch
                {
                    "DeviceGray" => PdfColorSpace.DeviceGray,
                    "DeviceRGB" => PdfColorSpace.DeviceRgb,
                    "DeviceCMYK" => PdfColorSpace.DeviceCmyk,
                    _ => PdfColorSpace.DeviceRgb,
                };
            }
            return PdfColorSpace.DeviceRgb;
        }


    }
}

