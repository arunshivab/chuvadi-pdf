// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: v2.0.0 R3 — SearchAsync tests

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Text.Tests;

// ── SearchOptions ─────────────────────────────────────────────────────────

public sealed class SearchOptionsTests
{
    [Fact]
    public void Default_ProvidesSensibleDefaults()
    {
        SearchOptions opts = SearchOptions.Default;
        opts.CaseSensitive.Should().BeFalse();
        opts.WholeWord.Should().BeFalse();
        opts.PageRangeStart.Should().Be(0);
        opts.PageRangeEnd.Should().Be(int.MaxValue);
    }

    [Fact]
    public void NegativePageRangeStart_Throws()
    {
        Action act = () => new SearchOptions { PageRangeStart = -1 };
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void NegativePageRangeEnd_Throws()
    {
        Action act = () => new SearchOptions { PageRangeEnd = -1 };
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void InitOnly_PropertiesSet()
    {
        SearchOptions opts = new SearchOptions
        {
            CaseSensitive = true,
            WholeWord = true,
            PageRangeStart = 2,
            PageRangeEnd = 5,
        };
        opts.CaseSensitive.Should().BeTrue();
        opts.WholeWord.Should().BeTrue();
        opts.PageRangeStart.Should().Be(2);
        opts.PageRangeEnd.Should().Be(5);
    }
}

// ── SearchMatch ───────────────────────────────────────────────────────────

public sealed class SearchMatchTests
{
    [Fact]
    public void Constructor_NullBoundingBoxes_Throws()
    {
        Action act = () => new SearchMatch(1, 0, 5, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_StoresFields()
    {
        SearchMatch m = new SearchMatch(
            pageNumber: 3,
            characterOffset: 42,
            length: 5,
            boundingBoxes: Array.Empty<Chuvadi.Pdf.Graphics.RectangleF>());
        m.PageNumber.Should().Be(3);
        m.CharacterOffset.Should().Be(42);
        m.Length.Should().Be(5);
        m.BoundingBoxes.Should().BeEmpty();
    }
}

// ── SearchAsync via authored PDFs ─────────────────────────────────────────

public sealed class SearchAsyncTests
{
    private static PdfDocument BuildSinglePage(string text)
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        builder.AddPage(PageSize.A4)
            .DrawText(text, 50, 700, StandardFonts.Helvetica, 12, Colors.Black);
        byte[] bytes = builder.ToByteArray();
        return PdfDocument.Open(new MemoryStream(bytes), leaveOpen: false);
    }

    private static PdfDocument BuildTwoPages(string page1Text, string page2Text)
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        builder.AddPage(PageSize.A4)
            .DrawText(page1Text, 50, 700, StandardFonts.Helvetica, 12, Colors.Black);
        builder.AddPage(PageSize.A4)
            .DrawText(page2Text, 50, 700, StandardFonts.Helvetica, 12, Colors.Black);
        byte[] bytes = builder.ToByteArray();
        return PdfDocument.Open(new MemoryStream(bytes), leaveOpen: false);
    }

    private static async Task<List<SearchMatch>> CollectAsync(
        IAsyncEnumerable<SearchMatch> source)
    {
        List<SearchMatch> all = new List<SearchMatch>();
        await foreach (SearchMatch m in source)
        {
            all.Add(m);
        }
        return all;
    }

    [Fact]
    public async Task SearchAsync_NullDocument_Throws()
    {
        Func<Task> act = async () =>
        {
            await foreach (SearchMatch _ in PdfDocumentTextExtensions.SearchAsync(
                null!, "x", null, CancellationToken.None))
            {
            }
        };
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SearchAsync_NullQuery_Throws()
    {
        using PdfDocument doc = BuildSinglePage("hello");
        Func<Task> act = async () =>
        {
            await foreach (SearchMatch _ in doc.SearchAsync(null!))
            {
            }
        };
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_Throws()
    {
        using PdfDocument doc = BuildSinglePage("hello");
        Func<Task> act = async () =>
        {
            await foreach (SearchMatch _ in doc.SearchAsync(""))
            {
            }
        };
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SearchAsync_QueryNotPresent_ReturnsNoMatches()
    {
        using PdfDocument doc = BuildSinglePage("hello world");
        List<SearchMatch> matches = await CollectAsync(doc.SearchAsync("xyz"));
        matches.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_QueryPresent_ReturnsAtLeastOneMatch()
    {
        using PdfDocument doc = BuildSinglePage("hello world");
        List<SearchMatch> matches = await CollectAsync(doc.SearchAsync("hello"));
        matches.Should().NotBeEmpty();
        matches[0].PageNumber.Should().Be(1);
        matches[0].Length.Should().Be(5);
        matches[0].BoundingBoxes.Should().NotBeEmpty();
    }

    [Fact]
    public async Task SearchAsync_DefaultIsCaseInsensitive()
    {
        using PdfDocument doc = BuildSinglePage("HELLO");
        List<SearchMatch> matches = await CollectAsync(doc.SearchAsync("hello"));
        matches.Should().NotBeEmpty();
    }

    [Fact]
    public async Task SearchAsync_CaseSensitive_OnlyExactCaseMatches()
    {
        using PdfDocument doc = BuildSinglePage("HELLO");
        SearchOptions opts = new SearchOptions { CaseSensitive = true };
        List<SearchMatch> matches = await CollectAsync(doc.SearchAsync("hello", opts));
        matches.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_WholeWord_RejectsPartialMatches()
    {
        using PdfDocument doc = BuildSinglePage("hello helloworld hello");
        SearchOptions opts = new SearchOptions { WholeWord = true };
        List<SearchMatch> matches = await CollectAsync(doc.SearchAsync("hello", opts));
        // Should match "hello" twice (start and end) but not "hello" in "helloworld".
        matches.Count.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task SearchAsync_PageRangeStart_SkipsEarlierPages()
    {
        using PdfDocument doc = BuildTwoPages("alpha", "beta");
        SearchOptions opts = new SearchOptions { PageRangeStart = 1 };
        List<SearchMatch> matches = await CollectAsync(doc.SearchAsync("alpha", opts));
        matches.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_PageRangeEnd_StopsAfterRange()
    {
        using PdfDocument doc = BuildTwoPages("alpha", "beta");
        SearchOptions opts = new SearchOptions { PageRangeStart = 0, PageRangeEnd = 0 };
        List<SearchMatch> matches = await CollectAsync(doc.SearchAsync("beta", opts));
        matches.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_Cancellation_HonouredBeforeFirstPage()
    {
        using PdfDocument doc = BuildSinglePage("hello");
        using CancellationTokenSource cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = async () =>
        {
            await foreach (SearchMatch _ in doc.SearchAsync(
                "hello", null, cts.Token))
            {
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}

// ── GetTextRuns extension ─────────────────────────────────────────────────

public sealed class GetTextRunsTests
{
    [Fact]
    public void GetTextRuns_NullDocument_Throws()
    {
        Action act = () => PdfDocumentTextExtensions.GetTextRuns(null!, 0);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetTextRuns_NegativePageIndex_Throws()
    {
        using PdfDocument doc = BuildEmpty();
        Action act = () => doc.GetTextRuns(-1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void GetTextRuns_PageBeyondCount_Throws()
    {
        using PdfDocument doc = BuildEmpty();
        Action act = () => doc.GetTextRuns(99);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void GetTextRuns_EmptyPage_ReturnsEmpty()
    {
        using PdfDocument doc = BuildEmpty();
        IReadOnlyList<TextRun> runs = doc.GetTextRuns(0);
        runs.Should().BeEmpty();
    }

    [Fact]
    public void GetTextRuns_WithText_ReturnsAtLeastOneRun()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        builder.AddPage(PageSize.A4)
            .DrawText("Hello", 50, 700, StandardFonts.Helvetica, 12, Colors.Black);
        byte[] bytes = builder.ToByteArray();
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(bytes), leaveOpen: false);

        IReadOnlyList<TextRun> runs = doc.GetTextRuns(0);
        runs.Should().NotBeEmpty();
        runs[0].Unicode.Should().Contain("H");
    }

    private static PdfDocument BuildEmpty()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        builder.AddPage(PageSize.A4);
        byte[] bytes = builder.ToByteArray();
        return PdfDocument.Open(new MemoryStream(bytes), leaveOpen: false);
    }
}
