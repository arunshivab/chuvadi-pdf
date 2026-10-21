# FilterPipeline

**Class** in `Chuvadi.Pdf.Filters` (Filters)

Applies and removes chains of PDF stream filters. PDF 32000-1:2008 §7.4.1.

```csharp
public sealed class FilterPipeline
```

## Constructors

### `FilterPipeline()`

Creates a pipeline with DeflateFilter pre-registered.

## Methods

### `Empty`

__static__

```csharp
static FilterPipeline Empty()
```

Creates an empty pipeline. Use `Register` to add filters.

### `Register`

```csharp
void Register(IStreamFilter filter)
```

Registers a filter. Replaces any existing registration for the same name.

### `RegisterAlias`

```csharp
void RegisterAlias(string alias, string canonicalName)
```

Registers an alias pointing to an already-registered filter.

### `IsRegistered`

```csharp
bool IsRegistered(string filterName)
```

Returns true if a filter with the given name is registered.

### `Decode`

```csharp
byte[] Decode(string filterName, byte[] encoded, FilterParameters? parms = null)
```

Decodes `encoded` by removing the named filter.

### `DecodeChain`

```csharp
byte[] DecodeChain(IReadOnlyList<string> filterNames, byte[] encoded, IReadOnlyList<FilterParameters?>? parmsArray = null)
```

Decodes `encoded` through a chain of filters, first to last.

### `Encode`

```csharp
byte[] Encode(string filterName, byte[] raw, FilterParameters? parms = null)
```

Encodes `raw` by applying the named filter.

---

_Source: [`src/Chuvadi.Pdf.Filters/FilterPipeline.cs`](../../../src/Chuvadi.Pdf.Filters/FilterPipeline.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
