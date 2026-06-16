// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.10.1 (form XObjects), §11.6.4.4 (/ca), §7.8.2 (content)
// PHASE: Document operations — wrap and re-render existing page content.

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
/// Rewrites selected pages by wrapping their existing content in a form XObject
/// that can be painted under an arbitrary transform and constant-alpha, with an
/// optional background fill drawn first. This is the shared engine behind page
/// recolouring (<see cref="PageOverlay"/>) and header/footer banding
/// (<see cref="HeaderFooter"/>), where existing content must be scaled to free
/// a band. Pages not edited are preserved byte-for-byte.
/// </summary>
internal sealed class PageContentEditor
{
    private readonly PdfDocument _document;
    private readonly List<PdfIndirectObject> _extraObjects = new List<PdfIndirectObject>();
    private readonly Dictionary<int, PageEdit> _edits = new Dictionary<int, PageEdit>();
    private int _nextObjectNumber;

    internal PageContentEditor(PdfDocument document)
    {
        _document = document;

        List<PdfIndirectObject> loaded = new List<PdfIndirectObject>();
        ObjectImporter.CollectReferences(
            document.Trailer, document.Objects, loaded, new HashSet<int>());

        _nextObjectNumber = 1;
        foreach (PdfIndirectObject obj in document.Objects.Objects)
        {
            if (obj.Id.ObjectNumber >= _nextObjectNumber)
            {
                _nextObjectNumber = obj.Id.ObjectNumber + 1;
            }
        }

        if (document.Trailer.TryGetValue(PdfName.Size, out PdfPrimitive? sizePrim)
            && sizePrim is PdfInteger size && size.Value >= _nextObjectNumber)
        {
            _nextObjectNumber = (int)size.Value;
        }
    }

    /// <summary>
    /// Recolours a page: optional background fill behind content drawn at the
    /// given opacity, content otherwise unchanged in position.
    /// </summary>
    internal void Recolor(int pageIndex, ColorF? background, float contentOpacity)
    {
        PageEdit edit = GetOrCreate(pageIndex);
        edit.Background = background;
        edit.ContentOpacity = contentOpacity;
        edit.ContentTransform = Transform.Identity;
    }

    /// <summary>
    /// Applies a transform to the existing content (used to scale/shift content
    /// to free header/footer bands) and queues overlay content streams drawn on
    /// top (e.g. header/footer text) in their own graphics-state scope.
    /// </summary>
    internal void TransformAndOverlay(
        int pageIndex,
        Transform contentTransform,
        ColorF? background,
        IReadOnlyList<byte[]> overlayStreams)
    {
        PageEdit edit = GetOrCreate(pageIndex);
        edit.Background = background;
        edit.ContentOpacity = 1f;
        edit.ContentTransform = contentTransform;
        edit.Overlays.AddRange(overlayStreams);
    }

    /// <summary>Adds extra resource entries (fonts, ExtGState) to a page's overlay scope.</summary>
    internal void AddOverlayFontResource(int pageIndex, string resourceName, PdfDictionary fontDict)
    {
        PageEdit edit = GetOrCreate(pageIndex);
        edit.OverlayFonts[resourceName] = fontDict;
    }

    private PageEdit GetOrCreate(int pageIndex)
    {
        if (!_edits.TryGetValue(pageIndex, out PageEdit? edit))
        {
            edit = new PageEdit();
            _edits[pageIndex] = edit;
        }

        return edit;
    }

    internal void Write(Stream output)
    {
        List<PdfIndirectObject> allObjects = new List<PdfIndirectObject>();
        HashSet<int> modifiedPageNumbers = new HashSet<int>();

        Dictionary<int, PdfObjectId> pageIds = PageTree.BuildIndexToIdMap(_document);

        foreach (KeyValuePair<int, PageEdit> entry in _edits)
        {
            if (!pageIds.TryGetValue(entry.Key, out PdfObjectId pageId))
            {
                continue;
            }

            PdfPage page = _document.Pages[entry.Key];
            PdfDictionary modified = BuildModifiedPage(page, entry.Value);
            allObjects.Add(new PdfIndirectObject(pageId, modified));
            modifiedPageNumbers.Add(pageId.ObjectNumber);
        }

        foreach (PdfIndirectObject obj in _document.Objects.Objects)
        {
            if (!modifiedPageNumbers.Contains(obj.Id.ObjectNumber))
            {
                allObjects.Add(obj);
            }
        }

        allObjects.AddRange(_extraObjects);

        PdfWriter.Write(output, allObjects, BuildTrailer());
    }

