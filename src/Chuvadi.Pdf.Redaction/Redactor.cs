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
using Chuvadi.Pdf.Operations;
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
        run.VectorMode = options.VectorMode;
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
        HashSet<int>[] redactedMcids = new HashSet<int>[work.Count];

        // Partly redacted images: each page's blanked copies, added to the
        // page's resources in phase 3.
        List<BlankedImage>[] blankedImages = new List<BlankedImage>[work.Count];
        PatternUses[] patternUses = new PatternUses[work.Count];
        for (int i = 0; i < work.Count; i++)
        {
            blankedImages[i] = new List<BlankedImage>();
            patternUses[i] = new PatternUses();
        }

        if (options.MaxDegreeOfParallelism == 1)
        {
            for (int i = 0; i < work.Count; i++)
            {
                (byte[] Bytes, List<RectangleF> OverlayBoxes, HashSet<int> RedactedMcids) rewritten =
                    RewriteContent(
                        originals[i], PageContexts(work[i].Rects, work[i].Replacements), run,
                        work[i].Page.Resources, work[i].Patterns, blankedImages[i], patternUses[i]);
                redactedBytes[i] = rewritten.Bytes;
                redactedMcids[i] = rewritten.RedactedMcids;
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
                (byte[] Bytes, List<RectangleF> OverlayBoxes, HashSet<int> RedactedMcids) rewritten =
                    RewriteContent(
                        originals[i], PageContexts(work[i].Rects, work[i].Replacements), run,
                        work[i].Page.Resources, work[i].Patterns, blankedImages[i], patternUses[i]);
                redactedBytes[i] = rewritten.Bytes;
                redactedMcids[i] = rewritten.RedactedMcids;
                overlayBytes[i] = BuildOverlayWithPatterns(
                    work[i], rewritten.OverlayBoxes, options.OverlayColor, options.DrawOverlay);
            });
        }

        // Phase 3 (serial) — allocate object numbers and assemble the output in
        // stable order, so the result is identical regardless of parallelism.
        List<PdfDictionary> modifiedPages = new List<PdfDictionary>(work.Count);
        HashSet<int> strippedPageExtras = new HashSet<int>();
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
            StripPrivateEntries(modifiedPage, strippedPageExtras);
            allObjects.Add(new PdfIndirectObject(work[i].PageId, modifiedPage));
            modifiedPages.Add(modifiedPage);
            rewrittenPageNums.Add(work[i].PageId.ObjectNumber);
        }

        Dictionary<int, (PdfDictionary Dict, byte[] Content, List<BlankedImage> Images, PatternUses Uses)> rewrittenForms =
            RewriteCollectedForms(document, run, allObjects, removedContentStreamNums);

        // An image whose Do was removed must leave the file, not just the
        // content stream: prune it from the rewritten pages' and forms'
        // /XObject resources, then omit it below if nothing else reaches it.
        HashSet<int> orphanImageCandidates = new HashSet<int>(strippedPageExtras);
        RedactInheritedFormPatterns(rewrittenForms, run, allObjects, removedContentStreamNums, ref nextObjectNum, orphanImageCandidates);
        for (int i = 0; i < work.Count; i++)
        {
            PruneUnusedImages(modifiedPages[i], work[i].Page.Resources, redactedBytes[i], run, orphanImageCandidates);
            AddBlankedImages(modifiedPages[i], work[i].Page.Resources, blankedImages[i], run, allObjects, ref nextObjectNum);
            // The visible page: the MediaBox clipped by the CropBox.
            PdfRectangle media = work[i].Page.MediaBox;
            PdfRectangle crop = work[i].Page.CropBox;
            RectangleF visible = Intersect(
                new RectangleF(Math.Min(media.X1, media.X2), Math.Min(media.Y1, media.Y2), media.Width, media.Height),
                new RectangleF(Math.Min(crop.X1, crop.X2), Math.Min(crop.Y1, crop.Y2), crop.Width, crop.Height));
            // Annotations are drawn over the page: none may cover a witness.
            foreach (RectangleF annotRect in AnnotationRects(work[i].Page.Dictionary, run.Store))
            {
                WithdrawShown(patternUses[i], annotRect, null);
            }

            RedactPatternCells(
                modifiedPages[i], work[i].Page.Resources, PageContexts(work[i].Rects, work[i].Replacements),
                patternUses[i], run, allObjects, ref nextObjectNum, orphanImageCandidates, 0, visible);
        }

        foreach (KeyValuePair<int, (PdfDictionary Dict, byte[] Content, List<BlankedImage> Images, PatternUses Uses)> form in rewrittenForms)
        {
            PdfDictionary? formResources = form.Value.Dict.TryGetValue(PdfName.Resources, out PdfPrimitive? fr)
                ? run.Store.Resolve(fr) as PdfDictionary
                : null;
            PruneUnusedImages(form.Value.Dict, formResources, form.Value.Content, run, orphanImageCandidates);
            AddBlankedImages(form.Value.Dict, formResources, form.Value.Images, run, allObjects, ref nextObjectNum);
            if (form.Value.Dict.ContainsKey(PdfName.Resources))
            {
                RedactPatternCells(
                    form.Value.Dict, formResources, run.FormInvocations[form.Key], form.Value.Uses,
                    run, allObjects, ref nextObjectNum, orphanImageCandidates, 0, null);
            }

            StripPrivateEntries(form.Value.Dict, orphanImageCandidates);
        }

        // Resources a rewritten page inherits from a page-tree node: the
        // page now carries its own (pruned) copy, and the node's resources
        // keep only what the pages still inheriting them use.
        Dictionary<int, PdfDictionary> replacedNodes = PruneInheritedResources(
            document, pageIds, work, modifiedPages, rewrittenPageNums, run, pipeline, orphanImageCandidates);

        // LA-04: neutralise the structure tree so redacted text cannot be
        // recovered through /ActualText, /Alt, or marked-content references in the
        // logical structure. Map each rewritten page to the MCIDs whose marked
        // content was removed, then strip the matching structure elements'
        // replacement text. PDF 32000-1:2008 §14.7-§14.9.
        Dictionary<int, PdfPrimitive> rewrittenStructObjects = new Dictionary<int, PdfPrimitive>();
        if (document.HasStructTree)
        {
            Dictionary<int, HashSet<int>> redactedMcidsByPage = new Dictionary<int, HashSet<int>>();
            for (int i = 0; i < work.Count; i++)
            {
                if (redactedMcids[i] is HashSet<int> pageMcids && pageMcids.Count > 0)
                {
                    redactedMcidsByPage[work[i].PageId.ObjectNumber] = pageMcids;
                }
            }

            if (redactedMcidsByPage.Count > 0)
            {
                NeutralizeStructTree(document, redactedMcidsByPage, rewrittenStructObjects);
            }
        }

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

            if (rewrittenStructObjects.TryGetValue(obj.Id.ObjectNumber, out PdfPrimitive? rewrittenStruct))
            {
                allObjects.Add(new PdfIndirectObject(obj.Id, rewrittenStruct));
                continue;
            }

            if (replacedNodes.TryGetValue(obj.Id.ObjectNumber, out PdfDictionary? replacedNode))
            {
                allObjects.Add(new PdfIndirectObject(obj.Id, replacedNode));
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

        if (run.StripPropertyLists.Count > 0)
        {
            StripNamedPropertyLists(allObjects, run.StripPropertyLists);
        }

        if (orphanImageCandidates.Count > 0)
        {
            allObjects = OmitUnreachable(allObjects, trailer, orphanImageCandidates);
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

    /// <summary>
    /// Tracks one open marked-content sequence (BDC/BMC … EMC) during a content
    /// rewrite: its operator and operands, its MCID when present, whether any of
    /// its body was redacted, and an optional buffer used to defer emission when
    /// the sequence carries an inline /ActualText or /Alt.
    /// </summary>
    private sealed class MarkedContentScope
    {
        public string Op { get; set; } = string.Empty;

        public List<PdfToken> Operands { get; set; } = new List<PdfToken>();

        public int? Mcid { get; set; }

        public MemoryStream? Buffer { get; set; }

        public bool Dropped { get; set; }

        // A named property list (BDC /Tag /Name) carrying replacement text.
        public PdfDictionary? NamedTextProperties { get; set; }
    }

    // blankedImages receives the blanked copies of partly redacted images;
    // null when new images cannot be added to the content's resources (a
    // form that inherits its resources), and such images are then removed.
    // patternUses (optional) receives the tiling-pattern names the content
    // paints with, and the local-space rectangles where a pattern fill is
    // known to show (see PatternUses).
    private static (byte[] Bytes, List<RectangleF> OverlayBoxes, HashSet<int> RedactedMcids) RewriteContent(
        byte[] content, IReadOnlyList<RedactContext> contexts, RedactRun run, PdfDictionary? resources,
        IReadOnlyList<PatternRule> patterns, List<BlankedImage>? blankedImages, PatternUses? patternUses = null)
    {
        using (MemoryStream input = new MemoryStream(content))
        using (MemoryStream output = new MemoryStream())
        using (PdfTokenizer tok = new PdfTokenizer(input))
        {
            RedactState state = new RedactState();
            state.Patterns = patterns;
            state.BlankedImages = blankedImages;
            List<PdfToken> pendingOperands = new List<PdfToken>();
            PathAccumulator path = new PathAccumulator();

            // Marked-content scope stack (BDC/BMC … EMC). Each scope records its
            // MCID (so a redacted sequence can be matched to its structure
            // element) and, when it carries an inline /ActualText or /Alt, buffers
            // its body so that property can be stripped if the body is redacted.
            // Set of MCIDs whose marked content had at least one glyph removed.
            List<MarkedContentScope> mcStack = new List<MarkedContentScope>();
            HashSet<int> redactedMcids = new HashSet<int>();

            // The stream every emitter writes to: the innermost open buffered
            // scope's buffer, or the main output when none is buffering.
            MemoryStream Sink()
            {
                for (int s = mcStack.Count - 1; s >= 0; s--)
                {
                    if (mcStack[s].Buffer is MemoryStream buffer)
                    {
                        return buffer;
                    }
                }

                return output;
            }

            // Mark every open marked-content scope as having had content removed,
            // so an ancestor's /ActualText is stripped and each scope's MCID is
            // recorded as redacted once its EMC is reached.
            void NoteDropped()
            {
                for (int s = 0; s < mcStack.Count; s++)
                {
                    mcStack[s].Dropped = true;
                }
            }

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
                    if (patternUses is not null)
                    {
                        NoteOverpaint("BI", pendingOperands, state, path, patternUses, run, resources);
                        AddPainted(patternUses, state.FillPattern, anywhere: true);
                    }

                    if (HandleInlineImage(tok, content, Sink(), state, contexts, token.ByteOffset, run, resources))
                    {
                        NoteDropped();
                    }

                    pendingOperands.Clear();
                    continue;
                }

                string op = token.RawText;
                TrackPatternPaint(op, pendingOperands, state, path, patternUses, run, resources);

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
                    // preserved. Otherwise the path hits a region and must lose
                    // (at least) the geometry inside it.
                    bool hitsRegion = path.Active
                        && !path.HasClip
                        && op != "n"
                        && PathBoxIntersects(path, contexts);

                    if (!hitsRegion)
                    {
                        MemoryStream sink = Sink();
                        FlushPath(sink, path);
                        WriteTokens(sink, pendingOperands);
                        byte[] paintBytes = Encoding.Latin1.GetBytes(op + "\n");
                        sink.Write(paintBytes, 0, paintBytes.Length);
                    }
                    else if (run.VectorMode == VectorRedactionMode.SubtractPrecise)
                    {
                        // Keep the provably untouched geometry; null means the
                        // construct was too doubtful to subtract precisely and
                        // the whole path drops (the legacy behaviour).
                        string? surviving = VectorSubtractor.TrySubtract(
                            path.Buffer, op, state, contexts);
                        if (!string.IsNullOrEmpty(surviving))
                        {
                            WriteRaw(Sink(), surviving);
                        }
                    }

                    path.Reset();
                    pendingOperands.Clear();
                    continue;
                }

                // A non-path operator while a path is buffered (illegal in
                // valid content but rendered by viewers): keep the path
                // BUFFERED across it. Flushing it here would emit the
                // construction operators with no region check, letting a
                // crafted "m … l … 5 w … S" keep ink inside a redaction
                // rectangle. The state operator itself is handled normally
                // below; since construction operators consume no graphics
                // state until the paint operator, re-emitting them after the
                // state operator is semantically identical.

                // Marked-content sequences: BDC (tag + property list), BMC (tag),
                // EMC. A sequence carrying an inline /ActualText or /Alt is
                // buffered so the property can be dropped if its body is redacted;
                // otherwise BDC/BMC is emitted immediately. MCID is recorded so the
                // structure tree can be neutralised by the post-pass.
                if (op == "BDC" || op == "BMC")
                {
                    int? mcid = op == "BDC"
                        ? ParseBdcMcid(pendingOperands, resources, run.Store)
                        : null;
                    bool carriesActualText = op == "BDC" && BdcHasInlineActualText(pendingOperands);

                    MarkedContentScope scope = new MarkedContentScope
                    {
                        Op = op,
                        Operands = new List<PdfToken>(pendingOperands),
                        Mcid = mcid,
                        Buffer = carriesActualText ? new MemoryStream() : null,
                        NamedTextProperties = op == "BDC"
                            ? ResolveNamedTextProperties(pendingOperands, resources, run.Store)
                            : null,
                    };

                    if (!carriesActualText)
                    {
                        MemoryStream sink = Sink();
                        WriteTokens(sink, pendingOperands);
                        sink.Write(Encoding.Latin1.GetBytes(op + "\n"), 0,
                            Encoding.Latin1.GetByteCount(op + "\n"));
                    }

                    mcStack.Add(scope);
                    pendingOperands.Clear();
                    continue;
                }

                if (op == "EMC")
                {
                    if (mcStack.Count == 0)
                    {
                        WriteRaw(Sink(), "EMC\n");
                        pendingOperands.Clear();
                        continue;
                    }

                    MarkedContentScope scope = mcStack[mcStack.Count - 1];
                    mcStack.RemoveAt(mcStack.Count - 1);

                    if (scope.Buffer is MemoryStream body)
                    {
                        // Buffered (carried /ActualText or /Alt): re-emit BDC with
                        // those properties dropped when the body was redacted, then
                        // the body and EMC, into the now-current sink.
                        MemoryStream sink = Sink();
                        WriteBdcOperands(sink, scope.Operands, stripActualText: scope.Dropped);
                        sink.Write(Encoding.Latin1.GetBytes(scope.Op + "\n"), 0,
                            Encoding.Latin1.GetByteCount(scope.Op + "\n"));
                        byte[] bodyBytes = body.ToArray();
                        sink.Write(bodyBytes, 0, bodyBytes.Length);
                        WriteRaw(sink, "EMC\n");
                    }
                    else
                    {
                        WriteRaw(Sink(), "EMC\n");
                    }

                    if (scope.Mcid is int closedMcid && scope.Dropped)
                    {
                        redactedMcids.Add(closedMcid);
                    }

                    if (scope.Dropped && scope.NamedTextProperties is PdfDictionary named)
                    {
                        lock (run.Sync)
                        {
                            run.StripPropertyLists.Add(named);
                        }
                    }

                    pendingOperands.Clear();
                    continue;
                }

                // Glyph-level text redaction: rebuild Tj and TJ so only the
                // in-region glyphs are removed, keeping neighbours in place.
                // ' and " are decomposed below so their text is redacted the same way.
                if (op == "Tj")
                {
                    if (EmitRedactedText(Sink(), pendingOperands, state, contexts))
                    {
                        NoteDropped();
                    }

                    pendingOperands.Clear();
                    continue;
                }

                if (op == "TJ")
                {
                    if (EmitRedactedTJ(Sink(), pendingOperands, state, contexts))
                    {
                        NoteDropped();
                    }

                    pendingOperands.Clear();
                    continue;
                }

                // ' (move to next line and show) and " (set word/char spacing,
                // move to next line, and show) are decomposed into their explicit
                // equivalents so the shown string runs through the same glyph-level
                // pattern redaction as Tj/TJ, instead of being dropped whole.
                if (op == "'" && pendingOperands.Count >= 1)
                {
                    MemoryStream sink = Sink();
                    WriteRaw(sink, "T*\n");
                    state.NextLine();
                    List<PdfToken> stringOnly = new List<PdfToken> { pendingOperands[pendingOperands.Count - 1] };
                    if (EmitRedactedText(sink, stringOnly, state, contexts))
                    {
                        NoteDropped();
                    }

                    pendingOperands.Clear();
                    continue;
                }

                if (op == "\"" && pendingOperands.Count >= 3)
                {
                    MemoryStream sink = Sink();
                    state.WordSpacing = ParseDouble(pendingOperands[0]);
                    state.CharSpacing = ParseDouble(pendingOperands[1]);
                    WriteRaw(
                        sink,
                        pendingOperands[0].RawText + " Tw\n"
                            + pendingOperands[1].RawText + " Tc\nT*\n");
                    state.NextLine();
                    List<PdfToken> stringOnly = new List<PdfToken> { pendingOperands[2] };
                    if (EmitRedactedText(sink, stringOnly, state, contexts))
                    {
                        NoteDropped();
                    }

                    pendingOperands.Clear();
                    continue;
                }

                // An image XObject meeting a redaction rectangle: only the
                // covered pixels are blanked (in a new image under a new
                // name); an image wholly covered, or one that cannot be
                // decoded, is removed.
                if (op == "Do"
                    && ResolveXObjectRef(pendingOperands, resources, run.Store) is { } placed
                    && IsImage(placed.Stream)
                    && ShouldRedactImageAtCtm(state, contexts))
                {
                    (int, Transform) cacheKey = (placed.ObjNum, state.Ctm);
                    if (!state.BlankedImageCache.TryGetValue(cacheKey, out (ImageBlankOutcome Outcome, string? Name) edit))
                    {
                        PdfStream imageStream = placed.Stream;
                        ImageBlankOutcome outcome = BlankImageAtCtm(
                            () =>
                            {
                                lock (run.Sync)
                                {
                                    return ImageSampleCropper.FromXObject(imageStream, run.Store);
                                }
                            },
                            state, contexts, run, resources, out string? newName);
                        edit = (outcome, newName);
                        state.BlankedImageCache[cacheKey] = edit;
                    }

                    if (edit.Outcome == ImageBlankOutcome.Untouched)
                    {
                        MemoryStream sink = Sink();
                        WriteTokens(sink, pendingOperands);
                        WriteRaw(sink, "Do\n");
                    }
                    else
                    {
                        if (edit.Outcome == ImageBlankOutcome.Blanked && edit.Name is string blankedName)
                        {
                            WriteRaw(Sink(), "/" + blankedName + " Do\n");
                        }

                        NoteDropped();
                    }

                    pendingOperands.Clear();
                    continue;
                }

                bool drop = ProcessOperator(op, pendingOperands, state, contexts, run, resources);

                if (drop)
                {
                    // An image, form or shading removed inside a marked-content
                    // scope: the scope's replacement text (a Figure's /Alt)
                    // describes what was removed.
                    NoteDropped();
                }
                else
                {
                    MemoryStream sink = Sink();
                    WriteTokens(sink, pendingOperands);
                    sink.Write(Encoding.Latin1.GetBytes(op + "\n"), 0,
                        Encoding.Latin1.GetByteCount(op + "\n"));
                }

                pendingOperands.Clear();
            }

            // Flush any path left unterminated at end of stream.
            if (path.Buffer.Count > 0)
            {
                FlushPath(Sink(), path);
            }

            // Flush any marked-content scope left open at end of stream (malformed
            // content): re-emit innermost-first so no buffered body is lost.
            while (mcStack.Count > 0)
            {
                MarkedContentScope scope = mcStack[mcStack.Count - 1];
                mcStack.RemoveAt(mcStack.Count - 1);
                if (scope.Buffer is MemoryStream body)
                {
                    MemoryStream sink = Sink();
                    WriteBdcOperands(sink, scope.Operands, stripActualText: scope.Dropped);
                    sink.Write(Encoding.Latin1.GetBytes(scope.Op + "\n"), 0,
                        Encoding.Latin1.GetByteCount(scope.Op + "\n"));
                    byte[] bodyBytes = body.ToArray();
                    sink.Write(bodyBytes, 0, bodyBytes.Length);
                }

                if (scope.Mcid is int closedMcid && scope.Dropped)
                {
                    redactedMcids.Add(closedMcid);
                }
            }

            return (output.ToArray(), state.PatternOverlayBoxes, redactedMcids);
        }
    }

    /// <summary>
    /// Extracts the <c>/MCID</c> of a <c>BDC</c> marked-content operator from its
    /// operands, whether the property list is given inline (<c>&lt;&lt;/MCID n&gt;&gt;</c>)
    /// or by name into the resource <c>/Properties</c> sub-dictionary. Returns null
    /// when no MCID is present. PDF 32000-1:2008 §14.6.
    /// </summary>
    private static int? ParseBdcMcid(
        List<PdfToken> operands, PdfDictionary? resources, PdfObjectStore store)
    {
        for (int i = 0; i + 1 < operands.Count; i++)
        {
            if (operands[i].Type == PdfTokenType.Name
                && operands[i].RawText == "MCID"
                && int.TryParse(operands[i + 1].RawText, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int inlineMcid))
            {
                return inlineMcid;
            }
        }

        // Named property list: the operand after the tag is a name resolved through
        // the page/form resource dictionary's /Properties entry.
        if (resources is null)
        {
            return null;
        }

        PdfDictionary? properties = resources.GetDictionary(PdfName.Intern("Properties"));
        if (properties is null)
        {
            return null;
        }

        for (int i = 1; i < operands.Count; i++)
        {
            if (operands[i].Type != PdfTokenType.Name)
            {
                continue;
            }

            if (!properties.TryGetValue(PdfName.Intern(operands[i].RawText), out PdfPrimitive? value))
            {
                continue;
            }

            PdfDictionary? prop = store.ResolveAs<PdfDictionary>(value);
            if (prop is not null
                && prop.TryGetValue(PdfName.Intern("MCID"), out PdfPrimitive? mcidValue)
                && store.Resolve(mcidValue) is PdfInteger mcidInt)
            {
                return mcidInt.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns true when a <c>BDC</c> operator's inline property list carries an
    /// <c>/ActualText</c> or <c>/Alt</c> entry — text that would survive redaction
    /// of the body unless it is dropped with it. PDF 32000-1:2008 §14.9.4.
    /// </summary>
    // Marked-content and structure keys whose values carry text that stands in
    // for the content: replacement text (§14.9.4), alternate description
    // (§14.9.3) and abbreviation expansion (§14.9.5).
    private static bool IsReplacementTextKey(string key) =>
        key is "ActualText" or "Alt" or "E";

    // For BDC /Tag /Name, resolves the named property list from /Resources
    // /Properties and returns it when it carries replacement text.
    private static PdfDictionary? ResolveNamedTextProperties(
        List<PdfToken> operands, PdfDictionary? resources, PdfObjectStore store)
    {
        if (operands.Count < 2
            || operands[^1].Type != PdfTokenType.Name
            || resources is null
            || !resources.TryGetValue(PdfName.Intern("Properties"), out PdfPrimitive? pv)
            || store.Resolve(pv) is not PdfDictionary properties
            || !properties.TryGetValue(PdfName.Intern(operands[^1].RawText), out PdfPrimitive? entry)
            || store.Resolve(entry) is not PdfDictionary list)
        {
            return null;
        }

        foreach (PdfName key in list.Keys)
        {
            if (IsReplacementTextKey(key.Value))
            {
                return list;
            }
        }

        return null;
    }

    private static bool BdcHasInlineActualText(List<PdfToken> operands)
    {
        for (int i = 0; i < operands.Count; i++)
        {
            if (operands[i].Type == PdfTokenType.Name && IsReplacementTextKey(operands[i].RawText))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Serialises a <c>BDC</c> operator's operands like <see cref="WriteTokens"/>,
    /// optionally dropping any inline <c>/ActualText</c> or <c>/Alt</c> entry (the
    /// name plus its string value) so a redacted sequence cannot be recovered from
    /// its replacement text.
    /// </summary>
    private static void WriteBdcOperands(
        MemoryStream output, List<PdfToken> operands, bool stripActualText)
    {
        bool skipValue = false;
        foreach (PdfToken t in operands)
        {
            if (skipValue)
            {
                skipValue = false;
                continue;
            }

            if (stripActualText
                && t.Type == PdfTokenType.Name
                && IsReplacementTextKey(t.RawText))
            {
                skipValue = true;
                continue;
            }

            if (t.Type == PdfTokenType.Name)
            {
                output.WriteByte((byte)'/');
            }

            output.Write(t.RawBytes, 0, t.RawBytes.Length);
            output.WriteByte(32);
        }
    }

    /// <summary>
    /// Returns true when the operator-with-operands should be dropped
    /// (i.e., its visible text intersects a redaction rectangle).
    /// </summary>
    /// <summary>
    /// Strips <c>/ActualText</c> and <c>/Alt</c> from every structure element
    /// whose marked content (its own or a descendant's MCID on the relevant page)
    /// was redacted, so the redacted text cannot be recovered through the logical
    /// structure tree. Rewritten structure objects are collected in
    /// <paramref name="rewritten"/> keyed by object number for the output pass.
    /// </summary>
    private static void NeutralizeStructTree(
        PdfDocument document,
        Dictionary<int, HashSet<int>> redactedMcidsByPage,
        Dictionary<int, PdfPrimitive> rewritten)
    {
        PdfDictionary? catalog = FindCatalog(document);
        if (catalog is null
            || !catalog.TryGetValue(PdfName.Intern("StructTreeRoot"), out PdfPrimitive? rootValue))
        {
            return;
        }

        PdfObjectStore store = document.Objects;
        HashSet<int> selfStrip = new HashSet<int>();
        HashSet<PdfDictionary> inlineStrip = new HashSet<PdfDictionary>(
            ReferenceEqualityComparer.Instance);
        Dictionary<int, PdfPrimitive> indirectValues = new Dictionary<int, PdfPrimitive>();
        Dictionary<int, HashSet<int>> inlineOwners = new Dictionary<int, HashSet<int>>();
        Dictionary<int, bool> resultCache = new Dictionary<int, bool>();
        HashSet<int> visiting = new HashSet<int>();

        bool IsMcidRedacted(int pageObjNum, int mcid)
        {
            return redactedMcidsByPage.TryGetValue(pageObjNum, out HashSet<int>? mcids)
                && mcids.Contains(mcid);
        }

        bool CollectElement(PdfDictionary element, int thisObjNum, bool isIndirect, int inheritedPg)
        {
            int pageObjNum = inheritedPg;
            if (element.TryGetValue(PdfName.Intern("Pg"), out PdfPrimitive? pgValue)
                && pgValue is PdfReference pgRef)
            {
                pageObjNum = pgRef.ObjectNumber;
            }

            // A marked-content reference dictionary points straight at an MCID.
            if (element.TryGetValue(PdfName.Intern("MCID"), out PdfPrimitive? mcidValue)
                && store.Resolve(mcidValue) is PdfInteger mcidInt)
            {
                return IsMcidRedacted(pageObjNum, mcidInt.Value);
            }

            bool redacted = false;
            if (element.TryGetValue(PdfName.Intern("K"), out PdfPrimitive? kids))
            {
                redacted |= Collect(kids, thisObjNum, pageObjNum);
            }

            bool hasReplacementText = element.ContainsKey(PdfName.Intern("ActualText"))
                || element.ContainsKey(PdfName.Intern("Alt"))
                || element.ContainsKey(PdfName.Intern("E"));
            if (redacted && hasReplacementText)
            {
                if (isIndirect)
                {
                    selfStrip.Add(thisObjNum);
                }
                else
                {
                    inlineStrip.Add(element);
                    if (!inlineOwners.TryGetValue(thisObjNum, out HashSet<int>? owned))
                    {
                        owned = new HashSet<int>();
                        inlineOwners[thisObjNum] = owned;
                    }

                    owned.Add(thisObjNum);
                }
            }

            return redacted;
        }

        bool Collect(PdfPrimitive node, int ownerObjNum, int inheritedPg)
        {
            bool isIndirect = node is PdfReference;
            int thisObjNum = isIndirect ? ((PdfReference)node).ObjectNumber : ownerObjNum;
            if (isIndirect)
            {
                if (resultCache.TryGetValue(thisObjNum, out bool cached))
                {
                    return cached;
                }

                if (!visiting.Add(thisObjNum))
                {
                    return false;
                }
            }

            PdfPrimitive resolved = store.Resolve(node);
            if (isIndirect && (resolved is PdfDictionary || resolved is PdfArray))
            {
                indirectValues[thisObjNum] = resolved;
            }

            bool redacted;
            if (resolved is PdfInteger leaf)
            {
                redacted = IsMcidRedacted(inheritedPg, leaf.Value);
            }
            else if (resolved is PdfArray array)
            {
                redacted = false;
                foreach (PdfPrimitive item in array)
                {
                    redacted |= Collect(item, thisObjNum, inheritedPg);
                }
            }
            else if (resolved is PdfDictionary dict)
            {
                redacted = CollectElement(dict, thisObjNum, isIndirect, inheritedPg);
            }
            else
            {
                redacted = false;
            }

            if (isIndirect)
            {
                visiting.Remove(thisObjNum);
                resultCache[thisObjNum] = redacted;
            }

            return redacted;
        }

        int rootOwner = rootValue is PdfReference rootRef ? rootRef.ObjectNumber : -1;
        Collect(rootValue, rootOwner, -1);

        HashSet<int> owners = new HashSet<int>(selfStrip);
        owners.UnionWith(inlineOwners.Keys);
        foreach (int objNum in owners)
        {
            if (!indirectValues.TryGetValue(objNum, out PdfPrimitive? value))
            {
                continue;
            }

            rewritten[objNum] = CloneStrippingActualText(
                value, selfStrip.Contains(objNum), inlineStrip);
        }
    }

    /// <summary>
    /// Deep-copies a structure object, dropping <c>/ActualText</c>, <c>/Alt</c> and <c>/E</c>
    /// from the object itself when <paramref name="stripSelf"/> is set and from any
    /// inline descendant dictionary in <paramref name="inlineStrip"/>. Indirect
    /// references are left untouched (their targets are rewritten separately).
    /// </summary>
    private static PdfPrimitive CloneStrippingActualText(
        PdfPrimitive value, bool stripSelf, HashSet<PdfDictionary> inlineStrip)
    {
        if (value is PdfDictionary dict)
        {
            bool stripThis = stripSelf || inlineStrip.Contains(dict);
            PdfDictionary copy = new PdfDictionary();
            foreach (KeyValuePair<PdfName, PdfPrimitive> entry in dict)
            {
                if (stripThis && IsReplacementTextKey(entry.Key.Value))
                {
                    continue;
                }

                copy.Set(entry.Key, CloneStrippingActualText(entry.Value, false, inlineStrip));
            }

            return copy;
        }

        if (value is PdfArray array)
        {
            List<PdfPrimitive> items = new List<PdfPrimitive>(array.Count);
            foreach (PdfPrimitive item in array)
            {
                items.Add(CloneStrippingActualText(item, false, inlineStrip));
            }

            return new PdfArray(items);
        }

        return value;
    }

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
            case "w":
                // Viewers consume the LAST operand (stack semantics), so a
                // crafted "0.1 50 w" must model width 50, not 0.1.
                if (operands.Count >= 1 && operands[^1].IsNumeric)
                {
                    state.LineWidth = ParseDouble(operands[^1]);
                }

                return false;
            case "M":
                if (operands.Count >= 1 && operands[^1].IsNumeric)
                {
                    state.MiterLimit = ParseDouble(operands[^1]);
                }

                return false;
            case "gs":
                ApplyGs(operands, state, resources, run.Store);
                return false;

            // ── Text state ─────────────────────────────────────────────────
            case "BT": state.BeginText(); return false;
            case "ET": state.EndText(); return false;
            case "Tf": ApplyTf(operands, state, resources, run.Store); return false;
            case "Td": ApplyTd(operands, state); return false;
            case "TD": ApplyTD(operands, state); return false;
            case "Tm": ApplyTm(operands, state); return false;
            case "T*": state.NextLine(); return false;
            case "TL":
                // Text leading (PDF 9.3.5), used by T*, ' and ".
                if (operands.Count >= 1 && operands[^1].IsNumeric)
                {
                    state.Leading = ParseDouble(operands[^1]);
                }

                return false;
            case "Tc":
                if (operands.Count >= 1 && operands[^1].IsNumeric)
                {
                    state.CharSpacing = ParseDouble(operands[^1]);
                }

                return false;
            case "Tw":
                if (operands.Count >= 1 && operands[^1].IsNumeric)
                {
                    state.WordSpacing = ParseDouble(operands[^1]);
                }

                return false;
            case "Ts":
                if (operands.Count >= 1 && operands[^1].IsNumeric)
                {
                    state.TextRise = ParseDouble(operands[^1]);
                }

                return false;
            case "Tz":
                if (operands.Count >= 1 && operands[^1].IsNumeric)
                {
                    state.HorizontalScaling = ParseDouble(operands[^1]) / 100.0;
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
                    // Pass only the string operand (the LAST operand) to detection.
                    List<PdfToken> stringOnly = new List<PdfToken> { operands[^1] };
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
    /// When the unit square mapped by the current CTM meets a redaction
    /// rectangle (inline images paint the unit square, like <c>Do</c> images)
    /// the covered pixels are blanked and the image is re-emitted as a new
    /// image XObject; a wholly covered or undecodable image is removed.
    /// Otherwise the original bytes are copied verbatim. Returns true when the
    /// image was changed or removed.
    /// </summary>
    private static bool HandleInlineImage(
        PdfTokenizer tok, byte[] content, MemoryStream output,
        RedactState state, IReadOnlyList<RedactContext> contexts, long biStart,
        RedactRun run, PdfDictionary? resources)
    {
        // Exact-extent consumption (3.18): /W /H /BPC /CS give the data length
        // for unfiltered images, so a byte pair that merely looks like "EI"
        // inside the binary cannot end the image early (which would
        // re-tokenise — and leak — the image's tail).
        int eiEnd = FindInlineImageEnd(tok, content);
        if (eiEnd < 0)
        {
            // No terminating EI (malformed): the rest of the stream is
            // unknowable — keep it verbatim rather than guessing what to drop.
            output.Write(content, (int)biStart, content.Length - (int)biStart);
            tok.Seek(content.Length);
            return false;
        }

        if (!ShouldRedactImageAtCtm(state, contexts))
        {
            output.Write(content, (int)biStart, eiEnd - (int)biStart);
            output.WriteByte((byte)'\n');
            tok.Seek(eiEnd);
            return false;
        }

        ImageBlankOutcome outcome = BlankImageAtCtm(
            () =>
            {
                lock (run.Sync)
                {
                    return ImageSampleCropper.FromInline(content, (int)biStart, eiEnd, resources, run.Store);
                }
            },
            state, contexts, run, resources, out string? name);

        tok.Seek(eiEnd);
        if (outcome == ImageBlankOutcome.Untouched)
        {
            output.Write(content, (int)biStart, eiEnd - (int)biStart);
            output.WriteByte((byte)'\n');
            return false;
        }

        if (outcome == ImageBlankOutcome.Blanked && name is not null)
        {
            WriteRaw(output, "/" + name + " Do\n");
        }

        return true;
    }

    /// <summary>
    /// Partial image redaction: maps every redaction rectangle into the
    /// image's unit square (through the current CTM and each context's
    /// placement) and blanks the pixels whose cells meet one, keeping the
    /// rest of the image. A blanked copy is recorded under a new resource
    /// name. Returns <see cref="ImageBlankOutcome.Failed"/> (the caller then
    /// removes the image) when new images cannot be added here or the
    /// placement is singular.
    /// </summary>
    private static ImageBlankOutcome BlankImageAtCtm(
        Func<ImageSource?> load, RedactState state, IReadOnlyList<RedactContext> contexts,
        RedactRun run, PdfDictionary? resources, out string? name)
    {
        name = null;
        if (state.BlankedImages is null || state.BlankedImages.Count >= MaxBlankedImagesPerStream)
        {
            return ImageBlankOutcome.Failed;
        }

        List<List<(double X, double Y)>> regions = new List<List<(double X, double Y)>>();
        for (int c = 0; c < contexts.Count; c++)
        {
            RedactContext context = contexts[c];
            if (context.Rects.Count == 0)
            {
                continue;
            }

            Transform inverse;
            try
            {
                inverse = state.Ctm.Multiply(context.BaseCtm).Invert();
            }
            catch (InvalidOperationException)
            {
                return ImageBlankOutcome.Failed;
            }

            foreach (RectangleF r in context.Rects)
            {
                List<(double X, double Y)> region = new List<(double X, double Y)>(4);
                foreach (PointF corner in new[]
                {
                    new PointF(r.X, r.Y),
                    new PointF(r.X + r.Width, r.Y),
                    new PointF(r.X + r.Width, r.Y + r.Height),
                    new PointF(r.X, r.Y + r.Height),
                })
                {
                    PointF u = inverse.TransformPoint(corner);
                    region.Add((u.X, u.Y));
                }

                regions.Add(region);
            }
        }

        List<(PdfReference Placeholder, PdfStream Stream)> masks = new List<(PdfReference Placeholder, PdfStream Stream)>();
        PdfReference Allocate(PdfStream stream)
        {
            PdfReference placeholder = new PdfReference(PdfObjectId.Invalid);
            masks.Add((placeholder, stream));
            return placeholder;
        }

        ImageBlankOutcome outcome = ImageSampleCropper.BlankRegions(load, regions, Allocate, out PdfStream? edited);
        if (outcome == ImageBlankOutcome.Blanked)
        {
            if (edited is null)
            {
                return ImageBlankOutcome.Failed;
            }

            name = NewBlankedImageName(state.BlankedImages, resources, run);
            state.BlankedImages.Add(new BlankedImage(name, edited, masks));
        }

        return outcome;
    }

    // Blanked copies one content stream may add; past this (a crafted page
    // placing one image many times) further partly covered images are
    // removed whole, which is always safe (B15).
    private const int MaxBlankedImagesPerStream = 256;

    // Tile copies examined per pattern before a cell is treated as
    // covering everything (its content is then removed, B15).
    private const int MaxPatternTileCopies = 4096;

    // Nested patterns (a pattern painted inside a pattern cell) are followed
    // to this depth; deeper cells are emptied.
    private const int MaxPatternDepth = 4;

    // A form that inherits its resources and could not be given its own
    // copy (it is drawn with different resources in different places) cannot
    // take a new pattern name, so each tiling pattern it paints is rewritten
    // in place (same object number), once per pattern object, against the
    // form's contexts in the invocations that resolve the name to it. Any
    // other use of that pattern object then sees the redacted cell too (B15:
    // over-redact rather than keep content a box covered).
    private static void RedactInheritedFormPatterns(
        Dictionary<int, (PdfDictionary Dict, byte[] Content, List<BlankedImage> Images, PatternUses Uses)> rewrittenForms,
        RedactRun run, List<PdfIndirectObject> allObjects, HashSet<int> excluded, ref int nextObjectNum,
        HashSet<int> candidates)
    {
        foreach (KeyValuePair<int, (PdfDictionary Dict, byte[] Content, List<BlankedImage> Images, PatternUses Uses)> form in rewrittenForms)
        {
            PatternUses uses = form.Value.Uses;
            if (uses.Painted.Count == 0
                || form.Value.Dict.ContainsKey(PdfName.Resources)
                || !run.InheritedInvocations.TryGetValue(form.Key, out List<(PdfDictionary? Resources, List<RedactContext> Contexts)>? invocations))
            {
                continue;
            }

            foreach (string name in uses.Painted)
            {
                // The form's contexts, grouped by the pattern object the name
                // resolves to in each invocation.
                Dictionary<int, List<RedactContext>> byPattern = new Dictionary<int, List<RedactContext>>();
                foreach ((PdfDictionary? resources, List<RedactContext> contexts) in invocations)
                {
                    if (resources is not null
                        && resources.TryGetValue(PdfName.Intern("Pattern"), out PdfPrimitive? pv)
                        && run.Store.Resolve(pv) is PdfDictionary patterns
                        && patterns.TryGetValue(PdfName.Intern(name), out PdfPrimitive? entry)
                        && entry is PdfReference reference)
                    {
                        int number = reference.ObjectId.ObjectNumber;
                        if (!byPattern.TryGetValue(number, out List<RedactContext>? list))
                        {
                            list = new List<RedactContext>();
                            byPattern[number] = list;
                        }

                        list.AddRange(contexts);
                    }
                }

                List<RectangleF>? painted = uses.PaintedAnywhere.Contains(name)
                    ? null
                    : uses.PaintedBounds.TryGetValue(name, out List<RectangleF>? bounds) ? bounds : new List<RectangleF>();
                foreach (KeyValuePair<int, List<RedactContext>> group in byPattern)
                {
                    PdfStream? cell = run.ReplacedPatterns.TryGetValue(group.Key, out PdfStream? done)
                        ? done
                        : run.Store.ResolveById(new PdfObjectId(group.Key, 0)) as PdfStream;
                    if (cell is null || NumberOf(run, cell.Dictionary, "PatternType") != 1)
                    {
                        continue;
                    }

                    PdfStream? redacted = RedactPatternCell(
                        cell, group.Value, painted, null, default, run, allObjects, ref nextObjectNum, candidates, 0);
                    if (redacted is null)
                    {
                        continue;
                    }

                    allObjects.RemoveAll(o => o.Id.ObjectNumber == group.Key);
                    allObjects.Add(new PdfIndirectObject(new PdfObjectId(group.Key, 0), redacted));
                    excluded.Add(group.Key);
                    run.ReplacedPatterns[group.Key] = redacted;
                }
            }
        }
    }

    /// <summary>
    /// Redacts the content of tiling-pattern cells (PDF 8.7.3.1) a content
    /// stream paints with. The painted pattern area inside a box is already
    /// removed with the path that painted it, but the cell's own content
    /// (an image, text) stays in the file. For each tile copy that reaches a
    /// box, the cell is rewritten as if placed there, so whatever that copy
    /// would show inside the box leaves the cell; the rewritten cell
    /// replaces the pattern in the container's resources (the original
    /// leaves the file unless something else uses it). A cell is left whole
    /// when some tile copy that shows the entire cell — inside a known
    /// pattern-filled rectangle and clear of every box — proves its content
    /// is visible on the page anyway; removing it would only change the
    /// page elsewhere.
    /// </summary>
    private static void RedactPatternCells(
        PdfDictionary container, PdfDictionary? baseResources, IReadOnlyList<RedactContext> contexts,
        PatternUses uses, RedactRun run, List<PdfIndirectObject> allObjects, ref int nextObjectNum,
        HashSet<int> candidates, int depth, RectangleF? pageBox)
    {
        if (uses.Painted.Count == 0)
        {
            return;
        }

        PdfDictionary? current = container.TryGetValue(PdfName.Resources, out PdfPrimitive? rv)
            ? run.Store.Resolve(rv) as PdfDictionary
            : baseResources;
        if (current is null
            || !current.TryGetValue(PdfName.Intern("Pattern"), out PdfPrimitive? pv)
            || run.Store.Resolve(pv) is not PdfDictionary patterns)
        {
            return;
        }

        PdfDictionary? newPatterns = null;
        foreach (string name in uses.Painted)
        {
            PdfName key = PdfName.Intern(name);
            if (!patterns.TryGetValue(key, out PdfPrimitive? entry)
                || run.Store.Resolve(entry) is not PdfStream cell
                || NumberOf(run, cell.Dictionary, "PatternType") != 1)
            {
                continue;
            }

            if (entry is PdfReference replacedRef
                && run.ReplacedPatterns.TryGetValue(replacedRef.ObjectId.ObjectNumber, out PdfStream? replaced))
            {
                cell = replaced;
            }

            List<RectangleF>? painted = uses.PaintedAnywhere.Contains(name)
                ? null
                : uses.PaintedBounds.TryGetValue(name, out List<RectangleF>? bounds) ? bounds : new List<RectangleF>();
            PdfStream? redacted = RedactPatternCell(
                cell, contexts, painted,
                pageBox is not null && uses.Shown.TryGetValue(name, out List<RectangleF>? shown) ? shown : null,
                pageBox ?? default, run, allObjects, ref nextObjectNum, candidates, depth);
            if (redacted is null)
            {
                continue;
            }

            PdfObjectId id = new PdfObjectId(nextObjectNum++, 0);
            allObjects.Add(new PdfIndirectObject(id, redacted));
            newPatterns ??= CopyDictionary(patterns);
            newPatterns.Set(key, new PdfReference(id));
            if (entry is PdfReference original)
            {
                candidates.Add(original.ObjectId.ObjectNumber);
            }
        }

        if (newPatterns is not null)
        {
            PdfDictionary resources = CopyDictionary(current);
            resources.Set(PdfName.Intern("Pattern"), newPatterns);
            container.Set(PdfName.Resources, resources);
        }
    }

    // Rewrites one tiling-pattern cell against the boxes, or returns null
    // when no tile copy reaches a box or the cell is provably shown whole
    // elsewhere.
    private static PdfStream? RedactPatternCell(
        PdfStream cell, IReadOnlyList<RedactContext> contexts, List<RectangleF>? painted, List<RectangleF>? shown,
        RectangleF pageBox, RedactRun run,
        List<PdfIndirectObject> allObjects, ref int nextObjectNum, HashSet<int> candidates, int depth)
    {
        PdfDictionary dict = cell.Dictionary;
        if (run.Store.Resolve(dict.GetAs<PdfPrimitive>(PdfName.Intern("BBox")) ?? PdfNull.Value) is not PdfArray bbox
            || bbox.Count < 4)
        {
            return null;
        }

        double cx0 = Math.Min(NumberAt(run, bbox, 0), NumberAt(run, bbox, 2));
        double cx1 = Math.Max(NumberAt(run, bbox, 0), NumberAt(run, bbox, 2));
        double cy0 = Math.Min(NumberAt(run, bbox, 1), NumberAt(run, bbox, 3));
        double cy1 = Math.Max(NumberAt(run, bbox, 1), NumberAt(run, bbox, 3));
        double xs = Math.Abs(NumberOf(run, dict, "XStep"));
        double ys = Math.Abs(NumberOf(run, dict, "YStep"));
        if (xs < 1e-9)
        {
            xs = cx1 - cx0;
        }

        if (ys < 1e-9)
        {
            ys = cy1 - cy0;
        }

        if (xs < 1e-9 || ys < 1e-9)
        {
            return null;
        }

        Transform matrix = Transform.Identity;
        if (run.Store.Resolve(dict.GetAs<PdfPrimitive>(PdfName.Intern("Matrix")) ?? PdfNull.Value) is PdfArray m
            && m.Count >= 6)
        {
            matrix = new Transform(
                NumberAt(run, m, 0), NumberAt(run, m, 1), NumberAt(run, m, 2),
                NumberAt(run, m, 3), NumberAt(run, m, 4), NumberAt(run, m, 5));
        }

        // Tile copies reaching a box, as redaction contexts in cell space.
        List<RedactContext> cellContexts = new List<RedactContext>();
        bool tooMany = false;
        foreach (RedactContext context in contexts)
        {
            if (context.Rects.Count == 0)
            {
                continue;
            }

            Transform toPage = matrix.Multiply(context.BaseCtm);
            Transform toCell;
            try
            {
                toCell = toPage.Invert();
            }
            catch (InvalidOperationException)
            {
                tooMany = true;
                continue;
            }

            foreach (RectangleF r in PaintedPartsOfBoxes(context, painted))
            {
                (double bx0, double by0, double bx1, double by1) = MappedBounds(toCell, r.X, r.Y, r.X + r.Width, r.Y + r.Height);
                double iMin = Math.Floor((bx0 - cx1) / xs);
                double iMax = Math.Ceiling((bx1 - cx0) / xs);
                double jMin = Math.Floor((by0 - cy1) / ys);
                double jMax = Math.Ceiling((by1 - cy0) / ys);
                if ((iMax - iMin + 1) * (jMax - jMin + 1) > MaxPatternTileCopies)
                {
                    tooMany = true;
                    continue;
                }

                for (long i = (long)iMin; i <= (long)iMax; i++)
                {
                    for (long j = (long)jMin; j <= (long)jMax; j++)
                    {
                        Transform copy = Transform.CreateTranslation(i * xs, j * ys).Multiply(toPage);
                        (double qx0, double qy0, double qx1, double qy1) = MappedBounds(copy, cx0, cy0, cx1, cy1);
                        if (qx0 < r.X + r.Width && qx1 > r.X && qy0 < r.Y + r.Height && qy1 > r.Y)
                        {
                            cellContexts.Add(new RedactContext(copy, new List<RectangleF> { r }, new List<string?>()));
                        }
                    }
                }
            }
        }

        if (cellContexts.Count == 0 && !tooMany)
        {
            return null;
        }

        if (!tooMany && ShownWholeElsewhere(contexts, shown, pageBox, matrix, cx0, cy0, cx1, cy1, xs, ys))
        {
            return null;
        }

        byte[] content = Array.Empty<byte>();
        PdfDictionary? cellResources = run.Store.Resolve(dict.GetAs<PdfPrimitive>(PdfName.Resources) ?? PdfNull.Value) as PdfDictionary;
        List<BlankedImage> images = new List<BlankedImage>();
        PatternUses nested = new PatternUses();
        if (!tooMany && depth < MaxPatternDepth)
        {
            byte[] original;
            try
            {
                original = DecodeStream(cell, run.Pipeline);
            }
            catch (Exception)
            {
                original = Array.Empty<byte>(); // undecodable cell: emptied (B15)
            }

            content = RewriteContent(
                original, cellContexts, run, cellResources, Array.Empty<PatternRule>(),
                cellResources is not null ? images : null, nested).Bytes;
        }

        PdfDictionary newDict = new PdfDictionary();
        foreach (PdfName key in dict.Keys)
        {
            if (key.Value is not ("Filter" or "DecodeParms" or "Length" or "PieceInfo"))
            {
                newDict.Set(key, dict[key]);
            }
        }

        newDict.Set(PdfName.Length, content.Length);
        PruneUnusedImages(newDict, cellResources, content, run, candidates);
        AddBlankedImages(newDict, cellResources, images, run, allObjects, ref nextObjectNum);
        RedactPatternCells(newDict, cellResources, cellContexts, nested, run, allObjects, ref nextObjectNum, candidates, depth + 1, null);
        return new PdfStream(newDict, content);
    }

    // The parts of a context's boxes (page space) where the pattern was
    // painted: each box clipped to each painted fill's bounds, or the boxes
    // themselves when the painted extent is not modelled (null).
    private static List<RectangleF> PaintedPartsOfBoxes(RedactContext context, List<RectangleF>? painted)
    {
        if (painted is null)
        {
            return context.Rects;
        }

        List<RectangleF> parts = new List<RectangleF>();
        foreach (RectangleF local in painted)
        {
            (double px0, double py0, double px1, double py1) = MappedBounds(
                context.BaseCtm, local.X, local.Y, local.X + local.Width, local.Y + local.Height);
            RectangleF paintedOnPage = new RectangleF(px0, py0, px1 - px0, py1 - py0);
            foreach (RectangleF r in context.Rects)
            {
                RectangleF part = Intersect(r, paintedOnPage);
                if (part.Width > 0 && part.Height > 0)
                {
                    parts.Add(part);
                }
            }
        }

        return parts;
    }

    // True when some tile copy lies wholly inside a rectangle where the
    // pattern fill is known to show (on the page, with no graphics state,
    // layer or text clip that could hide it) and clear of every box: the
    // whole cell is then visible on the page, so its content holds nothing
    // the boxes hide that is not shown elsewhere. Page content only.
    private static bool ShownWholeElsewhere(
        IReadOnlyList<RedactContext> contexts, List<RectangleF>? shown, RectangleF pageBox, Transform matrix,
        double cx0, double cy0, double cx1, double cy1, double xs, double ys)
    {
        if (shown is null || shown.Count == 0)
        {
            return false;
        }

        foreach (RedactContext context in contexts)
        {
            Transform placement = context.BaseCtm;
            if (Math.Abs(placement.B) > 1e-9 || Math.Abs(placement.C) > 1e-9)
            {
                continue;
            }

            Transform toPage = matrix.Multiply(placement);
            Transform toCell;
            try
            {
                toCell = toPage.Invert();
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            foreach (RectangleF local in shown)
            {
                (double vx0, double vy0, double vx1, double vy1) = MappedBounds(
                    placement, local.X, local.Y, local.X + local.Width, local.Y + local.Height);
                (double px0, double py0, double px1, double py1) = MappedBounds(toCell, vx0, vy0, vx1, vy1);
                double iMin = Math.Floor((px0 - cx0) / xs);
                double iMax = Math.Ceiling((px1 - cx0) / xs);
                double jMin = Math.Floor((py0 - cy0) / ys);
                double jMax = Math.Ceiling((py1 - cy0) / ys);
                if ((iMax - iMin + 1) * (jMax - jMin + 1) > MaxPatternTileCopies)
                {
                    continue;
                }

                for (long i = (long)iMin; i <= (long)iMax; i++)
                {
                    for (long j = (long)jMin; j <= (long)jMax; j++)
                    {
                        Transform copy = Transform.CreateTranslation(i * xs, j * ys).Multiply(toPage);
                        (double qx0, double qy0, double qx1, double qy1) = MappedBounds(copy, cx0, cy0, cx1, cy1);
                        if (qx0 < vx0 || qy0 < vy0 || qx1 > vx1 || qy1 > vy1
                            || qx0 < pageBox.X || qy0 < pageBox.Y || qx1 > pageBox.Right || qy1 > pageBox.Top)
                        {
                            continue;
                        }

                        bool clear = true;
                        foreach (RedactContext other in contexts)
                        {
                            foreach (RectangleF r in other.Rects)
                            {
                                if (qx0 < r.X + r.Width && qx1 > r.X && qy0 < r.Y + r.Height && qy1 > r.Y)
                                {
                                    clear = false;
                                }
                            }
                        }

                        if (clear)
                        {
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    private static (double X0, double Y0, double X1, double Y1) MappedBounds(
        Transform m, double x0, double y0, double x1, double y1)
    {
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        foreach (PointF corner in new[]
        {
            new PointF(x0, y0), new PointF(x1, y0), new PointF(x1, y1), new PointF(x0, y1),
        })
        {
            PointF p = m.TransformPoint(corner);
            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X);
            maxY = Math.Max(maxY, p.Y);
        }

        return (minX, minY, maxX, maxY);
    }

    private static double NumberOf(RedactRun run, PdfDictionary dict, string key)
    {
        return dict.TryGetValue(PdfName.Intern(key), out PdfPrimitive? v) ? NumberValue(run.Store.Resolve(v)) : 0.0;
    }

    private static double NumberAt(RedactRun run, PdfArray array, int index) =>
        NumberValue(run.Store.Resolve(array[index]));

    private static double NumberValue(PdfPrimitive p) => p switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0.0,
    };

    // A resource name for a blanked image that no existing XObject uses.
    private static string NewBlankedImageName(
        List<BlankedImage> taken, PdfDictionary? resources, RedactRun run)
    {
        PdfDictionary? xobjects = null;
        if (resources is not null && resources.TryGetValue(PdfName.XObject, out PdfPrimitive? xv))
        {
            lock (run.Sync)
            {
                xobjects = run.Store.Resolve(xv) as PdfDictionary;
            }
        }

        HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);
        foreach (BlankedImage image in taken)
        {
            used.Add(image.Name);
        }

        int n = taken.Count;
        while (true)
        {
            string candidate = "CvRedIm" + n.ToString(CultureInfo.InvariantCulture);
            if (!used.Contains(candidate) && (xobjects is null || !xobjects.ContainsKey(PdfName.Intern(candidate))))
            {
                return candidate;
            }

            n++;
        }
    }

    // Adds a content stream's blanked images to its container's resources
    // (copied, never edited in place), giving each image and its masks new
    // object numbers in a stable order.
    private static void AddBlankedImages(
        PdfDictionary container, PdfDictionary? baseResources, List<BlankedImage> images,
        RedactRun run, List<PdfIndirectObject> allObjects, ref int nextObjectNum)
    {
        if (images.Count == 0)
        {
            return;
        }

        PdfDictionary? current = container.TryGetValue(PdfName.Resources, out PdfPrimitive? rv)
            ? run.Store.Resolve(rv) as PdfDictionary
            : baseResources;
        PdfDictionary resources = current is null ? new PdfDictionary() : CopyDictionary(current);
        PdfDictionary xobjects = resources.TryGetValue(PdfName.XObject, out PdfPrimitive? xv)
            && run.Store.Resolve(xv) is PdfDictionary existing
                ? CopyDictionary(existing)
                : new PdfDictionary();

        foreach (BlankedImage image in images)
        {
            List<(PdfReference Placeholder, PdfReference Actual)> assigned =
                new List<(PdfReference Placeholder, PdfReference Actual)>();
            foreach ((PdfReference placeholder, PdfStream mask) in image.Masks)
            {
                PdfObjectId maskId = new PdfObjectId(nextObjectNum++, 0);
                assigned.Add((placeholder, new PdfReference(maskId)));
                allObjects.Add(new PdfIndirectObject(maskId, mask));
            }

            ReplacePlaceholders(image.Image.Dictionary, assigned);
            foreach ((PdfReference _, PdfStream mask) in image.Masks)
            {
                ReplacePlaceholders(mask.Dictionary, assigned);
            }

            PdfObjectId imageId = new PdfObjectId(nextObjectNum++, 0);
            allObjects.Add(new PdfIndirectObject(imageId, image.Image));
            xobjects.Set(PdfName.Intern(image.Name), new PdfReference(imageId));
        }

        resources.Set(PdfName.XObject, xobjects);
        container.Set(PdfName.Resources, resources);
    }

    private static void ReplacePlaceholders(
        PdfDictionary dict, List<(PdfReference Placeholder, PdfReference Actual)> assigned)
    {
        List<PdfName> keys = new List<PdfName>(dict.Keys);
        foreach (PdfName key in keys)
        {
            if (dict[key] is not PdfReference value)
            {
                continue;
            }

            foreach ((PdfReference placeholder, PdfReference actual) in assigned)
            {
                if (ReferenceEquals(value, placeholder))
                {
                    dict.Set(key, actual);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Consumes the inline image's dictionary tokens (the tokenizer sits just
    /// after <c>BI</c>) through <c>ID</c>, then returns the offset just past
    /// the terminating <c>EI</c>, or <c>-1</c> when none exists. For an
    /// unfiltered image the data length is computed exactly from
    /// <c>/W /H /BPC /CS</c> (<c>/ImageMask</c>), so false <c>EI</c> pairs
    /// inside the binary data cannot end the scan early; filtered or
    /// underspecified images fall back to the whitespace-delimited scan.
    /// </summary>
    private static int FindInlineImageEnd(PdfTokenizer tok, byte[] content)
    {
        int width = 0;
        int height = 0;
        int bpc = 8;
        int components = -1;
        bool filtered = false;
        bool imageMask = false;
        string? pendingKey = null;

        while (true)
        {
            PdfToken t = tok.Read();
            if (t.IsEndOfStream)
            {
                return -1;
            }

            if (t.Type == PdfTokenType.Keyword && t.RawText == "ID")
            {
                break;
            }

            if (t.Type == PdfTokenType.ArrayStart)
            {
                if (pendingKey is "F" or "Filter")
                {
                    filtered = true;
                }

                while (true)
                {
                    PdfToken inner = tok.Read();
                    if (inner.IsEndOfStream)
                    {
                        return -1;
                    }

                    if (inner.Type == PdfTokenType.ArrayEnd)
                    {
                        break;
                    }
                }

                pendingKey = null;
                continue;
            }

            if (t.Type == PdfTokenType.Name)
            {
                string name = t.RawText.TrimStart('/');
                if (pendingKey is null)
                {
                    pendingKey = name;
                    continue;
                }

                switch (pendingKey)
                {
                    case "CS":
                    case "ColorSpace":
                        components = name switch
                        {
                            "G" or "DeviceGray" or "CalGray" => 1,
                            "RGB" or "DeviceRGB" or "CalRGB" => 3,
                            "CMYK" or "DeviceCMYK" => 4,
                            "I" or "Indexed" => 1,
                            _ => -1,
                        };
                        break;
                    case "F":
                    case "Filter":
                        filtered = true;
                        break;
                    default:
                        break;
                }

                pendingKey = null;
                continue;
            }

            if (pendingKey is not null)
            {
                if (t.IsNumeric)
                {
                    double value = ParseDouble(t);
                    switch (pendingKey)
                    {
                        case "W" or "Width":
                            width = (int)value;
                            break;
                        case "H" or "Height":
                            height = (int)value;
                            break;
                        case "BPC" or "BitsPerComponent":
                            bpc = (int)value;
                            break;
                        default:
                            break;
                    }
                }
                else if (t.Type == PdfTokenType.Keyword && t.RawText == "true"
                    && pendingKey is "IM" or "ImageMask")
                {
                    imageMask = true;
                }

                pendingKey = null;
            }
        }

        // Data begins after the single whitespace byte following ID (§8.9.7).
        int dataStart = (int)tok.Position;
        if (dataStart < content.Length && IsWhitespaceByte(content[dataStart]))
        {
            dataStart++;
        }

        if (imageMask)
        {
            components = 1;
            bpc = 1;
        }

        if (!filtered && width > 0 && height > 0 && components > 0 && bpc > 0)
        {
            long rowBytes = (((long)width * components * bpc) + 7) / 8;
            long afterData = dataStart + (rowBytes * height);
            if (afterData <= content.Length)
            {
                long k = afterData;
                while (k < content.Length && IsWhitespaceByte(content[k]))
                {
                    k++;
                }

                if (k + 1 < content.Length
                    && content[k] == (byte)'E' && content[k + 1] == (byte)'I')
                {
                    return (int)k + 2;
                }
            }
        }

        for (int i = dataStart; i + 1 < content.Length; i++)
        {
            if (content[i] == (byte)'E' && content[i + 1] == (byte)'I'
                && (i == 0 || IsWhitespaceByte(content[i - 1]))
                && (i + 2 >= content.Length || IsWhitespaceByte(content[i + 2])))
            {
                return i + 2;
            }
        }

        return -1;
    }

    private static bool IsWhitespaceByte(byte b) =>
        b == 0x00 || b == 0x09 || b == 0x0A || b == 0x0C || b == 0x0D || b == 0x20;

    private static bool IsPathConstructionOp(string op)
    {
        return op == "m" || op == "l" || op == "c" || op == "v"
            || op == "y" || op == "re" || op == "h";
    }

    // Tracks the current fill/stroke pattern names and an axis-aligned clip
    // rectangle (q/Q saved), and records into uses which patterns are
    // painted and where a pattern fill certainly shows: a rectangle fill
    // under an axis-aligned CTM, intersected with a rectangular clip (an
    // unknown clip shape records nothing). Runs before the operator itself
    // is handled, while the path being painted is still buffered.
    private static void TrackPatternPaint(
        string op, List<PdfToken> operands, RedactState state, PathAccumulator path, PatternUses? uses,
        RedactRun run, PdfDictionary? resources)
    {
        if (uses is null)
        {
            return;
        }

        // Anything painted later over a rectangle where a pattern was seen
        // to show may hide it: such rectangles stop counting as shown.
        NoteOverpaint(op, operands, state, path, uses, run, resources);

        string? lastName = operands.Count > 0 && operands[^1].Type == PdfTokenType.Name
            ? DecodeNameText(operands[^1].RawText)
            : null;
        // A transform (cm, or q/Q restoring another CTM) inside a path
        // object (invalid, but viewers apply the CTM in force at the paint
        // operator) makes the buffered bounds unreliable.
        if ((op == "cm" || op == "q" || op == "Q") && path.Active)
        {
            state.PathCtmChanged = true;
        }

        switch (op)
        {
            case "q":
                state.PatternStack.Push((state.FillPattern, state.StrokePattern, state.ClipRect, state.ClipUnknown, state.MayHidePaint));
                return;
            case "Q":
                if (state.PatternStack.Count > 0)
                {
                    (state.FillPattern, state.StrokePattern, state.ClipRect, state.ClipUnknown, state.MayHidePaint) = state.PatternStack.Pop();
                }

                return;
            case "gs":
                // Opacity, soft masks and blend modes can hide a fill.
                state.MayHidePaint = true;
                return;
            case "Tr":
                if (operands.Count > 0 && ParseDouble(operands[^1]) >= 4)
                {
                    state.ClipUnknown = true; // text clipping
                }

                return;
            case "BDC" or "BMC":
                state.MarkedScopes.Push(op == "BDC" && operands.Count > 0 && operands[0].RawText.TrimStart('/') == "OC");
                return;
            case "EMC":
                if (state.MarkedScopes.Count > 0)
                {
                    state.MarkedScopes.Pop();
                }

                return;
            case "scn":
                state.FillPattern = lastName;
                return;
            case "SCN":
                state.StrokePattern = lastName;
                return;
            case "cs" or "sc" or "g" or "rg" or "k":
                state.FillPattern = null;
                return;
            case "CS" or "SC" or "G" or "RG" or "K":
                state.StrokePattern = null;
                return;
            case "Tj" or "TJ" or "'" or "\"" or "Do" or "BI":
                AddPainted(uses, state.FillPattern, anywhere: true);
                AddPainted(uses, state.StrokePattern, anywhere: true);
                return;
        }

        if (!IsPathPaintOp(op))
        {
            return;
        }

        bool fills = op is "f" or "F" or "f*" or "B" or "B*" or "b" or "b*";
        bool strokes = op is "S" or "s" or "B" or "B*" or "b" or "b*";
        if (strokes)
        {
            AddPainted(uses, state.StrokePattern, anywhere: true);
        }

        bool movedPath = state.PathCtmChanged;
        state.PathCtmChanged = false;
        if (fills && state.FillPattern is string fillName)
        {
            if (path.Active && !movedPath)
            {
                AddPainted(uses, fillName, anywhere: false);
                if (!uses.PaintedBounds.TryGetValue(fillName, out List<RectangleF>? bounds))
                {
                    bounds = new List<RectangleF>();
                    uses.PaintedBounds[fillName] = bounds;
                }

                bounds.Add(new RectangleF(path.MinX, path.MinY, path.MaxX - path.MinX, path.MaxY - path.MinY));
            }
            else
            {
                AddPainted(uses, fillName, anywhere: true);
            }

            if (!movedPath && !path.HasClip && !state.ClipUnknown && !state.MayHidePaint
                && !state.MarkedScopes.Contains(true) && SingleRectangle(path, state) is RectangleF filled)
            {
                RectangleF shown = filled;
                if (state.ClipRect is RectangleF clip)
                {
                    shown = Intersect(shown, clip);
                }

                if (shown.Width > 0 && shown.Height > 0)
                {
                    if (!uses.Shown.TryGetValue(fillName, out List<RectangleF>? list))
                    {
                        list = new List<RectangleF>();
                        uses.Shown[fillName] = list;
                    }

                    list.Add(shown);
                }
            }
        }

        // A clip set with this path takes effect after it is painted.
        if (path.HasClip)
        {
            if (SingleRectangle(path, state) is RectangleF clipRect)
            {
                state.ClipRect = state.ClipRect is RectangleF current ? Intersect(current, clipRect) : clipRect;
            }
            else
            {
                state.ClipUnknown = true;
            }
        }
    }

    // Withdraws "shown" pattern rectangles that a painting operator may
    // cover: a fill or stroke over its path bounds (a fill with the same
    // pattern does not hide that pattern), text over a generous box around
    // the shown string, an image over its unit square; a form, a shading,
    // or anything whose extent is unknown withdraws them all.
    private static void NoteOverpaint(
        string op, List<PdfToken> operands, RedactState state, PathAccumulator path, PatternUses uses,
        RedactRun run, PdfDictionary? resources)
    {
        if (uses.Shown.Count == 0)
        {
            return;
        }

        switch (op)
        {
            case "f" or "F" or "f*" or "B" or "B*" or "b" or "b*" or "S" or "s":
                if (!path.Active)
                {
                    return;
                }

                bool fills = op is "f" or "F" or "f*" or "B" or "B*" or "b" or "b*";
                bool strokes = op is "S" or "s" or "B" or "B*" or "b" or "b*";
                if (state.PathCtmChanged)
                {
                    WithdrawShown(uses, null, null);
                    return;
                }

                double reach = 0;
                if (strokes)
                {
                    Transform ctm = state.Ctm;
                    double scale = Math.Max(
                        Math.Max(Math.Abs(ctm.A), Math.Abs(ctm.B)),
                        Math.Max(Math.Abs(ctm.C), Math.Abs(ctm.D)));
                    reach = Math.Max(state.LineWidth, 1.0) * scale * Math.Max(Math.Sqrt(2), state.MiterLimit) / 2;
                }

                RectangleF painted = new RectangleF(
                    path.MinX - reach, path.MinY - reach,
                    path.MaxX - path.MinX + (2 * reach), path.MaxY - path.MinY + (2 * reach));
                // A fill with the same pattern repaints the same picture —
                // unless a graphics state (blend mode, opacity) may alter it.
                bool samePatternFill = fills && !strokes && state.FillPattern is not null && !state.MayHidePaint;
                WithdrawShown(uses, painted, samePatternFill ? state.FillPattern : null);
                return;
            case "Tj" or "TJ":
                WithdrawShown(uses, state.FontExtentUnknown ? null : TextPaintBounds(operands, state), null);
                return;
            case "'" or "\"" or "sh":
                WithdrawShown(uses, null, null);
                return;
            case "BI":
                WithdrawShown(uses, UnitSquareBounds(state.Ctm), null);
                return;
            case "Do":
                bool image = ResolveXObjectRef(operands, resources, run.Store) is { } placed && IsImage(placed.Stream);
                WithdrawShown(uses, image ? UnitSquareBounds(state.Ctm) : null, null);
                return;
        }
    }

    // Removes shown rectangles meeting bounds (all of them when bounds is
    // null), except those of the named pattern.
    private static void WithdrawShown(PatternUses uses, RectangleF? bounds, string? except)
    {
        foreach (KeyValuePair<string, List<RectangleF>> entry in uses.Shown)
        {
            if (entry.Key == except)
            {
                continue;
            }

            if (bounds is not RectangleF b)
            {
                entry.Value.Clear();
                continue;
            }

            entry.Value.RemoveAll(r =>
                r.X < b.X + b.Width && b.X < r.X + r.Width && r.Y < b.Y + b.Height && b.Y < r.Y + r.Height);
        }
    }

    // The /Rect of every annotation on a page (normalised).
    private static List<RectangleF> AnnotationRects(PdfDictionary page, PdfObjectStore store)
    {
        List<RectangleF> rects = new List<RectangleF>();
        if (!page.TryGetValue(PdfName.Intern("Annots"), out PdfPrimitive? av) || store.Resolve(av) is not PdfArray annots)
        {
            return rects;
        }

        foreach (PdfPrimitive item in annots)
        {
            if (store.Resolve(item) is not PdfDictionary annot
                || !annot.TryGetValue(PdfName.Intern("Rect"), out PdfPrimitive? rv)
                || store.Resolve(rv) is not PdfArray r
                || r.Count < 4)
            {
                continue;
            }

            double ax = PdfReal.ToDouble(store.Resolve(r[0]));
            double ay = PdfReal.ToDouble(store.Resolve(r[1]));
            double bx = PdfReal.ToDouble(store.Resolve(r[2]));
            double by = PdfReal.ToDouble(store.Resolve(r[3]));
            rects.Add(new RectangleF(Math.Min(ax, bx), Math.Min(ay, by), Math.Abs(bx - ax), Math.Abs(by - ay)));
        }

        return rects;
    }

    private static RectangleF UnitSquareBounds(Transform ctm)
    {
        PointF a = ctm.TransformPoint(new PointF(0, 0));
        PointF b = ctm.TransformPoint(new PointF(1, 0));
        PointF c = ctm.TransformPoint(new PointF(1, 1));
        PointF d = ctm.TransformPoint(new PointF(0, 1));
        double minX = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X));
        double minY = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y));
        double maxX = Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X));
        double maxY = Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y));
        return new RectangleF(minX, minY, maxX - minX, maxY - minY);
    }

    // A generous box around a Tj/TJ string: every byte counted as a glyph
    // up to two ems wide plus character and word spacing, TJ adjustments
    // added in full, from a font size below the baseline to two above it
    // (with rise), in text space at the current line position.
    private static RectangleF TextPaintBounds(List<PdfToken> operands, RedactState state)
    {
        double size = Math.Abs(state.FontSize);
        double perGlyph = (2 * size) + Math.Abs(state.CharSpacing) + Math.Abs(state.WordSpacing);
        double width = 0;
        foreach (PdfToken t in operands)
        {
            if (t.Type is PdfTokenType.LiteralString or PdfTokenType.HexString)
            {
                width += t.RawText.Length * perGlyph;
            }
            else if (t.IsNumeric)
            {
                width += Math.Abs(ParseDouble(t)) / 1000 * size;
            }
        }

        width *= Math.Max(Math.Abs(state.HorizontalScaling), 1.0);
        double x0 = state.TextX - width;
        double x1 = state.TextX + width;
        double y0 = state.TextRise - size - Math.Abs(state.TextRise);
        double y1 = state.TextRise + (2 * size) + Math.Abs(state.TextRise);
        Transform m = state.TextMatrix.Multiply(state.Ctm);
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        foreach (PointF corner in new[] { new PointF(x0, y0), new PointF(x1, y0), new PointF(x1, y1), new PointF(x0, y1) })
        {
            PointF p = m.TransformPoint(corner);
            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X);
            maxY = Math.Max(maxY, p.Y);
        }

        return new RectangleF(minX, minY, maxX - minX, maxY - minY);
    }

    private static void AddPainted(PatternUses uses, string? name, bool anywhere)
    {
        if (name is not null)
        {
            uses.Painted.Add(name);
            if (anywhere)
            {
                uses.PaintedAnywhere.Add(name);
            }
        }
    }

    private static RectangleF Intersect(RectangleF a, RectangleF b)
    {
        double x0 = Math.Max(a.X, b.X);
        double y0 = Math.Max(a.Y, b.Y);
        double x1 = Math.Min(a.X + a.Width, b.X + b.Width);
        double y1 = Math.Min(a.Y + a.Height, b.Y + b.Height);
        return x1 > x0 && y1 > y0 ? new RectangleF(x0, y0, x1 - x0, y1 - y0) : new RectangleF(x0, y0, 0, 0);
    }

    // The buffered path's single "re" rectangle in local (CTM-mapped) space
    // when the CTM keeps it axis-aligned; null for any other path.
    private static RectangleF? SingleRectangle(PathAccumulator path, RedactState state)
    {
        List<PdfToken>? rect = null;
        foreach ((List<PdfToken> operands, string op) in path.Buffer)
        {
            if (op is "W" or "W*")
            {
                continue;
            }

            if (op != "re" || rect is not null || operands.Count < 4)
            {
                return null;
            }

            rect = operands;
        }

        Transform ctm = state.Ctm;
        if (rect is null || Math.Abs(ctm.B) > 1e-9 || Math.Abs(ctm.C) > 1e-9)
        {
            return null;
        }

        double x = ParseDouble(rect[^4]);
        double y = ParseDouble(rect[^3]);
        double w = ParseDouble(rect[^2]);
        double h = ParseDouble(rect[^1]);
        PointF a = ctm.TransformPoint(new PointF(x, y));
        PointF b = ctm.TransformPoint(new PointF(x + w, y + h));
        double minX = Math.Min(a.X, b.X);
        double minY = Math.Min(a.Y, b.Y);
        return new RectangleF(minX, minY, Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
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
        // Last-n operands throughout: a viewer consumes the TOP of the
        // operand stack, so crafted extra operands must not shift the box.
        switch (op)
        {
            case "m":
            case "l":
                if (operands.Count >= 2)
                {
                    path.AddPoint(ParseDouble(operands[^2]), ParseDouble(operands[^1]), state);
                }
                break;

            case "c":
                if (operands.Count >= 6)
                {
                    path.AddPoint(ParseDouble(operands[^6]), ParseDouble(operands[^5]), state);
                    path.AddPoint(ParseDouble(operands[^4]), ParseDouble(operands[^3]), state);
                    path.AddPoint(ParseDouble(operands[^2]), ParseDouble(operands[^1]), state);
                }
                break;

            case "v":
            case "y":
                if (operands.Count >= 4)
                {
                    path.AddPoint(ParseDouble(operands[^4]), ParseDouble(operands[^3]), state);
                    path.AddPoint(ParseDouble(operands[^2]), ParseDouble(operands[^1]), state);
                }
                break;

            case "re":
                if (operands.Count >= 4)
                {
                    double x = ParseDouble(operands[^4]);
                    double y = ParseDouble(operands[^3]);
                    double w = ParseDouble(operands[^2]);
                    double h = ParseDouble(operands[^1]);
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

        // Last-6 operands: viewers consume the top of the operand stack, so
        // crafted extra operands must not shift the modelled matrix.
        int b = operands.Count - 6;
        Transform ctm = new Transform(
            ParseDouble(operands[b]), ParseDouble(operands[b + 1]),
            ParseDouble(operands[b + 2]), ParseDouble(operands[b + 3]),
            ParseDouble(operands[b + 4]), ParseDouble(operands[b + 5]));
        state.Ctm = ctm.Multiply(state.Ctm);
    }

    private static void ApplyTf(
        List<PdfToken> operands, RedactState state, PdfDictionary? resources, PdfObjectStore store)
    {
        if (operands.Count >= 2)
        {
            state.FontSize = ParseDouble(operands[^1]);
            state.FontExtentUnknown = !IsKnownExtentFont(operands[^2], resources, store);
            state.GlyphWidths = BuildGlyphWidths(operands[^2], resources, store);
            state.Font = BuildFont(operands[^2], resources, store, state.FontCache);
        }
    }

    // True when the named font resolves to a font whose glyphs stay within
    // about an em of the text position (not a Type 3 font, whose glyph
    // procedures can draw anywhere their /FontMatrix sends them).
    private static bool IsKnownExtentFont(PdfToken nameToken, PdfDictionary? resources, PdfObjectStore store)
    {
        if (resources is null
            || nameToken.Type != PdfTokenType.Name
            || !resources.TryGetValue(PdfName.Intern("Font"), out PdfPrimitive? fv)
            || store.Resolve(fv) is not PdfDictionary fonts
            || !fonts.TryGetValue(PdfName.Intern(DecodeNameText(nameToken.RawText)), out PdfPrimitive? entry)
            || store.Resolve(entry) is not PdfDictionary font)
        {
            return false;
        }

        return !(font.TryGetValue(PdfName.Subtype, out PdfPrimitive? st) && st is PdfName subtype && subtype.Value == "Type3");
    }

    // Applies the stroke-relevant entries of a named ExtGState (PDF 8.4.5):
    // /LW (line width) and /ML (miter limit). When the named state cannot be
    // resolved the stroke geometry becomes unknown, so the precise vector
    // subtractor stops keeping stroked geometry (StrokeStateReliable = false)
    // and falls back to whole-path dropping for strokes.
    private static void ApplyGs(
        List<PdfToken> operands, RedactState state, PdfDictionary? resources, PdfObjectStore store)
    {
        if (operands.Count < 1 || operands[0].Type != PdfTokenType.Name)
        {
            state.StrokeStateReliable = false;
            return;
        }

        string gsName = operands[0].RawText.TrimStart('/');
        if (resources is null
            || !resources.TryGetValue(PdfName.Intern("ExtGState"), out PdfPrimitive? extValue)
            || store.Resolve(extValue) is not PdfDictionary states
            || !states.TryGetValue(PdfName.Intern(gsName), out PdfPrimitive? entry)
            || store.Resolve(entry) is not PdfDictionary gsDict)
        {
            state.StrokeStateReliable = false;
            return;
        }

        if (gsDict.TryGetValue(PdfName.Intern("LW"), out PdfPrimitive? lw))
        {
            state.LineWidth = PdfReal.ToDouble(store.Resolve(lw));
        }

        if (gsDict.TryGetValue(PdfName.Intern("ML"), out PdfPrimitive? ml))
        {
            state.MiterLimit = PdfReal.ToDouble(store.Resolve(ml));
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
            double tx = ParseDouble(operands[^2]);
            double ty = ParseDouble(operands[^1]);

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
            double ty = ParseDouble(operands[^1]);
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
        int b = operands.Count - 6;
        state.TextMatrix = new Transform(
            ParseDouble(operands[b]), ParseDouble(operands[b + 1]),
            ParseDouble(operands[b + 2]), ParseDouble(operands[b + 3]),
            ParseDouble(operands[b + 4]), ParseDouble(operands[b + 5]));
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
        double spacing = 0;
        foreach (char ch in text)
        {
            spacing += state.CharSpacing + (ch == 32 ? state.WordSpacing : 0.0);
        }

        if (state.GlyphWidths is int[] glyphWidths)
        {
            double total = 0;
            foreach (char ch in text)
            {
                int code = ch;
                int w = (code >= 0 && code < 256) ? glyphWidths[code] : 0;
                total += w > 0 ? w : 500;
            }

            return ((total / 1000.0 * state.FontSize) + spacing) * state.HorizontalScaling;
        }

        return ((text.Length * state.FontSize * 0.6) + spacing) * state.HorizontalScaling;
    }

    // A glyph's Tc/Tw spacing expressed in TJ units (thousandths of text
    // space at the current font size), so a dropped glyph's gap reproduces
    // its full advance — not only its width — and survivors keep their place.
    private static double SpacingIn1000(char glyph, RedactState state)
    {
        double spacing = state.CharSpacing + (glyph == 32 ? state.WordSpacing : 0.0);
        return spacing * 1000.0 / state.FontSize;
    }

    // Emits a Tj operator with only the out-of-region glyphs retained. When no
    // glyph is in a region the original Tj is re-emitted; when all are, nothing
    // is emitted; otherwise a TJ array is written whose dropped runs become
    // negative numeric gaps equal to the removed glyphs' advance, so survivors
    // keep their original positions.
    private static bool EmitRedactedText(
        MemoryStream output, List<PdfToken> operands, RedactState state,
        IReadOnlyList<RedactContext> contexts)
    {
        if (operands.Count == 0)
        {
            WriteOriginalTj(output, operands);
            return false;
        }

        PdfToken stringToken = operands[operands.Count - 1];
        if (stringToken.Type != PdfTokenType.LiteralString
            && stringToken.Type != PdfTokenType.HexString)
        {
            WriteOriginalTj(output, operands);
            return false;
        }

        string text = ExtractString(stringToken);
        if (text.Length == 0 || state.FontSize <= 0)
        {
            WriteOriginalTj(output, operands);
            return false;
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
            advance[i] = width1000 + SpacingIn1000(text[i], state);
            double advUser = (width1000 / 1000.0 * state.FontSize
                + state.CharSpacing
                + (code == 32 ? state.WordSpacing : 0.0)) * state.HorizontalScaling;

            glyphX0[i] = cursor;
            glyphX1[i] = cursor + advUser;
            bool inRegion = MatchSpan(
                cursor, cursor + advUser, text[i], state, local, contexts, out string? replacement);
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
            return false;
        }

        if (allDropped && !anyReplacement)
        {
            return true;
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
        return true;
    }

    // Glyph-level redaction for a TJ array. Walks the array's strings and
    // kerning numbers, dropping only the glyphs whose span falls in a redaction
    // rectangle and preserving the original inter-glyph kerns so survivors stay
    // positioned. This is the array analogue of EmitRedactedText (single-string
    // Tj): without it, a whole TJ — typically one line of a Word/Office PDF —
    // was dropped if any glyph in it was redacted, and mid-line matches were
    // missed because the cursor was not advanced across array elements.
    private static bool EmitRedactedTJ(
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
            return false;
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
            cursor -= leadKern[i] / 1000.0 * state.FontSize * state.HorizontalScaling;

            double advUser = (widths[i] / 1000.0 * state.FontSize
                + state.CharSpacing
                + (chars[i] == 32 ? state.WordSpacing : 0.0)) * state.HorizontalScaling;
            glyphX0[i] = cursor;
            glyphX1[i] = cursor + advUser;
            bool inRegion = MatchSpan(
                cursor, cursor + advUser, (char)chars[i], state, local, contexts, out string? replacement);
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
            return false;
        }

        if (allDropped && !anyReplacement)
        {
            return true;
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
                    disp += widths[idx] + SpacingIn1000(chars[idx], state);
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
        return true;
    }

    // Adds page-space overlay rectangles covering maximal runs of glyphs removed
    // by a content pattern. Positions come from the rewrite's own glyph cursor,
    // so the boxes line up exactly with what was removed (no second layout pass).
    private static void AddPatternOverlayBoxes(
        bool[] patternDrop, double[] glyphX0, double[] glyphX1, RedactState state,
        Transform baseCtm)
    {
        Transform combined = state.TextMatrix.Multiply(state.Ctm).Multiply(baseCtm);
        double bottom = GlyphBottom(state) + state.TextRise;
        double top = GlyphTop(state) + state.TextRise;
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
            total += w + SpacingIn1000(ch, state);
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
    // Used for DRAWN OVERLAY boxes only — hit testing uses the tight per-glyph
    // ink bounds below, so a rectangle placed in the blank gap between lines
    // does not capture a neighbouring line it never touched.
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

    // ── Tight per-glyph ink bounds (hit testing) ───────────────────────────
    //
    // Deciding whether a redaction rectangle TARGETS a glyph must use the
    // glyph's actual ink extent, not an inflated line box: with the generous
    // box above, a rectangle drawn in the whitespace between two lines
    // "intersected" the line above (its phantom descent) and silently deleted
    // words the user never covered. Two vertical classes per edge:
    //
    //   top:    x-height-only lowercase (a c e g m n o p q r s u v w x z)
    //           reach ~0.53 em; everything else (caps, digits, ascenders,
    //           dotted i/j, accents, symbols) up to ~0.78 em.
    //   bottom: descender glyphs (g j p q y, low punctuation, brackets, @, Q)
    //           reach ~-0.22 em; everything else sits on the baseline with a
    //           small guard for rounded-glyph overshoot.
    //
    // Once a glyph IS hit, removal stays as over-redacting as before (B15) —
    // only the detection geometry is tight.
    private const double InkTopTallEm = 0.80;
    private const double InkTopXHeightEm = 0.60;
    private const double InkBottomDescenderEm = 0.25;
    private const double InkBottomBaselineEm = 0.04;

    private static double GlyphInkTop(RedactState state, char glyph)
    {
        bool xHeightOnly = glyph is 'a' or 'c' or 'e' or 'g' or 'm' or 'n'
            or 'o' or 'p' or 'q' or 'r' or 's' or 'u' or 'v' or 'w' or 'x'
            or 'z' or ' ' or '-';
        double em = xHeightOnly ? InkTopXHeightEm : InkTopTallEm;
        return em * state.FontSize;
    }

    private static double GlyphInkBottom(RedactState state, char glyph)
    {
        bool descender = glyph is 'g' or 'j' or 'p' or 'q' or 'y'
            or ',' or ';' or '_' or '(' or ')' or '[' or ']' or '{' or '}'
            or '@' or 'Q' or '$' or '\u00B5' or '\u00E7' or '\u00FD'
            or '\u00FF' or '\u00FE' or '\u201A' or '\u201E' or '\u00B8';
        double em = descender ? InkBottomDescenderEm : InkBottomBaselineEm;
        return -(em * state.FontSize);
    }

    private static double RunInkTop(RedactState state, string text)
    {
        double top = 0;

        for (int i = 0; i < text.Length; i++)
        {
            double glyphTop = GlyphInkTop(state, text[i]);

            if (glyphTop > top)
            {
                top = glyphTop;
            }
        }

        return top > 0 ? top : InkTopXHeightEm * state.FontSize;
    }

    private static double RunInkBottom(RedactState state, string text)
    {
        double bottom = 0;

        for (int i = 0; i < text.Length; i++)
        {
            double glyphBottom = GlyphInkBottom(state, text[i]);

            if (glyphBottom < bottom)
            {
                bottom = glyphBottom;
            }
        }

        return bottom < 0 ? bottom : -(InkBottomBaselineEm * state.FontSize);
    }

    // True when a glyph occupying [x0, x1] in text space (advance from the line
    // origin), spanning the glyph's tight ink extent, maps into any redaction
    // rectangle for some context. Outputs the in-place replacement string of
    // the first matched rectangle, if any.
    private static bool MatchSpan(
        double x0, double x1, char glyph, RedactState state, Transform local,
        IReadOnlyList<RedactContext> contexts, out string? replacement)
    {
        replacement = null;
        double bottom = GlyphInkBottom(state, glyph) + state.TextRise;
        double top = GlyphInkTop(state, glyph) + state.TextRise;
        for (int c = 0; c < contexts.Count; c++)
        {
            RedactContext context = contexts[c];
            foreach (Transform combined in TextFrames(local, state, context))
            {
                RectangleF box = TransformedBox(combined, x0, bottom, x1, top);
                for (int r = 0; r < context.Rects.Count; r++)
                {
                    if (!box.Intersect(context.Rects[r]).IsEmpty)
                    {
                        replacement = r < context.Replacements.Count ? context.Replacements[r] : null;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    // The frames a text box is tested in. Rectangles are page space, so the
    // true frame applies the content stream's own CTM (text matrix · CTM ·
    // form base). The text-matrix-only frame is also tested so callers that
    // derived rectangles from CTM-less text-fragment positions keep matching:
    // a glyph is removed when either frame hits a rectangle (over-redact when
    // in doubt), and text under a page-level cm can no longer escape.
    private static IEnumerable<Transform> TextFrames(Transform local, RedactState state, RedactContext context)
    {
        yield return local.Multiply(state.Ctm).Multiply(context.BaseCtm);
        if (!state.Ctm.Equals(Transform.Identity))
        {
            yield return local.Multiply(context.BaseCtm);
        }
    }

    // The axis-aligned page box of a text-space rectangle under a transform
    // (all four corners, so rotation and skew are covered).
    private static RectangleF TransformedBox(Transform m, double x0, double y0, double x1, double y1)
    {
        PointF a = m.TransformPoint(new PointF(x0, y0));
        PointF b = m.TransformPoint(new PointF(x1, y0));
        PointF c = m.TransformPoint(new PointF(x1, y1));
        PointF d = m.TransformPoint(new PointF(x0, y1));
        double minX = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X));
        double minY = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y));
        double maxX = Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X));
        double maxY = Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y));
        return RectangleF.FromCorners(minX, minY, maxX, maxY);
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

        double bottom = RunInkBottom(state, text) + state.TextRise;
        double top = RunInkTop(state, text) + state.TextRise;
        for (int c = 0; c < contexts.Count; c++)
        {
            RedactContext context = contexts[c];
            foreach (Transform combined in TextFrames(local, state, context))
            {
                RectangleF textBox = TransformedBox(combined, state.TextX, bottom, state.TextX + width, top);
                for (int r = 0; r < context.Rects.Count; r++)
                {
                    if (!textBox.Intersect(context.Rects[r]).IsEmpty)
                    {
                        return true;
                    }
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

    private static bool IsImage(PdfStream stream)
    {
        return stream.Dictionary.TryGetValue(PdfName.Subtype, out PdfPrimitive? subtype)
            && subtype is PdfName name
            && name.Value == "Image";
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

            // A form without resources of its own draws with those of the
            // content that invokes it; remember them (or that they differ
            // between invocations).
            if (!stream.Dictionary.ContainsKey(PdfName.Resources))
            {
                if (!run.InheritedInvocations.TryGetValue(objNum, out List<(PdfDictionary? Resources, List<RedactContext> Contexts)>? invocations))
                {
                    invocations = new List<(PdfDictionary? Resources, List<RedactContext> Contexts)>();
                    run.InheritedInvocations[objNum] = invocations;
                }

                invocations.Add((resources, formContexts));
                if (!run.InheritedFormResources.TryGetValue(objNum, out PdfDictionary? seen))
                {
                    run.InheritedFormResources[objNum] = resources;
                }
                else if (!ReferenceEquals(seen, resources))
                {
                    run.InheritedFormResources[objNum] = null;
                }
            }
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
        int eiEnd = FindInlineImageEnd(tok, content);
        tok.Seek(eiEnd < 0 ? content.Length : eiEnd);
    }

    // Rewrites every collected form XObject's content stream in place (same
    // object id), removing in-rect text under any of its placements, and marks
    // the original for exclusion so no unredacted copy survives in the output.
    private static Dictionary<int, (PdfDictionary Dict, byte[] Content, List<BlankedImage> Images, PatternUses Uses)> RewriteCollectedForms(
        PdfDocument document, RedactRun run, List<PdfIndirectObject> allObjects, HashSet<int> excluded)
    {
        Dictionary<int, (PdfDictionary Dict, byte[] Content, List<BlankedImage> Images, PatternUses Uses)> rewritten =
            new Dictionary<int, (PdfDictionary Dict, byte[] Content, List<BlankedImage> Images, PatternUses Uses)>();
        foreach (KeyValuePair<int, List<RedactContext>> kvp in run.FormInvocations)
        {
            int objNum = kvp.Key;
            if (run.Store.ResolveById(new PdfObjectId(objNum, 0)) is not PdfStream form)
            {
                continue;
            }

            byte[] formBytes = DecodeStream(form, run.Pipeline);
            PdfDictionary? formResources = ResolveFormResources(form, null, run.Store);

            // A form that inherits its resources from one and the same
            // invoking content everywhere is given its own copy of them
            // (equivalent for painting), so blanked images and redacted
            // pattern cells can be added to it. One drawn with different
            // inherited resources in different places cannot take new
            // names: a partly covered image in it is removed instead.
            PdfDictionary? materialised = null;
            if (!form.Dictionary.ContainsKey(PdfName.Resources)
                && run.InheritedFormResources.TryGetValue(objNum, out PdfDictionary? inherited)
                && inherited is not null
                && OnlyInvokerOf(document, run, objNum, inherited))
            {
                materialised = CopyDictionary(inherited);
                formResources = materialised;
                run.MaterialisedForms.Add(objNum);
            }

            // Blanked images join the form's own resources.
            List<BlankedImage> images = new List<BlankedImage>();
            bool ownResources = (form.Dictionary.ContainsKey(PdfName.Resources) || materialised is not null)
                && formResources is not null;
            PatternUses uses = new PatternUses();
            byte[] redacted = RewriteContent(
                formBytes, kvp.Value, run, formResources, System.Array.Empty<PatternRule>(),
                ownResources ? images : null, uses).Bytes;

            PdfDictionary newDict = CopyDictionary(form.Dictionary);
            newDict.Remove(PdfName.Intern("Filter"));
            newDict.Remove(PdfName.Intern("DecodeParms"));
            newDict.Set(PdfName.Length, redacted.Length);
            if (materialised is not null)
            {
                newDict.Set(PdfName.Resources, materialised);
            }

            allObjects.Add(new PdfIndirectObject(new PdfObjectId(objNum, 0), new PdfStream(newDict, redacted)));
            excluded.Add(objNum);
            rewritten[objNum] = (newDict, redacted, images, uses);
            run.RewrittenForms[objNum] = redacted;
        }

        return rewritten;
    }

    // True when every resource dictionary in the document that names the
    // form (pages' effective resources, and the resources of forms,
    // patterns, appearances and fonts) is the given one: the form is then
    // always drawn with those resources.
    private static bool OnlyInvokerOf(PdfDocument document, RedactRun run, int formNum, PdfDictionary invoker)
    {
        bool Names(PdfDictionary? resources)
        {
            if (resources is null
                || !resources.TryGetValue(PdfName.XObject, out PdfPrimitive? xv)
                || run.Store.Resolve(xv) is not PdfDictionary xobjects)
            {
                return false;
            }

            foreach (PdfName key in xobjects.Keys)
            {
                if (xobjects[key] is PdfReference r && r.ObjectId.ObjectNumber == formNum)
                {
                    return true;
                }
            }

            return false;
        }

        for (int p = 0; p < document.PageCount; p++)
        {
            PdfDictionary? resources = document.Pages[p].Resources;
            if (Names(resources) && !ReferenceEquals(resources, invoker))
            {
                return false;
            }
        }

        foreach (PdfIndirectObject obj in run.Store.Objects)
        {
            PdfDictionary? dict = obj.Value as PdfDictionary ?? (obj.Value as PdfStream)?.Dictionary;
            if (dict is null || !dict.TryGetValue(PdfName.Resources, out PdfPrimitive? rv))
            {
                continue;
            }

            PdfDictionary? resources = run.Store.Resolve(rv) as PdfDictionary;
            if (Names(resources) && !ReferenceEquals(resources, invoker))
            {
                return false;
            }
        }

        return true;
    }

    // ── Orphaned-resource removal ─────────────────────────────────────────

    // A redacted page's thumbnail (/Thumb) is a picture of the unredacted
    // page, and /PieceInfo (on a page or a form) holds the authoring
    // application's private data (often a full copy of the original
    // artwork): both are removed from the rewritten page or form, and the
    // objects they reference become removal candidates (omitted unless
    // something else still reaches them).
    private static void StripPrivateEntries(PdfDictionary page, HashSet<int> candidates)
    {
        foreach (string key in new[] { "Thumb", "PieceInfo" })
        {
            PdfName name = PdfName.Intern(key);
            if (page.TryGetValue(name, out PdfPrimitive? value))
            {
                CollectDirectReferences(value, candidates, 0);
                page.Remove(name);
            }
        }
    }

    // Prunes resources a rewritten page shares with other pages: resources
    // inherited from a page-tree node, and resource dictionaries that are
    // indirect objects (which other pages or nodes may reference). Each
    // rewritten page first gets its own direct copy (already pruned when its
    // content no longer uses an entry); then each shared node or object
    // keeps only the entries still used by the pages that were not
    // rewritten and use it (none left: every prunable entry goes). A page
    // whose content cannot be decoded draws nothing in any viewer and
    // contributes no uses. Returns the replacement objects by number.
    private static Dictionary<int, PdfDictionary> PruneInheritedResources(
        PdfDocument document, Dictionary<int, PdfObjectId> pageIds, List<RedactionWork> work,
        List<PdfDictionary> modifiedPages, HashSet<int> rewrittenPageNums, RedactRun run,
        FilterPipeline pipeline, HashSet<int> candidates)
    {
        Dictionary<int, PdfDictionary> replaced = new Dictionary<int, PdfDictionary>();

        // Shared holders: a node with direct /Resources (by node number), or
        // an indirect resource dictionary (by its own number).
        Dictionary<int, PdfDictionary> nodeHolders = new Dictionary<int, PdfDictionary>();
        HashSet<int> sharedDictionaries = new HashSet<int>();
        for (int i = 0; i < work.Count; i++)
        {
            (PdfPrimitive? entry, int nodeNum, PdfDictionary? node) = ResourceEntry(work[i].Page.Dictionary, run.Store);
            if (entry is PdfReference shared)
            {
                sharedDictionaries.Add(shared.ObjectId.ObjectNumber);
            }
            else if (entry is not null && node is not null)
            {
                nodeHolders[nodeNum] = node;
            }

            PdfPrimitive? current = modifiedPages[i].TryGetValue(PdfName.Resources, out PdfPrimitive? own) ? own : null;
            if ((current is null || current is PdfReference)
                && (current is null ? work[i].Page.Resources : run.Store.Resolve(current) as PdfDictionary) is PdfDictionary effective)
            {
                modifiedPages[i].Set(PdfName.Resources, CopyDictionary(effective));
            }
        }

        if (nodeHolders.Count == 0 && sharedDictionaries.Count == 0)
        {
            return replaced;
        }

        // Content of every page not rewritten, by the holder it draws with.
        Dictionary<int, MemoryStream> nodeUses = new Dictionary<int, MemoryStream>();
        Dictionary<int, MemoryStream> dictionaryUses = new Dictionary<int, MemoryStream>();
        try
        {
            for (int p = 0; p < document.PageCount; p++)
            {
                if (!pageIds.TryGetValue(p, out PdfObjectId id) || rewrittenPageNums.Contains(id.ObjectNumber))
                {
                    continue;
                }

                PdfPage page = document.Pages[p];
                (PdfPrimitive? entry, int nodeNum, PdfDictionary? node) = ResourceEntry(page.Dictionary, run.Store);
                MemoryStream? sink = null;
                if (entry is PdfReference shared && sharedDictionaries.Contains(shared.ObjectId.ObjectNumber))
                {
                    if (!dictionaryUses.TryGetValue(shared.ObjectId.ObjectNumber, out sink))
                    {
                        sink = new MemoryStream();
                        dictionaryUses[shared.ObjectId.ObjectNumber] = sink;
                    }
                }
                else if (entry is not null && entry is not PdfReference && node is not null && nodeHolders.ContainsKey(nodeNum))
                {
                    if (!nodeUses.TryGetValue(nodeNum, out sink))
                    {
                        sink = new MemoryStream();
                        nodeUses[nodeNum] = sink;
                    }
                }

                if (sink is null)
                {
                    continue;
                }

                byte[] content;
                try
                {
                    content = LoadContentBytes(page, run.Store, pipeline);
                }
                catch (Exception)
                {
                    continue;
                }

                sink.Write(content, 0, content.Length);
                sink.WriteByte((byte)'\n');
            }

            foreach (KeyValuePair<int, PdfDictionary> holder in nodeHolders)
            {
                if (run.Store.Resolve(holder.Value[PdfName.Resources]) is not PdfDictionary nodeResources)
                {
                    continue;
                }

                byte[] uses = nodeUses.TryGetValue(holder.Key, out MemoryStream? u) ? u.ToArray() : Array.Empty<byte>();
                PdfDictionary node = CopyDictionary(holder.Value);
                node.Set(PdfName.Resources, nodeResources);
                PruneUnusedImages(node, nodeResources, uses, run, candidates);
                if (!ReferenceEquals(node[PdfName.Resources], nodeResources))
                {
                    replaced[holder.Key] = node;
                }
            }

            // A resource dictionary also named by a form, pattern, appearance
            // or font is used by content not scanned here: it is left whole.
            HashSet<int> otherwiseUsed = sharedDictionaries.Count > 0
                ? ReferencedOutsidePageResources(document.Objects)
                : new HashSet<int>();
            foreach (int number in sharedDictionaries)
            {
                if (otherwiseUsed.Contains(number)
                    || run.Store.ResolveById(new PdfObjectId(number, 0)) is not PdfDictionary sharedResources)
                {
                    continue;
                }

                byte[] uses = dictionaryUses.TryGetValue(number, out MemoryStream? u) ? u.ToArray() : Array.Empty<byte>();
                PdfDictionary holder = new PdfDictionary();
                holder.Set(PdfName.Resources, sharedResources);
                PruneUnusedImages(holder, sharedResources, uses, run, candidates);
                if (holder[PdfName.Resources] is PdfDictionary pruned && !ReferenceEquals(pruned, sharedResources))
                {
                    replaced[number] = pruned;
                }
            }
        }
        finally
        {
            foreach (MemoryStream ms in nodeUses.Values)
            {
                ms.Dispose();
            }

            foreach (MemoryStream ms in dictionaryUses.Values)
            {
                ms.Dispose();
            }
        }

        return replaced;
    }

    // Object numbers referenced anywhere except as the /Resources entry of
    // a page or page-tree node.
    private static HashSet<int> ReferencedOutsidePageResources(PdfObjectStore store)
    {
        HashSet<int> found = new HashSet<int>();
        Stack<PdfPrimitive> pending = new Stack<PdfPrimitive>();
        foreach (PdfIndirectObject obj in store.Objects)
        {
            pending.Push(obj.Value);
            while (pending.Count > 0)
            {
                switch (pending.Pop())
                {
                    case PdfReference r:
                        found.Add(r.ObjectId.ObjectNumber);
                        break;
                    case PdfDictionary d:
                        bool pageTree = d.TryGetValue(PdfName.Type, out PdfPrimitive? t)
                            && t is PdfName tn && (tn.Value == "Page" || tn.Value == "Pages");
                        foreach (PdfName key in d.Keys)
                        {
                            if (!(pageTree && key.Value == "Resources"))
                            {
                                pending.Push(d[key]);
                            }
                        }

                        break;
                    case PdfArray arr:
                        for (int i = 0; i < arr.Count; i++)
                        {
                            pending.Push(arr[i]);
                        }

                        break;
                    case PdfStream st:
                        pending.Push(st.Dictionary);
                        break;
                }
            }
        }

        return found;
    }

    // A page's effective /Resources entry as written (its own, or the
    // nearest ancestor's), with that ancestor's number and dictionary when
    // inherited (0 and null when the page has its own).
    private static (PdfPrimitive? Entry, int NodeNum, PdfDictionary? Node) ResourceEntry(
        PdfDictionary page, PdfObjectStore store)
    {
        if (page.TryGetValue(PdfName.Resources, out PdfPrimitive? own))
        {
            return (own, 0, null);
        }

        if (FindResourceHolder(page, store) is { } holder)
        {
            return (holder.Node[PdfName.Resources], holder.ObjNum, holder.Node);
        }

        return (null, 0, null);
    }

    // The nearest page-tree ancestor holding /Resources for a page that has
    // none of its own, with its object number; null when there is none.
    private static (int ObjNum, PdfDictionary Node)? FindResourceHolder(PdfDictionary page, PdfObjectStore store)
    {
        PdfDictionary current = page;
        for (int depth = 0; depth < 64; depth++)
        {
            if (!current.TryGetValue(PdfName.Intern("Parent"), out PdfPrimitive? parentRef)
                || parentRef is not PdfReference reference
                || store.Resolve(parentRef) is not PdfDictionary parent)
            {
                return null;
            }

            if (parent.ContainsKey(PdfName.Resources))
            {
                return (reference.ObjectId.ObjectNumber, parent);
            }

            current = parent;
        }

        return null;
    }

    // Adds the object numbers of references held directly in a value or in
    // its direct (non-indirect) dictionaries and arrays.
    private static void CollectDirectReferences(PdfPrimitive value, HashSet<int> into, int depth)
    {
        if (depth > 32)
        {
            return;
        }

        switch (value)
        {
            case PdfReference r:
                into.Add(r.ObjectId.ObjectNumber);
                break;
            case PdfDictionary d:
                foreach (PdfName key in d.Keys)
                {
                    CollectDirectReferences(d[key], into, depth + 1);
                }

                break;
            case PdfArray a:
                for (int i = 0; i < a.Count; i++)
                {
                    CollectDirectReferences(a[i], into, depth + 1);
                }

                break;
        }
    }

    // Resource categories whose entries carry drawable content and are
    // referenced from content by name: images and forms (Do), patterns
    // (scn/SCN), shadings (sh, and shading patterns) and graphics states (gs,
    // whose /SMask groups hold content).
    private static readonly string[] PrunableCategories = { "Pattern", "Shading", "ExtGState" };

    // Rewrites a page's (or form's) /Resources so it keeps only entries its
    // rewritten content still names: unused images in /XObject, and unused
    // /Pattern, /Shading and /ExtGState entries. Pruned entries' object
    // numbers become removal candidates (with everything they reach — see
    // OmitUnreachable). Resources are copied (never edited in place) because
    // they may be shared with, or inherited by, other pages. Forms that
    // inherit the container's resources (no /Resources of their own)
    // contribute the names they use; if one cannot be decoded, nothing is
    // pruned (its uses are unknown).
    private static void PruneUnusedImages(
        PdfDictionary container, PdfDictionary? resources, byte[] content,
        RedactRun run, HashSet<int> candidates)
    {
        if (resources is null)
        {
            return;
        }

        PdfDictionary? xobjects = resources.TryGetValue(PdfName.XObject, out PdfPrimitive? xv)
            ? run.Store.Resolve(xv) as PdfDictionary
            : null;

        HashSet<string> invoked = new HashSet<string>(StringComparer.Ordinal);
        HashSet<string> named = new HashSet<string>(StringComparer.Ordinal);
        HashSet<string> paintedPatterns = new HashSet<string>(StringComparer.Ordinal);
        bool complete = true;
        CollectInvokedNames(
            content, xobjects, run, invoked, named, paintedPatterns, new HashSet<int>(), 0, ref complete);
        if (!complete)
        {
            return;
        }

        PdfDictionary? resourcesCopy = null;

        if (xobjects is not null)
        {
            PdfDictionary kept = new PdfDictionary();
            bool pruned = false;
            foreach (PdfName key in xobjects.Keys)
            {
                PdfPrimitive value = xobjects[key];
                if (!invoked.Contains(key.Value)
                    && run.Store.Resolve(value) is PdfStream stream
                    && stream.Dictionary.TryGetValue(PdfName.Subtype, out PdfPrimitive? st)
                    && st is PdfName subtype && subtype.Value == "Image")
                {
                    if (value is PdfReference r)
                    {
                        candidates.Add(r.ObjectId.ObjectNumber);
                    }

                    pruned = true;
                    continue;
                }

                kept.Set(key, value);
            }

            if (pruned)
            {
                resourcesCopy = CopyDictionary(resources);
                resourcesCopy.Set(PdfName.XObject, kept);
            }
        }

        foreach (string category in PrunableCategories)
        {
            PdfName categoryName = PdfName.Intern(category);
            if (!resources.TryGetValue(categoryName, out PdfPrimitive? cv)
                || run.Store.Resolve(cv) is not PdfDictionary entries)
            {
                continue;
            }

            // A pattern is in use only if something is painted while it is
            // the current colour: after redaction removes the fill, the
            // leftover "/P0 scn" alone does not keep the pattern's content.
            HashSet<string> used = category == "Pattern" ? paintedPatterns : named;
            PdfDictionary kept = new PdfDictionary();
            bool pruned = false;
            foreach (PdfName key in entries.Keys)
            {
                PdfPrimitive value = entries[key];
                if (!used.Contains(key.Value))
                {
                    if (value is PdfReference r)
                    {
                        candidates.Add(r.ObjectId.ObjectNumber);
                    }

                    pruned = true;
                    continue;
                }

                kept.Set(key, value);
            }

            if (pruned)
            {
                resourcesCopy ??= CopyDictionary(resources);
                resourcesCopy.Set(categoryName, kept);
            }
        }

        if (resourcesCopy is not null)
        {
            container.Set(PdfName.Resources, resourcesCopy);
        }
    }

    // Collects the XObject names invoked by Do, every name token used as an
    // operand, and the pattern names actually painted with (current fill or
    // stroke pattern when a painting operator runs), in the content and in
    // the resource-inheriting forms it invokes. Sets complete to false when
    // such a form cannot be decoded.
    private static void CollectInvokedNames(
        byte[] content, PdfDictionary? xobjects, RedactRun run,
        HashSet<string> invoked, HashSet<string> named, HashSet<string> paintedPatterns,
        HashSet<int> visitedForms, int depth, ref bool complete)
    {
        if (depth > 8)
        {
            complete = false;
            return;
        }

        using MemoryStream input = new MemoryStream(content);
        using PdfTokenizer tok = new PdfTokenizer(input);
        string? lastName = null;
        string? fillPattern = null;
        string? strokePattern = null;
        Stack<(string? Fill, string? Stroke)> saved = new Stack<(string? Fill, string? Stroke)>();
        while (true)
        {
            PdfToken t = tok.Read();
            if (t.IsEndOfStream)
            {
                break;
            }

            if (t.Type == PdfTokenType.Name)
            {
                lastName = DecodeNameText(t.RawText);
                named.Add(lastName);
                continue;
            }

            if (t.Type == PdfTokenType.Keyword && t.RawText == "BI")
            {
                SkipInlineImage(tok, content);
                MarkPainted(paintedPatterns, fillPattern, strokePattern);
                lastName = null;
                continue;
            }

            if (t.Type == PdfTokenType.Keyword)
            {
                switch (t.RawText)
                {
                    case "q":
                        saved.Push((fillPattern, strokePattern));
                        break;
                    case "Q":
                        if (saved.Count > 0)
                        {
                            (fillPattern, strokePattern) = saved.Pop();
                        }

                        break;
                    case "scn":
                        fillPattern = lastName;
                        break;
                    case "SCN":
                        strokePattern = lastName;
                        break;
                    case "cs":
                    case "sc":
                    case "g":
                    case "rg":
                    case "k":
                        fillPattern = null;
                        break;
                    case "CS":
                    case "SC":
                    case "G":
                    case "RG":
                    case "K":
                        strokePattern = null;
                        break;
                    case "f":
                    case "F":
                    case "f*":
                    case "B":
                    case "B*":
                    case "b":
                    case "b*":
                    case "S":
                    case "s":
                    case "Tj":
                    case "TJ":
                    case "'":
                    case "\"":
                    case "Do":
                    case "EI":
                        MarkPainted(paintedPatterns, fillPattern, strokePattern);
                        break;
                }
            }

            if (t.Type == PdfTokenType.Keyword && t.RawText == "Do" && lastName is not null)
            {
                if (invoked.Add(lastName)
                    && xobjects is not null
                    && xobjects.TryGetValue(PdfName.Intern(lastName), out PdfPrimitive? xref)
                    && xref is PdfReference formRef
                    && run.Store.Resolve(xref) is PdfStream form
                    && form.Dictionary.TryGetValue(PdfName.Subtype, out PdfPrimitive? st)
                    && st is PdfName subtype && subtype.Value == "Form"
                    && !form.Dictionary.ContainsKey(PdfName.Resources)
                    && !run.MaterialisedForms.Contains(formRef.ObjectId.ObjectNumber)
                    && visitedForms.Add(formRef.ObjectId.ObjectNumber))
                {
                    // A form without its own resources draws with these ones.
                    // A rewritten form is written with its redacted content,
                    // so that content (not the original) decides what it uses.
                    byte[] formContent;
                    if (run.RewrittenForms.TryGetValue(formRef.ObjectId.ObjectNumber, out byte[]? rewrittenContent))
                    {
                        formContent = rewrittenContent;
                    }
                    else
                    {
                        try
                        {
                            formContent = DecodeStream(form, run.Pipeline);
                        }
                        catch (Exception)
                        {
                            complete = false;
                            return;
                        }
                    }

                    CollectInvokedNames(
                        formContent, xobjects, run, invoked, named, paintedPatterns, visitedForms, depth + 1,
                        ref complete);
                    if (!complete)
                    {
                        return;
                    }
                }
            }

            if (t.Type == PdfTokenType.Keyword)
            {
                lastName = null;
            }
        }
    }

    // Painting marks both current patterns used (over-keeping is safe: a
    // pattern is only pruned when nothing at all is painted with it).
    private static void MarkPainted(HashSet<string> painted, string? fill, string? stroke)
    {
        if (fill is not null)
        {
            painted.Add(fill);
        }

        if (stroke is not null)
        {
            painted.Add(stroke);
        }
    }

    private static string DecodeNameText(string raw)
    {
        string name = raw.StartsWith('/') ? raw.Substring(1) : raw;
        if (!name.Contains('#'))
        {
            return name;
        }

        System.Text.StringBuilder sb = new System.Text.StringBuilder(name.Length);
        for (int i = 0; i < name.Length; i++)
        {
            if (name[i] == '#' && i + 2 < name.Length
                && int.TryParse(name.AsSpan(i + 1, 2), System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out int code))
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

    // Rewrites every output object that holds (directly or nested) one of the
    // named property lists used by a redacted marked-content sequence, with
    // the lists' replacement text removed. Other uses of a shared list lose
    // that accessibility text too — over-redaction in doubt (B15).
    private static void StripNamedPropertyLists(
        List<PdfIndirectObject> objects, HashSet<PdfDictionary> targets)
    {
        for (int i = 0; i < objects.Count; i++)
        {
            PdfPrimitive value = objects[i].Value;
            if (HoldsTarget(value, targets, 0))
            {
                objects[i] = new PdfIndirectObject(objects[i].Id, CloneStrippingTargets(value, targets));
            }
        }
    }

    private static bool HoldsTarget(PdfPrimitive value, HashSet<PdfDictionary> targets, int depth)
    {
        if (depth > 64)
        {
            return false;
        }

        switch (value)
        {
            case PdfDictionary d:
                if (targets.Contains(d))
                {
                    return true;
                }

                foreach (PdfName key in d.Keys)
                {
                    if (HoldsTarget(d[key], targets, depth + 1))
                    {
                        return true;
                    }
                }

                return false;
            case PdfArray a:
                for (int i = 0; i < a.Count; i++)
                {
                    if (HoldsTarget(a[i], targets, depth + 1))
                    {
                        return true;
                    }
                }

                return false;
            case PdfStream st:
                return HoldsTarget(st.Dictionary, targets, depth + 1);
            default:
                return false;
        }
    }

    private static PdfPrimitive CloneStrippingTargets(PdfPrimitive value, HashSet<PdfDictionary> targets)
    {
        switch (value)
        {
            case PdfDictionary d:
                bool strip = targets.Contains(d);
                PdfDictionary copy = new PdfDictionary();
                foreach (PdfName key in d.Keys)
                {
                    if (strip && IsReplacementTextKey(key.Value))
                    {
                        continue;
                    }

                    copy.Set(key, CloneStrippingTargets(d[key], targets));
                }

                return copy;
            case PdfArray a:
                List<PdfPrimitive> items = new List<PdfPrimitive>(a.Count);
                for (int i = 0; i < a.Count; i++)
                {
                    items.Add(CloneStrippingTargets(a[i], targets));
                }

                return new PdfArray(items);
            case PdfStream st:
                return new PdfStream((PdfDictionary)CloneStrippingTargets(st.Dictionary, targets), st.RawBytes);
            default:
                return value;
        }
    }

    // A structure tree object reference (PDF 14.7.4.3): /Type /OBJR, or an
    // untyped dictionary with /Obj and no structure type.
    private static bool IsObjectReference(PdfDictionary d)
    {
        if (d.TryGetValue(PdfName.Type, out PdfPrimitive? type) && type is PdfName name)
        {
            return name.Value == "OBJR";
        }

        return d.ContainsKey(PdfName.Intern("Obj")) && !d.ContainsKey(PdfName.Intern("S"));
    }

    // Drops candidate objects the output no longer reaches from the trailer.
    private static List<PdfIndirectObject> OmitUnreachable(
        List<PdfIndirectObject> objects, PdfDictionary trailer, HashSet<int> candidates)
    {
        Dictionary<int, PdfPrimitive> byNumber = new Dictionary<int, PdfPrimitive>();
        foreach (PdfIndirectObject obj in objects)
        {
            byNumber[obj.Id.ObjectNumber] = obj.Value;
        }

        // A removed object's dependents (an image's /SMask and /Mask, a
        // pattern's or form's resources and fonts, a graphics state's soft-mask
        // group) are candidates too: each still leaves only if nothing else
        // reaches it.
        HashSet<int> closure = new HashSet<int>(candidates);
        Stack<PdfPrimitive> expand = new Stack<PdfPrimitive>();
        foreach (int candidate in candidates)
        {
            if (byNumber.TryGetValue(candidate, out PdfPrimitive? value))
            {
                expand.Push(value);
            }
        }

        while (expand.Count > 0)
        {
            switch (expand.Pop())
            {
                case PdfReference r:
                    if (closure.Add(r.ObjectId.ObjectNumber)
                        && byNumber.TryGetValue(r.ObjectId.ObjectNumber, out PdfPrimitive? target))
                    {
                        expand.Push(target);
                    }

                    break;
                case PdfDictionary d:
                    foreach (PdfName key in d.Keys)
                    {
                        if (key.Value != "Parent" && key.Value != "P" && key.Value != "Pg")
                        {
                            expand.Push(d[key]);
                        }
                    }

                    break;
                case PdfArray a:
                    for (int i = 0; i < a.Count; i++)
                    {
                        expand.Push(a[i]);
                    }

                    break;
                case PdfStream st:
                    expand.Push(st.Dictionary);
                    break;
            }
        }

        candidates = closure;

        HashSet<int> reachable = new HashSet<int>();
        Stack<PdfPrimitive> pending = new Stack<PdfPrimitive>();
        pending.Push(trailer);
        while (pending.Count > 0)
        {
            PdfPrimitive current = pending.Pop();
            switch (current)
            {
                case PdfReference r:
                    if (reachable.Add(r.ObjectId.ObjectNumber)
                        && byNumber.TryGetValue(r.ObjectId.ObjectNumber, out PdfPrimitive? target))
                    {
                        pending.Push(target);
                    }

                    break;
                case PdfDictionary d:
                    bool objectReference = IsObjectReference(d);
                    foreach (PdfName key in d.Keys)
                    {
                        // A structure element's object reference (OBJR) only
                        // labels an object; it does not keep a removed one
                        // (an image no longer drawn) in the file.
                        if (objectReference && key.Value == "Obj" && d[key] is PdfReference labelled
                            && candidates.Contains(labelled.ObjectId.ObjectNumber))
                        {
                            continue;
                        }

                        pending.Push(d[key]);
                    }

                    break;
                case PdfArray a:
                    for (int i = 0; i < a.Count; i++)
                    {
                        pending.Push(a[i]);
                    }

                    break;
                case PdfStream st:
                    pending.Push(st.Dictionary);
                    break;
            }
        }

        List<PdfIndirectObject> kept = new List<PdfIndirectObject>(objects.Count);
        foreach (PdfIndirectObject obj in objects)
        {
            if (candidates.Contains(obj.Id.ObjectNumber) && !reachable.Contains(obj.Id.ObjectNumber))
            {
                continue;
            }

            kept.Add(obj);
        }

        return kept;
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

        // Also preload the logical structure tree. It hangs off the catalog, not
        // the page graph, so without this it is never resolved — leaving its
        // objects out of the object-number count (new content streams would then
        // reuse their numbers) and dropping the tags from the output entirely.
        // Redaction must keep and neutralise the structure tree (LA-04).
        PdfDictionary? catalog = FindCatalog(document);
        if (catalog is not null
            && catalog.TryGetValue(PdfName.Intern("StructTreeRoot"), out PdfPrimitive? structTreeRoot))
        {
            Visit(document.Objects, structTreeRoot, visited);
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

    // The current font's glyphs may draw outside the usual em box (a Type 3
    // font, or a font that could not be resolved).
    internal bool FontExtentUnknown { get; set; } = true;

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

    // Blanked copies of partly redacted images (null when new images cannot
    // be added to this content's resources), and the outcome per image and
    // placement so a repeated Do reuses one copy.
    internal List<BlankedImage>? BlankedImages { get; set; }

    // Pattern tracking for tiling-pattern cell redaction (see
    // TrackPatternPaint): current fill/stroke pattern names and the
    // axis-aligned clip rectangle (or an unknown clip), saved by q.
    internal string? FillPattern { get; set; }

    internal string? StrokePattern { get; set; }

    internal RectangleF? ClipRect { get; set; }

    internal bool ClipUnknown { get; set; }

    // An ExtGState is in effect (opacity, soft mask or blend mode may hide
    // paint); a cm arrived inside the buffered path; open marked-content
    // scopes (true for an optional-content /OC scope).
    internal bool MayHidePaint { get; set; }

    internal bool PathCtmChanged { get; set; }

    internal Stack<bool> MarkedScopes { get; } = new Stack<bool>();

    internal Stack<(string? Fill, string? Stroke, RectangleF? Clip, bool Unknown, bool MayHide)> PatternStack { get; } =
        new Stack<(string? Fill, string? Stroke, RectangleF? Clip, bool Unknown, bool MayHide)>();

    internal Dictionary<(int, Transform), (ImageBlankOutcome Outcome, string? Name)> BlankedImageCache { get; } =
        new Dictionary<(int, Transform), (ImageBlankOutcome Outcome, string? Name)>();

    // Text-state spacing (PDF 9.4.4): added to each glyph's advance in text
    // space. Word spacing applies only to the single-byte space (code 32).
    // Both are part of the graphics state and are saved/restored by q/Q.
    internal double CharSpacing { get; set; }
    internal double WordSpacing { get; set; }
    private readonly Stack<double> _charSpacingStack = new Stack<double>();
    private readonly Stack<double> _wordSpacingStack = new Stack<double>();

    // Text rise (Ts, text-space units, shifts glyphs up) and horizontal
    // scaling (Tz ÷ 100, scales every advance). Both are text-state
    // parameters saved and restored by q/Q (PDF 9.3).
    internal double TextRise { get; set; }
    internal double HorizontalScaling { get; set; } = 1.0;
    private readonly Stack<double> _riseStack = new Stack<double>();
    private readonly Stack<double> _scalingStack = new Stack<double>();

    // Stroke geometry state (PDF 8.4.3), used by the precise vector subtractor
    // to bound how far a stroked path's ink can reach from its centreline.
    // Saved/restored by q/Q like the rest of the graphics state. Defaults per
    // the spec: line width 1.0, miter limit 10.0.
    internal double LineWidth { get; set; } = 1.0;
    internal double MiterLimit { get; set; } = 10.0;

    // False once an ExtGState whose /LW or /ML could not be resolved has been
    // applied: the stroke ink allowance is then unknown and the vector
    // subtractor must fall back to whole-path dropping for strokes.
    internal bool StrokeStateReliable { get; set; } = true;

    private readonly Stack<double> _lineWidthStack = new Stack<double>();
    private readonly Stack<double> _miterLimitStack = new Stack<double>();
    private readonly Stack<bool> _strokeReliableStack = new Stack<bool>();

    // The rest of the text state (PDF 9.3): leading, font and size are part
    // of the graphics state too, saved by q and restored by Q.
    private readonly Stack<(double Leading, double FontSize, int[]? Widths, PdfFont? Font, bool ExtentUnknown)> _textStateStack =
        new Stack<(double Leading, double FontSize, int[]? Widths, PdfFont? Font, bool ExtentUnknown)>();

    internal void PushGraphicsState()
    {
        _textStateStack.Push((Leading, FontSize, GlyphWidths, Font, FontExtentUnknown));
        _ctmStack.Push(Ctm);
        _charSpacingStack.Push(CharSpacing);
        _wordSpacingStack.Push(WordSpacing);
        _riseStack.Push(TextRise);
        _scalingStack.Push(HorizontalScaling);
        _lineWidthStack.Push(LineWidth);
        _miterLimitStack.Push(MiterLimit);
        _strokeReliableStack.Push(StrokeStateReliable);
    }

    internal void PopGraphicsState()
    {
        if (_textStateStack.Count > 0)
        {
            (Leading, FontSize, GlyphWidths, Font, FontExtentUnknown) = _textStateStack.Pop();
        }

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

        if (_riseStack.Count > 0)
        {
            TextRise = _riseStack.Pop();
        }

        if (_scalingStack.Count > 0)
        {
            HorizontalScaling = _scalingStack.Pop();
        }

        if (_lineWidthStack.Count > 0)
        {
            LineWidth = _lineWidthStack.Pop();
        }

        if (_miterLimitStack.Count > 0)
        {
            MiterLimit = _miterLimitStack.Pop();
        }

        if (_strokeReliableStack.Count > 0)
        {
            StrokeStateReliable = _strokeReliableStack.Pop();
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
/// Tiling patterns a content stream paints with, and where a pattern fill is
/// known to show (local-space rectangles), for redacting pattern cells.
/// </summary>
internal sealed class PatternUses
{
    public HashSet<string> Painted { get; } = new HashSet<string>(StringComparer.Ordinal);

    public Dictionary<string, List<RectangleF>> Shown { get; } =
        new Dictionary<string, List<RectangleF>>(StringComparer.Ordinal);

    // Local-space bounds of the fills painted with each pattern; a pattern
    // in PaintedAnywhere was also painted where its extent is not modelled
    // (strokes, text, images, forms), so the whole box counts.
    public Dictionary<string, List<RectangleF>> PaintedBounds { get; } =
        new Dictionary<string, List<RectangleF>>(StringComparer.Ordinal);

    public HashSet<string> PaintedAnywhere { get; } = new HashSet<string>(StringComparer.Ordinal);
}

/// <summary>
/// A partly redacted image: its blanked copy, the resource name the rewritten
/// content uses for it, and its masks (referenced from the copy through
/// placeholder references until object numbers are assigned).
/// </summary>
internal sealed class BlankedImage
{
    public BlankedImage(string name, PdfStream image, List<(PdfReference Placeholder, PdfStream Stream)> masks)
    {
        Name = name;
        Image = image;
        Masks = masks;
    }

    public string Name { get; }

    public PdfStream Image { get; }

    public List<(PdfReference Placeholder, PdfStream Stream)> Masks { get; }
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

    /// <summary>How painted vector paths intersecting a region are removed.</summary>
    public VectorRedactionMode VectorMode { get; set; } = VectorRedactionMode.SubtractPrecise;

    /// <summary>Form object number to the placements at which it is invoked.</summary>
    public Dictionary<int, List<RedactContext>> FormInvocations { get; } = new();

    /// <summary>
    /// Form object number to its redacted content, once rewritten; resource
    /// pruning reads a rewritten form's uses from this, not the original.
    /// </summary>
    public Dictionary<int, byte[]> RewrittenForms { get; } = new();

    /// <summary>
    /// For a form without its own /Resources: the resources of the content
    /// invoking it, or null when different invocations use different ones.
    /// Guarded by <see cref="Sync"/>.
    /// </summary>
    public Dictionary<int, PdfDictionary?> InheritedFormResources { get; } = new();

    /// <summary>
    /// Forms that inherited their resources and were given their own copy:
    /// they no longer draw with (or keep anything in) the invoker's resources.
    /// </summary>
    public HashSet<int> MaterialisedForms { get; } = new();

    /// <summary>
    /// For a form without its own /Resources: each invocation's inherited
    /// resources and the form's redaction contexts there. Guarded by
    /// <see cref="Sync"/>.
    /// </summary>
    public Dictionary<int, List<(PdfDictionary? Resources, List<RedactContext> Contexts)>> InheritedInvocations { get; } = new();

    /// <summary>
    /// Tiling patterns rewritten in place (same object number) for forms that
    /// draw them through inherited resources; later cell redaction of the
    /// same pattern starts from the rewritten cell.
    /// </summary>
    public Dictionary<int, PdfStream> ReplacedPatterns { get; } = new();

    /// <summary>Guards <see cref="FormInvocations"/> during parallel collection.</summary>
    public object Sync { get; } = new();

    /// <summary>
    /// Named marked-content property lists (resolved from /Resources
    /// /Properties) used by a redacted sequence; their replacement text
    /// (/ActualText, /Alt, /E) is stripped from the output. Guarded by
    /// <see cref="Sync"/>.
    /// </summary>
    public HashSet<PdfDictionary> StripPropertyLists { get; } =
        new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
}
