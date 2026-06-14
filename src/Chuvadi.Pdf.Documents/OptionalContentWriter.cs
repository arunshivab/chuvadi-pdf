// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.11.4 — Optional content configuration (/D /ON /OFF)

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Documents;

/// <summary>
/// Writes optional-content (layer) visibility changes to a PDF document.
/// </summary>
/// <remarks>
/// Complements <see cref="OptionalContentReader"/>: read the groups, then toggle
/// any of them on or off by name and write a new document. The change edits the
/// default configuration's (/OCProperties/D) /ON and /OFF arrays; the original
/// document is not modified in place.
/// </remarks>
public static class OptionalContentWriter
{
    /// <summary>
    /// Writes a copy of <paramref name="document"/> to <paramref name="output"/>
    /// with the named layers shown or hidden in the default configuration.
    /// </summary>
    /// <param name="output">Destination stream for the modified document.</param>
    /// <param name="document">The source document.</param>
    /// <param name="visibilityByName">
    /// Layer name → visibility. <c>true</c> shows the layer, <c>false</c> hides
    /// it. Names not present in the document are ignored; layers not listed keep
    /// their current visibility.
    /// </param>
    /// <exception cref="ArgumentNullException">When any argument is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// When the document declares no optional content (/OCProperties).
    /// </exception>
    public static void SetVisibility(
        Stream output,
        PdfDocument document,
        IReadOnlyDictionary<string, bool> visibilityByName)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(visibilityByName);

        PreloadAllObjects(document);

        PdfObjectStore store = document.Objects;
        (PdfDictionary catalog, PdfObjectId catalogId) = FindCatalog(store);

        if (!catalog.TryGetValue(PdfName.Intern("OCProperties"), out PdfPrimitive? ocpRaw))
        {
            throw new InvalidOperationException(
                "The document declares no optional content (/OCProperties).");
        }

        bool ocpIsRef = ocpRaw is PdfReference;
        PdfObjectId ocpId = ocpIsRef ? ((PdfReference)ocpRaw).ObjectId : default;

        PdfDictionary ocProps = store.ResolveAs<PdfDictionary>(ocpRaw) ??
            throw new InvalidOperationException("/OCProperties is not a dictionary.");

        PdfArray ocgs = (ocProps.TryGetValue(PdfName.Intern("OCGs"), out PdfPrimitive? ocgsRaw)
            ? store.ResolveAs<PdfArray>(ocgsRaw)
            : null) ?? throw new InvalidOperationException("/OCProperties has no /OCGs array.");

        // Map each layer name to the reference used in /OCGs.
        Dictionary<string, PdfReference> refByName = new Dictionary<string, PdfReference>();
        for (int i = 0; i < ocgs.Count; i++)
        {
            if (ocgs[i] is PdfReference groupRef)
            {
                PdfDictionary? ocg = store.ResolveAs<PdfDictionary>(groupRef);
                if (ocg is not null &&
                    ocg.TryGetValue(PdfName.Intern("Name"), out PdfPrimitive? namePrim) &&
                    namePrim is PdfString nameStr)
                {
                    refByName[System.Text.Encoding.Latin1.GetString(nameStr.Bytes)] = groupRef;
                }
            }
        }

        // Resolve the default configuration dictionary /D.
        bool dIsRef = ocProps.TryGetValue(PdfName.Intern("D"), out PdfPrimitive? dRaw) &&
            dRaw is PdfReference;
        PdfObjectId dId = dIsRef ? ((PdfReference)dRaw!).ObjectId : default;
        PdfDictionary dDict = (dRaw is not null ? store.ResolveAs<PdfDictionary>(dRaw) : null)
            ?? new PdfDictionary();

        List<PdfReference> onList = ReadReferenceArray(dDict, "ON", store);
        List<PdfReference> offList = ReadReferenceArray(dDict, "OFF", store);

        // Apply the requested toggles unambiguously regardless of /BaseState:
        // a visible layer goes in /ON and not /OFF; a hidden layer the reverse.
        foreach (KeyValuePair<string, bool> request in visibilityByName)
        {
            if (!refByName.TryGetValue(request.Key, out PdfReference? target))
            {
                continue;
            }

            int number = target.ObjectNumber;
            onList.RemoveAll(r => r.ObjectNumber == number);
            offList.RemoveAll(r => r.ObjectNumber == number);

            if (request.Value)
            {
                onList.Add(target);
            }
            else
            {
                offList.Add(target);
            }
        }

