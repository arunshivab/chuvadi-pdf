// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.8.2 (content streams), §12.3.3 (outlines),
//        §11.6.4.4 (constant alpha / ExtGState). LA-24.
// One writer accumulates text stamps, page numbers, watermarks, headers and
// footers, and an outline, then emits the whole document in a single write.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// Accumulates several overlay operations — text stamps, page numbers, text
/// watermarks, headers and footers — plus an optional outline, and applies them
/// all in a single write. This avoids the repeated full-document serialization
/// that results from chaining the standalone stamp operations, each of which
/// writes the document on its own. Add steps in any order; call
/// <see cref="Write(System.IO.Stream, Chuvadi.Pdf.IO.EncryptionOptions?)"/> once to emit the result.
/// </summary>
public sealed class StampPipeline
{
    private readonly PdfDocument _document;
    private readonly StampWriter _writer;
    private readonly MarkTextComposer _composer;
    private int _markCounter;
    private int _extGStateCounter;

    /// <summary>Creates a pipeline that overlays content onto <paramref name="document"/>.</summary>
    /// <param name="document">The document to stamp.</param>
    /// <param name="fontData">
    /// (Since 3.36.0, D-412) Bytes of a TrueType/OpenType font to use as the
    /// primary face for every mark this pipeline draws (shaped and subset-embedded),
    /// or null to use the LiPi Sans face for each script with a Noto Emoji fallback.
    /// A header/footer added with its own <see cref="HeaderFooterOptions.FontData"/>
    /// overrides this for that band.
    /// </param>
    public StampPipeline(PdfDocument document, byte[]? fontData = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        _document = document;
        _writer = new StampWriter(document);
        _composer = new MarkTextComposer(fontData);
    }

    /// <summary>Adds an anchored text stamp whose template may contain tokens such as <c>{page}</c>.</summary>
    /// <param name="template">The text template; supports stamp tokens.</param>
    /// <param name="anchor">Where on the page the text sits.</param>
    /// <param name="fontSize">Font size in points.</param>
    /// <param name="color">Text color.</param>
    /// <param name="marginX">Horizontal inset from the page edge, in points.</param>
    /// <param name="marginY">Vertical inset from the page edge, in points.</param>
    /// <param name="pages">The zero-based page indices to stamp, or null for all pages.</param>
    /// <param name="filePath">Source path for filename tokens, or null.</param>
    /// <param name="timestamp">Timestamp for date/time tokens, or null.</param>
    /// <returns>This pipeline, for chaining.</returns>
    public StampPipeline AddTextStamp(string template, StampAnchor anchor, double fontSize, ColorF color, double marginX = 24, double marginY = 24, IEnumerable<int>? pages = null, string? filePath = null, DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        return AddAnchoredText(template, anchor, fontSize, color, marginX, marginY, pages, null, filePath, timestamp);
    }

    /// <summary>Adds page numbers formatted by <paramref name="numbering"/>.</summary>
    /// <param name="numbering">The numbering scheme (start value, padding, format, first-page mode).</param>
    /// <param name="anchor">Where on the page the number sits.</param>
    /// <param name="fontSize">Font size in points.</param>
    /// <param name="color">Text color.</param>
    /// <param name="marginX">Horizontal inset from the page edge, in points.</param>
    /// <param name="marginY">Vertical inset from the page edge, in points.</param>
    /// <param name="template">The template wrapping the number; <c>{number}</c> is the formatted value.</param>
    /// <returns>This pipeline, for chaining.</returns>
    public StampPipeline AddPageNumbers(StampNumbering numbering, StampAnchor anchor, double fontSize, ColorF color, double marginX = 24, double marginY = 24, string template = "{number}")
    {
        ArgumentNullException.ThrowIfNull(numbering);
        ArgumentNullException.ThrowIfNull(template);
        return AddAnchoredText(template, anchor, fontSize, color, marginX, marginY, null, numbering, null, null);
    }

    /// <summary>Adds a rotated, semi-transparent text watermark centered on each page.</summary>
    /// <param name="text">The watermark text.</param>
    /// <param name="fontSize">Font size in points.</param>
    /// <param name="color">Text color.</param>
    /// <param name="opacity">Constant alpha in the range 0 (transparent) to 1 (opaque).</param>
    /// <param name="rotationDegrees">Counter-clockwise rotation in degrees.</param>
    /// <param name="pages">The zero-based page indices to mark, or null for all pages.</param>
    /// <returns>This pipeline, for chaining.</returns>
    public StampPipeline AddTextWatermark(string text, double fontSize, ColorF color, double opacity = 0.12, double rotationDegrees = 45, IEnumerable<int>? pages = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return this;
        }

        string gsName = "CvWmGs" + _extGStateCounter++;
        double alpha = Math.Clamp(opacity, 0, 1);
        PdfDictionary gs = new PdfDictionary();
        gs.Set(PdfName.Type, PdfName.Intern("ExtGState"));
        gs.Set(PdfName.Intern("ca"), new PdfReal(alpha));
        gs.Set(PdfName.Intern("CA"), new PdfReal(alpha));
        _writer.RegisterExtGState(gsName, gs);

