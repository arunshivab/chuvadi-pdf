// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ISO 19005-1 §6.2.2 / ISO 19005-2 §6.2.4 — Output intent
// PHASE: Phase 3 — PDF/A structural metadata
//
// Builds the PDF/A OutputIntent dictionary and its embedded DestOutputProfile
// (an ICC stream). The subtype /S is GTS_PDFA1 for both PDF/A-1 and PDF/A-2.

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.PdfA;

internal static class OutputIntentBuilder
{
    /// <summary>The OutputIntent dictionary plus the ICC stream object to register.</summary>
    /// <param name="OutputIntent">The OutputIntent dictionary (to add to the catalog's array).</param>
    /// <param name="Objects">The DestOutputProfile stream indirect object.</param>
    internal sealed record Result(PdfDictionary OutputIntent, IReadOnlyList<PdfIndirectObject> Objects);

    /// <summary>
    /// Builds an sRGB output intent with an embedded ICC profile.
    /// </summary>
    /// <param name="iccProfile">The ICC profile bytes (RGB, 3 components).</param>
    /// <param name="conditionIdentifier">The OutputConditionIdentifier (e.g. "sRGB IEC61966-2.1").</param>
    /// <param name="registryName">Optional registry name.</param>
    /// <param name="allocate">Allocates a fresh object id.</param>
    /// <returns>The OutputIntent dictionary and the ICC stream object.</returns>
    /// <exception cref="ArgumentNullException">A required parameter is null.</exception>
    internal static Result Build(
        byte[] iccProfile,
        string conditionIdentifier,
        string? registryName,
        Func<PdfObjectId> allocate)
    {
        ArgumentNullException.ThrowIfNull(iccProfile);
        ArgumentNullException.ThrowIfNull(conditionIdentifier);
        ArgumentNullException.ThrowIfNull(allocate);

        PdfObjectId iccId = allocate();
        PdfDictionary iccDict = new PdfDictionary();
        iccDict.Set(PdfName.Intern("N"), 3);
        PdfIndirectObject iccObject = new PdfIndirectObject(iccId, new PdfStream(iccDict, iccProfile));

        PdfDictionary intent = new PdfDictionary();
        intent.Set(PdfName.Type, PdfName.Intern("OutputIntent"));
        intent.Set(PdfName.Intern("S"), PdfName.Intern("GTS_PDFA1"));
        intent.Set(PdfName.Intern("OutputConditionIdentifier"), new PdfString(conditionIdentifier));
        intent.Set(PdfName.Intern("Info"), new PdfString(conditionIdentifier));
        if (registryName is not null)
        {
            intent.Set(PdfName.Intern("RegistryName"), new PdfString(registryName));
        }

        intent.Set(PdfName.Intern("DestOutputProfile"), new PdfReference(iccId));

        return new Result(intent, new List<PdfIndirectObject> { iccObject });
    }
}
