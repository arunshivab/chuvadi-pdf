// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.6.2.2 (standard 14 fonts), §9.6.3 (TrueType
//        fonts), §9.6.6 (character encoding)
// PHASE: 3.33.0 (M1, D-302, D-307/D-308) — embed the standard fonts of an
//        existing document.

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Operations;

/// <summary>Options for <see cref="StandardFontEmbedder"/>.</summary>
public sealed class StandardFontEmbedOptions
{
    /// <summary>
    /// Gets a value indicating whether the common aliases of the standard
    /// fonts are embedded too: Arial, Times New Roman and Courier New, in the
    /// names Word and other programs write (for example <c>ArialMT</c>,
    /// <c>TimesNewRomanPS-BoldMT</c>). Default true.
    /// </summary>
    public bool IncludeAliases { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether an encrypted document may be
    /// processed. The output is written without encryption. Default false: an
    /// encrypted document throws <see cref="OperationsException"/>.
    /// </summary>
    public bool AllowEncryptedInput { get; init; }
}

/// <summary>What <see cref="StandardFontEmbedder.Embed"/> did.</summary>
public sealed class StandardFontEmbedResult
{
    internal StandardFontEmbedResult(IReadOnlyList<string> embeddedFonts, IReadOnlyList<string> notEmbedded)
    {
        EmbeddedFonts = embeddedFonts;
        NotEmbedded = notEmbedded;
    }

    /// <summary>Gets the <c>/BaseFont</c> names that were embedded, each once, in the order first found.</summary>
    public IReadOnlyList<string> EmbeddedFonts { get; }

    /// <summary>
    /// Gets one message for each non-embedded font that was left as it was:
    /// Symbol and ZapfDingbats (their substitute fonts' licence is not
    /// confirmed), composite (Type0) fonts, and fonts that are neither a
    /// standard font nor one of its aliases.
    /// </summary>
    public IReadOnlyList<string> NotEmbedded { get; }
}

/// <summary>
/// Embeds the standard 14 text fonts (Helvetica, Times, Courier) that a
/// document names but does not embed, as subsetted Liberation fonts with the
/// same metrics, so the document looks the same in every viewer (3.33.0).
/// </summary>
/// <remarks>
/// The fonts are found in the resources of the pages, of the forms they use,
/// of annotation appearances and of the interactive form's default resources
/// (<c>/DR</c>). Each substitute keeps the glyphs the original font's encoding
/// named. The document is written as a new file, so a signature in it no
/// longer verifies, and an encrypted document is written without encryption
/// (see <see cref="StandardFontEmbedOptions.AllowEncryptedInput"/>).
/// </remarks>
public static class StandardFontEmbedder
{
    private static readonly PdfName ResourcesKey = PdfName.Intern("Resources");
    private static readonly PdfName DefaultResourcesKey = PdfName.Intern("DR");

    /// <summary>
    /// Writes a copy of <paramref name="document"/> to <paramref name="output"/>
    /// in which the non-embedded standard fonts are embedded.
    /// </summary>
    /// <param name="document">The document. It is not changed.</param>
    /// <param name="output">The stream to write the new PDF to.</param>
    /// <param name="options">The options; null for the defaults.</param>
    /// <returns>Which fonts were embedded and which were not.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> or <paramref name="output"/> is null.</exception>
    /// <exception cref="OperationsException">
    /// The document is encrypted and <see cref="StandardFontEmbedOptions.AllowEncryptedInput"/> is false.
    /// </exception>
    public static StandardFontEmbedResult Embed(PdfDocument document, Stream output, StandardFontEmbedOptions? options = null)
    {
        return PdfWriteGuard.Run(document, "written", () =>
        {
            ArgumentNullException.ThrowIfNull(document);
            ArgumentNullException.ThrowIfNull(output);
            options ??= new StandardFontEmbedOptions();
            if (document.Encryption is not null && !options.AllowEncryptedInput)
            {
                throw new OperationsException(
                    "The document is encrypted; set StandardFontEmbedOptions.AllowEncryptedInput to write a decrypted copy with its fonts embedded.");
            }

            // Copy everything reachable from the trailer, renumbered densely, so
            // the caller's document is never changed.
            Dictionary<int, PdfPrimitive> reachable = new Dictionary<int, PdfPrimitive>();
            foreach (PdfName key in new[] { PdfName.Root, PdfName.Intern("Info") })
            {
                if (document.Trailer.TryGetValue(key, out PdfPrimitive? value))
                {
                    PdfCompressor.Collect(value, document.Objects, reachable);
                }
            }

            Dictionary<int, int> remap = new Dictionary<int, int>(reachable.Count);
            int next = 1;
            foreach (int number in reachable.Keys)
            {
                remap[number] = next++;
            }

            PdfObjectStore store = new PdfObjectStore();
            List<PdfIndirectObject> copies = new List<PdfIndirectObject>(reachable.Count);
            foreach (KeyValuePair<int, PdfPrimitive> entry in reachable)
            {
                PdfIndirectObject copy = new PdfIndirectObject(
                    new PdfObjectId(remap[entry.Key], 0), PdfCompressor.DeepCopy(entry.Value, remap));
                store.Add(copy);
                copies.Add(copy);
            }

            PdfDictionary trailer = new PdfDictionary();
            foreach (PdfName key in new[] { PdfName.Root, PdfName.Intern("Info"), PdfName.Intern("ID") })
            {
                if (document.Trailer.TryGetValue(key, out PdfPrimitive? value))
                {
                    trailer.Set(key, PdfCompressor.DeepCopy(value, remap));
                }
            }

            StandardFontSubstitution session = new StandardFontSubstitution(store, () => new PdfObjectId(next++, 0))
            {
                IncludeAliases = options.IncludeAliases,
            };
            HashSet<PdfDictionary> walked = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
            foreach (PdfIndirectObject copy in copies)
            {
                FindResources(copy.Value, store, session, walked);
            }

            List<PdfIndirectObject> objects = new List<PdfIndirectObject>(store.Objects);
            objects.Sort((a, b) => a.Id.ObjectNumber.CompareTo(b.Id.ObjectNumber));
            PdfWriter.Write(output, objects, trailer);
            return new StandardFontEmbedResult(session.Embedded, session.NotEmbedded.ToArray());
        });
    }

    // Hands every /Resources and /DR dictionary held directly in a value
    // (not through references: every indirect object is walked on its own)
    // to the session.
    private static void FindResources(
        PdfPrimitive value, PdfObjectStore store, StandardFontSubstitution session, HashSet<PdfDictionary> walked)
    {
        PdfDictionary? dict = value switch
        {
            PdfStream stream => stream.Dictionary,
            PdfDictionary d => d,
            _ => null,
        };

        if (dict is not null)
        {
            if (!walked.Add(dict))
            {
                return;
            }

            foreach (KeyValuePair<PdfName, PdfPrimitive> entry in dict)
            {
                if ((entry.Key.Equals(ResourcesKey) || entry.Key.Equals(DefaultResourcesKey))
                    && store.Resolve(entry.Value) is PdfDictionary resources)
                {
                    session.ProcessResources(resources);
                }
                else if (entry.Value is not PdfReference)
                {
                    FindResources(entry.Value, store, session, walked);
                }
            }

            return;
        }

        if (value is PdfArray array)
        {
            for (int i = 0; i < array.Count; i++)
            {
                if (array[i] is not PdfReference)
                {
                    FindResources(array[i], store, session, walked);
                }
            }
        }
    }
}
