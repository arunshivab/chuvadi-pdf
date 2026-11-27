// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ISO 19005-1 (PDF/A-1b), ISO 19005-2 (PDF/A-2b)
// PHASE: Phase 3 — PDF/A writer
//
// Writes a PdfDocument as a PDF/A-1b or PDF/A-2b file: embeds Standard-14 fonts
// with Liberation substitutes, strips JavaScript, adds an sRGB output intent and
// pdfaid XMP metadata, and serialises with a classic cross-reference table and
// the correct header version. When an unfixable conformance problem remains,
// nothing is written and the result reports the violations (3.35.0, PDA-01:
// including transparency in PDF/A-1b, DeviceCMYK without a CMYK output
// intent, optional content, embedded files and LZW compression).

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Forms;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Rendering.Walking;

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
        return PdfWriteGuard.Run(document, "converted", () =>
        {
            ArgumentNullException.ThrowIfNull(output);
            ArgumentNullException.ThrowIfNull(document);
            ArgumentNullException.ThrowIfNull(options);

            List<string> violations = new List<string>();
            List<string> warnings = new List<string>();
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
            // 3.35.0 (backlog 49): form fields get the appearances a viewer
            // would make, and /NeedAppearances is cleared (ISO 19005-1
            // §6.9, ISO 19005-2 §6.4.1); the graphics states lose their
            // transfer functions (ISO 19005-1 §6.2.8, ISO 19005-2 §6.2.5).
            List<PdfDictionary> generatedResources = GenerateFieldAppearances(document, catalog, store, Allocate, warnings);
            RemoveTransferFunctions(store, rootRef, warnings);

            FontSubstitution.SubstituteStandard14(document, Allocate, LiberationFaces.Get, violations, form, generatedResources);
            StripJavaScript(catalog, store);

            // 3.36.0 (PDA-01): the actions PDF/A forbids (and every /AA
            // additional-actions entry) are removed from fields, widgets,
            // pages and annotations, with a warning per kind.
            StripForbiddenActions(rootRef, store, options.Conformance == PdfAConformance.PdfA1B, warnings);

            // 3.34.0 (#49c): every annotation is printable and visible
            // (ISO 19005-1 §6.5.3, ISO 19005-2 §6.3.2).
            PrepareAnnotations(document, store);

            // 3.34.0 (#49d): a Type 1 subset lists its glyphs in /CharSet
            // (ISO 19005-1 §6.3.5).
            if (options.Conformance == PdfAConformance.PdfA1B)
            {
                AddCharSets(document, store);
            }

            // 3.36.0 (PDA-01): every embedded CID-font subset gets a /CIDSet
            // rebuilt from its program (both parts require it); a program that
            // cannot be read is a violation.
            RebuildCidSets(document, store, Allocate, violations);

            // 3.36.0 (PDA-01 part 3): a CID font whose declared /W widths do not
            // match its embedded program's glyph advances (the ReportLab width
            // bug, ISO 19005-2 §6.2.11.5) is refused; correcting the widths would
            // move glyphs on the page, so it is a violation, not a fix.
            CheckCidWidths(document, store, violations);

            // 3.36.0 (PDA-01 part 3, D-417(2)=a): the same width-consistency rule
            // (ISO 19005-2 §6.2.11.5) for simple embedded TrueType fonts. Only the
            // glyphs actually shown on a page are checked — the codes the content
            // streams render — because an unused /Widths entry whose cmap-mapped
            // glyph disagrees is not a conformance failure (veraPDF checks rendered
            // glyphs only), so checking every declared code would wrongly flag
            // clean files. The mapping uses the font's cmap with no code-as-glyph
            // fallback, and only symbolic fonts are checked (a non-symbolic font's
            // codes are governed by its encoding, not the raw cmap), so a mapping
            // that cannot be made unambiguously is skipped rather than guessed.
            CheckSimpleFontWidths(document, store, violations);

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
            // 3.34.0 (D-335): likewise pdf:Keywords follows /Info's /Keywords.
            string producer = Producer;
            string creatorTool = Producer;
            string? title = options.Title;
            string? author = options.Author;
            string? subject = null;
            string? keywords = null;
            string? createDate = null;
            string? modifyDate = null;
            if (part == 1
                && document.Trailer.TryGetValue(PdfName.Info, out PdfPrimitive? sourceInfo)
                && store.Resolve(sourceInfo) is PdfDictionary infoDict)
            {
                producer = InfoText(infoDict, "Producer", store) ?? Producer;
                creatorTool = InfoText(infoDict, "Creator", store) ?? Producer;
                title = InfoText(infoDict, "Title", store) ?? options.Title;
                author = InfoText(infoDict, "Author", store) ?? options.Author;
                subject = InfoText(infoDict, "Subject", store);
                keywords = InfoText(infoDict, "Keywords", store);

                // 3.34.0 (#49g, D-364): xmp:CreateDate and xmp:ModifyDate
                // follow /CreationDate and /ModDate (ISO 19005-1 §6.7.3). A date
                // without a time zone is left out with a warning, or written as
                // UTC (with a warning) when the options ask; a date that cannot
                // be read is left out with a warning.
                createDate = InfoDate(InfoText(infoDict, "CreationDate", store), "/CreationDate", "xmp:CreateDate", options, warnings);
                modifyDate = InfoDate(InfoText(infoDict, "ModDate", store), "/ModDate", "xmp:ModifyDate", options, warnings);
            }

            byte[] xmp = XmpMetadata.Build(part, "B", title, author, subject, keywords, producer, creatorTool, createDate, modifyDate);
            PdfObjectId metadataId = Allocate();
            PdfDictionary metadataDict = new PdfDictionary();
            metadataDict.Set(PdfName.Type, PdfName.Intern("Metadata"));
            metadataDict.Set(PdfName.Intern("Subtype"), PdfName.Intern("XML"));
            store.Add(new PdfIndirectObject(metadataId, new PdfStream(metadataDict, xmp)));
            catalog.Set(PdfName.Intern("Metadata"), new PdfReference(metadataId));
            catalog.Remove(PdfName.Intern("Version"));

            // 3.35.0 (PDA-01): what the fixes above cannot remove is checked
            // before anything is written, so a file that would not conform is
            // reported instead of written as if it did.
            PdfAConformanceCheck.Run(document, rootRef, store, options.Conformance, violations);

            if (violations.Count > 0)
            {
                return new PdfAResult(false, violations, warnings);
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

            return new PdfAResult(true, violations, warnings);
        });
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

    // PDA-01: the action types PDF/A forbids. ISO 19005-1 §6.6.1 (part 1) bars
    // Launch, Sound, Movie, ResetForm, ImportData and JavaScript; ISO 19005-2
    // §6.6.1 (part 2) also bars Hide, SetOCGState, Rendition, Trans and
    // GoTo3DView. Named actions other than the four page moves are barred in
    // both.
    private static readonly HashSet<string> ForbiddenActionsPart1 = new HashSet<string>(StringComparer.Ordinal)
    {
        "Launch", "Sound", "Movie", "ResetForm", "ImportData", "JavaScript",
    };

    private static readonly HashSet<string> ForbiddenActionsPart2 = new HashSet<string>(StringComparer.Ordinal)
    {
        "Launch", "Sound", "Movie", "ResetForm", "ImportData", "JavaScript",
        "Hide", "SetOCGState", "Rendition", "Trans", "GoTo3DView",
    };

    private static readonly HashSet<string> AllowedNamedActions = new HashSet<string>(StringComparer.Ordinal)
    {
        "NextPage", "PrevPage", "FirstPage", "LastPage",
    };

    private static readonly PdfName ActionKey = PdfName.Intern("A");
    private static readonly PdfName AdditionalActionsKey = PdfName.Intern("AA");
    private static readonly PdfName OpenActionKey = PdfName.Intern("OpenAction");
    private static readonly PdfName ActionSubtype = PdfName.Intern("S");
    private static readonly PdfName NamedActionKey = PdfName.Intern("N");
    private static readonly PdfName NextActionKey = PdfName.Intern("Next");

    // PDA-01: removes the actions PDF/A forbids from every reachable object —
    // fields, widgets, pages, annotations and the catalog — and the /AA
    // additional-actions entries, which PDF/A bars outright. Each kind removed
    // adds a warning. A forbidden action chained after an allowed one (/Next)
    // is pruned from the chain. Orphaned action objects drop out when the
    // writer re-collects the reachable objects before writing.
    private static void StripForbiddenActions(
        PdfPrimitive rootRef, PdfObjectStore store, bool partOne, List<string> warnings)
    {
        HashSet<string> forbidden = partOne ? ForbiddenActionsPart1 : ForbiddenActionsPart2;
        SortedSet<string> removed = new SortedSet<string>(StringComparer.Ordinal);
        HashSet<int> visitedRefs = new HashSet<int>();
        HashSet<PdfDictionary> visitedDicts = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        Stack<PdfPrimitive> pending = new Stack<PdfPrimitive>();
        pending.Push(rootRef);

        while (pending.Count > 0)
        {
            PdfWorkScope.ThrowIfCancellationRequested();
            PdfPrimitive current = pending.Pop();
            switch (current)
            {
                case PdfReference reference:
                    if (visitedRefs.Add(reference.ObjectId.ObjectNumber))
                    {
                        pending.Push(store.Resolve(reference));
                    }

                    break;
                case PdfStream stream:
                    pending.Push(stream.Dictionary);
                    break;
                case PdfDictionary dict:
                    if (!visitedDicts.Add(dict))
                    {
                        break;
                    }

                    StripActionsOnDictionary(dict, store, forbidden, removed);
                    foreach (KeyValuePair<PdfName, PdfPrimitive> entry in dict)
                    {
                        if (entry.Key != PdfName.Parent)
                        {
                            pending.Push(entry.Value);
                        }
                    }

                    break;
                case PdfArray array:
                    for (int i = array.Count - 1; i >= 0; i--)
                    {
                        pending.Push(array[i]);
                    }

                    break;
            }
        }

        warnings.AddRange(removed);
    }

    private static void StripActionsOnDictionary(
        PdfDictionary dict, PdfObjectStore store, HashSet<string> forbidden, SortedSet<string> removed)
    {
        if (dict.Remove(AdditionalActionsKey))
        {
            removed.Add("Removed an /AA additional-actions entry; PDF/A does not permit additional actions.");
        }

        if (dict.TryGetValue(ActionKey, out PdfPrimitive? action))
        {
            PdfPrimitive? kept = PruneAction(action, store, forbidden, removed, 0);
            if (kept is null)
            {
                dict.Remove(ActionKey);
            }
            else
            {
                dict.Set(ActionKey, kept);
            }
        }

        if (dict.TryGetValue(OpenActionKey, out PdfPrimitive? openAction))
        {
            PdfPrimitive? kept = PruneAction(openAction, store, forbidden, removed, 0);
            if (kept is null)
            {
                dict.Remove(OpenActionKey);
            }
            else
            {
                dict.Set(OpenActionKey, kept);
            }
        }
    }

    // Returns the action to keep, or null when the action itself is forbidden
    // (dropping it and its /Next chain). A kept action has its /Next chain
    // pruned of forbidden actions. A value that is not an action dictionary
    // (for example an /OpenAction destination array) is returned unchanged.
    private static PdfPrimitive? PruneAction(
        PdfPrimitive value, PdfObjectStore store, HashSet<string> forbidden, SortedSet<string> removed, int depth)
    {
        if (depth > 32 || store.Resolve(value) is not PdfDictionary action)
        {
            return value;
        }

        string? subtype = action.TryGetValue(ActionSubtype, out PdfPrimitive? s) && store.Resolve(s) is PdfName name
            ? name.Value
            : null;
        if (subtype is not null && (forbidden.Contains(subtype) || (subtype == "Named" && !IsAllowedNamed(action, store))))
        {
            removed.Add("Removed a " + subtype + " action; PDF/A does not permit it.");
            return null;
        }

        if (action.TryGetValue(NextActionKey, out PdfPrimitive? next))
        {
            PdfPrimitive? prunedNext = PruneNext(next, store, forbidden, removed, depth + 1);
            if (prunedNext is null)
            {
                action.Remove(NextActionKey);
            }
            else
            {
                action.Set(NextActionKey, prunedNext);
            }
        }

        return value;
    }

    private static PdfPrimitive? PruneNext(
        PdfPrimitive value, PdfObjectStore store, HashSet<string> forbidden, SortedSet<string> removed, int depth)
    {
        if (store.Resolve(value) is PdfArray array)
        {
            PdfArray kept = new PdfArray([]);
            foreach (PdfPrimitive item in array)
            {
                PdfPrimitive? pruned = PruneAction(item, store, forbidden, removed, depth);
                if (pruned is not null)
                {
                    kept.Add(pruned);
                }
            }

            return kept.Count > 0 ? kept : null;
        }

        return PruneAction(value, store, forbidden, removed, depth);
    }

    private static bool IsAllowedNamed(PdfDictionary action, PdfObjectStore store) =>
        action.TryGetValue(NamedActionKey, out PdfPrimitive? n) && store.Resolve(n) is PdfName named
        && AllowedNamedActions.Contains(named.Value);

    // Annotation flags, ISO 32000-1 §12.5.3 Table 165.
    private const int FlagInvisible = 0x1;
    private const int FlagHidden = 0x2;
    private const int FlagPrint = 0x4;
    private const int FlagNoView = 0x20;
    private const int FlagToggleNoView = 0x100;

    // Makes an appearance for each widget that has none, and, when the form's
    // /NeedAppearances is true, for every text and choice field (buttons keep
    // their designed state appearances, as viewers do); then clears the flag.
    // Returns the new appearances' resources, for font embedding.
    private static List<PdfDictionary> GenerateFieldAppearances(
        PdfDocument document, PdfDictionary catalog, PdfObjectStore store, Func<PdfObjectId> allocate, List<string> warnings)
    {
        List<PdfDictionary> resources = new List<PdfDictionary>();
        if (!catalog.TryGetValue(PdfName.Intern("AcroForm"), out PdfPrimitive? formRef)
            || store.Resolve(formRef) is not PdfDictionary acroForm)
        {
            return resources;
        }

        bool needAppearances = store.Resolve(acroForm.TryGetValue(PdfName.Intern("NeedAppearances"), out PdfPrimitive? na) ? na : PdfNull.Value)
            is PdfBoolean { Value: true };
        int made = 0;
        for (int i = 0; i < document.PageCount; i++)
        {
            if (!document.Pages[i].Dictionary.TryGetValue(PdfName.Intern("Annots"), out PdfPrimitive? annotsRef)
                || store.Resolve(annotsRef) is not PdfArray annots)
            {
                continue;
            }

            foreach (PdfPrimitive entry in annots)
            {
                if (store.Resolve(entry) is not PdfDictionary widget
                    || store.Resolve(widget.TryGetValue(PdfName.Subtype, out PdfPrimitive? st) ? st : PdfNull.Value) is not PdfName { Value: "Widget" })
                {
                    continue;
                }

                string? type = InheritedName(widget, "FT", store);
                bool hasAppearance = store.Resolve(widget.TryGetValue(PdfName.Intern("AP"), out PdfPrimitive? ap) ? ap : PdfNull.Value) is PdfDictionary;
                if (type is null or "Sig" || (hasAppearance && !(needAppearances && type is "Tx" or "Ch")))
                {
                    continue;
                }

                PdfStream? stream;
                try
                {
                    stream = FieldAppearanceGenerator.Generate(widget, store, acroForm, store.OnWarning, i + 1);
                }
                catch (Exception ex) when (!PdfWorkScope.IsAbort(ex) && ex is not OutOfMemoryException)
                {
                    warnings.Add("A form field's appearance on page " + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
                        + " could not be made (" + ex.GetType().Name + "); its stored appearance is kept.");
                    continue;
                }

                if (stream is null)
                {
                    continue;
                }

                Externalise(stream.Dictionary, store, allocate);
                PdfObjectId id = allocate();
                store.Add(new PdfIndirectObject(id, stream));
                if (store.Resolve(stream.Dictionary.TryGetValue(PdfName.Resources, out PdfPrimitive? res) ? res : PdfNull.Value) is PdfDictionary streamResources)
                {
                    resources.Add(streamResources);
                }

                PdfDictionary appearance = new PdfDictionary();
                if (type == "Btn")
                {
                    // A button's normal appearance is a dictionary of states.
                    string state = store.Resolve(widget.TryGetValue(PdfName.Intern("AS"), out PdfPrimitive? asv) ? asv : PdfNull.Value) is PdfName asName
                        ? asName.Value
                        : "Off";
                    PdfDictionary states = new PdfDictionary();
                    states.Set(PdfName.Intern(state), new PdfReference(id));
                    appearance.Set(PdfName.Intern("N"), states);
                    widget.Set(PdfName.Intern("AS"), PdfName.Intern(state));
                }
                else
                {
                    appearance.Set(PdfName.Intern("N"), new PdfReference(id));
                }

                widget.Set(PdfName.Intern("AP"), appearance);
                made++;
            }
        }

        if (acroForm.ContainsKey(PdfName.Intern("NeedAppearances")))
        {
            acroForm.Remove(PdfName.Intern("NeedAppearances"));
            warnings.Add("/NeedAppearances was removed from the form; " + made.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " field appearance(s) were made from the field values.");
        }
        else if (made > 0)
        {
            warnings.Add(made.ToString(System.Globalization.CultureInfo.InvariantCulture) + " form field(s) with no appearance were given one made from the field value.");
        }

        return resources;
    }

    // Moves every stream held directly inside a dictionary or array into an
    // object of its own (a written file can hold a stream only as an object).
    private static void Externalise(PdfDictionary dict, PdfObjectStore store, Func<PdfObjectId> allocate)
    {
        foreach (PdfName key in new List<PdfName>(dict.Keys))
        {
            PdfPrimitive? replaced = ExternaliseValue(dict[key], store, allocate);
            if (replaced is not null)
            {
                dict.Set(key, replaced);
            }
        }
    }

    // The reference to use in place of a direct stream, or null to keep the value.
    private static PdfPrimitive? ExternaliseValue(PdfPrimitive value, PdfObjectStore store, Func<PdfObjectId> allocate)
    {
        switch (value)
        {
            case PdfStream stream:
                Externalise(stream.Dictionary, store, allocate);
                PdfObjectId id = allocate();
                store.Add(new PdfIndirectObject(id, stream));
                return new PdfReference(id);
            case PdfDictionary dict:
                Externalise(dict, store, allocate);
                return null;
            case PdfArray array:
                List<PdfPrimitive> items = new List<PdfPrimitive>(array.Count);
                bool changed = false;
                foreach (PdfPrimitive element in array)
                {
                    PdfPrimitive? item = ExternaliseValue(element, store, allocate);
                    changed |= item is not null;
                    items.Add(item ?? element);
                }

                return changed ? new PdfArray(items) : null;
            default:
                return null;
        }
    }

    private static string? InheritedName(PdfDictionary field, string key, PdfObjectStore store)
    {
        PdfDictionary? d = field;
        for (int depth = 0; d is not null && depth < 32; depth++)
        {
            if (d.TryGetValue(PdfName.Intern(key), out PdfPrimitive? v) && store.Resolve(v) is PdfName name)
            {
                return name.Value;
            }

            d = d.TryGetValue(PdfName.Intern("Parent"), out PdfPrimitive? parent) ? store.Resolve(parent) as PdfDictionary : null;
        }

        return null;
    }

    // Removes /TR, and /TR2 other than /Default, from every graphics state the
    // file reaches through a resource dictionary's /ExtGState.
    private static void RemoveTransferFunctions(PdfObjectStore store, PdfPrimitive root, List<string> warnings)
    {
        int removed = 0;
        HashSet<int> visitedObjects = new HashSet<int>();
        Stack<PdfPrimitive> pending = new Stack<PdfPrimitive>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            PdfPrimitive item = pending.Pop();
            if (item is PdfReference reference)
            {
                if (visitedObjects.Add(reference.ObjectId.ObjectNumber))
                {
                    pending.Push(store.Resolve(reference));
                }

                continue;
            }

            PdfDictionary? dict = item switch
            {
                PdfDictionary d => d,
                PdfStream s => s.Dictionary,
                _ => null,
            };
            if (dict is null)
            {
                if (item is PdfArray array)
                {
                    foreach (PdfPrimitive element in array)
                    {
                        pending.Push(element);
                    }
                }

                continue;
            }

            if (dict.TryGetValue(PdfName.Intern("ExtGState"), out PdfPrimitive? statesRef)
                && store.Resolve(statesRef) is PdfDictionary states)
            {
                foreach (PdfPrimitive stateValue in states.Values)
                {
                    if (store.Resolve(stateValue) is not PdfDictionary state)
                    {
                        continue;
                    }

                    if (state.Remove(PdfName.Intern("TR")))
                    {
                        removed++;
                    }

                    if (state.TryGetValue(PdfName.Intern("TR2"), out PdfPrimitive? tr2)
                        && store.Resolve(tr2) is not PdfName { Value: "Default" })
                    {
                        state.Remove(PdfName.Intern("TR2"));
                        removed++;
                    }
                }
            }

            foreach (PdfPrimitive child in dict.Values)
            {
                pending.Push(child);
            }
        }

        if (removed > 0)
        {
            warnings.Add(removed.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " transfer function(s) were removed from graphics states; colours they adjusted are drawn as stored.");
        }
    }

    // 3.34.0 (#49c): PDF/A wants every annotation printable and shown. An
    // annotation that is neither shown nor printed (Hidden, or Invisible) is
    // removed from its page, so the page looks as before; any other gets the
    // Print flag and loses NoView and ToggleNoView, so one that only showed on
    // screen now prints, and one that only printed now shows too.
    private static void PrepareAnnotations(PdfDocument document, PdfObjectStore store)
    {
        for (int i = 0; i < document.PageCount; i++)
        {
            PdfDictionary page = document.Pages[i].Dictionary;
            if (!page.TryGetValue(PdfName.Intern("Annots"), out PdfPrimitive? annotsPrim)
                || store.Resolve(annotsPrim) is not PdfArray annots)
            {
                continue;
            }

            List<PdfPrimitive> kept = new List<PdfPrimitive>(annots.Count);
            bool removed = false;
            foreach (PdfPrimitive entry in annots)
            {
                if (store.Resolve(entry) is not PdfDictionary annot)
                {
                    kept.Add(entry);
                    continue;
                }

                int flags = annot.GetInteger(PdfName.Intern("F"), 0);
                if ((flags & (FlagHidden | FlagInvisible)) != 0)
                {
                    removed = true;
                    continue;
                }

                int wanted = (flags | FlagPrint) & ~(FlagNoView | FlagToggleNoView);
                if (wanted != flags || !annot.ContainsKey(PdfName.Intern("F")))
                {
                    annot.Set(PdfName.Intern("F"), wanted);
                }

                kept.Add(entry);
            }

            if (removed)
            {
                page.Set(PdfName.Intern("Annots"), new PdfArray(kept));
            }
        }
    }

    // 3.34.0 (#49d): a /CharSet ("/a/b/c") for every Type 1 subset font
    // (a BaseFont with a six-letter tag) that has none, listing the glyphs its
    // embedded program defines (/FontFile, or a /Type1C /FontFile3), .notdef
    // excepted. A program that cannot be read is left as it is.
    private static void AddCharSets(PdfDocument document, PdfObjectStore store)
    {
        HashSet<int> done = new HashSet<int>();
        foreach (PdfIndirectObject obj in CollectFontDescriptors(document, store))
        {
            if (!done.Add(obj.Id.ObjectNumber) || obj.Value is not PdfDictionary descriptor
                || descriptor.ContainsKey(PdfName.Intern("CharSet"))
                || descriptor.GetName(PdfName.Intern("FontName"))?.Value is not string fontName
                || !IsSubsetName(fontName))
            {
                continue;
            }

            IReadOnlyCollection<string> names;
            try
            {
                names = GlyphNames(descriptor, store);
            }
            catch (Exception ex) when (!PdfWorkScope.IsAbort(ex))
            {
                continue;
            }

            if (names.Count == 0)
            {
                continue;
            }

            List<string> sorted = new List<string>(names);
            sorted.Remove(".notdef");
            sorted.Sort(StringComparer.Ordinal);
            System.Text.StringBuilder charSet = new System.Text.StringBuilder();
            foreach (string name in sorted)
            {
                charSet.Append('/').Append(name);
            }

            descriptor.Set(PdfName.Intern("CharSet"), new PdfString(System.Text.Encoding.Latin1.GetBytes(charSet.ToString())));
        }
    }

    private static readonly PdfName FontFile2Key = PdfName.Intern("FontFile2");
    private static readonly PdfName FontFile3Key = PdfName.Intern("FontFile3");
    private static readonly PdfName CidToGidMapKey = PdfName.Intern("CIDToGIDMap");
    private static readonly PdfName CidSetKey = PdfName.Intern("CIDSet");
    private static readonly PdfName DescendantFontsKey = PdfName.Intern("DescendantFonts");
    private static readonly PdfName FontDescriptorKey = PdfName.Intern("FontDescriptor");

    private readonly record struct CidFont(
        PdfDictionary Descriptor, PdfObjectId DescriptorId, PdfDictionary CidFontDict, bool IsTrueType);

    // PDA-01: a subset CID font must carry a /CIDSet marking the CIDs its
    // embedded program contains (ISO 19005-1 §6.3.5-3, ISO 19005-2
    // §6.2.11.4.2). Rebuilds it from the program for every embedded CID-font
    // subset the pages use, replacing a missing or incomplete one. The file is
    // refused when the program cannot be read.
    private static void RebuildCidSets(
        PdfDocument document, PdfObjectStore store, Func<PdfObjectId> allocate, List<string> violations)
    {
        HashSet<int> done = new HashSet<int>();
        foreach (CidFont font in CollectCidFonts(document, store))
        {
            if (!done.Add(font.DescriptorId.ObjectNumber))
            {
                continue;
            }

            byte[]? cidSet;
            try
            {
                cidSet = BuildCidSet(font, store);
            }
            catch (Exception ex) when (!PdfWorkScope.IsAbort(ex))
            {
                violations.Add(
                    "A subset CID font's embedded program could not be read to rebuild its /CIDSet ("
                    + ex.Message + "); PDF/A conformance cannot be assured.");
                continue;
            }

            if (cidSet is null)
            {
                continue;
            }

            PdfObjectId id = allocate();
            store.Add(new PdfIndirectObject(id, new PdfStream(new PdfDictionary(), cidSet)));
            font.Descriptor.Set(CidSetKey, new PdfReference(id));
        }
    }

    // PDA-01 part 3: refuses a CID font whose declared /W widths disagree with
    // the embedded program's glyph advances by more than one unit (the gap is
    // in 1000ths of an em, so one unit absorbs ordinary rounding). The
    // ReportLab bug writes widths that do not match, which veraPDF rejects
    // (ISO 19005-2 §6.2.11.5); correcting them would move glyphs, so the file
    // is refused rather than silently rewritten.
    private static void CheckCidWidths(PdfDocument document, PdfObjectStore store, List<string> violations)
    {
        HashSet<int> done = new HashSet<int>();
        foreach (CidFont font in CollectCidFonts(document, store))
        {
            if (!done.Add(font.DescriptorId.ObjectNumber) || !WidthsMismatchProgram(font, store))
            {
                continue;
            }

            violations.Add(
                "A CID font's /W widths do not match its embedded program's glyph advances, "
                + "so PDF/A conformance cannot be assured; re-embed the font with correct widths.");
        }
    }

    // A simple embedded TrueType font and the object number of its font
    // dictionary (used to correlate with the glyphs the content streams show).
    private readonly record struct SimpleTrueTypeFont(
        int FontObjectNumber, PdfDictionary FontDict, PdfDictionary Descriptor);

    private static void CheckSimpleFontWidths(PdfDocument document, PdfObjectStore store, List<string> violations)
    {
        List<SimpleTrueTypeFont> fonts = CollectSimpleTrueTypeFonts(document, store);
        if (fonts.Count == 0)
        {
            return;
        }

        UsedGlyphCollector collector = new UsedGlyphCollector(store);
        for (int i = 0; i < document.PageCount; i++)
        {
            PdfPage page = document.Pages[i];
            collector.BeginPage(page.Resources);
            byte[] content = ContentStreamLoader.Load(page.Contents, store, page.PageNumber);
            ContentStreamWalker.Walk(content, collector);
        }

        IReadOnlyDictionary<int, HashSet<int>> used = collector.UsedCodes;
        HashSet<int> done = new HashSet<int>();
        foreach (SimpleTrueTypeFont font in fonts)
        {
            if (!done.Add(font.FontObjectNumber)
                || !used.TryGetValue(font.FontObjectNumber, out HashSet<int>? codes)
                || codes.Count == 0
                || !SimpleWidthsMismatch(font, codes, store))
            {
                continue;
            }

            violations.Add(
                "A simple TrueType font's /Widths do not match its embedded program's glyph "
                + "advances for glyphs it renders, so PDF/A conformance cannot be assured; "
                + "re-embed the font with correct widths.");
        }
    }

    // True when any rendered code's declared width differs from the embedded
    // program's advance for the cmap-mapped glyph by more than one unit (ISO
    // 19005-2 §6.2.11.5, 1-unit tolerance). Codes the font's cmap does not map,
    // and codes outside the /Widths range, are skipped rather than guessed.
    private static bool SimpleWidthsMismatch(SimpleTrueTypeFont font, HashSet<int> codes, PdfObjectStore store)
    {
        if (store.Resolve(Lookup(font.FontDict, PdfName.Intern("FirstChar"))) is not PdfInteger firstChar
            || store.Resolve(Lookup(font.FontDict, PdfName.Intern("Widths"))) is not PdfArray widths
            || store.Resolve(Lookup(font.Descriptor, FontFile2Key)) is not PdfStream program)
        {
            return false;
        }

        Chuvadi.Pdf.Fonts.Rendering.TrueTypeLoader loader =
            new Chuvadi.Pdf.Fonts.Rendering.TrueTypeLoader(Decode(program));
        int unitsPerEm = loader.UnitsPerEm;
        if (unitsPerEm <= 0)
        {
            return false;
        }

        // Only symbolic fonts are checked. A symbolic TrueType font has no
        // /Encoding and selects glyphs straight from its cmap (ISO 32000-1
        // §9.6.6.4), so the code→glyph mapping — and therefore the width
        // comparison — is unambiguous. A non-symbolic font maps codes through a
        // base encoding and an optional /Differences array to glyph names first,
        // so its declared width belongs to the named glyph, not to the raw
        // cmap[code] glyph; checking it without resolving that encoding would
        // wrongly flag correct fonts, so a non-symbolic font is left for the
        // renderer-accurate path and skipped here.
        bool symbolic = store.Resolve(Lookup(font.Descriptor, PdfName.Intern("Flags"))) is PdfInteger flags
            && (flags.Value & 4) != 0;
        if (!symbolic)
        {
            return false;
        }

        int glyphCount = loader.NumGlyphs;
        foreach (int code in codes)
        {
            int index = code - firstChar.Value;
            if (index < 0 || index >= widths.Count || AsDouble(store.Resolve(widths[index])) is not double declared || declared == 0.0)
            {
                continue;
            }

            int gid = loader.GetGlyphIndexFromCmap(code, symbolic);
            if (gid <= 0 || gid >= glyphCount)
            {
                continue;
            }

            double expected = Math.Round(loader.GetGlyphMetrics(gid).AdvanceWidth * 1000.0 / unitsPerEm);
            if (Math.Abs(declared - expected) > 1.0)
            {
                return true;
            }
        }

        return false;
    }

    // The simple embedded TrueType fonts (Subtype /TrueType with an embedded
    // /FontFile2, /Widths and /FirstChar) the pages use, with their font
    // dictionary object numbers.
    private static List<SimpleTrueTypeFont> CollectSimpleTrueTypeFonts(PdfDocument document, PdfObjectStore store)
    {
        List<SimpleTrueTypeFont> result = new List<SimpleTrueTypeFont>();
        HashSet<int> visited = new HashSet<int>();
        Stack<PdfDictionary> resources = new Stack<PdfDictionary>();
        for (int i = 0; i < document.PageCount; i++)
        {
            if (document.Pages[i].Resources is PdfDictionary r)
            {
                resources.Push(r);
            }
        }

        while (resources.Count > 0)
        {
            PdfDictionary res = resources.Pop();
            if (store.ResolveAs<PdfDictionary>(res.TryGetValue(PdfName.Intern("Font"), out PdfPrimitive? f) ? f : PdfNull.Value) is PdfDictionary fonts)
            {
                foreach (PdfPrimitive value in fonts.Values)
                {
                    if (value is not PdfReference fontRef
                        || !visited.Add(fontRef.ObjectId.ObjectNumber)
                        || store.Resolve(fontRef) is not PdfDictionary font
                        || font.GetName(PdfName.Subtype)?.Value != "TrueType"
                        || store.Resolve(font.TryGetValue(FontDescriptorKey, out PdfPrimitive? d) ? d : PdfNull.Value) is not PdfDictionary descriptor)
                    {
                        continue;
                    }

                    result.Add(new SimpleTrueTypeFont(fontRef.ObjectId.ObjectNumber, font, descriptor));
                }
            }

            if (store.ResolveAs<PdfDictionary>(res.TryGetValue(PdfName.XObject, out PdfPrimitive? x) ? x : PdfNull.Value) is PdfDictionary xobjects)
            {
                foreach (PdfPrimitive value in xobjects.Values)
                {
                    if (value is PdfReference xref && visited.Add(xref.ObjectId.ObjectNumber)
                        && store.Resolve(xref) is PdfStream formObj
                        && store.ResolveAs<PdfDictionary>(formObj.Dictionary.TryGetValue(PdfName.Resources, out PdfPrimitive? fr) ? fr : PdfNull.Value) is PdfDictionary formResources)
                    {
                        resources.Push(formResources);
                    }
                }
            }
        }

        return result;
    }

    // Collects, per font-dictionary object number, the single-byte codes the
    // content streams actually render (text-rendering mode 3, invisible, is
    // skipped, matching ISO 19005-2 §6.2.11.5). Form XObjects are walked too.
    private sealed class UsedGlyphCollector : IContentOperatorSink
    {
        private readonly PdfObjectStore _store;
        private readonly Dictionary<int, HashSet<int>> _used = new Dictionary<int, HashSet<int>>();
        private readonly HashSet<int> _visitedForms = new HashSet<int>();
        private PdfDictionary? _resources;
        private int _currentFontObject = -1;
        private int _renderMode;

        internal UsedGlyphCollector(PdfObjectStore store)
        {
            _store = store;
        }

        internal IReadOnlyDictionary<int, HashSet<int>> UsedCodes => _used;

        internal void BeginPage(PdfDictionary? resources)
        {
            _resources = resources;
            _currentFontObject = -1;
            _renderMode = 0;
        }

        public void SetFont(string name, double size)
        {
            _currentFontObject = -1;
            if (SubDictionary(PdfName.Intern("Font")) is PdfDictionary fonts
                && fonts.TryGetValue(PdfName.Intern(name), out PdfPrimitive? value)
                && value is PdfReference fontRef)
            {
                _currentFontObject = fontRef.ObjectId.ObjectNumber;
            }
        }

        public void SetTextRenderingMode(int mode)
        {
            _renderMode = mode;
        }

        public void ShowText(byte[] text)
        {
            Record(text);
        }

        public void MoveNextLineShowText(byte[] text)
        {
            Record(text);
        }

        public void SetSpacingMoveNextLineShowText(double wordSpacing, double charSpacing, byte[] text)
        {
            Record(text);
        }

        public void ShowTextArray(IReadOnlyList<TextArrayElement> elements)
        {
            foreach (TextArrayElement element in elements)
            {
                if (element.IsText && element.Text is byte[] bytes)
                {
                    Record(bytes);
                }
            }
        }

        public void InvokeXObject(string name)
        {
            if (SubDictionary(PdfName.XObject) is not PdfDictionary xobjects
                || !xobjects.TryGetValue(PdfName.Intern(name), out PdfPrimitive? value)
                || value is not PdfReference formRef
                || !_visitedForms.Add(formRef.ObjectId.ObjectNumber)
                || _store.Resolve(formRef) is not PdfStream form
                || form.Dictionary.GetName(PdfName.Subtype)?.Value != "Form")
            {
                return;
            }

            PdfDictionary? outer = _resources;
            int outerFont = _currentFontObject;
            int outerMode = _renderMode;
            _resources = form.Dictionary.TryGetValue(PdfName.Resources, out PdfPrimitive? fr)
                && _store.ResolveAs<PdfDictionary>(fr) is PdfDictionary formResources
                ? formResources
                : outer;
            ContentStreamWalker.Walk(ContentStreamLoader.Load(formRef, _store), this);
            _resources = outer;
            _currentFontObject = outerFont;
            _renderMode = outerMode;
        }

        private PdfDictionary? SubDictionary(PdfName key)
        {
            if (_resources is null || !_resources.TryGetValue(key, out PdfPrimitive? value))
            {
                return null;
            }

            return _store.ResolveAs<PdfDictionary>(value);
        }

        private void Record(byte[] text)
        {
            if (_currentFontObject < 0 || _renderMode == 3 || text.Length == 0)
            {
                return;
            }

            if (!_used.TryGetValue(_currentFontObject, out HashSet<int>? codes))
            {
                codes = new HashSet<int>();
                _used[_currentFontObject] = codes;
            }

            foreach (byte b in text)
            {
                codes.Add(b);
            }
        }
    }

    private static bool WidthsMismatchProgram(CidFont font, PdfObjectStore store)
    {
        Dictionary<int, double> declared = ParseCidWidths(font.CidFontDict, store);
        if (declared.Count == 0)
        {
            return false;
        }

        int unitsPerEm;
        Func<int, int> advanceForCid;
        if (font.IsTrueType)
        {
            if (store.Resolve(Lookup(font.Descriptor, FontFile2Key)) is not PdfStream program)
            {
                return false;
            }

            Chuvadi.Pdf.Fonts.Rendering.TrueTypeLoader loader =
                new Chuvadi.Pdf.Fonts.Rendering.TrueTypeLoader(Decode(program));
            unitsPerEm = loader.UnitsPerEm;
            int glyphCount = loader.NumGlyphs;
            Func<int, int> cidToGid = BuildCidToGid(font.CidFontDict, store, glyphCount);
            advanceForCid = cid =>
            {
                int gid = cidToGid(cid);
                return gid >= 0 && gid < glyphCount ? loader.GetGlyphMetrics(gid).AdvanceWidth : -1;
            };
        }
        else
        {
            if (store.Resolve(Lookup(font.Descriptor, FontFile3Key)) is not PdfStream program)
            {
                return false;
            }

            Chuvadi.Pdf.Fonts.Rendering.CffLoader loader =
                new Chuvadi.Pdf.Fonts.Rendering.CffLoader(Decode(program));
            unitsPerEm = loader.UnitsPerEm;
            int glyphCount = loader.NumGlyphs;
            bool cidKeyed = loader.IsCidFont;
            IReadOnlyDictionary<int, int> cidToGid = loader.CidToGid;
            advanceForCid = cid =>
            {
                int gid = cidKeyed ? (cidToGid.TryGetValue(cid, out int g) ? g : -1) : cid;
                return gid >= 0 && gid < glyphCount ? loader.GetGlyphMetrics(gid).AdvanceWidth : -1;
            };
        }

        if (unitsPerEm <= 0)
        {
            return false;
        }

        foreach (KeyValuePair<int, double> entry in declared)
        {
            int advance = advanceForCid(entry.Key);
            if (advance < 0)
            {
                continue;
            }

            double expected = Math.Round(advance * 1000.0 / unitsPerEm);
            if (Math.Abs(entry.Value - expected) > 1.0)
            {
                return true;
            }
        }

        return false;
    }

    // Maps each CID that /W assigns a width to that width (in 1000ths of an em).
    private static Dictionary<int, double> ParseCidWidths(PdfDictionary cidFontDict, PdfObjectStore store)
    {
        Dictionary<int, double> widths = new Dictionary<int, double>();
        if (store.Resolve(Lookup(cidFontDict, PdfName.Intern("W"))) is not PdfArray w)
        {
            return widths;
        }

        int i = 0;
        while (i < w.Count)
        {
            if (store.Resolve(w[i]) is not PdfInteger first)
            {
                i++;
                continue;
            }

            if (i + 1 >= w.Count)
            {
                break;
            }

            PdfPrimitive second = store.Resolve(w[i + 1]);
            if (second is PdfArray inner)
            {
                for (int j = 0; j < inner.Count; j++)
                {
                    if (AsDouble(store.Resolve(inner[j])) is double width)
                    {
                        widths[first.Value + j] = width;
                    }
                }

                i += 2;
            }
            else if (i + 2 < w.Count && second is PdfInteger last && AsDouble(store.Resolve(w[i + 2])) is double range)
            {
                for (int c = first.Value; c <= last.Value && c - first.Value < 65536; c++)
                {
                    widths[c] = range;
                }

                i += 3;
            }
            else
            {
                i++;
            }
        }

        return widths;
    }

    private static double? AsDouble(PdfPrimitive value) => value switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => null,
    };

    // A CID→GID mapping for a CIDFontType2 (Identity, or a /CIDToGIDMap stream).
    private static Func<int, int> BuildCidToGid(PdfDictionary cidFontDict, PdfObjectStore store, int glyphCount)
    {
        if (store.Resolve(Lookup(cidFontDict, CidToGidMapKey)) is PdfStream mapStream)
        {
            byte[] map = Decode(mapStream);
            return cid =>
            {
                int offset = cid * 2;
                return offset + 1 < map.Length ? (map[offset] << 8) | map[offset + 1] : -1;
            };
        }

        return cid => cid < glyphCount ? cid : -1;
    }

    // The bytes of a /CIDSet for the font, or null when there is no embedded
    // program to read. Bit c (the high bit of byte c/8 first) is set for every
    // CID the program contains.
    private static byte[]? BuildCidSet(CidFont font, PdfObjectStore store)
    {
        SortedSet<int> present = new SortedSet<int>();
        if (font.IsTrueType)
        {
            if (store.Resolve(Lookup(font.Descriptor, FontFile2Key)) is not PdfStream program)
            {
                return null;
            }

            Chuvadi.Pdf.Fonts.Rendering.TrueTypeLoader loader = new Chuvadi.Pdf.Fonts.Rendering.TrueTypeLoader(Decode(program));
            int numGlyphs = loader.NumGlyphs;
            if (store.Resolve(Lookup(font.CidFontDict, CidToGidMapKey)) is PdfStream mapStream)
            {
                byte[] map = Decode(mapStream);
                for (int c = 0; (c * 2) + 1 < map.Length; c++)
                {
                    int gid = (map[c * 2] << 8) | map[(c * 2) + 1];
                    if (c == 0 || (gid > 0 && gid < numGlyphs))
                    {
                        present.Add(c);
                    }
                }
            }
            else
            {
                for (int g = 0; g < numGlyphs; g++)
                {
                    present.Add(g);
                }
            }
        }
        else
        {
            if (store.Resolve(Lookup(font.Descriptor, FontFile3Key)) is not PdfStream program)
            {
                return null;
            }

            Chuvadi.Pdf.Fonts.Rendering.CffLoader loader = new Chuvadi.Pdf.Fonts.Rendering.CffLoader(Decode(program));
            if (loader.IsCidFont)
            {
                foreach (int cid in loader.CidToGid.Keys)
                {
                    present.Add(cid);
                }
            }
            else
            {
                for (int g = 0; g < loader.NumGlyphs; g++)
                {
                    present.Add(g);
                }
            }
        }

        if (present.Count == 0)
        {
            return null;
        }

        int maxCid = present.Max;
        byte[] bits = new byte[(maxCid / 8) + 1];
        foreach (int cid in present)
        {
            bits[cid / 8] |= (byte)(0x80 >> (cid % 8));
        }

        return bits;
    }

    private static PdfPrimitive Lookup(PdfDictionary dict, PdfName key) =>
        dict.TryGetValue(key, out PdfPrimitive? value) && value is not null ? value : PdfNull.Value;

    // The subset CID fonts (Type 0 with a CIDFontType0/2 descendant) the pages
    // use, with their descriptors.
    private static List<CidFont> CollectCidFonts(PdfDocument document, PdfObjectStore store)
    {
        List<CidFont> result = new List<CidFont>();
        HashSet<int> visited = new HashSet<int>();
        Stack<PdfDictionary> resources = new Stack<PdfDictionary>();
        for (int i = 0; i < document.PageCount; i++)
        {
            if (document.Pages[i].Resources is PdfDictionary r)
            {
                resources.Push(r);
            }
        }

        while (resources.Count > 0)
        {
            PdfDictionary res = resources.Pop();
            if (store.ResolveAs<PdfDictionary>(res.TryGetValue(PdfName.Intern("Font"), out PdfPrimitive? f) ? f : PdfNull.Value) is PdfDictionary fonts)
            {
                foreach (PdfPrimitive value in fonts.Values)
                {
                    if (store.Resolve(value) is not PdfDictionary type0 || type0.GetName(PdfName.Subtype)?.Value != "Type0"
                        || store.Resolve(type0.TryGetValue(DescendantFontsKey, out PdfPrimitive? df) ? df : PdfNull.Value) is not PdfArray descendants
                        || descendants.Count == 0 || store.Resolve(descendants[0]) is not PdfDictionary cidFont)
                    {
                        continue;
                    }

                    string? subtype = cidFont.GetName(PdfName.Subtype)?.Value;
                    if (subtype is not ("CIDFontType0" or "CIDFontType2")
                        || !cidFont.TryGetValue(FontDescriptorKey, out PdfPrimitive? d) || d is not PdfReference descRef
                        || !visited.Add(descRef.ObjectId.ObjectNumber) || store.Resolve(descRef) is not PdfDictionary descriptor)
                    {
                        continue;
                    }

                    // Every embedded CID font gets a /CIDSet rebuilt from its
                    // program (required for a subset, permitted otherwise); a
                    // font with no embedded program is skipped in BuildCidSet.
                    result.Add(new CidFont(descriptor, descRef.ObjectId, cidFont, subtype == "CIDFontType2"));
                }
            }

            if (store.ResolveAs<PdfDictionary>(res.TryGetValue(PdfName.XObject, out PdfPrimitive? x) ? x : PdfNull.Value) is PdfDictionary xobjects)
            {
                foreach (PdfPrimitive value in xobjects.Values)
                {
                    if (value is PdfReference xref && visited.Add(xref.ObjectId.ObjectNumber)
                        && store.Resolve(xref) is PdfStream form
                        && store.ResolveAs<PdfDictionary>(form.Dictionary.TryGetValue(PdfName.Resources, out PdfPrimitive? fr) ? fr : PdfNull.Value) is PdfDictionary formResources)
                    {
                        resources.Push(formResources);
                    }
                }
            }
        }

        return result;
    }

    // The font descriptors of the simple Type 1 fonts the pages use.
    private static List<PdfIndirectObject> CollectFontDescriptors(PdfDocument document, PdfObjectStore store)
    {
        List<PdfIndirectObject> result = new List<PdfIndirectObject>();
        HashSet<int> visited = new HashSet<int>();
        Stack<PdfDictionary> resources = new Stack<PdfDictionary>();
        for (int i = 0; i < document.PageCount; i++)
        {
            if (document.Pages[i].Resources is PdfDictionary r)
            {
                resources.Push(r);
            }
        }

        while (resources.Count > 0)
        {
            PdfDictionary res = resources.Pop();
            if (store.ResolveAs<PdfDictionary>(res.TryGetValue(PdfName.Intern("Font"), out PdfPrimitive? f) ? f : PdfNull.Value) is PdfDictionary fonts)
            {
                foreach (PdfPrimitive value in fonts.Values)
                {
                    if (store.Resolve(value) is PdfDictionary font
                        && font.GetName(PdfName.Subtype)?.Value is "Type1" or "MMType1"
                        && font.TryGetValue(PdfName.Intern("FontDescriptor"), out PdfPrimitive? d)
                        && d is PdfReference descriptorRef
                        && visited.Add(descriptorRef.ObjectId.ObjectNumber)
                        && store.Resolve(descriptorRef) is PdfDictionary descriptor)
                    {
                        result.Add(new PdfIndirectObject(descriptorRef.ObjectId, descriptor));
                    }
                }
            }

            if (store.ResolveAs<PdfDictionary>(res.TryGetValue(PdfName.XObject, out PdfPrimitive? x) ? x : PdfNull.Value) is PdfDictionary xobjects)
            {
                foreach (PdfPrimitive value in xobjects.Values)
                {
                    if (value is PdfReference xref && visited.Add(xref.ObjectId.ObjectNumber)
                        && store.Resolve(xref) is PdfStream form
                        && store.ResolveAs<PdfDictionary>(form.Dictionary.TryGetValue(PdfName.Resources, out PdfPrimitive? fr) ? fr : PdfNull.Value) is PdfDictionary formResources)
                    {
                        resources.Push(formResources);
                    }
                }
            }
        }

        return result;
    }

    private static bool IsSubsetName(string name) =>
        name.Length > 7 && name[6] == '+' && name.AsSpan(0, 6).IndexOfAnyExceptInRange('A', 'Z') < 0;

    private static IReadOnlyCollection<string> GlyphNames(PdfDictionary descriptor, PdfObjectStore store)
    {
        if (store.Resolve(descriptor.TryGetValue(PdfName.Intern("FontFile"), out PdfPrimitive? ff) ? ff : PdfNull.Value) is PdfStream type1)
        {
            return Chuvadi.Pdf.Fonts.Rendering.Type1FontRenderer.CharStringNames(Decode(type1));
        }

        if (store.Resolve(descriptor.TryGetValue(PdfName.Intern("FontFile3"), out PdfPrimitive? ff3) ? ff3 : PdfNull.Value) is PdfStream cff
            && cff.Dictionary.GetName(PdfName.Subtype)?.Value == "Type1C")
        {
            return new List<string>(new Chuvadi.Pdf.Fonts.Rendering.CffLoader(Decode(cff)).GlyphNameToGid.Keys);
        }

        return Array.Empty<string>();
    }

    private static byte[] Decode(PdfStream stream)
    {
        Chuvadi.Pdf.Filters.FilterPipeline pipeline = Chuvadi.Pdf.Filters.FilterRegistry.CreateDefaultPipeline();
        byte[] data = stream.RawBytes;
        PdfPrimitive? filter = stream.Filter;
        if (filter is PdfName single)
        {
            return pipeline.Decode(Chuvadi.Pdf.Filters.FilterRegistry.ResolveAlias(single.Value), data, null);
        }

        if (filter is PdfArray chain)
        {
            foreach (PdfPrimitive item in chain)
            {
                if (item is PdfName name)
                {
                    data = pipeline.Decode(Chuvadi.Pdf.Filters.FilterRegistry.ResolveAlias(name.Value), data, null);
                }
            }
        }

        return data;
    }

    // 3.34.0 (#49g, D-364): the XMP form of an /Info date, or null to leave
    // it out; a warning says why a date is left out or how it was written.
    private static string? InfoDate(string? pdfDate, string infoKey, string xmpKey, PdfAOptions options, List<string> warnings)
    {
        if (pdfDate is null)
        {
            return null;
        }

        string? xmp = XmpDate(pdfDate, options.AssumeUtcForZonelessDates, out bool zoneless);
        if (xmp is null)
        {
            warnings.Add(zoneless
                ? $"/Info {infoKey} '{pdfDate}' gives no time zone, so {xmpKey} was left out of the XMP metadata: a date without a zone has no known relation to UT, and validators read an XMP date without one in their own local zone. Set PdfAOptions.AssumeUtcForZonelessDates to write it as UTC."
                : $"/Info {infoKey} '{pdfDate}' is not a date that can be read, so {xmpKey} was left out of the XMP metadata.");
        }
        else if (zoneless)
        {
            warnings.Add($"/Info {infoKey} '{pdfDate}' gives no time zone; {xmpKey} was written as UTC ({xmp}) because PdfAOptions.AssumeUtcForZonelessDates is set.");
        }

        return xmp;
    }

    // 3.34.0 (#49g): a PDF date (D:YYYYMMDDHHmmSSOHH'mm', later parts
    // optional) as an XMP date (ISO 8601: YYYY-MM-DDThh:mm:ss+hh:mm); null
    // when it cannot be read, and null when it gives no time zone. A PDF date
    // without a zone has no known relation to UT (§7.9.4), and validators
    // read an XMP date without one in their own local zone, so writing it
    // would make PDF/A-1b's "equivalent" check (6.7.3) depend on where the
    // file is checked; such a date stays out of the XMP, as before 3.34.0.
    internal static string? XmpDate(string? pdfDate) => XmpDate(pdfDate, assumeUtc: false, out _);

    // As above; with assumeUtc (D-364) a date without a time zone is written
    // as UTC, its omitted parts taking the §7.9.4 defaults (month and day 01,
    // the rest zero). zoneless says the date gave no time zone.
    internal static string? XmpDate(string? pdfDate, bool assumeUtc, out bool zoneless)
    {
        zoneless = false;
        if (pdfDate is null)
        {
            return null;
        }

        string d = pdfDate.Trim();
        if (d.StartsWith("D:", StringComparison.Ordinal))
        {
            d = d[2..];
        }

        int pos = 0;
        int? Part(int length, int min, int max)
        {
            if (pos + length > d.Length || !int.TryParse(d.AsSpan(pos, length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int v) || v < min || v > max)
            {
                return null;
            }

            pos += length;
            return v;
        }

        bool AtEnd() => pos == d.Length;

        if (Part(4, 0, 9999) is not int year)
        {
            return null;
        }

        int? month = Part(2, 1, 12);
        int? day = month is null ? null : Part(2, 1, 31);
        int? hour = day is null ? null : Part(2, 0, 23);
        int? minute = hour is null ? null : Part(2, 0, 59);
        int? second = minute is null ? null : Part(2, 0, 59);
        if (hour is null && !AtEnd())
        {
            return null; // damaged: a date part that is not a number
        }

        string local = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{year:D4}-{month ?? 1:D2}-{day ?? 1:D2}T{hour ?? 0:D2}:{minute ?? 0:D2}:{second ?? 0:D2}");
        if (AtEnd())
        {
            zoneless = true;
            return assumeUtc ? local + "Z" : null;
        }

        char sign = d[pos++];
        if (sign == 'Z')
        {
            return hour is null ? null : local + "Z";
        }

        if (hour is null || sign is not ('+' or '-') || Part(2, 0, 23) is not int offsetHours)
        {
            return null; // a damaged offset
        }

        if (pos < d.Length && d[pos] == '\'')
        {
            pos++;
        }

        int offsetMinutes = Part(2, 0, 59) ?? 0;
        return local + string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{sign}{offsetHours:D2}:{offsetMinutes:D2}");
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

    // Depth-first, in the same order as before; iterative (3.35.0, DOC-01).
    private static void CollectReachable(
        PdfObjectStore store,
        PdfPrimitive? primitive,
        List<PdfIndirectObject> collected,
        HashSet<int> visited)
    {
        if (primitive is null)
        {
            return;
        }

        Stack<PdfPrimitive> pending = new Stack<PdfPrimitive>();
        pending.Push(primitive);
        while (pending.Count > 0)
        {
            PdfPrimitive current = pending.Pop();
            if (current is PdfReference reference)
            {
                if (!visited.Add(reference.ObjectId.ObjectNumber))
                {
                    continue;
                }

                PdfPrimitive resolved = store.Resolve(reference);
                collected.Add(new PdfIndirectObject(reference.ObjectId, resolved));
                pending.Push(resolved);
            }
            else if (current is PdfDictionary dictionary)
            {
                PushReversed(dictionary, pending, skipParent: true);
            }
            else if (current is PdfArray array)
            {
                for (int i = array.Count - 1; i >= 0; i--)
                {
                    pending.Push(array[i]);
                }
            }
            else if (current is PdfStream stream)
            {
                PushReversed(stream.Dictionary, pending, skipParent: false);
            }
        }
    }

    private static void PushReversed(PdfDictionary dictionary, Stack<PdfPrimitive> pending, bool skipParent)
    {
        List<PdfPrimitive> values = new List<PdfPrimitive>(dictionary.Count);
        foreach (KeyValuePair<PdfName, PdfPrimitive> entry in dictionary)
        {
            if (!(skipParent && entry.Key == PdfName.Parent))
            {
                values.Add(entry.Value);
            }
        }

        for (int i = values.Count - 1; i >= 0; i--)
        {
            pending.Push(values[i]);
        }
    }

}
