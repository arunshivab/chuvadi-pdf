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

        // Build the form-redaction registry: walk each page (and nested forms)
        // to record where every form XObject is placed, so its own content
        // stream can be rewritten. Text inside a form is otherwise never removed
        // — the form is only covered by the overlay, leaving it copyable.
        RedactRun run = new RedactRun(document.Objects, pipeline);
        for (int i = 0; i < work.Count; i++)
        {
            CollectForms(
                originals[i], PageContexts(work[i].Rects), run,
                work[i].Page.Resources, new HashSet<int>(), 0);
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
                redactedBytes[i] = RewriteContent(originals[i], PageContexts(work[i].Rects), run, work[i].Page.Resources);
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
                redactedBytes[i] = RewriteContent(originals[i], PageContexts(work[i].Rects), run, work[i].Page.Resources);
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

        RewriteCollectedForms(run, allObjects, removedContentStreamNums);


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

    private static byte[] RewriteContent(
        byte[] content, IReadOnlyList<RedactContext> contexts, RedactRun run, PdfDictionary? resources)
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

                // Inline image: BI <dict> ID <binary> EI. The binary data is not
                // tokenisable, so consume the whole run as a unit and decide
                // redaction from the CTM (it paints the unit square, like Do).
                if (token.RawText == "BI")
                {
                    HandleInlineImage(tok, content, output, state, contexts, token.ByteOffset);
                    pendingOperands.Clear();
                    continue;
                }

                string op = token.RawText;
                bool drop = ProcessOperator(op, pendingOperands, state, contexts, run, resources);

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
        RedactState state, IReadOnlyList<RedactContext> contexts, RedactRun run, PdfDictionary? resources)
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
                return ShouldRedactTj(operands, state, contexts);

            case "TJ":
                return ShouldRedactTJ(operands, state, contexts);

            case "'":
                state.NextLine();
                return ShouldRedactTj(operands, state, contexts);

            case "\"":
                // " : aw ac string '
                if (operands.Count >= 3)
                {
                    state.NextLine();
                    // Pass only the string operand to detection
                    List<PdfToken> stringOnly = new List<PdfToken> { operands[2] };
                    return ShouldRedactTj(stringOnly, state, contexts);
                }
                return false;

            // ── Image / form XObject painting ──────────────────────────────
            case "Do":
                // A form XObject's text is redacted inside its own content stream
                // (collected and rewritten separately), so the Do is kept. An
                // image XObject is dropped when its placement intersects a rect.
                return IsFormXObject(operands, run, resources)
                    ? false
                    : ShouldRedactImageAtCtm(state, contexts);

            default:
                return false;
        }
    }

    /// <summary>
    /// Returns true if the unit square (0,0)-(1,1) transformed by the current CTM
    /// intersects any redaction rectangle on this page.
    /// </summary>
    /// <summary>
    /// Consumes an inline image (<c>BI … ID &lt;binary&gt; EI</c>) as one unit.
    /// The image is dropped when the unit square mapped by the current CTM
    /// intersects a redaction rectangle (inline images paint the unit square,
    /// like <c>Do</c> images); otherwise the original bytes are copied verbatim.
    /// </summary>
    private static void HandleInlineImage(
        PdfTokenizer tok, byte[] content, MemoryStream output,
        RedactState state, IReadOnlyList<RedactContext> contexts, long biStart)
    {
        // Read the inline-image dictionary tokens until the ID keyword; the
        // binary image data begins immediately after it.
        while (true)
        {
            PdfToken t = tok.Read();
            if (t.IsEndOfStream)
            {
                // Malformed (no ID): copy from BI to end and stop.
                output.Write(content, (int)biStart, content.Length - (int)biStart);
                return;
            }

            if (t.Type == PdfTokenType.Keyword && t.RawText == "ID")
            {
                break;
            }
        }

        int eiEnd = FindInlineImageEnd(content, (int)tok.Position);

        if (!ShouldRedactImageAtCtm(state, contexts))
        {
            output.Write(content, (int)biStart, eiEnd - (int)biStart);
            output.WriteByte((byte)'\n');
        }

        tok.Seek(eiEnd);
    }

    /// <summary>
    /// Finds the offset just past the <c>EI</c> that ends an inline image's
    /// data, scanning from <paramref name="dataStart"/> for a whitespace-
    /// delimited <c>EI</c>. Returns the content length if none is found.
    /// </summary>
    private static int FindInlineImageEnd(byte[] content, int dataStart)
    {
        for (int i = dataStart; i + 1 < content.Length; i++)
        {
            if (content[i] == (byte)'E' && content[i + 1] == (byte)'I'
                && (i == 0 || IsWhitespaceByte(content[i - 1]))
                && (i + 2 >= content.Length || IsWhitespaceByte(content[i + 2])))
            {
                return i + 2;
            }
        }

        return content.Length;
    }

    private static bool IsWhitespaceByte(byte b) =>
        b == 0x00 || b == 0x09 || b == 0x0A || b == 0x0C || b == 0x0D || b == 0x20;

    private static bool ShouldRedactImageAtCtm(RedactState state, IReadOnlyList<RedactContext> contexts)
    {
        // Four corners of the unit square in local space.
        PointF tl = state.Ctm.TransformPoint(new PointF(0, 0));
        PointF tr = state.Ctm.TransformPoint(new PointF(1, 0));
        PointF bl = state.Ctm.TransformPoint(new PointF(0, 1));
        PointF br = state.Ctm.TransformPoint(new PointF(1, 1));

        for (int c = 0; c < contexts.Count; c++)
        {
            RedactContext context = contexts[c];
            if (context.Rects.Count == 0)
            {
                continue;
            }

            // Map the local-space corners into this context's page device space.
            PointF dtl = context.BaseCtm.TransformPoint(tl);
            PointF dtr = context.BaseCtm.TransformPoint(tr);
            PointF dbl = context.BaseCtm.TransformPoint(bl);
            PointF dbr = context.BaseCtm.TransformPoint(br);

            double minX = Math.Min(Math.Min(dtl.X, dtr.X), Math.Min(dbl.X, dbr.X));
            double maxX = Math.Max(Math.Max(dtl.X, dtr.X), Math.Max(dbl.X, dbr.X));
            double minY = Math.Min(Math.Min(dtl.Y, dtr.Y), Math.Min(dbl.Y, dbr.Y));
            double maxY = Math.Max(Math.Max(dtl.Y, dtr.Y), Math.Max(dbl.Y, dbr.Y));

            foreach (RectangleF r in context.Rects)
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
        List<PdfToken> operands, RedactState state, IReadOnlyList<RedactContext> contexts)
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
        return IsTextInRedactRect(text, state, contexts);
    }

    private static bool ShouldRedactTJ(
        List<PdfToken> operands, RedactState state, IReadOnlyList<RedactContext> contexts)
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

                if (IsTextInRedactRect(s, state, contexts))
                {
                    return true; // drop entire TJ
                }
            }
        }

        return false;
    }

    private static bool IsTextInRedactRect(
        string text, RedactState state, IReadOnlyList<RedactContext> contexts)
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

        // Local-space placement (text matrix × current CTM). For a form being
        // rewritten this is form-local; each context's BaseCtm then maps it into
        // the page device space where the rectangles live.
        Transform local = state.TextMatrix.Multiply(state.Ctm);

        for (int c = 0; c < contexts.Count; c++)
        {
            RedactContext context = contexts[c];
            Transform combined = local.Multiply(context.BaseCtm);
            PointF originDev = combined.TransformPoint(new PointF(state.TextX, state.TextY));
            PointF endDev = combined.TransformPoint(
                new PointF(state.TextX + width, state.TextY + height));

            RectangleF textBox = RectangleF.FromCorners(
                originDev.X, originDev.Y, endDev.X, endDev.Y);

            for (int r = 0; r < context.Rects.Count; r++)
            {
                if (!textBox.Intersect(context.Rects[r]).IsEmpty)
                {
                    return true;
                }
            }
        }

        return false;
    }

    // ── Form XObject resolution and collection ────────────────────────────

    // Wraps a page's rectangles as a single identity-placed redaction context.
    private static List<RedactContext> PageContexts(List<RectangleF> rects)
    {
        return new List<RedactContext> { new RedactContext(Transform.Identity, rects) };
    }

    // True when the XObject named by the Do operand resolves to a form.
    private static bool IsFormXObject(List<PdfToken> operands, RedactRun run, PdfDictionary? resources)
    {
        (int ObjNum, PdfStream Stream)? resolved = ResolveXObjectRef(operands, resources, run.Store);
        return resolved is not null && IsForm(resolved.Value.Stream);
    }

    private static bool IsForm(PdfStream stream)
    {
        return stream.Dictionary.TryGetValue(PdfName.Subtype, out PdfPrimitive? subtype)
            && subtype is PdfName name
            && name.Value == "Form";
    }

    // Resolves the XObject named by the trailing Do operand to its object number
    // and stream, or null when it cannot be resolved to an indirect stream.
    private static (int ObjNum, PdfStream Stream)? ResolveXObjectRef(
        List<PdfToken> operands, PdfDictionary? resources, PdfObjectStore store)
    {
        if (resources is null || operands.Count == 0)
        {
            return null;
        }

        PdfToken nameToken = operands[operands.Count - 1];
        if (nameToken.Type != PdfTokenType.Name)
        {
            return null;
        }

        string name = nameToken.RawText.TrimStart('/');

        if (!resources.TryGetValue(PdfName.XObject, out PdfPrimitive? xobjValue) ||
            store.Resolve(xobjValue) is not PdfDictionary xobjects)
        {
            return null;
        }

        if (!xobjects.TryGetValue(PdfName.Intern(name), out PdfPrimitive? entry))
        {
            return null;
        }

        int objNum = entry is PdfReference reference ? reference.ObjectNumber : -1;
        if (store.Resolve(entry) is not PdfStream stream)
        {
            return null;
        }

        return (objNum, stream);
    }

    private static Transform GetFormMatrix(PdfStream form, PdfObjectStore store)
    {
        if (form.Dictionary.TryGetValue(PdfName.Intern("Matrix"), out PdfPrimitive? value) &&
            store.Resolve(value) is PdfArray array && array.Count >= 6)
        {
            return new Transform(
                PdfReal.ToDouble(store.Resolve(array[0])), PdfReal.ToDouble(store.Resolve(array[1])),
                PdfReal.ToDouble(store.Resolve(array[2])), PdfReal.ToDouble(store.Resolve(array[3])),
                PdfReal.ToDouble(store.Resolve(array[4])), PdfReal.ToDouble(store.Resolve(array[5])));
        }

        return Transform.Identity;
    }

    private static PdfDictionary? ResolveFormResources(
        PdfStream form, PdfDictionary? parentResources, PdfObjectStore store)
    {
        if (form.Dictionary.TryGetValue(PdfName.Resources, out PdfPrimitive? value) &&
            store.Resolve(value) is PdfDictionary resources)
        {
            return resources;
        }

        return parentResources;
    }

    // Walks a content stream tracking the CTM and, at each form Do, records the
    // form's absolute placement (and the page rects) so its own content stream
    // can later be rewritten. Recurses into nested forms with a cycle guard.
    private static void CollectForms(
        byte[] content, IReadOnlyList<RedactContext> contexts, RedactRun run,
        PdfDictionary? resources, HashSet<int> activeStack, int depth)
    {
        if (depth > 32)
        {
            return;
        }

        using (MemoryStream input = new MemoryStream(content))
        using (PdfTokenizer tok = new PdfTokenizer(input))
        {
            RedactState state = new RedactState();
            List<PdfToken> pending = new List<PdfToken>();

            while (true)
            {
                PdfToken token = tok.Read();
                if (token.IsEndOfStream)
                {
                    break;
                }

                if (token.Type == PdfTokenType.ArrayStart)
                {
                    pending.Add(token);
                    while (true)
                    {
                        PdfToken inner = tok.Read();
                        pending.Add(inner);
                        if (inner.IsEndOfStream || inner.Type == PdfTokenType.ArrayEnd)
                        {
                            break;
                        }
                    }
                    continue;
                }

                if (token.Type != PdfTokenType.Keyword)
                {
                    pending.Add(token);
                    continue;
                }

                string op = token.RawText;
                if (op == "BI")
                {
                    SkipInlineImage(tok, content);
                    pending.Clear();
                    continue;
                }

                switch (op)
                {
                    case "q": state.PushGraphicsState(); break;
                    case "Q": state.PopGraphicsState(); break;
                    case "cm": ApplyCm(pending, state); break;
                    case "Do": CollectFormDo(pending, state, contexts, run, resources, activeStack, depth); break;
                    default: break;
                }

                pending.Clear();
            }
        }
    }

    private static void CollectFormDo(
        List<PdfToken> operands, RedactState state, IReadOnlyList<RedactContext> contexts,
        RedactRun run, PdfDictionary? resources, HashSet<int> activeStack, int depth)
    {
        (int ObjNum, PdfStream Stream)? resolved = ResolveXObjectRef(operands, resources, run.Store);
        if (resolved is null)
        {
            return;
        }

        int objNum = resolved.Value.ObjNum;
        PdfStream stream = resolved.Value.Stream;
        if (objNum < 0 || !IsForm(stream))
        {
            return;
        }

        Transform formMatrix = GetFormMatrix(stream, run.Store);
        List<RedactContext> formContexts = new List<RedactContext>(contexts.Count);
        foreach (RedactContext ctx in contexts)
        {
            Transform baseCtm = formMatrix.Multiply(state.Ctm).Multiply(ctx.BaseCtm);
            formContexts.Add(new RedactContext(baseCtm, ctx.Rects));
        }

        lock (run.Sync)
        {
            if (!run.FormInvocations.TryGetValue(objNum, out List<RedactContext>? list))
            {
                list = new List<RedactContext>();
                run.FormInvocations[objNum] = list;
            }
            list.AddRange(formContexts);
        }

        if (activeStack.Contains(objNum))
        {
            return;
        }

        activeStack.Add(objNum);
        byte[] formBytes = DecodeStream(stream, run.Pipeline);
        PdfDictionary? formResources = ResolveFormResources(stream, resources, run.Store);
        CollectForms(formBytes, formContexts, run, formResources, activeStack, depth + 1);
        activeStack.Remove(objNum);
    }

    // Consumes an inline image during collection (no output needed).
    private static void SkipInlineImage(PdfTokenizer tok, byte[] content)
    {
        while (true)
        {
            PdfToken t = tok.Read();
            if (t.IsEndOfStream)
            {
                return;
            }
            if (t.Type == PdfTokenType.Keyword && t.RawText == "ID")
            {
                break;
            }
        }

        int eiEnd = FindInlineImageEnd(content, (int)tok.Position);
        tok.Seek(eiEnd);
    }

    // Rewrites every collected form XObject's content stream in place (same
    // object id), removing in-rect text under any of its placements, and marks
    // the original for exclusion so no unredacted copy survives in the output.
    private static void RewriteCollectedForms(
        RedactRun run, List<PdfIndirectObject> allObjects, HashSet<int> excluded)
    {
        foreach (KeyValuePair<int, List<RedactContext>> kvp in run.FormInvocations)
        {
            int objNum = kvp.Key;
            if (run.Store.ResolveById(new PdfObjectId(objNum, 0)) is not PdfStream form)
            {
                continue;
            }

            byte[] formBytes = DecodeStream(form, run.Pipeline);
            PdfDictionary? formResources = ResolveFormResources(form, null, run.Store);
            byte[] redacted = RewriteContent(formBytes, kvp.Value, run, formResources);

            PdfDictionary newDict = CopyDictionary(form.Dictionary);
            newDict.Remove(PdfName.Intern("Filter"));
            newDict.Remove(PdfName.Intern("DecodeParms"));
            newDict.Set(PdfName.Length, redacted.Length);

            allObjects.Add(new PdfIndirectObject(new PdfObjectId(objNum, 0), new PdfStream(newDict, redacted)));
            excluded.Add(objNum);
        }
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

/// <summary>
/// A redaction context: text or images are removed when, after mapping by
/// <see cref="BaseCtm"/> into page device space, they intersect any of
/// <see cref="Rects"/>. Page content uses the identity transform; a form's
/// content uses the form's absolute placement.
/// </summary>
internal readonly struct RedactContext
{
    /// <summary>Initialises a redaction context.</summary>
    /// <param name="baseCtm">Maps local content space into page device space.</param>
    /// <param name="rects">Redaction rectangles in page device space.</param>
    public RedactContext(Transform baseCtm, List<RectangleF> rects)
    {
        BaseCtm = baseCtm;
        Rects = rects;
    }

    /// <summary>Transform from local content space into page device space.</summary>
    public Transform BaseCtm { get; }

    /// <summary>Redaction rectangles in page device space.</summary>
    public List<RectangleF> Rects { get; }
}

/// <summary>
/// Shared state for one redaction pass: the object store and filter pipeline,
/// plus the registry mapping each form XObject's object number to the placements
/// at which it is invoked (collected across all pages and nested forms).
/// </summary>
internal sealed class RedactRun
{
    /// <summary>Initialises a redaction run.</summary>
    /// <param name="store">The source object store.</param>
    /// <param name="pipeline">The filter pipeline used to decode streams.</param>
    public RedactRun(PdfObjectStore store, FilterPipeline pipeline)
    {
        Store = store;
        Pipeline = pipeline;
    }

    /// <summary>The source object store.</summary>
    public PdfObjectStore Store { get; }

    /// <summary>The filter pipeline used to decode form content streams.</summary>
    public FilterPipeline Pipeline { get; }

    /// <summary>Form object number to the placements at which it is invoked.</summary>
    public Dictionary<int, List<RedactContext>> FormInvocations { get; } = new();

    /// <summary>Guards <see cref="FormInvocations"/> during parallel collection.</summary>
    public object Sync { get; } = new();
}
