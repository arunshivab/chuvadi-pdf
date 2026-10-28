# PdfWorkScope

**Class** in `Chuvadi.Pdf.Primitives` (Primitives)

An ambient cancellation token and set of `PdfLimits` for the Chuvadi work done on the current asynchronous flow until it is disposed.

```csharp
public sealed class PdfWorkScope : IDisposable
```

## Remarks

Content walking, text parsing, painting, stream decoding and image decoding check the scope at short intervals: a cancelled token throws `OperationCanceledException` (well within a second on ordinary hardware), and a limit that would be passed throws `PdfLimitExceededException`.  

 `PageRasterizer` and `TextExtractor` open a scope per call; open one yourself to cancel or limit other work: 
```
 using (PdfWorkScope.Enter(limits, token)) { string svg = new SvgRenderer().RenderPage(doc, 0); } 
```
 Scopes nest: work inside an inner scope also honours every enclosing scope's token and limits. The scope flows with the execution context (into awaited code and `Task.Run`), and is restored when disposed. Dispose scopes in the reverse order of entering them; a scope disposed early (out of order) simply stops applying.

## Properties

### `Limits`

```csharp
PdfLimits Limits
```

Gets this scope's limits.

### `CancellationToken`

```csharp
CancellationToken CancellationToken
```

Gets this scope's cancellation token.

## Methods

### `Live`

__static__

```csharp
static PdfWorkScope? Current => Live(CurrentScope.Value)
```

Gets the innermost open scope on the current flow, or null when none is open.

### `Interlocked.Read`

```csharp
long MemoryBytes => Interlocked.Read(ref _memoryBytes)
```

Gets the large-buffer bytes charged to this scope so far.

### `Interlocked.Read`

```csharp
long ContentOperators => Interlocked.Read(ref _operators)
```

Gets the content-stream operators counted in this scope so far.

### `Enter`

__static__

```csharp
static PdfWorkScope Enter(PdfLimits? limits, CancellationToken cancellationToken = default)
```

Opens a scope on the current flow. Dispose it (for example with `using`) to restore the previous scope.

**Parameters**

- `limits` — The limits to apply; null for none.
- `cancellationToken` — The token to honour.

**Returns:** The new scope.

### `ThrowIfCancellationRequested`

__static__

```csharp
static void ThrowIfCancellationRequested()
```

Throws `OperationCanceledException` when the token of the current scope or of any enclosing scope is cancelled. Does nothing outside a scope.

### `CountOperators`

__static__

```csharp
static void CountOperators(long count = 1)
```

Counts content-stream operators against `PdfLimits.MaxContentOperators` and checks cancellation.

**Parameters**

- `count` — The number of operators processed.

### `ChargeMemory`

__static__

```csharp
static void ChargeMemory(long bytes)
```

Charges a large buffer against `PdfLimits.MaxMemoryBytes` and checks cancellation. Call before allocating it.

**Parameters**

- `bytes` — The buffer size in bytes.

### `CheckDecodedSize`

__static__

```csharp
static void CheckDecodedSize(long bytes)
```

Checks a stream's decoded size so far against `PdfLimits.MaxDecodedStreamBytes` and checks cancellation. Decoders call this while they produce output.

**Parameters**

- `bytes` — The decoded size so far, in bytes.

### `ChargeRaster`

__static__

```csharp
static void ChargeRaster(long width, long height)
```

Checks an output raster's size against `PdfLimits.MaxOutputPixels`, then charges its bytes (four per pixel) as memory. Call before allocating it.

**Parameters**

- `width` — The raster width in pixels.
- `height` — The raster height in pixels.

### `CheckOutputPixels`

__static__

```csharp
static void CheckOutputPixels(long width, long height)
```

Checks an output raster's size against `PdfLimits.MaxOutputPixels` without charging memory.

**Parameters**

- `width` — The raster width in pixels.
- `height` — The raster height in pixels.

### `Dispose`

```csharp
void Dispose()
```

Restores the scope that was current when this one was entered.

---

_Source: [`src/Chuvadi.Pdf.Primitives/PdfWorkScope.cs`](../../../src/Chuvadi.Pdf.Primitives/PdfWorkScope.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
