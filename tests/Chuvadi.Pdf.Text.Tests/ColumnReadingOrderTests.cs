// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.29 — layout extraction reads columns of running text one after
//        another, in bands split by full-width lines (D-104, backlog 28).

using System.Linq;
using System.Text;
using Chuvadi.Pdf.Documents;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Text.Tests;

public sealed class ColumnReadingOrderTests
{
    private static readonly string[] Left =
    {
        "Hospitals collect many measures of",
        "quality, but only a few of them are",
        "comparable between centres. This",
        "study reviews the indicators used",
        "in radiotherapy departments and",
        "proposes a small common set that",
    };

    private static readonly string[] Right =
    {
        "together. Data were collected from",
        "twelve departments over two years.",
        "Each department reported the same",
        "fields on the same form, and the",
        "forms were checked twice before the",
        "results were pooled. Departments",
    };

    private const string Title = "Quality indicators in radiotherapy: a synthetic page for column tests";
    private const string Footer = "Footer: this full-width line ends the page after both of the columns above.";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TwoColumns_AreReadOneAfterTheOther_WithTheFooterLast(bool drawnRowByRow)
    {
        string text = Layout(Page(drawnRowByRow));

        TestPdf.Lines(text).Should().Equal(Enumerable.Repeat(Title, 1).Concat(Left).Concat(Right).Append(Footer));
    }

    [Fact]
    public void OperatorMode_KeepsTheDrawingOrder()
    {
        using PdfDocument doc = TestPdf.Open(Page(drawnRowByRow: true));

        string text = new TextExtractor(doc.Objects, ExtractionStrategy.Operator).ExtractText(doc.Pages[0]);

        // Drawn row by row, so read row by row.
        TestPdf.Lines(text)[1].Should().Be(Left[0] + " " + Right[0]);
    }

    [Fact]
    public void LabelsAndValues_StayInRows()
    {
        // A two-column form of short labels and values is not running text.
        string[] labels = { "Date of Birth", "Father's Name", "Marital Status", "Mother Tongue", "Languages", "Nationality" };
        string[] values = { "09 September 1982", "S. Balasubramanian", "Married", "Malayalam", "Tamil, Hindi", "Indian" };
        StringBuilder content = new StringBuilder();
        for (int i = 0; i < labels.Length; i++)
        {
            content.Append(TestPdf.Line(50, 700 - (i * 14), labels[i])).Append(TestPdf.Line(320, 700 - (i * 14), values[i]));
        }

        string text = Layout(content.ToString());

        TestPdf.Lines(text)[0].Should().Be("Date of Birth 09 September 1982");
        TestPdf.Lines(text)[5].Should().Be("Nationality Indian");
    }

