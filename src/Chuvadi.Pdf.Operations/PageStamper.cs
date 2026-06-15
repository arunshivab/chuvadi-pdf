// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.10.1 (form XObjects), §7.8.2 (content streams)
// PHASE: Page composition — stamp a source page onto existing pages.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Operations;

/// <summary>Whether a stamp is drawn over or under the existing page content.</summary>
public enum StampPlacement
{
    /// <summary>Drawn on top of the existing content.</summary>
    Overlay,

    /// <summary>Drawn behind the existing content.</summary>
    Underlay,
}

/// <summary>
/// Stamps a source page onto one or more existing pages of a target document
/// under an affine transform, preserving the rest of the document. The source
/// page is imported once as a form XObject and reused across target pages, so
/// stamping a logo or letterhead onto every page is cheap. Existing content is
/// isolated in its own graphics-state scope so stamps are unaffected by it.
/// </summary>
public static class PageStamper
{
    /// <summary>Stamps a source page onto a single target page.</summary>
    public static void Place(
        Stream output,
        PdfDocument target,
        int targetPageIndex,
        PdfDocument source,
        int sourcePageIndex,
        Transform transform,
        StampPlacement placement = StampPlacement.Overlay)
    {
        Place(output, target, new[] { targetPageIndex }, source, sourcePageIndex, transform, placement);
    }

    /// <summary>Stamps a source page onto every page of the target document.</summary>
    public static void PlaceOnAll(
        Stream output,
        PdfDocument target,
        PdfDocument source,
        int sourcePageIndex,
        Transform transform,
        StampPlacement placement = StampPlacement.Overlay)
    {
        ArgumentNullException.ThrowIfNull(target);

        int count = target.PageCount;
        List<int> all = new List<int>(count);
        for (int i = 0; i < count; i++)
        {
            all.Add(i);
        }

        Place(output, target, all, source, sourcePageIndex, transform, placement);
    }

    /// <summary>Stamps a source page onto a set of target pages.</summary>
    public static void Place(
        Stream output,
        PdfDocument target,
        IReadOnlyList<int> targetPageIndices,
        PdfDocument source,
        int sourcePageIndex,
        Transform transform,
        StampPlacement placement = StampPlacement.Overlay)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(targetPageIndices);

        StampDocument document = new StampDocument(target);

        foreach (int pageIndex in targetPageIndices)
        {
            document.AddStamp(pageIndex, source, sourcePageIndex, transform, placement);
        }

