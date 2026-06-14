// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9 — Text
//        PDF 32000-1:2008 §8.4 — Graphics state
//        PDF 32000-1:2008 §7.8.2 — Content streams
// PHASE: Phase 2 — Chuvadi.Pdf.Redaction
// True PHI-safe content stream rewriting: removes text-showing operators
// whose device-space position intersects any redaction rectangle.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Filters;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Redaction;

/// <summary>
/// Applies true PHI-safe redactions to a PDF document. Text-showing operators
/// (Tj, TJ, ', '') whose visible position falls inside any redaction rectangle
/// are permanently removed from the content stream, then the area is overpainted
/// with an opaque rectangle for visual indication.
/// </summary>
/// <remarks>
/// The principle: cover-up alone is not redaction. Drawing a black rectangle on
/// top of text leaves the text in the content stream where Ctrl+A copy reveals
/// it. <see cref="Redactor"/> removes the text from the content stream itself
/// and only then paints the overlay rectangle.
///
/// Conservative principle: when in doubt, REDACT. If a TJ array contains any
/// string whose position is inside a redaction rectangle, the entire TJ is
/// dropped. Over-redaction is preferred over leaking PHI.
///
/// Limitations:
/// <list type="bullet">
///   <item>Phase 2 uses approximate font-metric width (Helvetica baseline).
///         Exact metric width requires loading and parsing embedded font tables.</item>
///   <item>Image content is not redacted (Phase 3).</item>
///   <item>Form XObjects are not recursed into (Phase 3).</item>
/// </list>
/// </remarks>
public static class Redactor
{
    /// <summary>
    /// Applies the redactions in <paramref name="options"/> to <paramref name="document"/>
    /// and writes the result to <paramref name="output"/>.
    /// </summary>
    public static void Apply(
        Stream output,
        PdfDocument document,
        RedactionOptions options)
    {
        if (output is null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        // Force-load all reachable objects from the page graph. PdfObjectStore is lazy
        // and only contains resolved objects, so we must walk the graph before iterating.
        PreloadAllObjects(document);

        // Resolve pattern rules into explicit rectangles by extracting text from each
        // page and matching the patterns against it.
        List<RedactionRect> allRects = new List<RedactionRect>(options.Rectangles);

        if (options.Patterns.Count > 0)
        {
            for (int p = 0; p < document.PageCount; p++)
            {
                List<RedactionRect> resolved = PatternMatcher.Resolve(
                    document, p, options.Patterns, options.PatternPadding);
                allRects.AddRange(resolved);
            }
        }

        // Group rectangles by page
        Dictionary<int, List<RectangleF>> byPage = new Dictionary<int, List<RectangleF>>();

        foreach (RedactionRect rect in allRects)
        {
            if (!byPage.TryGetValue(rect.PageIndex, out List<RectangleF>? list))
            {
                list = new List<RectangleF>();
                byPage[rect.PageIndex] = list;
            }

            list.Add(rect.Bounds);
        }

        FilterPipeline pipeline = FilterRegistry.CreateDefaultPipeline();

        // Find each page object in the source store
        Dictionary<int, PdfObjectId> pageIds = BuildPageIdMap(document);

        List<PdfIndirectObject> allObjects = new List<PdfIndirectObject>();
        HashSet<int> rewrittenPageNums = new HashSet<int>();
        HashSet<int> removedContentStreamNums = new HashSet<int>();

        int nextObjectNum = FindNextObjectNumber(document);

        List<RedactionWork> work = new List<RedactionWork>();

        foreach (KeyValuePair<int, List<RectangleF>> kvp in byPage)
        {
            int pageIndex = kvp.Key;

            if (pageIndex >= document.PageCount)
            {
                continue;
            }

            if (!pageIds.TryGetValue(pageIndex, out PdfObjectId pageId))
            {
                continue;
            }

            PdfPage page = document.Pages[pageIndex];
            work.Add(new RedactionWork(pageId, page, kvp.Value));
        }

        // Phase 1 (serial) — touches the lazy object store, which is NOT
        // thread-safe (a cache miss writes back), so loading and content-stream
        // tracking must stay single-threaded.
        byte[][] originals = new byte[work.Count][];
        for (int i = 0; i < work.Count; i++)
        {
            TrackOriginalContentStreams(work[i].Page, document.Objects, removedContentStreamNums);
            originals[i] = LoadContentBytes(work[i].Page, document.Objects, pipeline);
        }

        // Phase 2 (parallel-capable) — the redaction interpreter and overlay
        // generation are pure functions of the page bytes and rectangles, with
        // no shared state, so they can run concurrently. Opt-in via
        // MaxDegreeOfParallelism (default 1 = sequential).
        byte[][] redactedBytes = new byte[work.Count][];
        byte[][] overlayBytes = new byte[work.Count][];

        if (options.MaxDegreeOfParallelism == 1)
        {
            for (int i = 0; i < work.Count; i++)
            {
                redactedBytes[i] = RewriteContent(originals[i], work[i].Rects);
                overlayBytes[i] = BuildOverlay(work[i].Rects, options.OverlayColor);
            }
        }
        else
        {
            ParallelOptions parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = options.MaxDegreeOfParallelism <= 0
                    ? -1
                    : options.MaxDegreeOfParallelism,
            };

            Parallel.For(0, work.Count, parallelOptions, i =>
            {
                redactedBytes[i] = RewriteContent(originals[i], work[i].Rects);
                overlayBytes[i] = BuildOverlay(work[i].Rects, options.OverlayColor);
            });
        }

        // Phase 3 (serial) — allocate object numbers and assemble the output in
        // stable order, so the result is identical regardless of parallelism.
        for (int i = 0; i < work.Count; i++)
        {
            PdfObjectId redactedId = new PdfObjectId(nextObjectNum++, 0);
            PdfObjectId overlayId = new PdfObjectId(nextObjectNum++, 0);

            PdfDictionary redactedDict = new PdfDictionary();
            redactedDict.Set(PdfName.Length, redactedBytes[i].Length);
            allObjects.Add(new PdfIndirectObject(
                redactedId, new PdfStream(redactedDict, redactedBytes[i])));

            PdfDictionary overlayDict = new PdfDictionary();
            overlayDict.Set(PdfName.Length, overlayBytes[i].Length);
            allObjects.Add(new PdfIndirectObject(
                overlayId, new PdfStream(overlayDict, overlayBytes[i])));

            PdfDictionary modifiedPage = CopyDictionary(work[i].Page.Dictionary);
            PdfArray contents = new PdfArray([
                new PdfReference(redactedId),
                new PdfReference(overlayId),
            ]);
            modifiedPage.Set(PdfName.Contents, contents);
            allObjects.Add(new PdfIndirectObject(work[i].PageId, modifiedPage));
            rewrittenPageNums.Add(work[i].PageId.ObjectNumber);
        }

        // Copy untouched objects (excluding modified pages and replaced content streams)
        foreach (PdfIndirectObject obj in document.Objects.Objects)
        {
            if (rewrittenPageNums.Contains(obj.Id.ObjectNumber))
            {
                continue;
            }

            if (removedContentStreamNums.Contains(obj.Id.ObjectNumber))
            {
                continue;
            }

            allObjects.Add(obj);
        }

        // Build trailer with catalog reference
        PdfDictionary trailer = new PdfDictionary();

        foreach (PdfIndirectObject obj in document.Objects.Objects)
        {
            if (obj.Value is PdfDictionary dict &&
                dict.TryGetValue(PdfName.Type, out PdfPrimitive? t) &&
                t is PdfName tn && tn.Value == "Catalog")
            {
                trailer.Set(PdfName.Root, new PdfReference(obj.Id));
                break;
            }
        }

        PdfWriter.Write(output, allObjects, trailer);
    }

