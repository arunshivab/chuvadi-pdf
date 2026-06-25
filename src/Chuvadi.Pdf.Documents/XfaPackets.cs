// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.7.8 (XFA Forms), §12.5.2 (Widget /Rect)
//        XFA 3.3 §A — XFA packets; §A.2 — datasets data layer
// PHASE: Document introspection — XFA packet + data-layer extraction.

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Filters;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Documents;

/// <summary>
/// Provides read access to a document's XFA (XML Forms Architecture) packets and
/// to the data layer they carry. Obtain an instance from
/// <see cref="PdfDocument.Xfa"/>; it is null when the document has no XFA form.
/// </summary>
/// <remarks>
/// XFA content lives under <c>/AcroForm /XFA</c>, outside the page content
/// streams, so a document's filled values are not text-extractable from the
/// pages. This type exposes the raw packets (template, datasets, config, …) and,
/// from the <c>datasets</c> packet, a flat list of <see cref="XfaDataField"/>
/// (path → value), each carrying best-effort widget geometry where a matching
/// AcroForm widget exists — enough for a host to overlay values onto a rendered
/// template. It is not an XFA processor: it does not lay out or render the form.
/// PDF 32000-1:2008 §12.7.8.
/// </remarks>
public sealed class XfaPackets
{
    private static readonly FilterPipeline DecodePipeline = FilterRegistry.CreateDefaultPipeline();

    private readonly PdfDocument _document;
    private readonly IReadOnlyList<XfaPacket> _packets;
    private IReadOnlyList<XfaDataField>? _dataFields;

    private XfaPackets(PdfDocument document, IReadOnlyList<XfaPacket> packets, bool isSingleStream)
    {
        _document = document;
        _packets = packets;
        IsSingleStream = isSingleStream;
    }

    /// <summary>
    /// Gets a value indicating whether the <c>/XFA</c> entry is a single combined
    /// XDP stream (true) rather than an array of named packets (false). When true,
    /// <see cref="Packets"/> holds one packet with an empty <see cref="XfaPacket.Name"/>.
    /// </summary>
    public bool IsSingleStream { get; }

    /// <summary>Gets the XFA packets in document order.</summary>
    public IReadOnlyList<XfaPacket> Packets => _packets;

    /// <summary>Gets the <c>template</c> packet (form layout), or null when absent.</summary>
    public XfaPacket? Template => Get("template");

    /// <summary>Gets the <c>datasets</c> packet (data layer), or null when absent.</summary>
    public XfaPacket? Datasets => Get("datasets");

    /// <summary>Gets the <c>config</c> packet, or null when absent.</summary>
    public XfaPacket? Config => Get("config");

    /// <summary>Gets the <c>form</c> packet, or null when absent.</summary>
    public XfaPacket? Form => Get("form");

    /// <summary>
    /// Gets the data layer as a flat list of fields walked from the
    /// <c>datasets</c> packet's <c>&lt;xfa:data&gt;</c> subtree, each with a
    /// best-effort <see cref="XfaGeometry"/> when a matching AcroForm widget is
    /// found. Empty when there is no <c>datasets</c> packet. Computed once and
    /// cached.
    /// </summary>
    public IReadOnlyList<XfaDataField> DataFields => _dataFields ??= BuildDataFields();

    /// <summary>
    /// Returns the packet with the given name (ordinal match), or null when no
    /// such packet exists.
    /// </summary>
    /// <param name="name">The packet name, for example <c>"datasets"</c>.</param>
    public XfaPacket? Get(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        for (int i = 0; i < _packets.Count; i++)
        {
            if (string.Equals(_packets[i].Name, name, StringComparison.Ordinal))
            {
                return _packets[i];
            }
        }

        return null;
    }

