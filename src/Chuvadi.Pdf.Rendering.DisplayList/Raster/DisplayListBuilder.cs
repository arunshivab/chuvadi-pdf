// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.8 — Content streams; §8 — Graphics; §9 — Text
// PHASE: v2.0.0 R1 D3c-2 — DisplayList builder

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Filters;
using Chuvadi.Pdf.Fonts.Rendering;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Images;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Rendering.Walking;
using Path = Chuvadi.Pdf.Graphics.Path;

namespace Chuvadi.Pdf.Rendering.Raster;

/// <summary>
/// Builds a <see cref="PageDisplayList"/> from a <see cref="PdfPage"/> by
/// interpreting the page's content stream.
/// </summary>
/// <remarks>
/// <para>
/// The builder is renderer-neutral. It walks the PDF operator stream once,
/// maintaining graphics-state and path-construction state, and emits an
/// immutable sequence of <see cref="RenderOp"/> values into a
/// <see cref="PageDisplayList"/>. Every op carries the CTM-baked geometry
/// plus a snapshot of the active clip paths, so downstream consumers
/// (pixel rasterizer, SVG writer, accessibility walker) do not need to
/// track CTM or clip-stack state.
/// </para>
/// <para>
/// Operators supported in v2.0.0 R1: q Q cm; w J j M d (state); g G rg RG
/// k K sc SC scn SCN cs CS (colour); m l c v y h re (path construction);
/// S s f F f* B B* b b* n (path painting); W W* (clipping); BT ET Tf Tc
/// Tw Tz TL Ts Tr Td TD Tm T* Tj TJ ' " (text); Do (XObject - Image and
/// Form); BMC BDC EMC MP DP BX EX (marked content / compatibility - no-op).
/// </para>
/// <para>
/// Operators deferred to v2.1+: sh (shading), Pattern colorspaces (sc/scn
/// with /Pattern), BI/ID/EI (inline images), ExtGState soft masks.
/// </para>
/// </remarks>
public static class DisplayListBuilder
{
    /// <summary>
    /// Builds a display list for the page's content stream.
    /// </summary>
    /// <param name="page">The PDF page to interpret.</param>
    /// <param name="objects">The object store for resolving indirect references.</param>
    /// <param name="hintingScale">Device scale (DPI/72) for grid-fitting; 0 disables hinting (raster path only).</param>
    /// <param name="lightHinting">When true, grid-fit the Y axis only (lighter, grayscale-friendly).</param>
    /// <param name="autohintFallback">When true (the default), glyphs of fonts with no hinting programs are grid-fitted by the geometric autohinter.</param>
    /// <returns>
    /// An immutable display list. Empty if the page has no content
    /// stream. CTM-baked geometry; per-op clip snapshots. Page rotation
    /// is not applied here; that is a consumer concern.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="page"/> or <paramref name="objects"/> is null.
    /// </exception>
    public static PageDisplayList Build(PdfPage page, PdfObjectStore objects, double hintingScale = 0.0, bool lightHinting = false, bool autohintFallback = true)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(objects);

