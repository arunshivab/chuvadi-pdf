// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.7, §8, §9 — report layout over authoring
// PHASE: Phase 2.7 — Report layout tests

using System;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Images;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Authoring.Tests;

public sealed class ReportBuilderTests
{
    // ── Basics ────────────────────────────────────────────────────────────

    [Fact]
    public void MinimalReport_OneParagraph_OnePage()
    {
        byte[] pdf = ReportBuilder.Create()
            .AddParagraph("Hello report")
            .ToByteArray();

        using PdfDocument read = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        read.PageCount.Should().Be(1);
    }

    [Fact]
    public void EmptyReport_StillProducesOnePage()
    {
        byte[] pdf = ReportBuilder.Create().ToByteArray();

        using PdfDocument read = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        read.PageCount.Should().Be(1);
    }

    [Fact]
    public void LongContent_FlowsAcrossPages()
    {
        ReportBuilder report = ReportBuilder.Create();
        for (int i = 0; i < 120; i++)
        {
            report.AddParagraph($"Paragraph {i}: the quick brown fox jumps over the lazy dog.");
        }
        byte[] pdf = report.ToByteArray();

        using PdfDocument read = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        read.PageCount.Should().BeGreaterThan(1);
    }

    [Fact]
    public void PageBreak_ForcesNewPage()
    {
        byte[] pdf = ReportBuilder.Create()
            .AddParagraph("first")
            .AddPageBreak()
            .AddParagraph("second")
            .ToByteArray();

        using PdfDocument read = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        read.PageCount.Should().Be(2);
    }

    [Fact]
    public void Heading_UsesBoldFont()
    {
        byte[] pdf = ReportBuilder.Create()
            .AddHeading("Quarterly Summary")
            .AddParagraph("body")
            .ToByteArray();

        string asText = Encoding.Latin1.GetString(pdf);
        asText.Should().Contain("Quarterly Summary");
        asText.Should().Contain("/HelveticaBold");
    }

    [Fact]
    public void CustomPageSetup_AppliesSizeAndMargins()
    {
        byte[] pdf = ReportBuilder.Create()
            .WithPageSetup(new ReportPageSetup
            {
                PageSize = PageSize.Letter.Landscape(),
                MarginLeft = 30,
                MarginTop = 30,
                MarginRight = 30,
                MarginBottom = 30,
            })
            .AddParagraph("landscape letter")
            .ToByteArray();

        using PdfDocument read = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        read.Pages[0].Width.Should().BeApproximately(792, 0.1);
        read.Pages[0].Height.Should().BeApproximately(612, 0.1);
    }

    // ── Lists ─────────────────────────────────────────────────────────────

    [Fact]
    public void BulletList_EmitsWinAnsiBullet()
    {
        byte[] pdf = ReportBuilder.Create()
            .AddBulletList(new[] { "alpha", "beta" })
            .ToByteArray();

        string asText = Encoding.Latin1.GetString(pdf);
        // U+2022 maps to WinAnsi 0x95, emitted as octal \225 in the literal string.
        asText.Should().Contain("\\225");
        asText.Should().Contain("alpha");
        asText.Should().Contain("beta");
    }

    [Fact]
    public void NumberedList_RomanNumbering_FormatsMarkers()
    {
        byte[] pdf = ReportBuilder.Create()
            .AddNumberedList(
                new[] { "one", "two", "three" },
                new ListStyle
                {
                    Numbering = NumberingFormat.RomanLower,
                })
            .ToByteArray();

        string asText = Encoding.Latin1.GetString(pdf);
        asText.Should().Contain("(i.)");
        asText.Should().Contain("(ii.)");
        asText.Should().Contain("(iii.)");
    }

    [Fact]
    public void NumberedList_StartAt_Offsets()
    {
        byte[] pdf = ReportBuilder.Create()
            .AddNumberedList(
                new[] { "x", "y" },
                new ListStyle
                {
                    StartAt = 5,
                })
            .ToByteArray();

        string asText = Encoding.Latin1.GetString(pdf);
        asText.Should().Contain("(5.)");
        asText.Should().Contain("(6.)");
    }

    // ── Headers / footers / page numbers ──────────────────────────────────

