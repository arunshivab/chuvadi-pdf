# PdfArray

**Class** in `Chuvadi.Pdf.Primitives` (Primitives)

Represents a PDF array object — an ordered sequence of primitives. PDF 32000-1:2008 §7.3.6 — Array objects.

```csharp
public sealed class PdfArray : PdfPrimitive, IReadOnlyList<PdfPrimitive>
```

## Constructors

### `PdfArray()`

Creates an empty `PdfArray`.

### `PdfArray(int capacity)`

Creates a `PdfArray` with the given initial capacity.

### `PdfArray(IEnumerable<PdfPrimitive> items)`

Creates a `PdfArray` from an existing sequence.

## Properties

### `Count`

```csharp
int Count => _items.Count
```

<inheritdoc/>

### `index]`

```csharp
PdfPrimitive this[int index] => _items[index]
```

<inheritdoc/>

### `PrimitiveType`

```csharp
override PdfPrimitiveType PrimitiveType => PdfPrimitiveType.Array
```

<inheritdoc/>

## Methods

### `GetEnumerator`

```csharp
IEnumerator<PdfPrimitive> GetEnumerator() => _items.GetEnumerator()
```

<inheritdoc/>

### `Add`

```csharp
void Add(PdfPrimitive item) => _items.Add(item)
```

Appends a primitive to the end of the array.

### `Insert`

```csharp
void Insert(int index, PdfPrimitive item) => _items.Insert(index, item)
```

Inserts a primitive at the given index.

### `RemoveAt`

```csharp
void RemoveAt(int index) => _items.RemoveAt(index)
```

Removes the element at the given index.

### `GetAs<T>`

```csharp
T? GetAs<T>(int index) where T : PdfPrimitive => _items[index] as T
```

Gets the element at `index` cast to <typeparamref name="T"/>, or null.

### `GetInteger`

```csharp
int GetInteger(int index) => _items[index].Cast<PdfInteger>().Value
```

Gets the element at `index` as an integer value.

### `GetNumber`

```csharp
double GetNumber(int index) => PdfReal.ToDouble(_items[index])
```

Gets the element at `index` as a double value.

### `AsRectangle`

```csharp
(double X1, double Y1, double X2, double Y2) AsRectangle()
```

Interprets this array as a PDF rectangle [x1, y1, x2, y2]. <exception cref="InvalidOperationException"> Thrown when the array does not contain exactly four numeric elements. </exception>

### `ToString`

```csharp
override string ToString()
```

Returns the PDF syntax representation, e.g. `[1 0 R /Name 42]`.

---

_Source: [`src/Chuvadi.Pdf.Primitives/PdfArray.cs`](../../../src/Chuvadi.Pdf.Primitives/PdfArray.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
