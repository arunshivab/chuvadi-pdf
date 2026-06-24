// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Document operations — first-page handling for stamp numbering.

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// Controls how the document's first page (page index 0) participates in a
/// <see cref="StampNumbering"/> running sequence.
/// </summary>
public enum StampFirstPageMode
{
    /// <summary>The first page is numbered and consumes the start value.</summary>
    Number = 0,

    /// <summary>
    /// The first page is not stamped but still reserves its place in the
    /// sequence, so the second page shows the start value plus one.
    /// </summary>
    SkipKeepCount = 1,

    /// <summary>
    /// The first page is neither stamped nor counted, so the second page
    /// shows the start value.
    /// </summary>
    SkipRenumber = 2,
}
