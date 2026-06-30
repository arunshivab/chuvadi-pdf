// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  XFA 3.3 — template rendering.
// PHASE: LA-23b Phase B — positioned rendering.

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Xfa.Layout;
using Chuvadi.Pdf.Xfa.Model;
using Chuvadi.Pdf.Xfa.Parse;

namespace Chuvadi.Pdf.Xfa.Render;

/// <summary>
/// Renders a document's XFA template to a new PDF. Phase B supports positioned
/// layout: draws, fields, captions, borders, and check buttons placed by their
/// explicit coordinates.
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

        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();

        XfaPageArea? pageArea = FindFirst<XfaPageArea>(root);
        PageSize pageSize = ResolvePageSize(pageArea);
        XfaContentArea? contentArea = pageArea is null ? null : FindFirst<XfaContentArea>(pageArea);

        double originX = contentArea?.X.Points ?? 0.0;
        double originY = contentArea?.Y.Points ?? 0.0;

        PageBuilder page = builder.AddPage(pageSize);

        // Lay out the body subform(s): the structural children of the root that
        // are not page-geometry nodes.
        foreach (XfaNode child in root.Children)
        {
            if (child is XfaPageSet or XfaPageArea or XfaContentArea)
            {
                continue;
            }

            IReadOnlyList<XfaBox> boxes = XfaLayoutEngine.Layout(child, originX, originY);
            XfaContentEmitter.Emit(page, boxes);
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

    private static T? FindFirst<T>(XfaNode node)
        where T : XfaNode
    {
        if (node is T match)
        {
            return match;
        }

        foreach (XfaNode child in node.Children)
        {
            T? found = FindFirst<T>(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }
}
