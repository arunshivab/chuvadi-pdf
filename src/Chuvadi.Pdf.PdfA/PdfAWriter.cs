// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ISO 19005-1 (PDF/A-1b), ISO 19005-2 (PDF/A-2b)
// PHASE: Phase 3 — PDF/A writer
//
// Writes a PdfDocument as a PDF/A-1b or PDF/A-2b file: embeds Standard-14 fonts
// with Liberation substitutes, strips JavaScript, adds an sRGB output intent and
// pdfaid XMP metadata, and serialises with a classic cross-reference table and
// the correct header version. When an unfixable conformance problem remains,
// nothing is written and the result reports the violations.

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.PdfA;

/// <summary>Writes PDF/A-1b and PDF/A-2b conforming documents.</summary>
public static class PdfAWriter
{
    private const string Producer = "Chuvadi PDF/A";

    /// <summary>
    /// Writes <paramref name="document"/> to <paramref name="output"/> as a
    /// PDF/A file at the requested conformance level. When the document cannot be
    /// made conforming, nothing is written and the returned result reports why.
    /// </summary>
    /// <param name="output">The destination stream.</param>
    /// <param name="document">The source document (mutated in place during embedding).</param>
    /// <param name="options">The conformance options.</param>
    /// <returns>The write result.</returns>
    /// <exception cref="ArgumentNullException">A parameter is null.</exception>
    public static PdfAResult Write(Stream output, PdfDocument document, PdfAOptions options)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        List<string> violations = new List<string>();
        PdfObjectStore store = document.Objects;

        if (document.Trailer.ContainsKey(PdfName.Intern("Encrypt")))
        {
            violations.Add("Encrypted documents cannot conform to PDF/A.");
        }

        int nextNumber = NextObjectNumber(document);
        PdfObjectId Allocate() => new PdfObjectId(nextNumber++, 0);

        PdfPrimitive rootRef = document.Trailer.TryGetValue(PdfName.Root, out PdfPrimitive? r)
            ? r
            : throw new InvalidOperationException("The document trailer has no /Root entry.");
        if (store.Resolve(rootRef) is not PdfDictionary catalog)
        {
            throw new InvalidOperationException("The document /Root does not resolve to a catalog.");
        }

        // 3.33.0 (ticket 46, D-314): PDF/A-1b allows no /Differences in a
        // non-symbolic TrueType font, so a font whose encoding is not WinAnsi
        // gets the symbolic form there.
        NonWinAnsiForm form = options.Conformance == PdfAConformance.PdfA1B
            ? NonWinAnsiForm.Symbolic
            : NonWinAnsiForm.Differences;
        FontSubstitution.SubstituteStandard14(document, Allocate, LiberationFaces.Get, violations, form);
        StripJavaScript(catalog, store);

        byte[] icc = options.OutputIntentIccProfile ?? SrgbIccProfile.Load();
        OutputIntentBuilder.Result intent = OutputIntentBuilder.Build(
            icc, options.OutputConditionIdentifier, options.RegistryName, Allocate);
        AddOutputIntent(catalog, intent.OutputIntent);
        foreach (PdfIndirectObject obj in intent.Objects)
        {
            store.Add(obj);
        }

        int part = options.Conformance == PdfAConformance.PdfA1B ? 1 : 2;
        // 3.33.0 (D-318): PDF/A-1b requires the XMP Producer and CreatorTool
        // to equal /Info's /Producer and /Creator (ISO 19005-1 §6.7.3), so
        // they are taken from the /Info the file ends up with; a missing entry
        // gets "Chuvadi PDF/A", as before.
        // 3.33.0 (ticket 49, D-325): likewise dc:title, dc:creator and
        // dc:description follow /Info's /Title, /Author and /Subject; where
        // /Info has none, the options' Title and Author are used, as before. A
        // value XML cannot hold (a control character) is left as it was
        // before 3.33.0.
        string producer = Producer;
        string creatorTool = Producer;
        string? title = options.Title;
        string? author = options.Author;
        string? subject = null;
        if (part == 1
            && document.Trailer.TryGetValue(PdfName.Info, out PdfPrimitive? sourceInfo)
            && store.Resolve(sourceInfo) is PdfDictionary infoDict)
        {
            producer = InfoText(infoDict, "Producer", store) ?? Producer;
            creatorTool = InfoText(infoDict, "Creator", store) ?? Producer;
            title = InfoText(infoDict, "Title", store) ?? options.Title;
            author = InfoText(infoDict, "Author", store) ?? options.Author;
            subject = InfoText(infoDict, "Subject", store);
        }

        byte[] xmp = XmpMetadata.Build(part, "B", title, author, subject, producer, creatorTool);
        PdfObjectId metadataId = Allocate();
        PdfDictionary metadataDict = new PdfDictionary();
        metadataDict.Set(PdfName.Type, PdfName.Intern("Metadata"));
        metadataDict.Set(PdfName.Intern("Subtype"), PdfName.Intern("XML"));
        store.Add(new PdfIndirectObject(metadataId, new PdfStream(metadataDict, xmp)));
        catalog.Set(PdfName.Intern("Metadata"), new PdfReference(metadataId));
        catalog.Remove(PdfName.Intern("Version"));

