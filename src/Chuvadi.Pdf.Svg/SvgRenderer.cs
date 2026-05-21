// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  W3C SVG 1.1 §5 (root structure), §7 (coordinate systems)
// PHASE: v2.0.0 R2 — SVG renderer

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Rendering.DisplayList;

namespace Chuvadi.Pdf.Svg;

/// <summary>
/// Renders a PDF page to deterministic SVG markup.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SvgRenderer"/> consumes a <see cref="PageDisplayList"/> and
/// produces self-contained SVG 1.1 markup. The output is byte-for-byte
/// reproducible across runs and platforms: invariant-culture float
/// formatting, monotonic id allocation, no dictionary iteration in the
/// emit pass.
/// </para>
/// <para>
/// Coordinate system: the SVG <c>viewBox</c> is set to the PDF MediaBox
/// dimensions in user-space points. PDF Y-up coordinates are flipped to
/// SVG Y-down via a single page-level
/// <c>transform="matrix(1 0 0 -1 0 pageHeight)"</c> group wrapping all
/// painted content. Op-level geometry is emitted in PDF user space — the
/// outer flip handles the orientation swap once.
/// </para>
/// <para>
/// Construct with default options for a quick start; pass a configured
/// <see cref="SvgRenderOptions"/> for control over scale, font embedding,
/// precision, and so on.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// using PdfDocument doc = PdfDocument.Open("input.pdf");
/// string svg = new SvgRenderer().RenderPage(doc, pageIndex: 0);
/// File.WriteAllText("output.svg", svg);
/// </code>
/// </example>
public sealed class SvgRenderer
{
    private readonly SvgRenderOptions _options;

    /// <summary>
    /// Initialises a renderer with <see cref="SvgRenderOptions.Default"/>.
    /// </summary>
    public SvgRenderer()
        : this(SvgRenderOptions.Default)
    {
    }

    /// <summary>
    /// Initialises a renderer with the supplied options.
    /// </summary>
    /// <param name="options">The rendering options.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="options"/> is null.
    /// </exception>
    public SvgRenderer(SvgRenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.FontEmbedding == FontEmbedding.Woff2DataUri)
        {
            throw new NotSupportedException(
                "FontEmbedding.Woff2DataUri is reserved for a future release. " +
                "Use FontEmbedding.GlyphPaths in v2.0.0.");
        }

