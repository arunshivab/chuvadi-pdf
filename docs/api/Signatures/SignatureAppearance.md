# SignatureAppearance

**Class** in `Chuvadi.Pdf.Signatures.Signing` (Signatures)

Visible appearance for a signature field. When supplied on `PdfSigningOptions.Appearance`, the signature field is also marked as a Widget annotation, placed on a specific page within the given rectangle, and gets an appearance stream that PDF readers render in the page view.

```csharp
public sealed class SignatureAppearance
```

## Remarks

A signature is cryptographically complete without an appearance, and most automated workflows don't need one. Visible appearances matter for human-reviewed documents (contracts, agreements) where a reader expects to see "signed by …" on the page.  

 Since 3.32.0 Chuvadi draws a default appearance: the lines of `Text` (by default "Digitally signed by {signer's common name}" and "Date: {signing time}", in UTC, ISO 8601), sized to fit the box, with `Image` on the left third when one is given, and a thin black border. Text in a script Helvetica cannot write is drawn with the matching LiPi Sans face, embedded as a subset. Before 3.32.0 the default was the border alone.  

 When `PreRenderedAppearanceStream` is supplied, those bytes are used verbatim as the Form XObject content stream instead, and the caller is responsible for whatever fonts / colors / images they reference; `Text` and `Image` are then not used.

## Properties

### `PageIndex`

```csharp
required int PageIndex
```

Zero-based index of the page on which to place the widget.

### `Rectangle`

```csharp
required double[] Rectangle
```

Rectangle in default user space: [llx, lly, urx, ury].

### `PreRenderedAppearanceStream`

```csharp
byte[]? PreRenderedAppearanceStream
```

Optional pre-rendered Form XObject content stream. When null, the default appearance is generated (see the class remarks). When given, it wins: `Text` and `Image` are not used.

### `Text`

```csharp
string? Text
```

The text of the default appearance (3.32.0); `\n` separates lines. Null (the default) gives "Digitally signed by {signer's common name}" and "Date: {signing time}" (UTC, ISO 8601, e.g. 2026-11-07T10:30:00Z). An empty string draws no text, for an image-only appearance.

### `Image`

```csharp
byte[]? Image
```

A JPEG or PNG image for the default appearance (3.32.0): drawn on the left third of the box, beside the text, or filling the box when `Text` is empty. It keeps its aspect ratio; PNG transparency is kept.

---

_Source: [`src/Chuvadi.Pdf.Signatures/Signing/SignatureAppearance.cs`](../../../src/Chuvadi.Pdf.Signatures/Signing/SignatureAppearance.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
