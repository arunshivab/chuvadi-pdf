// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.5.5 (appearance streams — placement algorithm)
//        PDF 32000-1:2008 §8.10.1 (form XObjects), §12.7.2 (interactive forms)
// PHASE: Document operations — flatten annotations / form fields into content.

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

/// <summary>
/// Flattens annotations and AcroForm field widgets by baking each annotation's
/// normal appearance stream (<c>/AP /N</c>) into the page content as a form
/// XObject, then removing the live annotation. The output looks identical but is
/// static and no longer editable.
/// </summary>
/// <remarks>
/// Each baked appearance is placed per ISO 32000-1 §12.5.5: the appearance's
/// <c>/BBox</c> (transformed by its <c>/Matrix</c>) is mapped onto the
/// annotation's <c>/Rect</c>. Existing page content is preserved byte-for-byte and
/// wrapped in a balanced <c>q … Q</c> so the baked appearances draw at the page's
/// initial coordinate system. Annotations that cannot be baked (no appearance,
/// no <c>/BBox</c>, or an indeterminate appearance state) are left live unless
/// <see cref="AnnotationFlattenOptions.DropRemainingAnnotations"/> is set.
/// </remarks>
public static class AnnotationFlattener
{
    /// <summary>Flattens the document with <see cref="AnnotationFlattenOptions.Default"/> and writes the result.</summary>
    public static void Flatten(Stream output, PdfDocument document) => Flatten(output, document, AnnotationFlattenOptions.Default);

