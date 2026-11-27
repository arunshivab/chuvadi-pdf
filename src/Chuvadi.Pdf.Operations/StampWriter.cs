// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.7.3 (pages), §7.8.2 (content streams)
// PHASE: Document operations — overlay text streams onto existing pages.

using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// Appends overlay content streams (already-built text fragments) on top of
/// existing page content, injecting a shared Helvetica font resource. Existing
/// content is isolated in its own q/Q scope so overlays are unaffected by it.
/// Pages without overlays are preserved unchanged. Used by
/// <see cref="TextStamper"/>.
/// </summary>
internal sealed class StampWriter
{
    private readonly PdfDocument _document;
    private readonly List<PdfIndirectObject> _extraObjects = new List<PdfIndirectObject>();
    private readonly Dictionary<int, List<byte[]>> _overlays = new Dictionary<int, List<byte[]>>();
    private readonly Dictionary<int, Dictionary<string, PdfObjectId>> _overlayFonts = new Dictionary<int, Dictionary<string, PdfObjectId>>();
    private readonly Dictionary<string, PdfDictionary> _extGStates = new Dictionary<string, PdfDictionary>();
    private PdfObjectId? _outlineRootId;
    private int _nextObjectNumber;

    internal StampWriter(PdfDocument document)
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

    internal void AddOverlay(int pageIndex, byte[] streamBytes)
    {
        if (!_overlays.TryGetValue(pageIndex, out List<byte[]>? list))
        {
            list = new List<byte[]>();
            _overlays[pageIndex] = list;
        }

        list.Add(streamBytes);
    }

    internal void Write(Stream output)
    {
        Write(output, null);
    }

    internal void Write(Stream output, EncryptionOptions? encryption)
    {
        List<PdfIndirectObject> allObjects = new List<PdfIndirectObject>();
        HashSet<int> modifiedPageNumbers = new HashSet<int>();

        Dictionary<int, PdfObjectId> pageIds = PageTree.BuildIndexToIdMap(_document);

        foreach (KeyValuePair<int, List<byte[]>> entry in _overlays)
        {
            if (!pageIds.TryGetValue(entry.Key, out PdfObjectId pageId))
            {
                continue;
            }

            PdfPage page = _document.Pages[entry.Key];
            PdfDictionary modified = BuildModifiedPage(entry.Key, page, entry.Value);
            allObjects.Add(new PdfIndirectObject(pageId, modified));
            modifiedPageNumbers.Add(pageId.ObjectNumber);
        }

        PdfDictionary catalog = _document.Catalog;
        foreach (PdfIndirectObject obj in _document.Objects.Objects)
        {
            if (modifiedPageNumbers.Contains(obj.Id.ObjectNumber))
            {
                continue;
            }

            if (_outlineRootId is not null && ReferenceEquals(obj.Value, catalog))
            {
                PdfDictionary withOutline = ObjectImporter.CopyDictionary(catalog);
                withOutline.Set(PdfName.Outlines, new PdfReference(_outlineRootId.Value));
                allObjects.Add(new PdfIndirectObject(obj.Id, withOutline));
            }
            else
            {
                allObjects.Add(obj);
            }
        }

        allObjects.AddRange(_extraObjects);

        PdfWriter.Write(output, allObjects, BuildTrailer(), encryption);
    }

    internal PdfObjectId AllocateId()
    {
        return NextId();
    }

    // 3.36.0 (D-412): registers an embedded (Type0) font, by resource name, for
    // the overlays on one page. The font's own objects are added through
    // AddIndirectObject; this records the name→id reference the page's /Font
    // resources need so the overlay's Tf operators resolve.
    internal void AddOverlayFont(int pageIndex, string resourceName, PdfObjectId type0FontId)
    {
        if (!_overlayFonts.TryGetValue(pageIndex, out Dictionary<string, PdfObjectId>? fonts))
        {
            fonts = new Dictionary<string, PdfObjectId>();
            _overlayFonts[pageIndex] = fonts;
        }

        fonts[resourceName] = type0FontId;
    }

    internal void AddIndirectObject(PdfObjectId id, PdfPrimitive value)
    {
        _extraObjects.Add(new PdfIndirectObject(id, value));
    }

    internal void SetOutlineRoot(PdfObjectId rootId)
    {
        _outlineRootId = rootId;
    }

    internal void RegisterExtGState(string name, PdfDictionary state)
    {
        _extGStates[name] = state;
    }

    private PdfDictionary BuildModifiedPage(int pageIndex, PdfPage page, List<byte[]> overlays)
    {
        PdfDictionary pageDict = ObjectImporter.CopyDictionary(page.Dictionary);

        List<PdfPrimitive> existingContent = new List<PdfPrimitive>();
        PdfPrimitive? contents = page.Contents;
        if (contents is not null && contents is not PdfNull)
        {
            PdfPrimitive resolved = _document.Objects.Resolve(contents);
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

        // 3.36.0 (D-412): merge the page's embedded (Type0) overlay fonts into
        // its resources as indirect references. The mark text is shaped and the
        // faces subset-embedded as Type0 fonts, so there is no shared Helvetica
        // standard-14 font any more.
        PdfDictionary resources = page.Resources is not null
            ? ObjectImporter.CopyDictionary(page.Resources)
            : new PdfDictionary();

        if (_overlayFonts.TryGetValue(pageIndex, out Dictionary<string, PdfObjectId>? overlayFonts)
            && overlayFonts.Count > 0)
        {
            PdfDictionary fonts = GetOrCreateSubdict(resources, "Font");
            foreach (KeyValuePair<string, PdfObjectId> font in overlayFonts)
            {
                fonts.Set(PdfName.Intern(font.Key), new PdfReference(font.Value));
            }

            resources.Set(PdfName.Font, fonts);
        }

        if (_extGStates.Count > 0)
        {
            PdfDictionary gsDict = GetOrCreateSubdict(resources, "ExtGState");
            foreach (KeyValuePair<string, PdfDictionary> gs in _extGStates)
            {
                gsDict.Set(PdfName.Intern(gs.Key), gs.Value);
            }

            resources.Set(PdfName.Intern("ExtGState"), gsDict);
        }

        pageDict.Set(PdfName.Resources, resources);

        // Isolate existing content in its own q/Q so overlays are independent.
        PdfObjectId saveId = NextStreamFromBytes(Encoding.ASCII.GetBytes("q\n"));
        PdfObjectId restoreId = NextStreamFromBytes(Encoding.ASCII.GetBytes("Q\n"));

        PdfArray newContents = new PdfArray([]);
        newContents.Add(new PdfReference(saveId));
        foreach (PdfPrimitive c in existingContent)
        {
            newContents.Add(c);
        }

        newContents.Add(new PdfReference(restoreId));

        foreach (byte[] overlay in overlays)
        {
            PdfObjectId ovId = NextStreamFromBytes(overlay);
            newContents.Add(new PdfReference(ovId));
        }

        pageDict.Set(PdfName.Contents, newContents);
        return pageDict;
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
}
