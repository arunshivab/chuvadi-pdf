// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.7 — Document structure
// PHASE: Phase 2.7 — Report layout
// Flowing-document composer: content blocks paginate automatically with
// repeating table headers, page headers/footers, and formatted page numbers.

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Images;

namespace Chuvadi.Pdf.Authoring;

/// <summary>
/// Composes multi-page PDF reports from flowing content blocks — headings,
/// paragraphs, bulleted and numbered lists, tables, images, rules, and page
/// breaks — with automatic pagination.
/// </summary>
/// <remarks>
/// <para>
/// Unlike <see cref="PdfDocumentBuilder"/>, which draws at explicit
/// coordinates, <see cref="ReportBuilder"/> lays content out top-to-bottom
/// inside the page margins and starts new pages as needed. Tables longer than
/// a page continue across pages, optionally repeating their header row.
/// Page headers and footers support <c>{page}</c>, <c>{total}</c>,
/// <c>{title}</c>, and <c>{date}</c> tokens with Arabic, Roman, or letter
/// page numbering.
/// </para>
/// <para>
/// Every styling knob has a default, so a minimal report is three lines:
/// create, add content, save.
/// </para>
/// </remarks>
public sealed class ReportBuilder
{
    private readonly List<ReportBlock> _blocks = new();
    private ReportPageSetup _pageSetup = ReportPageSetup.Default;
    private HeaderFooterStyle? _header;
    private HeaderFooterStyle? _footer;
    private Action<PageBuilder, int, int>? _rawHeader;
    private Action<PageBuilder, int, int>? _rawFooter;
    private string? _title;
    private string? _author;
    private string? _subject;

    private ReportBuilder()
    {
    }

    /// <summary>Creates a new empty report.</summary>
    public static ReportBuilder Create() => new();

    // ── Document setup ────────────────────────────────────────────────────

    /// <summary>Sets the document /Title metadata (also available to headers/footers as {title}).</summary>
    public ReportBuilder SetTitle(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        _title = title;
        return this;
    }

    /// <summary>Sets the document /Author metadata.</summary>
    public ReportBuilder SetAuthor(string author)
    {
        ArgumentNullException.ThrowIfNull(author);
        _author = author;
        return this;
    }

    /// <summary>Sets the document /Subject metadata.</summary>
    public ReportBuilder SetSubject(string subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        _subject = subject;
        return this;
    }

    /// <summary>Sets the page size and margins for every page.</summary>
    public ReportBuilder WithPageSetup(ReportPageSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        _pageSetup = setup;
        return this;
    }

    /// <summary>
    /// Sets a styled page header. The text supports the tokens <c>{page}</c>,
    /// <c>{total}</c>, <c>{title}</c>, and <c>{date}</c>.
    /// </summary>
    public ReportBuilder WithHeader(HeaderFooterStyle header)
    {
        ArgumentNullException.ThrowIfNull(header);
        _header = header;
        _rawHeader = null;
        return this;
    }

    /// <summary>
    /// Sets a styled page footer. The text supports the tokens <c>{page}</c>,
    /// <c>{total}</c>, <c>{title}</c>, and <c>{date}</c> — for example
    /// <c>"Page {page} of {total}"</c>.
    /// </summary>
    public ReportBuilder WithFooter(HeaderFooterStyle footer)
    {
        ArgumentNullException.ThrowIfNull(footer);
        _footer = footer;
        _rawFooter = null;
        return this;
    }

    /// <summary>
    /// Sets a free-form header callback receiving the page, 1-based page
    /// number, and total page count — the escape hatch when the styled
    /// header is not flexible enough.
    /// </summary>
    public ReportBuilder WithHeader(Action<PageBuilder, int, int> draw)
    {
        ArgumentNullException.ThrowIfNull(draw);
        _rawHeader = draw;
        _header = null;
        return this;
    }

    /// <summary>Sets a free-form footer callback. Same shape as <see cref="WithHeader(Action{PageBuilder, int, int})"/>.</summary>
    public ReportBuilder WithFooter(Action<PageBuilder, int, int> draw)
    {
        ArgumentNullException.ThrowIfNull(draw);
        _rawFooter = draw;
        _footer = null;
        return this;
    }

    // ── Content blocks ────────────────────────────────────────────────────

    /// <summary>
    /// Adds a heading. Levels 1–3 map to 16 / 13.5 / 12 point bold with
    /// matching spacing; other levels render as level 3.
    /// </summary>
    public ReportBuilder AddHeading(string text, int level = 1)
    {
        ArgumentNullException.ThrowIfNull(text);
        double size = level <= 1 ? 16 : (level == 2 ? 13.5 : 12);
        ParagraphStyle style = new()
        {
            Font = ReportFont.HelveticaBold,
            FontSize = size,
            SpaceBefore = level <= 1 ? 8 : 6,
            SpaceAfter = level <= 1 ? 8 : 5,
        };
        _blocks.Add(new ParagraphBlock(text, style));
        return this;
    }

