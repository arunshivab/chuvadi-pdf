# XfaPacket

**Class** in `Chuvadi.Pdf.Documents` (Documents)

A single named packet of an XFA (XML Forms Architecture) form, such as the `template`, `datasets`, or `config` packet. The packets are stored under the document's `/AcroForm /XFA` entry, either as one XDP stream (in which case there is a single packet with an empty name) or as an array of name/stream pairs. PDF 32000-1:2008 §12.7.8.

```csharp
public sealed class XfaPacket
```

## Properties

### `Name`

```csharp
string Name
```

Gets the packet name as stored in the `/XFA` array (for example `"template"` or `"datasets"`). Empty for the single packet of a document whose `/XFA` entry is one combined XDP stream.

### `Xml`

```csharp
byte[] Xml
```

Gets the packet's XML content with all stream filters already removed (for example FlateDecode). The bytes are the raw XML as authored.

## Methods

### `Encoding.UTF8.GetString`

```csharp
string Text => _text ??= Encoding.UTF8.GetString(Xml)
```

Gets the packet's XML decoded as a UTF-8 string. Computed once on first access and cached. A leading byte-order mark, when present, is preserved in the returned text.

---

_Source: [`src/Chuvadi.Pdf.Documents/XfaPacket.cs`](../../../src/Chuvadi.Pdf.Documents/XfaPacket.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