        double radians = rotationDegrees * Math.PI / 180.0;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);
        double width = _composer.Measure(text, fontSize);

        HashSet<int>? targets = pages is null ? null : new HashSet<int>(pages);
        int total = _document.PageCount;
        for (int i = 0; i < total; i++)
        {
            if (targets is not null && !targets.Contains(i))
            {
                continue;
            }

            PdfPage page = _document.Pages[i];
            double cx = page.MediaBox.X1 + (page.MediaBox.Width / 2.0);
            double cy = page.MediaBox.Y1 + (page.MediaBox.Height / 2.0);
            double originX = cx - (width / 2.0 * cos);
            double originY = cy - (width / 2.0 * sin);

            Transform placement = new Transform(cos, sin, -sin, cos, originX, originY);
            EmitMark(_composer, i, text, placement, fontSize, color, gsName);
        }

        return this;
    }

    /// <summary>Adds a header and/or footer as anchored text segments (overlay only, no page reflow).</summary>
    /// <param name="options">The header/footer band text and layout.</param>
    /// <returns>This pipeline, for chaining.</returns>
    public StampPipeline AddHeaderFooter(HeaderFooterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // A header/footer may carry its own font (HeaderFooterOptions.FontData),
        // which overrides the pipeline's default face for its bands.
        MarkTextComposer composer = options.FontData is null
            ? _composer
            : new MarkTextComposer(options.FontData);

        if (options.Header is not null)
        {
            AddBand(composer, options.Header, options, top: true);
        }

        if (options.Footer is not null)
        {
            AddBand(composer, options.Footer, options, top: false);
        }

        return this;
    }

    /// <summary>Adds a document outline (bookmarks) that is folded into the single write.</summary>
    /// <param name="entries">The top-level outline entries; each may carry children.</param>
    /// <returns>This pipeline, for chaining.</returns>
    public StampPipeline AddOutline(IReadOnlyList<OutlineEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
        {
            return this;
        }

        Dictionary<int, PdfObjectId> pageIds = PageTree.BuildIndexToIdMap(_document);
        PdfObjectId rootId = _writer.AllocateId();

        List<OutlineNode> nodes = new List<OutlineNode>();
        AssignOutlineIds(entries, nodes);
        int topVisible = WriteOutlineLevel(nodes, rootId, pageIds);

        PdfDictionary outlines = new PdfDictionary();
        outlines.Set(PdfName.Type, PdfName.Outlines);
        if (nodes.Count > 0)
        {
            outlines.Set(PdfName.Intern("First"), new PdfReference(nodes[0].Id));
            outlines.Set(PdfName.Intern("Last"), new PdfReference(nodes[^1].Id));
        }

        outlines.Set(PdfName.Count, topVisible);
        _writer.AddIndirectObject(rootId, outlines);
        _writer.SetOutlineRoot(rootId);
        return this;
    }

    /// <summary>Writes the stamped document to <paramref name="output"/> in a single pass.</summary>
    /// <param name="output">The stream to write to.</param>
    /// <param name="encryption">The encryption options, or null for no encryption.</param>
    public void Write(Stream output, EncryptionOptions? encryption = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        _writer.Write(output, encryption);
    }

    private StampPipeline AddAnchoredText(string template, StampAnchor anchor, double fontSize, ColorF color, double marginX, double marginY, IEnumerable<int>? pages, StampNumbering? numbering, string? filePath, DateTimeOffset? timestamp)
    {
        return AddAnchoredText(_composer, template, anchor, fontSize, color, marginX, marginY, pages, numbering, filePath, timestamp);
    }

    private StampPipeline AddAnchoredText(MarkTextComposer composer, string template, StampAnchor anchor, double fontSize, ColorF color, double marginX, double marginY, IEnumerable<int>? pages, StampNumbering? numbering, string? filePath, DateTimeOffset? timestamp)
    {
        HashSet<int>? targets = pages is null ? null : new HashSet<int>(pages);
        int total = _document.PageCount;
        for (int i = 0; i < total; i++)
        {
            if (targets is not null && !targets.Contains(i))
            {
                continue;
            }

            string? number = null;
            if (numbering is not null)
            {
                int? value = numbering.ResolveValue(i);
                if (value is null)
                {
                    continue;
                }

                number = numbering.Format(value.Value);
            }

            StampContext context = numbering is null
                ? new StampContext(i + 1, total, filePath, timestamp)
                : new StampContext(i + 1, total, filePath, timestamp, number);
            string text = StampTokens.Resolve(template, context);
            if (text.Length == 0)
            {
                continue;
            }

            PdfPage page = _document.Pages[i];
            double textWidth = composer.Measure(text, fontSize);
            Transform placement = AnchorPlacement.ComputePlacement(anchor, page.MediaBox, textWidth, fontSize, marginX, marginY);
            EmitMark(composer, i, text, placement, fontSize, color, null);
        }

        return this;
    }

    // 3.36.0 (D-412): draws one line of mark text on a page through the given
    // composer — shaped, with LiPi + emoji faces subset-embedded — placed and
    // rotated by <paramref name="placement"/> used as a CTM, optionally under an
    // ExtGState (for watermark opacity). Replaces the Helvetica BuildShowText path.
    private void EmitMark(MarkTextComposer composer, int pageIndex, string text, Transform placement, double fontSize, ColorF color, string? gsName)
    {
        string prefix = "CvM" + _markCounter + "F";
        _markCounter++;
        ComposedMarkText composed = composer.Compose(text, fontSize, color, _writer.AllocateId, prefix);
        foreach (PdfIndirectObject obj in composed.FontObjects)
        {
            _writer.AddIndirectObject(obj.Id, obj.Value);
        }

        foreach (KeyValuePair<string, PdfObjectId> font in composed.FontResources)
        {
            _writer.AddOverlayFont(pageIndex, font.Key, font.Value);
        }

        StringBuilder sb = new StringBuilder();
        sb.Append("q\n");
        if (gsName is not null)
        {
            sb.Append('/').Append(gsName).Append(" gs\n");
        }

        sb.Append(StampText.Fmt(placement.A)).Append(' ').Append(StampText.Fmt(placement.B)).Append(' ')
            .Append(StampText.Fmt(placement.C)).Append(' ').Append(StampText.Fmt(placement.D)).Append(' ')
            .Append(StampText.Fmt(placement.E)).Append(' ').Append(StampText.Fmt(placement.F)).Append(" cm\n");
        sb.Append(composed.ContentFragment);
        sb.Append("Q\n");
        _writer.AddOverlay(pageIndex, Encoding.Latin1.GetBytes(sb.ToString()));
    }

    private void AddBand(MarkTextComposer composer, BandText band, HeaderFooterOptions options, bool top)
    {
        double marginY = top ? Math.Abs(options.HeaderBaselineOffset) : options.FooterBaselineOffset;
        StampAnchor left = top ? StampAnchor.TopLeft : StampAnchor.BottomLeft;
        StampAnchor center = top ? StampAnchor.TopCenter : StampAnchor.BottomCenter;
        StampAnchor right = top ? StampAnchor.TopRight : StampAnchor.BottomRight;

        if (!string.IsNullOrEmpty(band.Left))
        {
            AddAnchoredText(composer, band.Left, left, options.FontSize, options.Color, options.MarginX, marginY, options.PageIndices, null, options.FilePath, options.Timestamp);
        }

        if (!string.IsNullOrEmpty(band.Center))
        {
            AddAnchoredText(composer, band.Center, center, options.FontSize, options.Color, options.MarginX, marginY, options.PageIndices, null, options.FilePath, options.Timestamp);
        }

        if (!string.IsNullOrEmpty(band.Right))
        {
            AddAnchoredText(composer, band.Right, right, options.FontSize, options.Color, options.MarginX, marginY, options.PageIndices, null, options.FilePath, options.Timestamp);
        }
    }

    private void AssignOutlineIds(IReadOnlyList<OutlineEntry> entries, List<OutlineNode> siblings)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            OutlineNode node = new OutlineNode(entries[i], _writer.AllocateId());
            AssignOutlineIds(entries[i].Children, node.Children);
            siblings.Add(node);
        }
    }

    private int WriteOutlineLevel(List<OutlineNode> siblings, PdfObjectId parentId, Dictionary<int, PdfObjectId> pageIds)
    {
        for (int i = 0; i < siblings.Count; i++)
        {
            OutlineNode node = siblings[i];
            PdfDictionary item = new PdfDictionary();
            item.Set(PdfName.Intern("Title"), new PdfString(node.Entry.Title));
            item.Set(PdfName.Parent, new PdfReference(parentId));

            if (i > 0)
            {
                item.Set(PdfName.Intern("Prev"), new PdfReference(siblings[i - 1].Id));
            }

            if (i < siblings.Count - 1)
            {
                item.Set(PdfName.Intern("Next"), new PdfReference(siblings[i + 1].Id));
            }

            if (pageIds.TryGetValue(node.Entry.PageIndex, out PdfObjectId pageId))
            {
                PdfArray dest = new PdfArray([new PdfReference(pageId), PdfName.Intern("Fit")]);
                item.Set(PdfName.Intern("Dest"), dest);
            }

            if (node.Children.Count > 0)
            {
                int childVisible = WriteOutlineLevel(node.Children, node.Id, pageIds);
                item.Set(PdfName.Intern("First"), new PdfReference(node.Children[0].Id));
                item.Set(PdfName.Intern("Last"), new PdfReference(node.Children[^1].Id));
                item.Set(PdfName.Count, -childVisible);
            }

            _writer.AddIndirectObject(node.Id, item);
        }

        return siblings.Count;
    }

    private sealed class OutlineNode
    {
        internal OutlineNode(OutlineEntry entry, PdfObjectId id)
        {
            Entry = entry;
            Id = id;
        }

        internal OutlineEntry Entry { get; }

        internal PdfObjectId Id { get; }

        internal List<OutlineNode> Children { get; } = new List<OutlineNode>();
    }
}
