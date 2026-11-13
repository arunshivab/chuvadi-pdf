# FormField

**Class** in `Chuvadi.Pdf.Forms` (Forms)

A single AcroForm field, read from a PDF document. PDF 32000-1:2008 §12.7.3 — Field dictionaries.

```csharp
public sealed class FormField
```

## Constructors

### `FormField(string fullyQualifiedName, FormFieldType type, string? value, PdfObjectId objectId, IReadOnlyList<FormField> children)`

Initialises a new `FormField`.

## Properties

### `FullyQualifiedName`

```csharp
string FullyQualifiedName
```

Gets the fully qualified field name (e.g., "patient.firstName"), formed by joining ancestor partial names with periods. PDF 32000-1:2008 §12.7.3.2.

### `Type`

```csharp
FormFieldType Type
```

Gets the field type.

### `Value`

```csharp
string? Value
```

Gets the current field value as a string. Null for unset fields, signature fields, or button parent fields.

### `ObjectId`

```csharp
PdfObjectId ObjectId
```

Gets the object ID of the underlying field dictionary in the PDF. Used by `FormFiller` to locate fields for value updates.

### `Children`

```csharp
IReadOnlyList<FormField> Children
```

Gets the nested child fields, if any.

### `IsLeaf`

```csharp
bool IsLeaf => Children.Count == 0
```

Returns true when this is a leaf field (no children).

### `Widgets`

```csharp
IReadOnlyList<FormWidget> Widgets
```

Gets the field's widget annotations: where it is shown, and for a check box or radio button the state that turns each widget on (3.34.0). Empty for a field with no widget of its own (a parent of other fields).

### `Options`

```csharp
IReadOnlyList<FormChoiceOption> Options
```

Gets a choice field's options (`/Opt`), in order (3.34.0). Empty for other fields.

### `Flags`

```csharp
FormFieldFlags Flags
```

Gets the field flags (`/Ff`, inherited from ancestors when the field has none of its own) (3.34.0).

### `MaxLength`

```csharp
int? MaxLength
```

Gets a text field's maximum length (`/MaxLen`, inherited), or null when it has none (3.34.0).

---

_Source: [`src/Chuvadi.Pdf.Forms/FormField.cs`](../../../src/Chuvadi.Pdf.Forms/FormField.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