        document.Write(output);
    }

    /// <summary>
    /// Holds a target document plus pending stamps, imports source pages as
    /// form XObjects, and rewrites the affected pages.
    /// </summary>
    private sealed class StampDocument
    {
        private readonly PdfDocument _target;
        private readonly List<PdfIndirectObject> _extraObjects = new List<PdfIndirectObject>();
        private readonly Dictionary<int, List<Stamp>> _pageStamps = new Dictionary<int, List<Stamp>>();
        private readonly Dictionary<(PdfDocument Src, int Index), PdfObjectId> _importedForms =
            new Dictionary<(PdfDocument, int), PdfObjectId>();

        private int _nextObjectNumber;

        internal StampDocument(PdfDocument target)
        {
            _target = target;

            // The object store loads lazily, so force the full graph reachable
            // from the trailer into memory before numbering. This both yields a
            // correct highest-object-number (otherwise new ids would collide
            // with the catalog/pages) and ensures no unloaded object is dropped
            // when the originals are copied in Write.
            List<PdfIndirectObject> loaded = new List<PdfIndirectObject>();
            ObjectImporter.CollectReferences(
                target.Trailer, target.Objects, loaded, new HashSet<int>());

            _nextObjectNumber = 1;
            foreach (PdfIndirectObject obj in target.Objects.Objects)
            {
                if (obj.Id.ObjectNumber >= _nextObjectNumber)
                {
                    _nextObjectNumber = obj.Id.ObjectNumber + 1;
                }
            }

            // Floor at the trailer /Size (highest number + 1) as a safety net.
            if (target.Trailer.TryGetValue(PdfName.Size, out PdfPrimitive? sizePrim)
                && sizePrim is PdfInteger size && size.Value >= _nextObjectNumber)
            {
                _nextObjectNumber = (int)size.Value;
            }
        }

        internal void AddStamp(
            int targetPageIndex,
            PdfDocument source,
            int sourcePageIndex,
            Transform transform,
            StampPlacement placement)
        {
            PdfObjectId formId = ImportForm(source, sourcePageIndex);

            if (!_pageStamps.TryGetValue(targetPageIndex, out List<Stamp>? stamps))
            {
                stamps = new List<Stamp>();
                _pageStamps[targetPageIndex] = stamps;
            }

            stamps.Add(new Stamp(formId, transform, placement));
        }

        // Imports a source page as a form XObject once; subsequent stamps of the
        // same source page reuse the form.
        private PdfObjectId ImportForm(PdfDocument source, int sourcePageIndex)
        {
            (PdfDocument, int) key = (source, sourcePageIndex);
            if (_importedForms.TryGetValue(key, out PdfObjectId existing))
            {
                return existing;
            }

            PdfPage page = source.Pages[sourcePageIndex];
            PdfRectangle box = page.CropBox;
            byte[] body = ObjectImporter.ConcatenatePageContent(page, source.Objects);

            PdfDictionary? resources = page.Resources;
            List<PdfIndirectObject> referenced = new List<PdfIndirectObject>();
            if (resources is not null)
            {
                ObjectImporter.CollectReferences(
                    resources, source.Objects, referenced, new HashSet<int>());
            }

            // Assign new object numbers to the imported objects (single source
            // per import, so a local remap keyed by number is sufficient).
            Dictionary<(int Doc, int Num), int> idRemap = new Dictionary<(int Doc, int Num), int>();
            foreach (PdfIndirectObject refObj in referenced)
            {
                (int, int) refKey = (0, refObj.Id.ObjectNumber);
                if (!idRemap.ContainsKey(refKey))
                {
                    idRemap[refKey] = NextId().ObjectNumber;
                }
            }

            PdfObjectId formId = NextId();
            PdfDictionary formDict = new PdfDictionary();
            formDict.Set(PdfName.Type, PdfName.XObject);
            formDict.Set(PdfName.Subtype, PdfName.Intern("Form"));
            formDict.Set(PdfName.Intern("FormType"), 1);
            formDict.Set(PdfName.Intern("BBox"), new PdfArray([
                new PdfReal(box.X1), new PdfReal(box.Y1),
                new PdfReal(box.X2), new PdfReal(box.Y2)
            ]));
            formDict.Set(PdfName.Intern("Matrix"), new PdfArray([
                new PdfReal(1), new PdfReal(0), new PdfReal(0),
                new PdfReal(1), new PdfReal(0), new PdfReal(0)
            ]));

            if (resources is not null)
            {
                formDict.Set(PdfName.Resources,
                    ObjectImporter.DeepCopyDictionary(resources, 0, idRemap));
            }

            formDict.Set(PdfName.Length, body.Length);
            _extraObjects.Add(new PdfIndirectObject(formId, new PdfStream(formDict, body)));

            HashSet<(int Doc, int Num)> added = new HashSet<(int Doc, int Num)>();
            foreach (PdfIndirectObject refObj in referenced)
            {
                (int, int) refKey = (0, refObj.Id.ObjectNumber);
                if (!added.Add(refKey))
                {
                    continue;
                }

                int newId = idRemap[refKey];
                PdfPrimitive valueCopy =
                    ObjectImporter.DeepCopyPrimitive(refObj.Value, 0, idRemap);
                _extraObjects.Add(new PdfIndirectObject(new PdfObjectId(newId, 0), valueCopy));
            }

            _importedForms[key] = formId;
            return formId;
        }

        internal void Write(Stream output)
        {
            List<PdfIndirectObject> allObjects = new List<PdfIndirectObject>();
            HashSet<int> modifiedPageNumbers = new HashSet<int>();

            Dictionary<int, PdfObjectId> pageIds = BuildPageIdMap();

            foreach (KeyValuePair<int, List<Stamp>> entry in _pageStamps)
            {
                if (!pageIds.TryGetValue(entry.Key, out PdfObjectId pageId))
                {
                    continue;
                }

                PdfPage page = _target.Pages[entry.Key];
                PdfDictionary modified = BuildModifiedPage(page, entry.Value);
                allObjects.Add(new PdfIndirectObject(pageId, modified));
                modifiedPageNumbers.Add(pageId.ObjectNumber);
            }

            foreach (PdfIndirectObject obj in _target.Objects.Objects)
            {
                if (!modifiedPageNumbers.Contains(obj.Id.ObjectNumber))
                {
                    allObjects.Add(obj);
                }
            }

            allObjects.AddRange(_extraObjects);

            PdfWriter.Write(output, allObjects, BuildTrailer());
        }

        private PdfDictionary BuildModifiedPage(PdfPage page, List<Stamp> stamps)
        {
            PdfDictionary pageDict = ObjectImporter.CopyDictionary(page.Dictionary);

            // Resolve existing content stream references (single or array).
            List<PdfPrimitive> existingContent = new List<PdfPrimitive>();
            PdfPrimitive? contents = page.Contents;
            if (contents is not null && contents is not PdfNull)
            {
                PdfPrimitive resolved = _target.Objects.Resolve(contents);
                if (resolved is PdfStream && contents is PdfReference singleRef)
                {
                    existingContent.Add(singleRef);
                }
                else if (resolved is PdfArray array)
                {
                    for (int i = 0; i < array.Count; i++)
                    {
                        existingContent.Add(array[i]);
                    }
                }
            }

            // Resource names for the stamped forms, unique within this page.
            PdfDictionary resources = page.Resources is not null
                ? ObjectImporter.CopyDictionary(page.Resources)
                : new PdfDictionary();
            PdfDictionary xobjects = GetOrCreateSubdict(resources, "XObject");

            PdfArray underlays = new PdfArray([]);
            PdfArray overlays = new PdfArray([]);

            for (int i = 0; i < stamps.Count; i++)
            {
                Stamp stamp = stamps[i];
                string name = "CvStamp" + i.ToString(CultureInfo.InvariantCulture);
                xobjects.Set(PdfName.Intern(name), new PdfReference(stamp.FormId));

                byte[] op = BuildStampOperators(stamp.Transform, name);
                PdfObjectId opId = NextId();
                PdfDictionary opDict = new PdfDictionary();
                opDict.Set(PdfName.Length, op.Length);
                _extraObjects.Add(new PdfIndirectObject(opId, new PdfStream(opDict, op)));

                if (stamp.Placement == StampPlacement.Underlay)
                {
                    underlays.Add(new PdfReference(opId));
                }
                else
                {
                    overlays.Add(new PdfReference(opId));
                }
            }

            resources.Set(PdfName.XObject, xobjects);
            pageDict.Set(PdfName.Resources, resources);

            // Isolate existing content in its own q/Q scope so stamps are not
            // affected by any unbalanced state it leaves behind.
            PdfObjectId saveId = NextStreamFromBytes(Encoding.ASCII.GetBytes("q\n"));
            PdfObjectId restoreId = NextStreamFromBytes(Encoding.ASCII.GetBytes("Q\n"));

            PdfArray newContents = new PdfArray([]);
            for (int i = 0; i < underlays.Count; i++)
            {
                newContents.Add(underlays[i]);
            }

            newContents.Add(new PdfReference(saveId));
            foreach (PdfPrimitive c in existingContent)
            {
                newContents.Add(c);
            }

            newContents.Add(new PdfReference(restoreId));
            for (int i = 0; i < overlays.Count; i++)
            {
                newContents.Add(overlays[i]);
            }

            pageDict.Set(PdfName.Contents, newContents);
            return pageDict;
        }

        private static byte[] BuildStampOperators(Transform t, string name)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("q\n");
            sb.Append(Num(t.A)).Append(' ').Append(Num(t.B)).Append(' ')
                .Append(Num(t.C)).Append(' ').Append(Num(t.D)).Append(' ')
                .Append(Num(t.E)).Append(' ').Append(Num(t.F)).Append(" cm\n");
            sb.Append('/').Append(name).Append(" Do\n");
            sb.Append("Q\n");
            return Encoding.ASCII.GetBytes(sb.ToString());
        }

        private PdfObjectId NextStreamFromBytes(byte[] bytes)
        {
            PdfObjectId id = NextId();
            PdfDictionary dict = new PdfDictionary();
            dict.Set(PdfName.Length, bytes.Length);
            _extraObjects.Add(new PdfIndirectObject(id, new PdfStream(dict, bytes)));
            return id;
        }

        // Walks the page tree by /Kids references so each page index maps to the
        // object id that holds it (robust to nested page trees).
        private Dictionary<int, PdfObjectId> BuildPageIdMap()
        {
            Dictionary<int, PdfObjectId> map = new Dictionary<int, PdfObjectId>();
            int counter = 0;

            PdfDictionary catalog = _target.Catalog;
            if (!catalog.TryGetValue(PdfName.Pages, out PdfPrimitive? pagesPrim))
            {
                return map;
            }

            PdfDictionary? root = _target.Objects.ResolveAs<PdfDictionary>(pagesPrim);
            if (root is not null)
            {
                Walk(root, map, ref counter);
            }

            return map;
        }

        private void Walk(PdfDictionary node, Dictionary<int, PdfObjectId> map, ref int counter)
        {
            if (!node.TryGetValue(PdfName.Kids, out PdfPrimitive? kidsPrim))
            {
                return;
            }

            if (_target.Objects.Resolve(kidsPrim) is not PdfArray kids)
            {
                return;
            }

            for (int i = 0; i < kids.Count; i++)
            {
                if (kids[i] is not PdfReference kidRef)
                {
                    continue;
                }

                PdfPrimitive resolved = _target.Objects.Resolve(kidRef);
                if (resolved is not PdfDictionary kid)
                {
                    continue;
                }

                if (kid.TryGetValue(PdfName.Type, out PdfPrimitive? typePrim)
                    && typePrim is PdfName typeName && typeName.Value == "Pages")
                {
                    Walk(kid, map, ref counter);
                }
                else
                {
                    map[counter++] = kidRef.ObjectId;
                }
            }
        }

        private PdfDictionary BuildTrailer()
        {
            PdfDictionary trailer = new PdfDictionary();
            PdfDictionary catalog = _target.Catalog;

            foreach (PdfIndirectObject obj in _target.Objects.Objects)
            {
                if (ReferenceEquals(obj.Value, catalog))
                {
                    trailer.Set(PdfName.Root, new PdfReference(obj.Id));
                    return trailer;
                }
            }

            // Fall back to the catalog reference recorded in the source trailer.
            if (_target.Trailer.TryGetValue(PdfName.Root, out PdfPrimitive? rootPrim)
                && rootPrim is PdfReference rootRef)
            {
                trailer.Set(PdfName.Root, rootRef);
            }

            return trailer;
        }

        private static PdfDictionary GetOrCreateSubdict(PdfDictionary parent, string key)
        {
            if (parent.TryGetValue(PdfName.Intern(key), out PdfPrimitive? existing)
                && existing is PdfDictionary dict)
            {
                return ObjectImporter.CopyDictionary(dict);
            }

            return new PdfDictionary();
        }

        private PdfObjectId NextId()
        {
            return new PdfObjectId(_nextObjectNumber++, 0);
        }

        private static string Num(double value)
        {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }
    }

    private readonly struct Stamp
    {
        internal Stamp(PdfObjectId formId, Transform transform, StampPlacement placement)
        {
            FormId = formId;
            Transform = transform;
            Placement = placement;
        }

        internal PdfObjectId FormId { get; }

        internal Transform Transform { get; }

        internal StampPlacement Placement { get; }
    }
}
