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
    /// card (Luhn), SWIFT/BIC (country code; with a digit or after a label,
    /// 3.35.0), EIN, and ITIN (middle group, 3.35.0).
    /// </summary>
    public static IReadOnlyList<PatternRule> Financial => new List<PatternRule>
    {
        new PatternRule(CommonPatterns.Iban, null, PatternValidators.Iban),
        new PatternRule(CommonPatterns.AbaRouting, null, PatternValidators.AbaRouting),
        new PatternRule(CommonPatterns.CreditCard, null, PatternValidators.Luhn),
        new PatternRule(CommonPatterns.Swift, null, PatternValidators.Bic),
        new PatternRule(CommonPatterns.Ein),
        new PatternRule(CommonPatterns.Itin, null, PatternValidators.Itin),
    };

    /// <summary>
    /// Medical identifiers: NPI (Luhn over the 80840 prefix) and ICD-10 codes:
    /// a code with its decimal part ("J45.901"), or any code after a label
    /// ("Dx: E11"). Before 3.35.0 every letter-digit-character token
    /// matched ("A12", "B2B", "T1D").
    /// </summary>
    public static IReadOnlyList<PatternRule> Medical => new List<PatternRule>
    {
        new PatternRule(CommonPatterns.Npi, null, PatternValidators.Npi),
        new PatternRule(CommonPatterns.Icd10Code),
        new PatternRule(CommonPatterns.Icd10Labeled),
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
