// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2.1 — display-list intermediate

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>Abstract base for all display-list operations.</summary>
public abstract class RenderOp
{
    /// <summary>Discriminator for switch-pattern dispatch.</summary>
    public abstract RenderOpKind Kind { get; }

    /// <summary>Blend mode for compositing this op against the backdrop (PDF §11.3.5).</summary>
    public PdfBlendMode BlendMode { get; init; } = PdfBlendMode.Normal;

    /// <summary>Active soft mask (ExtGState /SMask) gating this op, or null.</summary>
    public SoftMaskInfo? SoftMask { get; init; }

    /// <summary>
    /// The annotation this op was drawn from (3.31), or null for the page's own
    /// content. Lets a printer filter or group annotation content.
    /// </summary>
    public AnnotationSource? Annotation { get; internal set; }

    /// <summary>
    /// The ids of the transparency groups this op is drawn inside, outermost
    /// first (3.31, D-173), for example <c>[7, 9]</c>. Empty when the op is in
    /// no group; never null. Each id is a <see cref="TransparencyGroup.Id"/> in
    /// <see cref="PageDisplayList.TransparencyGroups"/>.
    /// </summary>
    public IReadOnlyList<int> GroupIds { get; internal set; } = Array.Empty<int>();

    /// <summary>
    /// For an op inside a transparency group (3.31, D-173): its own opacity,
    /// blend mode and soft mask within the innermost group, without the
    /// values the group itself is composited with (those are on the
    /// <see cref="TransparencyGroup"/>). Null for an op in no group. The op's
    /// <see cref="BlendMode"/>, <see cref="SoftMask"/> and opacity properties
    /// keep the values drawn by renderers that do not composite groups.
    /// </summary>
    public OpTransparency? GroupTransparency { get; internal set; }

    /// <summary>
    /// The optional-content-group (layer) names that this op belongs to, from
    /// the enclosing marked-content sequences (<c>/OC … BDC … EMC</c>), ordered
    /// outermost-first. Empty when the op is not inside any optional-content
    /// layer. Never null. PDF 32000-1:2008 §8.11.3.2.
    /// </summary>
    /// <remarks>
    /// Populated by <see cref="DisplayListBuilder"/> as it walks the content
    /// stream; consumers see it as read-only. A drawing op nested inside
    /// <c>/OC /MC0 BDC</c> whose property resolves to an OCG named
    /// <c>"PDF9_AR-Wall"</c> carries that name here, enabling layer-aware
    /// extraction without re-walking the page.
    /// </remarks>
    public IReadOnlyList<string> Layers { get; internal set; } = Array.Empty<string>();

    /// <summary>
    /// The clipping paths in effect when this op was emitted, ordered
    /// outermost-first (draw order), each already in page space (the CTM is
    /// applied, matching <see cref="PathOp.Geometry"/> and
    /// <see cref="ClipOp.Geometry"/>). The effective clip region is the
    /// intersection of all paths in the list. Empty when the op is not clipped.
    /// Never null. PDF 32000-1:2008 §8.5.4.
    /// </summary>
    /// <remarks>
    /// Populated by <see cref="DisplayListBuilder"/> as it walks the content
    /// stream, snapshotting the active clip stack (saved and restored by
    /// <c>q</c>/<c>Q</c>) onto each op. Lets a consumer determine the region a
    /// glyph, path, or image is confined to — e.g. which table cell or detail
    /// view bounds it — without re-walking the clip stack.
    /// </remarks>
    public IReadOnlyList<PathGeometry> Clips { get; internal set; } = Array.Empty<PathGeometry>();
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
    /// <summary><see cref="ShadingOp"/>.</summary>
    Shading = 7,
    /// <summary><see cref="Type3UseOp"/>.</summary>
    Type3Glyph = 8,
    /// <summary><see cref="TilingPatternOp"/>.</summary>
    TilingPattern = 9,
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

    /// <summary>
    /// The path geometry in user space, exactly as authored in the content
    /// stream before the current transformation matrix was applied, or null
    /// when raw geometry was not retained. <see cref="Ctm"/> maps this onto
    /// <see cref="Geometry"/>: applying the CTM to each raw point yields the
    /// corresponding page-space point. Enables extraction at true scale and
    /// recovery of the original authored coordinates without inverting the CTM.
    /// </summary>
    public PathGeometry? RawGeometry { get; init; }

