# XfaValue

**Class** in `Chuvadi.Pdf.Xfa.Model` (Xfa)

The value of a field or draw, carrying its resolved text content.

```csharp
public sealed class XfaValue : XfaNode
```

## Properties

### `ElementName`

```csharp
override string ElementName => "value"
```

<inheritdoc />

### `Text`

```csharp
string? Text
```

Gets or sets the plain-text content of the value, when present.

### `RichText`

```csharp
string? RichText
```

Gets or sets the rich-text (XHTML) content, when the value uses `exData` with an HTML content type. Null for plain values.

### `ImageBase64`

```csharp
string? ImageBase64
```

Gets or sets the base64-encoded image payload (from an `&lt;image&gt;` value), used by image fields. Null when the value carries no image.

---

_Source: [`src/Chuvadi.Pdf.Xfa/Model/XfaNode.cs`](../../../src/Chuvadi.Pdf.Xfa/Model/XfaNode.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
