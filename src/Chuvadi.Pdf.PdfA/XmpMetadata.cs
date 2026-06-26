// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ISO 19005-1 §6.7 / ISO 16684-1 (XMP) — pdfaid identification schema
// PHASE: Phase 3 — PDF/A structural metadata
//
// Builds the document-level XMP packet carrying the pdfaid part/conformance
// identification required by PDF/A, plus optional Dublin Core title/creator. The
// packet is emitted as UTF-8 (no BOM) for an uncompressed /Metadata stream.

using System;
using System.Text;

namespace Chuvadi.Pdf.PdfA;

internal static class XmpMetadata
{
    /// <summary>
    /// Builds a PDF/A XMP packet for the given conformance level.
    /// </summary>
    /// <param name="part">The PDF/A part (1 or 2).</param>
    /// <param name="conformance">The conformance level (e.g. "B").</param>
    /// <param name="title">Optional document title (dc:title).</param>
    /// <param name="author">Optional document author (dc:creator).</param>
    /// <param name="producer">The producer string (pdf:Producer and xmp:CreatorTool).</param>
    /// <returns>The XMP packet bytes (UTF-8, no BOM).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="conformance"/> or <paramref name="producer"/> is null.</exception>
    internal static byte[] Build(int part, string conformance, string? title, string? author, string producer)
    {
        ArgumentNullException.ThrowIfNull(conformance);
        ArgumentNullException.ThrowIfNull(producer);

        StringBuilder sb = new StringBuilder();
        sb.Append("<?xpacket begin=\"\uFEFF\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>\n");
        sb.Append("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">\n");
        sb.Append(" <rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">\n");

        sb.Append("  <rdf:Description rdf:about=\"\" xmlns:pdfaid=\"http://www.aiim.org/pdfa/ns/id/\">\n");
        sb.Append("   <pdfaid:part>").Append(part).Append("</pdfaid:part>\n");
        sb.Append("   <pdfaid:conformance>").Append(Escape(conformance)).Append("</pdfaid:conformance>\n");
        sb.Append("  </rdf:Description>\n");

        if (title is not null || author is not null)
        {
            sb.Append("  <rdf:Description rdf:about=\"\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\">\n");
            if (title is not null)
            {
                sb.Append("   <dc:title><rdf:Alt><rdf:li xml:lang=\"x-default\">")
                  .Append(Escape(title)).Append("</rdf:li></rdf:Alt></dc:title>\n");
            }

            if (author is not null)
            {
                sb.Append("   <dc:creator><rdf:Seq><rdf:li>")
                  .Append(Escape(author)).Append("</rdf:li></rdf:Seq></dc:creator>\n");
            }

            sb.Append("  </rdf:Description>\n");
        }

        sb.Append("  <rdf:Description rdf:about=\"\" xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\">\n");
        sb.Append("   <xmp:CreatorTool>").Append(Escape(producer)).Append("</xmp:CreatorTool>\n");
        sb.Append("  </rdf:Description>\n");

        sb.Append("  <rdf:Description rdf:about=\"\" xmlns:pdf=\"http://ns.adobe.com/pdf/1.3/\">\n");
        sb.Append("   <pdf:Producer>").Append(Escape(producer)).Append("</pdf:Producer>\n");
        sb.Append("  </rdf:Description>\n");

        sb.Append(" </rdf:RDF>\n");
        sb.Append("</x:xmpmeta>\n");
        sb.Append("<?xpacket end=\"w\"?>");

        return new UTF8Encoding(false).GetBytes(sb.ToString());
    }

    private static string Escape(string value)
    {
        StringBuilder sb = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            switch (c)
            {
                case '&':
                    sb.Append("&amp;");
                    break;
                case '<':
                    sb.Append("&lt;");
                    break;
                case '>':
                    sb.Append("&gt;");
                    break;
                case '"':
                    sb.Append("&quot;");
                    break;
                case '\'':
                    sb.Append("&apos;");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }
}
