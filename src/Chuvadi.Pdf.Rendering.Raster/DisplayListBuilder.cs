// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.8 — Content streams; §8 — Graphics; §9 — Text
// PHASE: v2.0.0 R1 D3c-2 — DisplayList builder

using System;
using System.Collections.Generic;
using System.Text;
using Chuvadi.Pdf.Annotations;
using Chuvadi.Pdf.Content;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Filters;
using Chuvadi.Pdf.Fonts.Rendering;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Images;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using BlendModes = Chuvadi.Pdf.Rendering.DisplayList.BlendModes;
using PdfBlendMode = Chuvadi.Pdf.Rendering.DisplayList.PdfBlendMode;
using Type3Font = Chuvadi.Pdf.Rendering.DisplayList.Type3Font;
using Type3Glyph = Chuvadi.Pdf.Rendering.DisplayList.Type3Glyph;
using Chuvadi.Pdf.Rendering.Walking;
using Chuvadi.Pdf.Text.Shaping;
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
        return Build(page, objects, page.OptionalContent, hintingScale, lightHinting, autohintFallback);
    }

    /// <summary>
    /// Interprets a page's content with an explicit layer visibility: content
    /// in hidden optional-content groups is left out (pass
    /// <see cref="OptionalContentVisibility.AllVisible"/> to draw every layer).
    /// The other parameters are as for
    /// <see cref="Build(PdfPage, PdfObjectStore, double, bool, bool)"/>.
    /// </summary>
    /// <param name="page">The PDF page to interpret.</param>
    /// <param name="objects">The object store for resolving indirect references.</param>
    /// <param name="optionalContent">Which layers are visible.</param>
    /// <param name="hintingScale">Device scale (DPI/72) for grid-fitting; 0 disables hinting.</param>
    /// <param name="lightHinting">When true, grid-fit the Y axis only.</param>
    /// <param name="autohintFallback">When true, unhinted fonts are grid-fitted by the autohinter.</param>
    /// <returns>An immutable display list.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public static PageDisplayList Build(
        PdfPage page, PdfObjectStore objects, OptionalContentVisibility optionalContent,
        double hintingScale = 0.0, bool lightHinting = false, bool autohintFallback = true) =>
        Build(page, objects, optionalContent, AnnotationMode.View, hintingScale, lightHinting, autohintFallback);

    /// <summary>
    /// Builds a page's display list with explicit layer visibility and an
    /// <see cref="AnnotationMode"/> (3.31): <see cref="AnnotationMode.Print"/>
    /// draws what prints (the Print flag decides),
    /// <see cref="AnnotationMode.FormFieldsOnly"/> only the form fields, and
    /// <see cref="AnnotationMode.None"/> no annotations.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="objects">The object store for resolving indirect references.</param>
    /// <param name="optionalContent">Which layers are visible.</param>
    /// <param name="annotations">Which annotations are drawn.</param>
    /// <param name="hintingScale">Device scale (DPI/72) for grid-fitting; 0 disables hinting.</param>
    /// <param name="lightHinting">When true, grid-fit the Y axis only.</param>
    /// <param name="autohintFallback">When true, fonts with no hinting programs are autohinted.</param>
    /// <returns>The page's display list.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static PageDisplayList Build(
        PdfPage page, PdfObjectStore objects, OptionalContentVisibility optionalContent, AnnotationMode annotations,
        double hintingScale = 0.0, bool lightHinting = false, bool autohintFallback = true)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(optionalContent);

        Worker worker = new Worker(objects, hintingScale, lightHinting, autohintFallback, optionalContent) { Annotations = annotations };
        byte[] content = annotations == AnnotationMode.FormFieldsOnly ? Array.Empty<byte>() : ContentStreamLoader.Load(page.Contents, objects, page.PageNumber);
        return worker.BuildFromPage(content, page);
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
        // Nesting depth across sub-workers (form XObjects, tiling-pattern
        // cells, soft-mask groups, Type 3 glyphs). A self-referential pattern
        // or form would otherwise recurse until the process dies with an
        // un-catchable StackOverflowException.
        private const int MaxNestingDepth = 16;
        private int _nestingDepth;

        private readonly PdfObjectStore _objects;
        private readonly double _hintingScale;
        private readonly bool _lightHinting;
        private readonly bool _autohintFallback;
        private readonly Dictionary<string, FontRenderer?> _fontCache;
        private readonly Dictionary<string, int[]?> _cidToGidCache;
        private readonly Dictionary<string, Chuvadi.Pdf.Fonts.PdfFont?> _pdfFontCache;
        private readonly Dictionary<string, RenderableFont?> _std14FontCache;
        private readonly Dictionary<string, bool> _symbolicCache;
        private readonly Dictionary<string, Type1FontRenderer?> _type1Cache;
        private readonly Dictionary<string, (int First, double[] Widths)?> _widthsCache;
        private readonly Dictionary<string, (Dictionary<int, double> Widths, double Default)> _compositeWidthsCache =
            new Dictionary<string, (Dictionary<int, double>, double)>();
        private readonly Dictionary<string, Type3Font?> _type3FontCache;
        private readonly Dictionary<(string Font, int Code, uint Fill), PageDisplayList> _type3GlyphCache;

        // True while building a d1 (uncoloured) Type 3 glyph: colour operators
        // in the glyph description are ignored; the text fill colour is used.
        private bool _suppressColorOps;

        // Render-op accumulator
        private readonly List<RenderOp> _ops;

        // Graphics-state stack (q/Q)
        private BuilderGraphicsState _state;
        private readonly Stack<BuilderGraphicsState> _stateStack;

        // Path construction (pre-CTM, user-space coords)
        private Path _currentPath;

        // The start of the current subpath (the last m or re), where a path
        // closed by h continues, and whether the last path operator closed
        // it (Path then has no current point) (ticket 39, D-332).
        private PointF _subpathStart;
        private bool _subpathClosed;

        // Text state (NOT in q/Q stack — these reset on BT)
        private Transform _textMatrix;
        private Transform _textLineMatrix;

        // Deferred clip (W or W* observed; applies AFTER the next painting op)
        private bool _clipPending;
        private FillRule _clipRule;

        // Resources of the stream being walked (page or form XObject)
        private PdfDictionary? _resources;

        // 3.21: layer visibility. One entry per open BMC/BDC (true when that
        // BDC /OC sequence is hidden); painting is suppressed while any open
        // sequence is hidden. Graphics state still updates normally.
        private readonly OptionalContentVisibility _visibility;

        // 3.27: fallbacks are reported, not silent. Shared with sub-workers so
        // the page number and the once-per-subject rule cover forms too.
        private WarningReporter _reporter;
        private readonly List<bool> _hiddenStack = new List<bool>();
        private int _hiddenDepth;

        // 3.31 (D-146): which annotations are drawn.
        internal AnnotationMode Annotations { get; init; } = AnnotationMode.View;

        // 3.34.0 (Phase B N10): fonts read once per page, keyed by the font
        // dictionary and shared with the workers for forms, patterns, groups
        // and Type 3 glyphs. Before, each form's worker started empty and
        // decoded the font program again for every form it drew (a page of
        // 375 forms took over a second; a real file hit the 30 s limit). The
        // per-worker caches stay keyed by resource name for speed.
        private SharedFonts _shared = new SharedFonts();

        private sealed class SharedFonts
        {
            internal Dictionary<PdfDictionary, FontRenderer?> Renderers { get; } = new Dictionary<PdfDictionary, FontRenderer?>(ReferenceEqualityComparer.Instance);

            internal Dictionary<PdfDictionary, Type1FontRenderer?> Type1 { get; } = new Dictionary<PdfDictionary, Type1FontRenderer?>(ReferenceEqualityComparer.Instance);

            internal Dictionary<PdfDictionary, RenderableFont?> Std14 { get; } = new Dictionary<PdfDictionary, RenderableFont?>(ReferenceEqualityComparer.Instance);

            internal Dictionary<PdfDictionary, Chuvadi.Pdf.Fonts.PdfFont?> PdfFonts { get; } = new Dictionary<PdfDictionary, Chuvadi.Pdf.Fonts.PdfFont?>(ReferenceEqualityComparer.Instance);
        }

        // Looks the current font up in the shared cache, or resolves it and
        // remembers it there (a font with no dictionary is not shared).
        private T? Shared<T>(Dictionary<PdfDictionary, T?> cache, Func<T?> resolve)
            where T : class
        {
            PdfDictionary? fontDict = ResolveFontDict();
            if (fontDict is null)
            {
                return resolve();
            }

            if (cache.TryGetValue(fontDict, out T? found))
            {
                return found;
            }

            T? value = resolve();
            cache[fontDict] = value;
            return value;
        }

        public Worker(
            PdfObjectStore objects, double hintingScale = 0.0, bool lightHinting = false,
            bool autohintFallback = true, OptionalContentVisibility? visibility = null)
        {
            _visibility = visibility ?? OptionalContentVisibility.AllVisible;
            _objects = objects;
            _reporter = new WarningReporter(objects.OnWarning);
            _hintingScale = hintingScale;
            _lightHinting = lightHinting;
            _autohintFallback = autohintFallback;
            _fontCache = new Dictionary<string, FontRenderer?>();
            _cidToGidCache = new Dictionary<string, int[]?>(StringComparer.Ordinal);
            _pdfFontCache = new Dictionary<string, Chuvadi.Pdf.Fonts.PdfFont?>();
            _std14FontCache = new Dictionary<string, RenderableFont?>();
            _symbolicCache = new Dictionary<string, bool>();
            _type1Cache = new Dictionary<string, Type1FontRenderer?>();
            _widthsCache = new Dictionary<string, (int, double[])?>();
            _type3FontCache = new Dictionary<string, Type3Font?>();
            _type3GlyphCache = new Dictionary<(string, int, uint), PageDisplayList>();
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

        // Builds the display list for a page: the page content stream followed
        // by the page's annotation normal appearances (/AP /N), the way an
        // interactive viewer paints them. Each appearance form is placed onto
        // the annotation /Rect by the §12.5.5 algorithm via a ConcatMatrix, so
        // the shared form-XObject path applies the form's own /Matrix and
        // /Resources on top. Hybrid XFA/AcroForm documents carry their field
        // values in widget appearance streams, so this is what makes those
        // values visible in rasterized output.
        // PDF 32000-1:2008 §12.5.5 — Appearance streams.
        public PageDisplayList BuildFromPage(byte[] content, PdfPage page)
        {
            _reporter.PageNumber = page.PageNumber;
            _resources = page.Resources;
            ContentStreamWalker.Walk(content, this);

            IReadOnlyList<AnnotationAppearance> appearances = Annotations == AnnotationMode.None
                ? Array.Empty<AnnotationAppearance>()
                : PageAnnotationAppearances.Collect(page, _objects, Annotations, GeneratedAppearances.Instance);

            for (int i = 0; i < appearances.Count; i++)
            {
                AnnotationAppearance ap = appearances[i];
                if (IsHiddenByEntry(ap.Annotation))
                {
                    continue;
                }

                SaveState();
                ConcatMatrix(ap.ScaleX, 0, 0, ap.ScaleY, ap.OffsetX, ap.OffsetY);
                EmitFormXObject(ap.Appearance, _resources);
                RestoreState();
            }

            return new PageDisplayList(_ops, page.Width, page.Height);
        }

        // Appends a painting op unless hidden optional content is open.
        private void EmitOp(RenderOp op)
        {
            if (_hiddenDepth == 0)
            {
                // The display list itself can grow large (millions of glyphs
                // or path segments): charge it against the memory limit.
                int segments = op switch
                {
                    FillPathOp fill => fill.Path.Count,
                    StrokePathOp stroke => stroke.Path.Count,
                    DrawGlyphOp glyph => glyph.Path.Count,
                    _ => 0,
                };
                PdfWorkScope.ChargeMemory(256 + (segments * 96L));
                _ops.Add(op);
            }
        }

        // True when a form/image XObject or annotation carries a hidden /OC
        // entry (PDF 32000-1:2008 §8.11.3.3).
        private bool IsHiddenByEntry(PdfDictionary dict) =>
            dict.TryGetValue(PdfName.Intern("OC"), out PdfPrimitive? oc) && !_visibility.IsVisible(oc);

        // ── Marked content (optional content) ─────────────────────────────

        /// <inheritdoc />
        public void BeginMarkedContent(string tag, string? propertyName)
        {
            bool hidden = false;
            if (tag == "OC" && propertyName is not null
                && _resources is not null
                && _resources.TryGetValue(PdfName.Intern("Properties"), out PdfPrimitive? propsPrim)
                && _objects.ResolveAs<PdfDictionary>(propsPrim) is PdfDictionary properties
                && properties.TryGetValue(PdfName.Intern(propertyName), out PdfPrimitive? entry))
            {
                hidden = !_visibility.IsVisible(entry);
            }

            _hiddenStack.Add(hidden);
            if (hidden)
            {
                _hiddenDepth++;
            }
        }

        /// <inheritdoc />
        public void EndMarkedContent()
        {
            if (_hiddenStack.Count == 0)
            {
                return;
            }

            if (_hiddenStack[^1])
            {
                _hiddenDepth--;
            }

            _hiddenStack.RemoveAt(_hiddenStack.Count - 1);
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
            if (_suppressColorOps) { return; }
            _state.FillColor = ColorF.FromGray((float)gray);
            _state.FillValid = true;
            _state.FillColorSpace = null;
            _state.FillPattern = null;
        }

        /// <inheritdoc />
        public void SetStrokeGray(double gray)
        {
            if (_suppressColorOps) { return; }
            _state.StrokeColor = ColorF.FromGray((float)gray);
            _state.StrokeValid = true;
            _state.StrokeColorSpace = null;
            _state.StrokePattern = null;
        }

        /// <inheritdoc />
        public void SetFillRgb(double r, double g, double b)
        {
            if (_suppressColorOps) { return; }
            _state.FillColor = ColorF.FromRgb((float)r, (float)g, (float)b);
            _state.FillValid = true;
            _state.FillColorSpace = null;
            _state.FillPattern = null;
        }

        /// <inheritdoc />
        public void SetStrokeRgb(double r, double g, double b)
        {
            if (_suppressColorOps) { return; }
            _state.StrokeColor = ColorF.FromRgb((float)r, (float)g, (float)b);
            _state.StrokeValid = true;
            _state.StrokeColorSpace = null;
            _state.StrokePattern = null;
        }

        /// <inheritdoc />
        public void SetFillCmyk(double c, double m, double y, double k)
        {
            if (_suppressColorOps) { return; }
            _state.FillColor = ColorF.FromCmyk((float)c, (float)m, (float)y, (float)k);
            _state.FillValid = true;
            _state.FillColorSpace = null;
            _state.FillPattern = null;
        }

        /// <inheritdoc />
        public void SetStrokeCmyk(double c, double m, double y, double k)
        {
            if (_suppressColorOps) { return; }
            _state.StrokeColor = ColorF.FromCmyk((float)c, (float)m, (float)y, (float)k);
            _state.StrokeValid = true;
            _state.StrokeColorSpace = null;
            _state.StrokePattern = null;
        }

        /// <inheritdoc />
        public void SetColorSpace(string name, bool stroke)
        {
            if (_suppressColorOps) { return; }
            // cs / CS selects the active colour space. Device families resolve by
            // name; any other name refers to an entry in the page's
            // /Resources /ColorSpace dictionary (Separation, DeviceN, Indexed,
            // ICCBased, Lab, Cal*). Pattern carries no paintable colour, so paints
            // stay suppressed until a representable colour is set.
            ResolvedColorSpace? space = ResolveColorSpace(name);
            bool valid = space is not null && !space.IsPattern;

            if (stroke)
            {
                _state.StrokeColorSpace = space;
                _state.StrokeValid = valid;
                _state.StrokePattern = null;
            }
            else
            {
                _state.FillColorSpace = space;
                _state.FillValid = valid;
                _state.FillPattern = null;
            }
        }

        private ResolvedColorSpace? ResolveColorSpace(string name)
        {
            // Device and Pattern names resolve directly.
            ResolvedColorSpace? direct = ResolvedColorSpace.Parse(PdfName.Intern(name), _objects);
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

            PdfDictionary? spaces = _objects.ResolveAs<PdfDictionary>(csv);
            if (spaces is null
                || !spaces.TryGetValue(PdfName.Intern(name), out PdfPrimitive? entry)
                || entry is null)
            {
                return null;
            }

            return ResolvedColorSpace.Parse(entry, _objects);
        }

        /// <inheritdoc />
        public void SetColorN(double[] components, bool hasName, bool stroke)
        {
            if (_suppressColorOps) { return; }
            // sc / scn / SC / SCN sets colour in the current colour space. A
            // trailing name operand denotes a pattern, which is not a paintable
            // colour, so validity is cleared.
            if (hasName)
            {
                if (stroke) { _state.StrokeValid = false; } else { _state.FillValid = false; }
                return;
            }

            ResolvedColorSpace? space = stroke ? _state.StrokeColorSpace : _state.FillColorSpace;

            ColorF c;
            if (space is not null && !space.IsPattern)
            {
                // Convert through the resolved space (Separation/DeviceN tint
                // transforms, Indexed lookup, ICCBased alternate, Lab, Cal*).
                double[] rgb = space.ToRgb(components);
                c = ColorF.FromRgb((float)rgb[0], (float)rgb[1], (float)rgb[2]);
            }
            else
            {
                // No explicit space recorded: fall back to the operand count
                // (DeviceGray / DeviceRGB / DeviceCMYK).
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
            }

            if (stroke)
            {
                _state.StrokeColor = c;
                _state.StrokeValid = true;
                _state.StrokePattern = null;
            }
            else
            {
                _state.FillColor = c;
                _state.FillValid = true;
                _state.FillPattern = null;
            }
        }

        /// <inheritdoc />
        public void SetPatternColor(string name, double[] components, bool stroke)
        {
            if (_suppressColorOps) { return; }

            // A pattern is not a flat paintable colour, so flat paints stay
            // suppressed; the pattern itself paints through the fill/stroke
            // operators. The numeric operands before the name are the
            // underlying colour of an uncoloured (PaintType 2) tiling pattern —
            // recorded as the cell replay's starting colour.
            ColorF? underlying = components.Length switch
            {
                1 => ColorF.FromGray((float)components[0]),
                3 => ColorF.FromRgb((float)components[0], (float)components[1], (float)components[2]),
                4 => ColorF.FromCmyk(
                    (float)components[0], (float)components[1],
                    (float)components[2], (float)components[3]),
                _ => null,
            };

            if (stroke)
            {
                _state.StrokeValid = false;
                if (underlying is ColorF strokeUnderlying)
                {
                    _state.StrokePatternColor = strokeUnderlying;
                }

                _state.StrokePattern = ResolvePattern(name);
                return;
            }

            _state.FillValid = false;
            if (underlying is ColorF fillUnderlying)
            {
                _state.FillColor = fillUnderlying;
            }

            _state.FillPattern = ResolvePattern(name);
        }

        private PdfPrimitive? ResolvePattern(string name)
        {
            if (_resources is null
                || !_resources.TryGetValue(PdfName.Intern("Pattern"), out PdfPrimitive? pv))
            {
                return null;
            }

            PdfDictionary? patterns = _objects.ResolveAs<PdfDictionary>(pv);
            if (patterns is null
                || !patterns.TryGetValue(PdfName.Intern(name), out PdfPrimitive? entry))
            {
                return null;
            }

            return _objects.Resolve(entry);
        }

        // ── Path construction ─────────────────────────────────────────────

        /// <inheritdoc />
        public void MoveTo(double x, double y)
        {
            _currentPath.MoveTo(x, y);
            _subpathStart = new PointF(x, y);
            _subpathClosed = false;
        }

        /// <inheritdoc />
        public void LineTo(double x, double y)
        {
            // LineTo on an empty path is illegal in the Path API (it
            // requires a current point). Defend against malformed streams.
            if (_currentPath.IsEmpty)
            {
                _currentPath.MoveTo(x, y);
                _subpathStart = new PointF(x, y);
                _subpathClosed = false;
                return;
            }

            ContinueAfterClose();
            _currentPath.LineTo(x, y);
        }

        /// <inheritdoc />
        public void ClosePath()
        {
            _currentPath.ClosePath();
            _subpathClosed = true;
        }

        /// <inheritdoc />
        public void CurveTo(double x1, double y1, double x2, double y2, double x3, double y3)
        {
            if (_currentPath.IsEmpty)
            {
                SkipWithoutCurrentPoint("c");
                return;
            }

            ContinueAfterClose();
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
                SkipWithoutCurrentPoint("v");
                return;
            }

            ContinueAfterClose();
            _currentPath.CubicBezierTo(
                _currentPath.CurrentPoint,
                new PointF(x2, y2),
                new PointF(x3, y3));
        }

        /// <inheritdoc />
        public void CurveToY(double x1, double y1, double x3, double y3)
        {
            // y x1 y1 x3 y3 — Bezier with final point as second control
            if (_currentPath.IsEmpty)
            {
                SkipWithoutCurrentPoint("y");
                return;
            }

            ContinueAfterClose();
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
            _subpathStart = new PointF(x, y);
            _subpathClosed = true;
        }

        // 3.33.0 (ticket 39, D-332): after h (or re) the current point is the
        // start of the closed subpath (PDF 32000-1:2008 §8.5.2.1, as in
        // PostScript and Poppler), so a following l, c, v or y starts a new
        // subpath there. Path clears its current point on ClosePath, and
        // LineTo/CubicBezierTo then threw "requires a current point".
        private void ContinueAfterClose()
        {
            if (_subpathClosed)
            {
                _currentPath.MoveTo(_subpathStart.X, _subpathStart.Y);
                _subpathClosed = false;
            }
        }

        // 3.34.0 (Phase B test bench N13): image data that ends early is
        // completed with zero samples and drawn, with a warning, as Poppler and
        // MuPDF draw it; before, the image was skipped without a word. Data
        // with nothing in it, or a size past what an array can hold, is left
        // as it is (the caller then skips the image).
        private byte[] PadShortSamples(byte[] samples, long needed)
        {
            if (samples.Length == 0 || samples.Length >= needed || needed > Array.MaxLength)
            {
                return samples;
            }

            PdfWorkScope.ChargeMemory(needed);
            _reporter.Report(
                PdfWarningKind.StreamDamaged,
                "image",
                string.Create(System.Globalization.CultureInfo.InvariantCulture, $"The image data ends early ({samples.Length} of {needed} bytes); the missing samples are drawn as zero."));
            byte[] padded = new byte[needed];
            Buffer.BlockCopy(samples, 0, padded, 0, samples.Length);
            return padded;
        }

        // 3.34.0 (#52a): content cut short by syntax damage.
        public void ContentDamaged(string reason) =>
            _reporter.Report(
                PdfWarningKind.StreamDamaged,
                "content " + (_xobjectSubject ?? "(page)"),
                reason + " What came before is drawn; the rest is left out.");

        // A curve operator with no current point at all (no m before it):
        // skipped, as Poppler does ("No current point"), now with a warning.
        private void SkipWithoutCurrentPoint(string op) =>
            _reporter.Report(
                PdfWarningKind.ContentOperatorSkipped,
                "path operator " + op,
                "A path operator '" + op + "' has no current point (no m before it); it is skipped.");

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

            // /BM — blend mode (PDF §11.3.5). A name or array of names; the first
            // supported separable mode wins, otherwise Normal.
            if (TryReadBlendMode(gs, out PdfBlendMode blendMode))
            {
                _state.BlendMode = blendMode;
            }

            // /SMask — soft mask (PDF §11.6.5.2). /None clears any active mask; a
            // dictionary installs a luminosity or alpha masking group.
            ReadSoftMask(gs);
        }

        private void ReadSoftMask(PdfDictionary gs)
        {
            if (!gs.TryGetValue(PdfName.Intern("SMask"), out PdfPrimitive? smv))
            {
                return;
            }

            PdfPrimitive sm = _objects.Resolve(smv);
            if (sm is PdfName name)
            {
                if (name.Value == "None")
                {
                    _state.SoftMask = null;
                }

                return;
            }

            if (sm is not PdfDictionary smDict)
            {
                return;
            }

            if (!smDict.TryGetValue(PdfName.Intern("G"), out PdfPrimitive? gp)
                || _objects.Resolve(gp) is not PdfStream groupStream)
            {
                return;
            }

            // /S: Luminosity (default) or Alpha.
            bool isLuminosity = smDict.GetName(PdfName.Intern("S"))?.Value != "Alpha";

            // /BC: backdrop colour; first component is the backdrop luminosity for
            // areas the group does not paint (default black = 0 for luminosity).
            double backdrop = 0.0;
            PdfArray? bc = smDict.GetArray(PdfName.Intern("BC"));
            if (bc is not null && bc.Count > 0)
            {
                backdrop = AsDouble(_objects.Resolve(bc[0]));
            }

            PageDisplayList group = BuildSoftMaskGroup(groupStream, out Transform groupMatrix);
            _state.SoftMask = new RasterSoftMaskInfo(
                group, groupMatrix.Multiply(_state.Ctm), isLuminosity, backdrop);
        }

        private PageDisplayList BuildSoftMaskGroup(
            PdfStream groupStream, out Transform groupMatrix)
        {
            groupMatrix = Transform.Identity;
            if (groupStream.Dictionary.TryGetValue(PdfName.Intern("Matrix"), out PdfPrimitive? mp)
                && _objects.ResolveAs<PdfArray>(mp ?? PdfNull.Value) is PdfArray arr
                && arr.Count >= 6)
            {
                groupMatrix = new Transform(
                    AsDouble(arr[0]), AsDouble(arr[1]),
                    AsDouble(arr[2]), AsDouble(arr[3]),
                    AsDouble(arr[4]), AsDouble(arr[5]));
            }

            PdfDictionary? groupResources = _resources;
            if (groupStream.Dictionary.TryGetValue(PdfName.Intern("Resources"), out PdfPrimitive? rp)
                && _objects.ResolveAs<PdfDictionary>(rp ?? PdfNull.Value) is PdfDictionary r)
            {
                groupResources = r;
            }

            if (_nestingDepth >= MaxNestingDepth)
            {
                return new PageDisplayList(new List<RenderOp>(), 0, 0);
            }

            Worker sub = new Worker(_objects, _hintingScale, _lightHinting, _autohintFallback, _visibility);
            sub._nestingDepth = _nestingDepth + 1;
            sub._reporter = _reporter;
            sub._shared = _shared;

            byte[] content;
            try
            {
                content = ContentStreamLoader.Decode(groupStream);
            }
            catch (Exception abortCheck) when (!PdfWorkScope.IsAbort(abortCheck))
            {
                return new PageDisplayList(sub._ops, 0, 0);
            }

            if (content.Length > 0)
            {
                sub._resources = groupResources;

                // 3.34.0: the group paints only inside its /BBox (§8.10.1);
                // outside it the mask takes the backdrop /BC. Before, the
                // group's content was not clipped (found by the pdf.js run,
                // issue7891_bc1: the mask's text showed outside the box).
                double[]? bbox = ReadRectEntry(groupStream.Dictionary, "BBox");
                if (bbox is not null)
                {
                    double x0 = Math.Min(bbox[0], bbox[2]);
                    double y0 = Math.Min(bbox[1], bbox[3]);
                    sub.AppendRectangle(x0, y0, Math.Abs(bbox[2] - bbox[0]), Math.Abs(bbox[3] - bbox[1]));
                    sub.SetClip(evenOdd: false);
                    sub.EndPath();
                }

                ContentStreamWalker.Walk(content, sub);
            }

            return new PageDisplayList(sub._ops, 0, 0);
        }

        private bool TryReadBlendMode(PdfDictionary extGState, out PdfBlendMode mode)
        {
            mode = PdfBlendMode.Normal;
            if (!extGState.TryGetValue(PdfName.Intern("BM"), out PdfPrimitive? bmValue))
            {
                return false;
            }

            PdfPrimitive resolved = _objects.Resolve(bmValue);
            if (resolved is PdfName single)
            {
                mode = BlendModes.FromName(single.Value);
                return true;
            }

            if (resolved is PdfArray array)
            {
                for (int i = 0; i < array.Count; i++)
                {
                    if (_objects.Resolve(array[i]) is PdfName candidate
                        && BlendModes.TryFromName(candidate.Value, out PdfBlendMode m))
                    {
                        mode = m;
                        return true;
                    }
                }

                mode = PdfBlendMode.Normal;
                return true;
            }

            return false;
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

        /// <inheritdoc />
        public void PaintShading(string name)
        {
            if (_resources is null) { return; }
            if (!_resources.TryGetValue(PdfName.Intern("Shading"), out PdfPrimitive? shv)) { return; }
            PdfDictionary? shadings = _objects.ResolveAs<PdfDictionary>(shv);
            if (shadings is null) { return; }
            if (!shadings.TryGetValue(PdfName.Intern(name), out PdfPrimitive? shadingRef)) { return; }

            // The CTM at the sh operator maps shading space to page space.
            TryEmitShadeOp(shadingRef, _state.Ctm, SnapshotClips());
        }

        // True when the transform keeps circles circular (rotation, uniform
        // scale, reflection and translation only).
        private static bool IsConformal(Transform m)
        {
            PointF u = m.TransformVector(1.0, 0.0);
            PointF v = m.TransformVector(0.0, 1.0);
            double l1 = ((double)u.X * u.X) + ((double)u.Y * u.Y);
            double l2 = ((double)v.X * v.X) + ((double)v.Y * v.Y);
            double dot = ((double)u.X * v.X) + ((double)u.Y * v.Y);
            double tol = 1e-6 * Math.Max(l1, l2);
            return Math.Abs(l1 - l2) <= tol && Math.Abs(dot) <= tol;
        }

        // Emits a ShadeOp for a shading dictionary under the given
        // shading-space-to-page transform and clip set. Returns false when the
        // shading type is unsupported (e.g. mesh shadings 4-7) — the paint is
        // skipped rather than failing the whole page.
        private bool TryEmitShadeOp(
            PdfPrimitive shadingRef, Transform ctm, IReadOnlyList<ClipPath>? clips)
        {
            PdfShading shading;
            try
            {
                shading = PdfShading.Parse(shadingRef, _objects);
            }
            catch (ContentException ex)
            {
                _reporter.Report(
                    PdfWarningKind.ShadingSkipped,
                    WarningReporter.Describe("shading", shadingRef),
                    ex.Message + " The paint is skipped.");
                return false;
            }

            // Raster op geometry is baked to page space, so the gradient must
            // be too. Radii scale by the transform's unit-vector length.
            PointF unit = ctm.TransformVector(1.0, 0.0);
            double scale = Math.Sqrt((unit.X * unit.X) + (unit.Y * unit.Y));

            double[] coords = shading.Coords;
            PointF p0 = ctm.TransformPoint(new PointF(coords[0], coords[1]));

            const int sampleCount = 16;
            List<GradientStop> stops = new List<GradientStop>(sampleCount + 1);
            for (int i = 0; i <= sampleCount; i++)
            {
                double t = (double)i / sampleCount;
                (double r, double g, double b) = shading.EvaluateRgb(t);
                stops.Add(new GradientStop(t, ColorF.FromRgb((float)r, (float)g, (float)b)));
            }

            if (shading.IsRadial && !IsConformal(ctm))
            {
                // Circles become ellipses on the page: keep them in shading
                // space and carry the transform.
                EmitOp(new ShadeOp(
                    isRadial: true,
                    x0: coords[0],
                    y0: coords[1],
                    x1: coords[3],
                    y1: coords[4],
                    r0: coords[2],
                    r1: coords[5],
                    extendStart: shading.ExtendStart,
                    extendEnd: shading.ExtendEnd,
                    stops: stops,
                    clips: clips)
                { BlendMode = _state.BlendMode, SoftMask = _state.SoftMask, ShadingToPage = ctm });
            }
            else if (shading.IsRadial)
            {
                PointF p1 = ctm.TransformPoint(new PointF(coords[3], coords[4]));
                EmitOp(new ShadeOp(
                    isRadial: true,
                    x0: p0.X,
                    y0: p0.Y,
                    x1: p1.X,
                    y1: p1.Y,
                    r0: coords[2] * scale,
                    r1: coords[5] * scale,
                    extendStart: shading.ExtendStart,
                    extendEnd: shading.ExtendEnd,
                    stops: stops,
                    clips: clips)
                { BlendMode = _state.BlendMode, SoftMask = _state.SoftMask });
            }
            else
            {
                PointF p1 = ctm.TransformPoint(new PointF(coords[2], coords[3]));
                EmitOp(new ShadeOp(
                    isRadial: false,
                    x0: p0.X,
                    y0: p0.Y,
                    x1: p1.X,
                    y1: p1.Y,
                    r0: 0.0,
                    r1: 0.0,
                    extendStart: shading.ExtendStart,
                    extendEnd: shading.ExtendEnd,
                    stops: stops,
                    clips: clips)
                { BlendMode = _state.BlendMode, SoftMask = _state.SoftMask });
            }

            return true;
        }

        // ── Pattern fills (PDF 32000-1:2008 §8.7.3) ──────────────────────

        // Paints the current path with the current fill pattern: a PatternType
        // 2 (shading) pattern fills the path with its gradient; a PatternType
        // 1 (tiling) pattern replays its cell across the path. The path acts
        // as an additional clip in both cases. The pattern matrix maps pattern
        // space to this content stream's default space (identity in worker
        // coordinates; an enclosing form's placement composes afterwards).
        private void EmitPatternFill(FillRule rule)
        {
            Path device = TransformPath(_currentPath, _state.Ctm);
            EmitPatternPaint(
                _state.FillPattern!, new ClipPath(device, rule), device.EndpointBounds(), _state.FillColor);
        }

        // Paints the current path's stroke with the current stroke pattern:
        // the pattern is clipped to the stroke's outline (a stroke-outline
        // ClipPath expanded by the rasterizer exactly like a stroke op), so a
        // pattern-stroked line shows the pattern where the stroke would paint.
        private void EmitPatternStroke()
        {
            Path device = TransformPath(_currentPath, _state.Ctm);
            StrokeStyle style = BuildStrokeStyle();

            // The tile range must cover the stroke's ink, which extends past the
            // path by up to half the width times the miter limit.
            double margin = (style.Width / 2.0) * Math.Max(1.0, style.MiterLimit) + 1.0;
            RectangleF b = device.EndpointBounds();
            RectangleF bounds = new RectangleF(b.X - margin, b.Y - margin, b.Width + (2 * margin), b.Height + (2 * margin));
            EmitPatternPaint(_state.StrokePattern!, new ClipPath(device, style), bounds, _state.StrokePatternColor);
        }

        private void EmitPatternPaint(PdfPrimitive pattern, ClipPath region, RectangleF regionBounds, ColorF underlying)
        {
            PdfDictionary? dict = pattern as PdfDictionary ?? (pattern as PdfStream)?.Dictionary;
            if (dict is null)
            {
                return;
            }

            List<ClipPath> clips = new List<ClipPath>();
            IReadOnlyList<ClipPath>? active = SnapshotClips();
            if (active is not null)
            {
                clips.AddRange(active);
            }

            clips.Add(region);

            Transform patternMatrix = ReadMatrixEntry(dict, "Matrix");
            int patternType = ReadIntEntry(dict, "PatternType");

            if (patternType == 2)
            {
                if (dict.TryGetValue(PdfName.Intern("Shading"), out PdfPrimitive? shadingRef))
                {
                    TryEmitShadeOp(shadingRef, patternMatrix, clips);
                }

                return;
            }

            if (patternType == 1 && pattern is PdfStream cell)
            {
                EmitTilingPattern(cell, dict, patternMatrix, clips, regionBounds, underlying);
            }
        }

        // Replays a tiling pattern's cell across the filled region: the cell's
        // content stream is built once into a sub-display-list, then placed
        // once per tile via NestedDisplayListOp, clipped to the filled path.
        private void EmitTilingPattern(
            PdfStream cell, PdfDictionary dict, Transform patternMatrix,
            List<ClipPath> clips, RectangleF bounds, ColorF underlying)
        {
            const int MaxTiles = 4096;

            double[]? bbox = ReadRectEntry(dict, "BBox");
            if (bbox is null)
            {
                return;
            }

            double cellWidth = Math.Abs(bbox[2] - bbox[0]);
            double cellHeight = Math.Abs(bbox[3] - bbox[1]);
            double xStep = Math.Abs(ReadDoubleEntry(dict, "XStep", cellWidth));
            double yStep = Math.Abs(ReadDoubleEntry(dict, "YStep", cellHeight));
            if (xStep < 1e-6) { xStep = cellWidth; }
            if (yStep < 1e-6) { yStep = cellHeight; }
            if (xStep < 1e-6 || yStep < 1e-6)
            {
                return;
            }

            // The filled region's bounding box mapped into pattern space picks
            // the tile range. A singular pattern matrix paints nothing.
            Transform inverse;
            try
            {
                inverse = patternMatrix.Invert();
            }
            catch (InvalidOperationException)
            {
                return;
            }

            double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
            Span<PointF> corners = stackalloc PointF[4]
            {
                new PointF(bounds.X, bounds.Y),
                new PointF(bounds.Right, bounds.Y),
                new PointF(bounds.Right, bounds.Top),
                new PointF(bounds.X, bounds.Top),
            };
            foreach (PointF corner in corners)
            {
                PointF ps = inverse.TransformPoint(corner);
                minX = Math.Min(minX, ps.X);
                minY = Math.Min(minY, ps.Y);
                maxX = Math.Max(maxX, ps.X);
                maxY = Math.Max(maxY, ps.Y);
            }

            int i0 = (int)Math.Floor((minX - bbox[0] - cellWidth) / xStep) + 1;
            int i1 = (int)Math.Ceiling((maxX - bbox[0]) / xStep);
            int j0 = (int)Math.Floor((minY - bbox[1] - cellHeight) / yStep) + 1;
            int j1 = (int)Math.Ceiling((maxY - bbox[1]) / yStep);

            // Counted in floating point so absurd step/region ratios cannot
            // overflow; above MaxTiles the fill is repeated by the painter.
            double tiles =
                (Math.Ceiling((maxX - bbox[0]) / xStep) - Math.Floor((minX - bbox[0] - cellWidth) / xStep))
                * (Math.Ceiling((maxY - bbox[1]) / yStep) - Math.Floor((minY - bbox[1] - cellHeight) / yStep));
            if (!double.IsFinite(tiles) || tiles <= 0)
            {
                return;
            }

            bool repeat = tiles > MaxTiles;

            // Build the cell sub-list once. An uncoloured pattern (PaintType 2)
            // paints with the underlying colour recorded by SetPatternColor.
            if (_nestingDepth >= MaxNestingDepth)
            {
                return;
            }

            Worker sub = new Worker(_objects, _hintingScale, _lightHinting, _autohintFallback, _visibility);
            sub._nestingDepth = _nestingDepth + 1;
            sub._reporter = _reporter;
            sub._shared = _shared;
            sub._state.FillAlpha = _state.FillAlpha;
            sub._state.StrokeAlpha = _state.StrokeAlpha;
            sub._groupAlpha = _groupAlpha;
            if (ReadIntEntry(dict, "PaintType") == 2)
            {
                sub._state.FillColor = underlying;
                sub._state.StrokeColor = underlying;
                sub._suppressColorOps = true;
            }

            byte[] content;
            try
            {
                content = ContentStreamLoader.Decode(cell, _objects);
            }
            catch (Exception abortCheck) when (!PdfWorkScope.IsAbort(abortCheck))
            {
                return;
            }

            PdfDictionary? cellResources = null;
            if (cell.Dictionary.TryGetValue(PdfName.Intern("Resources"), out PdfPrimitive? resPrim))
            {
                cellResources = _objects.ResolveAs<PdfDictionary>(resPrim ?? PdfNull.Value);
            }

            sub._resources = cellResources ?? _resources;
            if (content.Length > 0)
            {
                ContentStreamWalker.Walk(content, sub);
            }

            if (sub._ops.Count == 0)
            {
                return;
            }

            PageDisplayList inner = new PageDisplayList(sub._ops, 0, 0);
            if (repeat)
            {
                EmitOp(new TilingFillOp(
                    inner, patternMatrix, xStep, yStep, bbox[0], bbox[1], cellWidth, cellHeight, bounds, clips)
                {
                    BlendMode = _state.BlendMode,
                    SoftMask = _state.SoftMask,
                });
                return;
            }

            for (int j = j0; j <= j1; j++)
            {
                for (int i = i0; i <= i1; i++)
                {
                    Transform composition = Transform
                        .CreateTranslation(i * xStep, j * yStep)
                        .Multiply(patternMatrix);
                    EmitOp(new NestedDisplayListOp(inner, composition, clips)
                    {
                        BlendMode = _state.BlendMode,
                        SoftMask = _state.SoftMask,
                    });
                }
            }
        }

        private Transform ReadMatrixEntry(PdfDictionary dict, string key)
        {
            if (dict.TryGetValue(PdfName.Intern(key), out PdfPrimitive? value)
                && _objects.Resolve(value) is PdfArray arr
                && arr.Count >= 6)
            {
                return new Transform(
                    AsDouble(arr[0]), AsDouble(arr[1]),
                    AsDouble(arr[2]), AsDouble(arr[3]),
                    AsDouble(arr[4]), AsDouble(arr[5]));
            }

            return Transform.Identity;
        }

        private double ReadDoubleEntry(PdfDictionary dict, string key, double fallback)
        {
            if (dict.TryGetValue(PdfName.Intern(key), out PdfPrimitive? value))
            {
                PdfPrimitive resolved = _objects.Resolve(value);
                if (resolved is PdfInteger i) { return i.Value; }
                if (resolved is PdfReal r) { return r.Value; }
            }

            return fallback;
        }

        private double[]? ReadRectEntry(PdfDictionary dict, string key)
        {
            if (dict.TryGetValue(PdfName.Intern(key), out PdfPrimitive? value)
                && _objects.Resolve(value) is PdfArray arr
                && arr.Count >= 4)
            {
                return new double[]
                {
                    AsDouble(arr[0]), AsDouble(arr[1]), AsDouble(arr[2]), AsDouble(arr[3]),
                };
            }

            return null;
        }

        // Paint colours with the constant ExtGState alpha (/ca, /CA) folded in.
        private ColorF EffectiveFill => ApplyAlpha(_state.FillColor, _state.FillAlpha * _groupAlpha);

        private ColorF EffectiveStroke => ApplyAlpha(_state.StrokeColor, _state.StrokeAlpha * _groupAlpha);

        // 3.34.0: the constant alpha (/ca) in effect where a transparency-group
        // form is drawn (§11.6.6), applied to everything the group paints. The
        // group's own content starts at alpha 1, so an /ca inside it does not
        // undo the outer one. This multiplies each painted object, which is
        // exact unless objects inside the group overlap (a full group would
        // be composited off-screen first). Before, a group drawn at /ca 0.3
        // whose content set /ca 1 painted fully opaque (found with the
        // Phase B test-bench file safedocs-0206).
        private double _groupAlpha = 1.0;

        private static ColorF ApplyAlpha(ColorF color, double alpha)
        {
            if (alpha >= 1.0) { return color; }
            double clamped = alpha < 0.0 ? 0.0 : alpha;
            ColorF rgb = color.ToRgb();
            return ColorF.FromRgb(rgb.R, rgb.G, rgb.B, rgb.Alpha * (float)clamped);
        }

        private void OpFill(FillRule rule)
        {
            if (!_currentPath.IsEmpty && _state.FillPattern is not null)
            {
                EmitPatternFill(rule);
            }
            else if (_state.FillValid && !_currentPath.IsEmpty)
            {
                Path transformed = TransformPath(_currentPath, _state.Ctm);
                EmitOp(new FillPathOp(transformed, EffectiveFill, rule, SnapshotClips()) { BlendMode = _state.BlendMode, SoftMask = _state.SoftMask });
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

            if (!_currentPath.IsEmpty && _state.StrokePattern is not null)
            {
                EmitPatternStroke();
            }
            else if (_state.StrokeValid && !_currentPath.IsEmpty)
            {
                Path transformed = TransformPath(_currentPath, _state.Ctm);
                StrokeStyle style = BuildStrokeStyle();
                EmitOp(new StrokePathOp(transformed, style, SnapshotClips()) { BlendMode = _state.BlendMode, SoftMask = _state.SoftMask, Pen = Pen(), UserWidth = _state.LineWidth, UserDash = _state.DashPattern, UserDashOffset = _state.DashOffset });
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

                if (_state.FillPattern is not null)
                {
                    EmitPatternFill(rule);
                }
                else if (_state.FillValid)
                {
                    EmitOp(new FillPathOp(transformed, EffectiveFill, rule, snapshot) { BlendMode = _state.BlendMode, SoftMask = _state.SoftMask });
                }

                if (_state.StrokePattern is not null)
                {
                    EmitPatternStroke();
                }
                else if (_state.StrokeValid)
                {
                    StrokeStyle style = BuildStrokeStyle();
                    EmitOp(new StrokePathOp(transformed, style, snapshot) { BlendMode = _state.BlendMode, SoftMask = _state.SoftMask, Pen = Pen(), UserWidth = _state.LineWidth, UserDash = _state.DashPattern, UserDashOffset = _state.DashOffset });
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

        // The stroke style in page space. The path is transformed by the CTM
        // before it is emitted, so the line width and dash lengths (user-space
        // values) are scaled by the CTM's linear scale too — sqrt(|det|), exact
        // for uniform scales and rotations, the area-preserving average for a
        // non-uniform scale. Without it a "0.1 0 0 0.1 0 0 cm 40 w" line (as
        // Ghostscript writes) drew 40 pt wide instead of 4.
        private StrokeStyle BuildStrokeStyle()
        {
            Transform ctm = _state.Ctm;
            double scale = Math.Sqrt(Math.Abs((ctm.A * ctm.D) - (ctm.B * ctm.C)));
            if (!double.IsFinite(scale))
            {
                scale = 1.0;
            }

            double[] dashes = _state.DashPattern;
            if (dashes.Length > 0 && scale != 1.0)
            {
                double[] scaled = new double[dashes.Length];
                for (int i = 0; i < dashes.Length; i++)
                {
                    scaled[i] = dashes[i] * scale;
                }

                dashes = scaled;
            }

            return new StrokeStyle
            {
                Width = _state.LineWidth * scale,
                Cap = _state.LineCap,
                Join = _state.LineJoin,
                MiterLimit = _state.MiterLimit,
                DashPattern = dashes,
                DashOffset = _state.DashOffset * scale,
                Color = EffectiveStroke,
            };
        }

        // 3.34.0 (#23): the pen of a stroke, the CTM's linear part (see
        // StrokePathOp.Pen); the stroke ops also carry the user-space width
        // and dash.
        private Transform Pen()
        {
            Transform ctm = _state.Ctm;
            return new Transform(ctm.A, ctm.B, ctm.C, ctm.D, 0, 0);
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
            _state.Type3 = GetType3Font(name);
        }

        // Resolves and caches the Type 3 font for a resource name, or null when
        // the named font is not a Type 3 font.
        private Type3Font? GetType3Font(string name)
        {
            if (string.IsNullOrEmpty(name) || _resources is null)
            {
                return null;
            }

            string cacheKey = name;
            if (_type3FontCache.TryGetValue(cacheKey, out Type3Font? cached))
            {
                return cached;
            }

            Type3Font? font = null;
            if (_resources.TryGetValue(PdfName.Intern("Font"), out PdfPrimitive? fontsRef)
                && _objects.ResolveAs<PdfDictionary>(fontsRef ?? PdfNull.Value) is PdfDictionary fonts
                && fonts.TryGetValue(PdfName.Intern(name), out PdfPrimitive? fontRef)
                && _objects.ResolveAs<PdfDictionary>(fontRef ?? PdfNull.Value) is PdfDictionary fontDict)
            {
                font = Type3Font.FromDictionary(fontDict, _objects);
            }

            _type3FontCache[cacheKey] = font;
            return font;
        }

        // Seeds the worker for rendering one Type 3 glyph: the inherited text
        // fill colour (used by uncoloured d1 glyphs) is installed as the fill.
        internal void SeedType3Fill(ColorF fill)
        {
            _state.FillColor = fill;
            _state.FillValid = true;
            _state.FillColorSpace = null;
        }

        /// <inheritdoc />
        public void SetGlyphWidth(double wx, double wy)
        {
            // d0 — coloured glyph; width comes from /Widths, colour from the glyph.
        }

        /// <inheritdoc />
        public void SetGlyphWidthAndBBox(
            double wx, double wy, double llx, double lly, double urx, double ury)
        {
            // d1 — uncoloured glyph: ignore colour operators, paint with text colour.
            _suppressColorOps = true;
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

            if (_state.Type3 is Type3Font type3)
            {
                ShowTextType3(raw, type3);
                return;
            }

            // Rendering mode 3 = invisible; skip emission but still advance.
            bool emit = _state.TextRenderingMode != 3;

            FontRenderer? renderer = GetFontRenderer();

            // Non-embedded Standard-14 fonts (Helvetica/Times/Courier/Symbol/
            // ZapfDingbats) carry no font program; their glyph outlines come
            // from the embedded substitute bundle via RenderableFont.
            // Embedded Type1 (FontFile) programs, parsed separately from the
            // TrueType path. 3.34.0 (Phase B N5): tried before the standard-14
            // outlines, so an embedded font with a standard-14 name (an Adobe
            // "Times-Roman" subset) draws its own glyphs, not the substitute.
            Type1FontRenderer? type1 = renderer is null ? GetType1Font() : null;
            RenderableFont? std14 = renderer is null && type1 is null ? GetStd14Font() : null;

            Chuvadi.Pdf.Fonts.PdfFont? pdfFont = GetPdfFont();
            bool symbolic = IsSymbolicFont();

            // Simple fonts use single-byte codes: one byte = one code = one glyph.
            foreach (byte b in raw)
            {
                // Each glyph shown counts toward page complexity (and checks
                // cancellation): one Tj can carry millions of them.
                PdfWorkScope.CountOperators();
                int code = b;
                double advance;

                if (renderer is not null)
                {
                    int gid = ResolveEmbeddedGid(renderer, pdfFont, code, symbolic);
                    GlyphOutline glyph = SafeOutline(renderer, gid);
                    GlyphOutline? hintedGlyph = TryHint(renderer, gid, out double hintPixels);
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
                        EmitOp(new DrawGlyphOp(placed, EffectiveFill, SnapshotClips()) { BlendMode = _state.BlendMode, SoftMask = _state.SoftMask });
                    }

                    double programAdvance = hintedGlyph is not null && !_lightHinting
                        ? hintedGlyph.Metrics.AdvanceWidth / hintPixels
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
                        EmitOp(new DrawGlyphOp(placed, EffectiveFill, SnapshotClips()) { BlendMode = _state.BlendMode, SoftMask = _state.SoftMask });
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
                        EmitOp(new DrawGlyphOp(placed, EffectiveFill, SnapshotClips()) { BlendMode = _state.BlendMode, SoftMask = _state.SoftMask });
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

        // Renders a run of single-byte codes in a Type 3 font. Each glyph's
        // CharProc is built once (cached) in glyph space, then painted via a
        // NestedDisplayListOp whose composition maps glyph space to page space:
        // FontMatrix · textScale · TextMatrix · CTM.
        private void ShowTextType3(byte[] raw, Type3Font type3)
        {
            bool emit = _state.TextRenderingMode != 3;
            double th = _state.HorizontalScaling / 100.0;

            Transform fontMatrix = new Transform(
                type3.FontMatrix[0], type3.FontMatrix[1], type3.FontMatrix[2],
                type3.FontMatrix[3], type3.FontMatrix[4], type3.FontMatrix[5]);

            foreach (byte b in raw)
            {
                PdfWorkScope.CountOperators();
                int code = b;
                double advance = 0.0;

                if (type3.TryGetGlyph(code, out Type3Glyph glyph))
                {
                    if (emit && _state.FillValid && glyph.Content.Length > 0)
                    {
                        PageDisplayList inner = GetOrBuildType3Glyph(type3, code, glyph);
                        if (inner.Ops.Count > 0)
                        {
                            Transform textScale = new Transform(
                                _state.FontSize * th, 0, 0, _state.FontSize, 0, _state.TextRise);
                            Transform composition = fontMatrix
                                .Multiply(textScale)
                                .Multiply(_textMatrix)
                                .Multiply(_state.Ctm);
                            EmitOp(new NestedDisplayListOp(inner, composition, SnapshotClips())
                            {
                                BlendMode = _state.BlendMode,
                                SoftMask = _state.SoftMask,
                            });
                        }
                    }

                    advance = glyph.Width * type3.FontMatrix[0] * _state.FontSize;
                }

                double extra = _state.CharacterSpacing;
                if (code == 32)
                {
                    extra += _state.WordSpacing;
                }

                double tx = (advance + extra) * th;
                Transform advanceMatrix = new Transform(1, 0, 0, 1, tx, 0);
                _textMatrix = advanceMatrix.Multiply(_textMatrix);
            }
        }

        // Builds (or returns a cached) glyph-space sub-display-list for one Type 3
        // glyph. Keyed by font, code, and the fill colour (which an uncoloured d1
        // glyph bakes in), so cached entries stay correct across colour changes.
        private PageDisplayList GetOrBuildType3Glyph(Type3Font type3, int code, Type3Glyph glyph)
        {
            ColorF fill = EffectiveFill;
            (string, int, uint) key = (_state.FontName ?? string.Empty, code, fill.ToArgb32());
            if (_type3GlyphCache.TryGetValue(key, out PageDisplayList? cached))
            {
                return cached;
            }

            if (_nestingDepth >= MaxNestingDepth)
            {
                return new PageDisplayList(new List<RenderOp>(), 0, 0);
            }

            Worker sub = new Worker(_objects, _hintingScale, _lightHinting, _autohintFallback, _visibility);
            sub._nestingDepth = _nestingDepth + 1;
            sub._reporter = _reporter;
            sub._shared = _shared;
            sub._resources = type3.Resources ?? _resources;
            sub.SeedType3Fill(fill);

            ContentStreamWalker.Walk(glyph.Content, sub);

            PageDisplayList inner = new PageDisplayList(sub._ops, 0, 0);
            _type3GlyphCache[key] = inner;
            return inner;
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

        // The shared code-to-glyph rules (3.31, Fonts.Rendering EmbeddedGlyphs).
        private static int ResolveEmbeddedGid(
            FontRenderer renderer, Chuvadi.Pdf.Fonts.PdfFont? pdfFont, int code, bool symbolic) =>
            EmbeddedGlyphs.ForSimpleCode(renderer, pdfFont, code, symbolic);

        // Decodes a single content-stream code to the character whose glyph a
        // substitute (Standard-14) font draws. 3.34.0 (#37): the font's
        // encoding (its glyph name) comes first, as for embedded TrueType
        // fonts (D-175) and as Poppler and MuPDF do; ToUnicode, which is for
        // extraction, is used only when the encoding gives no character.
        // Falls back to Latin-1 interpretation of the raw byte.
        private static char DecodeOneChar(Chuvadi.Pdf.Fonts.PdfFont? pdfFont, int code)
        {
            if (pdfFont is not null)
            {
                char byEncoding = pdfFont.GetEncodingCharacter(code);
                if (byEncoding != '\0')
                {
                    return byEncoding;
                }

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
        // (Identity-H) and are CIDs. A CID selects its glyph through the
        // descendant's /CIDToGIDMap stream (CIDFontType2) or the CFF charset
        // of a CID-keyed program (CIDFontType0); otherwise the CID is the GID.
        private void ShowTextComposite(byte[] raw)
        {
            if (raw.Length < 2 || _state.FontSize <= 0)
            {
                return;
            }

            bool emit = _state.TextRenderingMode != 3;
            FontRenderer? renderer = GetFontRenderer();
            int[]? cidToGid = renderer is null ? null : GetCidToGidMap();

            // 3.30 (D-105): a Type 0 font with no font program is drawn with a
            // standard 14 substitute, each code decoded through ToUnicode and
            // placed by the descendant's /W widths. Before, it drew nothing.
            if (renderer is null && GetStd14Font() is RenderableFont substitute)
            {
                ShowTextCompositeSubstitute(raw, substitute, emit);
                return;
            }

            for (int i = 0; i + 1 < raw.Length; i += 2)
            {
                PdfWorkScope.CountOperators();
                int cid = (raw[i] << 8) | raw[i + 1];
                int code = EmbeddedGlyphs.ForCid(renderer, cidToGid, cid);
                double advance;

                if (renderer is null)
                {
                    advance = 0.5 * _state.FontSize;
                }
                else
                {
                    GlyphOutline glyph = SafeOutline(renderer, code);
                    GlyphOutline? hintedGlyph = TryHint(renderer, code, out double hintPixels);
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
                        EmitOp(new DrawGlyphOp(placed, EffectiveFill, SnapshotClips()) { BlendMode = _state.BlendMode, SoftMask = _state.SoftMask });
                    }

                    advance = hintedGlyph is not null && !_lightHinting
                        ? hintedGlyph.Metrics.AdvanceWidth / hintPixels
                        : glyph.Metrics.AdvanceWidthAt(_state.FontSize);
                }

                double tx = (advance + _state.CharacterSpacing) * (_state.HorizontalScaling / 100.0);
                Transform advanceMatrix = new Transform(1, 0, 0, 1, tx, 0);
                _textMatrix = advanceMatrix.Multiply(_textMatrix);
            }
        }

        private void ShowTextCompositeSubstitute(byte[] raw, RenderableFont substitute, bool emit)
        {
            Chuvadi.Pdf.Fonts.PdfFont? pdfFont = GetPdfFont();
            (Dictionary<int, double> Widths, double Default) widths = GetCompositeWidths();
            int count = raw.Length / 2;
            int[] cids = new int[count];
            string[] texts = new string[count];
            for (int k = 0; k < count; k++)
            {
                cids[k] = (raw[2 * k] << 8) | raw[(2 * k) + 1];
                texts[k] = pdfFont?.DecodeCode(cids[k]) ?? string.Empty;
            }

            int i = 0;
            while (i < count)
            {
                PdfWorkScope.CountOperators();

                // Indic text (D-100): the run of codes in one script is shaped
                // with the bundled LiPi Sans face for it and drawn from the
                // run's start; the codes still advance by their /W widths.
                LipiScript script = ScriptOf(texts[i]);
                if (script != LipiScript.Latin)
                {
                    int end = i + 1;
                    while (end < count && (ScriptOf(texts[end]) == script || IsJoiner(texts[end])))
                    {
                        end++;
                    }

                    StringBuilder run = new StringBuilder();
                    for (int k = i; k < end; k++)
                    {
                        run.Append(texts[k]);
                    }

                    if (emit && _state.FillValid)
                    {
                        DrawBundledRun(run.ToString(), script, IsBoldSubstitute(substitute));
                    }

                    for (int k = i; k < end; k++)
                    {
                        AdvanceByWidth(cids[k], widths);
                    }

                    i = end;
                    continue;
                }

                string text = texts[i];
                if (text.Length > 0 && emit && _state.FillValid)
                {
                    Path glyphPath = substitute.GetGlyphPath(text[0], _state.FontSize);
                    if (!glyphPath.IsEmpty)
                    {
                        EmitGlyphPath(glyphPath);
                    }
                    else if (!char.IsWhiteSpace(text[0]))
                    {
                        ReportMissingGlyph(text);
                    }
                }

                AdvanceByWidth(cids[i], widths);
                i++;
            }
        }

        private void AdvanceByWidth(int cid, (Dictionary<int, double> Widths, double Default) widths)
        {
            double advance = (widths.Widths.TryGetValue(cid, out double w) ? w : widths.Default) / 1000.0 * _state.FontSize;

            // Word spacing applies to single-byte code 32 only (§9.3.3).
            double tx = (advance + _state.CharacterSpacing) * (_state.HorizontalScaling / 100.0);
            _textMatrix = new Transform(1, 0, 0, 1, tx, 0).Multiply(_textMatrix);
        }

        private void EmitGlyphPath(Path glyphPath)
        {
            Transform glyphPlacement = _textMatrix.Multiply(_state.Ctm);
            if (_state.TextRise != 0.0)
            {
                glyphPlacement = new Transform(1, 0, 0, 1, 0, _state.TextRise).Multiply(glyphPlacement);
            }

            EmitOp(new DrawGlyphOp(TransformPath(glyphPath, glyphPlacement), EffectiveFill, SnapshotClips()) { BlendMode = _state.BlendMode, SoftMask = _state.SoftMask });
        }

        private void ReportMissingGlyph(string text)
        {
            _reporter.Report(
                PdfWarningKind.GlyphSkipped,
                "font /" + _state.FontName + " characters",
                "the substitute for this font that is not embedded has no glyph for some of its characters (for example U+"
                    + char.ConvertToUtf32(text, 0).ToString("X4", System.Globalization.CultureInfo.InvariantCulture) + "); they are not drawn");
        }

        // The standard 14 substitute chosen for a font that is not embedded
        // carries the font's weight (Standard14Substitute decides it from the
        // name, flags and /FontWeight), so it also picks the LiPi weight.
        private static bool IsBoldSubstitute(RenderableFont substitute) =>
            substitute.FontName.Contains("Bold", StringComparison.Ordinal);

        // Shapes a run of one Indic script with the LiPi Sans face for it (Bold
        // for bold text, D-152) and draws the glyphs at their natural advances
        // from the current position.
        private void DrawBundledRun(string text, LipiScript script, bool bold)
        {
            LipiFace? face = LipiFace.For(script, bold);
            if (face is null || text.Length == 0)
            {
                ReportMissingGlyph(text.Length > 0 ? text : " ");
                return;
            }

            IReadOnlyList<ShapedGlyph> glyphs;
            try
            {
                glyphs = TextShaper.Shape(face.Face, text, script);
            }
            catch (Exception ex) when (!PdfWorkScope.IsAbort(ex))
            {
                ReportMissingGlyph(text);
                return;
            }

            double scale = _state.FontSize / 1000.0;
            double th = _state.HorizontalScaling / 100.0;
            double pen = 0.0;
            Transform start = _textMatrix;
            foreach (ShapedGlyph glyph in glyphs)
            {
                GlyphOutline outline = SafeOutline(face.Renderer, glyph.GlyphId).Scale(_state.FontSize);
                if (!outline.IsEmpty)
                {
                    Transform at = new Transform(1, 0, 0, 1, (pen + glyph.XOffset) * scale * th, glyph.YOffset * scale).Multiply(start);
                    Transform glyphPlacement = at.Multiply(_state.Ctm);
                    if (_state.TextRise != 0.0)
                    {
                        glyphPlacement = new Transform(1, 0, 0, 1, 0, _state.TextRise).Multiply(glyphPlacement);
                    }

                    EmitOp(new DrawGlyphOp(TransformPath(outline.Outline, glyphPlacement), EffectiveFill, SnapshotClips()) { BlendMode = _state.BlendMode, SoftMask = _state.SoftMask });
                }

                pen += glyph.XAdvance;
            }
        }

        // The bundled script a decoded code's text belongs to: Latin (drawn
        // with the standard 14 substitute) unless its first character is in
        // one of the Indic scripts the bundled faces cover.
        private static LipiScript ScriptOf(string text) =>
            text.Length == 0 || IsJoiner(text) ? LipiScript.Latin : ScriptClassifier.Classify(char.ConvertToUtf32(text, 0));

        // Zero-width (non-)joiners shape with the run around them.
        private static bool IsJoiner(string text) => text.Length == 1 && (text[0] == '\u200C' || text[0] == '\u200D');

        // The current Type 0 font's descendant /W widths by CID and its /DW
        // (1000 when absent), in glyph-space thousandths (§9.7.4.3).
        private (Dictionary<int, double> Widths, double Default) GetCompositeWidths()
        {
            string key = _state.FontName ?? string.Empty;
            if (_compositeWidthsCache.TryGetValue(key, out (Dictionary<int, double>, double) cached))
            {
                return cached;
            }

            Dictionary<int, double> widths = new Dictionary<int, double>();
            double fallback = 1000.0;
            PdfDictionary? descendant = CurrentDescendantFont();
            if (descendant is not null)
            {
                if (descendant.TryGetValue(PdfName.Intern("DW"), out PdfPrimitive? dw) && dw is not null)
                {
                    fallback = _objects.Resolve(dw) switch { PdfInteger n => n.Value, PdfReal r => r.Value, _ => fallback };
                }

                if (descendant.TryGetValue(PdfName.Intern("W"), out PdfPrimitive? wRef) && wRef is not null
                    && _objects.Resolve(wRef) is PdfArray w)
                {
                    ReadCidWidths(w, widths);
                }
            }

            (Dictionary<int, double>, double) result = (widths, fallback);
            _compositeWidthsCache[key] = result;
            return result;
        }

        // /W entries: "c [w1 w2 …]" or "cFirst cLast w".
        private void ReadCidWidths(PdfArray w, Dictionary<int, double> widths)
        {
            int i = 0;
            while (i + 1 < w.Count)
            {
                if (_objects.Resolve(w[i]) is not PdfInteger first)
                {
                    break;
                }

                PdfPrimitive next = _objects.Resolve(w[i + 1]);
                if (next is PdfArray list)
                {
                    for (int k = 0; k < list.Count && k < 65536; k++)
                    {
                        widths[first.Value + k] = Number(_objects.Resolve(list[k]));
                    }

                    i += 2;
                }
                else if (next is PdfInteger last && i + 2 < w.Count)
                {
                    double width = Number(_objects.Resolve(w[i + 2]));
                    for (int c = first.Value; c <= last.Value && c - first.Value < 65536; c++)
                    {
                        widths[c] = width;
                    }

                    i += 3;
                }
                else
                {
                    break;
                }
            }
        }

        private static double Number(PdfPrimitive value) => value switch
        {
            PdfInteger n => n.Value,
            PdfReal r => r.Value,
            _ => 0.0,
        };

        private PdfDictionary? CurrentDescendantFont()
        {
            PdfDictionary? resources = _state.FontResources;
            if (resources is null || string.IsNullOrEmpty(_state.FontName)
                || !resources.TryGetValue(PdfName.Intern("Font"), out PdfPrimitive? fontsRef) || fontsRef is null
                || _objects.ResolveAs<PdfDictionary>(fontsRef) is not PdfDictionary fonts
                || !fonts.TryGetValue(PdfName.Intern(_state.FontName), out PdfPrimitive? fontRef) || fontRef is null
                || _objects.ResolveAs<PdfDictionary>(fontRef) is not PdfDictionary font
                || !font.TryGetValue(PdfName.Intern("DescendantFonts"), out PdfPrimitive? dfRef) || dfRef is null
                || _objects.ResolveAs<PdfArray>(dfRef) is not { Count: > 0 } descendants)
            {
                return null;
            }

            return _objects.ResolveAs<PdfDictionary>(descendants[0]);
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
        // A glyph whose outline data is damaged draws nothing (and advances by
        // the font's /Widths) instead of failing the whole page.
        private GlyphOutline SafeOutline(FontRenderer renderer, int glyphId)
        {
            try
            {
                return renderer.GetGlyphOutline(glyphId);
            }
            catch (Exception ex) when (!PdfWorkScope.IsAbort(ex))
            {
                _reporter.Report(
                    PdfWarningKind.GlyphSkipped,
                    "font /" + _state.FontName,
                    "the outline of glyph " + glyphId.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    " is damaged (" + ex.Message + "); damaged glyphs in this font draw nothing");
                return new GlyphOutline(new Path(), new GlyphMetrics(0, 0, 1000, default));
            }
        }

        // Grid-fits one glyph. Ordinary text is hinted at Tf, as in 3.33.0.
        // When the text matrix and the CTM scale it by more than 2 or less
        // than 0.5 (a page that sets 1 Tf and scales with Tm, say), it is
        // hinted at the size it is really drawn instead, not at one pixel
        // (D-377). A rotated, skewed or unevenly scaled matrix is not
        // hinted, because the pixel grid no longer lines up with the glyph.
        // The outline comes back in glyph space at Tf, and pixelsPerUnit is
        // what to divide the hinted advance by.
        // Within this range of text-matrix-and-CTM scale, text is hinted at
        // Tf (3.33.0's size); outside it, at the drawn size (D-377).
        private const double MinTfHintingScale = 0.5;
        private const double MaxTfHintingScale = 2.0;

        private GlyphOutline? TryHint(FontRenderer renderer, int glyphId, out double pixelsPerUnit)
        {
            pixelsPerUnit = 0.0;
            if (_hintingScale <= 0.0)
            {
                return null;
            }

            double drawScale = UniformTextScale(_textMatrix.Multiply(_state.Ctm));
            if (drawScale <= 0.0)
            {
                return null;
            }

            double sizeScale = drawScale > MaxTfHintingScale || drawScale < MinTfHintingScale ? drawScale : 1.0;
            int ppem = (int)Math.Round(_state.FontSize * sizeScale * _hintingScale);

            if (ppem <= 0)
            {
                return null;
            }

            GlyphOutline? hinted;
            try
            {
                hinted = renderer.GetHintedGlyphOutline(glyphId, ppem, _lightHinting, _autohintFallback);
            }
            catch (Exception ex) when (!PdfWorkScope.IsAbort(ex))
            {
                return null;
            }

            if (hinted is null)
            {
                return null;
            }

            // Device pixels -> glyph space at Tf, so the text matrix, the CTM
            // and the painter's device scale restore the fitted positions
            // instead of compounding them.
            pixelsPerUnit = sizeScale * _hintingScale;
            Transform toGlyphSpace = Transform.CreateScale(1.0 / pixelsPerUnit);
            Path glyphSpace = TransformPath(hinted.Outline, toGlyphSpace);
            return new GlyphOutline(glyphSpace, hinted.Metrics);
        }

        // The vertical scale of a text-to-user matrix when it is an even
        // scale (with or without a mirror), or 0 when it rotates, skews or
        // stretches. A stretch of up to 2% still counts as even: Word and
        // other office writers set text with matrices such as
        // [0.9981 0 0 1], and the grid then drifts by at most 2% of the em.
        private static double UniformTextScale(Transform m)
        {
            double sx = Math.Abs(m.A);
            double sy = Math.Abs(m.D);
            double largest = Math.Max(sx, sy);
            if (largest <= 0.0 || double.IsNaN(largest) || double.IsInfinity(largest))
            {
                return 0.0;
            }

            double skewTolerance = largest * 1e-3;
            if (Math.Abs(m.B) > skewTolerance || Math.Abs(m.C) > skewTolerance)
            {
                return 0.0;
            }

            if (Math.Abs(sx - sy) > largest * 0.02)
            {
                return 0.0;
            }

            return sy;
        }

        // The current composite font's /CIDToGIDMap stream as CID → GID, or
        // null when it has none (or it is /Identity).
        private int[]? GetCidToGidMap()
        {
            if (string.IsNullOrEmpty(_state.FontName))
            {
                return null;
            }

            if (_cidToGidCache.TryGetValue(_state.FontName, out int[]? cached))
            {
                return cached;
            }

            int[]? map = null;
            PdfDictionary? font = ResolveFontDict();
            if (font is not null
                && font.TryGetValue(PdfName.Intern("DescendantFonts"), out PdfPrimitive? dfRef)
                && _objects.ResolveAs<PdfArray>(dfRef ?? PdfNull.Value) is PdfArray descendants
                && descendants.Count > 0
                && _objects.ResolveAs<PdfDictionary>(descendants[0]) is PdfDictionary cidFont
                && ExtractStreamBytes(cidFont, "CIDToGIDMap") is byte[] bytes)
            {
                map = new int[bytes.Length / 2];
                for (int k = 0; k < map.Length; k++)
                {
                    map[k] = (bytes[2 * k] << 8) | bytes[(2 * k) + 1];
                }
            }

            _cidToGidCache[_state.FontName] = map;
            return map;
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

            FontRenderer? renderer = Shared(_shared.Renderers, ResolveFontRenderer);
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

            RenderableFont? font = Shared(_shared.Std14, ResolveStd14Font);
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
                if (font.IsStandard14)
                {
                    return font;
                }

                // 3.30 (D-105): a simple font the PDF names but does not embed
                // is drawn with a standard 14 substitute (Liberation outlines)
                // at the PDF's /Widths positions. Before, it drew nothing.
                string? substitute = SubstituteFor(fd);
                return substitute is null ? null : RenderableFont.AsSubstitute(fd, _objects, substitute);
            }
            catch (System.Exception abortCheck) when (!PdfWorkScope.IsAbort(abortCheck))
            {
                return null;
            }
        }

        // The standard 14 font that stands in for a font dictionary with no
        // font program (a simple font, or a Type 0 font's descendant), or null
        // when the font is embedded, is a Type 3 font, or has no substitute.
        // Reports the substitution once per font.
        private string? SubstituteFor(PdfDictionary fontDict)
        {
            string subtype = _objects.ResolveAs<PdfName>(fontDict.TryGetValue(PdfName.Intern("Subtype"), out PdfPrimitive? st) ? st ?? PdfNull.Value : PdfNull.Value)?.Value ?? string.Empty;
            PdfDictionary target = fontDict;
            if (subtype == "Type0")
            {
                PdfArray? descendants = _objects.ResolveAs<PdfArray>(fontDict.TryGetValue(PdfName.Intern("DescendantFonts"), out PdfPrimitive? df) ? df ?? PdfNull.Value : PdfNull.Value);
                PdfDictionary? descendant = descendants is { Count: > 0 } ? _objects.ResolveAs<PdfDictionary>(descendants[0]) : null;
                if (descendant is null)
                {
                    return null;
                }

                target = descendant;
            }
            else if (subtype is not ("Type1" or "MMType1" or "TrueType"))
            {
                return null;
            }

            PdfDictionary? descriptor = _objects.ResolveAs<PdfDictionary>(
                target.TryGetValue(PdfName.Intern("FontDescriptor"), out PdfPrimitive? fdRef) ? fdRef ?? PdfNull.Value : PdfNull.Value);
            if (descriptor is not null
                && (descriptor.ContainsKey(PdfName.Intern("FontFile"))
                    || descriptor.ContainsKey(PdfName.Intern("FontFile2"))
                    || descriptor.ContainsKey(PdfName.Intern("FontFile3"))))
            {
                return null;
            }

            string baseFont = _objects.ResolveAs<PdfName>(
                fontDict.TryGetValue(PdfName.Intern("BaseFont"), out PdfPrimitive? bf) ? bf ?? PdfNull.Value : PdfNull.Value)?.Value ?? string.Empty;
            int flags = descriptor is not null ? (int)NumberIn(descriptor, "Flags") : 0;
            string? substitute = Standard14Substitute.Choose(
                baseFont,
                flags,
                descriptor is not null ? NumberIn(descriptor, "ItalicAngle") : 0.0,
                descriptor is not null ? NumberIn(descriptor, "FontWeight") : 0.0);

            _reporter.Report(
                PdfWarningKind.FontSubstituted,
                "font /" + _state.FontName,
                substitute is null
                    ? "the font '" + baseFont + "' is not embedded and has no substitute; its text is not drawn"
                    : "the font '" + baseFont + "' is not embedded; its text is drawn with " + Standard14Substitute.OutlineSource(substitute)
                        + " (" + substitute + ") at the PDF's character positions");
            return substitute;
        }

        private double NumberIn(PdfDictionary dict, string key)
        {
            if (!dict.TryGetValue(PdfName.Intern(key), out PdfPrimitive? value) || value is null)
            {
                return 0.0;
            }

            return _objects.Resolve(value) switch
            {
                PdfInteger i => i.Value,
                PdfReal r => r.Value,
                _ => 0.0,
            };
        }

        private static bool IsType1Program(byte[] program) =>
            program.Length >= 2 && ((program[0] == (byte)'%' && program[1] == (byte)'!') || (program[0] == 0x80 && program[1] == 0x01));

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

            Type1FontRenderer? font = Shared(_shared.Type1, ResolveType1Font);
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
                Type1FontRenderer? created = Type1FontRenderer.Create(fontFile, fd, _objects);
                if (created is null)
                {
                    _reporter.Report(PdfWarningKind.FontSubstituted, "font /" + _state.FontName, "the embedded Type 1 font program could not be read; the text is drawn with a substitute font");
                }

                return created;
            }
            catch (System.Exception abortCheck) when (!PdfWorkScope.IsAbort(abortCheck))
            {
                _reporter.Report(PdfWarningKind.FontSubstituted, "font /" + _state.FontName, "the embedded Type 1 font program could not be read (" + abortCheck.Message + "); the text is drawn with a substitute font");
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

            Chuvadi.Pdf.Fonts.PdfFont? font = Shared(_shared.PdfFonts, ResolvePdfFont);
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
            catch (System.Exception abortCheck) when (!PdfWorkScope.IsAbort(abortCheck))
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
            catch (Exception ex) when (!PdfWorkScope.IsAbort(ex))
            {
                // A damaged font program falls back like a missing one.
                _reporter.Report(
                    PdfWarningKind.FontSubstituted,
                    "font /" + _state.FontName,
                    "the embedded font program could not be read (" + ex.Message + "); the text is drawn with a substitute font");
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
                    try
                    {
                        byte[] program = ContentStreamLoader.Decode(fontStream);

                        // 3.34.0 (Phase B N5): a Type 1 program (PFA "%!" or
                        // PFB) is drawn by the Type 1 renderer, not parsed as
                        // TrueType here; before, that parse failed with a
                        // misleading "substitute" warning, and a font with a
                        // standard-14 name then got the substitute outlines.
                        if (key == "FontFile" && IsType1Program(program))
                        {
                            return null;
                        }

                        return program;
                    }
                    catch (FilterException damaged)
                    {
                        // 3.34.0 (#52c): a damaged font program is treated as
                        // not embedded, so a substitute draws the text. Before,
                        // the page failed (pdf.js issue11651).
                        _reporter.Report(
                            PdfWarningKind.StreamDamaged,
                            "font program " + WarningReporter.Describe(key, ffRef ?? PdfNull.Value),
                            damaged.Message + " The font is drawn with a substitute.");
                        return null;
                    }
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

        // The XObject being drawn, for example "Im0 12 0 R", or null.
        private string? _xobjectSubject;

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

            if (xobjStream is null || IsHiddenByEntry(xobjStream.Dictionary))
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

            // 3.32.0 (D-271): names the XObject in a StreamDamaged warning.
            string? outerSubject = _xobjectSubject;
            _xobjectSubject = WarningReporter.Describe(name, xobjRef ?? PdfNull.Value);
            try
            {
                if (subtype.Value == "Image")
                {
                    EmitImageXObject(xobjStream);
                }
                else if (subtype.Value == "Form")
                {
                    EmitFormXObject(xobjStream, _resources);
                }
            }
            finally
            {
                _xobjectSubject = outerSubject;
            }
        }

        /// <inheritdoc />
        public void InlineImage(PdfDictionary dictionary, byte[] data)
        {
            // 3.34.0 (Phase B N4): inline images are drawn through the image
            // XObject path. Before, they were not drawn at all.
            PdfDictionary image = WithResourceColorSpace(dictionary);
            string? outerSubject = _xobjectSubject;
            _xobjectSubject = "(inline)";
            try
            {
                EmitImageXObject(new PdfStream(image, data));
            }
            finally
            {
                _xobjectSubject = outerSubject;
            }
        }

        // An inline image may name its colour space in the resources'
        // /ColorSpace (§8.9.7); the named entry is put in its place.
        private PdfDictionary WithResourceColorSpace(PdfDictionary image)
        {
            if (!image.TryGetValue(PdfName.Intern("ColorSpace"), out PdfPrimitive? cs)
                || cs is not PdfName name
                || ResolvedColorSpace.Parse(name, _objects) is not null
                || _resources is null
                || !_resources.TryGetValue(PdfName.Intern("ColorSpace"), out PdfPrimitive? spacesRef)
                || _objects.ResolveAs<PdfDictionary>(spacesRef ?? PdfNull.Value) is not PdfDictionary spaces
                || !spaces.TryGetValue(name, out PdfPrimitive? entry)
                || entry is null)
            {
                return image;
            }

            PdfDictionary copy = new PdfDictionary();
            foreach (KeyValuePair<PdfName, PdfPrimitive> pair in image)
            {
                copy.Set(pair.Key, pair.Value);
            }

            copy.Set(PdfName.Intern("ColorSpace"), _objects.Resolve(entry));
            return copy;
        }

        // The encoded JPEG bytes of a DCT stream. 3.34.0: filters before
        // DCTDecode in a chain ([/FlateDecode /DCTDecode]) are decoded first;
        // before, the still-compressed bytes were taken as the JPEG.
        private byte[] CodecBytes(PdfStream stream)
        {
            if (stream.Filter is not PdfArray { Count: > 1 } filters)
            {
                return stream.RawBytes;
            }

            PdfDictionary outer = new PdfDictionary();
            foreach (KeyValuePair<PdfName, PdfPrimitive> pair in stream.Dictionary)
            {
                outer.Set(pair.Key, pair.Value);
            }

            outer.Set(PdfName.Filter, new PdfArray(Prefix(filters)));
            if (stream.Dictionary.TryGetValue(PdfName.Intern("DecodeParms"), out PdfPrimitive? parms)
                && _objects.Resolve(parms) is PdfArray { Count: > 1 } parmList)
            {
                outer.Set(PdfName.Intern("DecodeParms"), new PdfArray(Prefix(parmList)));
            }

            return ContentStreamLoader.Decode(new PdfStream(outer, stream.RawBytes), _objects);
        }

        // The gray samples of a one-component JPEG of the given size, or none
        // when it is not one (the caller then leaves the mask off).
        private static byte[] JpegGraySamples(byte[] jpeg, int width, int height)
        {
            if (jpeg.Length < 2 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
            {
                return Array.Empty<byte>();
            }

            ImageFrame gray = JpegDecoder.Decode(jpeg);
            if (gray.Width != width || gray.Height != height || gray.OriginalFormat != ImageColorFormat.Gray8)
            {
                return Array.Empty<byte>();
            }

            byte[] samples = new byte[(long)width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    samples[((long)y * width) + x] = gray.Pixels.GetPixelBgra(x, y).B;
                }
            }

            return samples;
        }

        private static List<PdfPrimitive> Prefix(PdfArray array)
        {
            List<PdfPrimitive> items = new List<PdfPrimitive>(array.Count - 1);
            for (int i = 0; i < array.Count - 1; i++)
            {
                items.Add(array[i]);
            }

            return items;
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
            string imageSubject = "image " + (_xobjectSubject ?? "(direct)");

            // 3.34.0 (Phase B N7): an image format not supported yet is
            // reported, not skipped in silence.
            if (LastFilterIs(xobjStream, "JPXDecode"))
            {
                _reporter.Report(PdfWarningKind.ImageSkipped, imageSubject, "JPEG 2000 (JPXDecode) images are not supported yet; the image is skipped.");
                return;
            }

            bool codec = StreamIsJpegOrJpx(xobjStream);
            try
            {
                imageBytes = codec ? CodecBytes(xobjStream) : ContentStreamLoader.Decode(xobjStream, _objects);
            }
            catch (FilterException damaged)
            {
                // 3.32.0 (D-271): say why the image is missing.
                _reporter.Report(PdfWarningKind.StreamDamaged, "image " + (_xobjectSubject ?? "(direct)"), damaged.Message + " The image is skipped.");
                return;
            }
            catch (Exception abortCheck) when (!PdfWorkScope.IsAbort(abortCheck))
            {
                return;
            }

            // Stencil mask (/ImageMask true, PDF 32000-1:2008 §8.9.6.2): a
            // 1-bpc image whose painted samples take the current fill colour
            // and whose masked samples paint nothing. Composed as an RGBA
            // frame so the ordinary image path (clip, alpha, blend, soft
            // mask) applies unchanged.
            if (ReadBoolEntry(xobjStream.Dictionary, "ImageMask"))
            {
                ImageFrame? stencil = StencilFrame(xobjStream, imageBytes);
                if (stencil is not null)
                {
                    EmitOp(new DrawImageOp(stencil, _state.Ctm, SnapshotClips(), _state.FillAlpha * _groupAlpha)
                    {
                        BlendMode = _state.BlendMode,
                        SoftMask = _state.SoftMask,
                    });
                }

                return;
            }

            ImageFrame? frame = null;

            try
            {
                if (imageBytes.Length > 2 &&
                    imageBytes[0] == 0xFF && imageBytes[1] == 0xD8)
                {
                    frame = CmykJpegFrame(xobjStream, imageBytes) ?? JpegDecoder.Decode(imageBytes);
                }
                else if (imageBytes.Length > 8 &&
                         imageBytes[0] == 137 && imageBytes[1] == 80)
                {
                    frame = PngDecoder.Decode(imageBytes);
                }
            }
            catch (ImageException bad)
            {
                _reporter.Report(PdfWarningKind.ImageSkipped, imageSubject, bad.Message + " The image is skipped.");
                return;
            }

            // Not a self-describing codec stream: interpret the bytes as raw
            // PDF image samples (Flate/CCITT/LZW output) using the image
            // dictionary's geometry and colour space.
            // 3.34.0: JPEG bytes that do not decode are never read as raw
            // samples (they drew as noise once short data was padded, N13).
            if (frame is null && codec)
            {
                _reporter.Report(PdfWarningKind.ImageSkipped, imageSubject, "The JPEG data could not be read; the image is skipped.");
                return;
            }

            bool raw = frame is null;
            frame ??= FrameFromRawSamples(xobjStream, imageBytes);

            if (frame is null)
            {
                _reporter.Report(PdfWarningKind.ImageSkipped, imageSubject, "The image's colour space, bit depth or size is not supported; the image is skipped.");
                return;
            }

            // 3.34.0 (Phase B N7): explicit masking (/Mask, §8.9.6.3, §8.9.6.4).
            // Before, /Mask was ignored and the image was drawn unmasked.
            if (!ApplyExplicitMask(xobjStream, frame, raw ? imageBytes : null, imageSubject))
            {
                return;
            }

            ApplySoftMask(xobjStream, frame);

            EmitOp(new DrawImageOp(frame, _state.Ctm, SnapshotClips(), _state.FillAlpha * _groupAlpha) { BlendMode = _state.BlendMode, SoftMask = _state.SoftMask });
        }

        // Converts decoded raw samples into an ImageFrame for the cases the
        // raster pipeline supports: 1-bpc DeviceGray (scanned bilevel, e.g.
        // CCITTFaxDecode output), 8-bpc DeviceGray, and 8-bpc DeviceRGB,
        // including ICCBased streams with 1 or 3 components, honouring a
        // /Decode [1 0] inversion for the gray cases. Stencil masks
        // (/ImageMask true) and other colour spaces return null and the
        // image is skipped, as before.
        // 3.34.0 (Phase B N3): a CMYK (or YCCK) JPEG is drawn from its stored
        // channel values read through the image dictionary's /Decode array,
        // like any other CMYK image. Before, the Adobe inverted-channel rule
        // was applied as well, so a PDF that does not invert with /Decode drew
        // as a negative. Null when the JPEG is not four-component, its size
        // does not match the dictionary, or the dictionary's colour space is
        // not a four-component one; the JPEG then decodes as before.
        private ImageFrame? CmykJpegFrame(PdfStream stream, byte[] jpeg)
        {
            PdfDictionary dict = stream.Dictionary;
            if (ReadBoolEntry(dict, "ImageMask") || IndexedSpace(dict) is not null || ResolveComponentCount(dict) != 4)
            {
                return null;
            }

            byte[]? cmyk = JpegDecoder.DecodeCmykSamples(jpeg, out int width, out int height);
            if (cmyk is null || width != ReadIntEntry(dict, "Width") || height != ReadIntEntry(dict, "Height"))
            {
                return null;
            }

            return FrameFromCmyk(dict, cmyk, width, height);
        }

        // 8-bpc CMYK samples (C, M, Y, K per pixel) to RGB with the standard
        // subtractive formula, honouring a /Decode [1 0 ...] inversion.
        private ImageFrame? FrameFromCmyk(PdfDictionary dict, byte[] samples, int width, int height)
        {
            bool invert = GrayDecodeInverted(dict);
            samples = PadShortSamples(samples, (long)width * height * 4);
            if (samples.Length < (long)width * height * 4)
            {
                return null;
            }

            ImageFrame frame = ImageFrame.Create(width, height, ImageColorFormat.Rgb24);
            for (int y = 0; y < height; y++)
            {
                PdfWorkScope.ThrowIfCancellationRequested();
                for (int x = 0; x < width; x++)
                {
                    int si = ((y * width) + x) * 4;
                    double c = samples[si] / 255.0;
                    double m = samples[si + 1] / 255.0;
                    double yc = samples[si + 2] / 255.0;
                    double k = samples[si + 3] / 255.0;
                    if (invert)
                    {
                        c = 1 - c;
                        m = 1 - m;
                        yc = 1 - yc;
                        k = 1 - k;
                    }

                    byte r = (byte)Math.Round(255.0 * (1 - c) * (1 - k));
                    byte g = (byte)Math.Round(255.0 * (1 - m) * (1 - k));
                    byte b = (byte)Math.Round(255.0 * (1 - yc) * (1 - k));
                    frame.Pixels.SetPixelBgra(x, y, b, g, r, 255);
                }
            }

            return frame;
        }

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

            // 3.32.0 (D-268): Indexed (palette) images of 1, 2, 4 or 8 bits
            // per sample. Before, they were skipped (nothing drawn).
            if (IndexedSpace(dict) is ResolvedColorSpace palette)
            {
                return IndexedFrame(dict, palette, samples, width, height, bpc);
            }

            int components = ResolveComponentCount(dict);
            bool invert = GrayDecodeInverted(dict);

            if (components == 1 && bpc == 1)
            {
                int stride = (width + 7) / 8;
                samples = PadShortSamples(samples, (long)stride * height);
                if (samples.Length < stride * height)
                {
                    return null;
                }

                ImageFrame frame = ImageFrame.Create(width, height, ImageColorFormat.Gray8);
                for (int y = 0; y < height; y++)
                {
                    PdfWorkScope.ThrowIfCancellationRequested();
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
                samples = PadShortSamples(samples, (long)width * height);
                if (samples.Length < width * height)
                {
                    return null;
                }

                ImageFrame frame = ImageFrame.Create(width, height, ImageColorFormat.Gray8);
                for (int y = 0; y < height; y++)
                {
                    PdfWorkScope.ThrowIfCancellationRequested();
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
                samples = PadShortSamples(samples, (long)width * height * 3);
                if (samples.Length < width * height * 3)
                {
                    return null;
                }

                ImageFrame frame = ImageFrame.Create(width, height, ImageColorFormat.Rgb24);
                for (int y = 0; y < height; y++)
                {
                    PdfWorkScope.ThrowIfCancellationRequested();
                    for (int x = 0; x < width; x++)
                    {
                        int si = ((y * width) + x) * 3;
                        frame.Pixels.SetPixelBgra(
                            x, y, samples[si + 2], samples[si + 1], samples[si], 255);
                    }
                }
                return frame;
            }

            // 8-bpc DeviceCMYK (or ICCBased with N=4): converted to RGB with
            // the standard subtractive formula so CMYK images display. A
            // /Decode [1 0 …] inversion (Adobe CMYK convention) is honoured.
            if (components == 4 && bpc == 8)
            {
                return FrameFromCmyk(dict, samples, width, height);
            }

            return null;
        }

        // Applies an image's /SMask (a DeviceGray image giving per-pixel alpha)
        // onto the decoded frame's alpha channel. The soft mask is sampled at
        // the frame's resolution (nearest-neighbour) when the dimensions differ.
        // Without this, soft-masked images (e.g. faded watermarks) render as
        // fully opaque, painting their backing colour where they should be
        // transparent.
        // Whether the stream's last filter is the given one (aliases resolved).
        private static bool LastFilterIs(PdfStream stream, string filterName)
        {
            PdfPrimitive? filter = stream.Filter;
            PdfName? last = filter as PdfName ?? (filter is PdfArray { Count: > 0 } array ? array.GetAs<PdfName>(array.Count - 1) : null);
            return last is not null && FilterRegistry.ResolveAlias(last.Value) == filterName;
        }

        // 3.34.0 (Phase B N7): /Mask as a stencil image (§8.9.6.3): a 1-bit
        // image, scaled to the base image, whose samples of 1 (0 with /Decode
        // [1 0]) leave the base image unpainted; or as an array of colour-key
        // ranges (§8.9.6.4) over the raw samples of an 8-bit image with 1 or
        // 3 components, or of a JPEG decoded to gray or RGB. Returns false
        // when the mask stream cannot be decoded: the image is then skipped
        // with a warning rather than drawn unmasked over what it should only
        // partly cover (a scanned page's colour layer under a text mask).
        private bool ApplyExplicitMask(PdfStream xobjStream, ImageFrame frame, byte[]? rawSamples, string imageSubject)
        {
            if (!xobjStream.Dictionary.TryGetValue(PdfName.Intern("Mask"), out PdfPrimitive? maskRef) || maskRef is null)
            {
                return true;
            }

            PdfPrimitive mask = _objects.Resolve(maskRef);
            if (mask is PdfStream maskStream)
            {
                return ApplyStencilMask(maskStream, frame, imageSubject);
            }

            if (mask is PdfArray ranges)
            {
                ApplyColorKeyMask(xobjStream.Dictionary, ranges, frame, rawSamples, imageSubject);
            }

            return true;
        }

        private bool ApplyStencilMask(PdfStream maskStream, ImageFrame frame, string imageSubject)
        {
            int mw = ReadIntEntry(maskStream.Dictionary, "Width");
            int mh = ReadIntEntry(maskStream.Dictionary, "Height");
            if (mw <= 0 || mh <= 0)
            {
                _reporter.Report(PdfWarningKind.ImageSkipped, imageSubject, "The image's /Mask has no size; the image is skipped rather than drawn unmasked.");
                return false;
            }

            byte[] bits;
            try
            {
                bits = ContentStreamLoader.Decode(maskStream, _objects);
            }
            catch (Exception damaged) when (!PdfWorkScope.IsAbort(damaged))
            {
                _reporter.Report(PdfWarningKind.ImageSkipped, imageSubject, "The image's /Mask could not be decoded (" + damaged.Message + "); the image is skipped rather than drawn unmasked.");
                return false;
            }

            int stride = (mw + 7) / 8;
            bits = PadShortSamples(bits, (long)stride * mh);
            if ((long)bits.Length < (long)stride * mh)
            {
                _reporter.Report(PdfWarningKind.ImageSkipped, imageSubject, "The image's /Mask holds no data; the image is skipped rather than drawn unmasked.");
                return false;
            }

            // A mask that is not marked /ImageMask (an ordinary 1-bit gray
            // image) is read as Poppler and MuPDF read it: white (1) shows the
            // image, black (0) hides it (found by the pdf.js run, issue6621).
            bool zeroMasks = GrayDecodeInverted(maskStream.Dictionary) != !ReadBoolEntry(maskStream.Dictionary, "ImageMask");
            int fw = frame.Width;
            int fh = frame.Height;
            for (int y = 0; y < fh; y++)
            {
                PdfWorkScope.ThrowIfCancellationRequested();
                int my = Math.Min(mh - 1, (int)((long)y * mh / fh));
                for (int x = 0; x < fw; x++)
                {
                    int mx = Math.Min(mw - 1, (int)((long)x * mw / fw));
                    int bit = (bits[(my * stride) + (mx >> 3)] >> (7 - (mx & 7))) & 1;
                    if ((bit == 1) != zeroMasks)
                    {
                        (byte b, byte g, byte r, byte _) = frame.Pixels.GetPixelBgra(x, y);
                        frame.Pixels.SetPixelBgra(x, y, b, g, r, 0);
                    }
                }
            }

            return true;
        }

        private void ApplyColorKeyMask(PdfDictionary dict, PdfArray ranges, ImageFrame frame, byte[]? rawSamples, string imageSubject)
        {
            int bpc = ReadIntEntry(dict, "BitsPerComponent");
            int components = rawSamples is null ? (frame.OriginalFormat == ImageColorFormat.Gray8 ? 1 : 3) : ResolveComponentCount(dict);
            if ((rawSamples is not null && bpc != 8) || components is not (1 or 3) || ranges.Count < components * 2)
            {
                _reporter.Report(PdfWarningKind.ImageSkipped, imageSubject, "The image's colour-key /Mask is not supported for this image format; the image is drawn without it.");
                return;
            }

            int[] lo = new int[components];
            int[] hi = new int[components];
            for (int c = 0; c < components; c++)
            {
                lo[c] = (int)Math.Round(PdfReal.ToDouble(ranges[2 * c]));
                hi[c] = (int)Math.Round(PdfReal.ToDouble(ranges[(2 * c) + 1]));
            }

            int fw = frame.Width;
            int fh = frame.Height;
            for (int y = 0; y < fh; y++)
            {
                PdfWorkScope.ThrowIfCancellationRequested();
                for (int x = 0; x < fw; x++)
                {
                    (byte b, byte g, byte r, byte a) = frame.Pixels.GetPixelBgra(x, y);
                    bool inside = true;
                    for (int c = 0; c < components && inside; c++)
                    {
                        int v;
                        if (rawSamples is not null)
                        {
                            long at = (((long)y * fw) + x) * components + c;
                            v = at < rawSamples.Length ? rawSamples[at] : 0;
                        }
                        else
                        {
                            v = components == 1 ? r : c switch { 0 => r, 1 => g, _ => b };
                        }

                        inside = v >= lo[c] && v <= hi[c];
                    }

                    if (inside && a != 0)
                    {
                        frame.Pixels.SetPixelBgra(x, y, b, g, r, 0);
                    }
                }
            }
        }

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
                // 3.34.0: a soft mask may itself be a (gray) JPEG; before, its
                // decode failed and the image was drawn without its mask.
                smSamples = StreamIsJpegOrJpx(smStream)
                    ? JpegGraySamples(CodecBytes(smStream), smWidth, smHeight)
                    : ContentStreamLoader.Decode(smStream, _objects);
            }
            catch (Exception abortCheck) when (!PdfWorkScope.IsAbort(abortCheck))
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
        // The image's colour space when it is /Indexed and parses, else null.
        private ResolvedColorSpace? IndexedSpace(PdfDictionary dict)
        {
            if (!dict.TryGetValue(PdfName.Intern("ColorSpace"), out PdfPrimitive? csRef)
                || _objects.Resolve(csRef) is not PdfArray array
                || array.Count == 0
                || _objects.Resolve(array[0]) is not PdfName family
                || family.Value != "Indexed")
            {
                return null;
            }

            ResolvedColorSpace? space = ResolvedColorSpace.Parse(array, _objects);
            return space is not null && space.Kind == ResolvedColorSpace.Family.Indexed ? space : null;
        }

        // Expands a palette image through its colour space into an RGB frame.
        private ImageFrame? IndexedFrame(PdfDictionary dict, ResolvedColorSpace palette, byte[] samples, int width, int height, int bpc)
        {
            dict.TryGetValue(PdfName.Intern("Decode"), out PdfPrimitive? decode);
            int hival = (1 << Math.Clamp(bpc, 1, 8)) - 1;
            if (dict.TryGetValue(PdfName.Intern("ColorSpace"), out PdfPrimitive? csRef)
                && _objects.Resolve(csRef) is PdfArray array && array.Count >= 3
                && _objects.Resolve(array[2]) is PdfInteger hi)
            {
                hival = hi.Value;
            }

            byte[]? indices = IndexedSamples.ToIndices(samples, width, height, bpc, decode, _objects, hival);
            if (indices is null)
            {
                return null;
            }

            byte[] table = new byte[256 * 3];
            double[] component = new double[1];
            for (int i = 0; i < 256; i++)
            {
                component[0] = i;
                double[] rgb = palette.ToRgb(component);
                table[i * 3] = ToByte(rgb.Length > 0 ? rgb[0] : 0);
                table[(i * 3) + 1] = ToByte(rgb.Length > 1 ? rgb[1] : 0);
                table[(i * 3) + 2] = ToByte(rgb.Length > 2 ? rgb[2] : 0);
            }

            ImageFrame frame = ImageFrame.Create(width, height, ImageColorFormat.Rgb24);
            for (int y = 0; y < height; y++)
            {
                PdfWorkScope.ThrowIfCancellationRequested();
                for (int x = 0; x < width; x++)
                {
                    int t = indices[((long)y * width) + x] * 3;
                    frame.Pixels.SetPixelBgra(x, y, table[t + 2], table[t + 1], table[t], 255);
                }
            }

            return frame;
        }

        private static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value * 255.0), 0, 255);

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
                    "DeviceCMYK" or "CMYK" => 4,
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
                    return count.Value is 1 or 3 or 4 ? count.Value : 0;
                }
            }

            return 0;
        }

        // True when /Decode is [1 0] for a single-component image (inverted
        // gray, common in scanned PDFs).
        // Builds an RGBA frame for a stencil mask: painted bits take the
        // current fill colour at full alpha; masked bits are fully
        // transparent. Default /Decode [0 1] paints sample 0; [1 0] flips.
        private ImageFrame? StencilFrame(PdfStream stream, byte[] samples)
        {
            PdfDictionary dict = stream.Dictionary;
            int width = ReadIntEntry(dict, "Width");
            int height = ReadIntEntry(dict, "Height");
            if (width <= 0 || height <= 0)
            {
                return null;
            }

            int stride = (width + 7) / 8;
            samples = PadShortSamples(samples, (long)stride * height);
            if ((long)samples.Length < (long)stride * height)
            {
                return null;
            }

            bool onePaints = GrayDecodeInverted(dict);
            ColorF fill = _state.FillColor.ToRgb();
            byte r = (byte)Math.Round(Math.Clamp(fill.R, 0f, 1f) * 255f);
            byte g = (byte)Math.Round(Math.Clamp(fill.G, 0f, 1f) * 255f);
            byte b = (byte)Math.Round(Math.Clamp(fill.B, 0f, 1f) * 255f);

            PdfWorkScope.ChargeMemory((long)width * height * 4);
            PixelBuffer buffer = new PixelBuffer(width, height);
            for (int y = 0; y < height; y++)
            {
                PdfWorkScope.ThrowIfCancellationRequested();
                int rowStart = y * stride;
                for (int x = 0; x < width; x++)
                {
                    int bit = (samples[rowStart + (x >> 3)] >> (7 - (x & 7))) & 1;
                    bool paint = onePaints ? bit == 1 : bit == 0;
                    if (paint)
                    {
                        buffer.SetPixelBgra(x, y, b, g, r, 255);
                    }
                    else
                    {
                        buffer.SetPixelBgra(x, y, 0, 0, 0, 0);
                    }
                }
            }

            return new ImageFrame(buffer, ImageColorFormat.Rgba32);
        }

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
            if (IsHiddenByEntry(xobjStream.Dictionary))
            {
                return;
            }

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
            if (_nestingDepth >= MaxNestingDepth)
            {
                return;
            }

            Worker sub = new Worker(_objects, _hintingScale, _lightHinting, _autohintFallback, _visibility);
            sub._nestingDepth = _nestingDepth + 1;
            sub._reporter = _reporter;
            sub._shared = _shared;

            // A form XObject inherits the graphics state in effect at the Do
            // (PDF 32000-1:2008 §8.10.1), not the defaults: since 3.32.0
            // (D-259) colours and colour spaces, line style, text state,
            // constant alpha and blend mode all carry over. Before, only the
            // alpha did, so a form that paints without setting a colour (as
            // LibreOffice watermarks do) painted black. Not carried over:
            //  - the CTM: the form draws in its own space, and the placement
            //    composes when the nested list is drawn;
            //  - the clip: it is applied to the nested list (SnapshotClips);
            //  - fill and stroke patterns: a pattern is defined in the space it
            //    was set in, so inside a form with its own matrix it would be
            //    misplaced (unchanged: such a form paints its default colour);
            //  - the soft mask: it is set on the nested list instead, so its
            //    coverage is made in the outer space (3.34.0, #42).
            BuilderGraphicsState inherited = _state.Clone();
            inherited.Ctm = Transform.Identity;
            inherited.ActiveClips = new List<ClipPath>();
            inherited.FillPattern = null;
            inherited.StrokePattern = null;
            inherited.SoftMask = null;
            sub._groupAlpha = _groupAlpha;
            if (IsTransparencyGroup(xobjStream.Dictionary))
            {
                sub._groupAlpha = _groupAlpha * _state.FillAlpha;
                inherited.FillAlpha = 1.0;
                inherited.StrokeAlpha = 1.0;
            }

            sub._state = inherited;

            byte[] formContent;

            try
            {
                formContent = ContentStreamLoader.Decode(xobjStream);
            }
            catch (FilterException damaged)
            {
                // 3.32.0 (D-271): say why the form is missing.
                _reporter.Report(PdfWarningKind.StreamDamaged, "form " + (_xobjectSubject ?? "(appearance)"), damaged.Message + " The form is skipped.");
                return;
            }
            catch (Exception abortCheck) when (!PdfWorkScope.IsAbort(abortCheck))
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

            // 3.34.0 (#42, D-264): the soft mask in effect at the Do applies to
            // the form's content; the rasterizer makes its coverage in this
            // (the outer) space.
            EmitOp(new NestedDisplayListOp(inner, composition, SnapshotClips()) { SoftMask = _state.SoftMask });
        }

        private bool IsTransparencyGroup(PdfDictionary form) =>
            form.TryGetValue(PdfName.Intern("Group"), out PdfPrimitive? group)
            && _objects.ResolveAs<PdfDictionary>(group ?? PdfNull.Value) is PdfDictionary g
            && g.TryGetValue(PdfName.Intern("S"), out PdfPrimitive? kind)
            && _objects.Resolve(kind) is PdfName { Value: "Transparency" };

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
