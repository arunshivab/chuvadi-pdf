// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.7.8 (XFA Forms)
//        XFA 3.3 §A — XFA packets (template, datasets, config, ...)
// PHASE: Document introspection — XFA packet access.

using System.Text;

namespace Chuvadi.Pdf.Documents;

/// <summary>
/// A single named packet of an XFA (XML Forms Architecture) form, such as the
/// <c>template</c>, <c>datasets</c>, or <c>config</c> packet. The packets are
/// stored under the document's <c>/AcroForm /XFA</c> entry, either as one XDP
/// stream (in which case there is a single packet with an empty name) or as an
/// array of name/stream pairs. PDF 32000-1:2008 §12.7.8.
/// </summary>
public sealed class XfaPacket
{
    private string? _text;

    internal XfaPacket(string name, byte[] xml)
    {
        Name = name;
        Xml = xml;
    }

    /// <summary>
    /// Gets the packet name as stored in the <c>/XFA</c> array (for example
    /// <c>"template"</c> or <c>"datasets"</c>). Empty for the single packet of a
    /// document whose <c>/XFA</c> entry is one combined XDP stream.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the packet's XML content with all stream filters already removed
    /// (for example FlateDecode). The bytes are the raw XML as authored.
    /// </summary>
    public byte[] Xml { get; }

    /// <summary>
    /// Gets the packet's XML decoded as a UTF-8 string. Computed once on first
    /// access and cached. A leading byte-order mark, when present, is preserved
    /// in the returned text.
    /// </summary>
    public string Text => _text ??= Encoding.UTF8.GetString(Xml);
}
