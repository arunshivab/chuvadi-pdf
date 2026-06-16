// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.5 (file structure), §7.7.2 (catalog)
// PHASE: Document operations — shared in-place rewrite primitive.

using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// Rewrites a document, preserving every existing indirect object except those
/// explicitly replaced, while appending newly allocated objects and amending
/// the trailer. New object numbers are allocated above the document's current
/// maximum so they never collide. Used by operations that augment a document
/// without restructuring its page tree (metadata, outlines).
/// </summary>
internal sealed class DocumentRewriter
{
    private readonly PdfDocument _document;
    private readonly List<PdfIndirectObject> _added = new List<PdfIndirectObject>();
    private readonly Dictionary<int, PdfPrimitive> _replacements = new Dictionary<int, PdfPrimitive>();
    private readonly Dictionary<PdfName, PdfPrimitive> _trailerEntries = new Dictionary<PdfName, PdfPrimitive>();
    private int _nextObjectNumber;

    internal DocumentRewriter(PdfDocument document)
    {
        _document = document;

        // Force the full reachable graph into memory so no lazily loaded
        // object is dropped, and so the highest object number is correct.
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

    /// <summary>Allocates a fresh object id above the document's maximum.</summary>
    internal PdfObjectId AllocateId()
    {
        return new PdfObjectId(_nextObjectNumber++, 0);
    }

    /// <summary>Adds a newly created indirect object to the output.</summary>
    internal void AddObject(PdfObjectId id, PdfPrimitive value)
    {
        _added.Add(new PdfIndirectObject(id, value));
    }

    /// <summary>
    /// Replaces the value of an existing indirect object (by object number) in
    /// the output. The original is dropped in favour of the replacement.
    /// </summary>
    internal void ReplaceObject(PdfObjectId id, PdfPrimitive value)
    {
        _replacements[id.ObjectNumber] = value;
    }

    /// <summary>Sets or overrides an entry in the output trailer.</summary>
    internal void SetTrailerEntry(PdfName key, PdfPrimitive value)
    {
        _trailerEntries[key] = value;
    }

    /// <summary>
    /// Resolves the object id that holds the document Catalog, so callers can
    /// replace it (e.g. to attach /Outlines).
    /// </summary>
    internal PdfObjectId CatalogId()
    {
        PdfDictionary catalog = _document.Catalog;
        foreach (PdfIndirectObject obj in _document.Objects.Objects)
        {
            if (ReferenceEquals(obj.Value, catalog))
            {
                return obj.Id;
            }
        }

        if (_document.Trailer.TryGetValue(PdfName.Root, out PdfPrimitive? rootPrim)
            && rootPrim is PdfReference rootRef)
        {
            return rootRef.ObjectId;
        }

        throw new OperationsException("The document has no resolvable Catalog object.");
    }

    /// <summary>Gets a deep, renumber-free copy of the document Catalog.</summary>
    internal PdfDictionary CopyCatalog()
    {
        return ObjectImporter.CopyDictionary(_document.Catalog);
    }

    /// <summary>Writes the rewritten document to <paramref name="output"/>.</summary>
    internal void Write(Stream output)
    {
        List<PdfIndirectObject> allObjects = new List<PdfIndirectObject>();

        foreach (PdfIndirectObject obj in _document.Objects.Objects)
        {
            if (_replacements.TryGetValue(obj.Id.ObjectNumber, out PdfPrimitive? replacement))
            {
                allObjects.Add(new PdfIndirectObject(obj.Id, replacement));
            }
            else
            {
                allObjects.Add(obj);
            }
        }

        allObjects.AddRange(_added);

        PdfWriter.Write(output, allObjects, BuildTrailer());
    }

    private PdfDictionary BuildTrailer()
    {
        PdfDictionary trailer = new PdfDictionary();

        // Carry forward /Root (catalog) and existing /Info unless overridden.
        PdfObjectId catalogId = CatalogId();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        if (_document.Trailer.TryGetValue(PdfName.Info, out PdfPrimitive? infoPrim))
        {
            trailer.Set(PdfName.Info, infoPrim);
        }

        foreach (KeyValuePair<PdfName, PdfPrimitive> entry in _trailerEntries)
        {
            trailer.Set(entry.Key, entry.Value);
        }

        return trailer;
    }
}
