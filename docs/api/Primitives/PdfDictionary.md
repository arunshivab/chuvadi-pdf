# PdfDictionary

**Class** in `Chuvadi.Pdf.Primitives` (Primitives)

Represents a PDF dictionary object — a map from `PdfName` keys to `PdfPrimitive` values. PDF 32000-1:2008 §7.3.7 — Dictionary objects.

```csharp
public sealed class PdfDictionary : PdfPrimitive, IReadOnlyDictionary<PdfName, PdfPrimitive>
```

## Constructors

### `PdfDictionary()`

Creates an empty `PdfDictionary`.

### `PdfDictionary(int capacity)`

Creates a `PdfDictionary` with the given initial capacity.

## Properties

### `Count`

```csharp
int Count => _entries.Count
```

<inheritdoc/>

### `key]`

```csharp
PdfPrimitive this[PdfName key] => _entries[key]
```

<inheritdoc/>

### `Keys`

```csharp
IEnumerable<PdfName> Keys => _entries.Keys
```

<inheritdoc/>

### `Values`

```csharp
IEnumerable<PdfPrimitive> Values => _entries.Values
```

<inheritdoc/>

### `PrimitiveType`

```csharp
override PdfPrimitiveType PrimitiveType => PdfPrimitiveType.Dictionary
```

<inheritdoc/>

## Methods

### `ContainsKey`

```csharp
bool ContainsKey(PdfName key) => _entries.ContainsKey(key)
```

<inheritdoc/>

### `TryGetValue`

```csharp
bool TryGetValue(PdfName key, [MaybeNullWhen(false)] out PdfPrimitive value)
```

<inheritdoc/>

### `Set`

```csharp
void Set(PdfName key, PdfPrimitive value) => _entries[key] = value
```

Sets or replaces the value for `key`.

### `Set`

```csharp
void Set(PdfName key, int value) => _entries[key] = new PdfInteger(value)
```

Sets or replaces an integer value.

### `Set`

```csharp
void Set(PdfName key, bool value) => _entries[key] = PdfBoolean.FromBool(value)
```

Sets or replaces a boolean value.

### `Remove`

```csharp
bool Remove(PdfName key) => _entries.Remove(key)
```

Removes the entry with the given key. Returns true if removed.

### `GetName`

```csharp
PdfName? GetName(PdfName key) => GetAs<PdfName>(key)
```

Gets the value as a `PdfName`, or null if absent.

### `GetNumber`

```csharp
double GetNumber(PdfName key, double defaultValue = 0.0)
```

Gets the value as a double. Accepts both `PdfInteger` and `PdfReal` values.

### `GetDictionary`

```csharp
PdfDictionary? GetDictionary(PdfName key) => GetAs<PdfDictionary>(key)
```

Gets the value as a `PdfDictionary`, or null if absent.

### `GetArray`

```csharp
PdfArray? GetArray(PdfName key) => GetAs<PdfArray>(key)
```

Gets the value as a `PdfArray`, or null if absent.

### `GetName`

```csharp
PdfName? Type => GetName(PdfName.Type)
```

Gets the `/Type` entry, or null if absent.

### `GetName`

```csharp
PdfName? Subtype => GetName(PdfName.Subtype)
```

Gets the `/Subtype` entry, or null if absent.

### `ToString`

```csharp
override string ToString()
```

Returns the PDF syntax representation.

---

_Source: [`src/Chuvadi.Pdf.Primitives/PdfDictionary.cs`](../../../src/Chuvadi.Pdf.Primitives/PdfDictionary.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