        Worker worker = new Worker(objects, hintingScale, lightHinting, autohintFallback);
        byte[] content = ContentStreamLoader.Load(page.Contents, objects);
        return worker.BuildFromBytes(content, page.Resources, page.Width, page.Height);
    }

    /// <summary>
    /// Builds a display list directly from raw content-stream bytes.
    /// </summary>
    /// <remarks>
    /// This overload bypasses <see cref="PdfPage"/> entirely and is useful
    /// for: (a) testing the operator interpreter in isolation, (b) rendering
    /// arbitrary content streams (e.g. Form XObject contents in custom
    /// pipelines), (c) tooling that constructs content streams in memory.
    ///
    /// The caller is responsible for supplying a resources dictionary that
    /// resolves any /Font and /XObject references used by the content
    /// stream. Pass null when the stream uses no resources.
    /// </remarks>
    /// <param name="content">The raw (decoded) content-stream bytes.</param>
    /// <param name="resources">
    /// The resources dictionary for font and XObject lookup. May be null.
    /// </param>
    /// <param name="objects">The object store for resolving indirect references.</param>
    /// <param name="pageWidth">The MediaBox width for the resulting display list.</param>
    /// <param name="pageHeight">The MediaBox height for the resulting display list.</param>
    /// <param name="hintingScale">Device scale (DPI/72) for grid-fitting; 0 disables hinting (raster path only).</param>
    /// <param name="lightHinting">When true, grid-fit the Y axis only (lighter, grayscale-friendly).</param>
    /// <param name="autohintFallback">When true (the default), glyphs of fonts with no hinting programs are grid-fitted by the geometric autohinter.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="content"/> or <paramref name="objects"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="pageWidth"/> or <paramref name="pageHeight"/> is negative.
    /// </exception>
    public static PageDisplayList Build(
        byte[] content,
        PdfDictionary? resources,
        PdfObjectStore objects,
        double pageWidth,
        double pageHeight,
        double hintingScale = 0.0,
        bool lightHinting = false,
        bool autohintFallback = true)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(objects);

        Worker worker = new Worker(objects, hintingScale, lightHinting, autohintFallback);
        return worker.BuildFromBytes(content, resources, pageWidth, pageHeight);
    }

    /// <summary>
    /// Internal worker that owns mutable interpretation state. A new
    /// instance is created per Build call so the public API is stateless.
    /// </summary>
    private sealed class Worker : IContentOperatorSink
    {
        private readonly PdfObjectStore _objects;
        private readonly double _hintingScale;
        private readonly bool _lightHinting;
        private readonly bool _autohintFallback;
        private readonly Dictionary<string, FontRenderer?> _fontCache;
        private readonly Dictionary<string, Chuvadi.Pdf.Fonts.PdfFont?> _pdfFontCache;
        private readonly Dictionary<string, RenderableFont?> _std14FontCache;
        private readonly Dictionary<string, bool> _symbolicCache;
        private readonly Dictionary<string, Type1FontRenderer?> _type1Cache;
        private readonly Dictionary<string, (int First, double[] Widths)?> _widthsCache;

        // Render-op accumulator
        private readonly List<RenderOp> _ops;

        // Graphics-state stack (q/Q)
        private BuilderGraphicsState _state;
        private readonly Stack<BuilderGraphicsState> _stateStack;

        // Path construction (pre-CTM, user-space coords)
        private Path _currentPath;

        // Text state (NOT in q/Q stack — these reset on BT)
        private Transform _textMatrix;
        private Transform _textLineMatrix;

        // Deferred clip (W or W* observed; applies AFTER the next painting op)
        private bool _clipPending;
        private FillRule _clipRule;

        // Resources of the stream being walked (page or form XObject)
        private PdfDictionary? _resources;

        public Worker(PdfObjectStore objects, double hintingScale = 0.0, bool lightHinting = false, bool autohintFallback = true)
        {
            _objects = objects;
            _hintingScale = hintingScale;
            _lightHinting = lightHinting;
            _autohintFallback = autohintFallback;
            _fontCache = new Dictionary<string, FontRenderer?>();
            _pdfFontCache = new Dictionary<string, Chuvadi.Pdf.Fonts.PdfFont?>();
            _std14FontCache = new Dictionary<string, RenderableFont?>();
            _symbolicCache = new Dictionary<string, bool>();
            _type1Cache = new Dictionary<string, Type1FontRenderer?>();
            _widthsCache = new Dictionary<string, (int, double[])?>();
            _ops = new List<RenderOp>();
            _state = new BuilderGraphicsState();
            _stateStack = new Stack<BuilderGraphicsState>();
            _currentPath = new Path();
            _textMatrix = Transform.Identity;
            _textLineMatrix = Transform.Identity;
            _clipPending = false;
            _clipRule = FillRule.NonZeroWinding;
        }

        public PageDisplayList BuildFromBytes(
            byte[] content,
            PdfDictionary? resources,
            double pageWidth,
            double pageHeight)
        {
            _resources = resources;
            ContentStreamWalker.Walk(content, this);

            return new PageDisplayList(_ops, pageWidth, pageHeight);
        }

        // ── Graphics state operators ──────────────────────────────────────

        /// <inheritdoc />
        public void SaveState()
        {
            _stateStack.Push(_state.Clone());
        }

        /// <inheritdoc />
        public void RestoreState()
        {
            if (_stateStack.Count > 0)
            {
                _state = _stateStack.Pop();
            }
        }

        /// <inheritdoc />
        public void ConcatMatrix(double a, double b, double c, double d, double e, double f)
        {
            Transform local = new Transform(a, b, c, d, e, f);

            // PDF row-vector convention: local cm pre-multiplies the CTM
            _state.Ctm = local.Multiply(_state.Ctm);
        }

        /// <inheritdoc />
        public void SetLineWidth(double width)
        {
            _state.LineWidth = width;
        }

        /// <inheritdoc />
        public void SetLineCap(int cap)
        {
            _state.LineCap = (LineCap)cap;
        }

        /// <inheritdoc />
        public void SetLineJoin(int join)
        {
            _state.LineJoin = (LineJoin)join;
        }

        /// <inheritdoc />
        public void SetMiterLimit(double limit)
        {
            _state.MiterLimit = limit;
        }

        /// <inheritdoc />
        public void SetDashPattern(double[] dashes, double phase)
        {
            _state.DashPattern = dashes;
            _state.DashOffset = phase;
        }

        // ── Colour operators ──────────────────────────────────────────────

        /// <inheritdoc />
        public void SetFillGray(double gray)
        {
            _state.FillColor = ColorF.FromGray((float)gray);
            _state.FillValid = true;
        }

        /// <inheritdoc />
        public void SetStrokeGray(double gray)
        {
            _state.StrokeColor = ColorF.FromGray((float)gray);
            _state.StrokeValid = true;
        }

        /// <inheritdoc />
        public void SetFillRgb(double r, double g, double b)
        {
            _state.FillColor = ColorF.FromRgb((float)r, (float)g, (float)b);
            _state.FillValid = true;
        }

        /// <inheritdoc />
        public void SetStrokeRgb(double r, double g, double b)
        {
            _state.StrokeColor = ColorF.FromRgb((float)r, (float)g, (float)b);
            _state.StrokeValid = true;
        }

        /// <inheritdoc />
        public void SetFillCmyk(double c, double m, double y, double k)
        {
            _state.FillColor = ColorF.FromCmyk((float)c, (float)m, (float)y, (float)k);
            _state.FillValid = true;
        }

        /// <inheritdoc />
        public void SetStrokeCmyk(double c, double m, double y, double k)
        {
            _state.StrokeColor = ColorF.FromCmyk((float)c, (float)m, (float)y, (float)k);
            _state.StrokeValid = true;
        }

        /// <inheritdoc />
        public void SetColorSpace(string name, bool stroke)
        {
            // cs / CS sets the active colour space. We track validity:
            // device colour spaces remain valid; Pattern marks invalid so
            // subsequent paints get suppressed until a representable
            // colour is set via rg/g/k.
            bool isDevice = name == "DeviceGray" || name == "DeviceRGB" || name == "DeviceCMYK"
                         || name == "G" || name == "RGB" || name == "CMYK";

            if (stroke)
            {
                _state.StrokeValid = isDevice;
            }
            else
            {
                _state.FillValid = isDevice;
            }
        }

        /// <inheritdoc />
        public void SetColorN(double[] components, bool hasName, bool stroke)
        {
            // sc / scn / SC / SCN — set colour in current colour space.
            // We support 1, 3, or 4 numeric operands (DeviceGray/RGB/CMYK).
            // A trailing name operand (Pattern) suppresses validity.
            if (hasName)
            {
                if (stroke) { _state.StrokeValid = false; } else { _state.FillValid = false; }
                return;
            }

            ColorF c;

            switch (components.Length)
            {
                case 1:
                    c = ColorF.FromGray((float)components[0]);
                    break;
                case 3:
                    c = ColorF.FromRgb(
                        (float)components[0],
                        (float)components[1],
                        (float)components[2]);
                    break;
                case 4:
                    c = ColorF.FromCmyk(
                        (float)components[0],
                        (float)components[1],
                        (float)components[2],
                        (float)components[3]);
                    break;
                default:
                    return;
            }

            if (stroke)
            {
                _state.StrokeColor = c;
                _state.StrokeValid = true;
            }
            else
            {
                _state.FillColor = c;
                _state.FillValid = true;
            }
        }

        // ── Path construction ─────────────────────────────────────────────

        /// <inheritdoc />
        public void MoveTo(double x, double y)
        {
            _currentPath.MoveTo(x, y);
        }

        /// <inheritdoc />
        public void LineTo(double x, double y)
        {
            // LineTo on an empty path is illegal in the Path API (it
            // requires a current point). Defend against malformed streams.
            if (_currentPath.IsEmpty)
            {
                _currentPath.MoveTo(x, y);
                return;
            }

            _currentPath.LineTo(x, y);
        }

        /// <inheritdoc />
        public void ClosePath()
        {
            _currentPath.ClosePath();
        }

        /// <inheritdoc />
        public void CurveTo(double x1, double y1, double x2, double y2, double x3, double y3)
        {
            if (_currentPath.IsEmpty)
            {
                return; // Malformed — c requires a current point
            }

            _currentPath.CubicBezierTo(
                new PointF(x1, y1),
                new PointF(x2, y2),
                new PointF(x3, y3));
        }

        /// <inheritdoc />
        public void CurveToV(double x2, double y2, double x3, double y3)
        {
            // v x2 y2 x3 y3 — Bezier with initial point as first control
            if (_currentPath.IsEmpty)
            {
                return;
            }

            PointF current;

            try
            {
                current = _currentPath.CurrentPoint;
            }
            catch (InvalidOperationException)
            {
                return;
            }

            _currentPath.CubicBezierTo(
                current,
                new PointF(x2, y2),
                new PointF(x3, y3));
        }

        /// <inheritdoc />
        public void CurveToY(double x1, double y1, double x3, double y3)
        {
            // y x1 y1 x3 y3 — Bezier with final point as second control
            if (_currentPath.IsEmpty)
            {
                return;
            }

            PointF endPt = new PointF(x3, y3);
            _currentPath.CubicBezierTo(
                new PointF(x1, y1),
                endPt,
                endPt);
        }

        /// <inheritdoc />
        public void AppendRectangle(double x, double y, double w, double h)
        {
            _currentPath.MoveTo(x, y);
            _currentPath.LineTo(x + w, y);
            _currentPath.LineTo(x + w, y + h);
            _currentPath.LineTo(x, y + h);
            _currentPath.ClosePath();
        }

        // ── Path painting ─────────────────────────────────────────────────

        /// <inheritdoc />
        public void FillPath(bool evenOdd)
        {
            OpFill(evenOdd ? FillRule.EvenOdd : FillRule.NonZeroWinding);
        }

        /// <inheritdoc />
        public void FillAndStrokePath(bool evenOdd, bool closeFirst)
        {
            OpFillStroke(evenOdd ? FillRule.EvenOdd : FillRule.NonZeroWinding, closeFirst);
        }

        /// <inheritdoc />
        public void SetClip(bool evenOdd)
        {
            _clipPending = true;
            _clipRule = evenOdd ? FillRule.EvenOdd : FillRule.NonZeroWinding;
        }

        // ── IContentOperatorSink — ExtGState ───────────────────────────

        /// <inheritdoc />
        public void ApplyExtGState(string name)
        {
            if (_resources is null) { return; }
            if (!_resources.TryGetValue(PdfName.Intern("ExtGState"), out PdfPrimitive? egv)) { return; }
            PdfDictionary? extGStates = _objects.ResolveAs<PdfDictionary>(egv);
            if (extGStates is null) { return; }
            if (!extGStates.TryGetValue(PdfName.Intern(name), out PdfPrimitive? gsRef)) { return; }
            if (_objects.Resolve(gsRef) is not PdfDictionary gs) { return; }

            // /ca = constant non-stroking (fill) alpha, /CA = constant stroking
            // alpha (PDF 32000-1:2008 §8.4.5, Table 58). Out-of-range values clamp.
            if (TryReadAlpha(gs, "ca", out double fillAlpha)) { _state.FillAlpha = fillAlpha; }
            if (TryReadAlpha(gs, "CA", out double strokeAlpha)) { _state.StrokeAlpha = strokeAlpha; }
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

        // Paint colours with the constant ExtGState alpha (/ca, /CA) folded in.
        private ColorF EffectiveFill => ApplyAlpha(_state.FillColor, _state.FillAlpha);

        private ColorF EffectiveStroke => ApplyAlpha(_state.StrokeColor, _state.StrokeAlpha);

        private static ColorF ApplyAlpha(ColorF color, double alpha)
        {
            if (alpha >= 1.0) { return color; }
            double clamped = alpha < 0.0 ? 0.0 : alpha;
            ColorF rgb = color.ToRgb();
            return ColorF.FromRgb(rgb.R, rgb.G, rgb.B, rgb.Alpha * (float)clamped);
        }

        private void OpFill(FillRule rule)
        {
            if (_state.FillValid && !_currentPath.IsEmpty)
            {
                Path transformed = TransformPath(_currentPath, _state.Ctm);
                _ops.Add(new FillPathOp(transformed, EffectiveFill, rule, SnapshotClips()));
            }

            ApplyDeferredClip();
            _currentPath = new Path();
        }

        /// <inheritdoc />
        public void StrokePath(bool closeFirst)
        {
            if (closeFirst && !_currentPath.IsEmpty)
            {
                _currentPath.ClosePath();
            }

            if (_state.StrokeValid && !_currentPath.IsEmpty)
            {
                Path transformed = TransformPath(_currentPath, _state.Ctm);
                StrokeStyle style = BuildStrokeStyle();
                _ops.Add(new StrokePathOp(transformed, style, SnapshotClips()));
            }

            ApplyDeferredClip();
            _currentPath = new Path();
        }

        private void OpFillStroke(FillRule rule, bool closeFirst)
        {
            if (closeFirst && !_currentPath.IsEmpty)
            {
                _currentPath.ClosePath();
            }

            if (!_currentPath.IsEmpty)
            {
                Path transformed = TransformPath(_currentPath, _state.Ctm);
                IReadOnlyList<ClipPath>? snapshot = SnapshotClips();

                if (_state.FillValid)
                {
                    _ops.Add(new FillPathOp(transformed, EffectiveFill, rule, snapshot));
                }

                if (_state.StrokeValid)
                {
                    StrokeStyle style = BuildStrokeStyle();
                    _ops.Add(new StrokePathOp(transformed, style, snapshot));
                }
            }

            ApplyDeferredClip();
            _currentPath = new Path();
        }

        /// <inheritdoc />
        public void EndPath()
        {
            // n — no painting, but a pending clip still applies
            ApplyDeferredClip();
            _currentPath = new Path();
        }

        private void ApplyDeferredClip()
        {
            if (!_clipPending)
            {
                return;
            }

            if (!_currentPath.IsEmpty)
            {
                Path transformedClip = TransformPath(_currentPath, _state.Ctm);
                _state.ActiveClips.Add(new ClipPath(transformedClip, _clipRule));
            }

            _clipPending = false;
        }

        private StrokeStyle BuildStrokeStyle()
        {
            return new StrokeStyle
            {
                Width = _state.LineWidth,
                Cap = _state.LineCap,
                Join = _state.LineJoin,
                MiterLimit = _state.MiterLimit,
                DashPattern = _state.DashPattern,
                DashOffset = _state.DashOffset,
                Color = EffectiveStroke,
            };
        }

        private IReadOnlyList<ClipPath>? SnapshotClips()
        {
            if (_state.ActiveClips.Count == 0)
            {
                return null;
            }

            // RenderOp will defensively copy; pass the list reference.
            return _state.ActiveClips;
        }

        // ── Path geometry helpers ─────────────────────────────────────────

        private static Path TransformPath(Path source, Transform ctm)
        {
            Path result = new Path();

            foreach (PathSegment seg in source.Segments)
            {
                switch (seg.Kind)
                {
                    case PathSegmentKind.MoveTo:
                        PointF mp = ctm.TransformPoint(seg.P0);
                        result.MoveTo(mp.X, mp.Y);
                        break;
                    case PathSegmentKind.LineTo:
                        PointF lp = ctm.TransformPoint(seg.P0);
                        result.LineTo(lp.X, lp.Y);
                        break;
                    case PathSegmentKind.CubicBezierTo:
                        result.CubicBezierTo(
                            ctm.TransformPoint(seg.P0),
                            ctm.TransformPoint(seg.P1),
                            ctm.TransformPoint(seg.P2));
                        break;
                    case PathSegmentKind.ClosePath:
                        result.ClosePath();
                        break;
                }
            }

            return result;
        }

        // ── Text operators ────────────────────────────────────────────────

        /// <inheritdoc />
        public void BeginText()
        {
            _textMatrix = Transform.Identity;
            _textLineMatrix = Transform.Identity;
        }

        /// <inheritdoc />
        public void EndText()
        {
        }

        /// <inheritdoc />
        public void SetCharSpacing(double spacing)
        {
            _state.CharacterSpacing = spacing;
        }

        /// <inheritdoc />
        public void SetWordSpacing(double spacing)
        {
            _state.WordSpacing = spacing;
        }

        /// <inheritdoc />
        public void SetHorizontalScaling(double scale)
        {
            _state.HorizontalScaling = scale;
        }

        /// <inheritdoc />
        public void SetLeading(double leading)
        {
            _state.TextLeading = leading;
        }

        /// <inheritdoc />
        public void SetTextRenderingMode(int mode)
        {
            _state.TextRenderingMode = mode;
        }

        /// <inheritdoc />
        public void SetTextRise(double rise)
        {
            _state.TextRise = rise;
        }

        /// <inheritdoc />
        public void SetFont(string name, double size)
        {
            _state.FontName = name;
            _state.FontSize = size;
            _state.FontResources = _resources;
            _state.FontIsComposite = DetermineComposite(_resources, name);
        }

        // Returns true when the named font resource is a Type0 (composite) font.
        private bool DetermineComposite(PdfDictionary? resources, string fontName)
        {
            if (resources is null || string.IsNullOrEmpty(fontName))
            {
                return false;
            }

            if (!resources.TryGetValue(PdfName.Intern("Font"), out PdfPrimitive? fontDictRef))
            {
                return false;
            }

            PdfDictionary? fonts = _objects.ResolveAs<PdfDictionary>(fontDictRef ?? PdfNull.Value);

            if (fonts is null || !fonts.TryGetValue(PdfName.Intern(fontName), out PdfPrimitive? fontRef))
            {
                return false;
            }

            PdfDictionary? fd = _objects.ResolveAs<PdfDictionary>(fontRef ?? PdfNull.Value);
            PdfName? subtype = fd?.GetName(PdfName.Intern("Subtype"));
            return subtype is not null && subtype.Value == "Type0";
        }

        /// <inheritdoc />
        public void TextMove(double tx, double ty)
        {
            Transform t = new Transform(1, 0, 0, 1, tx, ty);
            _textLineMatrix = t.Multiply(_textLineMatrix);
            _textMatrix = _textLineMatrix;
        }

        /// <inheritdoc />
        public void TextMoveWithLeading(double tx, double ty)
        {
            _state.TextLeading = -ty;

            Transform t = new Transform(1, 0, 0, 1, tx, ty);
            _textLineMatrix = t.Multiply(_textLineMatrix);
            _textMatrix = _textLineMatrix;
        }

        /// <inheritdoc />
        public void SetTextMatrix(double a, double b, double c, double d, double e, double f)
        {
            Transform t = new Transform(a, b, c, d, e, f);

            _textMatrix = t;
            _textLineMatrix = t;
        }

        /// <inheritdoc />
        public void TextNextLine()
        {
            // T* — move to start of next line: 0 -leading Td
            Transform t = new Transform(1, 0, 0, 1, 0, -_state.TextLeading);
            _textLineMatrix = t.Multiply(_textLineMatrix);
            _textMatrix = _textLineMatrix;
        }

        /// <inheritdoc />
        public void ShowTextArray(IReadOnlyList<TextArrayElement> elements)
        {
            // [( str ) num ( str ) num ...] TJ
            foreach (TextArrayElement element in elements)
            {
                if (element.IsText)
                {
                    RouteShowText(element.Text!);
                }
                else
                {
                    // Positive displacement = move BACK in text direction.
                    // Per §9.4.3: tx = -displacement/1000 * fontSize * (Th/100)
                    double disp = element.Adjustment;
                    double tx = -disp / 1000.0 * _state.FontSize * (_state.HorizontalScaling / 100.0);
                    Transform tr = new Transform(1, 0, 0, 1, tx, 0);
                    _textMatrix = tr.Multiply(_textMatrix);
                }
            }
        }

        /// <inheritdoc />
        public void MoveNextLineShowText(byte[] text)
        {
            // ' — move to next line and show text
            TextNextLine();
            ShowTextSimple(text);
        }

        /// <inheritdoc />
        public void SetSpacingMoveNextLineShowText(double wordSpacing, double charSpacing, byte[] text)
        {
            // " — aw ac string — set word/char spacing, move to next line, show
            _state.WordSpacing = wordSpacing;
            _state.CharacterSpacing = charSpacing;
            TextNextLine();
            ShowTextSimple(text);
        }

        // ── Text showing ──────────────────────────────────────────────────

        /// <inheritdoc />
        public void ShowText(byte[] text)
        {
            RouteShowText(text);
        }

        // Routes a raw show-text string to the composite or simple path,
        // matching the pre-consolidation Tj/TJ dispatch. The ' and "
        // operators bypass this and use the simple path directly, exactly
        // as before consolidation (recorded follow-up: route those too).
        private void RouteShowText(byte[] raw)
        {
            if (_state.FontIsComposite)
            {
                ShowTextComposite(raw);
            }
            else
            {
                ShowTextSimple(raw);
            }
        }

        private void ShowTextSimple(byte[] raw)
        {
            if (raw is null || raw.Length == 0 || _state.FontSize <= 0)
            {
                return;
            }

            // Rendering mode 3 = invisible; skip emission but still advance.
            bool emit = _state.TextRenderingMode != 3;

            FontRenderer? renderer = GetFontRenderer();

            // Non-embedded Standard-14 fonts (Helvetica/Times/Courier/Symbol/
            // ZapfDingbats) carry no font program; their glyph outlines come
            // from the embedded substitute bundle via RenderableFont.
            RenderableFont? std14 = renderer is null ? GetStd14Font() : null;

            // Embedded Type1 (FontFile) programs, parsed separately from the
            // TrueType path.
            Type1FontRenderer? type1 = renderer is null && std14 is null ? GetType1Font() : null;

            Chuvadi.Pdf.Fonts.PdfFont? pdfFont = GetPdfFont();
            bool symbolic = IsSymbolicFont();

            // Simple fonts use single-byte codes: one byte = one code = one glyph.
            foreach (byte b in raw)
            {
                int code = b;
                double advance;

                if (renderer is not null)
                {
                    int gid = ResolveEmbeddedGid(renderer, pdfFont, code, symbolic);
                    GlyphOutline glyph = renderer.GetGlyphOutline(gid);
                    GlyphOutline? hintedGlyph = TryHint(renderer, gid);
                    GlyphOutline scaled = hintedGlyph ?? glyph.Scale(_state.FontSize);

                    if (emit && !scaled.IsEmpty && _state.FillValid)
                    {
                        Transform glyphPlacement = _textMatrix.Multiply(_state.Ctm);

                        if (_state.TextRise != 0.0)
                        {
                            Transform rise = new Transform(1, 0, 0, 1, 0, _state.TextRise);
                            glyphPlacement = rise.Multiply(glyphPlacement);
                        }

                        Path placed = TransformPath(scaled.Outline, glyphPlacement);
                        _ops.Add(new DrawGlyphOp(placed, EffectiveFill, SnapshotClips()));
                    }

                    double programAdvance = hintedGlyph is not null && !_lightHinting
                        ? hintedGlyph.Metrics.AdvanceWidth / _hintingScale
                        : glyph.Metrics.AdvanceWidthAt(_state.FontSize);
                    advance = ResolveSimpleAdvance(code, programAdvance);
                }
                else if (std14 is not null)
                {
                    char ch = DecodeOneChar(pdfFont, code);
                    Path glyphPath = std14.GetGlyphPath(ch, _state.FontSize);

                    if (emit && !glyphPath.IsEmpty && _state.FillValid)
                    {
                        Transform glyphPlacement = _textMatrix.Multiply(_state.Ctm);

                        if (_state.TextRise != 0.0)
                        {
                            Transform rise = new Transform(1, 0, 0, 1, 0, _state.TextRise);
                            glyphPlacement = rise.Multiply(glyphPlacement);
                        }

                        Path placed = TransformPath(glyphPath, glyphPlacement);
                        _ops.Add(new DrawGlyphOp(placed, EffectiveFill, SnapshotClips()));
                    }

                    advance = ResolveSimpleAdvance(code, std14.GetAdvanceWidth(ch, _state.FontSize));
                }
                else if (type1 is not null)
                {
                    Path glyphPath = type1.GetGlyphPath(code, _state.FontSize, out double adv);

                    if (emit && !glyphPath.IsEmpty && _state.FillValid)
                    {
                        Transform glyphPlacement = _textMatrix.Multiply(_state.Ctm);

                        if (_state.TextRise != 0.0)
                        {
                            Transform rise = new Transform(1, 0, 0, 1, 0, _state.TextRise);
                            glyphPlacement = rise.Multiply(glyphPlacement);
                        }

                        Path placed = TransformPath(glyphPath, glyphPlacement);
                        _ops.Add(new DrawGlyphOp(placed, EffectiveFill, SnapshotClips()));
                    }

                    advance = ResolveSimpleAdvance(code, adv);
                }
                else
                {
                    // No font available — approximate advance, no glyph emission.
                    advance = 0.6 * _state.FontSize;
                }

                // Per §9.4.4: tx = (w + Tc + Tw·(code==32 ? 1 : 0)) · Th/100.
                // Word spacing applies to the single-byte code 32 only.
                double extra = _state.CharacterSpacing;

                if (code == 32)
                {
                    extra += _state.WordSpacing;
                }

                double tx = (advance + extra) * (_state.HorizontalScaling / 100.0);

                Transform advanceMatrix = new Transform(1, 0, 0, 1, tx, 0);
                _textMatrix = advanceMatrix.Multiply(_textMatrix);
            }
        }

        // Returns the advance for a code from the font's /Widths array (PDF
        // glyph-space, 1/1000 em) when present, else the supplied program
        // advance. /Widths is the authoritative source for simple fonts.
        private double ResolveSimpleAdvance(int code, double programAdvance)
        {
            (int First, double[] Widths)? table = GetSimpleWidths();
            if (table is null)
            {
                return programAdvance;
            }

            int index = code - table.Value.First;
            if (index < 0 || index >= table.Value.Widths.Length)
            {
                return programAdvance;
            }

            double w = table.Value.Widths[index];
            return w > 0 ? w / 1000.0 * _state.FontSize : programAdvance;
        }

        private (int First, double[] Widths)? GetSimpleWidths()
        {
            if (string.IsNullOrEmpty(_state.FontName))
            {
                return null;
            }

            if (_widthsCache.TryGetValue(_state.FontName, out (int First, double[] Widths)? cached))
            {
                return cached;
            }

            (int First, double[] Widths)? table = ResolveSimpleWidths();
            _widthsCache[_state.FontName] = table;
            return table;
        }

        private (int First, double[] Widths)? ResolveSimpleWidths()
        {
            PdfDictionary? fd = ResolveFontDict();
            if (fd is null)
            {
                return null;
            }

            if (!fd.TryGetValue(PdfName.Intern("FirstChar"), out PdfPrimitive? firstPrim)
                || _objects.ResolveAs<PdfInteger>(firstPrim ?? PdfNull.Value) is not PdfInteger first)
            {
                return null;
            }

            if (!fd.TryGetValue(PdfName.Intern("Widths"), out PdfPrimitive? widthsPrim)
                || _objects.ResolveAs<PdfArray>(widthsPrim ?? PdfNull.Value) is not PdfArray widthsArr)
            {
                return null;
            }

            double[] widths = new double[widthsArr.Count];
            for (int i = 0; i < widthsArr.Count; i++)
            {
                PdfPrimitive? w = _objects.Resolve(widthsArr[i]);
                widths[i] = w switch
                {
                    PdfInteger pi => pi.Value,
                    PdfReal pr => pr.Value,
                    _ => 0.0,
                };
            }

            return (first.Value, widths);
        }

        // Resolves a content-stream byte code to a glyph index in an embedded
        // TrueType/OpenType program. Non-symbolic fonts with a Unicode cmap go
        // code -> Unicode (via the font's encoding) -> Unicode cmap, preserving
        // standard-encoding behaviour; otherwise the code selects the glyph
        // directly through the symbol/Mac cmap or as a subset glyph index.
        private int ResolveEmbeddedGid(
            FontRenderer renderer, Chuvadi.Pdf.Fonts.PdfFont? pdfFont, int code, bool symbolic)
        {
            if (!symbolic && pdfFont is not null)
            {
                string decoded = pdfFont.DecodeCode(code);
                if (decoded.Length > 0)
                {
                    int gid = renderer.GetGlyphIndexUnicode(decoded[0]);
                    if (gid != 0)
                    {
                        return gid;
                    }
                }
            }

            int byCode = renderer.GetGlyphIndexForCode(code, symbolic);
            if (byCode != 0)
            {
                return byCode;
            }

            // Final attempt: a symbolic font that nonetheless has a usable
            // Unicode mapping via its encoding.
            if (pdfFont is not null)
            {
                string decoded = pdfFont.DecodeCode(code);
                if (decoded.Length > 0)
                {
                    int gid = renderer.GetGlyphIndexUnicode(decoded[0]);
                    if (gid != 0)
                    {
                        return gid;
                    }
                }
            }

            return 0;
        }

        // Decodes a single content-stream code to one character via the font's
        // encoding/ToUnicode, for substitute (Standard-14) rendering. Falls back
        // to Latin-1 interpretation of the raw byte.
        private static char DecodeOneChar(Chuvadi.Pdf.Fonts.PdfFont? pdfFont, int code)
        {
            if (pdfFont is not null)
            {
                string decoded = pdfFont.DecodeCode(code);
                if (decoded.Length > 0)
                {
                    return decoded[0];
                }
            }

            return (char)code;
        }

        // Reads the /Flags symbolic bit (bit 3, value 4) from the current font's
        // FontDescriptor. Cached per resource font name.
        private bool IsSymbolicFont()
        {
            if (string.IsNullOrEmpty(_state.FontName))
            {
                return false;
            }

            if (_symbolicCache.TryGetValue(_state.FontName, out bool cached))
            {
                return cached;
            }

            bool symbolic = ResolveSymbolicFlag();
            _symbolicCache[_state.FontName] = symbolic;
            return symbolic;
        }

        private bool ResolveSymbolicFlag()
        {
            PdfDictionary? fd = ResolveFontDict();
            if (fd is null)
            {
                return false;
            }

            PdfDictionary? descriptor = _objects.ResolveAs<PdfDictionary>(
                fd.TryGetValue(PdfName.Intern("FontDescriptor"), out PdfPrimitive? d) ? d ?? PdfNull.Value : PdfNull.Value);

            // Type0 fonts carry the descriptor on the descendant font.
            if (descriptor is null
                && fd.TryGetValue(PdfName.Intern("DescendantFonts"), out PdfPrimitive? df)
                && _objects.ResolveAs<PdfArray>(df ?? PdfNull.Value) is PdfArray arr
                && arr.Count > 0
                && _objects.ResolveAs<PdfDictionary>(arr[0]) is PdfDictionary cid
                && cid.TryGetValue(PdfName.Intern("FontDescriptor"), out PdfPrimitive? cd))
            {
                descriptor = _objects.ResolveAs<PdfDictionary>(cd ?? PdfNull.Value);
            }

            if (descriptor is null)
            {
                return false;
            }

            if (descriptor.TryGetValue(PdfName.Intern("Flags"), out PdfPrimitive? flagsPrim)
                && _objects.ResolveAs<PdfInteger>(flagsPrim ?? PdfNull.Value) is PdfInteger flags)
            {
                // Bit 3 (value 4) = Symbolic.
                return (flags.Value & 4) != 0;
            }

            return false;
        }

        // Resolves the current font's dictionary from the page resources.
        private PdfDictionary? ResolveFontDict()
        {
            PdfDictionary? resources = _state.FontResources;
            if (resources is null)
            {
                return null;
            }

            if (!resources.TryGetValue(PdfName.Intern("Font"), out PdfPrimitive? fontDict))
            {
                return null;
            }

            PdfDictionary? fonts = _objects.ResolveAs<PdfDictionary>(fontDict ?? PdfNull.Value);
            if (fonts is null)
            {
                return null;
            }

            if (!fonts.TryGetValue(PdfName.Intern(_state.FontName), out PdfPrimitive? fontRef))
            {
                return null;
            }

            return _objects.ResolveAs<PdfDictionary>(fontRef ?? PdfNull.Value);
        }

        // Renders a composite (Type0) text string. Codes are two bytes
        // (Identity-H); with an Identity CIDToGIDMap the code is the GID,
        // so the outline is resolved directly by glyph index.
        private void ShowTextComposite(byte[] raw)
        {
            if (raw.Length < 2 || _state.FontSize <= 0)
            {
                return;
            }

            bool emit = _state.TextRenderingMode != 3;
            FontRenderer? renderer = GetFontRenderer();

            for (int i = 0; i + 1 < raw.Length; i += 2)
            {
                int code = (raw[i] << 8) | raw[i + 1];
                double advance;

                if (renderer is null)
                {
                    advance = 0.5 * _state.FontSize;
                }
                else
                {
                    GlyphOutline glyph = renderer.GetGlyphOutline(code);
                    GlyphOutline? hintedGlyph = TryHint(renderer, code);
                    GlyphOutline scaled = hintedGlyph ?? glyph.Scale(_state.FontSize);

                    if (emit && !scaled.IsEmpty && _state.FillValid)
                    {
                        Transform glyphPlacement = _textMatrix.Multiply(_state.Ctm);

                        if (_state.TextRise != 0.0)
                        {
                            Transform rise = new Transform(1, 0, 0, 1, 0, _state.TextRise);
                            glyphPlacement = rise.Multiply(glyphPlacement);
                        }

                        Path placed = TransformPath(scaled.Outline, glyphPlacement);
                        _ops.Add(new DrawGlyphOp(placed, EffectiveFill, SnapshotClips()));
                    }

                    advance = hintedGlyph is not null && !_lightHinting
                        ? hintedGlyph.Metrics.AdvanceWidth / _hintingScale
                        : glyph.Metrics.AdvanceWidthAt(_state.FontSize);
                }

                double tx = (advance + _state.CharacterSpacing) * (_state.HorizontalScaling / 100.0);
                Transform advanceMatrix = new Transform(1, 0, 0, 1, tx, 0);
                _textMatrix = advanceMatrix.Multiply(_textMatrix);
            }
        }

        // ── Font resolution ───────────────────────────────────────────────

        // Returns a grid-fitted, device-space outline when hinting is enabled
        // and the glyph can be hinted; otherwise null so the caller falls back
        // to the scaled unhinted outline. Device ppem equals the device-space
        // font size (the text/CTM scale is already folded into _state.FontSize).
        // Grid-fits a glyph at the true device ppem (PDF size x device scale),
        // then expresses the device-space fitted outline back in PDF user
        // space by dividing by the device scale. The painter re-applies the
        // same scale, exactly reconstructing the grid-fitted pixel positions.
        // Returns null when hinting is off or the glyph cannot be hinted.
        private GlyphOutline? TryHint(FontRenderer renderer, int glyphId)
        {
            if (_hintingScale <= 0.0)
            {
                return null;
            }

            int ppem = (int)Math.Round(_state.FontSize * _hintingScale);

            if (ppem <= 0)
            {
                return null;
            }

            GlyphOutline? hinted = renderer.GetHintedGlyphOutline(glyphId, ppem, _lightHinting, _autohintFallback);

            if (hinted is null)
            {
                return null;
            }

            // Device pixels -> PDF user space, so the painter's device scale
            // restores the fitted positions instead of compounding them.
            Transform toUserSpace = Transform.CreateScale(1.0 / _hintingScale);
            Path userSpace = TransformPath(hinted.Outline, toUserSpace);
            return new GlyphOutline(userSpace, hinted.Metrics);
        }

        private FontRenderer? GetFontRenderer()
        {
            if (string.IsNullOrEmpty(_state.FontName))
            {
                return null;
            }

            if (_fontCache.TryGetValue(_state.FontName, out FontRenderer? cached))
            {
                return cached;
            }

            FontRenderer? renderer = ResolveFontRenderer();
            _fontCache[_state.FontName] = renderer;
            return renderer;
        }

        private RenderableFont? GetStd14Font()
        {
            if (string.IsNullOrEmpty(_state.FontName))
            {
                return null;
            }

            if (_std14FontCache.TryGetValue(_state.FontName, out RenderableFont? cached))
            {
                return cached;
            }

            RenderableFont? font = ResolveStd14Font();
            _std14FontCache[_state.FontName] = font;
            return font;
        }

        private RenderableFont? ResolveStd14Font()
        {
            PdfDictionary? resources = _state.FontResources;

            if (resources is null)
            {
                return null;
            }

            if (!resources.TryGetValue(PdfName.Intern("Font"), out PdfPrimitive? fontDict))
            {
                return null;
            }

            PdfDictionary? fonts = _objects.ResolveAs<PdfDictionary>(fontDict ?? PdfNull.Value);

            if (fonts is null)
            {
                return null;
            }

            if (!fonts.TryGetValue(PdfName.Intern(_state.FontName), out PdfPrimitive? fontRef))
            {
                return null;
            }

            PdfDictionary? fd = _objects.ResolveAs<PdfDictionary>(fontRef ?? PdfNull.Value);

            if (fd is null)
            {
                return null;
            }

            try
            {
                RenderableFont font = RenderableFont.FromDictionary(fd, _objects);
                return font.IsStandard14 ? font : null;
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        private Type1FontRenderer? GetType1Font()
        {
            if (string.IsNullOrEmpty(_state.FontName))
            {
                return null;
            }

            if (_type1Cache.TryGetValue(_state.FontName, out Type1FontRenderer? cached))
            {
                return cached;
            }

            Type1FontRenderer? font = ResolveType1Font();
            _type1Cache[_state.FontName] = font;
            return font;
        }

        private Type1FontRenderer? ResolveType1Font()
        {
            PdfDictionary? fd = ResolveFontDict();
            if (fd is null)
            {
                return null;
            }

            PdfName? subtype = fd.GetName(PdfName.Intern("Subtype"));
            if (subtype is null || !string.Equals(subtype.Value, "Type1", System.StringComparison.Ordinal))
            {
                return null;
            }

            PdfDictionary? descriptor = _objects.ResolveAs<PdfDictionary>(
                fd.TryGetValue(PdfName.Intern("FontDescriptor"), out PdfPrimitive? d) ? d ?? PdfNull.Value : PdfNull.Value);
            if (descriptor is null)
            {
                return null;
            }

            byte[]? fontFile = ExtractStreamBytes(descriptor, "FontFile");
            if (fontFile is null)
            {
                return null;
            }

            try
            {
                return Type1FontRenderer.Create(fontFile, fd, _objects);
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        private Chuvadi.Pdf.Fonts.PdfFont? GetPdfFont()
        {
            if (string.IsNullOrEmpty(_state.FontName))
            {
                return null;
            }

            if (_pdfFontCache.TryGetValue(_state.FontName, out Chuvadi.Pdf.Fonts.PdfFont? cached))
            {
                return cached;
            }

            Chuvadi.Pdf.Fonts.PdfFont? font = ResolvePdfFont();
            _pdfFontCache[_state.FontName] = font;
            return font;
        }

        private Chuvadi.Pdf.Fonts.PdfFont? ResolvePdfFont()
        {
            PdfDictionary? resources = _state.FontResources;

            if (resources is null)
            {
                return null;
            }

            if (!resources.TryGetValue(PdfName.Intern("Font"), out PdfPrimitive? fontDict))
            {
                return null;
            }

            PdfDictionary? fonts = _objects.ResolveAs<PdfDictionary>(fontDict ?? PdfNull.Value);

            if (fonts is null)
            {
                return null;
            }

            if (!fonts.TryGetValue(PdfName.Intern(_state.FontName), out PdfPrimitive? fontRef))
            {
                return null;
            }

            PdfDictionary? fd = _objects.ResolveAs<PdfDictionary>(fontRef ?? PdfNull.Value);

            if (fd is null)
            {
                return null;
            }

            try
            {
                return Chuvadi.Pdf.Fonts.PdfFont.FromDictionary(fd, _objects);
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        private FontRenderer? ResolveFontRenderer()
        {
            PdfDictionary? resources = _state.FontResources;

            if (resources is null)
            {
                return null;
            }

            if (!resources.TryGetValue(PdfName.Intern("Font"), out PdfPrimitive? fontDict))
            {
                return null;
            }

            PdfDictionary? fonts = _objects.ResolveAs<PdfDictionary>(fontDict ?? PdfNull.Value);

            if (fonts is null)
            {
                return null;
            }

            if (!fonts.TryGetValue(PdfName.Intern(_state.FontName), out PdfPrimitive? fontRef))
            {
                return null;
            }

            byte[]? fontBytes = ExtractFontBytes(fontRef ?? PdfNull.Value);

            if (fontBytes is null)
            {
                return null;
            }

            try
            {
                return new FontRenderer(fontBytes);
            }
            catch (FontRenderingException)
            {
                return null;
            }
        }

        private byte[]? ExtractFontBytes(PdfPrimitive fontRef)
        {
            PdfDictionary? fontDict = _objects.ResolveAs<PdfDictionary>(fontRef);

            if (fontDict is null)
            {
                return null;
            }

            if (!fontDict.TryGetValue(PdfName.Intern("FontDescriptor"), out PdfPrimitive? fdRef))
            {
                // Type0 (composite) fonts carry no direct FontDescriptor;
                // the embedded program lives on the descendant CIDFont.
                if (fontDict.TryGetValue(PdfName.Intern("DescendantFonts"), out PdfPrimitive? dfRef))
                {
                    PdfArray? descendants = _objects.ResolveAs<PdfArray>(dfRef ?? PdfNull.Value);

                    if (descendants is not null && descendants.Count > 0)
                    {
                        PdfDictionary? cidFont = _objects.ResolveAs<PdfDictionary>(descendants[0]);

                        if (cidFont is not null
                            && cidFont.TryGetValue(PdfName.Intern("FontDescriptor"), out PdfPrimitive? cidFdRef))
                        {
                            fdRef = cidFdRef;
                        }
                    }
                }

                if (fdRef is null)
                {
                    return null;
                }
            }

            PdfDictionary? fd = _objects.ResolveAs<PdfDictionary>(fdRef ?? PdfNull.Value);

            if (fd is null)
            {
                return null;
            }

            string[] keys = ["FontFile2", "FontFile", "FontFile3"];

            foreach (string key in keys)
            {
                if (!fd.TryGetValue(PdfName.Intern(key), out PdfPrimitive? ffRef))
                {
                    continue;
                }

                PdfStream? fontStream = _objects.ResolveAs<PdfStream>(ffRef ?? PdfNull.Value);

                if (fontStream is not null)
                {
                    return ContentStreamLoader.Decode(fontStream);
                }
            }

            return null;
        }

        // Resolves and decodes a named stream entry (e.g. FontFile) on a
        // dictionary, returning the decoded bytes or null when absent.
        private byte[]? ExtractStreamBytes(PdfDictionary dict, string key)
        {
            if (!dict.TryGetValue(PdfName.Intern(key), out PdfPrimitive? streamRef))
            {
                return null;
            }

            PdfStream? stream = _objects.ResolveAs<PdfStream>(streamRef ?? PdfNull.Value);
            return stream is null ? null : ContentStreamLoader.Decode(stream);
        }

        // ── XObject Do ────────────────────────────────────────────────────

        /// <inheritdoc />
        public void InvokeXObject(string name)
        {
            if (_resources is null)
            {
                return;
            }

            if (!_resources.TryGetValue(PdfName.Intern("XObject"), out PdfPrimitive? xobjDictRef))
            {
                return;
            }

            PdfDictionary? xObjects = _objects.ResolveAs<PdfDictionary>(xobjDictRef ?? PdfNull.Value);

            if (xObjects is null)
            {
                return;
            }

            if (!xObjects.TryGetValue(PdfName.Intern(name), out PdfPrimitive? xobjRef))
            {
                return;
            }

            PdfStream? xobjStream = _objects.ResolveAs<PdfStream>(xobjRef ?? PdfNull.Value);

            if (xobjStream is null)
            {
                return;
            }

            if (!xobjStream.Dictionary.TryGetValue(PdfName.Intern("Subtype"), out PdfPrimitive? subtypePrim))
            {
                return;
            }

            if (subtypePrim is not PdfName subtype)
            {
                return;
            }

            if (subtype.Value == "Image")
            {
                EmitImageXObject(xobjStream);
            }
            else if (subtype.Value == "Form")
            {
                EmitFormXObject(xobjStream, _resources);
            }
        }

        // Image-format filters (DCTDecode/JPXDecode) carry the encoded image
        // bytes directly; they must not go through the sample-filter pipeline.
        private static bool StreamIsJpegOrJpx(PdfStream stream)
        {
            if (!stream.IsFiltered)
            {
                return false;
            }

            PdfPrimitive? filter = stream.Filter;

            if (filter is PdfName name)
            {
                string r = FilterRegistry.ResolveAlias(name.Value);
                return r == "DCTDecode" || r == "JPXDecode";
            }

            if (filter is PdfArray array && array.Count > 0)
            {
                PdfName? last = array.GetAs<PdfName>(array.Count - 1);
                if (last is null)
                {
                    return false;
                }

                string r = FilterRegistry.ResolveAlias(last.Value);
                return r == "DCTDecode" || r == "JPXDecode";
            }

            return false;
        }

        private void EmitImageXObject(PdfStream xobjStream)
        {
            byte[] imageBytes;

            try
            {
                imageBytes = StreamIsJpegOrJpx(xobjStream) ? xobjStream.RawBytes : ContentStreamLoader.Decode(xobjStream);
            }
            catch (Exception)
            {
                return;
            }

            ImageFrame? frame = null;

            try
            {
                if (imageBytes.Length > 2 &&
                    imageBytes[0] == 0xFF && imageBytes[1] == 0xD8)
                {
                    frame = JpegDecoder.Decode(imageBytes);
                }
                else if (imageBytes.Length > 8 &&
                         imageBytes[0] == 137 && imageBytes[1] == 80)
                {
                    frame = PngDecoder.Decode(imageBytes);
                }
            }
            catch (ImageException)
            {
                return;
            }

            // Not a self-describing codec stream: interpret the bytes as raw
            // PDF image samples (Flate/CCITT/LZW output) using the image
            // dictionary's geometry and colour space.
            frame ??= FrameFromRawSamples(xobjStream, imageBytes);

            if (frame is null)
            {
                return;
            }

            ApplySoftMask(xobjStream, frame);

            _ops.Add(new DrawImageOp(frame, _state.Ctm, SnapshotClips(), _state.FillAlpha));
        }

        // Converts decoded raw samples into an ImageFrame for the cases the
        // raster pipeline supports: 1-bpc DeviceGray (scanned bilevel, e.g.
        // CCITTFaxDecode output), 8-bpc DeviceGray, and 8-bpc DeviceRGB,
        // including ICCBased streams with 1 or 3 components, honouring a
        // /Decode [1 0] inversion for the gray cases. Stencil masks
        // (/ImageMask true) and other colour spaces return null and the
        // image is skipped, as before.
        private ImageFrame? FrameFromRawSamples(PdfStream stream, byte[] samples)
        {
            PdfDictionary dict = stream.Dictionary;

            if (ReadBoolEntry(dict, "ImageMask"))
            {
                return null;
            }

            int width = ReadIntEntry(dict, "Width");
            int height = ReadIntEntry(dict, "Height");
            int bpc = ReadIntEntry(dict, "BitsPerComponent");

            if (width <= 0 || height <= 0)
            {
                return null;
            }

            int components = ResolveComponentCount(dict);
            bool invert = GrayDecodeInverted(dict);

            if (components == 1 && bpc == 1)
            {
                int stride = (width + 7) / 8;
                if (samples.Length < stride * height)
                {
                    return null;
                }

                ImageFrame frame = ImageFrame.Create(width, height, ImageColorFormat.Gray8);
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int bit = (samples[(y * stride) + (x >> 3)] >> (7 - (x & 7))) & 1;
                        bool white = invert ? bit == 0 : bit == 1;
                        byte v = white ? (byte)255 : (byte)0;
                        frame.Pixels.SetPixelBgra(x, y, v, v, v, 255);
                    }
                }
                return frame;
            }

            if (components == 1 && bpc == 8)
            {
                if (samples.Length < width * height)
                {
                    return null;
                }

                ImageFrame frame = ImageFrame.Create(width, height, ImageColorFormat.Gray8);
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        byte v = samples[(y * width) + x];
                        if (invert)
                        {
                            v = (byte)(255 - v);
                        }
                        frame.Pixels.SetPixelBgra(x, y, v, v, v, 255);
                    }
                }
                return frame;
            }

            if (components == 3 && bpc == 8)
            {
                if (samples.Length < width * height * 3)
                {
                    return null;
                }

                ImageFrame frame = ImageFrame.Create(width, height, ImageColorFormat.Rgb24);
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int si = ((y * width) + x) * 3;
                        frame.Pixels.SetPixelBgra(
                            x, y, samples[si + 2], samples[si + 1], samples[si], 255);
                    }
                }
                return frame;
            }

            return null;
        }

        // Applies an image's /SMask (a DeviceGray image giving per-pixel alpha)
        // onto the decoded frame's alpha channel. The soft mask is sampled at
        // the frame's resolution (nearest-neighbour) when the dimensions differ.
        // Without this, soft-masked images (e.g. faded watermarks) render as
        // fully opaque, painting their backing colour where they should be
        // transparent.
        private void ApplySoftMask(PdfStream xobjStream, ImageFrame frame)
        {
            if (!xobjStream.Dictionary.TryGetValue(PdfName.Intern("SMask"), out PdfPrimitive? smRef))
            {
                return;
            }

            if (_objects.Resolve(smRef) is not PdfStream smStream)
            {
                return;
            }

            int smWidth = ReadIntEntry(smStream.Dictionary, "Width");
            int smHeight = ReadIntEntry(smStream.Dictionary, "Height");
            int smBpc = ReadIntEntry(smStream.Dictionary, "BitsPerComponent");

            if (smWidth <= 0 || smHeight <= 0 || smBpc != 8)
            {
                return;
            }

            byte[] smSamples;

            try
            {
                smSamples = ContentStreamLoader.Decode(smStream);
            }
            catch (Exception)
            {
                return;
            }

            if (smSamples.Length < smWidth * smHeight)
            {
                return;
            }

            bool invert = GrayDecodeInverted(smStream.Dictionary);
            int fw = frame.Width;
            int fh = frame.Height;

            // /Matte: base colour samples are pre-multiplied against this colour
            // (PDF 32000-1:2008 §11.6.5.3). Recover c = (c' - m)/alpha + m.
            double[]? matte = ReadMatteEntry(smStream.Dictionary);

            for (int y = 0; y < fh; y++)
            {
                int sy = smHeight == fh ? y : (int)((long)y * smHeight / fh);
                if (sy >= smHeight)
                {
                    sy = smHeight - 1;
                }

                for (int x = 0; x < fw; x++)
                {
                    int sx = smWidth == fw ? x : (int)((long)x * smWidth / fw);
                    if (sx >= smWidth)
                    {
                        sx = smWidth - 1;
                    }

                    byte alpha = smSamples[(sy * smWidth) + sx];
                    if (invert)
                    {
                        alpha = (byte)(255 - alpha);
                    }

                    (byte b, byte g, byte r, byte _) = frame.Pixels.GetPixelBgra(x, y);

                    if (matte is not null && alpha > 0 && matte.Length >= 3)
                    {
                        double a = alpha / 255.0;
                        r = Unpremultiply(r, matte[0], a);
                        g = Unpremultiply(g, matte[1], a);
                        b = Unpremultiply(b, matte[2], a);
                    }

                    frame.Pixels.SetPixelBgra(x, y, b, g, r, alpha);
                }
            }
        }

        // Recovers a single matte-premultiplied colour channel:
        // c = (c' - m)/alpha + m, clamped to [0, 1] and scaled back to a byte.
        private static byte Unpremultiply(byte sample, double matte, double alpha)
        {
            double recovered = (((sample / 255.0) - matte) / alpha) + matte;
            if (recovered < 0.0) { recovered = 0.0; }
            if (recovered > 1.0) { recovered = 1.0; }
            return (byte)Math.Round(recovered * 255.0);
        }

        // Reads a soft mask's /Matte colour as components in [0, 1], or null.
        private static double[]? ReadMatteEntry(PdfDictionary smDict)
        {
            if (!smDict.TryGetValue(PdfName.Intern("Matte"), out PdfPrimitive? value)
                || value is not PdfArray array || array.Count == 0)
            {
                return null;
            }

            double[] matte = new double[array.Count];
            for (int i = 0; i < array.Count; i++)
            {
                matte[i] = PdfReal.ToDouble(array[i]);
            }
            return matte;
        }

        // Number of colour components implied by /ColorSpace: DeviceGray and
        // CalGray are 1, DeviceRGB and CalRGB are 3, and ICCBased defers to
        // its stream's /N. Unsupported spaces return 0.
        private int ResolveComponentCount(PdfDictionary dict)
        {
            if (!dict.TryGetValue(PdfName.Intern("ColorSpace"), out PdfPrimitive? csRef))
            {
                return 0;
            }

            PdfPrimitive cs = _objects.Resolve(csRef);

            if (cs is PdfName name)
            {
                return name.Value switch
                {
                    "DeviceGray" or "CalGray" or "G" => 1,
                    "DeviceRGB" or "CalRGB" or "RGB" => 3,
                    _ => 0,
                };
            }

            if (cs is PdfArray array && array.Count >= 2 &&
                array[0] is PdfName family && family.Value == "ICCBased")
            {
                PdfStream? icc = _objects.ResolveAs<PdfStream>(array[1]);
                if (icc is not null &&
                    icc.Dictionary.TryGetValue(PdfName.Intern("N"), out PdfPrimitive? n) &&
                    n is PdfInteger count)
                {
                    return count.Value is 1 or 3 ? count.Value : 0;
                }
            }

            return 0;
        }

        // True when /Decode is [1 0] for a single-component image (inverted
        // gray, common in scanned PDFs).
        private bool GrayDecodeInverted(PdfDictionary dict)
        {
            if (!dict.TryGetValue(PdfName.Intern("Decode"), out PdfPrimitive? decodeRef))
            {
                return false;
            }

            return _objects.Resolve(decodeRef) is PdfArray decode &&
                   decode.Count >= 2 &&
                   AsDouble(decode[0]) > AsDouble(decode[1]);
        }

        private int ReadIntEntry(PdfDictionary dict, string key)
        {
            if (!dict.TryGetValue(PdfName.Intern(key), out PdfPrimitive? value))
            {
                return 0;
            }
            return _objects.Resolve(value) is PdfInteger i ? i.Value : 0;
        }

        private bool ReadBoolEntry(PdfDictionary dict, string key)
        {
            if (!dict.TryGetValue(PdfName.Intern(key), out PdfPrimitive? value))
            {
                return false;
            }
            return _objects.Resolve(value) is PdfBoolean b && b.Value;
        }

        private void EmitFormXObject(PdfStream xobjStream, PdfDictionary? outerResources)
        {
            // Form XObject's Matrix entry composes with the current CTM
            Transform formMatrix = Transform.Identity;

            if (xobjStream.Dictionary.TryGetValue(PdfName.Intern("Matrix"), out PdfPrimitive? matrixPrim))
            {
                PdfArray? arr = _objects.ResolveAs<PdfArray>(matrixPrim ?? PdfNull.Value);

                if (arr is not null && arr.Count >= 6)
                {
                    formMatrix = new Transform(
                        AsDouble(arr[0]), AsDouble(arr[1]),
                        AsDouble(arr[2]), AsDouble(arr[3]),
                        AsDouble(arr[4]), AsDouble(arr[5]));
                }
            }

            // Resolve form's own resources, or inherit from outer page
            PdfDictionary? formResources = outerResources;

            if (xobjStream.Dictionary.TryGetValue(PdfName.Intern("Resources"), out PdfPrimitive? resPrim))
            {
                PdfDictionary? r = _objects.ResolveAs<PdfDictionary>(resPrim ?? PdfNull.Value);

                if (r is not null)
                {
                    formResources = r;
                }
            }

            // Build the sub-display-list in form-local space with a fresh
            // worker (identity CTM, fresh path/text state, fresh stack).
            Worker sub = new Worker(_objects, _hintingScale, _lightHinting, _autohintFallback);

            byte[] formContent;

            try
            {
                formContent = ContentStreamLoader.Decode(xobjStream);
            }
            catch (Exception)
            {
                return;
            }

            PageDisplayList inner;

            if (formContent.Length > 0)
            {
                sub._resources = formResources;
                ContentStreamWalker.Walk(formContent, sub);
            }

            inner = new PageDisplayList(sub._ops, 0, 0);

            // Composition: form-local · outer CTM (row-vector convention)
            Transform composition = formMatrix.Multiply(_state.Ctm);

            _ops.Add(new NestedDisplayListOp(inner, composition, SnapshotClips()));
        }

        private static double AsDouble(PdfPrimitive p)
        {
            return p switch
            {
                PdfInteger i => i.Value,
                PdfReal r => r.Value,
                _ => 0.0,
            };
        }


    }
}