    /// <summary>
    /// The current transformation matrix in effect when this path was painted,
    /// mapping <see cref="RawGeometry"/> (user space) onto <see cref="Geometry"/>
    /// (page space). <see cref="AffineMatrix.Identity"/> when no raw geometry
    /// was retained. PDF 32000-1:2008 §8.3.4.
    /// </summary>
    public AffineMatrix Ctm { get; init; } = AffineMatrix.Identity;

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

    /// <summary>
    /// Constant fill opacity (ExtGState /ca), 0..1. Default 1 (opaque).
    /// </summary>
    public double FillOpacity { get; init; } = 1.0;

    /// <summary>
    /// Constant stroke opacity (ExtGState /CA), 0..1. Default 1 (opaque).
    /// </summary>
    public double StrokeOpacity { get; init; } = 1.0;
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


/// <summary>The annotation an op was drawn from (3.31).</summary>
/// <param name="Dictionary">The annotation dictionary.</param>
/// <param name="Subtype">Its <c>/Subtype</c>, for example "Widget", "Highlight" or "Stamp".</param>
/// <param name="Flags">Its <c>/F</c> flags.</param>
/// <param name="FieldName">For a widget, the field's full name (parts joined with "."); otherwise null.</param>
/// <param name="Generated">True when the appearance was made from the annotation's entries rather than taken from its <c>/AP</c>.</param>
public sealed record AnnotationSource(PdfDictionary Dictionary, string Subtype, int Flags, string? FieldName, bool Generated);

/// <summary>A single positioned glyph within a <see cref="TextOp"/>.</summary>
/// <param name="GlyphId">The glyph's character code in the font (kept under this name for compatibility).</param>
/// <param name="Unicode">The Unicode text the glyph stands for.</param>
/// <param name="X">The glyph's x position in text space, from the start of the run.</param>
/// <param name="Y">The glyph's y position in text space.</param>
/// <param name="Advance">The glyph's advance in user space.</param>
public readonly record struct DisplayListGlyph(
    int GlyphId,
    string Unicode,
    double X,
    double Y,
    double Advance)
{
    /// <summary>
    /// The character code from the content stream, as a big-endian number of
    /// <see cref="CodeLength"/> bytes (3.31). Use with <see cref="TextOp.Font"/>
    /// to re-encode the text in an embedded font.
    /// </summary>
    public int Code { get; init; }

    /// <summary>
    /// The number of bytes of <see cref="Code"/> (1 for simple fonts, 2 for
    /// Type 0 fonts); 0 for a space Chuvadi inserts between runs for text
    /// extraction, which has no code in the content stream (3.31).
    /// </summary>
    public int CodeLength { get; init; }
}

/// <summary>Renders a positioned glyph run.</summary>
public sealed class TextOp : RenderOp
{
    /// <inheritdoc />
    public override RenderOpKind Kind => RenderOpKind.Text;


    /// <summary>Font resource name as declared in /Resources/Font.</summary>
    public required string FontKey { get; init; }

    /// <summary>Base font name (e.g. "Helvetica", "Times-Roman", or a subset like "ABCDEF+MyFont").</summary>
    public required string BaseFont { get; init; }

    /// <summary>
    /// The font, resolved through the same resources as the text (3.31): the
    /// program, its kind and the substitute for a font that is not embedded.
    /// One instance per font dictionary on the page. Null when the font
    /// resource cannot be found.
    /// </summary>
    public DisplayListFont? Font { get; init; }

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

    /// <summary>
    /// Constant fill opacity (ExtGState /ca), 0..1. Default 1 (opaque).
    /// </summary>
    public double FillOpacity { get; init; } = 1.0;

    /// <summary>
    /// Constant stroke opacity (ExtGState /CA), 0..1. Default 1 (opaque).
    /// </summary>
    public double StrokeOpacity { get; init; } = 1.0;
}

/// <summary>The colour transform of a JPEG image (its Adobe APP14 marker, if any).</summary>
public enum JpegColorTransform
{
    /// <summary>No transform recorded, or not a JPEG image.</summary>
    None = 0,

    /// <summary>YCbCr (APP14 transform 1), or the JFIF default for 3 components.</summary>
    YCbCr = 1,

    /// <summary>YCCK (APP14 transform 2): a CMYK JPEG stored as YCCK.</summary>
    Ycck = 2,
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