    /// <summary>
    /// Per-page work item carried between the redaction phases: the page object
    /// id, the page, and the rectangles to redact on it.
    /// </summary>
    private readonly struct RedactionWork
    {
        public RedactionWork(PdfObjectId pageId, PdfPage page, List<RectangleF> rects)
        {
            PageId = pageId;
            Page = page;
            Rects = rects;
        }

        public PdfObjectId PageId { get; }

        public PdfPage Page { get; }

        public List<RectangleF> Rects { get; }
    }

    // ── Content stream rewriter ───────────────────────────────────────────

    private static byte[] RewriteContent(byte[] content, List<RectangleF> rects)
    {
        using (MemoryStream input = new MemoryStream(content))
        using (MemoryStream output = new MemoryStream())
        using (PdfTokenizer tok = new PdfTokenizer(input))
        {
            RedactState state = new RedactState();
            List<PdfToken> pendingOperands = new List<PdfToken>();

            while (true)
            {
                PdfToken token = tok.Read();

                if (token.IsEndOfStream)
                {
                    break;
                }

                if (token.Type == PdfTokenType.ArrayStart)
                {
                    // Collect entire array as one logical operand group
                    pendingOperands.Add(token);

                    while (true)
                    {
                        PdfToken inner = tok.Read();

                        if (inner.IsEndOfStream || inner.Type == PdfTokenType.ArrayEnd)
                        {
                            pendingOperands.Add(inner);
                            break;
                        }

                        pendingOperands.Add(inner);
                    }

                    continue;
                }

                if (token.Type != PdfTokenType.Keyword)
                {
                    pendingOperands.Add(token);
                    continue;
                }

                string op = token.RawText;
                bool drop = ProcessOperator(op, pendingOperands, state, rects);

                if (!drop)
                {
                    WriteTokens(output, pendingOperands);
                    output.Write(Encoding.Latin1.GetBytes(op + "\n"), 0,
                        Encoding.Latin1.GetByteCount(op + "\n"));
                }

                pendingOperands.Clear();
            }

            return output.ToArray();
        }
    }

