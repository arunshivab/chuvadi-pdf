// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.4.3 (text showing)
// PHASE: Document operations — shared stamp number formatting.

using System.Globalization;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// Shared formatting helper for stamp content streams. As of 3.36.0 (D-412) all
/// mark text (stamps, page numbers, watermarks, headers and footers) is shaped
/// and subset-embedded through <see cref="Chuvadi.Pdf.Authoring.MarkTextComposer"/>,
/// so the former Helvetica standard-14 metrics and show-text builder are gone;
/// only the invariant number formatter used to emit content-stream operands
/// remains.
/// </summary>
internal static class StampText
{
    /// <summary>Formats a number for a content-stream operand, invariant culture.</summary>
    internal static string Fmt(double value)
    {
        return value.ToString("0.######", CultureInfo.InvariantCulture);
    }
}
