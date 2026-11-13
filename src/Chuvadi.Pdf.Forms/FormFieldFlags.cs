// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.7.3.1 Table 221 (all fields), Table 226
//        (buttons), Table 228 (text), Table 230 (choice) — /Ff bits
// PHASE: 3.34.0 (D-335 item 1)

using System;

namespace Chuvadi.Pdf.Forms;

/// <summary>
/// The field flags (<c>/Ff</c>) of a form field. The values are the PDF bit
/// positions, so a raw <c>/Ff</c> integer casts directly.
/// </summary>
[Flags]
public enum FormFieldFlags
{
    /// <summary>No flag is set.</summary>
    None = 0,

    /// <summary>The user may not change the value (bit 1).</summary>
    ReadOnly = 1 << 0,

    /// <summary>The field must have a value when the form is submitted (bit 2).</summary>
    Required = 1 << 1,

    /// <summary>The field is not exported when the form is submitted (bit 3).</summary>
    NoExport = 1 << 2,

    /// <summary>Text: the field may hold several lines (bit 13).</summary>
    Multiline = 1 << 12,

    /// <summary>Text: the value is a password and is not echoed (bit 14).</summary>
    Password = 1 << 13,

    /// <summary>Radio: exactly one button must be on; clicking the on button does not turn it off (bit 15).</summary>
    NoToggleToOff = 1 << 14,

    /// <summary>Button: the field is a set of radio buttons (bit 16).</summary>
    Radio = 1 << 15,

    /// <summary>Button: the field is a pushbutton with no value (bit 17).</summary>
    Pushbutton = 1 << 16,

    /// <summary>Choice: a combo box rather than a list box (bit 18).</summary>
    Combo = 1 << 17,

    /// <summary>Choice: a combo box with an editable text box (bit 19).</summary>
    Edit = 1 << 18,

    /// <summary>Choice: the options are sorted (bit 20).</summary>
    Sort = 1 << 19,

    /// <summary>Text: the field selects a file to submit (bit 21).</summary>
    FileSelect = 1 << 20,

    /// <summary>Choice: more than one option may be selected (bit 22).</summary>
    MultiSelect = 1 << 21,

    /// <summary>Text and choice: no spell checking (bit 23).</summary>
    DoNotSpellCheck = 1 << 22,

    /// <summary>Text: no scrolling beyond the visible area (bit 24).</summary>
    DoNotScroll = 1 << 23,

    /// <summary>Text: the field is divided into <c>/MaxLen</c> equal cells (bit 25).</summary>
    Comb = 1 << 24,

    /// <summary>
    /// Bit 26. Radio: buttons with the same on-state turn on and off together.
    /// On a text field the same bit means the value is rich text.
    /// </summary>
    RadiosInUnison = 1 << 25,

    /// <summary>Choice: a new value is committed as soon as a selection is made (bit 27).</summary>
    CommitOnSelChange = 1 << 26,
}
