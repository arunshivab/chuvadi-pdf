// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.34.0 (D-358, D-362) — what FormFiller does with names that match
//        no field.

namespace Chuvadi.Pdf.Forms;

/// <summary>
/// What <see cref="FormFiller"/> does with a name that matches no field
/// (<see cref="FormFillOptions.UnknownFields"/>).
/// </summary>
public enum FormUnknownFields
{
    /// <summary>The name is ignored, as before 3.34.0 (the default in 3.x).</summary>
    Ignore,

    /// <summary>
    /// The name is listed in <see cref="FormFillResult.UnknownFields"/>. Only
    /// the overload that returns a <see cref="FormFillResult"/> can report;
    /// the others throw <see cref="System.ArgumentException"/>.
    /// </summary>
    Report,

    /// <summary>
    /// A <see cref="FormException"/> with <see cref="FormErrorReason.UnknownField"/>
    /// is thrown before anything is written.
    /// </summary>
    Throw,
}
