// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.34.0 (D-335) — how PageOperations.Merge carries form fields.

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// How <see cref="PageOperations.Merge(System.IO.Stream, System.Collections.Generic.IReadOnlyList{Chuvadi.Pdf.Documents.PdfDocument}, MergeOptions)"/>
/// carries the inputs' form fields (<see cref="MergeOptions.FormFields"/>).
/// </summary>
/// <remarks>
/// With every mode except <see cref="Drop"/>, the merged form also gets the
/// inputs' default resources (<c>/DR</c>; a resource name used by two inputs
/// for different resources is renamed <c>name_2</c> in the later input, and
/// its <c>/DA</c> strings follow), <c>NeedAppearances</c> and
/// <c>SigFlags</c> (set when any input sets them), and the calculation order
/// (<c>/CO</c>, concatenated). XFA forms are not carried: the merged form is
/// AcroForm only.
/// </remarks>
public enum MergeFormFields
{
    /// <summary>
    /// No form: the merged document has no <c>/AcroForm</c>, and the widgets
    /// stay on their pages as plain annotations (the behaviour before 3.34.0).
    /// </summary>
    Drop = 0,

    /// <summary>
    /// Every input's fields are kept as separate fields. A top-level field
    /// whose name an earlier input already uses is renamed <c>name_2</c>
    /// (then <c>name_3</c>, ...), so filling one never changes the other.
    /// </summary>
    RenameOnConflict = 1,

    /// <summary>
    /// Fields with the same fully qualified name and type become one field
    /// with the widgets of both, so they show the same value (the earlier
    /// input's). Parents with the same name are merged: a child both have is
    /// shared the same way, and a child only the later input has moves under
    /// the earlier parent, keeping what it inherited. Where a top-level
    /// field's tree cannot be shared (a type differs, or a field with children
    /// meets one without) the later top-level field is renamed as with
    /// <see cref="RenameOnConflict"/>. (Before the 3.34.0 release, a field with
    /// named children was always renamed.)
    /// </summary>
    ShareSameName = 2,
}