    [Fact]
    public void Footer_PageTokens_ExpandPerPage()
    {
        ReportBuilder report = ReportBuilder.Create()
            .WithFooter(new HeaderFooterStyle
            {
                Text = "Page {page} of {total}",
            });
        report.AddParagraph("a").AddPageBreak().AddParagraph("b").AddPageBreak().AddParagraph("c");
        byte[] pdf = report.ToByteArray();

        string asText = Encoding.Latin1.GetString(pdf);
        asText.Should().Contain("Page 1 of 3");
        asText.Should().Contain("Page 2 of 3");
        asText.Should().Contain("Page 3 of 3");
    }

    [Fact]
    public void Footer_RomanNumbering_FormatsPageNumbers()
    {
        ReportBuilder report = ReportBuilder.Create()
            .WithFooter(new HeaderFooterStyle
            {
                Text = "{page} / {total}",
                PageNumbering = NumberingFormat.RomanLower,
            });
        report.AddParagraph("a").AddPageBreak().AddParagraph("b");
        byte[] pdf = report.ToByteArray();

        string asText = Encoding.Latin1.GetString(pdf);
        asText.Should().Contain("(i / ii)");
        asText.Should().Contain("(ii / ii)");
    }

    [Fact]
    public void Header_TitleToken_UsesDocumentTitle()
    {
        byte[] pdf = ReportBuilder.Create()
            .SetTitle("Lab Results")
            .WithHeader(new HeaderFooterStyle
            {
                Text = "{title}",
            })
            .AddParagraph("body")
            .ToByteArray();

        Encoding.Latin1.GetString(pdf).Should().Contain("Lab Results");
    }

    [Fact]
    public void Header_HiddenOnFirstPage_SkipsPageOne()
    {
        ReportBuilder report = ReportBuilder.Create()
            .WithHeader(new HeaderFooterStyle
            {
                Text = "BAND{page}BAND",
                ShowOnFirstPage = false,
            });
        report.AddParagraph("a").AddPageBreak().AddParagraph("b");
        byte[] pdf = report.ToByteArray();

        string asText = Encoding.Latin1.GetString(pdf);
        asText.Should().NotContain("BAND1BAND");
        asText.Should().Contain("BAND2BAND");
    }

    [Fact]
    public void RawFooterCallback_ReceivesPageNumbers()
    {
        ReportBuilder report = ReportBuilder.Create()
            .WithFooter((page, num, total) => page.DrawText(
                $"RAW{num}of{total}", 50, page.Height - 30,
                StandardFonts.Helvetica, 8, Colors.Black));
        report.AddParagraph("a").AddPageBreak().AddParagraph("b");
        byte[] pdf = report.ToByteArray();

        string asText = Encoding.Latin1.GetString(pdf);
        asText.Should().Contain("RAW1of2");
        asText.Should().Contain("RAW2of2");
    }

    // ── Tables ────────────────────────────────────────────────────────────

    [Fact]
    public void Table_LongBody_PaginatesWithRepeatedHeader()
    {
        ReportTable table = new();
        table.AddColumn("Patient").AddColumn("Status");
        for (int i = 0; i < 80; i++)
        {
            table.AddRow($"Row{i}", "OK");
        }

        byte[] pdf = ReportBuilder.Create().AddTable(table).ToByteArray();

        using PdfDocument read = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        read.PageCount.Should().BeGreaterThan(1);

        string asText = Encoding.Latin1.GetString(pdf);
        int headerCount = CountOccurrences(asText, "(Patient)");
        headerCount.Should().Be(read.PageCount, "the header repeats on every table page");
    }

    [Fact]
    public void Table_HeaderRepeatDisabled_DrawsHeaderOnce()
    {
        ReportTable table = new()
        {
            Style = new TableStyle
            {
                RepeatHeaderOnEveryPage = false,
            },
        };
        table.AddColumn("OnlyOnce").AddColumn("B");
        for (int i = 0; i < 80; i++)
        {
            table.AddRow($"r{i}", "x");
        }

        byte[] pdf = ReportBuilder.Create().AddTable(table).ToByteArray();

        CountOccurrences(Encoding.Latin1.GetString(pdf), "(OnlyOnce)").Should().Be(1);
    }

