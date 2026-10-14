// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.5.3 (painting), §9.3.6 (text rendering mode)
// PHASE: Display list — content inventory (does a page paint anything?).

using System;
using Chuvadi.Pdf.Documents;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>
/// Counts what a page actually paints, from the same display list the SVG and
/// raster renderers draw — page content, nested form XObjects, Type 3 glyphs
/// and visible annotation appearances included. Clipping, invisible text
/// (render mode 3 and clip-only mode 7), fully transparent paint and empty
/// geometry are not counted, so <see cref="HasDrawableContent"/> is false for
/// a page that renders blank (for example the placeholder pages of a dynamic
/// XFA form whose real content needs composing).
/// </summary>
/// <remarks>
/// Content inside optional-content groups is counted whether or not the group
/// is currently visible. A white fill over white paper counts as drawn.
/// </remarks>
public sealed class PageContentInventory
{
    private PageContentInventory(int textRuns, int paths, int images, int shadings)
    {
        TextRuns = textRuns;
        Paths = paths;
        Images = images;
        Shadings = shadings;
    }

    /// <summary>Gets the number of visible text-show operations (with at least one glyph).</summary>
    public int TextRuns { get; }

    /// <summary>Gets the number of painted (filled and/or stroked) paths.</summary>
    public int Paths { get; }

    /// <summary>Gets the number of painted images (including stencil masks).</summary>
    public int Images { get; }

    /// <summary>Gets the number of painted shadings (gradients, shading patterns).</summary>
    public int Shadings { get; }

    /// <summary>Gets whether the page paints any visible text.</summary>
    public bool HasVisibleText => TextRuns > 0;

    /// <summary>Gets whether the page paints anything at all.</summary>
    public bool HasDrawableContent => TextRuns + Paths + Images + Shadings > 0;

    /// <summary>Builds the inventory for one page of a document.</summary>
    /// <param name="document">The document.</param>
    /// <param name="pageIndex">Zero-based page index.</param>
    /// <returns>The page's content inventory.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="document"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the page index is out of range.</exception>
    public static PageContentInventory Inspect(PdfDocument document, int pageIndex)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (pageIndex < 0 || pageIndex >= document.PageCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageIndex), pageIndex, "Page index is outside the document.");
        }

        return FromDisplayList(DisplayListBuilder.Build(document, pageIndex));
    }

    /// <summary>Builds the inventory from an already-built display list.</summary>
    /// <param name="list">The page display list.</param>
    /// <returns>The content inventory.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="list"/> is null.</exception>
    public static PageContentInventory FromDisplayList(PageDisplayList list)
    {
        ArgumentNullException.ThrowIfNull(list);

        int text = 0;
        int paths = 0;
        int images = 0;
        int shadings = 0;
        Count(list, ref text, ref paths, ref images, ref shadings, 0);
        return new PageContentInventory(text, paths, images, shadings);
    }

    private static void Count(
        PageDisplayList list, ref int text, ref int paths, ref int images, ref int shadings, int depth)
    {
        foreach (RenderOp op in list)
        {
            switch (op)
            {
                case TextOp t when IsVisibleText(t):
                    text++;
                    break;
                case PathOp p when IsVisiblePath(p):
                    paths++;
                    break;
                case ImageOp i when i.Alpha > 0 && i.Width > 0 && i.Height > 0:
                    images++;
                    break;
                case ShadingOp:
                    shadings++;
                    break;
                case TilingPatternOp tp when depth < 8 && tp.Tiles.Count > 0:
                    // A tiling pattern paints its cell's content (once per
                    // tile; counted once).
                    PageContentInventory cell = FromNested(tp.Cell, depth + 1);
                    text += cell.TextRuns;
                    paths += cell.Paths;
                    images += cell.Images;
                    shadings += cell.Shadings;
                    break;
                case Type3UseOp g when depth < 8:
                    // A Type 3 glyph is text drawn by a content stream: count it
                    // as text when its procedure paints anything.
                    PageContentInventory glyph = FromNested(g.Glyph, depth + 1);
                    if (glyph.HasDrawableContent)
                    {
                        text++;
                    }

                    break;
            }
        }
    }

    private static PageContentInventory FromNested(PageDisplayList list, int depth)
    {
        int text = 0;
        int paths = 0;
        int images = 0;
        int shadings = 0;
        Count(list, ref text, ref paths, ref images, ref shadings, depth);
        return new PageContentInventory(text, paths, images, shadings);
    }

    private static bool IsVisibleText(TextOp t)
    {
        if (t.Glyphs.Count == 0)
        {
            return false;
        }

        return t.RenderingMode switch
        {
            TextRenderingMode.Invisible or TextRenderingMode.Clip => false,
            TextRenderingMode.Stroke or TextRenderingMode.StrokeAndClip => t.StrokeOpacity > 0,
            TextRenderingMode.FillThenStroke or TextRenderingMode.FillStrokeAndClip =>
                t.FillOpacity > 0 || t.StrokeOpacity > 0,
            _ => t.FillOpacity > 0,
        };
    }

    private static bool IsVisiblePath(PathOp p)
    {
        if (p.Geometry.IsEmpty)
        {
            return false;
        }

        return p.Mode switch
        {
            PaintMode.Stroke => p.StrokeOpacity > 0,
            PaintMode.FillAndStroke => p.FillOpacity > 0 || p.StrokeOpacity > 0,
            _ => p.FillOpacity > 0,
        };
    }
}
