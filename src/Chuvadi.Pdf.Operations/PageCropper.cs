// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §14.11.2 (page boundaries: MediaBox/CropBox)
//        §8.5.4 (clipping path operators W, n); §7.8.2 (content streams)

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Filters;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// Crops pages to a rectangle by setting their <c>/MediaBox</c> and
/// <c>/CropBox</c> to the crop rectangle and wrapping the existing content in a
/// hard clip (<c>q &lt;rect&gt; re W n &#8230; Q</c>) so nothing outside the
/// rectangle is painted.
/// </summary>
/// <remarks>
/// This is a lossless, visual crop: in-box content is preserved byte-for-byte
/// and the page is resized to the crop rectangle, but the bytes of off-box
/// content remain in the file (clipped from view, not removed). For a
/// redaction-grade crop that physically removes off-box content, a future
/// scrubbing mode is required.
/// </remarks>
public static class PageCropper
{
    /// <summary>Crops the requested pages of <paramref name="document"/> and writes the result to <paramref name="output"/>.</summary>
    /// <param name="output">The stream the cropped document is written to.</param>
    /// <param name="document">The source document.</param>
    /// <param name="crops">The pages to crop and the rectangle each is confined to.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public static void Crop(Stream output, PdfDocument document, IReadOnlyList<PageCrop> crops)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(crops);
        new Worker(document, crops).Write(output);
    }

    // ── Implementation ────────────────────────────────────────────────────

    private sealed class Worker
    {
        private readonly PdfDocument _document;
        private readonly PdfObjectStore _store;
        private readonly Dictionary<int, PageCrop> _crops = new Dictionary<int, PageCrop>();
        private readonly List<PdfIndirectObject> _extraObjects = new List<PdfIndirectObject>();
        private readonly List<(PdfObjectId Id, PdfDictionary Dict)> _modifiedPages =
            new List<(PdfObjectId, PdfDictionary)>();

        // Object numbers of annotations (and their appearance streams) dropped
        // by a Scrub crop because they lie entirely outside the crop box; these
        // objects are omitted from the output so their content does not leak.
        private readonly HashSet<int> _droppedObjects = new HashSet<int>();

        // Objects reachable from a scrubbed page's ORIGINAL dictionary. After
        // scrubbing, any of them no longer reachable from the catalog (a
        // replaced image or form, its masks and resources) is omitted from the
        // output — otherwise its full, uncropped content stays in the file.
        private readonly HashSet<int> _scrubCandidates = new HashSet<int>();
        private int _nextObjectNumber;

        internal Worker(PdfDocument document, IReadOnlyList<PageCrop> crops)
        {
            _document = document;
            _store = document.Objects;

            // B16: the store is lazy; without a full graph preload the output
            // would silently omit every object the crop never resolved
            // (annotations, fonts, outlines).
            ObjectGraphPreloader.Preload(document);

            foreach (PageCrop crop in crops)
            {
                _crops[crop.PageIndex] = crop;
            }

            _nextObjectNumber = 1;
            foreach (PdfIndirectObject obj in _document.Objects.Objects)
            {
                if (obj.Id.ObjectNumber >= _nextObjectNumber)
                {
                    _nextObjectNumber = obj.Id.ObjectNumber + 1;
                }
            }

            if (_document.Trailer.TryGetValue(PdfName.Size, out PdfPrimitive? sizePrim)
                && sizePrim is PdfInteger size && size.Value >= _nextObjectNumber)
            {
                _nextObjectNumber = size.Value;
            }
        }

        internal void Write(Stream output)
        {
            Dictionary<int, PdfObjectId> pageIds = PageTree.BuildIndexToIdMap(_document);
            PdfObjectId catalogId = FindCatalogId();

            foreach (KeyValuePair<int, PageCrop> entry in _crops)
            {
                if (entry.Key >= 0 && entry.Key < _document.PageCount
                    && pageIds.TryGetValue(entry.Key, out PdfObjectId pageId))
                {
                    ProcessPage(entry.Key, pageId, entry.Value);
                }
            }

            List<PdfIndirectObject> all = new List<PdfIndirectObject>();
            HashSet<int> replaced = new HashSet<int>();

            foreach ((PdfObjectId id, PdfDictionary dict) in _modifiedPages)
            {
                all.Add(new PdfIndirectObject(id, dict));
                replaced.Add(id.ObjectNumber);
            }

            // An appearance stream can be SHARED: only omit dropped objects
            // that no surviving annotation still references.
            HashSet<int> retained = CollectRetainedAnnotationObjects(pageIds);
            _droppedObjects.ExceptWith(retained);

            foreach (PdfIndirectObject obj in _document.Objects.Objects)
            {
                if (!replaced.Contains(obj.Id.ObjectNumber)
                    && !_droppedObjects.Contains(obj.Id.ObjectNumber))
                {
                    all.Add(obj);
                }
            }

            all.AddRange(_extraObjects);

            PdfDictionary trailer = BuildTrailer(catalogId);
            if (_scrubCandidates.Count > 0)
            {
                all = OmitUnreachableCandidates(all, trailer);
            }

            PdfWriter.Write(output, all, trailer);
        }

        private void ProcessPage(int pageIndex, PdfObjectId pageId, PageCrop crop)
        {
            RectangleF cropBox = crop.CropBox;
            PdfDictionary pageDict = _document.Pages[pageIndex].Dictionary;
            PdfDictionary newPage = ObjectImporter.CopyDictionary(pageDict);

            if (crop.Mode == PageCropMode.Scrub)
            {
                CollectReachable(pageDict, _scrubCandidates, skipParent: true);

                byte[] decoded = DecodePageContent(pageDict);

                // Resources may be inherited from the page tree (§7.7.3.4).
                PdfDictionary? resources = _document.Pages[pageIndex].Resources;
                PageScrubber.ScrubResult result = PageScrubber.Scrub(
                    decoded, cropBox.X, cropBox.Y, cropBox.Right, cropBox.Top, resources, _store, Allocate);

                // Keep only the XObjects the scrubbed content still invokes,
                // plus the cropped/scrubbed replacements.
                newPage.Set(
                    PdfName.Resources,
                    PageScrubber.BuildScrubbedResources(resources, result, _store, Allocate));

                PdfArray scrubContents = new PdfArray([]);
                scrubContents.Add(new PdfReference(NextStreamFromBytes(result.Content)));
                newPage.Set(PdfName.Contents, scrubContents);

                // Scrub-grade: annotations whose /Rect lies entirely outside
                // the crop box are removed from /Annots and their objects (and
                // appearance streams) omitted from the output — an off-box
                // annotation otherwise keeps its full content in the file.
                FilterAnnotations(newPage, cropBox);
            }
            else
            {
                // ClipOnly: wrap the existing content in a hard clip to the crop rectangle:
                //   q <x> <y> <w> <h> re W n <existing content> Q
                string clip = string.Format(
                    CultureInfo.InvariantCulture,
                    "q\n{0:0.####} {1:0.####} {2:0.####} {3:0.####} re W n\n",
                    cropBox.X, cropBox.Y, cropBox.Width, cropBox.Height);

                PdfArray contents = new PdfArray([]);
                contents.Add(new PdfReference(NextStreamFromBytes(Encoding.Latin1.GetBytes(clip))));
                AppendExistingContent(pageDict, contents);
                contents.Add(new PdfReference(NextStreamFromBytes(Encoding.Latin1.GetBytes("\nQ\n"))));
                newPage.Set(PdfName.Contents, contents);
            }

            // Reset the page boundary to the crop rectangle.
            PdfArray box = new PdfArray(new PdfPrimitive[]
            {
                new PdfReal(cropBox.X),
                new PdfReal(cropBox.Y),
                new PdfReal(cropBox.Right),
                new PdfReal(cropBox.Top),
            });
            newPage.Set(PdfName.Intern("MediaBox"), box);
            newPage.Set(PdfName.Intern("CropBox"), box);

            _modifiedPages.Add((pageId, newPage));
        }

        private byte[] DecodePageContent(PdfDictionary pageDict)
        {
            FilterPipeline pipeline = FilterRegistry.CreateDefaultPipeline();
            using MemoryStream merged = new MemoryStream();

            if (pageDict.TryGetValue(PdfName.Contents, out PdfPrimitive? contentsPrim))
            {
                PdfPrimitive resolved = _store.Resolve(contentsPrim);
                if (resolved is PdfArray array)
                {
                    foreach (PdfPrimitive item in array)
                    {
                        AppendDecodedStream(_store.Resolve(item), pipeline, merged);
                    }
                }
                else
                {
                    AppendDecodedStream(resolved, pipeline, merged);
                }
            }

            return merged.ToArray();
        }

        private static void AppendDecodedStream(PdfPrimitive? resolved, FilterPipeline pipeline, MemoryStream merged)
        {
            if (resolved is not PdfStream stream)
            {
                return;
            }

            byte[] bytes = DecodeStream(stream, pipeline);
            merged.Write(bytes, 0, bytes.Length);
            merged.WriteByte((byte)'\n');
        }

        private static byte[] DecodeStream(PdfStream stream, FilterPipeline pipeline)
        {
            PdfPrimitive? filter = stream.Filter;
            if (filter is null)
            {
                return stream.RawBytes;
            }

            if (filter is PdfName fn)
            {
                string resolvedFilter = FilterRegistry.ResolveAlias(fn.Value);
                return pipeline.Decode(resolvedFilter, stream.RawBytes, null);
            }

            byte[] data = stream.RawBytes;
            if (filter is PdfArray filterArray)
            {
                foreach (PdfPrimitive f in filterArray)
                {
                    if (f is PdfName n)
                    {
                        string resolvedFilter = FilterRegistry.ResolveAlias(n.Value);
                        data = pipeline.Decode(resolvedFilter, data, null);
                    }
                }
            }

            return data;
        }

        private void AppendExistingContent(PdfDictionary pageDict, PdfArray contents)
        {
            if (!pageDict.TryGetValue(PdfName.Contents, out PdfPrimitive? contentsPrim))
            {
                return;
            }

            PdfPrimitive resolved = _store.Resolve(contentsPrim);
            if (resolved is PdfArray array)
            {
                for (int i = 0; i < array.Count; i++)
                {
                    PdfPrimitive item = array[i];
                    if (item is PdfReference reference)
                    {
                        contents.Add(reference);
                    }
                    else if (_store.Resolve(item) is PdfStream stream)
                    {
                        contents.Add(new PdfReference(MaterializeRawStream(stream)));
                    }
                }
            }
            else if (contentsPrim is PdfReference contentRef)
            {
                contents.Add(contentRef);
            }
            else if (resolved is PdfStream directStream)
            {
                contents.Add(new PdfReference(MaterializeRawStream(directStream)));
            }
        }

        private PdfObjectId MaterializeRawStream(PdfStream stream)
        {
            PdfDictionary dict = ObjectImporter.CopyDictionary(stream.Dictionary);
            PdfObjectId id = NextId();
            _extraObjects.Add(new PdfIndirectObject(id, new PdfStream(dict, stream.RawBytes)));
            return id;
        }

        private PdfObjectId NextStreamFromBytes(byte[] bytes)
        {
            PdfDictionary dict = new PdfDictionary();
            dict.Set(PdfName.Length, bytes.Length);
            PdfObjectId id = NextId();
            _extraObjects.Add(new PdfIndirectObject(id, new PdfStream(dict, bytes)));
            return id;
        }

        // Collects the object numbers of every annotation that SURVIVES in the
        // output (modified pages use their rewritten dictionaries; untouched
        // pages keep their originals), plus those annotations' /AP objects, so
        // shared appearance streams are never omitted with a dropped sibling.
        private HashSet<int> CollectRetainedAnnotationObjects(
            Dictionary<int, PdfObjectId> pageIds)
        {
            HashSet<int> retained = new HashSet<int>();

            Dictionary<int, PdfDictionary> modified = new Dictionary<int, PdfDictionary>();
            foreach ((PdfObjectId id, PdfDictionary dict) in _modifiedPages)
            {
                modified[id.ObjectNumber] = dict;
            }

            for (int i = 0; i < _document.PageCount; i++)
            {
                PdfDictionary pageDict =
                    pageIds.TryGetValue(i, out PdfObjectId pageId)
                        && modified.TryGetValue(pageId.ObjectNumber, out PdfDictionary? replacedPage)
                    ? replacedPage
                    : _document.Pages[i].Dictionary;

                if (!pageDict.TryGetValue(PdfName.Intern("Annots"), out PdfPrimitive? annotsPrim)
                    || _store.Resolve(annotsPrim) is not PdfArray annots)
                {
                    continue;
                }

                foreach (PdfPrimitive item in annots)
                {
                    if (item is PdfReference annotRef)
                    {
                        retained.Add(annotRef.ObjectId.ObjectNumber);
                    }

                    if (_store.Resolve(item) is PdfDictionary annot
                        && annot.TryGetValue(PdfName.Intern("AP"), out PdfPrimitive? apPrim))
                    {
                        if (apPrim is PdfReference apRef)
                        {
                            retained.Add(apRef.ObjectId.ObjectNumber);
                        }

                        if (_store.Resolve(apPrim) is PdfDictionary ap)
                        {
                            foreach (KeyValuePair<PdfName, PdfPrimitive> entry in ap)
                            {
                                if (entry.Value is PdfReference directRef)
                                {
                                    retained.Add(directRef.ObjectId.ObjectNumber);
                                }

                                if (_store.Resolve(entry.Value) is PdfDictionary states)
                                {
                                    foreach (KeyValuePair<PdfName, PdfPrimitive> state in states)
                                    {
                                        if (state.Value is PdfReference stateRef)
                                        {
                                            retained.Add(stateRef.ObjectId.ObjectNumber);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return retained;
        }

        // Removes from /Annots every annotation whose /Rect lies entirely
        // outside the crop box, and records the annotation objects (plus their
        // /AP appearance streams, one level deep) for omission from the output.
        // Annotations overlapping the box, and annotations without a parseable
        // /Rect, are kept (conservative). Dangling references left elsewhere
        // (e.g. AcroForm /Fields) resolve to null per §7.3.10.
        private void FilterAnnotations(PdfDictionary newPage, RectangleF cropBox)
        {
            PdfName annotsName = PdfName.Intern("Annots");
            if (!newPage.TryGetValue(annotsName, out PdfPrimitive? annotsPrim)
                || _store.Resolve(annotsPrim) is not PdfArray annots)
            {
                return;
            }

            PdfArray kept = new PdfArray([]);
            foreach (PdfPrimitive item in annots)
            {
                if (_store.Resolve(item) is not PdfDictionary annot
                    || !IsEntirelyOutside(annot, cropBox))
                {
                    kept.Add(item);
                    continue;
                }

                if (item is PdfReference annotRef)
                {
                    _droppedObjects.Add(annotRef.ObjectId.ObjectNumber);
                }

                CollectAppearanceObjects(annot);
            }

            if (kept.Count == 0)
            {
                newPage.Remove(annotsName);
            }
            else
            {
                newPage.Set(annotsName, kept);
            }
        }

        private bool IsEntirelyOutside(PdfDictionary annot, RectangleF cropBox)
        {
            if (!annot.TryGetValue(PdfName.Intern("Rect"), out PdfPrimitive? rectPrim)
                || _store.Resolve(rectPrim) is not PdfArray rect
                || rect.Count < 4)
            {
                return false;
            }

            double rx0 = PdfReal.ToDouble(_store.Resolve(rect[0]));
            double ry0 = PdfReal.ToDouble(_store.Resolve(rect[1]));
            double rx1 = PdfReal.ToDouble(_store.Resolve(rect[2]));
            double ry1 = PdfReal.ToDouble(_store.Resolve(rect[3]));
            double lowX = Math.Min(rx0, rx1);
            double highX = Math.Max(rx0, rx1);
            double lowY = Math.Min(ry0, ry1);
            double highY = Math.Max(ry0, ry1);

            return highX <= cropBox.X || lowX >= cropBox.Right
                || highY <= cropBox.Y || lowY >= cropBox.Top;
        }

        // Records a dropped annotation's /AP appearance objects (the N/R/D
        // entries, and one level of state sub-dictionaries) for omission.
        private void CollectAppearanceObjects(PdfDictionary annot)
        {
            if (!annot.TryGetValue(PdfName.Intern("AP"), out PdfPrimitive? apPrim)
                || _store.Resolve(apPrim) is not PdfDictionary ap)
            {
                return;
            }

            foreach (KeyValuePair<PdfName, PdfPrimitive> entry in ap)
            {
                if (entry.Value is PdfReference directRef)
                {
                    _droppedObjects.Add(directRef.ObjectId.ObjectNumber);
                }

                // A state dictionary maps appearance-state names to streams.
                if (_store.Resolve(entry.Value) is PdfDictionary states)
                {
                    foreach (KeyValuePair<PdfName, PdfPrimitive> state in states)
                    {
                        if (state.Value is PdfReference stateRef)
                        {
                            _droppedObjects.Add(stateRef.ObjectId.ObjectNumber);
                        }
                    }
                }
            }
        }

        private PdfObjectId FindCatalogId()
        {
            foreach (PdfIndirectObject obj in _document.Objects.Objects)
            {
                if (ReferenceEquals(obj.Value, _document.Catalog))
                {
                    return obj.Id;
                }
            }

            if (_document.Trailer.TryGetValue(PdfName.Root, out PdfPrimitive? rootPrim)
                && rootPrim is PdfReference rootRef)
            {
                return rootRef.ObjectId;
            }

            throw new OperationsException("Document catalog object could not be located.");
        }

        private PdfDictionary BuildTrailer(PdfObjectId catalogId)
        {
            PdfDictionary trailer = new PdfDictionary();
            trailer.Set(PdfName.Root, new PdfReference(catalogId));

            if (_document.Trailer.TryGetValue(PdfName.Info, out PdfPrimitive? infoPrim))
            {
                trailer.Set(PdfName.Info, infoPrim);
            }

            return trailer;
        }

        private PdfObjectId NextId()
        {
            return new PdfObjectId(_nextObjectNumber++, 0);
        }

        // Registers a new stream object and returns a reference to it.
        private PdfReference Allocate(PdfStream stream)
        {
            PdfObjectId id = NextId();
            _extraObjects.Add(new PdfIndirectObject(id, stream));
            return new PdfReference(id);
        }

        // Adds every object number reachable from a primitive (through
        // references, dictionaries, arrays and stream dictionaries). The page's
        // /Parent link is skipped so the walk stays within the page's own graph.
        private void CollectReachable(PdfPrimitive root, HashSet<int> into, bool skipParent)
        {
            Stack<PdfPrimitive> pending = new Stack<PdfPrimitive>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                PdfPrimitive current = pending.Pop();
                switch (current)
                {
                    case PdfReference r:
                        if (into.Add(r.ObjectId.ObjectNumber))
                        {
                            PdfPrimitive target = _store.Resolve(r);
                            if (target is not PdfNull)
                            {
                                pending.Push(target);
                            }
                        }

                        break;
                    case PdfDictionary d:
                        foreach (PdfName key in d.Keys)
                        {
                            if (skipParent && ReferenceEquals(current, root) && key.Value == "Parent")
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
        }

        // Drops scrub candidates that the output no longer reaches from the
        // trailer (catalog and info). Shared objects still used elsewhere —
        // another page, an annotation, the outline — remain reachable and stay.
        private List<PdfIndirectObject> OmitUnreachableCandidates(
            List<PdfIndirectObject> objects, PdfDictionary trailer)
        {
            Dictionary<int, PdfPrimitive> byNumber = new Dictionary<int, PdfPrimitive>();
            foreach (PdfIndirectObject obj in objects)
            {
                byNumber[obj.Id.ObjectNumber] = obj.Value;
            }

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
                        foreach (PdfName key in d.Keys)
                        {
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
                int n = obj.Id.ObjectNumber;
                if (_scrubCandidates.Contains(n) && !reachable.Contains(n))
                {
                    continue;
                }

                kept.Add(obj);
            }

            return kept;
        }
    }
}