    /// <summary>Adds a heading with a fully custom style.</summary>
    public ReportBuilder AddHeading(string text, ParagraphStyle style)
        => AddParagraph(text, style);

    /// <summary>Adds a paragraph using <see cref="ParagraphStyle.Default"/> or the supplied style.</summary>
    public ReportBuilder AddParagraph(string text, ParagraphStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        _blocks.Add(new ParagraphBlock(text, style ?? ParagraphStyle.Default));
        return this;
    }

    /// <summary>Adds a bulleted list.</summary>
    public ReportBuilder AddBulletList(IEnumerable<string> items, ListStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        _blocks.Add(new ListBlock(new List<string>(items), style ?? ListStyle.Default, ordered: false));
        return this;
    }

    /// <summary>Adds a numbered list (Arabic, Roman, or letter numbering per the style).</summary>
    public ReportBuilder AddNumberedList(IEnumerable<string> items, ListStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        _blocks.Add(new ListBlock(new List<string>(items), style ?? ListStyle.Default, ordered: true));
        return this;
    }

    /// <summary>Adds a table; tables longer than a page paginate automatically.</summary>
    public ReportBuilder AddTable(ReportTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (table.Columns.Count == 0)
        {
            throw new ArgumentException("Table has no columns.", nameof(table));
        }
        _blocks.Add(new TableBlock(table));
        return this;
    }

    /// <summary>
    /// Adds an image (JPEG, PNG, TIFF, or BMP). When width/height are omitted
    /// the natural size at 96 DPI is used, capped to the content width.
    /// Supplying only one dimension preserves the aspect ratio.
    /// </summary>
    public ReportBuilder AddImage(
        byte[] imageBytes,
        double? width = null,
        double? height = null,
        TextAlignment alignment = TextAlignment.Left,
        double spaceAfter = 8)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        _blocks.Add(new ImageBlock(imageBytes, null, width, height, alignment, spaceAfter));
        return this;
    }

    /// <summary>Adds an already-decoded image frame. Same sizing rules as the byte overload.</summary>
    public ReportBuilder AddImage(
        ImageFrame image,
        double? width = null,
        double? height = null,
        TextAlignment alignment = TextAlignment.Left,
        double spaceAfter = 8)
    {
        ArgumentNullException.ThrowIfNull(image);
        _blocks.Add(new ImageBlock(null, image, width, height, alignment, spaceAfter));
        return this;
    }

    /// <summary>Adds vertical blank space.</summary>
    public ReportBuilder AddSpacer(double points)
    {
        if (points < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(points), "Spacer height cannot be negative.");
        }
        _blocks.Add(new SpacerBlock(points));
        return this;
    }

    /// <summary>Forces the following content onto a new page.</summary>
    public ReportBuilder AddPageBreak()
    {
        _blocks.Add(new PageBreakBlock());
        return this;
    }

    /// <summary>Adds a horizontal rule across the content width.</summary>
    public ReportBuilder AddHorizontalRule(
        Color? color = null, double width = 0.75, double spaceAfter = 8)
    {
        _blocks.Add(new RuleBlock(color ?? Colors.Gray, width, spaceAfter));
        return this;
    }

    // ── Output ────────────────────────────────────────────────────────────

    /// <summary>Composes the report and returns the PDF bytes.</summary>
    public byte[] ToByteArray()
    {
        PdfDocumentBuilder doc = PdfDocumentBuilder.Create();
        if (_title is not null)
        {
            doc.SetTitle(_title);
        }
        if (_author is not null)
        {
            doc.SetAuthor(_author);
        }
        if (_subject is not null)
        {
            doc.SetSubject(_subject);
        }

        ConfigureBands(doc);

        ReportLayoutEngine engine = new(doc, _pageSetup);
        engine.Run(_blocks);
        return doc.ToByteArray();
    }

    /// <summary>Composes the report and writes the PDF to a stream.</summary>
    public void Save(Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);
        byte[] bytes = ToByteArray();
        output.Write(bytes, 0, bytes.Length);
    }

    /// <summary>Composes the report and writes the PDF to a file (overwritten when present).</summary>
    public void SaveToFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        File.WriteAllBytes(path, ToByteArray());
    }

    private void ConfigureBands(PdfDocumentBuilder doc)
    {
        if (_rawHeader is not null)
        {
            doc.SetHeader(_rawHeader);
        }
        else if (_header is not null)
        {
            HeaderFooterStyle style = _header;
            doc.SetHeader((page, num, total) => DrawBand(page, style, num, total, isHeader: true));
        }

        if (_rawFooter is not null)
        {
            doc.SetFooter(_rawFooter);
        }
        else if (_footer is not null)
        {
            HeaderFooterStyle style = _footer;
            doc.SetFooter((page, num, total) => DrawBand(page, style, num, total, isHeader: false));
        }
    }

    private void DrawBand(
        PageBuilder page, HeaderFooterStyle style, int pageNumber, int total, bool isHeader)
    {
        if (pageNumber == 1 && !style.ShowOnFirstPage)
        {
            return;
        }

        string text = ExpandTokens(style.Text, pageNumber, total, style.PageNumbering);
        string font = style.Font.Resolve();
        double y = isHeader
            ? style.EdgeOffset
            : page.Height - style.EdgeOffset - style.FontSize;

        double contentLeft = _pageSetup.MarginLeft;
        double contentWidth = _pageSetup.ContentWidth;
        double textWidth = ReportLayoutEngine.Measure(text, font, style.FontSize);
        double x = contentLeft;
        if (style.Alignment == TextAlignment.Center)
        {
            x = contentLeft + ((contentWidth - textWidth) / 2);
        }
        else if (style.Alignment == TextAlignment.Right)
        {
            x = contentLeft + contentWidth - textWidth;
        }

        page.DrawText(WinAnsiText.Map(text), x, y, font, style.FontSize, style.Color);

        if (style.RuleLine)
        {
            double ruleY = isHeader
                ? y + style.FontSize + 4
                : y - 4;
            page.DrawLine(contentLeft, ruleY, contentLeft + contentWidth, ruleY,
                style.Color, 0.5);
        }
    }

    private string ExpandTokens(string text, int pageNumber, int total, NumberingFormat numbering)
    {
        return text
            .Replace("{page}", PageNumberFormatter.Format(pageNumber, numbering), StringComparison.Ordinal)
            .Replace("{total}", PageNumberFormatter.Format(total, numbering), StringComparison.Ordinal)
            .Replace("{title}", _title ?? string.Empty, StringComparison.Ordinal)
            .Replace("{date}", DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }
}

