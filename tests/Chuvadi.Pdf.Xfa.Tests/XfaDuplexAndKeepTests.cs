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

public sealed class XfaDuplexAndKeepTests
{
    private static readonly string FixturesDir =
        Path.Combine(System.AppContext.BaseDirectory, "Fixtures");

    private static XfaSubform ParseFixture(string name) =>
        XfaTemplateParser.Parse(
            File.ReadAllBytes(Path.Combine(FixturesDir, name)))!;

    // ── Duplex pagination ─────────────────────────────────────────────────────

    [Fact]
    public void Compose_Duplex_AlternatesFrontAndBackByParity()
    {
        // Front is odd-only (content y=40); Back is even-only (content y=80).
        // Three 80pt blocks in 100pt content areas: one per page, alternating.
        IReadOnlyList<XfaComposedPage> pages =
            XfaPaginator.Compose(ParseFixture("synthetic-duplex.xml"));

        pages.Should().HaveCount(3);
        pages[0].Area.Name.Should().Be("Front");
        pages[1].Area.Name.Should().Be("Back");
        pages[2].Area.Name.Should().Be("Front");

        FindText(pages[0], "side one").Y.Should().BeApproximately(40, 0.01);
        FindText(pages[1], "side two").Y.Should().BeApproximately(80, 0.01);
        FindText(pages[2], "side three").Y.Should().BeApproximately(40, 0.01);
    }

    [Fact]
    public void Parse_OddOrEven_IsCaptured()
    {
        XfaSubform root = ParseFixture("synthetic-duplex.xml");
        List<XfaPageArea> areas = CollectPageAreas(root);

        areas.Should().HaveCount(2);
        areas[0].OddOrEven.Should().Be(XfaOddOrEven.Odd);
        areas[1].OddOrEven.Should().Be(XfaOddOrEven.Even);
    }

    [Fact]
    public void Parse_DuplexRelation_IsCaptured()
    {
        XfaSubform root = ParseFixture("synthetic-duplex.xml");
        FindPageSet(root)!.Relation.Should().Be(XfaPageSetRelation.DuplexPaginated);
    }

    // ── Keep constraints ──────────────────────────────────────────────────────

    [Fact]
    public void Compose_KeepNextContentArea_MovesGroupTogether()
    {
        // filler (60pt) leaves 40pt in the 100pt content area. heading (20pt,
        // keep next=contentArea) would fit, but its bound body (40pt) would not
        // — so the whole group moves to the next region (page 2).
        IReadOnlyList<XfaComposedPage> pages =
            XfaPaginator.Compose(ParseFixture("synthetic-keep.xml"));

        pages.Should().HaveCount(2);
        AllTexts(pages[0]).Should().Contain("filler");
        AllTexts(pages[0]).Should().NotContain("heading");

        FindText(pages[1], "heading").Y.Should().BeApproximately(0, 0.01);
        FindText(pages[1], "body").Y.Should().BeApproximately(
            20, 0.01, "the bound body flows immediately after the heading");
    }

    [Fact]
    public void Compose_KeepNextPageArea_AllowsContentAreaCrossingWithinPage()
    {
        // Two 100pt content areas per page. filler (90pt) nearly fills ca1.
        // heading (20pt, keep next=pageArea) + body (90pt): body would cross to
        // the NEXT PAGE, violating pageArea scope — so the group is rolled back
        // and re-placed on page 2, where heading lands in ca1 and body in ca2
        // (a content-area transition, which pageArea scope permits).
        IReadOnlyList<XfaComposedPage> pages =
            XfaPaginator.Compose(ParseFixture("synthetic-keep-page.xml"));

        pages.Should().HaveCount(2);
        AllTexts(pages[0]).Should().Contain("filler");
        AllTexts(pages[0]).Should().NotContain("heading");

        FindText(pages[1], "heading").Y.Should().BeApproximately(
            0, 0.01, "the group restarts at page 2's first content area");
        FindText(pages[1], "body").Y.Should().BeApproximately(
            120, 0.01, "the body overflows into ca2 (y=120) on the same page");
    }

    [Fact]
    public void Parse_Keep_IsCaptured()
    {
        XfaSubform root = ParseFixture("synthetic-keep.xml");
        XfaNode heading = FindByName(root, "heading");
        heading.KeepNext.Should().Be(XfaKeepScope.ContentArea);
        heading.KeepPrevious.Should().Be(XfaKeepScope.None);
        heading.KeepIntact.Should().Be(XfaKeepScope.None);
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