    [Fact]
    public void Table_NoHeader_OmitsHeaderRow()
    {
        ReportTable table = new()
        {
            Style = new TableStyle
            {
                ShowHeader = false,
            },
        };
        table.AddColumn("Hidden").AddColumn("AlsoHidden");
        table.AddRow("a", "b");

        byte[] pdf = ReportBuilder.Create().AddTable(table).ToByteArray();

        string asText = Encoding.Latin1.GetString(pdf);
        asText.Should().NotContain("(Hidden)");
        asText.Should().Contain("(a)");
    }

    [Fact]
    public void Table_ColumnWidths_FixedFractionAndAuto_Render()
    {
        ReportTable table = new();
        table.AddColumn(new ReportColumn
        {
            Header = "Fixed",
            WidthMode = ColumnWidthMode.Points,
            Width = 120,
        });
        table.AddColumn(new ReportColumn
        {
            Header = "Frac",
            WidthMode = ColumnWidthMode.Fraction,
            Width = 0.3,
        });
        table.AddColumn("Auto");
        table.AddRow("a", "b", "c");

        byte[] pdf = ReportBuilder.Create().AddTable(table).ToByteArray();

        using PdfDocument read = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        read.PageCount.Should().Be(1);
    }

    [Fact]
    public void Table_ColSpanAndRowSpan_RenderWithoutError()
    {
        ReportTable table = new();
        table.AddColumn("A").AddColumn("B").AddColumn("C");

        ReportRow merged = new();
        merged.Cells.Add(new ReportCell("spans two columns")
        {
            ColSpan = 2,
        });
        merged.Cells.Add(new ReportCell("solo"));
        table.AddRow(merged);

        ReportRow tall = new();
        tall.Cells.Add(new ReportCell("spans two rows")
        {
            RowSpan = 2,
        });
        tall.Cells.Add(new ReportCell("b1"));
        tall.Cells.Add(new ReportCell("c1"));
        table.AddRow(tall);

        ReportRow second = new();
        second.Cells.Add(new ReportCell("b2"));
        second.Cells.Add(new ReportCell("c2"));
        table.AddRow(second);

        byte[] pdf = ReportBuilder.Create().AddTable(table).ToByteArray();

        string asText = Encoding.Latin1.GetString(pdf);
        asText.Should().Contain("spans two columns");
        asText.Should().Contain("spans two rows");
        asText.Should().Contain("(c2)");
    }

    [Fact]
    public void Table_OverlappingSpans_Throws()
    {
        ReportTable table = new();
        table.AddColumn("A").AddColumn("B");

        ReportRow r1 = new();
        r1.Cells.Add(new ReportCell("tall")
        {
            RowSpan = 2,
        });
        r1.Cells.Add(new ReportCell("b1"));
        table.AddRow(r1);

        // Second row wrongly supplies two cells — only one position is free.
        table.AddRow("x", "y");

        ReportBuilder report = ReportBuilder.Create().AddTable(table);
        Action act = () => report.ToByteArray();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Table_AlternatingRows_EmitFillColor()
    {
        ReportTable table = new()
        {
            Style = new TableStyle
            {
                AlternatingRowBackground = Color.FromHex("#E0E8F0"),
            },
        };
        table.AddColumn("A");
        table.AddRow("r1").AddRow("r2").AddRow("r3").AddRow("r4");

        byte[] pdf = ReportBuilder.Create().AddTable(table).ToByteArray();

        string asText = Encoding.Latin1.GetString(pdf);
        // #E0E8F0 → 0.878431 0.909804 0.941176 rg
        asText.Should().Contain("0.878431 0.909804 0.941176 rg");
    }

    [Fact]
    public void Table_EllipsisOverflow_TruncatesWithEllipsis()
    {
        ReportTable table = new();
        table.AddColumn(new ReportColumn
        {
            Header = "Narrow",
            WidthMode = ColumnWidthMode.Points,
            Width = 60,
            Overflow = CellOverflow.Ellipsis,
        });
        table.AddRow("an extremely long value that cannot possibly fit in sixty points");

        byte[] pdf = ReportBuilder.Create().AddTable(table).ToByteArray();

        string asText = Encoding.Latin1.GetString(pdf);
        // U+2026 maps to WinAnsi 0x85, emitted as octal \205.
        asText.Should().Contain("\\205");
    }

    [Fact]
    public void Table_CellStyleOverrides_Apply()
    {
        ReportTable table = new();
        table.AddColumn("A");
        ReportRow row = new();
        row.Cells.Add(new ReportCell("important")
        {
            Font = ReportFont.TimesBold,
            TextColor = Colors.Red,
            Background = Colors.LightGray,
        });
        table.AddRow(row);

        byte[] pdf = ReportBuilder.Create().AddTable(table).ToByteArray();

        string asText = Encoding.Latin1.GetString(pdf);
        asText.Should().Contain("/TimesBold");
        asText.Should().Contain("1 0 0 rg");
    }

    [Fact]
    public void Table_ImageCell_Embeds()
    {
        ImageFrame frame = ImageFrame.Create(4, 4, ImageColorFormat.Rgb24);
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                frame.Pixels.SetPixelBgra(x, y, 10, 20, 30, 255);
            }
        }

