# XfaPageArea

**Class** in `Chuvadi.Pdf.Xfa.Model` (Xfa)

A single page area, defining its size and content region.

```csharp
public sealed class XfaPageArea : XfaNode
```

## Properties

### `ElementName`

```csharp
override string ElementName => "pageArea"
```

<inheritdoc />

### `MediumLong`

```csharp
XfaMeasurement? MediumLong
```

Gets or sets the long edge of the page medium, when specified.

### `MediumShort`

```csharp
XfaMeasurement? MediumShort
```

Gets or sets the short edge of the page medium, when specified.

### `Landscape`

```csharp
bool Landscape
```

Gets or sets a value indicating whether the medium is oriented landscape (long edge horizontal).

### `MinOccur`

```csharp
int MinOccur
```

Gets or sets the minimum occurrence count (from `&lt;occur min&gt;`).

### `MaxOccur`

```csharp
int MaxOccur
```

Gets or sets the maximum occurrence count (from `&lt;occur max&gt;`). -1 means unbounded: the page area repeats for as long as overflow demands.

### `OddOrEven`

```csharp
XfaOddOrEven OddOrEven
```

Gets or sets which page parity this area may serve (duplex pagination).

---

_Source: [`src/Chuvadi.Pdf.Xfa/Model/XfaNode.cs`](../../../src/Chuvadi.Pdf.Xfa/Model/XfaNode.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