    /// <summary>
    /// Returns true when the operator-with-operands should be dropped
    /// (i.e., its visible text intersects a redaction rectangle).
    /// </summary>
    private static bool ProcessOperator(
        string op, List<PdfToken> operands,
        RedactState state, List<RectangleF> rects)
    {
        switch (op)
        {
            // ── Graphics state ─────────────────────────────────────────────
            case "q": state.PushGraphicsState(); return false;
            case "Q": state.PopGraphicsState(); return false;
            case "cm": ApplyCm(operands, state); return false;

            // ── Text state ─────────────────────────────────────────────────
            case "BT": state.BeginText(); return false;
            case "ET": state.EndText(); return false;
            case "Tf": ApplyTf(operands, state); return false;
            case "Td": ApplyTd(operands, state); return false;
            case "TD": ApplyTD(operands, state); return false;
            case "Tm": ApplyTm(operands, state); return false;
            case "T*": state.NextLine(); return false;

            // ── Text-showing operators ─────────────────────────────────────
            case "Tj":
                return ShouldRedactTj(operands, state, rects);

            case "TJ":
                return ShouldRedactTJ(operands, state, rects);

            case "'":
                state.NextLine();
                return ShouldRedactTj(operands, state, rects);

            case "\"":
                // " : aw ac string '
                if (operands.Count >= 3)
                {
                    state.NextLine();
                    // Pass only the string operand to detection
                    List<PdfToken> stringOnly = new List<PdfToken> { operands[2] };
                    return ShouldRedactTj(stringOnly, state, rects);
                }
                return false;

            // ── Image / XObject painting (Phase 1.1.3) ─────────────────────
            case "Do":
                // Paints the XObject named by the operand at the current CTM.
                // The image (or form) occupies the unit square [0,1]×[0,1] in
                // its local space. We compute the device-space bounding box of
                // the four corners and drop the Do if any rect intersects.
                return ShouldRedactImageAtCtm(state, rects);

            default:
                return false;
        }
    }

    /// <summary>
    /// Returns true if the unit square (0,0)-(1,1) transformed by the current CTM
    /// intersects any redaction rectangle on this page.
    /// </summary>
    private static bool ShouldRedactImageAtCtm(RedactState state, List<RectangleF> rects)
    {
        if (rects.Count == 0)
        {
            return false;
        }

        // Four corners of the unit square in local space.
        PointF tl = state.Ctm.TransformPoint(new PointF(0, 0));
        PointF tr = state.Ctm.TransformPoint(new PointF(1, 0));
        PointF bl = state.Ctm.TransformPoint(new PointF(0, 1));
        PointF br = state.Ctm.TransformPoint(new PointF(1, 1));

        double minX = Math.Min(Math.Min(tl.X, tr.X), Math.Min(bl.X, br.X));
        double maxX = Math.Max(Math.Max(tl.X, tr.X), Math.Max(bl.X, br.X));
        double minY = Math.Min(Math.Min(tl.Y, tr.Y), Math.Min(bl.Y, br.Y));
        double maxY = Math.Max(Math.Max(tl.Y, tr.Y), Math.Max(bl.Y, br.Y));

        foreach (RectangleF r in rects)
        {
            double rMinX = r.X;
            double rMaxX = r.X + r.Width;
            double rMinY = r.Y;
            double rMaxY = r.Y + r.Height;

            if (minX < rMaxX && maxX > rMinX && minY < rMaxY && maxY > rMinY)
            {
                return true;
            }
        }

        return false;
    }

