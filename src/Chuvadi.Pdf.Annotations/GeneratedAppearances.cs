// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.31 — which appearance to draw for each annotation (D-146): the
//        file's own, or one generated from the annotation's entries.

using System;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Forms;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Annotations;

/// <summary>
/// The appearance provider the renderers, text extraction and flattening use
/// (3.31). A form field is drawn from its value when the form's
/// <c>/NeedAppearances</c> is true (text and choice fields) or when the widget
/// has no <c>/AP</c>; a comment with no <c>/AP</c> is drawn from its own
/// entries. Everything else keeps the file's own appearance. Nothing is
/// written back to the file.
/// </summary>
public sealed class GeneratedAppearances : IAnnotationAppearanceProvider
{
    /// <summary>The shared instance; it keeps no state.</summary>
    public static GeneratedAppearances Instance { get; } = new GeneratedAppearances();

    private GeneratedAppearances()
    {
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public PdfStream? GetAppearance(PdfDictionary annotation, PdfStream? stored, PdfPage page, PdfObjectStore objects)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(objects);
        string subtype = objects.ResolveAs<PdfName>(Get(annotation, "Subtype"))?.Value ?? string.Empty;
        int pageNumber = page.PageNumber;
        try
        {
            if (subtype == "Widget")
            {
                PdfDictionary? acroForm = page.AcroForm;
                if (!NeedsField(annotation, stored, acroForm, objects))
                {
                    return stored;
                }

                return FieldAppearanceGenerator.Generate(annotation, objects, acroForm, objects.OnWarning, pageNumber) ?? stored;
            }

            if (stored is not null || !CommentAppearanceGenerator.IsSupported(subtype))
            {
                return stored;
            }

            return CommentAppearanceGenerator.Generate(annotation, objects, page.AcroForm, objects.OnWarning, pageNumber);
        }
        catch (Exception ex) when (!PdfWorkScope.IsAbort(ex) && ex is not OutOfMemoryException)
        {
            objects.OnWarning?.Invoke(new PdfWarning(
                PdfWarningKind.AppearanceNotGenerated,
                pageNumber,
                subtype + " annotation",
                "its appearance could not be made from its entries (" + ex.GetType().Name + "); " + (stored is null ? "it draws nothing" : "its stored appearance is drawn")));
            return stored;
        }
    }

    // A widget is regenerated when it has no appearance, or when the form says
    // its appearances are out of date (text and choice fields; buttons keep
    // their designed state appearances). Signatures never.
    private static bool NeedsField(PdfDictionary widget, PdfStream? stored, PdfDictionary? acroForm, PdfObjectStore objects)
    {
        string? type = Inherited(widget, "FT", objects);
        if (type is null or "Sig")
        {
            return false;
        }

        if (stored is null)
        {
            return true;
        }

        bool needAppearances = acroForm is not null && objects.Resolve(Get(acroForm, "NeedAppearances")) is PdfBoolean { Value: true };
        return needAppearances && type is "Tx" or "Ch";
    }

    private static string? Inherited(PdfDictionary widget, string key, PdfObjectStore objects)
    {
        PdfDictionary? d = widget;
        for (int depth = 0; d is not null && depth < 32; depth++)
        {
            if (objects.ResolveAs<PdfName>(Get(d, key)) is PdfName n)
            {
                return n.Value;
            }

            d = objects.ResolveAs<PdfDictionary>(Get(d, "Parent"));
        }

        return null;
    }

    private static PdfPrimitive Get(PdfDictionary? dict, string key) =>
        dict is not null && dict.TryGetValue(PdfName.Intern(key), out PdfPrimitive? v) && v is not null ? v : PdfNull.Value;
}
