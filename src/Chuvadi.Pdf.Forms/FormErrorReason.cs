// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.34.0 (D-358, D-362) — why FormFiller refused a value.

namespace Chuvadi.Pdf.Forms;

/// <summary>
/// Why <see cref="FormFiller"/> refused a value (<see cref="FormException.Reason"/>).
/// </summary>
public enum FormErrorReason
{
    /// <summary>
    /// A check box or radio-group value matches none of the field's states.
    /// </summary>
    UnknownState,

    /// <summary>
    /// With <see cref="FormFillOptions.Validate"/>, a choice value is not one
    /// of the field's option export values (an editable combo box accepts any
    /// value).
    /// </summary>
    NotInOptions,

    /// <summary>
    /// With <see cref="FormFillOptions.Validate"/>, a text value is longer
    /// than the field's maximum length (counted in Unicode code points).
    /// </summary>
    TooLong,

    /// <summary>
    /// More than one value was given for a field that holds one: anything but
    /// a multi-select list box.
    /// </summary>
    TooManyValues,

    /// <summary>
    /// With <see cref="FormUnknownFields.Throw"/>, a name matches no field.
    /// </summary>
    UnknownField,
}