    internal static XfaPackets? TryRead(PdfDocument document)
    {
        if (document.XfaKind == XfaKind.None)
        {
            return null;
        }

        PdfDictionary? acroForm = ResolveAcroForm(document);

        if (acroForm is null
            || !acroForm.TryGetValue(PdfName.Intern("XFA"), out PdfPrimitive? xfaEntry))
        {
            return null;
        }

        PdfPrimitive resolved = document.Objects.Resolve(xfaEntry);
        List<XfaPacket> packets = new List<XfaPacket>();
        bool singleStream;

        if (resolved is PdfStream stream)
        {
            packets.Add(new XfaPacket(string.Empty, DecodeStream(stream)));
            singleStream = true;
        }
        else if (resolved is PdfArray array)
        {
            singleStream = false;

            for (int i = 0; i + 1 < array.Count; i += 2)
            {
                PdfString? name = array.GetAs<PdfString>(i);
                PdfStream? packetStream = document.Objects.ResolveAs<PdfStream>(array[i + 1]);

                if (packetStream is null)
                {
                    continue;
                }

                string packetName = name is not null ? name.ToTextString() : string.Empty;
                packets.Add(new XfaPacket(packetName, DecodeStream(packetStream)));
            }
        }
        else
        {
            return null;
        }

        if (packets.Count == 0)
        {
            return null;
        }

        return new XfaPackets(document, packets, singleStream);
    }

    private IReadOnlyList<XfaDataField> BuildDataFields()
    {
        XfaPacket? datasets = Datasets;

        if (datasets is null)
        {
            return Array.Empty<XfaDataField>();
        }

        IReadOnlyList<XfaDatasetsWalker.Leaf> leaves = XfaDatasetsWalker.Walk(datasets.Text);
        Dictionary<string, XfaGeometry> widgets = BuildWidgetIndex();
        List<XfaDataField> fields = new List<XfaDataField>(leaves.Count);

        for (int i = 0; i < leaves.Count; i++)
        {
            XfaDatasetsWalker.Leaf leaf = leaves[i];
            string last = LastSegment(leaf.NodePath);
            XfaGeometry? geometry = null;

            if (last.Length > 0 && widgets.TryGetValue(last, out XfaGeometry? match))
            {
                geometry = match;
            }

            fields.Add(new XfaDataField(leaf.NodePath, leaf.Value, geometry));
        }

        return fields;
    }

    private Dictionary<string, XfaGeometry> BuildWidgetIndex()
    {
        Dictionary<string, XfaGeometry> map = new Dictionary<string, XfaGeometry>(StringComparer.Ordinal);
        PdfDictionary? acroForm = ResolveAcroForm(_document);

        if (acroForm is null
            || !acroForm.TryGetValue(PdfName.Intern("Fields"), out PdfPrimitive? fieldsEntry))
        {
            return map;
        }

        PdfArray? fields = _document.Objects.ResolveAs<PdfArray>(fieldsEntry);

        if (fields is null)
        {
            return map;
        }

        Dictionary<PdfObjectId, int> annotPage = BuildAnnotPageMap();

        for (int i = 0; i < fields.Count; i++)
        {
            WalkField(fields[i], parentName: null, annotPage, map);
        }

        return map;
    }

    private void WalkField(
        PdfPrimitive entry,
        string? parentName,
        Dictionary<PdfObjectId, int> annotPage,
        Dictionary<string, XfaGeometry> map)
    {
        PdfObjectId id = entry is PdfReference reference ? reference.ObjectId : PdfObjectId.Invalid;
        PdfDictionary? dictionary = _document.Objects.ResolveAs<PdfDictionary>(entry);

        if (dictionary is null)
        {
            return;
        }

        string? partial = ReadTextString(dictionary, "T");
        string name = ComposeName(parentName, partial);

        if (dictionary.TryGetValue(PdfName.Intern("Kids"), out PdfPrimitive? kidsEntry))
        {
            PdfArray? kids = _document.Objects.ResolveAs<PdfArray>(kidsEntry);

            if (kids is not null)
            {
                for (int i = 0; i < kids.Count; i++)
                {
                    WalkField(kids[i], name, annotPage, map);
                }
            }
        }

        if (!dictionary.TryGetValue(PdfName.Intern("Rect"), out PdfPrimitive? rectEntry))
        {
            return;
        }

        PdfArray? rect = _document.Objects.ResolveAs<PdfArray>(rectEntry);

        if (rect is null || !TryReadRect(rect, out PdfRectangle rectangle))
        {
            return;
        }

        string last = LastSegment(name);

        if (last.Length == 0 || map.ContainsKey(last))
        {
            return;
        }

        int pageIndex = annotPage.TryGetValue(id, out int page) ? page : -1;
        map[last] = new XfaGeometry(pageIndex, rectangle);
    }

