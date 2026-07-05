// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  XFA 3.3 — template rendering.
// PHASE: LA-23b Phase C — merged, flowed, paginated rendering.

using System;
using System.IO;
using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Xfa.Layout;
using Chuvadi.Pdf.Xfa.Model;
using Chuvadi.Pdf.Xfa.Parse;

namespace Chuvadi.Pdf.Xfa.Render;

/// <summary>
/// Renders a document's XFA template to a new PDF. Positioned and flowed
/// (top-to-bottom, left-right-top-to-bottom) layouts are supported; datasets
/// values are merged into bound fields; flowed content paginates across
/// content areas and pages per the page set's occurrence rules, honoring
/// forced breaks.
/// </summary>
public static class XfaRenderer
{
    /// <summary>
    /// Renders the XFA template of <paramref name="document"/> to a new PDF
    /// written to <paramref name="output"/>.
    /// </summary>
    /// <param name="output">The destination stream for the rendered PDF.</param>
    /// <param name="document">The source document; must contain an XFA template.</param>
    /// <param name="options">Rendering options.</param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="XfaRenderException">The document has no usable XFA template.</exception>
    public static void Render(Stream output, PdfDocument document, XfaRenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        XfaPackets? packets = document.Xfa;
        if (packets?.Template is null)
        {
            throw new XfaRenderException("The document does not contain an XFA template packet.");
        }

        XfaSubform root = XfaTemplateParser.Parse(packets.Template.Xml)
            ?? throw new XfaRenderException("The XFA template does not contain a root subform.");

        // Merge datasets values into the template fields so bound fields render
        // their actual values rather than the (usually empty) template defaults.
        XfaDataMerge.Apply(root, packets.DataFields);

        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();

        foreach (XfaComposedPage composed in XfaPaginator.Compose(root))
        {
            PageBuilder page = builder.AddPage(ResolvePageSize(composed.Area));
            XfaContentEmitter.Emit(page, composed.Boxes);
        }

        byte[] bytes = builder.ToByteArray();
        output.Write(bytes, 0, bytes.Length);
    }

    private static PageSize ResolvePageSize(XfaPageArea? pageArea)
    {
        if (pageArea?.MediumLong is { } longEdge && pageArea.MediumShort is { } shortEdge)
        {
            double l = longEdge.Points;
            double s = shortEdge.Points;
            return pageArea.Landscape ? new PageSize(l, s) : new PageSize(s, l);
        }

        return PageSize.Letter;
    }
}
