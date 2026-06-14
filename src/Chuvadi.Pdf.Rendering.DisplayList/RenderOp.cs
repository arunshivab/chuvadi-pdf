// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2.1 — display-list intermediate

using System.Collections.Generic;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>Abstract base for all display-list operations.</summary>
public abstract class RenderOp
{
    /// <summary>Discriminator for switch-pattern dispatch.</summary>
    public abstract RenderOpKind Kind { get; }
}

/// <summary>Tag identifying the concrete <see cref="RenderOp"/> subtype.</summary>
public enum RenderOpKind
{
    /// <summary><see cref="PathOp"/>.</summary>
    Path = 0,
    /// <summary><see cref="TextOp"/>.</summary>
    Text = 1,
    /// <summary><see cref="ImageOp"/>.</summary>
    Image = 2,
    /// <summary><see cref="ClipOp"/>.</summary>
    Clip = 3,
    /// <summary><see cref="TransformOp"/>.</summary>
    Transform = 4,
    /// <summary><see cref="OpacityOp"/>.</summary>
    Opacity = 5,
    /// <summary><see cref="BlendModeOp"/>.</summary>
    BlendMode = 6,
}

/// <summary>Line cap style (PDF §8.4.3.3).</summary>
public enum LineCap
{
    /// <summary>Butt cap (default).</summary>
    Butt = 0,
    /// <summary>Round cap.</summary>
    Round = 1,
    /// <summary>Projecting square cap.</summary>
    Square = 2,
}

/// <summary>Line join style (PDF §8.4.3.4).</summary>
public enum LineJoin
{
    /// <summary>Miter (default).</summary>
    Miter = 0,
    /// <summary>Round.</summary>
    Round = 1,
    /// <summary>Bevel.</summary>
    Bevel = 2,
}

/// <summary>Stroke style (line attributes).</summary>
public sealed record StrokeStyle(
    double LineWidth,
    LineCap Cap,
    LineJoin Join,
    double MiterLimit,
    double[]? DashArray,
    double DashPhase);

/// <summary>Whether a path is filled, stroked, or both.</summary>
public enum PaintMode
{
    /// <summary>Fill only.</summary>
    Fill = 0,
    /// <summary>Stroke only.</summary>
    Stroke = 1,
    /// <summary>Fill then stroke.</summary>
    FillAndStroke = 2,
}

/// <summary>Renders a path with fill and/or stroke.</summary>
public sealed class PathOp : RenderOp
{
    /// <inheritdoc />
    public override RenderOpKind Kind => RenderOpKind.Path;

    /// <summary>The path geometry to render.</summary>
    public required PathGeometry Geometry { get; init; }

    /// <summary>Paint mode (fill / stroke / both).</summary>
    public required PaintMode Mode { get; init; }

    /// <summary>Fill rule.</summary>
    public FillRule FillRule { get; init; } = FillRule.NonZero;

    /// <summary>Fill color (only meaningful when Mode includes fill).</summary>
    public PdfColor FillColor { get; init; }

    /// <summary>Stroke color (only meaningful when Mode includes stroke).</summary>
    public PdfColor StrokeColor { get; init; }

    /// <summary>Stroke style (only meaningful when Mode includes stroke).</summary>
    public StrokeStyle? Stroke { get; init; }
}

/// <summary>Rendering mode for a <see cref="TextOp"/> (PDF §9.3.6).</summary>
public enum TextRenderingMode
{
    /// <summary>Fill glyphs.</summary>
    Fill = 0,
    /// <summary>Stroke glyphs.</summary>
    Stroke = 1,
    /// <summary>Fill then stroke glyphs.</summary>
    FillThenStroke = 2,
    /// <summary>Invisible (just advances the text cursor).</summary>
    Invisible = 3,
    /// <summary>Fill and add to clip path.</summary>
    FillAndClip = 4,
    /// <summary>Stroke and add to clip path.</summary>
    StrokeAndClip = 5,
    /// <summary>Fill, stroke, and add to clip path.</summary>
    FillStrokeAndClip = 6,
    /// <summary>Add to clip path only.</summary>
    Clip = 7,
}

/// <summary>A single positioned glyph within a <see cref="TextOp"/>.</summary>
public readonly record struct DisplayListGlyph(
    int GlyphId,
    string Unicode,
    double X,
    double Y,
    double Advance);

/// <summary>Renders a positioned glyph run.</summary>
public sealed class TextOp : RenderOp
{
    /// <inheritdoc />
    public override RenderOpKind Kind => RenderOpKind.Text;

    /// <summary>Font resource name as declared in /Resources/Font.</summary>
    public required string FontKey { get; init; }

    /// <summary>Base font name (e.g. "Helvetica", "Times-Roman", or a subset like "ABCDEF+MyFont").</summary>
    public required string BaseFont { get; init; }

    /// <summary>Font size in user space.</summary>
    public required double FontSize { get; init; }

    /// <summary>Per-glyph positions and Unicode mappings.</summary>
    public required IReadOnlyList<DisplayListGlyph> Glyphs { get; init; }

    /// <summary>Combined CTM × text matrix for the glyph origins.</summary>
    public required AffineMatrix Transform { get; init; }

    /// <summary>Text rendering mode (PDF §9.3.6).</summary>
    public TextRenderingMode RenderingMode { get; init; } = TextRenderingMode.Fill;

