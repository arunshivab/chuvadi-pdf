// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.6 (encryption), §12.7.4.3 (/MaxLen),
//        §12.7.4.4 (/Opt)
// PHASE: 3.34.0 (D-342, D-358, D-362) — FormFiller options.

using Chuvadi.Pdf.IO;

namespace Chuvadi.Pdf.Forms;

/// <summary>Options for <see cref="FormFiller"/>.</summary>
public sealed class FormFillOptions
{
    /// <summary>
    /// Gets the encryption to apply to the filled document, or null (the
    /// default) to write it unencrypted. A source opened with a password is
    /// decrypted in memory, so without this option the filled copy is not
    /// encrypted; pass the encryption again to keep the form protected.
    /// </summary>
    public EncryptionOptions? Encryption { get; init; }

    /// <summary>
    /// Gets what happens to a name that matches no field (3.34.0). The default,
    /// <see cref="FormUnknownFields.Ignore"/>, is the behaviour before 3.34.0;
    /// it may become <see cref="FormUnknownFields.Throw"/> in 4.0.
    /// </summary>
    public FormUnknownFields UnknownFields { get; init; }

    /// <summary>
    /// Gets whether values are checked against the field before they are
    /// written (3.34.0). When true, a choice value that is not one of the
    /// field's option export values throws a <see cref="FormException"/> with
    /// <see cref="FormErrorReason.NotInOptions"/> (an editable combo box
    /// accepts any value), and a text value longer than the field's maximum
    /// length, counted in Unicode code points, throws one with
    /// <see cref="FormErrorReason.TooLong"/>; an empty value always clears the
    /// field. When false (the default in 3.x; it may become true in 4.0),
    /// values are written exactly as given. In neither mode is a value
    /// truncated or changed.
    /// </summary>
    public bool Validate { get; init; }
}
