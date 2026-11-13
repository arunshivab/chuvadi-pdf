// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.34.0 (D-358, D-362) — what a fill did.

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Forms;

/// <summary>
/// What <see cref="FormFiller"/> did: the fields it wrote and, with
/// <see cref="FormUnknownFields.Report"/>, the names that matched no field.
/// </summary>
public sealed class FormFillResult
{
    /// <summary>
    /// Gets the names of the fields whose value was written, in the order the
    /// values were given. A pushbutton (which has no value) is not listed.
    /// </summary>
    public IReadOnlyList<string> FilledFields { get; internal init; } = Array.Empty<string>();

    /// <summary>
    /// Gets the names that matched no field, in the order given, when
    /// <see cref="FormFillOptions.UnknownFields"/> is
    /// <see cref="FormUnknownFields.Report"/>; otherwise empty.
    /// </summary>
    public IReadOnlyList<string> UnknownFields { get; internal init; } = Array.Empty<string>();
}