    // ── Operator state updates ────────────────────────────────────────────

    private static void ApplyCm(List<PdfToken> operands, RedactState state)
    {
        if (operands.Count < 6)
        {
            return;
        }

        Transform ctm = new Transform(
            ParseDouble(operands[0]), ParseDouble(operands[1]),
            ParseDouble(operands[2]), ParseDouble(operands[3]),
            ParseDouble(operands[4]), ParseDouble(operands[5]));
        state.Ctm = ctm.Multiply(state.Ctm);
    }

    private static void ApplyTf(List<PdfToken> operands, RedactState state)
    {
        if (operands.Count >= 2)
        {
            state.FontSize = ParseDouble(operands[1]);
        }
    }

    private static void ApplyTd(List<PdfToken> operands, RedactState state)
    {
        if (operands.Count >= 2)
        {
            double tx = ParseDouble(operands[0]);
            double ty = ParseDouble(operands[1]);
            state.TextLineX += tx;
            state.TextLineY += ty;
            state.TextX = state.TextLineX;
            state.TextY = state.TextLineY;
        }
    }

    private static void ApplyTD(List<PdfToken> operands, RedactState state)
    {
        if (operands.Count >= 2)
        {
            double ty = ParseDouble(operands[1]);
            state.Leading = -ty;
            ApplyTd(operands, state);
        }
    }

    private static void ApplyTm(List<PdfToken> operands, RedactState state)
    {
        if (operands.Count < 6)
        {
            return;
        }

        state.TextMatrix = new Transform(
            ParseDouble(operands[0]), ParseDouble(operands[1]),
            ParseDouble(operands[2]), ParseDouble(operands[3]),
            ParseDouble(operands[4]), ParseDouble(operands[5]));
        state.TextX = ParseDouble(operands[4]);
        state.TextY = ParseDouble(operands[5]);
        state.TextLineX = state.TextX;
        state.TextLineY = state.TextY;
    }

    // ── Text redaction decisions ──────────────────────────────────────────

    private static bool ShouldRedactTj(
        List<PdfToken> operands, RedactState state, List<RectangleF> rects)
    {
        if (operands.Count == 0)
        {
            return false;
        }

        PdfToken stringToken = operands[operands.Count - 1];

        if (stringToken.Type != PdfTokenType.LiteralString &&
            stringToken.Type != PdfTokenType.HexString)
        {
            return false;
        }

        string text = ExtractString(stringToken);
        return IsTextInRedactRect(text, state, rects);
    }

