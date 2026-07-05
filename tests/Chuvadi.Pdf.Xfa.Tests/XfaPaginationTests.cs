// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chuvadi.Pdf.Xfa.Layout;
using Chuvadi.Pdf.Xfa.Model;
using Chuvadi.Pdf.Xfa.Parse;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Xfa.Tests;

public sealed class XfaPaginationTests
{
    private static readonly string FixturesDir =
        Path.Combine(System.AppContext.BaseDirectory, "Fixtures");

    private static XfaSubform ParseFixture(string name) =>
        XfaTemplateParser.Parse(
            File.ReadAllBytes(Path.Combine(FixturesDir, name)))!;

    // ── Q1: sequential contentArea fill ──────────────────────────────────────

    [Fact]
    public void Compose_TwoContentAreas_FillsInDocumentOrderThenNewPage()
    {
        // Two 100pt content areas; three 60pt blocks. Block 0 fills ca1; block 1
        // does not fit ca1's remaining 40pt so it moves to ca2 (y=120); block 2
        // does not fit ca2's remaining 40pt so it opens page 2 back at ca1.
        IReadOnlyList<XfaComposedPage> pages =
            XfaPaginator.Compose(ParseFixture("synthetic-two-contentareas.xml"));

        pages.Should().HaveCount(2);

        XfaBox b0 = FindText(pages[0], "block zero");
        XfaBox b1 = FindText(pages[0], "block one");
        b0.Y.Should().BeApproximately(0, 0.01, "block zero starts content area 1");
        b1.Y.Should().BeApproximately(120, 0.01, "block one overflows into content area 2");

        XfaBox b2 = FindText(pages[1], "block two");
        b2.Y.Should().BeApproximately(0, 0.01, "block two opens page 2 at content area 1");
    }

    // ── Q2: occur + orderedOccurrence page recurrence ─────────────────────────

    [Fact]
    public void Compose_OrderedOccurrence_UsesFirstPageOnceThenUnboundedBody()
    {
        // First (occur max=1, content y=50) then Body (occur max=-1, content
        // y=10). Four 80pt blocks in 100pt content areas: one block per page.
        IReadOnlyList<XfaComposedPage> pages =
            XfaPaginator.Compose(ParseFixture("synthetic-paginated.xml"));

        pages.Should().HaveCount(4);
        pages[0].Area.Name.Should().Be("First");
        pages[1].Area.Name.Should().Be("Body");
        pages[2].Area.Name.Should().Be("Body");
        pages[3].Area.Name.Should().Be("Body");

        FindText(pages[0], "page zero content").Y.Should().BeApproximately(
            50, 0.01, "the First page's content area starts at y=50");
        FindText(pages[1], "page one content").Y.Should().BeApproximately(
            10, 0.01, "the Body page's content area starts at y=10");
    }

    [Fact]
    public void Parse_Occur_CapturesMinMax()
    {
        XfaSubform root = ParseFixture("synthetic-paginated.xml");
        List<XfaPageArea> areas = CollectPageAreas(root);

        areas.Should().HaveCount(2);
        areas[0].Name.Should().Be("First");
        areas[0].MaxOccur.Should().Be(1);
        areas[1].Name.Should().Be("Body");
        areas[1].MaxOccur.Should().Be(-1, "max=-1 means unbounded recurrence");
    }

    [Fact]
    public void Parse_PageSetRelation_IsCaptured()
    {
        XfaSubform root = ParseFixture("synthetic-paginated.xml");
        XfaPageSet pageSet = FindPageSet(root)!;
        pageSet.Relation.Should().Be(XfaPageSetRelation.OrderedOccurrence);
    }

    // ── Q3: forced breaks ─────────────────────────────────────────────────────

    [Fact]
    public void Compose_BreakBeforePageArea_ForcesNewPage()
    {
        // alpha fits page 1 comfortably; bravo carries breakBefore pageArea so it
        // must open page 2 even though page 1 had room; charlie follows bravo.
        IReadOnlyList<XfaComposedPage> pages =
            XfaPaginator.Compose(ParseFixture("synthetic-breaks.xml"));

        pages.Should().HaveCount(2);
        AllTexts(pages[0]).Should().Contain("alpha");
        AllTexts(pages[0]).Should().NotContain("bravo");

        AllTexts(pages[1]).Should().Contain("bravo");
        AllTexts(pages[1]).Should().Contain("charlie");
        FindText(pages[1], "bravo").Y.Should().BeApproximately(0, 0.01);
        FindText(pages[1], "charlie").Y.Should().BeApproximately(
            30, 0.01, "charlie flows after the 30pt bravo block");
    }

    [Fact]
    public void Parse_BreakBefore_IsCapturedOnSubform()
    {
        XfaSubform root = ParseFixture("synthetic-breaks.xml");
        XfaNode b = FindByName(root, "b");
        b.BreakBefore.Should().Be(XfaBreakTarget.PageArea);
        b.BreakAfter.Should().BeNull();
    }

    // ── Regression: single-page positioned form stays single-page ─────────────

    [Fact]
    public void Compose_RealForm_RemainsSinglePage()
    {
        using Chuvadi.Pdf.Documents.PdfDocument doc = Chuvadi.Pdf.Documents.PdfDocument.Open(
            Path.Combine(FixturesDir, "livecycle-coi-redacted.pdf"));

        XfaSubform root = XfaTemplateParser.Parse(doc.Xfa!.Template!.Xml)!;
        XfaDataMerge.Apply(root, doc.Xfa.DataFields);

        IReadOnlyList<XfaComposedPage> pages = XfaPaginator.Compose(root);
        pages.Should().HaveCount(1, "the certificate's content fits its single content area");
        pages[0].Boxes.Should().NotBeEmpty();
    }

    private static XfaBox FindText(XfaComposedPage page, string text) =>
        page.Boxes.First(b => b.Text == text);

    private static List<string> AllTexts(XfaComposedPage page) =>
        page.Boxes.Where(b => b.Text is not null).Select(b => b.Text!).ToList();

    private static List<XfaPageArea> CollectPageAreas(XfaNode root)
    {
        List<XfaPageArea> areas = new List<XfaPageArea>();
        Collect(root);
        return areas;

        void Collect(XfaNode node)
        {
            if (node is XfaPageArea area)
            {
                areas.Add(area);
            }

            foreach (XfaNode child in node.Children)
            {
                Collect(child);
            }
        }
    }

    private static XfaPageSet? FindPageSet(XfaNode node)
    {
        if (node is XfaPageSet pageSet)
        {
            return pageSet;
        }

        foreach (XfaNode child in node.Children)
        {
            XfaPageSet? found = FindPageSet(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static XfaNode FindByName(XfaNode root, string name)
    {
        Queue<XfaNode> queue = new Queue<XfaNode>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            XfaNode node = queue.Dequeue();
            if (node.Name == name)
            {
                return node;
            }

            foreach (XfaNode child in node.Children)
            {
                queue.Enqueue(child);
            }
        }

        throw new KeyNotFoundException($"No node named '{name}'.");
    }
}
