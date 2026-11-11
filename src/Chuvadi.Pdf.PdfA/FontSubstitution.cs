// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.6 (simple fonts), ISO 19005-1 §6.3.4 (font embedding)
// PHASE: Phase 3 — PDF/A font embedding; 3.33.0 (M1, D-302): runs on the
//        shared standard-14 embedding engine in Chuvadi.Pdf.Authoring.
//
// Walks a document's page resources (and the forms they use) and replaces
// non-embedded Standard-14 simple fonts with embedded, subsetted Liberation
// substitutes. Other non-embedded fonts are reported as violations.

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.PdfA;

internal static class FontSubstitution
{
    /// <summary>
    /// Replaces non-embedded Standard-14 simple fonts in <paramref name="document"/>
    /// with embedded Liberation substitutes, registering new objects in the
    /// document's store and rewriting the resource references in place.
    /// </summary>
    /// <param name="document">The document to process.</param>
    /// <param name="allocate">Allocates a fresh object id.</param>
    /// <param name="fontProvider">Returns the TTF bytes for a Liberation face key, or null.</param>
    /// <param name="violations">Receives messages for fonts that could not be embedded.</param>
    /// <param name="form">
    /// How a font whose encoding is not WinAnsi is written (3.33.0, ticket 46,
    /// D-314): symbolic for PDF/A-1b, /Differences for PDF/A-2b.
    /// </param>
    /// <exception cref="ArgumentNullException">A parameter is null.</exception>
    internal static void SubstituteStandard14(
        PdfDocument document,
        Func<PdfObjectId> allocate,
        Func<string, byte[]?> fontProvider,
        IList<string> violations,
        NonWinAnsiForm form)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(allocate);
        ArgumentNullException.ThrowIfNull(fontProvider);
        ArgumentNullException.ThrowIfNull(violations);

        StandardFontSubstitution session = new StandardFontSubstitution(document.Objects, allocate, fontProvider)
        {
            NonWinAnsiForm = form,
        };
        for (int i = 0; i < document.Pages.Count; i++)
        {
            PdfDictionary? resources = document.Pages[i].Resources;
            if (resources is not null)
            {
                session.ProcessResources(resources);
            }
        }

        foreach (string message in session.NotEmbedded)
        {
            violations.Add(message);
        }
    }
}
