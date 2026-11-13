# FormWidget

**Class** in `Chuvadi.Pdf.Forms` (Forms)

One widget annotation of a form field: where the field is shown, and for a check box or radio button the state that turns it on.

```csharp
public sealed class FormWidget
```

## Constructors

### `FormWidget(int pageIndex, PdfRectangle rect, PdfObjectId objectId, string? onState, string? exportValue)`

Initialises a new `FormWidget`.

**Parameters**

- `pageIndex` — The zero-based page the widget is on, or -1.
- `rect` — The widget rectangle in user space.
- `objectId` — The widget's object id, or 0 0 when it is a direct object.
- `onState` — A button widget's "on" appearance state name, or null.
- `exportValue` — A button widget's export value, or null.

## Properties

### `PageIndex`

```csharp
int PageIndex
```

Gets the zero-based index of the page whose `/Annots` lists the widget, or -1 when no page does.

### `Rect`

```csharp
PdfRectangle Rect
```

Gets the widget rectangle (`/Rect`) in PDF user space, normalised so that `PdfRectangle.X1` and `PdfRectangle.Y1` are the lower-left corner.

### `ObjectId`

```csharp
PdfObjectId ObjectId
```

Gets the object id of the widget annotation. For a field whose only widget is merged into the field dictionary, this is the field's id; for a widget written as a direct object it is 0 0.

### `OnState`

```csharp
string? OnState
```

Gets, for a check box or radio button, the name of the appearance state that shows the widget as on (the `/AP /N` key other than `Off`); null for other fields.

### `ExportValue`

```csharp
string? ExportValue
```

Gets, for a check box or radio button, the value the widget stands for: the field's `/Opt` entry for this widget when the field has one, otherwise `OnState`; null for other fields.

---

_Source: [`src/Chuvadi.Pdf.Forms/FormWidget.cs`](../../../src/Chuvadi.Pdf.Forms/FormWidget.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
