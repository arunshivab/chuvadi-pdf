# FormException

**Class** in `Chuvadi.Pdf.Forms` (Forms)

Thrown when an AcroForm or outline operation fails.

```csharp
public sealed class FormException : PdfException
```

## Remarks

Since 3.34.0 an exception thrown by `FormFiller` for a value it refuses carries the details as properties beside the English message: `Reason`, `FieldName`, `Value`, `ValidStates` and `MaxLength`.

## Constructors

### `FormException()`

Initialises a new `FormException` with no message.

### `FormException(string message)`

Initialises a new `FormException` with a message.

### `FormException(string message, Exception innerException)`

Initialises a new `FormException` with a message and inner exception.

## Properties

### `Reason`

```csharp
FormErrorReason? Reason
```

Gets why `FormFiller` refused a value, or null for any other form error (3.34.0).

### `FieldName`

```csharp
string? FieldName
```

Gets the fully qualified name of the field the error is about, or null (3.34.0).

### `Value`

```csharp
string? Value
```

Gets the value that was refused, or null (3.34.0).

### `ValidStates`

```csharp
IReadOnlyList<string> ValidStates
```

Gets the values the field accepts, for an app to list in its own message (3.34.0): for `FormErrorReason.UnknownState` each button's on-state, its export value when that differs, and `Off`; for `FormErrorReason.NotInOptions` the export values of the field's options, in order. Empty for the other reasons.

### `MaxLength`

```csharp
int? MaxLength
```

Gets the field's maximum length in Unicode code points, for `FormErrorReason.TooLong`; otherwise null (3.34.0).

---

_Source: [`src/Chuvadi.Pdf.Forms/FormException.cs`](../../../src/Chuvadi.Pdf.Forms/FormException.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
