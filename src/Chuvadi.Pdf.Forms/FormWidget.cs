// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.5.6.19 (widget annotations), §12.7.4.2.3 (check
//        boxes), §12.7.4.2.4 (radio buttons, /Opt export values)
// PHASE: 3.34.0 (D-335 item 1) — where a field is shown on the page.

using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Forms;

/// <summary>
/// One widget annotation of a form field: where the field is shown, and for a
/// check box or radio button the state that turns it on.
/// </summary>
public sealed class FormWidget
{
    /// <summary>Initialises a new <see cref="FormWidget"/>.</summary>
    /// <param name="pageIndex">The zero-based page the widget is on, or -1.</param>
    /// <param name="rect">The widget rectangle in user space.</param>
    /// <param name="objectId">The widget's object id, or 0 0 when it is a direct object.</param>
    /// <param name="onState">A button widget's "on" appearance state name, or null.</param>
    /// <param name="exportValue">A button widget's export value, or null.</param>
    public FormWidget(int pageIndex, PdfRectangle rect, PdfObjectId objectId, string? onState, string? exportValue)
    {
        PageIndex = pageIndex;
        Rect = rect;
        ObjectId = objectId;
        OnState = onState;
        ExportValue = exportValue;
    }

    /// <summary>
    /// Gets the zero-based index of the page whose <c>/Annots</c> lists the
    /// widget, or -1 when no page does.
    /// </summary>
    public int PageIndex { get; }

    /// <summary>
    /// Gets the widget rectangle (<c>/Rect</c>) in PDF user space, normalised
    /// so that <see cref="PdfRectangle.X1"/> and <see cref="PdfRectangle.Y1"/>
    /// are the lower-left corner.
    /// </summary>
    public PdfRectangle Rect { get; }

    /// <summary>
    /// Gets the object id of the widget annotation. For a field whose only
    /// widget is merged into the field dictionary, this is the field's id; for
    /// a widget written as a direct object it is 0 0.
    /// </summary>
    public PdfObjectId ObjectId { get; }

    /// <summary>
    /// Gets, for a check box or radio button, the name of the appearance state
    /// that shows the widget as on (the <c>/AP /N</c> key other than
    /// <c>Off</c>); null for other fields.
    /// </summary>
    public string? OnState { get; }

    /// <summary>
    /// Gets, for a check box or radio button, the value the widget stands for:
    /// the field's <c>/Opt</c> entry for this widget when the field has one,
    /// otherwise <see cref="OnState"/>; null for other fields.
    /// </summary>
    public string? ExportValue { get; }
}