    [Fact]
    public void ProseBesideShortCells_IsNotSplit_WhenTheTestIsUnsure()
    {
        // Running text on the left, a column of short entries on the right.
        StringBuilder content = new StringBuilder();
        for (int i = 0; i < Left.Length; i++)
        {
            content.Append(TestPdf.Line(50, 700 - (i * 14), Left[i])).Append(TestPdf.Line(320, 700 - (i * 14), "Item " + i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        string text = Layout(content.ToString());

        TestPdf.Lines(text)[0].Should().Be(Left[0] + " Item 0");
    }

    [Fact]
    public void AnalyzeLayout_ReportsTheBandsAndColumnsInReadingOrder()
    {
        using PdfDocument doc = TestPdf.Open(Page(drawnRowByRow: true));

        PageLayout layout = new TextExtractor(doc.Objects, ExtractionStrategy.Layout).AnalyzeLayout(doc.Pages[0]);

        layout.Source.Should().Be(ReadingOrderSource.Columns);
        layout.Bands.Should().HaveCount(3);
        layout.Bands[0].Columns.Should().HaveCount(1);
        layout.Bands[1].Columns.Should().HaveCount(2);
        layout.Bands[1].Columns[0].Bounds.X1.Should().Be(50);
        layout.Bands[1].Columns[1].Bounds.X1.Should().Be(320);
        layout.Bands[2].Columns.Should().HaveCount(1);
        layout.Bands[0].Bounds.Y1.Should().BeGreaterThanOrEqualTo(layout.Bands[1].Bounds.Y2);
    }

    [Fact]
    public void AnalyzeLayout_OfASingleColumnPage_IsRows()
    {
        using PdfDocument doc = TestPdf.Open(string.Concat(Left.Select((l, i) => TestPdf.Line(50, 700 - (i * 14), l))));

        new TextExtractor(doc.Objects).AnalyzeLayout(doc.Pages[0]).Should().BeSameAs(PageLayout.Rows);
    }

    [Fact]
    public void RowsLayout_ReadsAcrossTheColumns_AsIn328()
    {
        using PdfDocument doc = TestPdf.Open(Page(drawnRowByRow: false));

        string text = new TextExtractor(doc.Objects).ExtractText(doc.Pages[0], PageLayout.Rows);

        TestPdf.Lines(text)[1].Should().Be(Left[0] + " " + Right[0]);
    }

    [Fact]
    public void AnApplicationLayout_OverridesTheOneFound()
    {
        using PdfDocument doc = TestPdf.Open(Page(drawnRowByRow: true));
        TextExtractor extractor = new TextExtractor(doc.Objects, ExtractionStrategy.Layout);
        PageLayout found = extractor.AnalyzeLayout(doc.Pages[0]);

        // The same bands, with the two columns swapped.
        LayoutBand body = found.Bands[1];
        LayoutBand swapped = new LayoutBand(body.Bounds, new[] { body.Columns[1], body.Columns[0] });
        PageLayout mine = new PageLayout(ReadingOrderSource.Columns, new[] { found.Bands[0], swapped, found.Bands[2] });

        string text = extractor.ExtractText(doc.Pages[0], mine);

        TestPdf.Lines(text).Should().Equal(Enumerable.Repeat(Title, 1).Concat(Right).Concat(Left).Append(Footer));
    }

    [Fact]
    public void TextOutsideEveryBand_IsReadAsRowsByItsHeight()
    {
        using PdfDocument doc = TestPdf.Open(Page(drawnRowByRow: true));
        TextExtractor extractor = new TextExtractor(doc.Objects, ExtractionStrategy.Layout);
        PageLayout found = extractor.AnalyzeLayout(doc.Pages[0]);

        // Only the body band: the title and footer fall outside it.
        string text = extractor.ExtractText(doc.Pages[0], new PageLayout(ReadingOrderSource.Columns, new[] { found.Bands[1] }));

        TestPdf.Lines(text).Should().Equal(Enumerable.Repeat(Title, 1).Concat(Left).Concat(Right).Append(Footer));
    }

    // A title, two columns of six lines (left at x 50, right at x 320) and a
    // footer, drawn column after column or row by row.
    private static string Page(bool drawnRowByRow)
    {
        StringBuilder sb = new StringBuilder(TestPdf.Line(50, 740, Title));
        if (drawnRowByRow)
        {
            for (int i = 0; i < Left.Length; i++)
            {
                sb.Append(TestPdf.Line(50, 700 - (i * 14), Left[i])).Append(TestPdf.Line(320, 700 - (i * 14), Right[i]));
            }
        }
        else
        {
            for (int i = 0; i < Left.Length; i++)
            {
                sb.Append(TestPdf.Line(50, 700 - (i * 14), Left[i]));
            }

            for (int i = 0; i < Right.Length; i++)
            {
                sb.Append(TestPdf.Line(320, 700 - (i * 14), Right[i]));
            }
        }

        return sb.Append(TestPdf.Line(50, 600, Footer)).ToString();
    }

    private static string Layout(string content)
    {
        using PdfDocument doc = TestPdf.Open(content);
        return new TextExtractor(doc.Objects, ExtractionStrategy.Layout).ExtractText(doc.Pages[0]);
    }
}
