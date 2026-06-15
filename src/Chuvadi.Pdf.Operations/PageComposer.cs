// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.10.1 (form XObjects), §8.3.4 (CTM), §7.7.3 (pages)
// PHASE: Page composition — build a new document by placing source pages.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// Builds a new PDF by placing pages from existing documents onto target
/// sheets under arbitrary affine transforms. Each placed page is imported as a
/// form XObject, so vector and text content stay intact and selectable (not
/// rasterised). One <see cref="PlacePage"/> per sheet covers rotate-any-angle
/// and resize; several per sheet cover N-up and imposition.
/// </summary>
/// <remarks>
/// The supplied <see cref="Transform"/> maps the source page's coordinate
/// space to the target sheet's coordinate space (PDF default user space,
/// origin bottom-left). The source page's content is placed in its native
/// (un-rotated) coordinates; use <see cref="PdfPage.EffectiveSize"/> to size
/// and position against the page as displayed.
/// </remarks>
public sealed class PageComposer
{
    private readonly List<TargetPage> _pages = new List<TargetPage>();
    private readonly List<PdfDocument> _sourceDocs = new List<PdfDocument>();

    /// <summary>Adds a blank target sheet of a standard or custom size.</summary>
    public PageComposer AddPage(PageSize size)
    {
        return AddPage(size.Width, size.Height);
    }

