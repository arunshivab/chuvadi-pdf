# ReportFont

**Class** in `Chuvadi.Pdf.Authoring` (Authoring)

A report font: a Standard-14 family plus bold/italic flags, resolved to the matching Standard-14 PostScript name at draw time.

```csharp
public sealed class ReportFont
```

## Properties

### `Family`

```csharp
ReportFontFamily Family
```

Gets or initialises the font family. Default: Helvetica.

### `Bold`

```csharp
bool Bold
```

Gets or initialises whether the bold variant is used.

### `Italic`

```csharp
bool Italic
```

Gets or initialises whether the italic (oblique) variant is used.

### `Helvetica`

__static__

```csharp
static ReportFont Helvetica
```

Regular Helvetica.

### `HelveticaBold`

__static__

```csharp
static ReportFont HelveticaBold
```

Bold Helvetica.

### `Times`

__static__

```csharp
static ReportFont Times
```

Regular Times.

### `TimesBold`

__static__

```csharp
static ReportFont TimesBold
```

Bold Times.

### `Courier`

__static__

```csharp
static ReportFont Courier
```

Regular Courier.

## Methods

### `Resolve`

```csharp
string Resolve()
```

Resolves to the Standard-14 PostScript font name.

---

_Source: [`src/Chuvadi.Pdf.Authoring/ReportStyles.cs`](../../../src/Chuvadi.Pdf.Authoring/ReportStyles.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
