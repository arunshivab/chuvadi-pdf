# DisplayListBuilder

**Class** in `Chuvadi.Pdf.Rendering.Raster` (Rendering)

Builds a `PageDisplayList` from a `PdfPage` by interpreting the page's content stream.

```csharp
public static class DisplayListBuilder
```

## Remarks

The builder is renderer-neutral. It walks the PDF operator stream once, maintaining graphics-state and path-construction state, and emits an immutable sequence of `RenderOp` values into a `PageDisplayList`. Every op carries the CTM-baked geometry plus a snapshot of the active clip paths, so downstream consumers (pixel rasterizer, SVG writer, accessibility walker) do not need to track CTM or clip-stack state.  

 Operators supported in v2.0.0 R1: q Q cm; w J j M d (state); g G rg RG k K sc SC scn SCN cs CS (colour); m l c v y h re (path construction); S s f F f* B B* b b* n (path painting); W W* (clipping); BT ET Tf Tc Tw Tz TL Ts Tr Td TD Tm T* Tj TJ ' " (text); Do (XObject - Image and Form); BMC BDC EMC MP DP BX EX (marked content / compatibility - no-op).  

 Operators deferred to v2.1+: sh (shading), Pattern colorspaces (sc/scn with /Pattern), BI/ID/EI (inline images), ExtGState soft masks.

## Methods

### `Build`

__static__

```csharp
static PageDisplayList Build(PdfPage page, PdfObjectStore objects, double hintingScale = 0.0, bool lightHinting = false, bool autohintFallback = true)
```

Builds a display list for the page's content stream.

**Parameters**

- `page` — The PDF page to interpret.
- `objects` — The object store for resolving indirect references.
- `hintingScale` — Device scale (DPI/72) for grid-fitting; 0 disables hinting (raster path only).
- `lightHinting` — When true, grid-fit the Y axis only (lighter, grayscale-friendly).
- `autohintFallback` — When true (the default), glyphs of fonts with no hinting programs are grid-fitted by the geometric autohinter.

**Returns:** An immutable display list. Empty if the page has no content stream. CTM-baked geometry; per-op clip snapshots. Page rotation is not applied here; that is a consumer concern. <exception cref="ArgumentNullException"> Thrown when `page` or `objects` is null. </exception>

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/Raster/DisplayListBuilder.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/Raster/DisplayListBuilder.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
