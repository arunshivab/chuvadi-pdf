// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.7.4.4, Table 231 — choice field /Opt
// PHASE: 3.34.0 (D-335 item 1)

using System;

namespace Chuvadi.Pdf.Forms;

/// <summary>One option of a choice field (list box or combo box).</summary>
public sealed class FormChoiceOption
{
    /// <summary>Initialises a new <see cref="FormChoiceOption"/>.</summary>
    /// <param name="exportValue">The value stored in <c>/V</c> when this option is chosen.</param>
    /// <param name="displayText">The text shown for the option.</param>
    public FormChoiceOption(string exportValue, string displayText)
    {
        ExportValue = exportValue ?? throw new ArgumentNullException(nameof(exportValue));
        DisplayText = displayText ?? throw new ArgumentNullException(nameof(displayText));
    }

    /// <summary>Gets the value stored in <c>/V</c> when this option is chosen.</summary>
    public string ExportValue { get; }

    /// <summary>
    /// Gets the text shown for the option. Equal to <see cref="ExportValue"/>
    /// when the <c>/Opt</c> entry is a single string.
    /// </summary>
    public string DisplayText { get; }
}
