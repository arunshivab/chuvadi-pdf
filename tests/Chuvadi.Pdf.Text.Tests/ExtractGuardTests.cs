// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.24 — embedding API: cancellation (A3) and limits (A4) on
// TextExtractor.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Text.Tests;

public sealed class ExtractGuardTests
{
    [Fact]
    public async Task ExtractText_StopsSoonAfterCancellation()
    {
        using PdfDocument doc = Open(Repeat("BT /F1 12 Tf 10 10 Td (ABCDEFGHIJ) Tj ET\n", 400_000));
        TextExtractor extractor = new TextExtractor(doc.Objects);
        using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        Stopwatch watch = Stopwatch.StartNew();
        Task<string> work = Task.Run(() => extractor.ExtractText(doc.Pages[0], cts.Token));
        Task finished = await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(20)));
        watch.Stop();

        finished.Should().BeSameAs(work);
        Func<Task> outcome = () => work;
        await outcome.Should().ThrowAsync<OperationCanceledException>();
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1.5));
    }

    [Fact]
    public void ExtractFragments_HonoursAnAlreadyCancelledToken()
    {
        using PdfDocument doc = Open("BT /F1 12 Tf 10 10 Td (HELLO) Tj ET");
        using CancellationTokenSource cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(
            () => new TextExtractor(doc.Objects).ExtractFragments(doc.Pages[0], cts.Token));
    }

    [Fact]
    public void PageComplexityLimit_StopsTheExtraction()
    {
        using PdfDocument doc = Open(
            Repeat("BT /F1 12 Tf 10 10 Td (A) Tj ET\n", 2_000),
            new PdfLimits { MaxContentOperators = 1_000 });

        Assert.Throws<PdfLimitExceededException>(() => new TextExtractor(doc.Objects).ExtractText(doc.Pages[0]))
            .LimitName.Should().Be(nameof(PdfLimits.MaxContentOperators));
    }

    [Fact]
    public void OperandsWithNoOperator_CountTowardTheComplexityLimit()
    {
        // Two million numbers and no operator: malformed, and no longer free.
        using PdfDocument doc = Open(Repeat("0 ", 2_000_000), new PdfLimits { MaxContentOperators = 100 });

        Assert.Throws<PdfLimitExceededException>(() => new TextExtractor(doc.Objects).ExtractText(doc.Pages[0]));
    }

    [Fact]
    public void WithinTheLimits_TextIsExtracted()
    {
        using PdfDocument doc = Open("BT /F1 12 Tf 10 10 Td (HELLO) Tj ET", new PdfLimits { MaxContentOperators = 100 });

        new TextExtractor(doc.Objects).ExtractText(doc.Pages[0], CancellationToken.None).Should().Contain("HELLO");
    }

    private static string Repeat(string s, int n)
    {
        StringBuilder sb = new StringBuilder(s.Length * n);
        for (int i = 0; i < n; i++)
        {
            sb.Append(s);
        }

        return sb.ToString();
    }

    private static PdfDocument Open(string content, PdfLimits? limits = null)
    {
        List<string> bodies = new List<string>
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 400 400]/Resources<</Font<</F1 5 0 R>>>>/Contents 4 0 R>>",
            "<</Length " + content.Length + ">>\nstream\n" + content + "\nendstream",
            "<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>",
        };

        StringBuilder sb = new StringBuilder("%PDF-1.7\n");
        List<int> offsets = new List<int>();
        for (int i = 0; i < bodies.Count; i++)
        {
            offsets.Add(sb.Length);
            sb.Append(i + 1).Append(" 0 obj\n").Append(bodies[i]).Append("\nendobj\n");
        }

        int xref = sb.Length;
        sb.Append("xref\n0 ").Append(bodies.Count + 1).Append("\n0000000000 65535 f \n");
        foreach (int off in offsets)
        {
            sb.Append(off.ToString("D10", System.Globalization.CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        sb.Append("trailer\n<</Size ").Append(bodies.Count + 1).Append("/Root 1 0 R>>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        return PdfDocument.Open(new MemoryStream(Encoding.Latin1.GetBytes(sb.ToString())), new PdfOpenOptions { Limits = limits });
    }
}
