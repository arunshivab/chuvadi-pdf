// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ISO 19005-1 (PDF/A-1), ISO 19005-2 (PDF/A-2)
// PHASE: Phase 3 — PDF/A writer

using System.Collections.Generic;

namespace Chuvadi.Pdf.PdfA;

/// <summary>The PDF/A conformance level to target.</summary>
public enum PdfAConformance
{
    /// <summary>PDF/A-1b (ISO 19005-1, level B — basic visual reproduction).</summary>
    PdfA1B,

    /// <summary>PDF/A-2b (ISO 19005-2, level B — basic visual reproduction).</summary>
    PdfA2B,
}

/// <summary>Options controlling PDF/A production.</summary>
public sealed class PdfAOptions
{
    /// <summary>The conformance level to target.</summary>
    public required PdfAConformance Conformance { get; init; }

    /// <summary>
    /// An ICC RGB output profile for the output intent. When null, a bundled
    /// public-domain sRGB profile is used.
    /// </summary>
    public byte[]? OutputIntentIccProfile { get; init; }

    /// <summary>The output condition identifier recorded in the output intent.</summary>
    public string OutputConditionIdentifier { get; init; } = "sRGB IEC61966-2.1";

    /// <summary>An optional registry name for the output intent.</summary>
    public string? RegistryName { get; init; }

    /// <summary>An optional document title written to the XMP metadata.</summary>
    public string? Title { get; init; }

    /// <summary>An optional document author written to the XMP metadata.</summary>
    public string? Author { get; init; }

    /// <summary>
    /// Gets whether an <c>/Info</c> date that gives no time zone is written to
    /// the XMP metadata as UTC (3.34.0). By default (false) such a date is left
    /// out of the XMP, since it has no known relation to UT (PDF 32000-1:2008
    /// §7.9.4) and validators read an XMP date without a zone in their own
    /// local zone; either way <see cref="PdfAResult.Warnings"/> says so. Omitted
    /// parts take the §7.9.4 defaults (month and day 01, the rest zero).
    /// </summary>
    public bool AssumeUtcForZonelessDates { get; init; }
}

/// <summary>The outcome of a PDF/A write attempt.</summary>
public sealed class PdfAResult
{
    internal PdfAResult(bool succeeded, IReadOnlyList<string> violations, IReadOnlyList<string> warnings)
    {
        Succeeded = succeeded;
        Violations = violations;
        Warnings = warnings;
    }

    /// <summary>
    /// True when a conforming file was written. When false, nothing was written
    /// to the output stream and <see cref="Violations"/> explains why.
    /// </summary>
    public bool Succeeded { get; }

    /// <summary>Messages describing conformance problems that could not be fixed.</summary>
    public IReadOnlyList<string> Violations { get; }

    /// <summary>
    /// Gets what the conversion changed or left out that the caller may want
    /// to know (3.34.0): for example an <c>/Info</c> date without a time zone
    /// left out of the XMP metadata, or written as UTC.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; }
}
