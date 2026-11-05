# TransparencyGroup

**Record** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

One transparency group on a page (3.31): a form XObject with `/Group /S /Transparency`, each time it is drawn. Its ops are one unbroken run of the page's op list, `FirstOpIndex` to `FirstOpIndex` + `OpCount` - 1, and a nested group's run lies inside its parent's. Groups that draw nothing are left out. Ids are 1, 2, 3 … in the order the groups start.

```csharp
public sealed record TransparencyGroup
```

## Properties

### `Id`

```csharp
required int Id
```

The group's id, as used in `RenderOp.GroupIds`.

### `ParentId`

```csharp
int? ParentId
```

The id of the group this one is drawn inside, or null for a top-level group.

### `Depth`

```csharp
required int Depth
```

How many groups enclose this one, plus one (1 for a top-level group).

### `Isolated`

```csharp
bool Isolated
```

The `/I` (isolated) flag.

### `Knockout`

```csharp
bool Knockout
```

The `/K` (knockout) flag.

### `ColorSpace`

```csharp
string? ColorSpace
```

The `/CS` colour space family name (for example "DeviceRGB" or "ICCBased"), or null.

### `Box`

```csharp
PdfRectangle Box
```

The form's `/BBox` mapped to page space: the bounding box of its four transformed corners.

### `Matrix`

```csharp
AffineMatrix Matrix
```

The form-to-page transform: the CTM when the form is drawn, with its `/Matrix`.

### `Opacity`

```csharp
double Opacity
```

The constant alpha the group is composited with (`/ca` when the form is drawn, relative to the parent group or the page).

### `StrokeOpacity`

```csharp
double StrokeOpacity
```

The stroking constant alpha (`/CA`) in effect when the form is drawn.

### `BlendMode`

```csharp
PdfBlendMode BlendMode
```

The blend mode the group is composited with.

### `SoftMask`

```csharp
SoftMaskInfo? SoftMask
```

The soft mask the group is composited through, or null.

### `Form`

```csharp
PdfStream? Form
```

The form XObject.

### `FirstOpIndex`

```csharp
int FirstOpIndex
```

The index of the group's first op in the page's op list.

### `OpCount`

```csharp
int OpCount
```

The number of ops in the group's run, nested groups' ops included.

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/TransparencyGroup.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/TransparencyGroup.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