    private Dictionary<PdfObjectId, int> BuildAnnotPageMap()
    {
        Dictionary<PdfObjectId, int> map = new Dictionary<PdfObjectId, int>();

        foreach (PdfPage page in _document.Pages)
        {
            if (!page.Dictionary.TryGetValue(PdfName.Intern("Annots"), out PdfPrimitive? annotsEntry))
            {
                continue;
            }

            PdfArray? annots = _document.Objects.ResolveAs<PdfArray>(annotsEntry);

            if (annots is null)
            {
                continue;
            }

            for (int i = 0; i < annots.Count; i++)
            {
                if (annots[i] is PdfReference reference)
                {
                    map[reference.ObjectId] = page.Index;
                }
            }
        }

        return map;
    }

    private string? ReadTextString(PdfDictionary dictionary, string key)
    {
        if (!dictionary.TryGetValue(PdfName.Intern(key), out PdfPrimitive? entry))
        {
            return null;
        }

        PdfString? value = _document.Objects.ResolveAs<PdfString>(entry);
        return value?.ToTextString();
    }

    private bool TryReadRect(PdfArray rect, out PdfRectangle rectangle)
    {
        rectangle = default;

        if (rect.Count < 4)
        {
            return false;
        }

        if (!TryNumber(rect[0], out double x1)
            || !TryNumber(rect[1], out double y1)
            || !TryNumber(rect[2], out double x2)
            || !TryNumber(rect[3], out double y2))
        {
            return false;
        }

        rectangle = new PdfRectangle(x1, y1, x2, y2);
        return true;
    }

    private bool TryNumber(PdfPrimitive primitive, out double value)
    {
        PdfPrimitive resolved = _document.Objects.Resolve(primitive);

        if (resolved is PdfInteger integer)
        {
            value = integer.Value;
            return true;
        }

        if (resolved is PdfReal real)
        {
            value = real.Value;
            return true;
        }

        value = 0.0;
        return false;
    }

    private static string ComposeName(string? parentName, string? partial)
    {
        if (parentName is null)
        {
            return partial ?? string.Empty;
        }

        return partial is null ? parentName : parentName + "." + partial;
    }

    private static string LastSegment(string path)
    {
        int dot = path.LastIndexOf('.');
        return dot < 0 ? path : path.Substring(dot + 1);
    }

    private static PdfDictionary? ResolveAcroForm(PdfDocument document)
    {
        PdfDictionary catalog = document.Catalog;

        if (!catalog.TryGetValue(PdfName.Intern("AcroForm"), out PdfPrimitive? acroEntry))
        {
            return null;
        }

        return document.Objects.ResolveAs<PdfDictionary>(acroEntry);
    }

    private static byte[] DecodeStream(PdfStream stream)
    {
        if (!stream.IsFiltered)
        {
            return stream.RawBytes;
        }

        PdfPrimitive? filter = stream.Filter;

        if (filter is PdfName name)
        {
            return DecodePipeline.Decode(FilterRegistry.ResolveAlias(name.Value), stream.RawBytes, null);
        }

        if (filter is PdfArray array)
        {
            byte[] data = stream.RawBytes;

            for (int i = 0; i < array.Count; i++)
            {
                PdfName? element = array.GetAs<PdfName>(i);

                if (element is null)
                {
                    continue;
                }

                data = DecodePipeline.Decode(FilterRegistry.ResolveAlias(element.Value), data, null);
            }

            return data;
        }

        return stream.RawBytes;
    }
}
