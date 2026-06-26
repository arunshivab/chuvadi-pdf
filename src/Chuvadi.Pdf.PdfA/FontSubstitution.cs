// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.6 (simple fonts), ISO 19005-1 §6.3.4 (font embedding)
// PHASE: Phase 3 — PDF/A font embedding
//
// Walks a document's font resources and replaces non-embedded Standard-14 simple
// fonts with embedded, subsetted Liberation substitutes (WinAnsiEncoding). Other
// non-embedded fonts are reported as violations. The substitute byte source is
// injected so the bundling strategy is decided by the caller.

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.PdfA;

internal static class FontSubstitution
{
    private static readonly PdfName FontKey = PdfName.Intern("Font");
    private static readonly PdfName XObjectKey = PdfName.Intern("XObject");
    private static readonly PdfName ResourcesKey = PdfName.Intern("Resources");
    private static readonly PdfName SubtypeKey = PdfName.Intern("Subtype");
    private static readonly PdfName BaseFontKey = PdfName.Intern("BaseFont");
    private static readonly PdfName FontDescriptorKey = PdfName.Intern("FontDescriptor");
    private static readonly PdfName DescendantFontsKey = PdfName.Intern("DescendantFonts");

    /// <summary>
    /// Replaces non-embedded Standard-14 simple fonts in <paramref name="document"/>
    /// with embedded Liberation substitutes, registering new objects in the
    /// document's store and rewriting the resource references in place.
    /// </summary>
    /// <param name="document">The document to process.</param>
    /// <param name="allocate">Allocates a fresh object id.</param>
    /// <param name="fontProvider">Returns the TTF bytes for a Liberation face key, or null.</param>
    /// <param name="violations">Receives messages for fonts that could not be embedded.</param>
    /// <exception cref="ArgumentNullException">A parameter is null.</exception>
    internal static void SubstituteStandard14(
        PdfDocument document,
        Func<PdfObjectId> allocate,
        Func<string, byte[]?> fontProvider,
        IList<string> violations)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(allocate);
        ArgumentNullException.ThrowIfNull(fontProvider);
        ArgumentNullException.ThrowIfNull(violations);

        PdfObjectStore store = document.Objects;
        Dictionary<string, PdfObjectId> faceCache = new Dictionary<string, PdfObjectId>(StringComparer.Ordinal);
        HashSet<PdfDictionary> visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);

        for (int i = 0; i < document.Pages.Count; i++)
        {
            PdfDictionary? resources = document.Pages[i].Resources;
            if (resources is not null)
            {
                ProcessResources(resources, store, allocate, fontProvider, violations, faceCache, visited);
            }
        }
    }

    private static void ProcessResources(
        PdfDictionary resources,
        PdfObjectStore store,
        Func<PdfObjectId> allocate,
        Func<string, byte[]?> fontProvider,
        IList<string> violations,
        Dictionary<string, PdfObjectId> faceCache,
        HashSet<PdfDictionary> visited)
    {
        if (!visited.Add(resources))
        {
            return;
        }

        if (resources.TryGetValue(FontKey, out PdfPrimitive? fontsRef)
            && store.Resolve(fontsRef) is PdfDictionary fonts)
        {
            foreach (PdfName key in new List<PdfName>(fonts.Keys))
            {
                if (fonts.TryGetValue(key, out PdfPrimitive? value)
                    && store.Resolve(value) is PdfDictionary fontDict)
                {
                    ProcessFont(key, fontDict, fonts, store, allocate, fontProvider, violations, faceCache);
                }
            }
        }

        if (resources.TryGetValue(XObjectKey, out PdfPrimitive? xobjRef)
            && store.Resolve(xobjRef) is PdfDictionary xobjects)
        {
            foreach (PdfPrimitive value in new List<PdfPrimitive>(xobjects.Values))
            {
                if (store.Resolve(value) is PdfStream form
                    && form.Dictionary.TryGetValue(ResourcesKey, out PdfPrimitive? nestedRef)
                    && store.Resolve(nestedRef) is PdfDictionary nested)
                {
                    ProcessResources(nested, store, allocate, fontProvider, violations, faceCache, visited);
                }
            }
        }
    }

    private static void ProcessFont(
        PdfName key,
        PdfDictionary fontDict,
        PdfDictionary fonts,
        PdfObjectStore store,
        Func<PdfObjectId> allocate,
        Func<string, byte[]?> fontProvider,
        IList<string> violations,
        Dictionary<string, PdfObjectId> faceCache)
    {
        if (IsEmbedded(fontDict, store))
        {
            return;
        }

        string? baseFont = NameValue(store, fontDict, BaseFontKey);
        if (baseFont is null)
        {
            violations.Add("A font has no /BaseFont and cannot be embedded.");
            return;
        }

        string? subtype = NameValue(store, fontDict, SubtypeKey);
        if (subtype == "Type0")
        {
            violations.Add($"Non-embedded composite (Type0) font '{baseFont}' cannot be auto-embedded.");
            return;
        }

        Standard14Substitute? substitute = Standard14Map.Lookup(baseFont);
        if (substitute is null)
        {
            violations.Add($"Non-embedded font '{baseFont}' has no Standard-14 substitute.");
            return;
        }

        if (!faceCache.TryGetValue(substitute.Face, out PdfObjectId fontId))
        {
            byte[]? ttf = fontProvider(substitute.Face);
            if (ttf is null)
            {
                violations.Add($"Substitute face '{substitute.Face}' is unavailable for '{baseFont}'.");
                return;
            }

            EmbeddableFont program = SimpleFontProgram.Build(ttf, WinAnsiEncoding.CodeToUnicode());
            SimpleFontEmbedder.FontObjects built =
                SimpleFontEmbedder.BuildSimpleTrueTypeFont(ttf, program, substitute.Face, substitute.Serif, allocate);
            foreach (PdfIndirectObject obj in built.Objects)
            {
                store.Add(obj);
            }

            fontId = built.FontId;
            faceCache[substitute.Face] = fontId;
        }

        fonts.Set(key, new PdfReference(fontId));
    }

    private static bool IsEmbedded(PdfDictionary fontDict, PdfObjectStore store)
    {
        if (HasFontFile(fontDict, store))
        {
            return true;
        }

        // Type0: the descriptor lives on the descendant CIDFont.
        if (fontDict.TryGetValue(DescendantFontsKey, out PdfPrimitive? descRef)
            && store.Resolve(descRef) is PdfArray descendants
            && descendants.Count > 0
            && store.Resolve(descendants[0]) is PdfDictionary cidFont)
        {
            return HasFontFile(cidFont, store);
        }

        return false;
    }

    private static bool HasFontFile(PdfDictionary fontDict, PdfObjectStore store)
    {
        if (!fontDict.TryGetValue(FontDescriptorKey, out PdfPrimitive? descRef)
            || store.Resolve(descRef) is not PdfDictionary descriptor)
        {
            return false;
        }

        return descriptor.ContainsKey(PdfName.Intern("FontFile"))
            || descriptor.ContainsKey(PdfName.Intern("FontFile2"))
            || descriptor.ContainsKey(PdfName.Intern("FontFile3"));
    }

    private static string? NameValue(PdfObjectStore store, PdfDictionary dict, PdfName key)
    {
        if (dict.TryGetValue(key, out PdfPrimitive? value) && store.Resolve(value) is PdfName name)
        {
            return name.Value;
        }

        return null;
    }
}
