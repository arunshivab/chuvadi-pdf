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
        => Build(part, conformance, title, author, producer, producer);

    /// <summary>
    /// Builds the XMP packet with a separate <c>xmp:CreatorTool</c> (3.33.0,
    /// D-318): PDF/A-1b requires <c>pdf:Producer</c> and <c>xmp:CreatorTool</c>
    /// to equal the document information dictionary's /Producer and /Creator
    /// (ISO 19005-1 §6.7.3).
    /// </summary>
    /// <param name="part">The PDF/A part (1 or 2).</param>
    /// <param name="conformance">The conformance level, e.g. "B".</param>
    /// <param name="title">The dc:title, or null.</param>
    /// <param name="author">The dc:creator, or null.</param>
    /// <param name="producer">The pdf:Producer.</param>
    /// <param name="creatorTool">The xmp:CreatorTool.</param>
    /// <returns>The XMP packet bytes (UTF-8).</returns>
    internal static byte[] Build(int part, string conformance, string? title, string? author, string producer, string creatorTool)
        => Build(part, conformance, title, author, null, producer, creatorTool);

    /// <summary>
    /// Builds the XMP packet with a <c>dc:description</c> too (3.33.0, D-325):
    /// PDF/A-1b requires dc:title, dc:creator and dc:description to equal the
    /// document information dictionary's /Title, /Author and /Subject
    /// (ISO 19005-1 §6.7.3).
    /// </summary>
    /// <param name="part">The PDF/A part (1 or 2).</param>
    /// <param name="conformance">The conformance level, e.g. "B".</param>
    /// <param name="title">The dc:title, or null.</param>
    /// <param name="author">The dc:creator, or null.</param>
    /// <param name="subject">The dc:description, or null.</param>
    /// <param name="producer">The pdf:Producer.</param>
    /// <param name="creatorTool">The xmp:CreatorTool.</param>
    /// <returns>The XMP packet bytes (UTF-8).</returns>
    internal static byte[] Build(
        int part, string conformance, string? title, string? author, string? subject, string producer, string creatorTool)
        => Build(part, conformance, title, author, subject, null, producer, creatorTool);

    /// <summary>
    /// Builds the XMP packet with <c>pdf:Keywords</c> too (3.34.0, D-335):
    /// PDF/A-1b requires it to equal the document information dictionary's
    /// /Keywords (ISO 19005-1 §6.7.3).
    /// </summary>
    /// <param name="part">The PDF/A part (1 or 2).</param>
    /// <param name="conformance">The conformance level, e.g. "B".</param>
    /// <param name="title">The dc:title, or null.</param>
    /// <param name="author">The dc:creator, or null.</param>
    /// <param name="subject">The dc:description, or null.</param>
    /// <param name="keywords">The pdf:Keywords, or null.</param>
    /// <param name="producer">The pdf:Producer.</param>
    /// <param name="creatorTool">The xmp:CreatorTool.</param>
    /// <returns>The XMP packet bytes (UTF-8).</returns>
    internal static byte[] Build(
        int part, string conformance, string? title, string? author, string? subject, string? keywords,
        string producer, string creatorTool)
        => Build(part, conformance, title, author, subject, keywords, producer, creatorTool, null, null);

    /// <summary>
    /// Builds the XMP packet with <c>xmp:CreateDate</c> and
    /// <c>xmp:ModifyDate</c> too (3.34.0, #49g): PDF/A-1b requires them to
    /// equal the document information dictionary's /CreationDate and /ModDate
    /// (ISO 19005-1 §6.7.3).
    /// </summary>
    /// <param name="part">The PDF/A part (1 or 2).</param>
    /// <param name="conformance">The conformance level, e.g. "B".</param>
    /// <param name="title">The dc:title, or null.</param>
    /// <param name="author">The dc:creator, or null.</param>
    /// <param name="subject">The dc:description, or null.</param>
    /// <param name="keywords">The pdf:Keywords, or null.</param>
    /// <param name="producer">The pdf:Producer.</param>
    /// <param name="creatorTool">The xmp:CreatorTool.</param>
    /// <param name="createDate">The xmp:CreateDate (ISO 8601), or null.</param>
    /// <param name="modifyDate">The xmp:ModifyDate (ISO 8601), or null.</param>
    /// <returns>The XMP packet bytes (UTF-8).</returns>
    internal static byte[] Build(
        int part, string conformance, string? title, string? author, string? subject, string? keywords,
        string producer, string creatorTool, string? createDate, string? modifyDate)
    {
        ArgumentNullException.ThrowIfNull(conformance);
        ArgumentNullException.ThrowIfNull(producer);
        ArgumentNullException.ThrowIfNull(creatorTool);

        StringBuilder sb = new StringBuilder();
        sb.Append("<?xpacket begin=\"\uFEFF\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>\n");
        sb.Append("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">\n");
        sb.Append(" <rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">\n");

        sb.Append("  <rdf:Description rdf:about=\"\" xmlns:pdfaid=\"http://www.aiim.org/pdfa/ns/id/\">\n");
        sb.Append("   <pdfaid:part>").Append(part).Append("</pdfaid:part>\n");
        sb.Append("   <pdfaid:conformance>").Append(Escape(conformance)).Append("</pdfaid:conformance>\n");
        sb.Append("  </rdf:Description>\n");

        if (title is not null || author is not null || subject is not null)
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

            if (subject is not null)
            {
                sb.Append("   <dc:description><rdf:Alt><rdf:li xml:lang=\"x-default\">")
                  .Append(Escape(subject)).Append("</rdf:li></rdf:Alt></dc:description>\n");
            }

            sb.Append("  </rdf:Description>\n");
        }

        sb.Append("  <rdf:Description rdf:about=\"\" xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\">\n");
        sb.Append("   <xmp:CreatorTool>").Append(Escape(creatorTool)).Append("</xmp:CreatorTool>\n");
        if (createDate is not null)
        {
            sb.Append("   <xmp:CreateDate>").Append(Escape(createDate)).Append("</xmp:CreateDate>\n");
        }

        if (modifyDate is not null)
        {
            sb.Append("   <xmp:ModifyDate>").Append(Escape(modifyDate)).Append("</xmp:ModifyDate>\n");
        }

        sb.Append("  </rdf:Description>\n");

        sb.Append("  <rdf:Description rdf:about=\"\" xmlns:pdf=\"http://ns.adobe.com/pdf/1.3/\">\n");
        sb.Append("   <pdf:Producer>").Append(Escape(producer)).Append("</pdf:Producer>\n");
        if (keywords is not null)
        {
            sb.Append("   <pdf:Keywords>").Append(Escape(keywords)).Append("</pdf:Keywords>\n");
        }

        sb.Append("  </rdf:Description>\n");

        sb.Append(" </rdf:RDF>\n");
        sb.Append("</x:xmpmeta>\n");
        sb.Append("<?xpacket end=\"w\"?>");

        return new UTF8Encoding(false).GetBytes(sb.ToString());
    }

    /// <summary>
    /// True when every character of <paramref name="value"/> may appear in
    /// XML 1.0 (tab, line feed, carriage return, and U+0020 and above apart
    /// from U+FFFE and U+FFFF; surrogate pairs allowed).
    /// </summary>
    /// <param name="value">The text.</param>
    /// <returns>True when the text can be written into the XMP packet.</returns>
    internal static bool IsXmlText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                i++;
                continue;
            }

            if (char.IsSurrogate(c) || c == '\uFFFE' || c == '\uFFFF' || (c < 0x20 && c != '\t' && c != '\n' && c != '\r'))
            {
                return false;
            }
        }

        return true;
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
