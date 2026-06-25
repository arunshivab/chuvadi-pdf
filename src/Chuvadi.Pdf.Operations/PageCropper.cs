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
        private int _nextObjectNumber;

        internal Worker(PdfDocument document, IReadOnlyList<PageCrop> crops)
        {
            _document = document;
            _store = document.Objects;

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

            foreach (PdfIndirectObject obj in _document.Objects.Objects)
            {
                if (!replaced.Contains(obj.Id.ObjectNumber))
                {
                    all.Add(obj);
                }
            }

            all.AddRange(_extraObjects);
            PdfWriter.Write(output, all, BuildTrailer(catalogId));
        }

        private void ProcessPage(int pageIndex, PdfObjectId pageId, PageCrop crop)
        {
            RectangleF cropBox = crop.CropBox;
            PdfDictionary pageDict = _document.Pages[pageIndex].Dictionary;
            PdfDictionary newPage = ObjectImporter.CopyDictionary(pageDict);

            if (crop.Mode == PageCropMode.Scrub)
            {
                byte[] decoded = DecodePageContent(pageDict);
                PdfPrimitive? resPrim = pageDict.GetAs<PdfPrimitive>(PdfName.Resources);
                PdfDictionary? resources = resPrim is null ? null : _store.Resolve(resPrim) as PdfDictionary;
                PageScrubber.ScrubResult result = PageScrubber.Scrub(
                    decoded, cropBox.X, cropBox.Y, cropBox.Right, cropBox.Top, resources, _store);

                if (result.NewXObjects.Count > 0)
                {
                    RegisterXObjects(newPage, resources, result.NewXObjects);
                }

                PdfArray scrubContents = new PdfArray([]);
                scrubContents.Add(new PdfReference(NextStreamFromBytes(result.Content)));
                newPage.Set(PdfName.Contents, scrubContents);
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

        private void RegisterXObjects(
            PdfDictionary newPage, PdfDictionary? resources,
            List<(string Name, PdfStream Stream)> xobjs)
        {
            PdfDictionary resDict = resources is null
                ? new PdfDictionary()
                : ObjectImporter.CopyDictionary(resources);

            PdfPrimitive? xobjPrim = resDict.GetAs<PdfPrimitive>(PdfName.Intern("XObject"));
            PdfDictionary xobjDict =
                (xobjPrim is not null && _store.Resolve(xobjPrim) is PdfDictionary existing)
                    ? ObjectImporter.CopyDictionary(existing)
                    : new PdfDictionary();

            foreach ((string name, PdfStream stream) in xobjs)
            {
                PdfObjectId id = NextId();
                _extraObjects.Add(new PdfIndirectObject(id, stream));
                xobjDict.Set(PdfName.Intern(name), new PdfReference(id));
            }

            resDict.Set(PdfName.Intern("XObject"), xobjDict);
            newPage.Set(PdfName.Resources, resDict);
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
    }
}
