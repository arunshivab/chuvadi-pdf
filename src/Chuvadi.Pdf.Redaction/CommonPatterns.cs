// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.2 — Chuvadi.Pdf.Redaction pattern-based extension
// Pre-built regex patterns for common PHI / PII tokens.

using System;
using System.Text.RegularExpressions;

namespace Chuvadi.Pdf.Redaction;

/// <summary>
/// Pre-built regex strings for common PHI / PII tokens.
/// </summary>
/// <remarks>
/// These are conservative starting points. Real-world documents have many edge
/// cases (whitespace inside identifiers, OCR artefacts, locale-specific formats);
/// production deployments should tune patterns to their corpus.
/// </remarks>
public static class CommonPatterns
{
    /// <summary>
    /// US Social Security Number. Matches the conventional XXX-XX-XXXX format.
    /// </summary>
    public const string UsSsn = @"\b\d{3}-\d{2}-\d{4}\b";

    /// <summary>
    /// US phone number. Matches (XXX) XXX-XXXX, XXX-XXX-XXXX, and XXX.XXX.XXXX.
    /// </summary>
    public const string UsPhone = @"\b(?:\(\d{3}\)\s?|\d{3}[-.])\d{3}[-.]\d{4}\b";

    /// <summary>
    /// Email address. RFC-5322 inspired but conservative enough to avoid false positives.
    /// </summary>
    public const string Email = @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b";

    /// <summary>
    /// ICD-10 code prefix. Matches the letter+two-digit prefix of any ICD-10 code,
    /// e.g. "E11" or "J45.901". Intentionally loose; tune downward if you want exact codes.
    /// </summary>
    public const string Icd10Prefix = @"\b[A-TV-Z][0-9][0-9A-Z](?:\.[0-9A-Z]{1,4})?\b";

    /// <summary>
    /// Credit card primary account number. Matches 13-19 digits possibly grouped by
    /// spaces or dashes. Does not validate the Luhn checksum — match precision is
    /// the caller's responsibility.
    /// </summary>
    public const string CreditCard = @"\b(?:\d[ -]?){13,19}\b";

    /// <summary>
    /// ISO-8601 date. Matches YYYY-MM-DD and YYYY/MM/DD.
    /// </summary>
    public const string IsoDate = @"\b\d{4}[-/]\d{2}[-/]\d{2}\b";

    /// <summary>
    /// US ZIP code. Matches 5-digit and ZIP+4 forms.
    /// </summary>
    public const string UsZip = @"\b\d{5}(?:-\d{4})?\b";

    /// <summary>
    /// UK NHS number (10 digits, optionally grouped 3-3-4 with spaces).
    /// </summary>
    public const string UkNhsNumber = @"\b\d{3}\s?\d{3}\s?\d{4}\b";

    /// <summary>
    /// Indian Permanent Account Number (PAN): five letters, four digits, one
    /// letter. Format-only; PAN has no checksum.
    /// </summary>
    public const string IndiaPan = @"\b[A-Z]{5}[0-9]{4}[A-Z]\b";

    /// <summary>
    /// Indian Aadhaar number: 12 digits (first digit 2-9), optionally grouped
    /// 4-4-4 with spaces. Pair with <see cref="PatternValidators.Verhoeff"/>.
    /// </summary>
    public const string IndiaAadhaar = @"\b[2-9]\d{3}\s?\d{4}\s?\d{4}\b";

    /// <summary>
    /// IBAN: two-letter country, two check digits, 11-30 alphanumerics. Pair
    /// with <see cref="PatternValidators.Iban"/> (mod-97).
    /// </summary>
    public const string Iban = @"\b[A-Z]{2}\d{2}[A-Z0-9]{11,30}\b";

    /// <summary>
    /// US ABA routing number: nine digits. Very loose on its own; pair with
    /// <see cref="PatternValidators.AbaRouting"/>.
    /// </summary>
    public const string AbaRouting = @"\b\d{9}\b";

    /// <summary>
    /// SWIFT / BIC code: six letters, two alphanumerics, optional three-character
    /// branch. Format-only.
    /// </summary>
    public const string Swift = @"\b[A-Z]{6}[A-Z0-9]{2}(?:[A-Z0-9]{3})?\b";

    /// <summary>US Employer Identification Number (EIN): XX-XXXXXXX.</summary>
    public const string Ein = @"\b\d{2}-\d{7}\b";

    /// <summary>
    /// US Individual Taxpayer Identification Number (ITIN): 9XX-XX-XXXX.
    /// </summary>
    public const string Itin = @"\b9\d{2}-\d{2}-\d{4}\b";

    /// <summary>
    /// US National Provider Identifier (NPI): ten digits. Loose on its own; pair
    /// with <see cref="PatternValidators.Npi"/>.
    /// </summary>
    public const string Npi = @"\b\d{10}\b";

    /// <summary>IPv4 address in dotted-decimal form.</summary>
    public const string IPv4 =
        @"\b(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)\b";

    /// <summary>
    /// Builds a rule that redacts a labelled value: the given label followed by
    /// an optional separator and an alphanumeric value (e.g. "MRN: 0099123").
    /// The whole match - label and value - is redacted. Prefer this over a bare
    /// number regex for identifiers (MRN, account, policy) that have no checksum.
    /// </summary>
    /// <param name="label">The literal label text preceding the value.</param>
    /// <returns>A <see cref="PatternRule"/> matching the label and its value.</returns>
    public static PatternRule LabeledValue(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        string escaped = Regex.Escape(label);
        string pattern = escaped + @"\s*[:#]?\s*([A-Za-z0-9][A-Za-z0-9\-/]*)";
        return new PatternRule(pattern);
    }
}