    /// <summary>
    /// For a palette (<c>/Indexed</c>) image whose samples were expanded to
    /// colour in <see cref="PixelData"/>: the original palette indices, one
    /// byte per pixel, row by row; otherwise null.
    /// </summary>
    public byte[]? IndexSamples { get; init; }

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
    /// Constant image opacity (ExtGState /ca), 0..1. Default 1 (opaque).
    /// Composes with <see cref="SoftMaskAlpha"/> when both are present.
    /// </summary>
    public double Alpha { get; init; } = 1.0;

    /// <summary>
    /// Transformation matrix placing the image. The unit-square at (0,0)-(1,1)
    /// is mapped to the image's destination rectangle.
    /// </summary>
    public required AffineMatrix Transform { get; init; }

    /// <summary>The image XObject this op was drawn from (3.31), or null for an inline image.</summary>
    public PdfStream? Source { get; init; }

    /// <summary>The image's <c>/Decode</c> array (3.31), or null when absent.</summary>
    public double[]? Decode { get; init; }

    /// <summary>
    /// True for a stencil mask (<c>/ImageMask true</c>, §8.9.6.2): the 1-bit
    /// samples paint <see cref="StencilColor"/> (3.31). <see cref="PixelData"/>
    /// then holds that colour with the mask as <see cref="SoftMaskAlpha"/>, and
    /// <see cref="Source"/> the original 1-bit samples.
    /// </summary>
    public bool IsStencilMask { get; init; }

    /// <summary>For a stencil mask, the fill colour it paints (3.31).</summary>
    public PdfColor StencilColor { get; init; }

    /// <summary>For a JPEG image, its colour transform from the Adobe APP14 marker (3.31).</summary>
    public JpegColorTransform JpegColorTransform { get; init; }

    /// <summary>For a JPEG image, its number of colour components from the frame header (3.31); 0 otherwise.</summary>
    public int JpegComponents { get; init; }

    /// <summary>True for a CMYK or YCCK JPEG (four components, 3.31).</summary>
    public bool IsCmykJpeg => Format == ImageFormat.Jpeg && JpegComponents == 4;

    /// <summary>The ICC profile stream of an <c>/ICCBased</c> colour space (3.31), or null.</summary>
    public PdfStream? IccProfileStream { get; init; }

    /// <summary>Returns the decoded ICC profile bytes, or null when there is none or it cannot be decoded (3.31).</summary>
    /// <returns>The profile, or null.</returns>
    public byte[]? GetIccProfile()
    {
        if (IccProfileStream is null)
        {
            return null;
        }

        try
        {
            return Chuvadi.Pdf.Rendering.Walking.ContentStreamLoader.Decode(IccProfileStream);
        }
        catch (Exception ex) when (!PdfWorkScope.IsAbort(ex))
        {
            return null;
        }
    }
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

    /// <summary>
    /// When set, the clip region is the area a stroke of
    /// <see cref="Geometry"/> in this style would paint (its outline), not
    /// the path's interior: content after the clip shows only where the
    /// stroke would. Used for a stroke whose colour is a pattern.
    /// <see cref="FillRule"/> does not apply.
    /// </summary>
    public StrokeStyle? Stroke { get; init; }

    /// <summary>
    /// How far a stroke in <paramref name="style"/> can reach beyond its path
    /// (page units): half the line width, scaled by the miter limit (or the
    /// diagonal of a square cap, whichever is larger).
    /// </summary>
    /// <param name="style">The stroke style.</param>
    /// <returns>The reach.</returns>
    public static double StrokeReach(StrokeStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        return Math.Abs(style.LineWidth) / 2 * Math.Max(Math.Sqrt(2), style.MiterLimit);
    }
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

/// <summary>A single colour stop of a shading gradient.</summary>
public readonly struct ShadingStop
{
    /// <summary>Creates a gradient stop.</summary>
    /// <param name="offset">Normalised position along the axis in [0, 1].</param>
    /// <param name="r">Red in [0, 1].</param>
    /// <param name="g">Green in [0, 1].</param>
    /// <param name="b">Blue in [0, 1].</param>
    public ShadingStop(double offset, double r, double g, double b)
    {
        Offset = offset;
        R = r;
        G = g;
        B = b;
    }

    /// <summary>Normalised position along the axis in [0, 1].</summary>
    public double Offset { get; }

    /// <summary>Red in [0, 1].</summary>
    public double R { get; }

    /// <summary>Green in [0, 1].</summary>
    public double G { get; }

    /// <summary>Blue in [0, 1].</summary>
    public double B { get; }
}

/// <summary>
/// Paints an axial or radial shading (the <c>sh</c> operator). Coordinates and
/// radii are in page space (the CTM at the <c>sh</c> operator has already been
/// applied); the renderer fills the current clip region with the gradient.
/// </summary>
public sealed class ShadingOp : RenderOp
{
    /// <inheritdoc />
    public override RenderOpKind Kind => RenderOpKind.Shading;