    private PdfDictionary BuildModifiedPage(PdfPage page, PageEdit edit)
    {
        PdfDictionary pageDict = ObjectImporter.CopyDictionary(page.Dictionary);
        PdfRectangle mediaBox = page.MediaBox;

        // Wrap existing content as a form XObject so it can be transformed and
        // faded without disturbing the operators inside it.
        byte[] contentBytes = ObjectImporter.ConcatenatePageContent(page, _document.Objects);
        PdfObjectId formId = NextId();

        PdfDictionary formDict = new PdfDictionary();
        formDict.Set(PdfName.Type, PdfName.XObject);
        formDict.Set(PdfName.Subtype, PdfName.Intern("Form"));
        formDict.Set(PdfName.Intern("FormType"), 1);
        formDict.Set(PdfName.Intern("BBox"), new PdfArray([
            new PdfReal(mediaBox.X1), new PdfReal(mediaBox.Y1),
            new PdfReal(mediaBox.X2), new PdfReal(mediaBox.Y2)
        ]));
        formDict.Set(PdfName.Intern("Matrix"), new PdfArray([
            new PdfReal(1), new PdfReal(0), new PdfReal(0),
            new PdfReal(1), new PdfReal(0), new PdfReal(0)
        ]));

        // The wrapped content keeps the page's own resources.
        if (page.Resources is not null)
        {
            formDict.Set(PdfName.Resources, ObjectImporter.CopyDictionary(page.Resources));
        }

        formDict.Set(PdfName.Length, contentBytes.Length);
        _extraObjects.Add(new PdfIndirectObject(formId, new PdfStream(formDict, contentBytes)));

        // Build the new page resources: an XObject entry for the wrapped form,
        // an ExtGState for opacity, plus any overlay fonts.
        PdfDictionary resources = new PdfDictionary();
        PdfDictionary xobjects = new PdfDictionary();
        string formName = "CvContent";
        xobjects.Set(PdfName.Intern(formName), new PdfReference(formId));
        resources.Set(PdfName.XObject, xobjects);

        bool needsGs = edit.ContentOpacity < 1f;
        string gsName = "CvGsContent";
        if (needsGs)
        {
            PdfDictionary extGState = new PdfDictionary();
            PdfDictionary gs = new PdfDictionary();
            gs.Set(PdfName.Type, PdfName.Intern("ExtGState"));
            gs.Set(PdfName.Intern("ca"), new PdfReal(edit.ContentOpacity));
            gs.Set(PdfName.Intern("CA"), new PdfReal(edit.ContentOpacity));
            extGState.Set(PdfName.Intern(gsName), gs);
            resources.Set(PdfName.Intern("ExtGState"), extGState);
        }

        if (edit.OverlayFonts.Count > 0)
        {
            PdfDictionary fonts = new PdfDictionary();
            foreach (KeyValuePair<string, PdfDictionary> f in edit.OverlayFonts)
            {
                fonts.Set(PdfName.Intern(f.Key), f.Value);
            }

            resources.Set(PdfName.Font, fonts);
        }

        // Assemble the new content stream:
        //   [background fill] q [gs] <cm> /CvContent Do Q [overlays...]
        StringBuilder content = new StringBuilder();

        if (edit.Background is ColorF bg)
        {
            ColorF rgb = bg.ToRgb();
            content.Append("q\n");
            content.Append(Fmt(rgb.R)).Append(' ').Append(Fmt(rgb.G)).Append(' ')
                .Append(Fmt(rgb.B)).Append(" rg\n");
            content.Append(Fmt(mediaBox.X1)).Append(' ').Append(Fmt(mediaBox.Y1)).Append(' ')
                .Append(Fmt(mediaBox.Width)).Append(' ').Append(Fmt(mediaBox.Height)).Append(" re\n");
            content.Append("f\n");
            content.Append("Q\n");
        }

        content.Append("q\n");
        if (needsGs)
        {
            content.Append('/').Append(gsName).Append(" gs\n");
        }

        Transform t = edit.ContentTransform;
        if (!IsIdentity(t))
        {
            content.Append(Fmt(t.A)).Append(' ').Append(Fmt(t.B)).Append(' ')
                .Append(Fmt(t.C)).Append(' ').Append(Fmt(t.D)).Append(' ')
                .Append(Fmt(t.E)).Append(' ').Append(Fmt(t.F)).Append(" cm\n");
        }

        content.Append('/').Append(formName).Append(" Do\n");
        content.Append("Q\n");

        // Overlays (header/footer) drawn after, each in its own q/Q.
        PdfArray newContents = new PdfArray([]);
        PdfObjectId mainId = NextStreamFromBytes(Encoding.Latin1.GetBytes(content.ToString()));
        newContents.Add(new PdfReference(mainId));

        foreach (byte[] overlay in edit.Overlays)
        {
            PdfObjectId ovId = NextStreamFromBytes(overlay);
            newContents.Add(new PdfReference(ovId));
        }

        pageDict.Set(PdfName.Resources, resources);
        pageDict.Set(PdfName.Contents, newContents);
        return pageDict;
    }

    private static bool IsIdentity(Transform t)
    {
        return t.A == 1 && t.B == 0 && t.C == 0 && t.D == 1 && t.E == 0 && t.F == 0;
    }

    private PdfObjectId NextStreamFromBytes(byte[] bytes)
    {
        PdfObjectId id = NextId();
        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Length, bytes.Length);
        _extraObjects.Add(new PdfIndirectObject(id, new PdfStream(dict, bytes)));
        return id;
    }

    private PdfDictionary BuildTrailer()
    {
        PdfDictionary trailer = new PdfDictionary();
        PdfDictionary catalog = _document.Catalog;

        foreach (PdfIndirectObject obj in _document.Objects.Objects)
        {
            if (ReferenceEquals(obj.Value, catalog))
            {
                trailer.Set(PdfName.Root, new PdfReference(obj.Id));

                if (_document.Trailer.TryGetValue(PdfName.Info, out PdfPrimitive? infoPrim))
                {
                    trailer.Set(PdfName.Info, infoPrim);
                }

                return trailer;
            }
        }

        if (_document.Trailer.TryGetValue(PdfName.Root, out PdfPrimitive? rootPrim)
            && rootPrim is PdfReference rootRef)
        {
            trailer.Set(PdfName.Root, rootRef);
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

    private sealed class PageEdit
    {
        internal ColorF? Background { get; set; }

        internal float ContentOpacity { get; set; } = 1f;

        internal Transform ContentTransform { get; set; } = Transform.Identity;

        internal List<byte[]> Overlays { get; } = new List<byte[]>();

        internal Dictionary<string, PdfDictionary> OverlayFonts { get; } =
            new Dictionary<string, PdfDictionary>(StringComparer.Ordinal);
    }
}