        if (violations.Count > 0)
        {
            return new PdfAResult(false, violations);
        }

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, rootRef);
        if (document.Trailer.TryGetValue(PdfName.Info, out PdfPrimitive? infoRef))
        {
            trailer.Set(PdfName.Info, infoRef);
        }

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>();
        HashSet<int> visited = new HashSet<int>();
        CollectReachable(store, rootRef, objects, visited);
        if (document.Trailer.TryGetValue(PdfName.Info, out PdfPrimitive? info2))
        {
            CollectReachable(store, info2, objects, visited);
        }

        using MemoryStream buffer = new MemoryStream();
        PdfWriter.Write(buffer, objects, trailer, null, SynthesizedMetadata.None, XrefStyle.Classic);
        byte[] bytes = buffer.ToArray();
        PatchHeaderVersion(bytes, part == 1 ? (byte)'4' : (byte)'7');
        output.Write(bytes, 0, bytes.Length);

        return new PdfAResult(true, violations);
    }

    // A text entry of the document information dictionary, or null.
    // (3.33.0: null too when the text holds a character XML cannot carry.)
    private static string? InfoText(PdfDictionary info, string key, PdfObjectStore store) =>
        info.TryGetValue(PdfName.Intern(key), out PdfPrimitive? value)
            && store.Resolve(value) is PdfString text
            && text.ToTextString() is string decoded
            && XmpMetadata.IsXmlText(decoded)
            ? decoded
            : null;

    private static int NextObjectNumber(PdfDocument document)
    {
        if (document.Trailer.TryGetValue(PdfName.Intern("Size"), out PdfPrimitive? sizeValue)
            && document.Objects.Resolve(sizeValue) is PdfInteger size
            && size.Value > 0)
        {
            return size.Value;
        }

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

    private static void AddOutputIntent(PdfDictionary catalog, PdfDictionary intent)
    {
        PdfName key = PdfName.Intern("OutputIntents");
        PdfArray intents = catalog.TryGetValue(key, out PdfPrimitive? existing) && existing is PdfArray array
            ? array
            : new PdfArray();
        intents.Add(intent);
        catalog.Set(key, intents);
    }

    private static void StripJavaScript(PdfDictionary catalog, PdfObjectStore store)
    {
        catalog.Remove(PdfName.Intern("AA"));

        if (catalog.TryGetValue(PdfName.Intern("OpenAction"), out PdfPrimitive? openRef)
            && store.Resolve(openRef) is PdfDictionary action
            && action.TryGetValue(PdfName.Intern("S"), out PdfPrimitive? s)
            && store.Resolve(s) is PdfName actionType
            && actionType.Value == "JavaScript")
        {
            catalog.Remove(PdfName.Intern("OpenAction"));
        }

        if (catalog.TryGetValue(PdfName.Intern("Names"), out PdfPrimitive? namesRef)
            && store.Resolve(namesRef) is PdfDictionary names)
        {
            names.Remove(PdfName.Intern("JavaScript"));
        }
    }

    private static void PatchHeaderVersion(byte[] bytes, byte minorDigit)
    {
        // Header is "%PDF-1.x"; patch the minor version digit at offset 7.
        if (bytes.Length >= 8
            && bytes[0] == '%' && bytes[1] == 'P' && bytes[2] == 'D' && bytes[3] == 'F'
            && bytes[4] == '-' && bytes[5] == '1' && bytes[6] == '.')
        {
            bytes[7] = minorDigit;
        }
    }

    private static void CollectReachable(
        PdfObjectStore store,
        PdfPrimitive? primitive,
        List<PdfIndirectObject> collected,
        HashSet<int> visited)
    {
        if (primitive is PdfReference reference)
        {
            if (!visited.Add(reference.ObjectId.ObjectNumber))
            {
                return;
            }

            PdfPrimitive resolved = store.Resolve(reference);
            collected.Add(new PdfIndirectObject(reference.ObjectId, resolved));
            CollectReachable(store, resolved, collected, visited);
        }
        else if (primitive is PdfDictionary dictionary)
        {
            foreach (KeyValuePair<PdfName, PdfPrimitive> entry in dictionary)
            {
                if (entry.Key == PdfName.Parent)
                {
                    continue;
                }

                CollectReachable(store, entry.Value, collected, visited);
            }
        }
        else if (primitive is PdfArray array)
        {
            for (int i = 0; i < array.Count; i++)
            {
                CollectReachable(store, array[i], collected, visited);
            }
        }
        else if (primitive is PdfStream stream)
        {
            foreach (KeyValuePair<PdfName, PdfPrimitive> entry in stream.Dictionary)
            {
                CollectReachable(store, entry.Value, collected, visited);
            }
        }
    }
}
