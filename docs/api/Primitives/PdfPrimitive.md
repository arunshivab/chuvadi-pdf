# PdfPrimitive

**Class** in `Chuvadi.Pdf.Primitives` (Primitives)

Abstract base class for all PDF primitive object types.

```csharp
public abstract class PdfPrimitive
```

## Remarks

The PDF specification defines eight primitive types: 
 
- `PdfNull` — the null object 
- `PdfBoolean` — true or false 
- `PdfInteger` — a signed integer 
- `PdfReal` — a floating-point number 
- `PdfName` — an interned symbolic name (e.g. /Type) 
- `PdfString` — a byte string (literal or hex-encoded) 
- `PdfArray` — an ordered sequence of primitives 
- `PdfDictionary` — a keyed map of primitives 
- `PdfStream` — a dictionary plus a binary byte payload 
- `PdfReference` — an indirect reference to another object  All primitive instances are immutable. Mutable document structures (pages, annotations, form fields) are in `Chuvadi.Pdf.Documents`. PDF 32000-1:2008 §7.3 — Objects.

## Properties

### `PrimitiveType`

```csharp
abstract PdfPrimitiveType PrimitiveType
```

Gets the PDF type of this primitive.

### `IsNull`

```csharp
bool IsNull => PrimitiveType == PdfPrimitiveType.Null
```

Returns true if this primitive is `PdfNull`.

## Methods

### `As<T>`

```csharp
T? As<T>() where T : PdfPrimitive => this as T
```

Attempts to cast this primitive to <typeparamref name="T"/>. <typeparam name="T">The target primitive type.</typeparam>

**Returns:** This instance cast to <typeparamref name="T"/>, or `null` if the cast is not valid.

### `Cast<T>`

```csharp
T Cast<T>() where T : PdfPrimitive
```

Casts this primitive to <typeparamref name="T"/>, throwing if the cast is not valid. <typeparam name="T">The target primitive type.</typeparam>

**Returns:** This instance cast to <typeparamref name="T"/>. <exception cref="InvalidCastException"> Thrown when this primitive is not of type <typeparamref name="T"/>. </exception>

### `ToString`

```csharp
abstract override string ToString()
```

Returns a PDF-syntax string representation of this primitive, suitable for use in a PDF content stream or object definition.

---

_Source: [`src/Chuvadi.Pdf.Primitives/PdfPrimitive.cs`](../../../src/Chuvadi.Pdf.Primitives/PdfPrimitive.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