// ── Block model (internal) ────────────────────────────────────────────────

/// <summary>Base of the report content blocks.</summary>
internal abstract class ReportBlock
{
}

/// <summary>A flowing text paragraph.</summary>
internal sealed class ParagraphBlock : ReportBlock
{
    internal ParagraphBlock(string text, ParagraphStyle style)
    {
        Text = text;
        Style = style;
    }

    internal string Text { get; }

    internal ParagraphStyle Style { get; }
}

/// <summary>A bulleted or numbered list.</summary>
internal sealed class ListBlock : ReportBlock
{
    internal ListBlock(List<string> items, ListStyle style, bool ordered)
    {
        Items = items;
        Style = style;
        Ordered = ordered;
    }

    internal List<string> Items { get; }

    internal ListStyle Style { get; }

    internal bool Ordered { get; }
}

/// <summary>A table.</summary>
internal sealed class TableBlock : ReportBlock
{
    internal TableBlock(ReportTable table)
    {
        Table = table;
    }

    internal ReportTable Table { get; }
}

/// <summary>An image.</summary>
internal sealed class ImageBlock : ReportBlock
{
    internal ImageBlock(
        byte[]? bytes, ImageFrame? frame,
        double? width, double? height, TextAlignment alignment, double spaceAfter)
    {
        Bytes = bytes;
        Frame = frame;
        Width = width;
        Height = height;
        Alignment = alignment;
        SpaceAfter = spaceAfter;
    }

    internal byte[]? Bytes { get; }

    internal ImageFrame? Frame { get; }

    internal double? Width { get; }

    internal double? Height { get; }

    internal TextAlignment Alignment { get; }

    internal double SpaceAfter { get; }
}

/// <summary>Vertical blank space.</summary>
internal sealed class SpacerBlock : ReportBlock
{
    internal SpacerBlock(double height)
    {
        Height = height;
    }

    internal double Height { get; }
}

/// <summary>A forced page break.</summary>
internal sealed class PageBreakBlock : ReportBlock
{
}

/// <summary>A horizontal rule.</summary>
internal sealed class RuleBlock : ReportBlock
{
    internal RuleBlock(Color color, double width, double spaceAfter)
    {
        Color = color;
        Width = width;
        SpaceAfter = spaceAfter;
    }

    internal Color Color { get; }

    internal double Width { get; }

    internal double SpaceAfter { get; }
}
