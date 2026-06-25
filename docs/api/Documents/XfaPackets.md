# XfaPackets

**Class** in `Chuvadi.Pdf.Documents` (Documents)

Provides read access to a document's XFA (XML Forms Architecture) packets and to the data layer they carry. Obtain an instance from `PdfDocument.Xfa`; it is null when the document has no XFA form.

```csharp
public sealed class XfaPackets
```

## Remarks

XFA content lives under `/AcroForm /XFA`, outside the page content streams, so a document's filled values are not text-extractable from the pages. This type exposes the raw packets (template, datasets, config, …) and, from the `datasets` packet, a flat list of `XfaDataField` (path → value), each carrying best-effort widget geometry where a matching AcroForm widget exists — enough for a host to overlay values onto a rendered template. It is not an XFA processor: it does not lay out or render the form. PDF 32000-1:2008 §12.7.8.

## Properties

### `IsSingleStream`

```csharp
bool IsSingleStream
```

Gets a value indicating whether the `/XFA` entry is a single combined XDP stream (true) rather than an array of named packets (false). When true, `Packets` holds one packet with an empty `XfaPacket.Name`.

### `Packets`

```csharp
IReadOnlyList<XfaPacket> Packets => _packets
```

Gets the XFA packets in document order.

## Methods

### `Get`

```csharp
XfaPacket? Template => Get("template")
```

Gets the `template` packet (form layout), or null when absent.

### `Get`

```csharp
XfaPacket? Datasets => Get("datasets")
```

Gets the `datasets` packet (data layer), or null when absent.

### `Get`

```csharp
XfaPacket? Config => Get("config")
```

Gets the `config` packet, or null when absent.

### `Get`

```csharp
XfaPacket? Form => Get("form")
```

Gets the `form` packet, or null when absent.

### `BuildDataFields`

```csharp
IReadOnlyList<XfaDataField> DataFields => _dataFields ??= BuildDataFields()
```

Gets the data layer as a flat list of fields walked from the `datasets` packet's `&lt;xfa:data&gt;` subtree, each with a best-effort `XfaGeometry` when a matching AcroForm widget is found. Empty when there is no `datasets` packet. Computed once and cached.

### `Get`

```csharp
XfaPacket? Get(string name)
```

Returns the packet with the given name (ordinal match), or null when no such packet exists.

**Parameters**

- `name` — The packet name, for example `"datasets"`.

---

_Source: [`src/Chuvadi.Pdf.Documents/XfaPackets.cs`](../../../src/Chuvadi.Pdf.Documents/XfaPackets.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