    /// <summary>Adds a blank target sheet of arbitrary dimensions (points).</summary>
    public PageComposer AddPage(double width, double height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width), "Page dimensions must be positive.");
        }

        _pages.Add(new TargetPage(width, height));
        return this;
    }

    /// <summary>
    /// Adds a blank target sheet sized to a source page's displayed size
    /// (crop box, accounting for <see cref="PdfPage.Rotate"/>).
    /// </summary>
    public PageComposer AddPageMatching(PdfDocument source, int sourcePageIndex)
    {
        ArgumentNullException.ThrowIfNull(source);

        PdfPage page = source.Pages[sourcePageIndex];
        (double width, double height) = page.EffectiveSize;
        return AddPage(width, height);
    }

    /// <summary>
    /// Places a source page onto the current target sheet under the given
    /// transform. Call repeatedly to compose several pages onto one sheet.
    /// </summary>
    public PageComposer PlacePage(PdfDocument source, int sourcePageIndex, Transform transform)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (_pages.Count == 0)
        {
            throw new InvalidOperationException(
                "Call AddPage before PlacePage — there is no target sheet yet.");
        }

        TargetPage target = _pages[_pages.Count - 1];
        PdfPage page = source.Pages[sourcePageIndex];
        int docIndex = SourceDocIndex(source);

        PdfRectangle box = page.CropBox;
        byte[] body = ObjectImporter.ConcatenatePageContent(page, source.Objects);

        PdfDictionary? resources = page.Resources;
        List<PdfIndirectObject> referenced = new List<PdfIndirectObject>();
        if (resources is not null)
        {
            ObjectImporter.CollectReferences(
                resources, source.Objects, referenced, new HashSet<int>());
        }

        string name = "Fm" + target.Forms.Count.ToString(CultureInfo.InvariantCulture);
        target.Forms.Add(new PlacedForm(name, box, body, resources, docIndex, referenced));

        // q <a b c d e f> cm /Fm Do Q  — paint the imported page under the CTM.
        target.Content.Append("q\n");
        target.Content.Append(Num(transform.A)).Append(' ').Append(Num(transform.B)).Append(' ')
            .Append(Num(transform.C)).Append(' ').Append(Num(transform.D)).Append(' ')
            .Append(Num(transform.E)).Append(' ').Append(Num(transform.F)).Append(" cm\n");
        target.Content.Append('/').Append(name).Append(" Do\n");
        target.Content.Append("Q\n");

        return this;
    }

    /// <summary>Writes the composed document to <paramref name="output"/>.</summary>
    public void Write(Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (_pages.Count == 0)
        {
            throw new InvalidOperationException("No pages to write — call AddPage first.");
        }

        List<PdfIndirectObject> allObjects = new List<PdfIndirectObject>();

        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        int nextId = 3;

        // Object ids: pages, then content streams, then forms.
        List<PdfObjectId> pageIds = new List<PdfObjectId>();
        foreach (TargetPage unused in _pages)
        {
            pageIds.Add(new PdfObjectId(nextId++, 0));
        }

        List<PdfObjectId> contentIds = new List<PdfObjectId>();
        foreach (TargetPage unused in _pages)
        {
            contentIds.Add(new PdfObjectId(nextId++, 0));
        }

        Dictionary<PlacedForm, PdfObjectId> formIds = new Dictionary<PlacedForm, PdfObjectId>();
        foreach (TargetPage page in _pages)
        {
            foreach (PlacedForm form in page.Forms)
            {
                formIds[form] = new PdfObjectId(nextId++, 0);
            }
        }

        // Remap imported referenced objects, keyed by (source document, number)
        // so distinct objects that share a number across documents stay distinct.
        Dictionary<(int Doc, int Num), int> idRemap = new Dictionary<(int Doc, int Num), int>();
        foreach (TargetPage page in _pages)
        {
            foreach (PlacedForm form in page.Forms)
            {
                foreach (PdfIndirectObject refObj in form.Referenced)
                {
                    (int, int) key = (form.SourceDocIndex, refObj.Id.ObjectNumber);
                    if (!idRemap.ContainsKey(key))
                    {
                        idRemap[key] = nextId++;
                    }
                }
            }
        }

        // Pages root.
        PdfArray kids = new PdfArray([]);
        foreach (PdfObjectId pid in pageIds)
        {
            kids.Add(new PdfReference(pid));
        }

        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, kids);
        pagesDict.Set(PdfName.Count, _pages.Count);
        allObjects.Add(new PdfIndirectObject(pagesId, pagesDict));

        // Page objects + their content streams + per-page XObject resources.
        for (int i = 0; i < _pages.Count; i++)
        {
            TargetPage page = _pages[i];

            byte[] contentBytes = Encoding.Latin1.GetBytes(page.Content.ToString());
            PdfDictionary contentDict = new PdfDictionary();
            contentDict.Set(PdfName.Length, contentBytes.Length);
            allObjects.Add(new PdfIndirectObject(
                contentIds[i], new PdfStream(contentDict, contentBytes)));

            PdfDictionary xobjects = new PdfDictionary();
            foreach (PlacedForm form in page.Forms)
            {
                xobjects.Set(PdfName.Intern(form.Name), new PdfReference(formIds[form]));
            }

            PdfDictionary resources = new PdfDictionary();
            resources.Set(PdfName.XObject, xobjects);

            PdfDictionary pageDict = new PdfDictionary();
            pageDict.Set(PdfName.Type, PdfName.Page);
            pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
            pageDict.Set(PdfName.MediaBox, new PdfArray([
                new PdfReal(0), new PdfReal(0),
                new PdfReal(page.Width), new PdfReal(page.Height)
            ]));
            pageDict.Set(PdfName.Contents, new PdfReference(contentIds[i]));
            pageDict.Set(PdfName.Resources, resources);
            allObjects.Add(new PdfIndirectObject(pageIds[i], pageDict));
        }

        // Form XObjects.
        foreach (TargetPage page in _pages)
        {
            foreach (PlacedForm form in page.Forms)
            {
                PdfDictionary formDict = new PdfDictionary();
                formDict.Set(PdfName.Type, PdfName.XObject);
                formDict.Set(PdfName.Subtype, PdfName.Intern("Form"));
                formDict.Set(PdfName.Intern("FormType"), 1);
                formDict.Set(PdfName.Intern("BBox"), new PdfArray([
                    new PdfReal(form.BBox.X1), new PdfReal(form.BBox.Y1),
                    new PdfReal(form.BBox.X2), new PdfReal(form.BBox.Y2)
                ]));
                formDict.Set(PdfName.Intern("Matrix"), new PdfArray([
                    new PdfReal(1), new PdfReal(0), new PdfReal(0),
                    new PdfReal(1), new PdfReal(0), new PdfReal(0)
                ]));

                if (form.SourceResources is not null)
                {
                    formDict.Set(PdfName.Resources, ObjectImporter.DeepCopyDictionary(
                        form.SourceResources, form.SourceDocIndex, idRemap));
                }

                formDict.Set(PdfName.Length, form.Body.Length);
                allObjects.Add(new PdfIndirectObject(
                    formIds[form], new PdfStream(formDict, form.Body)));
            }
        }

        // Imported referenced objects (deduplicated by remap key).
        HashSet<(int Doc, int Num)> added = new HashSet<(int Doc, int Num)>();
        foreach (TargetPage page in _pages)
        {
            foreach (PlacedForm form in page.Forms)
            {
                foreach (PdfIndirectObject refObj in form.Referenced)
                {
                    (int, int) key = (form.SourceDocIndex, refObj.Id.ObjectNumber);
                    if (!added.Add(key))
                    {
                        continue;
                    }

                    int newId = idRemap[key];
                    PdfPrimitive valueCopy = ObjectImporter.DeepCopyPrimitive(
                        refObj.Value, form.SourceDocIndex, idRemap);
                    allObjects.Add(new PdfIndirectObject(new PdfObjectId(newId, 0), valueCopy));
                }
            }
        }

        PdfDictionary catalogDict = new PdfDictionary();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));
        allObjects.Add(new PdfIndirectObject(catalogId, catalogDict));

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        PdfWriter.Write(output, allObjects, trailer);
    }

    private int SourceDocIndex(PdfDocument source)
    {
        for (int i = 0; i < _sourceDocs.Count; i++)
        {
            if (ReferenceEquals(_sourceDocs[i], source))
            {
                return i;
            }
        }

        _sourceDocs.Add(source);
        return _sourceDocs.Count - 1;
    }

    private static string Num(double value)
    {
        return value.ToString("0.######", CultureInfo.InvariantCulture);
    }

    private sealed class TargetPage
    {
        internal TargetPage(double width, double height)
        {
            Width = width;
            Height = height;
        }

        internal double Width { get; }

        internal double Height { get; }

        internal StringBuilder Content { get; } = new StringBuilder();

        internal List<PlacedForm> Forms { get; } = new List<PlacedForm>();
    }

    private sealed class PlacedForm
    {
        internal PlacedForm(
            string name,
            PdfRectangle bbox,
            byte[] body,
            PdfDictionary? sourceResources,
            int sourceDocIndex,
            List<PdfIndirectObject> referenced)
        {
            Name = name;
            BBox = bbox;
            Body = body;
            SourceResources = sourceResources;
            SourceDocIndex = sourceDocIndex;
            Referenced = referenced;
        }

        internal string Name { get; }

        internal PdfRectangle BBox { get; }

        internal byte[] Body { get; }

        internal PdfDictionary? SourceResources { get; }

        internal int SourceDocIndex { get; }

        internal List<PdfIndirectObject> Referenced { get; }
    }
}