    private static bool ShouldRedactTJ(
        List<PdfToken> operands, RedactState state, List<RectangleF> rects)
    {
        // TJ: array of strings and kerning numbers.
        // Conservative: if ANY string in the array is in a redaction rect, drop the entire TJ.
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
                string s = ExtractString(t);

                if (IsTextInRedactRect(s, state, rects))
                {
                    return true; // drop entire TJ
                }
            }
        }

        return false;
    }

    private static bool IsTextInRedactRect(
        string text, RedactState state, List<RectangleF> rects)
    {
        if (string.IsNullOrEmpty(text) || state.FontSize <= 0)
        {
            return false;
        }

        // Approximate text bounding box in user space:
        //   width  ≈ length × fontSize × 0.6 (Helvetica baseline)
        //   height ≈ fontSize
        double width = text.Length * state.FontSize * 0.6;
        double height = state.FontSize;

        // Origin: TextX/TextY in text space; transform by CTM (text matrix is already
        // applied via TextX/TextY tracking for simple cases)
        Transform combined = state.TextMatrix.Multiply(state.Ctm);
        PointF originDev = combined.TransformPoint(new PointF(state.TextX, state.TextY));
        PointF endDev = combined.TransformPoint(
            new PointF(state.TextX + width, state.TextY + height));

        RectangleF textBox = RectangleF.FromCorners(
            originDev.X, originDev.Y, endDev.X, endDev.Y);

        foreach (RectangleF r in rects)
        {
            RectangleF intersection = textBox.Intersect(r);

            if (!intersection.IsEmpty)
            {
                return true;
            }
        }

        return false;
    }

    // ── Overlay generation ────────────────────────────────────────────────

    private static byte[] BuildOverlay(List<RectangleF> rects, ColorF overlayColor)
    {
        ColorF rgb = overlayColor.ToRgb();
        string r = Fmt(rgb.R);
        string g = Fmt(rgb.G);
        string b = Fmt(rgb.B);

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("q");
        sb.AppendLine($"{r} {g} {b} rg");

        foreach (RectangleF rect in rects)
        {
            sb.AppendLine($"{Fmt(rect.X)} {Fmt(rect.Y)} {Fmt(rect.Width)} {Fmt(rect.Height)} re");
            sb.AppendLine("f");
        }

        sb.AppendLine("Q");
        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    // ── Content stream loading and re-emission ────────────────────────────

    private static byte[] LoadContentBytes(
        PdfPage page, PdfObjectStore store, FilterPipeline pipeline)
    {
        PdfPrimitive? contents = page.Contents;

        if (contents is null || contents is PdfNull)
        {
            return [];
        }

        PdfPrimitive resolved = store.Resolve(contents);

        if (resolved is PdfStream stream)
        {
            return DecodeStream(stream, pipeline);
        }

        if (resolved is PdfArray array)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                for (int i = 0; i < array.Count; i++)
                {
                    PdfPrimitive item = store.Resolve(array[i]);

                    if (item is PdfStream s)
                    {
                        byte[] decoded = DecodeStream(s, pipeline);
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

        return [];
    }

    private static byte[] DecodeStream(PdfStream stream, FilterPipeline pipeline)
    {
        if (!stream.IsFiltered)
        {
            return stream.RawBytes;
        }

        PdfPrimitive? filter = stream.Filter;

        if (filter is PdfName fn)
        {
            string resolvedFilter = FilterRegistry.ResolveAlias(fn.Value);
            return pipeline.Decode(resolvedFilter, stream.RawBytes, null);
        }

        if (filter is PdfArray fa)
        {
            byte[] data = stream.RawBytes;

            for (int i = 0; i < fa.Count; i++)
            {
                PdfName? n = fa.GetAs<PdfName>(i);

                if (n is null)
                {
                    continue;
                }

                string resolvedFilter = FilterRegistry.ResolveAlias(n.Value);
                data = pipeline.Decode(resolvedFilter, data, null);
            }

            return data;
        }

        return stream.RawBytes;
    }

    private static void WriteTokens(MemoryStream output, List<PdfToken> tokens)
    {
        foreach (PdfToken t in tokens)
        {
            // PdfTokenizer.ReadName strips the leading '/' from a Name token's
            // RawBytes (it stores just the name content). We re-prepend it here
            // when serialising back to a content stream.
            if (t.Type == PdfTokenType.Name)
            {
                output.WriteByte((byte)'/');
            }
            output.Write(t.RawBytes, 0, t.RawBytes.Length);
            output.WriteByte(32);
        }
    }

    // ── Page object discovery ─────────────────────────────────────────────

    private static Dictionary<int, PdfObjectId> BuildPageIdMap(PdfDocument document)
    {
        Dictionary<int, PdfObjectId> map = new Dictionary<int, PdfObjectId>();
        int idx = 0;

        foreach (PdfIndirectObject obj in document.Objects.Objects)
        {
            if (obj.Value is not PdfDictionary dict)
            {
                continue;
            }

            if (!dict.TryGetValue(PdfName.Type, out PdfPrimitive? typePrim))
            {
                continue;
            }

            if (typePrim is PdfName typeName && typeName.Value == "Page")
            {
                map[idx++] = obj.Id;
            }
        }

        return map;
    }

    private static int FindNextObjectNumber(PdfDocument document)
    {
        int max = 0;

        foreach (PdfIndirectObject obj in document.Objects.Objects)
        {
            if (obj.Id.ObjectNumber > max)
            {
                max = obj.Id.ObjectNumber;
            }
        }

        return max + 1;
    }

    // ── Object graph preload ──────────────────────────────────────────────

    /// <summary>
    /// Forces all reachable objects from the page graph into the document's
    /// object cache. PdfObjectStore is lazy and only contains what has been
    /// explicitly resolved; without this preload, iterating Objects.Objects
    /// returns an incomplete snapshot and the output PDF loses content streams.
    /// </summary>
    private static void PreloadAllObjects(PdfDocument document)
    {
        HashSet<int> visited = new HashSet<int>();
        int pageCount = document.PageCount;

        for (int i = 0; i < pageCount; i++)
        {
            PdfPage page = document.Pages[i];
            Visit(document.Objects, page.Dictionary, visited);
        }
    }

    private static void Visit(PdfObjectStore store, PdfPrimitive? p, HashSet<int> visited)
    {
        if (p is null)
        {
            return;
        }

        if (p is PdfReference reference)
        {
            int num = reference.ObjectId.ObjectNumber;

            if (!visited.Add(num))
            {
                return;
            }

            PdfPrimitive resolved = store.Resolve(reference);
            Visit(store, resolved, visited);
            return;
        }

        if (p is PdfArray arr)
        {
            for (int i = 0; i < arr.Count; i++)
            {
                Visit(store, arr[i], visited);
            }
            return;
        }

        if (p is PdfDictionary dict)
        {
            foreach (KeyValuePair<PdfName, PdfPrimitive> entry in dict)
            {
                Visit(store, entry.Value, visited);
            }
            return;
        }

        if (p is PdfStream stream)
        {
            Visit(store, stream.Dictionary, visited);
        }
    }

    /// <summary>
    /// Records the object numbers of the page's original content streams so
    /// they can be excluded from the output. Critical for PHI safety: leaving
    /// the original stream in the output would allow direct object retrieval
    /// to recover redacted text.
    /// </summary>
    private static void TrackOriginalContentStreams(
        PdfPage page, PdfObjectStore store, HashSet<int> set)
    {
        PdfPrimitive? contents = page.Contents;

        if (contents is null || contents is PdfNull)
        {
            return;
        }

        if (contents is PdfReference reference)
        {
            set.Add(reference.ObjectId.ObjectNumber);
        }

        PdfPrimitive resolved = store.Resolve(contents);

        if (resolved is PdfArray arr)
        {
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] is PdfReference r)
                {
                    set.Add(r.ObjectId.ObjectNumber);
                }
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static double ParseDouble(PdfToken token)
    {
        if (double.TryParse(token.RawText, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
        {
            return v;
        }

        return 0;
    }

    private static string ExtractString(PdfToken token)
    {
        string raw = token.RawText;

        if (token.Type == PdfTokenType.LiteralString)
        {
            if (raw.Length >= 2 && raw[0] == '(' && raw[raw.Length - 1] == ')')
            {
                return raw.Substring(1, raw.Length - 2);
            }
        }
        else if (token.Type == PdfTokenType.HexString)
        {
            if (raw.Length >= 2 && raw[0] == '<' && raw[raw.Length - 1] == '>')
            {
                string hex = raw.Substring(1, raw.Length - 2);
                StringBuilder sb = new StringBuilder(hex.Length / 2);

                for (int i = 0; i + 1 < hex.Length; i += 2)
                {
                    if (byte.TryParse(hex.Substring(i, 2), NumberStyles.HexNumber, null, out byte b))
                    {
                        sb.Append((char)b);
                    }
                }

                return sb.ToString();
            }
        }

        return raw;
    }

    private static PdfDictionary CopyDictionary(PdfDictionary source)
    {
        PdfDictionary copy = new PdfDictionary();

        foreach (KeyValuePair<PdfName, PdfPrimitive> entry in source)
        {
            copy.Set(entry.Key, entry.Value);
        }

        return copy;
    }

    private static string Fmt(double v)
    {
        return v.ToString("F6", CultureInfo.InvariantCulture);
    }
}

// ── Internal redaction state ──────────────────────────────────────────────

internal sealed class RedactState
{
    private readonly Stack<Transform> _ctmStack;

    internal RedactState()
    {
        _ctmStack = new Stack<Transform>();
        Ctm = Transform.Identity;
        TextMatrix = Transform.Identity;
        FontSize = 12.0;
    }

    internal Transform Ctm { get; set; }
    internal Transform TextMatrix { get; set; }
    internal double TextX { get; set; }
    internal double TextY { get; set; }
    internal double TextLineX { get; set; }
    internal double TextLineY { get; set; }
    internal double FontSize { get; set; }
    internal double Leading { get; set; }

    internal void PushGraphicsState()
    {
        _ctmStack.Push(Ctm);
    }

    internal void PopGraphicsState()
    {
        if (_ctmStack.Count > 0)
        {
            Ctm = _ctmStack.Pop();
        }
    }

    internal void BeginText()
    {
        TextMatrix = Transform.Identity;
        TextX = 0;
        TextY = 0;
        TextLineX = 0;
        TextLineY = 0;
    }

    internal void EndText()
    {
        // No-op: text state is reset on next BT
    }

    internal void NextLine()
    {
        TextLineY -= Leading;
        TextX = TextLineX;
        TextY = TextLineY;
    }
}