        PdfDictionary newD = CopyDictionary(dDict);
        newD.Set(PdfName.Intern("ON"), new PdfArray(onList));
        newD.Set(PdfName.Intern("OFF"), new PdfArray(offList));

        // Rewrite the shallowest indirect object that owns the change.
        List<PdfIndirectObject> updated = new List<PdfIndirectObject>();
        if (dIsRef)
        {
            updated.Add(new PdfIndirectObject(dId, newD));
        }
        else
        {
            PdfDictionary newOcProps = CopyDictionary(ocProps);
            newOcProps.Set(PdfName.Intern("D"), newD);

            if (ocpIsRef)
            {
                updated.Add(new PdfIndirectObject(ocpId, newOcProps));
            }
            else
            {
                PdfDictionary newCatalog = CopyDictionary(catalog);
                newCatalog.Set(PdfName.Intern("OCProperties"), newOcProps);
                updated.Add(new PdfIndirectObject(catalogId, newCatalog));
            }
        }

        WriteWithReplacements(output, store, catalogId, updated);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static List<PdfReference> ReadReferenceArray(
        PdfDictionary dict, string key, PdfObjectStore store)
    {
        List<PdfReference> result = new List<PdfReference>();
        if (dict.TryGetValue(PdfName.Intern(key), out PdfPrimitive? prim) &&
            store.ResolveAs<PdfArray>(prim) is PdfArray arr)
        {
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] is PdfReference r)
                {
                    result.Add(r);
                }
            }
        }

        return result;
    }

    private static void WriteWithReplacements(
        Stream output,
        PdfObjectStore store,
        PdfObjectId catalogId,
        List<PdfIndirectObject> updated)
    {
        HashSet<int> rewritten = new HashSet<int>();
        foreach (PdfIndirectObject obj in updated)
        {
            rewritten.Add(obj.Id.ObjectNumber);
        }

        List<PdfIndirectObject> allObjects = new List<PdfIndirectObject>(updated);
        foreach (PdfIndirectObject obj in store.Objects)
        {
            if (!rewritten.Contains(obj.Id.ObjectNumber))
            {
                allObjects.Add(obj);
            }
        }

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        PdfWriter.Write(output, allObjects, trailer);
    }

    private static (PdfDictionary Catalog, PdfObjectId Id) FindCatalog(PdfObjectStore store)
    {
        foreach (PdfIndirectObject obj in store.Objects)
        {
            if (obj.Value is PdfDictionary dict &&
                dict.TryGetValue(PdfName.Type, out PdfPrimitive? t) &&
                t is PdfName name && name.Value == "Catalog")
            {
                return (dict, obj.Id);
            }
        }

        throw new InvalidOperationException("The document has no /Catalog object.");
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

    private static void PreloadAllObjects(PdfDocument document)
    {
        HashSet<int> visited = new HashSet<int>();

        for (int i = 0; i < document.PageCount; i++)
        {
            Visit(document.Objects, document.Pages[i].Dictionary, visited);
        }

        Visit(document.Objects, document.Catalog, visited);
    }

    private static void Visit(PdfObjectStore store, PdfPrimitive? primitive, HashSet<int> visited)
    {
        if (primitive is null)
        {
            return;
        }

        if (primitive is PdfReference reference)
        {
            if (!visited.Add(reference.ObjectNumber))
            {
                return;
            }

            Visit(store, store.Resolve(reference), visited);
            return;
        }

        if (primitive is PdfDictionary dict)
        {
            foreach (KeyValuePair<PdfName, PdfPrimitive> entry in dict)
            {
                Visit(store, entry.Value, visited);
            }
        }
        else if (primitive is PdfArray arr)
        {
            for (int i = 0; i < arr.Count; i++)
            {
                Visit(store, arr[i], visited);
            }
        }
        else if (primitive is PdfStream stream)
        {
            foreach (KeyValuePair<PdfName, PdfPrimitive> entry in stream.Dictionary)
            {
                Visit(store, entry.Value, visited);
            }
        }
    }
}