        ReportTable table = new();
        table.AddColumn("Logo").AddColumn("Name");
        ReportRow row = new();
        row.Cells.Add(new ReportCell
        {
            ImageFrame = frame,
        });
        row.Cells.Add(new ReportCell("Chuvadi"));
        table.AddRow(row);

        byte[] pdf = ReportBuilder.Create().AddTable(table).ToByteArray();

        string asText = Encoding.Latin1.GetString(pdf);
        asText.Should().Contain("/Img0 Do");
        using PdfDocument read = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        read.PageCount.Should().Be(1);
    }

    // ── Images and rules ──────────────────────────────────────────────────

    [Fact]
    public void ImageBlock_Png_RendersOnPage()
    {
        ImageFrame frame = ImageFrame.Create(10, 5, ImageColorFormat.Rgb24);
        for (int y = 0; y < 5; y++)
        {
            for (int x = 0; x < 10; x++)
            {
                frame.Pixels.SetPixelBgra(x, y, 1, 2, 3, 255);
            }
        }
        using MemoryStream ms = new();
        PngEncoder.Encode(frame, ms, includeAlpha: false);

        byte[] pdf = ReportBuilder.Create()
            .AddImage(ms.ToArray(), width: 200, alignment: TextAlignment.Center)
            .ToByteArray();

        string asText = Encoding.Latin1.GetString(pdf);
        asText.Should().Contain("/Img0 Do");
    }

    [Fact]
    public void HorizontalRule_DrawsAcrossContentWidth()
    {
        byte[] pdf = ReportBuilder.Create()
            .AddParagraph("above")
            .AddHorizontalRule()
            .AddParagraph("below")
            .ToByteArray();

        using PdfDocument read = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        read.PageCount.Should().Be(1);
    }

    [Fact]
    public void JustifiedParagraph_Renders()
    {
        byte[] pdf = ReportBuilder.Create()
            .AddParagraph(
                "Justified text stretches every full line so both edges align cleanly, " +
                "which calls for word-by-word placement across the column width of the page.",
                new ParagraphStyle
                {
                    Alignment = TextAlignment.Justify,
                })
            .ToByteArray();

        using PdfDocument read = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        read.PageCount.Should().Be(1);
    }

    // ── PageNumberFormatter ───────────────────────────────────────────────

    [Theory]
    [InlineData(1, NumberingFormat.Arabic, "1")]
    [InlineData(1, NumberingFormat.RomanUpper, "I")]
    [InlineData(1, NumberingFormat.RomanLower, "i")]
    [InlineData(4, NumberingFormat.RomanUpper, "IV")]
    [InlineData(9, NumberingFormat.RomanLower, "ix")]
    [InlineData(1944, NumberingFormat.RomanUpper, "MCMXLIV")]
    [InlineData(1, NumberingFormat.LetterUpper, "A")]
    [InlineData(26, NumberingFormat.LetterLower, "z")]
    [InlineData(27, NumberingFormat.LetterUpper, "AA")]
    [InlineData(28, NumberingFormat.LetterLower, "ab")]
    [InlineData(0, NumberingFormat.RomanUpper, "0")]
    public void PageNumberFormatter_Formats(int value, NumberingFormat format, string expected)
    {
        PageNumberFormatter.Format(value, format).Should().Be(expected);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static int CountOccurrences(string text, string needle)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
