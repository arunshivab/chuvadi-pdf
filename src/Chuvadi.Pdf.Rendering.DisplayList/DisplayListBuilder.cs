// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.8 — Content streams; §8 — Graphics; §9 — Text
// PHASE: v2.0.0 R1 D3c-2 — DisplayList builder

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Filters;
using Chuvadi.Pdf.Fonts.Rendering;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Images;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using Path = Chuvadi.Pdf.Graphics.Path;

namespace Chuvadi.Pdf.Rendering.DisplayList;

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
    /// <returns>
    /// An immutable display list. Empty if the page has no content
    /// stream. CTM-baked geometry; per-op clip snapshots. Page rotation
    /// is not applied here; that is a consumer concern.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="page"/> or <paramref name="objects"/> is null.
    /// </exception>
    public static PageDisplayList Build(PdfPage page, PdfObjectStore objects)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(objects);

        Worker worker = new Worker(objects);
        byte[] content = worker.LoadContentBytes(page.Contents);
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
        double pageHeight)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(objects);

        Worker worker = new Worker(objects);
        return worker.BuildFromBytes(content, resources, pageWidth, pageHeight);
    }

    /// <summary>
    /// Internal worker that owns mutable interpretation state. A new
    /// instance is created per Build call so the public API is stateless.
    /// </summary>
    private sealed class Worker
    {
        private readonly PdfObjectStore _objects;
        private readonly FilterPipeline _pipeline;
        private readonly Dictionary<string, FontRenderer?> _fontCache;

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

        public Worker(PdfObjectStore objects)
        {
            _objects = objects;
            _pipeline = FilterRegistry.CreateDefaultPipeline();
            _fontCache = new Dictionary<string, FontRenderer?>();
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
            if (content.Length > 0)
            {
                Interpret(content, resources);
            }

            return new PageDisplayList(_ops, pageWidth, pageHeight);
        }

        // ── Content stream loading ────────────────────────────────────────

        public byte[] LoadContentBytes(PdfPrimitive? contents)
        {
            if (contents is null || contents is PdfNull)
            {
                return Array.Empty<byte>();
            }

            PdfPrimitive resolved = _objects.Resolve(contents);

            if (resolved is PdfStream single)
            {
                return DecodeStream(single);
            }

            if (resolved is PdfArray array)
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    for (int i = 0; i < array.Count; i++)
                    {
                        PdfPrimitive item = _objects.Resolve(array[i]);

                        if (item is PdfStream s)
                        {
                            byte[] decoded = DecodeStream(s);
                            ms.Write(decoded, 0, decoded.Length);

                            if (i < array.Count - 1)
                            {
                                ms.WriteByte(32);
                            }
                        }
                    }

                    return ms.ToArray();
                }
            }

            return Array.Empty<byte>();
        }

        private byte[] DecodeStream(PdfStream stream)
        {
            if (!stream.IsFiltered)
            {
                return stream.RawBytes;
            }

            PdfPrimitive? filter = stream.Filter;

            if (filter is PdfName filterName)
            {
                string resolved = FilterRegistry.ResolveAlias(filterName.Value);
                return _pipeline.Decode(resolved, stream.RawBytes, null);
            }

            if (filter is PdfArray filterArray)
            {
                byte[] data = stream.RawBytes;

                for (int i = 0; i < filterArray.Count; i++)
                {
                    PdfName? fn = filterArray.GetAs<PdfName>(i);

                    if (fn is null)
                    {
                        continue;
                    }

                    string resolved = FilterRegistry.ResolveAlias(fn.Value);
                    data = _pipeline.Decode(resolved, data, null);
                }

                return data;
            }

            return stream.RawBytes;
        }

        // ── Interpreter loop ──────────────────────────────────────────────

        private void Interpret(byte[] content, PdfDictionary? resources)
        {
            using (MemoryStream ms = new MemoryStream(content))
            using (PdfTokenizer tokenizer = new PdfTokenizer(ms))
            {
                List<PdfToken> operands = new List<PdfToken>();

                while (true)
                {
                    PdfToken token = tokenizer.Read();

                    if (token.IsEndOfStream)
                    {
                        break;
                    }

                    if (token.Type == PdfTokenType.ArrayStart)
                    {
                        // Collect array inline for TJ
                        List<PdfToken> arrTokens = new List<PdfToken>();

                        while (true)
                        {
                            PdfToken inner = tokenizer.Read();

                            if (inner.IsEndOfStream || inner.Type == PdfTokenType.ArrayEnd)
                            {
                                break;
                            }

                            arrTokens.Add(inner);
                        }

                        operands.Add(new PdfToken(PdfTokenType.ArrayStart, Array.Empty<byte>(), 0));
                        operands.AddRange(arrTokens);
                        operands.Add(new PdfToken(PdfTokenType.ArrayEnd, Array.Empty<byte>(), 0));
                        continue;
                    }

                    if (token.Type != PdfTokenType.Keyword)
                    {
                        operands.Add(token);
                        continue;
                    }

                    Execute(token.RawText, operands, resources);
                    operands.Clear();
                }
            }
        }

        // ── Operator dispatch ─────────────────────────────────────────────

        private void Execute(string op, List<PdfToken> operands, PdfDictionary? resources)
        {
            switch (op)
            {
                // Graphics state
                case "q": OpQ(); break;
                case "Q": OpQUpper(); break;
                case "cm": OpCm(operands); break;

                // Stroke state parameters
                case "w": if (operands.Count > 0) { _state.LineWidth = ParseDouble(operands[0]); } break;
                case "J": if (operands.Count > 0) { _state.LineCap = (LineCap)ParseInt(operands[0]); } break;
                case "j": if (operands.Count > 0) { _state.LineJoin = (LineJoin)ParseInt(operands[0]); } break;
                case "M": if (operands.Count > 0) { _state.MiterLimit = ParseDouble(operands[0]); } break;
                case "d": OpDashPattern(operands); break;
                case "i": break; // Flatness — visual hint, ignored
                case "ri": break; // Rendering intent — ignored at builder level
                case "gs": break; // ExtGState — deferred to v2.1+

                // Colour operators
                case "g": OpFillGray(operands); break;
                case "G": OpStrokeGray(operands); break;
                case "rg": OpFillRgb(operands); break;
                case "RG": OpStrokeRgb(operands); break;
                case "k": OpFillCmyk(operands); break;
                case "K": OpStrokeCmyk(operands); break;
                case "cs": OpColorSpace(operands, stroke: false); break;
                case "CS": OpColorSpace(operands, stroke: true); break;
                case "sc": case "scn": OpScColor(operands, stroke: false); break;
                case "SC": case "SCN": OpScColor(operands, stroke: true); break;

                // Path construction
                case "m": if (operands.Count >= 2) { _currentPath.MoveTo(ParseDouble(operands[0]), ParseDouble(operands[1])); } break;
                case "l": OpL(operands); break;
                case "c": OpC(operands); break;
                case "v": OpV(operands); break;
                case "y": OpY(operands); break;
                case "h": _currentPath.ClosePath(); break;
                case "re": OpRe(operands); break;

                // Path painting
                case "f": case "F": OpFill(FillRule.NonZeroWinding); break;
                case "f*": OpFill(FillRule.EvenOdd); break;
                case "S": OpStroke(closeFirst: false); break;
                case "s": OpStroke(closeFirst: true); break;
                case "B": OpFillStroke(FillRule.NonZeroWinding, closeFirst: false); break;
                case "B*": OpFillStroke(FillRule.EvenOdd, closeFirst: false); break;
                case "b": OpFillStroke(FillRule.NonZeroWinding, closeFirst: true); break;
                case "b*": OpFillStroke(FillRule.EvenOdd, closeFirst: true); break;
                case "n": OpEndPath(); break;

                // Clipping
                case "W": _clipPending = true; _clipRule = FillRule.NonZeroWinding; break;
                case "W*": _clipPending = true; _clipRule = FillRule.EvenOdd; break;

                // Text object delimiters
                case "BT": _textMatrix = Transform.Identity; _textLineMatrix = Transform.Identity; break;
                case "ET": break;

                // Text state
                case "Tc": if (operands.Count > 0) { _state.CharacterSpacing = ParseDouble(operands[0]); } break;
                case "Tw": if (operands.Count > 0) { _state.WordSpacing = ParseDouble(operands[0]); } break;
                case "Tz": if (operands.Count > 0) { _state.HorizontalScaling = ParseDouble(operands[0]); } break;
                case "TL": if (operands.Count > 0) { _state.TextLeading = ParseDouble(operands[0]); } break;
                case "Ts": if (operands.Count > 0) { _state.TextRise = ParseDouble(operands[0]); } break;
                case "Tr": if (operands.Count > 0) { _state.TextRenderingMode = (int)ParseInt(operands[0]); } break;
                case "Tf": OpTf(operands, resources); break;

                // Text positioning
                case "Td": OpTd(operands); break;
                case "TD": OpTD(operands); break;
                case "Tm": OpTm(operands); break;
                case "T*": OpTStar(); break;

                // Text showing
                case "Tj": if (operands.Count > 0) { ShowText(ExtractString(operands[0])); } break;
                case "TJ": OpTJ(operands); break;
                case "'": OpQuote(operands); break;
                case "\"": OpDoubleQuote(operands); break;

                // XObjects
                case "Do": OpDo(operands, resources); break;

                // Marked content / compatibility — parsed, operands consumed, no emission
                case "BMC": case "BDC": case "EMC": case "MP": case "DP": case "BX": case "EX": break;

                // Unrecognised operator — silently ignored, operands cleared by caller
                default: break;
            }
        }

        // ── Graphics state operators ──────────────────────────────────────

        private void OpQ()
        {
            _stateStack.Push(_state.Clone());
        }

        private void OpQUpper()
        {
            if (_stateStack.Count > 0)
            {
                _state = _stateStack.Pop();
            }
        }

        private void OpCm(List<PdfToken> operands)
        {
            if (operands.Count < 6)
            {
                return;
            }

            Transform local = new Transform(
                ParseDouble(operands[0]), ParseDouble(operands[1]),
                ParseDouble(operands[2]), ParseDouble(operands[3]),
                ParseDouble(operands[4]), ParseDouble(operands[5]));

            // PDF row-vector convention: local cm pre-multiplies the CTM
            _state.Ctm = local.Multiply(_state.Ctm);
        }

        private void OpDashPattern(List<PdfToken> operands)
        {
            // d [dashArray] phase
            if (operands.Count < 1)
            {
                return;
            }

            int phaseIdx = operands.Count - 1;
            double phase = ParseDouble(operands[phaseIdx]);

            // Skip leading ArrayStart and trailing ArrayEnd tokens if present
            int start = 0;
            int end = phaseIdx;

            while (start < end && operands[start].Type == PdfTokenType.ArrayStart)
            {
                start++;
            }

            while (end > start && operands[end - 1].Type == PdfTokenType.ArrayEnd)
            {
                end--;
            }

            int len = end - start;
            double[] dashes = new double[len];

            for (int i = 0; i < len; i++)
            {
                dashes[i] = ParseDouble(operands[start + i]);
            }

            _state.DashPattern = dashes;
            _state.DashOffset = phase;
        }

        // ── Colour operators ──────────────────────────────────────────────

        private void OpFillGray(List<PdfToken> operands)
        {
            if (operands.Count < 1)
            {
                return;
            }
            _state.FillColor = ColorF.FromGray((float)ParseDouble(operands[0]));
            _state.FillValid = true;
        }

        private void OpStrokeGray(List<PdfToken> operands)
        {
            if (operands.Count < 1)
            {
                return;
            }
            _state.StrokeColor = ColorF.FromGray((float)ParseDouble(operands[0]));
            _state.StrokeValid = true;
        }

        private void OpFillRgb(List<PdfToken> operands)
        {
            if (operands.Count < 3)
            {
                return;
            }
            _state.FillColor = ColorF.FromRgb(
                (float)ParseDouble(operands[0]),
                (float)ParseDouble(operands[1]),
                (float)ParseDouble(operands[2]));
            _state.FillValid = true;
        }

        private void OpStrokeRgb(List<PdfToken> operands)
        {
            if (operands.Count < 3)
            {
                return;
            }
            _state.StrokeColor = ColorF.FromRgb(
                (float)ParseDouble(operands[0]),
                (float)ParseDouble(operands[1]),
                (float)ParseDouble(operands[2]));
            _state.StrokeValid = true;
        }

        private void OpFillCmyk(List<PdfToken> operands)
        {
            if (operands.Count < 4)
            {
                return;
            }
            _state.FillColor = ColorF.FromCmyk(
                (float)ParseDouble(operands[0]),
                (float)ParseDouble(operands[1]),
                (float)ParseDouble(operands[2]),
                (float)ParseDouble(operands[3]));
            _state.FillValid = true;
        }

        private void OpStrokeCmyk(List<PdfToken> operands)
        {
            if (operands.Count < 4)
            {
                return;
            }
            _state.StrokeColor = ColorF.FromCmyk(
                (float)ParseDouble(operands[0]),
                (float)ParseDouble(operands[1]),
                (float)ParseDouble(operands[2]),
                (float)ParseDouble(operands[3]));
            _state.StrokeValid = true;
        }

        private void OpColorSpace(List<PdfToken> operands, bool stroke)
        {
            // cs / CS sets the active colour space. We track validity:
            // device colour spaces remain valid; Pattern marks invalid so
            // subsequent paints get suppressed until a representable
            // colour is set via rg/g/k.
            if (operands.Count < 1)
            {
                return;
            }

            string name = ExtractName(operands[0]);
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

        private void OpScColor(List<PdfToken> operands, bool stroke)
        {
            // sc / scn / SC / SCN — set colour in current colour space.
            // We support 1, 3, or 4 numeric operands (DeviceGray/RGB/CMYK).
            // A trailing name operand (Pattern) suppresses validity.
            int numericCount = 0;
            bool hasName = false;

            foreach (PdfToken t in operands)
            {
                if (t.Type == PdfTokenType.Integer || t.Type == PdfTokenType.Real)
                {
                    numericCount++;
                }
                else if (t.Type == PdfTokenType.Name)
                {
                    hasName = true;
                }
            }

            if (hasName)
            {
                if (stroke) { _state.StrokeValid = false; } else { _state.FillValid = false; }
                return;
            }

            ColorF c;

            switch (numericCount)
            {
                case 1:
                    c = ColorF.FromGray((float)ParseDouble(operands[0]));
                    break;
                case 3:
                    c = ColorF.FromRgb(
                        (float)ParseDouble(operands[0]),
                        (float)ParseDouble(operands[1]),
                        (float)ParseDouble(operands[2]));
                    break;
                case 4:
                    c = ColorF.FromCmyk(
                        (float)ParseDouble(operands[0]),
                        (float)ParseDouble(operands[1]),
                        (float)ParseDouble(operands[2]),
                        (float)ParseDouble(operands[3]));
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

        private void OpL(List<PdfToken> operands)
        {
            if (operands.Count < 2)
            {
                return;
            }

            // LineTo on an empty path is illegal in the Path API (it
            // requires a current point). Defend against malformed streams.
            if (_currentPath.IsEmpty)
            {
                _currentPath.MoveTo(ParseDouble(operands[0]), ParseDouble(operands[1]));
                return;
            }

            _currentPath.LineTo(ParseDouble(operands[0]), ParseDouble(operands[1]));
        }

        private void OpC(List<PdfToken> operands)
        {
            if (operands.Count < 6)
            {
                return;
            }

            if (_currentPath.IsEmpty)
            {
                return; // Malformed — c requires a current point
            }

            _currentPath.CubicBezierTo(
                ParsePoint(operands, 0),
                ParsePoint(operands, 2),
                ParsePoint(operands, 4));
        }

        private void OpV(List<PdfToken> operands)
        {
            // v x2 y2 x3 y3 — Bezier with initial point as first control
            if (operands.Count < 4)
            {
                return;
            }

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
                ParsePoint(operands, 0),
                ParsePoint(operands, 2));
        }

        private void OpY(List<PdfToken> operands)
        {
            // y x1 y1 x3 y3 — Bezier with final point as second control
            if (operands.Count < 4)
            {
                return;
            }

            if (_currentPath.IsEmpty)
            {
                return;
            }

            PointF endPt = ParsePoint(operands, 2);
            _currentPath.CubicBezierTo(
                ParsePoint(operands, 0),
                endPt,
                endPt);
        }

        private void OpRe(List<PdfToken> operands)
        {
            if (operands.Count < 4)
            {
                return;
            }

            double x = ParseDouble(operands[0]);
            double y = ParseDouble(operands[1]);
            double w = ParseDouble(operands[2]);
            double h = ParseDouble(operands[3]);

            _currentPath.MoveTo(x, y);
            _currentPath.LineTo(x + w, y);
            _currentPath.LineTo(x + w, y + h);
            _currentPath.LineTo(x, y + h);
            _currentPath.ClosePath();
        }

        // ── Path painting ─────────────────────────────────────────────────

        private void OpFill(FillRule rule)
        {
            if (_state.FillValid && !_currentPath.IsEmpty)
            {
                Path transformed = TransformPath(_currentPath, _state.Ctm);
                _ops.Add(new FillPathOp(transformed, _state.FillColor, rule, SnapshotClips()));
            }

            ApplyDeferredClip();
            _currentPath = new Path();
        }

        private void OpStroke(bool closeFirst)
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
                    _ops.Add(new FillPathOp(transformed, _state.FillColor, rule, snapshot));
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

        private void OpEndPath()
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
                Color = _state.StrokeColor,
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

        private void OpTf(List<PdfToken> operands, PdfDictionary? resources)
        {
            if (operands.Count < 2)
            {
                return;
            }

            _state.FontName = ExtractName(operands[0]);
            _state.FontSize = ParseDouble(operands[1]);
            _state.FontResources = resources;
        }

        private void OpTd(List<PdfToken> operands)
        {
            if (operands.Count < 2)
            {
                return;
            }

            double tx = ParseDouble(operands[0]);
            double ty = ParseDouble(operands[1]);

            Transform t = new Transform(1, 0, 0, 1, tx, ty);
            _textLineMatrix = t.Multiply(_textLineMatrix);
            _textMatrix = _textLineMatrix;
        }

        private void OpTD(List<PdfToken> operands)
        {
            if (operands.Count < 2)
            {
                return;
            }

            double tx = ParseDouble(operands[0]);
            double ty = ParseDouble(operands[1]);
            _state.TextLeading = -ty;

            Transform t = new Transform(1, 0, 0, 1, tx, ty);
            _textLineMatrix = t.Multiply(_textLineMatrix);
            _textMatrix = _textLineMatrix;
        }

        private void OpTm(List<PdfToken> operands)
        {
            if (operands.Count < 6)
            {
                return;
            }

            Transform t = new Transform(
                ParseDouble(operands[0]), ParseDouble(operands[1]),
                ParseDouble(operands[2]), ParseDouble(operands[3]),
                ParseDouble(operands[4]), ParseDouble(operands[5]));

            _textMatrix = t;
            _textLineMatrix = t;
        }

        private void OpTStar()
        {
            // T* — move to start of next line: 0 -leading Td
            Transform t = new Transform(1, 0, 0, 1, 0, -_state.TextLeading);
            _textLineMatrix = t.Multiply(_textLineMatrix);
            _textMatrix = _textLineMatrix;
        }

        private void OpTJ(List<PdfToken> operands)
        {
            // [( str ) num ( str ) num ...] TJ
            bool inArray = false;

            foreach (PdfToken t in operands)
            {
                if (t.Type == PdfTokenType.ArrayStart)
                {
                    inArray = true;
                    continue;
                }

                if (t.Type == PdfTokenType.ArrayEnd)
                {
                    inArray = false;
                    continue;
                }

                if (!inArray)
                {
                    continue;
                }

                if (t.Type == PdfTokenType.LiteralString || t.Type == PdfTokenType.HexString)
                {
                    ShowText(ExtractString(t));
                }
                else if (t.Type == PdfTokenType.Integer || t.Type == PdfTokenType.Real)
                {
                    // Positive displacement = move BACK in text direction.
                    // Per §9.4.3: tx = -displacement/1000 * fontSize * (Th/100)
                    double disp = ParseDouble(t);
                    double tx = -disp / 1000.0 * _state.FontSize * (_state.HorizontalScaling / 100.0);
                    Transform tr = new Transform(1, 0, 0, 1, tx, 0);
                    _textMatrix = tr.Multiply(_textMatrix);
                }
            }
        }

        private void OpQuote(List<PdfToken> operands)
        {
            // ' — move to next line and show text
            OpTStar();

            if (operands.Count > 0)
            {
                ShowText(ExtractString(operands[0]));
            }
        }

        private void OpDoubleQuote(List<PdfToken> operands)
        {
            // " — aw ac string — set word/char spacing, move to next line, show
            if (operands.Count < 3)
            {
                return;
            }

            _state.WordSpacing = ParseDouble(operands[0]);
            _state.CharacterSpacing = ParseDouble(operands[1]);
            OpTStar();
            ShowText(ExtractString(operands[2]));
        }

        // ── Text showing ──────────────────────────────────────────────────

        private void ShowText(string text)
        {
            if (string.IsNullOrEmpty(text) || _state.FontSize <= 0)
            {
                return;
            }

            // Rendering mode 3 = invisible; skip emission but still advance.
            bool emit = _state.TextRenderingMode != 3;

            FontRenderer? renderer = GetFontRenderer();

            foreach (char c in text)
            {
                double advance;

                if (renderer is null)
                {
                    // No font available — approximate advance, no glyph emission
                    advance = 0.6 * _state.FontSize;
                }
                else
                {
                    GlyphOutline scaled = renderer.GetGlyphOutlineForChar(c).Scale(_state.FontSize);

                    if (emit && !scaled.IsEmpty && _state.FillValid)
                    {
                        // Glyph outline is in PDF text space with the
                        // font-size scale already applied. Compose:
                        //   final = textMatrix · ctm
                        // and apply to the glyph path.
                        Transform glyphPlacement = _textMatrix.Multiply(_state.Ctm);

                        // Apply text rise if non-zero
                        if (_state.TextRise != 0.0)
                        {
                            Transform rise = new Transform(1, 0, 0, 1, 0, _state.TextRise);
                            glyphPlacement = rise.Multiply(glyphPlacement);
                        }

                        Path placed = TransformPath(scaled.Outline, glyphPlacement);
                        _ops.Add(new DrawGlyphOp(placed, _state.FillColor, SnapshotClips()));
                    }

                    advance = scaled.Metrics.AdvanceWidthAt(_state.FontSize);
                }

                // Per §9.4.4: tx = (w + Tc + Tw·(c==space ? 1 : 0)) · Th/100
                double extra = _state.CharacterSpacing;

                if (c == ' ')
                {
                    extra += _state.WordSpacing;
                }

                double tx = (advance + extra) * (_state.HorizontalScaling / 100.0);

                Transform advanceMatrix = new Transform(1, 0, 0, 1, tx, 0);
                _textMatrix = advanceMatrix.Multiply(_textMatrix);
            }
        }

        // ── Font resolution ───────────────────────────────────────────────

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
                return null;
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
                    return DecodeStream(fontStream);
                }
            }

            return null;
        }

        // ── XObject Do ────────────────────────────────────────────────────

        private void OpDo(List<PdfToken> operands, PdfDictionary? resources)
        {
            if (operands.Count < 1 || resources is null)
            {
                return;
            }

            string name = ExtractName(operands[0]);

            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            if (!resources.TryGetValue(PdfName.Intern("XObject"), out PdfPrimitive? xobjDictRef))
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
                EmitFormXObject(xobjStream, resources);
            }
        }

        private void EmitImageXObject(PdfStream xobjStream)
        {
            byte[] imageBytes;

            try
            {
                imageBytes = DecodeStream(xobjStream);
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

            if (frame is null)
            {
                return;
            }

            _ops.Add(new DrawImageOp(frame, _state.Ctm, SnapshotClips()));
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
            Worker sub = new Worker(_objects);

            byte[] formContent;

            try
            {
                formContent = sub.DecodeStream(xobjStream);
            }
            catch (Exception)
            {
                return;
            }

            PageDisplayList inner;

            if (formContent.Length > 0)
            {
                sub.Interpret(formContent, formResources);
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

        // ── Token parsing helpers ─────────────────────────────────────────

        private static double ParseDouble(PdfToken token)
        {
            if (double.TryParse(token.RawText,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double v))
            {
                return v;
            }

            return 0.0;
        }

        private static long ParseInt(PdfToken token)
        {
            if (long.TryParse(token.RawText,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long v))
            {
                return v;
            }

            return 0;
        }

        private static PointF ParsePoint(List<PdfToken> operands, int startIndex)
        {
            return new PointF(
                ParseDouble(operands[startIndex]),
                ParseDouble(operands[startIndex + 1]));
        }

        private static string ExtractString(PdfToken token)
        {
            // Literal string and hex string both deliver bytes; we treat
            // bytes as Latin-1 for now (the rasterizer does the same).
            // Proper Unicode mapping via ToUnicode CMaps is future work.
            byte[] bytes = token.RawBytes;

            if (token.Type == PdfTokenType.HexString)
            {
                // Hex bytes are already decoded by the tokenizer
                char[] chars = new char[bytes.Length];

                for (int i = 0; i < bytes.Length; i++)
                {
                    chars[i] = (char)bytes[i];
                }

                return new string(chars);
            }

            // Literal string: handle escape sequences
            return DecodeLiteralString(bytes);
        }

        private static string DecodeLiteralString(byte[] bytes)
        {
            // Bytes from a literal string token may contain backslash
            // escapes per §7.3.4.2. We decode common ones.
            System.Text.StringBuilder sb = new System.Text.StringBuilder(bytes.Length);

            for (int i = 0; i < bytes.Length; i++)
            {
                byte b = bytes[i];

                if (b == (byte)'\\' && i + 1 < bytes.Length)
                {
                    byte next = bytes[i + 1];

                    switch (next)
                    {
                        case (byte)'n': sb.Append('\n'); i++; continue;
                        case (byte)'r': sb.Append('\r'); i++; continue;
                        case (byte)'t': sb.Append('\t'); i++; continue;
                        case (byte)'b': sb.Append('\b'); i++; continue;
                        case (byte)'f': sb.Append('\f'); i++; continue;
                        case (byte)'(': sb.Append('('); i++; continue;
                        case (byte)')': sb.Append(')'); i++; continue;
                        case (byte)'\\': sb.Append('\\'); i++; continue;
                    }
                }

                sb.Append((char)b);
            }

            return sb.ToString();
        }

        private static string ExtractName(PdfToken token)
        {
            if (token.Type != PdfTokenType.Name)
            {
                return string.Empty;
            }

            byte[] bytes = token.RawBytes;
            char[] chars = new char[bytes.Length];

            for (int i = 0; i < bytes.Length; i++)
            {
                chars[i] = (char)bytes[i];
            }

            return new string(chars);
        }
    }
}
