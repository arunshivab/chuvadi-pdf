// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.3 — Chuvadi.Pdf.Redaction pattern sets
// Ready-to-use groups of PatternRule with checksum validators pre-attached.

using System.Collections.Generic;

namespace Chuvadi.Pdf.Redaction;

/// <summary>
/// Curated groups of <see cref="PatternRule"/> with checksum validators already
/// attached, for common redaction scenarios. Each property returns a fresh list
/// the caller may freely modify.
/// </summary>
/// <remarks>
/// These are conservative starting points; tune patterns to your corpus before
/// relying on them in production.
/// </remarks>
public static class PatternSets
{
    /// <summary>
    /// Financial identifiers: IBAN (mod-97), ABA routing (weighted), payment
    /// card (Luhn), SWIFT/BIC, EIN, and ITIN.
    /// </summary>
    public static IReadOnlyList<PatternRule> Financial => new List<PatternRule>
    {
        new PatternRule(CommonPatterns.Iban, null, PatternValidators.Iban),
        new PatternRule(CommonPatterns.AbaRouting, null, PatternValidators.AbaRouting),
        new PatternRule(CommonPatterns.CreditCard, null, PatternValidators.Luhn),
        new PatternRule(CommonPatterns.Swift),
        new PatternRule(CommonPatterns.Ein),
        new PatternRule(CommonPatterns.Itin),
    };

    /// <summary>
    /// Medical identifiers: NPI (Luhn over the 80840 prefix) and ICD-10 code
    /// prefixes.
    /// </summary>
    public static IReadOnlyList<PatternRule> Medical => new List<PatternRule>
    {
        new PatternRule(CommonPatterns.Npi, null, PatternValidators.Npi),
        new PatternRule(CommonPatterns.Icd10Prefix),
    };

    /// <summary>
    /// General personally-identifiable information: US SSN, email, US phone,
    /// India PAN, India Aadhaar (Verhoeff), IPv4, and ISO dates.
    /// </summary>
    public static IReadOnlyList<PatternRule> GeneralPii => new List<PatternRule>
    {
        new PatternRule(CommonPatterns.UsSsn),
        new PatternRule(CommonPatterns.Email),
        new PatternRule(CommonPatterns.UsPhone),
        new PatternRule(CommonPatterns.IndiaPan),
        new PatternRule(CommonPatterns.IndiaAadhaar, null, PatternValidators.Verhoeff),
        new PatternRule(CommonPatterns.IPv4),
        new PatternRule(CommonPatterns.IsoDate),
    };
}
