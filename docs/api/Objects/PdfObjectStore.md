# PdfObjectStore

**Class** in `Chuvadi.Pdf.Objects` (Objects)

In-memory store for PDF indirect objects, with lazy indirect reference resolution.

```csharp
public sealed class PdfObjectStore : IPdfObjectResolver
```

## Remarks

`PdfObjectStore` is the central object graph used during PDF reading, writing, and modification. It maps `PdfObjectId` values to `PdfIndirectObject` instances and resolves `PdfReference` primitives to their target values. Lazy loading: the store does not pre-populate itself. Objects are added on demand as the reader encounters them or as the document model requests them. An optional `Func{T, TResult}` loader delegate can be provided to load objects on demand from the underlying PDF stream. Thread safety: not thread-safe. Synchronise externally if needed.

## Constructors

### `PdfObjectStore()`

Creates an empty `PdfObjectStore` with no loader. Objects must be added explicitly via `Add(PdfIndirectObject)`.

### `PdfObjectStore(Func<PdfObjectId, PdfIndirectObject?> loader)`

Creates a `PdfObjectStore` with a loader delegate. When an object is not in the store, the loader is called and the result is cached.

**Parameters**

- `loader` — A function that loads an object given its `PdfObjectId`. Return null when the object does not exist.

## Properties

### `Limits`

```csharp
PdfLimits? Limits
```

Gets or sets the resource limits for work on this document's objects (set from `PdfOpenOptions.Limits` when the document is opened). `PageRasterizer` and `TextExtractor` apply them to every call. Null: no limits.

### `Count`

```csharp
int Count => _objects.Count
```

Gets the number of objects currently loaded in the store.

### `Objects`

```csharp
IEnumerable<PdfIndirectObject> Objects => _objects.Values
```

Gets all indirect objects currently in the store.

## Methods

### `Add`

```csharp
void Add(PdfIndirectObject obj)
```

Adds or replaces an indirect object in the store.

### `Add`

```csharp
void Add(PdfObjectId id, PdfPrimitive value)
```

Adds a primitive with the given identity as an indirect object.

### `Remove`

```csharp
bool Remove(PdfObjectId id)
```

Removes the object with the given identity from the store. Returns true if it was present.

### `TryGet`

```csharp
bool TryGet(PdfObjectId id, out PdfIndirectObject? obj)
```

Attempts to get the indirect object with the given identity. If not in the store and a loader was provided, the loader is called.

### `Resolve`

```csharp
PdfPrimitive Resolve(PdfPrimitive primitive)
```

<inheritdoc/>

### `ResolveById`

```csharp
PdfPrimitive ResolveById(PdfObjectId id)
```

<inheritdoc/>

### `Contains`

```csharp
bool Contains(PdfObjectId id)
```

<inheritdoc/>

### `ResolveDictionaryEntry<T>`

```csharp
T? ResolveDictionaryEntry<T>(PdfDictionary dictionary, PdfName key)
```

Resolves a `PdfDictionary` entry that may be a direct or indirect value. Returns null when the key is absent or the value is of the wrong type.

---

_Source: [`src/Chuvadi.Pdf.Objects/PdfObjectStore.cs`](../../../src/Chuvadi.Pdf.Objects/PdfObjectStore.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