    /// <summary>Fill color (when mode includes fill).</summary>
    public PdfColor FillColor { get; init; }

    /// <summary>Stroke color (when mode includes stroke).</summary>
    public PdfColor StrokeColor { get; init; }

    /// <summary>Resolved presentation style (family, weight, slant).</summary>
    public FontStyle Style { get; init; } = FontStyle.Default;
}

/// <summary>Raster format of image pixel data.</summary>
public enum ImageFormat
{
    /// <summary>Raw raw byte buffer in the declared color space.</summary>
    Raw = 0,
    /// <summary>JPEG-encoded (DCT). Pass through unchanged where possible.</summary>
    Jpeg = 1,
    /// <summary>PNG-encoded (FlateDecode-based).</summary>
    Png = 2,
}

/// <summary>Renders a raster image.</summary>
public sealed class ImageOp : RenderOp
{
    /// <inheritdoc />
    public override RenderOpKind Kind => RenderOpKind.Image;

    /// <summary>Pixel data (interpretation depends on <see cref="Format"/> and <see cref="ColorSpace"/>).</summary>
    public required byte[] PixelData { get; init; }

    /// <summary>Encoding format of <see cref="PixelData"/>.</summary>
    public required ImageFormat Format { get; init; }

    /// <summary>Pixel width.</summary>
    public required int Width { get; init; }

    /// <summary>Pixel height.</summary>
    public required int Height { get; init; }

    /// <summary>Bits per component (typically 8).</summary>
    public int BitsPerComponent { get; init; } = 8;

    /// <summary>Color space of raw pixel data.</summary>
    public PdfColorSpace ColorSpace { get; init; } = PdfColorSpace.DeviceRgb;

    /// <summary>
    /// Optional soft-mask alpha (one 8-bit sample per pixel) from the image's
    /// <c>/SMask</c>. When present, it is applied as the image's alpha channel.
    /// </summary>
    public byte[]? SoftMaskAlpha { get; init; }

    /// <summary>Width of <see cref="SoftMaskAlpha"/> in samples.</summary>
    public int SoftMaskWidth { get; init; }

    /// <summary>Height of <see cref="SoftMaskAlpha"/> in samples.</summary>
    public int SoftMaskHeight { get; init; }

    /// <summary>
    /// Transformation matrix placing the image. The unit-square at (0,0)-(1,1)
    /// is mapped to the image's destination rectangle.
    /// </summary>
    public required AffineMatrix Transform { get; init; }
}

/// <summary>Pushes a clipping region.</summary>
public sealed class ClipOp : RenderOp
{
    /// <inheritdoc />
    public override RenderOpKind Kind => RenderOpKind.Clip;

    /// <summary>The clipping path.</summary>
    public required PathGeometry Geometry { get; init; }

    /// <summary>Fill rule for the clip region.</summary>
    public FillRule FillRule { get; init; } = FillRule.NonZero;
}

/// <summary>Pushes or pops a graphics-state transformation matrix.</summary>
public sealed class TransformOp : RenderOp
{
    /// <inheritdoc />
    public override RenderOpKind Kind => RenderOpKind.Transform;

    /// <summary>True for push (q + cm), false for pop (Q).</summary>
    public required bool Push { get; init; }

    /// <summary>The cumulative CTM after this op (for renderers that don't track state).</summary>
    public AffineMatrix Ctm { get; init; } = AffineMatrix.Identity;
}

/// <summary>Pushes or pops an opacity group.</summary>
public sealed class OpacityOp : RenderOp
{
    /// <inheritdoc />
    public override RenderOpKind Kind => RenderOpKind.Opacity;

    /// <summary>True for push, false for pop.</summary>
    public required bool Push { get; init; }

    /// <summary>Constant alpha [0, 1] (only meaningful on push).</summary>
    public double Alpha { get; init; } = 1.0;

    /// <summary>Whether the group is isolated (PDF transparency group).</summary>
    public bool Isolated { get; init; }
}

/// <summary>PDF blend modes (§11.3.5).</summary>
public enum PdfBlendMode
{
    /// <summary>Normal (default).</summary>
    Normal = 0,
    /// <summary>Multiply.</summary>
    Multiply = 1,
    /// <summary>Screen.</summary>
    Screen = 2,
    /// <summary>Overlay.</summary>
    Overlay = 3,
    /// <summary>Darken.</summary>
    Darken = 4,
    /// <summary>Lighten.</summary>
    Lighten = 5,
    /// <summary>Color dodge.</summary>
    ColorDodge = 6,
    /// <summary>Color burn.</summary>
    ColorBurn = 7,
    /// <summary>Hard light.</summary>
    HardLight = 8,
    /// <summary>Soft light.</summary>
    SoftLight = 9,
    /// <summary>Difference.</summary>
    Difference = 10,
    /// <summary>Exclusion.</summary>
    Exclusion = 11,
}

/// <summary>Pushes or pops a blend mode.</summary>
public sealed class BlendModeOp : RenderOp
{
    /// <inheritdoc />
    public override RenderOpKind Kind => RenderOpKind.BlendMode;

    /// <summary>True for push, false for pop.</summary>
    public required bool Push { get; init; }

    /// <summary>Blend mode (only meaningful on push).</summary>
    public PdfBlendMode Mode { get; init; } = PdfBlendMode.Normal;
}
