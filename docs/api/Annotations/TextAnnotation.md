# TextAnnotation

**Class** in `Chuvadi.Pdf.Annotations` (Annotations)

Sticky-note text annotation (§12.5.6.4).

```csharp
public sealed class TextAnnotation : PdfAnnotation
```

## Properties

### `IconName`

```csharp
string IconName
```

Gets the icon name. Standard values: Comment, Key, Note, Help, NewParagraph, Paragraph, Insert. Default: Note.

### `IsOpen`

```csharp
bool IsOpen
```

Gets whether the annotation pops open by default.

---

_Source: [`src/Chuvadi.Pdf.Annotations/Annotations.cs`](../../../src/Chuvadi.Pdf.Annotations/Annotations.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
