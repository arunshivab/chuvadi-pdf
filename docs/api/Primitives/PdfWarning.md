# PdfWarning

**Class** in `Chuvadi.Pdf.Primitives` (Primitives)

Reports a fallback taken while working on a document: something in the file could not be used, and Chuvadi carried on without it instead of failing. Receive warnings with `PdfOpenOptions.OnWarning`.

```csharp
public sealed class PdfWarning
```

## Constructors

### `PdfWarning(PdfWarningKind kind, int? pageNumber, string subject, string reason)`

Initializes a new warning.

**Parameters**

- `kind` — What happened.
- `pageNumber` — The 1-based page being processed, or null when not known.
- `subject` — What it happened to, for example `font /F1`.
- `reason` — Why, in plain words.

## Properties

### `Kind`

```csharp
PdfWarningKind Kind
```

Gets what happened.

### `PageNumber`

```csharp
int? PageNumber
```

Gets the 1-based page being processed, or null when not known.

### `Subject`

```csharp
string Subject
```

Gets what it happened to, for example `font /F1` or `shading 12 0 R`.

### `Reason`

```csharp
string Reason
```

Gets why it happened and what was done instead, in plain words.

## Methods

### `ToString`

```csharp
override string ToString()
```

Returns the warning as one line, for example for a log.

**Returns:** Page, kind, subject and reason.

---

_Source: [`src/Chuvadi.Pdf.Primitives/PdfWarning.cs`](../../../src/Chuvadi.Pdf.Primitives/PdfWarning.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