    /// <summary>True for a radial shading; false for axial (linear).</summary>
    public required bool IsRadial { get; init; }

    /// <summary>Start point x in page space (axial/radial circle 0 centre).</summary>
    public double X0 { get; init; }

    /// <summary>Start point y in page space.</summary>
    public double Y0 { get; init; }

    /// <summary>End point x in page space (axial/radial circle 1 centre).</summary>
    public double X1 { get; init; }

    /// <summary>End point y in page space.</summary>
    public double Y1 { get; init; }

    /// <summary>Radius of circle 0 in page space (radial only).</summary>
    public double R0 { get; init; }

    /// <summary>Radius of circle 1 in page space (radial only).</summary>
    public double R1 { get; init; }

    /// <summary>Whether the shading extends beyond the start of the axis.</summary>
    public bool ExtendStart { get; init; }

    /// <summary>Whether the shading extends beyond the end of the axis.</summary>
    public bool ExtendEnd { get; init; }

    /// <summary>The sampled gradient stops, in increasing offset order.</summary>
    public IReadOnlyList<ShadingStop> Stops { get; init; } = System.Array.Empty<ShadingStop>();

    /// <summary>
    /// When set, the centres and radii are in shading space and this maps
    /// shading space to page space. Used for a radial shading under a
    /// transform that does not keep circles circular (unequal scaling or
    /// skew), where the circles become ellipses on the page. Null when the
    /// geometry is already in page space.
    /// </summary>
    public AffineMatrix? ShadingToPage { get; init; }

    /// <summary>The <c>/ShadingType</c>, 1 to 7 (3.31); 2 (axial) or 3 (radial) for the ops the list draws.</summary>
    public int ShadingType { get; init; } = 2;

    /// <summary>
    /// The shading dictionary, or for mesh types 4 to 7 the shading stream
    /// (3.31), so a printer can pass it on as it is (PostScript Level 3 and
    /// PCL accept all seven types). Null for ops built without one.
    /// </summary>
    public PdfPrimitive? Shading { get; init; }

    /// <summary>
    /// False for a shading the structured list cannot express as stops
    /// (function-based type 1, meshes 4 to 7, 3.31): only
    /// <see cref="ShadingType"/>, <see cref="Shading"/> and
    /// <see cref="ShadingToPage"/> are set, and SVG and WPF skip the op.
    /// </summary>
    public bool IsExpressible { get; init; } = true;

    /// <summary>For a mesh shading (types 4 to 7), its decoded stream data (3.31); otherwise null.</summary>
    /// <returns>The data, or null.</returns>
    public byte[]? GetShadingData()
    {
        if (Shading is not PdfStream stream)
        {
            return null;
        }

        try
        {
            return Chuvadi.Pdf.Rendering.Walking.ContentStreamLoader.Decode(stream);
        }
        catch (Exception ex) when (!PdfWorkScope.IsAbort(ex))
        {
            return null;
        }
    }
}

/// <summary>
/// Paints one Type 3 glyph: a cached glyph-space sub-display-list positioned by a
/// composition transform (FontMatrix · text-scale · text matrix · CTM). The
/// renderer wraps the sub-list in a transform group so a repeated glyph reuses
/// the same cached ops. Blend mode and soft mask ride on this op (not baked into
/// the cached sub-list), so the cache is colour-keyed only.
/// </summary>
public sealed class Type3UseOp : RenderOp
{
    /// <inheritdoc />
    public override RenderOpKind Kind => RenderOpKind.Type3Glyph;

    /// <summary>Initialises a <see cref="Type3UseOp"/>.</summary>
    /// <param name="glyph">The glyph's sub-display-list, in glyph space.</param>
    /// <param name="composition">Glyph space → page space transform.</param>
    public Type3UseOp(PageDisplayList glyph, AffineMatrix composition)
    {
        Glyph = glyph;
        Composition = composition;
    }

    /// <summary>The glyph's sub-display-list, in glyph space.</summary>
    public PageDisplayList Glyph { get; }

    /// <summary>Glyph space → page space transform for this occurrence.</summary>
    public AffineMatrix Composition { get; }
}