    /// <summary>Flattens the document using <paramref name="options"/> and writes the result to <paramref name="output"/>.</summary>
    public static void Flatten(Stream output, PdfDocument document, AnnotationFlattenOptions options)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        new Worker(document, options).Write(output);
    }

    // ── Implementation ────────────────────────────────────────────────────

    private sealed class Worker
    {
        private readonly PdfDocument _document;
        private readonly AnnotationFlattenOptions _options;
        private readonly PdfObjectStore _store;
        private readonly List<PdfIndirectObject> _extraObjects = new List<PdfIndirectObject>();
        private readonly List<(PdfObjectId Id, PdfDictionary Dict)> _modifiedPages =
            new List<(PdfObjectId, PdfDictionary)>();

        private int _nextObjectNumber;
        private bool _flattenedAnyWidget;
        private bool _keptWidgetLive;

        internal Worker(PdfDocument document, AnnotationFlattenOptions options)
        {
            _document = document;
            _options = options;
            _store = document.Objects;

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

            for (int i = 0; i < _document.PageCount; i++)
            {
                if (pageIds.TryGetValue(i, out PdfObjectId pageId))
                {
                    ProcessPage(i, pageId);
                }
            }

            PdfDictionary? modifiedCatalog = null;
            if (ShouldRemoveAcroForm())
            {
                modifiedCatalog = ObjectImporter.CopyDictionary(_document.Catalog);
                modifiedCatalog.Remove(PdfName.Intern("AcroForm"));
            }

            List<PdfIndirectObject> all = new List<PdfIndirectObject>();
            HashSet<int> replaced = new HashSet<int>();

            foreach ((PdfObjectId id, PdfDictionary dict) in _modifiedPages)
            {
                all.Add(new PdfIndirectObject(id, dict));
                replaced.Add(id.ObjectNumber);
            }

            if (modifiedCatalog is not null)
            {
                all.Add(new PdfIndirectObject(catalogId, modifiedCatalog));
                replaced.Add(catalogId.ObjectNumber);
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

        private bool ShouldRemoveAcroForm()
        {
            return (_options.Kinds & AnnotationFlattenKinds.FormFields) != 0
                && _options.RemoveAcroForm
                && _flattenedAnyWidget
                && !_keptWidgetLive
                && _document.Catalog.ContainsKey(PdfName.Intern("AcroForm"));
        }

        private void ProcessPage(int pageIndex, PdfObjectId pageId)
        {
            PdfPage page = _document.Pages[pageIndex];
            PdfDictionary pageDict = page.Dictionary;

            if (!pageDict.TryGetValue(PdfName.Intern("Annots"), out PdfPrimitive? annotsPrim))
            {
                return;
            }

            PdfArray? annots = _store.ResolveAs<PdfArray>(annotsPrim ?? PdfNull.Value);
            if (annots is null || annots.Count == 0)
            {
                return;
            }

            List<PdfPrimitive> kept = new List<PdfPrimitive>();
            StringBuilder overlay = new StringBuilder();
            List<(string Name, PdfReference Ref)> xobjectAdds = new List<(string, PdfReference)>();
            List<(string Name, PdfDictionary Gs)> extGStateAdds = new List<(string, PdfDictionary)>();
            HashSet<string> xobjectNames = CollectResourceNames(page, PdfName.XObject);
            HashSet<string> extGStateNames = CollectResourceNames(page, PdfName.Intern("ExtGState"));
            int xobjectCounter = 0;
            int extGStateCounter = 0;
            bool changed = false;

            for (int i = 0; i < annots.Count; i++)
            {
                PdfPrimitive entry = annots[i];
                PdfDictionary? annot = _store.ResolveAs<PdfDictionary>(entry);
                if (annot is null)
                {
                    kept.Add(entry);
                    continue;
                }

                PdfName? subtype = annot.GetName(PdfName.Subtype);
                bool isWidget = subtype is not null && subtype.Value == "Widget";
                bool selected = isWidget
                    ? (_options.Kinds & AnnotationFlattenKinds.FormFields) != 0
                    : (_options.Kinds & AnnotationFlattenKinds.Markup) != 0;

                if (!selected)
                {
                    if (_options.DropRemainingAnnotations)
                    {
                        changed = true;
                    }
                    else
                    {
                        kept.Add(entry);
                        if (isWidget)
                        {
                            _keptWidgetLive = true;
                        }
                    }

                    continue;
                }

                int flags = annot.GetInteger(PdfName.Intern("F"), 0);
                bool invisible = (flags & 0x2) != 0 || (flags & 0x20) != 0;
                if (_options.SkipHiddenAndNoView && invisible)
                {
                    changed = true;
                    continue;
                }

                PdfStream? appearance = ResolveNormalAppearance(annot);
                if (appearance is null
                    || !TryReadRect(annot, out double rx0, out double ry0, out double rx1, out double ry1)
                    || !TryComputePlacement(appearance, rx0, ry0, rx1, ry1, out double sx, out double sy, out double tx, out double ty))
                {
                    if (_options.DropRemainingAnnotations)
                    {
                        changed = true;
                    }
                    else
                    {
                        kept.Add(entry);
                        if (isWidget)
                        {
                            _keptWidgetLive = true;
                        }
                    }

                    continue;
                }

                PdfObjectId formId = MaterializeForm(appearance);
                string formName = NextName("CvFlatAp", xobjectNames, ref xobjectCounter);
                xobjectAdds.Add((formName, new PdfReference(formId)));

                double alpha = ReadOpacity(annot);
                string? gsName = null;
                if (alpha < 1.0)
                {
                    gsName = NextName("CvFlatGs", extGStateNames, ref extGStateCounter);
                    extGStateAdds.Add((gsName, MakeAlphaGraphicsState(alpha)));
                }

                overlay.Append("q\n");
                if (gsName is not null)
                {
                    overlay.Append('/').Append(gsName).Append(" gs\n");
                }

                overlay.Append(Fmt(sx)).Append(" 0 0 ").Append(Fmt(sy)).Append(' ')
                    .Append(Fmt(tx)).Append(' ').Append(Fmt(ty)).Append(" cm\n");
                overlay.Append('/').Append(formName).Append(" Do\n");
                overlay.Append("Q\n");

                if (isWidget)
                {
                    _flattenedAnyWidget = true;
                }

                changed = true;
            }

            if (!changed)
            {
                return;
            }

            PdfDictionary newPage = ObjectImporter.CopyDictionary(pageDict);
            if (kept.Count == 0)
            {
                newPage.Remove(PdfName.Intern("Annots"));
            }
            else
            {
                newPage.Set(PdfName.Intern("Annots"), new PdfArray(kept));
            }

            if (overlay.Length > 0)
            {
                PdfDictionary resources = page.Resources is not null
                    ? ObjectImporter.CopyDictionary(page.Resources)
                    : new PdfDictionary();
                MergeResourceSubDictionary(resources, PdfName.XObject, xobjectAdds);
                if (extGStateAdds.Count > 0)
                {
                    MergeExtGStates(resources, extGStateAdds);
                }

                newPage.Set(PdfName.Resources, resources);

                PdfArray contents = new PdfArray([]);
                PdfObjectId guardId = NextStreamFromBytes(Encoding.Latin1.GetBytes("q\n"));
                contents.Add(new PdfReference(guardId));
                AppendExistingContent(pageDict, contents);
                byte[] overlayBytes = Encoding.Latin1.GetBytes("Q\n" + overlay.ToString());
                PdfObjectId overlayId = NextStreamFromBytes(overlayBytes);
                contents.Add(new PdfReference(overlayId));
                newPage.Set(PdfName.Contents, contents);
            }

            _modifiedPages.Add((pageId, newPage));
        }

        // ── Appearance resolution ─────────────────────────────────────────

        private PdfStream? ResolveNormalAppearance(PdfDictionary annot)
        {
            if (!annot.TryGetValue(PdfName.Intern("AP"), out PdfPrimitive? apPrim))
            {
                return null;
            }

            PdfDictionary? appearanceDict = _store.ResolveAs<PdfDictionary>(apPrim ?? PdfNull.Value);
            if (appearanceDict is null
                || !appearanceDict.TryGetValue(PdfName.Intern("N"), out PdfPrimitive? normalPrim))
            {
                return null;
            }

            PdfPrimitive normal = _store.Resolve(normalPrim);
            if (normal is PdfStream stream)
            {
                return stream;
            }

            if (normal is PdfDictionary states)
            {
                PdfName? appearanceState = annot.GetName(PdfName.Intern("AS"));
                if (appearanceState is not null && states.TryGetValue(appearanceState, out PdfPrimitive? selectedState))
                {
                    return _store.ResolveAs<PdfStream>(selectedState);
                }

                if (states.TryGetValue(PdfName.Intern("Off"), out PdfPrimitive? offState))
                {
                    return _store.ResolveAs<PdfStream>(offState);
                }

                if (states.Count == 1)
                {
                    foreach (PdfPrimitive only in states.Values)
                    {
                        return _store.ResolveAs<PdfStream>(only);
                    }
                }
            }

            return null;
        }

        // ── Placement (ISO 32000-1 §12.5.5) ───────────────────────────────

        private bool TryComputePlacement(
            PdfStream appearance, double rx0, double ry0, double rx1, double ry1,
            out double sx, out double sy, out double tx, out double ty)
        {
            sx = sy = tx = ty = 0;

            PdfDictionary form = appearance.Dictionary;
            if (!form.TryGetValue(PdfName.Intern("BBox"), out PdfPrimitive? bboxPrim))
            {
                return false;
            }

            PdfArray? bbox = _store.ResolveAs<PdfArray>(bboxPrim ?? PdfNull.Value);
            if (bbox is null || bbox.Count < 4
                || !TryNumber(bbox[0], out double bx0) || !TryNumber(bbox[1], out double by0)
                || !TryNumber(bbox[2], out double bx1) || !TryNumber(bbox[3], out double by1))
            {
                return false;
            }

            Transform matrix = ReadMatrix(form);
            PointF c0 = matrix.TransformPoint(new PointF(bx0, by0));
            PointF c1 = matrix.TransformPoint(new PointF(bx1, by0));
            PointF c2 = matrix.TransformPoint(new PointF(bx1, by1));
            PointF c3 = matrix.TransformPoint(new PointF(bx0, by1));

            double minX = Math.Min(Math.Min(c0.X, c1.X), Math.Min(c2.X, c3.X));
            double maxX = Math.Max(Math.Max(c0.X, c1.X), Math.Max(c2.X, c3.X));
            double minY = Math.Min(Math.Min(c0.Y, c1.Y), Math.Min(c2.Y, c3.Y));
            double maxY = Math.Max(Math.Max(c0.Y, c1.Y), Math.Max(c2.Y, c3.Y));

            double transformedWidth = maxX - minX;
            double transformedHeight = maxY - minY;
            if (transformedWidth <= 0 || transformedHeight <= 0)
            {
                return false;
            }

            sx = (rx1 - rx0) / transformedWidth;
            sy = (ry1 - ry0) / transformedHeight;
            tx = rx0 - (sx * minX);
            ty = ry0 - (sy * minY);
            return true;
        }

        private Transform ReadMatrix(PdfDictionary form)
        {
            if (form.TryGetValue(PdfName.Intern("Matrix"), out PdfPrimitive? matrixPrim)
                && _store.ResolveAs<PdfArray>(matrixPrim ?? PdfNull.Value) is PdfArray matrix
                && matrix.Count >= 6
                && TryNumber(matrix[0], out double a) && TryNumber(matrix[1], out double b)
                && TryNumber(matrix[2], out double c) && TryNumber(matrix[3], out double d)
                && TryNumber(matrix[4], out double e) && TryNumber(matrix[5], out double f))
            {
                return new Transform(a, b, c, d, e, f);
            }

            return Transform.Identity;
        }

        private bool TryReadRect(PdfDictionary annot, out double x0, out double y0, out double x1, out double y1)
        {
            x0 = y0 = x1 = y1 = 0;

            if (!annot.TryGetValue(PdfName.Intern("Rect"), out PdfPrimitive? rectPrim))
            {
                return false;
            }

            PdfArray? rect = _store.ResolveAs<PdfArray>(rectPrim ?? PdfNull.Value);
            if (rect is null || rect.Count < 4
                || !TryNumber(rect[0], out double a) || !TryNumber(rect[1], out double b)
                || !TryNumber(rect[2], out double c) || !TryNumber(rect[3], out double d))
            {
                return false;
            }

            x0 = Math.Min(a, c);
            y0 = Math.Min(b, d);
            x1 = Math.Max(a, c);
            y1 = Math.Max(b, d);
            return x1 > x0 && y1 > y0;
        }

        private double ReadOpacity(PdfDictionary annot)
        {
            if (annot.TryGetValue(PdfName.Intern("CA"), out PdfPrimitive? caPrim) && TryNumber(caPrim, out double ca))
            {
                return ca;
            }

            return 1.0;
        }

        private bool TryNumber(PdfPrimitive primitive, out double value)
        {
            PdfPrimitive resolved = _store.Resolve(primitive);
            switch (resolved)
            {
                case PdfReal real:
                    value = real.Value;
                    return true;
                case PdfInteger integer:
                    value = integer.Value;
                    return true;
                default:
                    value = 0;
                    return false;
            }
        }

        // ── Object construction ───────────────────────────────────────────

        private PdfObjectId MaterializeForm(PdfStream appearance)
        {
            PdfDictionary form = ObjectImporter.CopyDictionary(appearance.Dictionary);
            form.Set(PdfName.Type, PdfName.XObject);
            form.Set(PdfName.Subtype, PdfName.Intern("Form"));
            if (!form.ContainsKey(PdfName.Intern("FormType")))
            {
                form.Set(PdfName.Intern("FormType"), 1);
            }

            form.Set(PdfName.Length, appearance.RawBytes.Length);

            PdfObjectId id = NextId();
            _extraObjects.Add(new PdfIndirectObject(id, new PdfStream(form, appearance.RawBytes)));
            return id;
        }

        private PdfObjectId MaterializeRawStream(PdfStream stream)
        {
            PdfDictionary dict = ObjectImporter.CopyDictionary(stream.Dictionary);
            dict.Set(PdfName.Length, stream.RawBytes.Length);

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

        private static PdfDictionary MakeAlphaGraphicsState(double alpha)
        {
            PdfDictionary gs = new PdfDictionary();
            gs.Set(PdfName.Type, PdfName.Intern("ExtGState"));
            gs.Set(PdfName.Intern("ca"), new PdfReal(alpha));
            gs.Set(PdfName.Intern("CA"), new PdfReal(alpha));
            return gs;
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

        // ── Resource merging ──────────────────────────────────────────────

        private void MergeResourceSubDictionary(
            PdfDictionary resources, PdfName key, List<(string Name, PdfReference Ref)> additions)
        {
            PdfDictionary sub = resources.TryGetValue(key, out PdfPrimitive? existing)
                && _store.ResolveAs<PdfDictionary>(existing) is PdfDictionary current
                ? ObjectImporter.CopyDictionary(current)
                : new PdfDictionary();

            foreach ((string name, PdfReference reference) in additions)
            {
                sub.Set(PdfName.Intern(name), reference);
            }

            resources.Set(key, sub);
        }

        private void MergeExtGStates(
            PdfDictionary resources, List<(string Name, PdfDictionary Gs)> additions)
        {
            PdfName key = PdfName.Intern("ExtGState");
            PdfDictionary sub = resources.TryGetValue(key, out PdfPrimitive? existing)
                && _store.ResolveAs<PdfDictionary>(existing) is PdfDictionary current
                ? ObjectImporter.CopyDictionary(current)
                : new PdfDictionary();

            foreach ((string name, PdfDictionary gs) in additions)
            {
                sub.Set(PdfName.Intern(name), gs);
            }

            resources.Set(key, sub);
        }

        private HashSet<string> CollectResourceNames(PdfPage page, PdfName key)
        {
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            if (page.Resources is PdfDictionary resources
                && resources.TryGetValue(key, out PdfPrimitive? sub)
                && _store.ResolveAs<PdfDictionary>(sub) is PdfDictionary entries)
            {
                foreach (PdfName name in entries.Keys)
                {
                    names.Add(name.Value);
                }
            }

            return names;
        }

        private static string NextName(string prefix, HashSet<string> reserved, ref int counter)
        {
            string name;
            do
            {
                name = prefix + counter.ToString(CultureInfo.InvariantCulture);
                counter++;
            }
            while (reserved.Contains(name));

            reserved.Add(name);
            return name;
        }

        // ── Trailer / catalog ─────────────────────────────────────────────

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

        private static string Fmt(double value)
        {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }
    }
}