        _options = options;
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Renders page <paramref name="pageIndex"/> of <paramref name="document"/>
    /// to an SVG string.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="document"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="pageIndex"/> is outside
    /// <c>[0, document.PageCount)</c>.
    /// </exception>
    public string RenderPage(PdfDocument document, int pageIndex)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (pageIndex < 0 || pageIndex >= document.PageCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageIndex),
                pageIndex,
                $"Page index must be in [0, {document.PageCount}).");
        }

        PdfPage page = document.Pages[pageIndex];
        return RenderInternal(page, document.Objects, scaleOverride: null);
    }

    /// <summary>
    /// Renders <paramref name="page"/> to an SVG string, resolving indirect
    /// references through <paramref name="objects"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="page"/> or <paramref name="objects"/> is null.
    /// </exception>
    public string RenderPage(PdfPage page, PdfObjectStore objects)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(objects);

        return RenderInternal(page, objects, scaleOverride: null);
    }

    /// <summary>
    /// Renders page <paramref name="pageIndex"/> of <paramref name="document"/>
    /// to <paramref name="output"/> as UTF-8 SVG bytes.
    /// </summary>
    /// <param name="document">The source document.</param>
    /// <param name="pageIndex">Zero-based page index.</param>
    /// <param name="output">The writable destination stream.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="document"/> or <paramref name="output"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="pageIndex"/> is outside
    /// <c>[0, document.PageCount)</c>.
    /// </exception>
    public void RenderPage(PdfDocument document, int pageIndex, Stream output)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(output);

        string svg = RenderPage(document, pageIndex);
        byte[] bytes = Encoding.UTF8.GetBytes(svg);
        output.Write(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Renders page <paramref name="pageIndex"/> of <paramref name="document"/>
    /// scaled so that the longer side is at most <paramref name="maxDimension"/>
    /// SVG user units while preserving aspect ratio.
    /// </summary>
    /// <param name="document">The source document.</param>
    /// <param name="pageIndex">Zero-based page index.</param>
    /// <param name="maxDimension">
    /// The maximum width or height (in SVG user units) of the rendered
    /// thumbnail. Must be positive.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="document"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="pageIndex"/> is out of range or
    /// <paramref name="maxDimension"/> is not positive.
    /// </exception>
    public string RenderThumbnail(PdfDocument document, int pageIndex, int maxDimension)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (pageIndex < 0 || pageIndex >= document.PageCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageIndex),
                pageIndex,
                $"Page index must be in [0, {document.PageCount}).");
        }

        if (maxDimension <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxDimension),
                maxDimension,
                "maxDimension must be positive.");
        }

        PdfPage page = document.Pages[pageIndex];
        double longer = Math.Max(page.Width, page.Height);
        double scale = longer <= 0 ? 1.0 : maxDimension / longer;
        return RenderInternal(page, document.Objects, scaleOverride: scale);
    }

    // ── Internal renderer ─────────────────────────────────────────────────

    private string RenderInternal(PdfPage page, PdfObjectStore objects, double? scaleOverride)
    {
        PageDisplayList list = DisplayListBuilder.Build(page, objects);
        return RenderDisplayList(list, page.Width, page.Height, scaleOverride);
    }

    /// <summary>
    /// Renders an already-built display list to SVG. Lower-level entry
    /// point useful for tests and tooling that synthesise display lists in
    /// memory.
    /// </summary>
    /// <param name="list">The display list to paint.</param>
    /// <param name="pageWidth">The MediaBox width in user-space points.</param>
    /// <param name="pageHeight">The MediaBox height in user-space points.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="list"/> is null.
    /// </exception>
    public string RenderDisplayList(PageDisplayList list, double pageWidth, double pageHeight)
    {
        ArgumentNullException.ThrowIfNull(list);

        return RenderDisplayList(list, pageWidth, pageHeight, scaleOverride: null);
    }

    private string RenderDisplayList(
        PageDisplayList list,
        double pageWidth,
        double pageHeight,
        double? scaleOverride)
    {
        double scale = scaleOverride ?? _options.Scale;
        SvgWriter writer = new SvgWriter(_options.DecimalPrecision, _options.IndentOutput);

        // XML prologue — keep it minimal; SVG embedded in HTML usually
        // omits the declaration but a standalone .svg file should have
        // an explicit charset.
        writer.WriteRaw("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        writer.WriteLine();

        // Root <svg>.
        writer.OpenTag("svg");
        writer.AttrLiteral("xmlns", "http://www.w3.org/2000/svg");
        writer.AttrLiteral("xmlns:xlink", "http://www.w3.org/1999/xlink");

        // viewBox always uses unscaled PDF points; width/height carry the scale.
        writer.AttrDouble("width", pageWidth * scale);
        writer.AttrDouble("height", pageHeight * scale);

        StringBuilder vb = new StringBuilder(32);
        writer.AppendPathNumber(vb, 0, false);
        writer.AppendPathNumber(vb, 0, true);
        writer.AppendPathNumber(vb, pageWidth, true);
        writer.AppendPathNumber(vb, pageHeight, true);
        writer.AttrLiteral("viewBox", vb.ToString());

        writer.CloseStartTag();

        // Build the emitter, run discover + register + defs.
        OpEmitter emitter = new OpEmitter(writer, _options);
        emitter.Discover(list);
        emitter.RegisterClips(list);
        IReadOnlyList<GlyphCache.DefsEntry> glyphDefs = emitter.AllocateGlyphIds();
        emitter.WriteDefs(glyphDefs);

        // Page-level Y-flip: matrix(1 0 0 -1 0 pageHeight) maps PDF user
        // space to SVG user space. PDF (x, y) becomes SVG (x, pageHeight - y).
        StringBuilder flip = new StringBuilder(32);
        flip.Append("matrix(1 0 0 -1 0 ");
        writer.AppendPathNumber(flip, pageHeight, false);
        flip.Append(')');

        writer.OpenTag("g");
        writer.AttrLiteral("transform", flip.ToString());
        writer.CloseStartTag();

        emitter.Emit(list);

        writer.CloseTag("g");
        writer.CloseTag("svg");

        return writer.ToSvgString();
    }
}
