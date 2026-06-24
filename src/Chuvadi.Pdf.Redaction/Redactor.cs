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
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Filters;
using Chuvadi.Pdf.Fonts;
using Chuvadi.Pdf.Fonts.Rendering;
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
        // Rectangle-based redaction targets. Pattern (text) redaction is applied
        // separately, by content, inside the rewrite — it is not resolved to
        // rectangles here (that required a second, approximate layout pass that
        // could drift from the rewrite's own glyph positions).
        List<RedactionRect> allRects = new List<RedactionRect>(options.Rectangles);

        // Group rectangles by page (with a parallel list of optional
        // in-place replacement strings, same index order as the rectangles).
        Dictionary<int, List<RectangleF>> byPage = new Dictionary<int, List<RectangleF>>();
        Dictionary<int, List<string?>> byPageReplacements =
            new Dictionary<int, List<string?>>();

        foreach (RedactionRect rect in allRects)
        {
            if (!byPage.TryGetValue(rect.PageIndex, out List<RectangleF>? list))
            {
                list = new List<RectangleF>();
                byPage[rect.PageIndex] = list;
                byPageReplacements[rect.PageIndex] = new List<string?>();
            }

            list.Add(rect.Bounds);
            byPageReplacements[rect.PageIndex].Add(rect.ReplacementText);
        }

        FilterPipeline pipeline = FilterRegistry.CreateDefaultPipeline();

        // Find each page object in the source store
        Dictionary<int, PdfObjectId> pageIds = BuildPageIdMap(document);

        List<PdfIndirectObject> allObjects = new List<PdfIndirectObject>();
        HashSet<int> rewrittenPageNums = new HashSet<int>();
        HashSet<int> removedContentStreamNums = new HashSet<int>();
        HashSet<int> redactedAnnotRefs = new HashSet<int>();
        HashSet<int> annotRemovalCandidates = new HashSet<int>();
        Dictionary<int, PdfDictionary> rewrittenFieldObjects =
            new Dictionary<int, PdfDictionary>();

        int nextObjectNum = FindNextObjectNumber(document);

        List<RedactionWork> work = new List<RedactionWork>();

        // Pages to rewrite = those with rectangles plus those with applicable
        // text patterns (a page may need rewriting for patterns alone, with no
        // rectangles). Process in stable page order.
        SortedSet<int> pagesToProcess = new SortedSet<int>(byPage.Keys);
        if (options.Patterns.Count > 0)
        {
            for (int p = 0; p < document.PageCount; p++)
            {
                if (PageApplicablePatterns(options.Patterns, p).Count > 0)
                {
                    pagesToProcess.Add(p);
                }
            }
        }

        foreach (int pageIndex in pagesToProcess)
        {
            if (pageIndex >= document.PageCount)
            {
                continue;
            }

            if (!pageIds.TryGetValue(pageIndex, out PdfObjectId pageId))
            {
                continue;
            }

            List<RectangleF> rects = byPage.TryGetValue(pageIndex, out List<RectangleF>? rectList)
                ? rectList
                : new List<RectangleF>();
            List<string?> replacements =
                byPageReplacements.TryGetValue(pageIndex, out List<string?>? replList)
                    ? replList
                    : new List<string?>();
            List<PatternRule> pagePatterns = PageApplicablePatterns(options.Patterns, pageIndex);

            PdfPage page = document.Pages[pageIndex];
            work.Add(new RedactionWork(pageId, page, rects, replacements, pagePatterns));
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
                originals[i], PageContexts(work[i].Rects, work[i].Replacements), run,
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
                (byte[] Bytes, List<RectangleF> OverlayBoxes) rewritten = RewriteContent(
                    originals[i], PageContexts(work[i].Rects, work[i].Replacements), run,
                    work[i].Page.Resources, work[i].Patterns);
                redactedBytes[i] = rewritten.Bytes;
                overlayBytes[i] = BuildOverlayWithPatterns(
                    work[i], rewritten.OverlayBoxes, options.OverlayColor, options.DrawOverlay);
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
                (byte[] Bytes, List<RectangleF> OverlayBoxes) rewritten = RewriteContent(
                    originals[i], PageContexts(work[i].Rects, work[i].Replacements), run,
                    work[i].Page.Resources, work[i].Patterns);
                redactedBytes[i] = rewritten.Bytes;
                overlayBytes[i] = BuildOverlayWithPatterns(
                    work[i], rewritten.OverlayBoxes, options.OverlayColor, options.DrawOverlay);
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
            RedactAnnotations(
                modifiedPage, work[i].Page, work[i].Rects,
                document.Objects, redactedAnnotRefs, annotRemovalCandidates,
                rewrittenFieldObjects);
            PdfArray contents = new PdfArray([
                new PdfReference(redactedId),
                new PdfReference(overlayId),
            ]);
            modifiedPage.Set(PdfName.Contents, contents);
            allObjects.Add(new PdfIndirectObject(work[i].PageId, modifiedPage));
            rewrittenPageNums.Add(work[i].PageId.ObjectNumber);
        }

        RewriteCollectedForms(run, allObjects, removedContentStreamNums);

        // Physically remove redacted annotations and any sub-objects they solely
        // own (action dictionaries holding /URI, appearance streams), so a
        // redacted link's target or a note's text cannot be recovered. An object
        // still reachable from the catalog without passing through a redacted
        // annotation is shared and is kept.
        HashSet<int> removedAnnotationNums = new HashSet<int>();
        if (redactedAnnotRefs.Count > 0)
        {
            HashSet<int> survivors = new HashSet<int>(redactedAnnotRefs);
            PdfDictionary? catalog = FindCatalog(document);
            if (catalog is not null)
            {
                Visit(document.Objects, catalog, survivors);
            }

            survivors.ExceptWith(redactedAnnotRefs);
            foreach (int candidate in annotRemovalCandidates)
            {
                if (!survivors.Contains(candidate))
                {
                    removedAnnotationNums.Add(candidate);
                }
            }
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

            if (removedAnnotationNums.Contains(obj.Id.ObjectNumber))
            {
                continue;
            }

            if (rewrittenFieldObjects.TryGetValue(obj.Id.ObjectNumber, out PdfDictionary? rewrittenField))
            {
                allObjects.Add(new PdfIndirectObject(obj.Id, rewrittenField));
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
        public RedactionWork(
            PdfObjectId pageId, PdfPage page, List<RectangleF> rects, List<string?> replacements,
            IReadOnlyList<PatternRule> patterns)
        {
            PageId = pageId;
            Page = page;
            Rects = rects;
            Replacements = replacements;
            Patterns = patterns;
        }

        public PdfObjectId PageId { get; }

        public PdfPage Page { get; }

        public List<RectangleF> Rects { get; }

        public List<string?> Replacements { get; }

        public IReadOnlyList<PatternRule> Patterns { get; }
    }

    // Returns the patterns from the set that apply to the given page (honouring
    // each rule's optional page filter).
    private static List<PatternRule> PageApplicablePatterns(
        IList<PatternRule> patterns, int pageIndex)
    {
        List<PatternRule> result = new List<PatternRule>();
        for (int i = 0; i < patterns.Count; i++)
        {
            if (patterns[i].AppliesToPage(pageIndex))
            {
                result.Add(patterns[i]);
            }
        }

        return result;
    }

    // ── Content stream rewriter ───────────────────────────────────────────

    private static (byte[] Bytes, List<RectangleF> OverlayBoxes) RewriteContent(
        byte[] content, IReadOnlyList<RedactContext> contexts, RedactRun run, PdfDictionary? resources,
        IReadOnlyList<PatternRule> patterns)
    {
        using (MemoryStream input = new MemoryStream(content))
        using (MemoryStream output = new MemoryStream())
        using (PdfTokenizer tok = new PdfTokenizer(input))
        {
            RedactState state = new RedactState();
            state.Patterns = patterns;
            List<PdfToken> pendingOperands = new List<PdfToken>();
            PathAccumulator path = new PathAccumulator();

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

                // Path construction: buffer the operator instead of emitting it,
                // accumulating the path's bounding box, so the whole path can be
                // dropped at its paint operator if it lies in a redaction region.
                if (IsPathConstructionOp(op))
                {
                    AccumulatePathBox(op, pendingOperands, state, path);
                    path.Buffer.Add((new List<PdfToken>(pendingOperands), op));
                    pendingOperands.Clear();
                    continue;
                }

                if (op == "W" || op == "W*")
                {
                    // A clipping path draws nothing and must be preserved
                    // (dropping a clip could expose content), so flag and keep it.
                    path.HasClip = true;
                    path.Buffer.Add((new List<PdfToken>(pendingOperands), op));
                    pendingOperands.Clear();
                    continue;
                }

                if (IsPathPaintOp(op))
                {
                    // "n" paints nothing, so it never leaks; a clipped path is
                    // preserved. Otherwise drop the path when it hits a region.
                    bool dropPath = path.Active
                        && !path.HasClip
                        && op != "n"
                        && PathBoxIntersects(path, contexts);

                    if (!dropPath)
                    {
                        FlushPath(output, path);
                        WriteTokens(output, pendingOperands);
                        byte[] paintBytes = Encoding.Latin1.GetBytes(op + "\n");
                        output.Write(paintBytes, 0, paintBytes.Length);
                    }

                    path.Reset();
                    pendingOperands.Clear();
                    continue;
                }

                // A non-path operator while a path is buffered (unusual in valid
                // content): emit the buffered path verbatim before handling it.
                if (path.Buffer.Count > 0)
                {
                    FlushPath(output, path);
                    path.Reset();
                }

                // Glyph-level text redaction: rebuild Tj and TJ so only the
                // in-region glyphs are removed, keeping neighbours in place.
                // ' and " are decomposed below so their text is redacted the same way.
                if (op == "Tj")
                {
                    EmitRedactedText(output, pendingOperands, state, contexts);
                    pendingOperands.Clear();
                    continue;
                }

                if (op == "TJ")
                {
                    EmitRedactedTJ(output, pendingOperands, state, contexts);
                    pendingOperands.Clear();
                    continue;
                }

                // ' (move to next line and show) and " (set word/char spacing,
                // move to next line, and show) are decomposed into their explicit
                // equivalents so the shown string runs through the same glyph-level
                // pattern redaction as Tj/TJ, instead of being dropped whole.
                if (op == "'" && pendingOperands.Count >= 1)
                {
                    WriteRaw(output, "T*\n");
                    state.NextLine();
                    List<PdfToken> stringOnly = new List<PdfToken> { pendingOperands[pendingOperands.Count - 1] };
                    EmitRedactedText(output, stringOnly, state, contexts);
                    pendingOperands.Clear();
                    continue;
                }

                if (op == "\"" && pendingOperands.Count >= 3)
                {
                    state.WordSpacing = ParseDouble(pendingOperands[0]);
                    state.CharSpacing = ParseDouble(pendingOperands[1]);
                    WriteRaw(
                        output,
                        pendingOperands[0].RawText + " Tw\n"
                            + pendingOperands[1].RawText + " Tc\nT*\n");
                    state.NextLine();
                    List<PdfToken> stringOnly = new List<PdfToken> { pendingOperands[2] };
                    EmitRedactedText(output, stringOnly, state, contexts);
                    pendingOperands.Clear();
                    continue;
                }

                bool drop = ProcessOperator(op, pendingOperands, state, contexts, run, resources);

                if (!drop)
                {
                    WriteTokens(output, pendingOperands);
                    output.Write(Encoding.Latin1.GetBytes(op + "\n"), 0,
                        Encoding.Latin1.GetByteCount(op + "\n"));
                }

                pendingOperands.Clear();
            }

            // Flush any path left unterminated at end of stream.
            if (path.Buffer.Count > 0)
            {
                FlushPath(output, path);
            }

            return (output.ToArray(), state.PatternOverlayBoxes);
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
            case "Tf": ApplyTf(operands, state, resources, run.Store); return false;
            case "Td": ApplyTd(operands, state); return false;
            case "TD": ApplyTD(operands, state); return false;
            case "Tm": ApplyTm(operands, state); return false;
            case "T*": state.NextLine(); return false;
            case "Tc":
                if (operands.Count >= 1 && operands[0].IsNumeric)
                {
                    state.CharSpacing = ParseDouble(operands[0]);
                }

                return false;
            case "Tw":
                if (operands.Count >= 1 && operands[0].IsNumeric)
                {
                    state.WordSpacing = ParseDouble(operands[0]);
                }

                return false;

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

    private static bool IsPathConstructionOp(string op)
    {
        return op == "m" || op == "l" || op == "c" || op == "v"
            || op == "y" || op == "re" || op == "h";
    }

    private static bool IsPathPaintOp(string op)
    {
        return op == "f" || op == "F" || op == "f*" || op == "S" || op == "s"
            || op == "B" || op == "B*" || op == "b" || op == "b*" || op == "n";
    }

    // Expands the buffered path's bounding box (in path/user space, via the
    // current CTM) with the points contributed by a construction operator.
    private static void AccumulatePathBox(
        string op, List<PdfToken> operands, RedactState state, PathAccumulator path)
    {
        switch (op)
        {
            case "m":
            case "l":
                if (operands.Count >= 2)
                {
                    path.AddPoint(ParseDouble(operands[0]), ParseDouble(operands[1]), state);
                }
                break;

            case "c":
                if (operands.Count >= 6)
                {
                    path.AddPoint(ParseDouble(operands[0]), ParseDouble(operands[1]), state);
                    path.AddPoint(ParseDouble(operands[2]), ParseDouble(operands[3]), state);
                    path.AddPoint(ParseDouble(operands[4]), ParseDouble(operands[5]), state);
                }
                break;

            case "v":
            case "y":
                if (operands.Count >= 4)
                {
                    path.AddPoint(ParseDouble(operands[0]), ParseDouble(operands[1]), state);
                    path.AddPoint(ParseDouble(operands[2]), ParseDouble(operands[3]), state);
                }
                break;

            case "re":
                if (operands.Count >= 4)
                {
                    double x = ParseDouble(operands[0]);
                    double y = ParseDouble(operands[1]);
                    double w = ParseDouble(operands[2]);
                    double h = ParseDouble(operands[3]);
                    path.AddPoint(x, y, state);
                    path.AddPoint(x + w, y + h, state);
                }
                break;

            case "h":
                // "h" closes the subpath and adds no new points.
                break;
        }
    }

    // True when the buffered path's bounding box, mapped into a context's page
    // device space, overlaps a redaction rectangle. Mirrors the image test.
    private static bool PathBoxIntersects(PathAccumulator path, IReadOnlyList<RedactContext> contexts)
    {
        if (!path.Active)
        {
            return false;
        }

        PointF c00 = new PointF(path.MinX, path.MinY);
        PointF c10 = new PointF(path.MaxX, path.MinY);
        PointF c01 = new PointF(path.MinX, path.MaxY);
        PointF c11 = new PointF(path.MaxX, path.MaxY);

        for (int c = 0; c < contexts.Count; c++)
        {
            RedactContext context = contexts[c];
            if (context.Rects.Count == 0)
            {
                continue;
            }

            PointF d00 = context.BaseCtm.TransformPoint(c00);
            PointF d10 = context.BaseCtm.TransformPoint(c10);
            PointF d01 = context.BaseCtm.TransformPoint(c01);
            PointF d11 = context.BaseCtm.TransformPoint(c11);

            double minX = Math.Min(Math.Min(d00.X, d10.X), Math.Min(d01.X, d11.X));
            double maxX = Math.Max(Math.Max(d00.X, d10.X), Math.Max(d01.X, d11.X));
            double minY = Math.Min(Math.Min(d00.Y, d10.Y), Math.Min(d01.Y, d11.Y));
            double maxY = Math.Max(Math.Max(d00.Y, d10.Y), Math.Max(d01.Y, d11.Y));

            foreach (RectangleF r in context.Rects)
            {
                if (minX < r.X + r.Width && maxX > r.X
                    && minY < r.Y + r.Height && maxY > r.Y)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void FlushPath(MemoryStream output, PathAccumulator path)
    {
        foreach ((List<PdfToken> operands, string op) in path.Buffer)
        {
            WriteTokens(output, operands);
            byte[] bytes = Encoding.Latin1.GetBytes(op + "\n");
            output.Write(bytes, 0, bytes.Length);
        }
    }

    // Buffers a path's construction operators and tracks its user-space bounding
    // box so a painted path inside a redaction region can be dropped wholesale.
    private sealed class PathAccumulator
    {
        public bool Active { get; private set; }

        public bool HasClip { get; set; }

        public double MinX { get; private set; }

        public double MinY { get; private set; }

        public double MaxX { get; private set; }

        public double MaxY { get; private set; }

        public List<(List<PdfToken> Operands, string Op)> Buffer { get; } =
            new List<(List<PdfToken> Operands, string Op)>();

        public void AddPoint(double x, double y, RedactState state)
        {
            PointF p = state.Ctm.TransformPoint(new PointF(x, y));
            if (!Active)
            {
                MinX = MaxX = p.X;
                MinY = MaxY = p.Y;
                Active = true;
                return;
            }

            if (p.X < MinX)
            {
                MinX = p.X;
            }

            if (p.X > MaxX)
            {
                MaxX = p.X;
            }

            if (p.Y < MinY)
            {
                MinY = p.Y;
            }

            if (p.Y > MaxY)
            {
                MaxY = p.Y;
            }
        }

        public void Reset()
        {
            Active = false;
            HasClip = false;
            MinX = MinY = MaxX = MaxY = 0;
            Buffer.Clear();
        }
    }

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

    private static void ApplyTf(
        List<PdfToken> operands, RedactState state, PdfDictionary? resources, PdfObjectStore store)
    {
        if (operands.Count >= 2)
        {
            state.FontSize = ParseDouble(operands[1]);
            state.GlyphWidths = BuildGlyphWidths(operands[0], resources, store);
            state.Font = BuildFont(operands[0], resources, store, state.FontCache);
        }
    }

    // Resolves the font named by the Tf operand into a PdfFont (for decoding
    // glyph codes to Unicode), caching the result per name so each font's
    // ToUnicode CMap is parsed at most once. Returns null when the font cannot
    // be resolved or decoded, in which case callers fall back to treating codes
    // as Latin-1.
    private static PdfFont? BuildFont(
        PdfToken nameToken,
        PdfDictionary? resources,
        PdfObjectStore store,
        Dictionary<string, PdfFont?> cache)
    {
        if (resources is null || nameToken.Type != PdfTokenType.Name)
        {
            return null;
        }

        string fontName = nameToken.RawText.TrimStart('/');
        if (cache.TryGetValue(fontName, out PdfFont? cached))
        {
            return cached;
        }

        PdfFont? result = null;
        if (resources.TryGetValue(PdfName.Intern("Font"), out PdfPrimitive? fontDictValue)
            && store.Resolve(fontDictValue) is PdfDictionary fonts
            && fonts.TryGetValue(PdfName.Intern(fontName), out PdfPrimitive? fontEntry)
            && store.Resolve(fontEntry) is PdfDictionary font)
        {
            try
            {
                result = PdfFont.FromDictionary(font, store);
            }
            catch (Exception)
            {
                result = null;
            }
        }

        cache[fontName] = result;
        return result;
    }

    // Builds a per-byte-code width table (1/1000 em) for the font named by the
    // Tf operand: from the font's /Widths array when present, otherwise from the
    // Standard-14 metrics for a base-14 font. Returns null when widths are
    // unknown so the caller falls back to an estimate.
    private static int[]? BuildGlyphWidths(
        PdfToken nameToken, PdfDictionary? resources, PdfObjectStore store)
    {
        if (resources is null || nameToken.Type != PdfTokenType.Name)
        {
            return null;
        }

        string fontName = nameToken.RawText.TrimStart('/');

        if (!resources.TryGetValue(PdfName.Intern("Font"), out PdfPrimitive? fontDictValue)
            || store.Resolve(fontDictValue) is not PdfDictionary fonts
            || !fonts.TryGetValue(PdfName.Intern(fontName), out PdfPrimitive? fontEntry)
            || store.Resolve(fontEntry) is not PdfDictionary font)
        {
            return null;
        }

        int[] widths = new int[256];

        if (font.TryGetValue(PdfName.Intern("Widths"), out PdfPrimitive? widthsValue)
            && store.Resolve(widthsValue) is PdfArray widthsArray
            && font.TryGetValue(PdfName.Intern("FirstChar"), out PdfPrimitive? firstCharValue))
        {
            int firstChar = (int)PdfReal.ToDouble(store.Resolve(firstCharValue));
            for (int i = 0; i < widthsArray.Count; i++)
            {
                int code = firstChar + i;
                if (code >= 0 && code < 256)
                {
                    widths[code] = (int)PdfReal.ToDouble(store.Resolve(widthsArray[i]));
                }
            }

            return widths;
        }

        if (font.TryGetValue(PdfName.Intern("BaseFont"), out PdfPrimitive? baseFontValue)
            && store.Resolve(baseFontValue) is PdfName baseFontName
            && Standard14GlyphWidths.IsStandard14(baseFontName.Value))
        {
            for (int code = 0; code < 256; code++)
            {
                widths[code] = Standard14GlyphWidths.Width(baseFontName.Value, (char)code);
            }

            return widths;
        }

        return null;
    }

    private static void ApplyTd(List<PdfToken> operands, RedactState state)
    {
        if (operands.Count >= 2)
        {
            double tx = ParseDouble(operands[0]);
            double ty = ParseDouble(operands[1]);

            // PDF text model: Td moves the text line matrix by (tx, ty) in text
            // space (Tlm' = translate(tx, ty) x Tlm) and starts a fresh in-line
            // advance. The position lives entirely in the text matrix; TextX is
            // the running advance from the line origin (0 at the new origin).
            state.TextMatrix = state.TextMatrix.Translate(tx, ty);
            state.TextX = 0;
            state.TextY = 0;
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

        // Tm sets the text line matrix absolutely. The translation lives in the
        // matrix itself; the in-line advance (TextX) starts at the line origin.
        // Earlier code also copied the translation into TextX/TextY and then
        // transformed that point by the same matrix, double-applying the
        // translation and missing every glyph under a non-identity Tm.
        state.TextMatrix = new Transform(
            ParseDouble(operands[0]), ParseDouble(operands[1]),
            ParseDouble(operands[2]), ParseDouble(operands[3]),
            ParseDouble(operands[4]), ParseDouble(operands[5]));
        state.TextX = 0;
        state.TextY = 0;
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

    // Width of a decoded string in user space (before font size scaling is
    // applied by the caller's transform): sums per-byte glyph advances when the
    // active font's widths are known, otherwise a Helvetica-baseline estimate.
    private static double MeasureTextWidth(string text, RedactState state)
    {
        if (state.GlyphWidths is int[] glyphWidths)
        {
            double total = 0;
            foreach (char ch in text)
            {
                int code = ch;
                int w = (code >= 0 && code < 256) ? glyphWidths[code] : 0;
                total += w > 0 ? w : 500;
            }

            return total / 1000.0 * state.FontSize;
        }

        return text.Length * state.FontSize * 0.6;
    }

    // Emits a Tj operator with only the out-of-region glyphs retained. When no
    // glyph is in a region the original Tj is re-emitted; when all are, nothing
    // is emitted; otherwise a TJ array is written whose dropped runs become
    // negative numeric gaps equal to the removed glyphs' advance, so survivors
    // keep their original positions.
    private static void EmitRedactedText(
        MemoryStream output, List<PdfToken> operands, RedactState state,
        IReadOnlyList<RedactContext> contexts)
    {
        if (operands.Count == 0)
        {
            WriteOriginalTj(output, operands);
            return;
        }

        PdfToken stringToken = operands[operands.Count - 1];
        if (stringToken.Type != PdfTokenType.LiteralString
            && stringToken.Type != PdfTokenType.HexString)
        {
            WriteOriginalTj(output, operands);
            return;
        }

        string text = ExtractString(stringToken);
        if (text.Length == 0 || state.FontSize <= 0)
        {
            WriteOriginalTj(output, operands);
            return;
        }

        bool[] drop = new bool[text.Length];
        string?[] glyphReplacement = new string?[text.Length];
        double[] advance = new double[text.Length];
        double[] glyphX0 = new double[text.Length];
        double[] glyphX1 = new double[text.Length];
        bool anyDropped = false;
        bool allDropped = true;
        bool anyReplacement = false;
        double cursor = state.TextX;
        // Match the fragment frame used to build the rectangles: the content parser
        // records each fragment at its text-matrix translation and never applies the
        // CTM, so the strip uses the text matrix alone (page cm is out of frame for
        // both). Forms still map through BaseCtm in the combined transform below.
        Transform local = state.TextMatrix;

        for (int i = 0; i < text.Length; i++)
        {
            int code = text[i];
            int width1000 =
                (state.GlyphWidths is int[] gw && code >= 0 && code < 256 && gw[code] > 0)
                    ? gw[code]
                    : 500;
            advance[i] = width1000;
            double advUser = width1000 / 1000.0 * state.FontSize
                + state.CharSpacing
                + (code == 32 ? state.WordSpacing : 0.0);

            glyphX0[i] = cursor;
            glyphX1[i] = cursor + advUser;
            bool inRegion = MatchSpan(
                cursor, cursor + advUser, state, local, contexts, out string? replacement);
            drop[i] = inRegion;
            glyphReplacement[i] = replacement;

            cursor += advUser;
        }

        // Advance the in-line text cursor so a subsequent show operator on the
        // same line is measured from the correct position (PDF text positions
        // accumulate across Tj/TJ until the next line move resets TextX).
        state.TextX = cursor;

        // Pattern (content) redaction: flag glyphs that fall within a text-pattern
        // match, independent of layout geometry. Then derive the drop summary.
        bool[] patternDrop = new bool[text.Length];
        MarkPatternDrops(text, state, patternDrop);
        for (int i = 0; i < text.Length; i++)
        {
            if (patternDrop[i])
            {
                drop[i] = true;
            }

            if (drop[i])
            {
                anyDropped = true;
                if (glyphReplacement[i] is not null)
                {
                    anyReplacement = true;
                }
            }
            else
            {
                allDropped = false;
            }
        }

        Transform overlayBase = contexts.Count > 0 ? contexts[0].BaseCtm : Transform.Identity;
        AddPatternOverlayBoxes(patternDrop, glyphX0, glyphX1, state, overlayBase);

        if (!anyDropped)
        {
            WriteOriginalTj(output, operands);
            return;
        }

        if (allDropped && !anyReplacement)
        {
            return;
        }

        StringBuilder tj = new StringBuilder();
        tj.Append('[');
        int idx = 0;
        while (idx < text.Length)
        {
            if (drop[idx])
            {
                double gap = 0;
                string? replacement = null;
                while (idx < text.Length && drop[idx])
                {
                    gap += advance[idx];
                    if (replacement is null)
                    {
                        replacement = glyphReplacement[idx];
                    }

                    idx++;
                }

                if (replacement is null)
                {
                    tj.Append(' ')
                        .Append((-gap).ToString("0.###", CultureInfo.InvariantCulture))
                        .Append(' ');
                }
                else
                {
                    double replacementWidth = MeasureString1000(replacement, state);
                    if (replacementWidth > gap)
                    {
                        throw new RedactionException(
                            $"Replacement text \"{replacement}\" is wider than the redacted "
                            + "span and cannot be drawn in place; use a shorter replacement.");
                    }

                    tj.Append(EncodeLiteralString(replacement));
                    double remaining = gap - replacementWidth;
                    if (remaining > 0)
                    {
                        tj.Append(' ')
                            .Append((-remaining).ToString("0.###", CultureInfo.InvariantCulture))
                            .Append(' ');
                    }
                }
            }
            else
            {
                StringBuilder run = new StringBuilder();
                while (idx < text.Length && !drop[idx])
                {
                    run.Append(text[idx]);
                    idx++;
                }

                tj.Append(EncodeLiteralString(run.ToString()));
            }
        }

        tj.Append("] TJ\n");
        byte[] bytes = Encoding.Latin1.GetBytes(tj.ToString());
        output.Write(bytes, 0, bytes.Length);
    }

    // Glyph-level redaction for a TJ array. Walks the array's strings and
    // kerning numbers, dropping only the glyphs whose span falls in a redaction
    // rectangle and preserving the original inter-glyph kerns so survivors stay
    // positioned. This is the array analogue of EmitRedactedText (single-string
    // Tj): without it, a whole TJ — typically one line of a Word/Office PDF —
    // was dropped if any glyph in it was redacted, and mid-line matches were
    // missed because the cursor was not advanced across array elements.
    private static void EmitRedactedTJ(
        MemoryStream output, List<PdfToken> operands, RedactState state,
        IReadOnlyList<RedactContext> contexts)
    {
        // Flatten the array into ordered glyphs, recording the kern number that
        // precedes each (a TJ number k shifts the next glyph left by k/1000 of
        // the text space).
        List<char> chars = new List<char>();
        List<double> widths = new List<double>();
        List<double> leadKern = new List<double>();
        double pendingKern = 0.0;
        bool sawArray = false;

        for (int t = 0; t < operands.Count; t++)
        {
            PdfToken tok = operands[t];
            if (tok.Type == PdfTokenType.ArrayStart)
            {
                sawArray = true;
                continue;
            }
            if (tok.Type == PdfTokenType.ArrayEnd)
            {
                break;
            }
            if (!sawArray)
            {
                continue;
            }

            if (tok.IsNumeric)
            {
                pendingKern += ParseDouble(tok);
                continue;
            }

            if (tok.Type == PdfTokenType.LiteralString || tok.Type == PdfTokenType.HexString)
            {
                string s = ExtractString(tok);
                for (int j = 0; j < s.Length; j++)
                {
                    int code = s[j];
                    int width1000 =
                        (state.GlyphWidths is int[] gw && code >= 0 && code < 256 && gw[code] > 0)
                            ? gw[code]
                            : 500;
                    chars.Add(s[j]);
                    widths.Add(width1000);
                    leadKern.Add(j == 0 ? pendingKern : 0.0);
                    pendingKern = 0.0;
                }
            }
        }

        int n = chars.Count;
        if (n == 0 || state.FontSize <= 0)
        {
            WriteOriginalTJ(output, operands);
            return;
        }

        bool[] drop = new bool[n];
        string?[] glyphReplacement = new string?[n];
        double[] glyphX0 = new double[n];
        double[] glyphX1 = new double[n];
        bool anyDropped = false;
        bool allDropped = true;
        bool anyReplacement = false;
        double cursor = state.TextX;
        Transform local = state.TextMatrix;

        for (int i = 0; i < n; i++)
        {
            // Apply the kern preceding this glyph (TJ number k → shift left by
            // k/1000 × font size) before measuring its span.
            cursor -= leadKern[i] / 1000.0 * state.FontSize;

            double advUser = widths[i] / 1000.0 * state.FontSize
                + state.CharSpacing
                + (chars[i] == 32 ? state.WordSpacing : 0.0);
            glyphX0[i] = cursor;
            glyphX1[i] = cursor + advUser;
            bool inRegion = MatchSpan(
                cursor, cursor + advUser, state, local, contexts, out string? replacement);
            drop[i] = inRegion;
            glyphReplacement[i] = replacement;

            cursor += advUser;
        }

        // Advance the in-line text cursor (see EmitRedactedText) so a following
        // show operator on the same line measures from the correct position.
        state.TextX = cursor;

        // Pattern (content) redaction: flag glyphs within a text-pattern match,
        // independent of layout geometry. Then derive the drop summary.
        bool[] patternDrop = new bool[n];
        MarkPatternDrops(new string(chars.ToArray()), state, patternDrop);
        for (int i = 0; i < n; i++)
        {
            if (patternDrop[i])
            {
                drop[i] = true;
            }

            if (drop[i])
            {
                anyDropped = true;
                if (glyphReplacement[i] is not null)
                {
                    anyReplacement = true;
                }
            }
            else
            {
                allDropped = false;
            }
        }

        Transform overlayBase = contexts.Count > 0 ? contexts[0].BaseCtm : Transform.Identity;
        AddPatternOverlayBoxes(patternDrop, glyphX0, glyphX1, state, overlayBase);

        if (!anyDropped)
        {
            WriteOriginalTJ(output, operands);
            return;
        }

        if (allDropped && !anyReplacement)
        {
            return;
        }

        // Rebuild. Displacement is tracked in 1/1000 em, rightward-positive: a
        // glyph width w contributes +w, a TJ kern number k contributes −k. The
        // number that reproduces a displacement d is −d.
        StringBuilder tj = new StringBuilder();
        tj.Append('[');
        int idx = 0;
        while (idx < n)
        {
            if (drop[idx])
            {
                double disp = 0;
                string? replacement = null;
                while (idx < n && drop[idx])
                {
                    disp += -leadKern[idx];
                    disp += widths[idx];
                    if (replacement is null)
                    {
                        replacement = glyphReplacement[idx];
                    }

                    idx++;
                }

                if (replacement is null)
                {
                    if (disp != 0)
                    {
                        tj.Append(' ')
                            .Append((-disp).ToString("0.###", CultureInfo.InvariantCulture))
                            .Append(' ');
                    }
                }
                else
                {
                    double replacementWidth = MeasureString1000(replacement, state);
                    if (replacementWidth > disp)
                    {
                        throw new RedactionException(
                            $"Replacement text \"{replacement}\" is wider than the redacted "
                            + "span and cannot be drawn in place; use a shorter replacement.");
                    }

                    tj.Append(EncodeLiteralString(replacement));
                    double remaining = disp - replacementWidth;
                    if (remaining > 0)
                    {
                        tj.Append(' ')
                            .Append((-remaining).ToString("0.###", CultureInfo.InvariantCulture))
                            .Append(' ');
                    }
                }
            }
            else
            {
                // Emit the kern preceding this survivor (preserving original
                // inter-glyph spacing), then accumulate survivors until the next
                // kern or dropped glyph.
                if (leadKern[idx] != 0)
                {
                    tj.Append(' ')
                        .Append(leadKern[idx].ToString("0.###", CultureInfo.InvariantCulture))
                        .Append(' ');
                }

                StringBuilder run = new StringBuilder();
                run.Append(chars[idx]);
                idx++;
                while (idx < n && !drop[idx] && leadKern[idx] == 0)
                {
                    run.Append(chars[idx]);
                    idx++;
                }

                tj.Append(EncodeLiteralString(run.ToString()));
            }
        }

        tj.Append("] TJ\n");
        byte[] bytes = Encoding.Latin1.GetBytes(tj.ToString());
        output.Write(bytes, 0, bytes.Length);
    }

    // Adds page-space overlay rectangles covering maximal runs of glyphs removed
    // by a content pattern. Positions come from the rewrite's own glyph cursor,
    // so the boxes line up exactly with what was removed (no second layout pass).
    private static void AddPatternOverlayBoxes(
        bool[] patternDrop, double[] glyphX0, double[] glyphX1, RedactState state,
        Transform baseCtm)
    {
        Transform combined = state.TextMatrix.Multiply(state.Ctm).Multiply(baseCtm);
        double bottom = GlyphBottom(state);
        double top = GlyphTop(state);
        int i = 0;
        while (i < patternDrop.Length)
        {
            if (!patternDrop[i])
            {
                i++;
                continue;
            }

            int runStart = i;
            while (i < patternDrop.Length && patternDrop[i])
            {
                i++;
            }

            PointF corner0 = combined.TransformPoint(new PointF(glyphX0[runStart], bottom));
            PointF corner1 = combined.TransformPoint(new PointF(glyphX1[i - 1], top));
            state.PatternOverlayBoxes.Add(
                RectangleF.FromCorners(corner0.X, corner0.Y, corner1.X, corner1.Y));
        }
    }

    private static void WriteRaw(MemoryStream output, string text)
    {
        byte[] bytes = Encoding.Latin1.GetBytes(text);
        output.Write(bytes, 0, bytes.Length);
    }

    private static void WriteOriginalTJ(MemoryStream output, List<PdfToken> operands)
    {
        WriteTokens(output, operands);
        byte[] op = Encoding.Latin1.GetBytes("TJ\n");
        output.Write(op, 0, op.Length);
    }

    // Flags glyphs that fall within a text-pattern match. Each glyph's byte code
    // is decoded to Unicode through the active font (its ToUnicode CMap when
    // present), the operator's text is matched against the page's patterns, and
    // every glyph contributing to a match is marked for removal. This is
    // position-free: it removes the matched text wherever it physically sits,
    // independent of layout transforms, kerning, or character spacing — so it
    // does not drift the way geometry-based resolution can. A pattern that spans
    // two separate show operators is not matched here (a documented limitation).
    private static void MarkPatternDrops(string codes, RedactState state, bool[] drop)
    {
        IReadOnlyList<PatternRule>? patterns = state.Patterns;
        if (patterns is null || patterns.Count == 0 || codes.Length == 0)
        {
            return;
        }

        StringBuilder textBuilder = new StringBuilder(codes.Length);
        List<int> glyphOfChar = new List<int>(codes.Length);
        for (int i = 0; i < codes.Length; i++)
        {
            string unicode = state.Font is PdfFont font
                ? font.DecodeCode(codes[i])
                : codes[i].ToString();
            for (int k = 0; k < unicode.Length; k++)
            {
                textBuilder.Append(unicode[k]);
                glyphOfChar.Add(i);
            }
        }

        string text = textBuilder.ToString();
        if (text.Length == 0)
        {
            return;
        }

        for (int p = 0; p < patterns.Count; p++)
        {
            PatternRule rule = patterns[p];
            foreach (Match match in rule.Regex.Matches(text))
            {
                if (rule.Validator is not null && !rule.Validator(match.Value))
                {
                    continue;
                }

                int matchEnd = match.Index + match.Length;
                for (int c = match.Index; c < matchEnd && c < glyphOfChar.Count; c++)
                {
                    drop[glyphOfChar[c]] = true;
                }
            }
        }
    }

    // Total advance of a string in 1/1000 em for the active font (no font-size
    // scaling), used to fit-check replacement text against the removed span.
    private static double MeasureString1000(string text, RedactState state)
    {
        double total = 0;
        foreach (char ch in text)
        {
            int code = ch;
            int w = (state.GlyphWidths is int[] gw && code >= 0 && code < 256 && gw[code] > 0)
                ? gw[code]
                : 500;
            total += w;
        }

        return total;
    }

    private static void WriteOriginalTj(MemoryStream output, List<PdfToken> operands)
    {
        WriteTokens(output, operands);
        byte[] op = Encoding.Latin1.GetBytes("Tj\n");
        output.Write(op, 0, op.Length);
    }

    // Vertical extent of a glyph box in text space, relative to the baseline
    // (text-space y = 0). Generous ascent/descent fractions plus a small margin
    // make the box fully cover ascenders and descender tails (g, y, p, q)
    // without clipping; over-coverage is safe and intended for a redaction box.
    private const double TextAscentEm = 0.80;
    private const double TextDescentEm = 0.25;
    private const double GlyphBoxMarginPoints = 1.5;

    private static double GlyphTop(RedactState state)
    {
        return (TextAscentEm * state.FontSize) + GlyphBoxMarginPoints;
    }

    private static double GlyphBottom(RedactState state)
    {
        return -((TextDescentEm * state.FontSize) + GlyphBoxMarginPoints);
    }

    // True when a glyph occupying [x0, x1] in text space (advance from the line
    // origin), spanning the font's descent-to-ascent vertical extent, maps into
    // any redaction rectangle for some context. Outputs the in-place replacement
    // string of the first matched rectangle, if any.
    private static bool MatchSpan(
        double x0, double x1, RedactState state, Transform local,
        IReadOnlyList<RedactContext> contexts, out string? replacement)
    {
        replacement = null;
        for (int c = 0; c < contexts.Count; c++)
        {
            RedactContext context = contexts[c];
            Transform combined = local.Multiply(context.BaseCtm);
            PointF originDev = combined.TransformPoint(new PointF(x0, GlyphBottom(state)));
            PointF endDev = combined.TransformPoint(new PointF(x1, GlyphTop(state)));
            RectangleF box = RectangleF.FromCorners(originDev.X, originDev.Y, endDev.X, endDev.Y);

            for (int r = 0; r < context.Rects.Count; r++)
            {
                if (!box.Intersect(context.Rects[r]).IsEmpty)
                {
                    replacement = r < context.Replacements.Count ? context.Replacements[r] : null;
                    return true;
                }
            }
        }

        return false;
    }

    private static string EncodeLiteralString(string value)
    {
        StringBuilder sb = new StringBuilder(value.Length + 2);
        sb.Append('(');
        foreach (char ch in value)
        {
            if (ch == '\\' || ch == '(' || ch == ')')
            {
                sb.Append('\\');
            }

            sb.Append(ch);
        }

        sb.Append(')');
        return sb.ToString();
    }

    private static bool IsTextInRedactRect(
        string text, RedactState state, IReadOnlyList<RedactContext> contexts)
    {
        if (string.IsNullOrEmpty(text) || state.FontSize <= 0)
        {
            return false;
        }

        // Text width in user space: sum of per-glyph advances when the font's
        // widths are known, else a Helvetica-baseline estimate.
        double width = MeasureTextWidth(text, state);

        // Match the fragment frame used to build the rectangles: the content parser
        // records each fragment at its text-matrix translation and never applies the
        // CTM, so the strip uses the text matrix alone (page cm is out of frame for
        // both). Forms still map through BaseCtm in the combined transform below.
        Transform local = state.TextMatrix;

        for (int c = 0; c < contexts.Count; c++)
        {
            RedactContext context = contexts[c];
            Transform combined = local.Multiply(context.BaseCtm);
            PointF originDev = combined.TransformPoint(
                new PointF(state.TextX, GlyphBottom(state)));
            PointF endDev = combined.TransformPoint(
                new PointF(state.TextX + width, GlyphTop(state)));

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
    private static List<RedactContext> PageContexts(
        List<RectangleF> rects, List<string?> replacements)
    {
        return new List<RedactContext>
        {
            new RedactContext(Transform.Identity, rects, replacements),
        };
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
            formContexts.Add(new RedactContext(baseCtm, ctx.Rects, ctx.Replacements));
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
            byte[] redacted = RewriteContent(formBytes, kvp.Value, run, formResources, System.Array.Empty<PatternRule>()).Bytes;

            PdfDictionary newDict = CopyDictionary(form.Dictionary);
            newDict.Remove(PdfName.Intern("Filter"));
            newDict.Remove(PdfName.Intern("DecodeParms"));
            newDict.Set(PdfName.Length, redacted.Length);

            allObjects.Add(new PdfIndirectObject(new PdfObjectId(objNum, 0), new PdfStream(newDict, redacted)));
            excluded.Add(objNum);
        }
    }

    // ── Overlay generation ────────────────────────────────────────────────

    // Builds the overlay for a page, combining the explicit redaction rectangles
    // with the boxes covering content-pattern removals (which carry no
    // replacement text). The boxes are drawn in the same overlay colour.
    //
    // Content-pattern box positions are derived from the rewrite's linear text
    // advance. For normally laid-out text this matches the page exactly, but for
    // heavily justified text inside clipped table cells the advance can drift and
    // place the box off the visible page. Such out-of-bounds boxes are suppressed
    // so a drifted box is never painted in the wrong place — the matched text is
    // still physically removed, leaving a clean gap. (A render-faithful box for
    // those cases is tracked as follow-up work.)
    private static byte[] BuildOverlayWithPatterns(
        RedactionWork work, List<RectangleF> patternBoxes, ColorF overlayColor, bool drawOverlay)
    {
        List<RectangleF> rects = new List<RectangleF>(work.Rects);
        List<string?> replacements = new List<string?>(work.Replacements);

        PdfRectangle crop = work.Page.CropBox;
        RectangleF pageRect = RectangleF.FromCorners(crop.X1, crop.Y1, crop.X2, crop.Y2);
        for (int b = 0; b < patternBoxes.Count; b++)
        {
            if (patternBoxes[b].Intersect(pageRect).IsEmpty)
            {
                continue;
            }

            rects.Add(patternBoxes[b]);
            replacements.Add(null);
        }

        return BuildOverlay(rects, replacements, overlayColor, drawOverlay);
    }

    private static byte[] BuildOverlay(
        List<RectangleF> rects, List<string?> replacements, ColorF overlayColor, bool drawOverlay)
    {
        // Boxless redaction: the matched glyphs are physically removed; with the
        // overlay disabled (or a fully transparent colour) no box is painted and
        // the page reads as clean where the text was.
        if (!drawOverlay || overlayColor.Alpha <= 0f)
        {
            return [];
        }

        ColorF rgb = overlayColor.ToRgb();
        string r = Fmt(rgb.R);
        string g = Fmt(rgb.G);
        string b = Fmt(rgb.B);

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("q");
        sb.AppendLine($"{r} {g} {b} rg");

        for (int idx = 0; idx < rects.Count; idx++)
        {
            // A region with in-place replacement text is not boxed over, so the
            // replacement remains visible.
            if (idx < replacements.Count && replacements[idx] is not null)
            {
                continue;
            }

            RectangleF rect = rects[idx];
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

    // ── Annotation redaction (R1) ─────────────────────────────────────────

    /// <summary>
    /// Redacts in-region annotations on a page. Widget (form-field) annotations
    /// have their field value stripped and any indirect value/appearance objects
    /// physically removed, while the field object itself is kept (still
    /// referenced by the AcroForm tree) but emptied. Every other annotation
    /// whose /Rect intersects a region is dropped from /Annots and recorded,
    /// with the sub-objects it owns, for physical removal.
    /// </summary>
    private static void RedactAnnotations(
        PdfDictionary modifiedPage,
        PdfPage page,
        List<RectangleF> rects,
        PdfObjectStore store,
        HashSet<int> redactedAnnotRefs,
        HashSet<int> removalCandidates,
        Dictionary<int, PdfDictionary> rewrittenFieldObjects)
    {
        if (!page.Dictionary.TryGetValue(PdfName.Intern("Annots"), out PdfPrimitive? annotsPrim))
        {
            return;
        }

        if (store.Resolve(annotsPrim) is not PdfArray annots)
        {
            return;
        }

        PdfArray kept = new PdfArray();
        for (int i = 0; i < annots.Count; i++)
        {
            PdfPrimitive entry = annots[i];
            if (store.Resolve(entry) is not PdfDictionary annot)
            {
                kept.Add(entry);
                continue;
            }

            if (!TryGetAnnotationRect(annot, store, out RectangleF annotRect)
                || !IntersectsAnyRect(annotRect, rects))
            {
                kept.Add(entry);
                continue;
            }

            if (IsWidgetAnnotation(annot))
            {
                // Strip the form-field value; drop the widget from /Annots but
                // keep the (now-emptied) field object for AcroForm consistency.
                RedactWidgetValue(
                    annot, entry, store,
                    redactedAnnotRefs, removalCandidates, rewrittenFieldObjects);
            }
            else if (entry is PdfReference reference)
            {
                int num = reference.ObjectId.ObjectNumber;
                redactedAnnotRefs.Add(num);
                removalCandidates.Add(num);
                Visit(store, annot, removalCandidates);
            }
        }

        if (kept.Count > 0)
        {
            modifiedPage.Set(PdfName.Intern("Annots"), kept);
        }
        else
        {
            modifiedPage.Remove(PdfName.Intern("Annots"));
        }
    }

    // Form-field value keys to strip from a redacted widget and its parent
    // field. /V and /DV are the value; /AP, /AS render it; /RV, /I, /TU may
    // echo it. Field structure (/FT, /T, /Kids, /Parent) is kept.
    private static readonly string[] SensitiveFieldKeys =
        new[] { "V", "DV", "AP", "AS", "RV", "I", "TU" };

    /// <summary>
    /// Strips the value of the form field reached through a redacted widget:
    /// the widget itself (merged field/widget) and every value-bearing ancestor
    /// found via /Parent. The objects are rewritten without their value keys and
    /// any indirect value/appearance objects are queued for physical removal.
    /// </summary>
    private static void RedactWidgetValue(
        PdfDictionary widget,
        PdfPrimitive widgetEntry,
        PdfObjectStore store,
        HashSet<int> redactedRefs,
        HashSet<int> removalCandidates,
        Dictionary<int, PdfDictionary> rewrittenFieldObjects)
    {
        if (widgetEntry is PdfReference widgetRef)
        {
            StripFieldValue(
                widgetRef.ObjectId.ObjectNumber, widget, store,
                redactedRefs, removalCandidates, rewrittenFieldObjects);
        }

        // Walk /Parent so a split field (value on the parent, /Rect on the
        // widget) also has its value cleared. Guard against /Parent cycles.
        PdfPrimitive? current = widget.TryGetValue(PdfName.Intern("Parent"), out PdfPrimitive? parent)
            ? parent
            : null;
        HashSet<int> seen = new HashSet<int>();
        while (current is PdfReference parentRef && seen.Add(parentRef.ObjectId.ObjectNumber))
        {
            if (store.Resolve(parentRef) is not PdfDictionary parentField)
            {
                break;
            }

            StripFieldValue(
                parentRef.ObjectId.ObjectNumber, parentField, store,
                redactedRefs, removalCandidates, rewrittenFieldObjects);

            current = parentField.TryGetValue(PdfName.Intern("Parent"), out PdfPrimitive? grandParent)
                ? grandParent
                : null;
        }
    }

    private static void StripFieldValue(
        int objectNumber,
        PdfDictionary source,
        PdfObjectStore store,
        HashSet<int> redactedRefs,
        HashSet<int> removalCandidates,
        Dictionary<int, PdfDictionary> rewrittenFieldObjects)
    {
        PdfDictionary target = rewrittenFieldObjects.TryGetValue(objectNumber, out PdfDictionary? existing)
            ? existing
            : CopyDictionary(source);

        bool changed = false;
        for (int i = 0; i < SensitiveFieldKeys.Length; i++)
        {
            PdfName key = PdfName.Intern(SensitiveFieldKeys[i]);
            if (target.TryGetValue(key, out PdfPrimitive? value))
            {
                // Queue indirect value/appearance objects for physical removal,
                // then drop the key so the inline value is gone from the field.
                CollectIndirectRefs(value, store, redactedRefs, removalCandidates);
                target.Remove(key);
                changed = true;
            }
        }

        if (changed)
        {
            rewrittenFieldObjects[objectNumber] = target;
        }
    }

    // Walks a stripped value, queuing any indirect object it references (and the
    // objects those own) for physical removal. Pre-seeding redactedRefs makes
    // the later survivor walk stop at these nodes so an object reachable only
    // through the stripped value is removed while a shared one is kept.
    private static void CollectIndirectRefs(
        PdfPrimitive value,
        PdfObjectStore store,
        HashSet<int> redactedRefs,
        HashSet<int> removalCandidates)
    {
        if (value is PdfReference reference)
        {
            int num = reference.ObjectId.ObjectNumber;
            redactedRefs.Add(num);
            removalCandidates.Add(num);
            Visit(store, store.Resolve(reference), removalCandidates);
            return;
        }

        if (value is PdfArray array)
        {
            for (int i = 0; i < array.Count; i++)
            {
                CollectIndirectRefs(array[i], store, redactedRefs, removalCandidates);
            }
            return;
        }

        if (value is PdfDictionary dict)
        {
            foreach (KeyValuePair<PdfName, PdfPrimitive> entry in dict)
            {
                CollectIndirectRefs(entry.Value, store, redactedRefs, removalCandidates);
            }
        }
    }

    private static bool IsWidgetAnnotation(PdfDictionary annot)
    {
        return annot.TryGetValue(PdfName.Subtype, out PdfPrimitive? subtype)
            && subtype is PdfName name
            && name.Value == "Widget";
    }

    private static bool TryGetAnnotationRect(
        PdfDictionary annot, PdfObjectStore store, out RectangleF rect)
    {
        rect = RectangleF.Zero;
        if (!annot.TryGetValue(PdfName.Intern("Rect"), out PdfPrimitive? rectPrim))
        {
            return false;
        }

        if (store.Resolve(rectPrim) is not PdfArray array || array.Count < 4)
        {
            return false;
        }

        double x1 = PdfReal.ToDouble(store.Resolve(array[0]));
        double y1 = PdfReal.ToDouble(store.Resolve(array[1]));
        double x2 = PdfReal.ToDouble(store.Resolve(array[2]));
        double y2 = PdfReal.ToDouble(store.Resolve(array[3]));
        rect = RectangleF.FromCorners(x1, y1, x2, y2);
        return true;
    }

    private static bool IntersectsAnyRect(RectangleF rect, List<RectangleF> rects)
    {
        for (int i = 0; i < rects.Count; i++)
        {
            if (!rect.Intersect(rects[i]).IsEmpty)
            {
                return true;
            }
        }

        return false;
    }

    private static PdfDictionary? FindCatalog(PdfDocument document)
    {
        foreach (PdfIndirectObject obj in document.Objects.Objects)
        {
            if (obj.Value is PdfDictionary dict
                && dict.TryGetValue(PdfName.Type, out PdfPrimitive? type)
                && type is PdfName name
                && name.Value == "Catalog")
            {
                return dict;
            }
        }

        return null;
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
    // Running advance from the current text-line origin, in text space (0 at a
    // fresh Td / Tm / T*). The line position and orientation live in TextMatrix.
    internal double TextX { get; set; }

    // Baseline offset in text space (0); the glyph box's vertical extent is
    // derived from font metrics, not from this field.
    internal double TextY { get; set; }
    internal double FontSize { get; set; }

    // Per-byte-code glyph widths (1/1000 em) for the active font, or null when
    // the font's widths are unknown (then callers fall back to an estimate).
    internal int[]? GlyphWidths { get; set; }
    internal double Leading { get; set; }

    // Active font for the current text-showing operator, used to decode glyph
    // codes to Unicode for content-based pattern matching. Null when unknown.
    internal PdfFont? Font { get; set; }

    // Per-name cache of decoded fonts for this content stream, so the ToUnicode
    // CMap of each font is parsed at most once per page.
    internal Dictionary<string, PdfFont?> FontCache { get; } =
        new Dictionary<string, PdfFont?>(StringComparer.Ordinal);

    // Text patterns to redact by content (independent of geometry). Null or empty
    // when only rectangle-based redaction is in effect.
    internal IReadOnlyList<PatternRule>? Patterns { get; set; }

    // Page-space rectangles covering glyphs removed by a content pattern, so the
    // overlay can paint a box over them (rectangle-based removal already gets its
    // overlay from the rectangles). Filled by the text-showing operators.
    internal List<RectangleF> PatternOverlayBoxes { get; } = new List<RectangleF>();

    // Text-state spacing (PDF 9.4.4): added to each glyph's advance in text
    // space. Word spacing applies only to the single-byte space (code 32).
    // Both are part of the graphics state and are saved/restored by q/Q.
    internal double CharSpacing { get; set; }
    internal double WordSpacing { get; set; }
    private readonly Stack<double> _charSpacingStack = new Stack<double>();
    private readonly Stack<double> _wordSpacingStack = new Stack<double>();

    internal void PushGraphicsState()
    {
        _ctmStack.Push(Ctm);
        _charSpacingStack.Push(CharSpacing);
        _wordSpacingStack.Push(WordSpacing);
    }

    internal void PopGraphicsState()
    {
        if (_ctmStack.Count > 0)
        {
            Ctm = _ctmStack.Pop();
        }

        if (_charSpacingStack.Count > 0)
        {
            CharSpacing = _charSpacingStack.Pop();
        }

        if (_wordSpacingStack.Count > 0)
        {
            WordSpacing = _wordSpacingStack.Pop();
        }
    }

    internal void BeginText()
    {
        TextMatrix = Transform.Identity;
        TextX = 0;
        TextY = 0;
    }

    internal void EndText()
    {
        // No-op: text state is reset on next BT
    }

    internal void NextLine()
    {
        // T* / ' / " : move the text line matrix down by the leading and reset
        // the in-line advance to the new line origin.
        TextMatrix = TextMatrix.Translate(0, -Leading);
        TextX = 0;
        TextY = 0;
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
    /// <param name="replacements">
    /// Optional in-place replacement strings, parallel to <paramref name="rects"/>.
    /// </param>
    public RedactContext(Transform baseCtm, List<RectangleF> rects, List<string?> replacements)
    {
        BaseCtm = baseCtm;
        Rects = rects;
        Replacements = replacements;
    }

    /// <summary>Transform from local content space into page device space.</summary>
    public Transform BaseCtm { get; }

    /// <summary>Redaction rectangles in page device space.</summary>
    public List<RectangleF> Rects { get; }

    /// <summary>Optional in-place replacement strings, parallel to <see cref="Rects"/>.</summary>
    public List<string?> Replacements { get; }
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
