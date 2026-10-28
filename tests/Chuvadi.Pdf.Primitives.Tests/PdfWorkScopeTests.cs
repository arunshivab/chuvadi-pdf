// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.24 — embedding API: the ambient work scope (A3, A4).

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Primitives.Tests;

public sealed class PdfWorkScopeTests
{
    [Fact]
    public void OutsideAScope_ChecksDoNothing()
    {
        PdfWorkScope.Current.Should().BeNull();
        PdfWorkScope.CountOperators(long.MaxValue / 2);
        PdfWorkScope.ChargeMemory(long.MaxValue / 2);
        PdfWorkScope.CheckDecodedSize(long.MaxValue);
        PdfWorkScope.ThrowIfCancellationRequested();
    }

    [Fact]
    public void Dispose_RestoresTheEnclosingScope()
    {
        using (PdfWorkScope outer = PdfWorkScope.Enter(null))
        {
            using (PdfWorkScope inner = PdfWorkScope.Enter(null))
            {
                PdfWorkScope.Current.Should().BeSameAs(inner);
            }

            PdfWorkScope.Current.Should().BeSameAs(outer);
        }

        PdfWorkScope.Current.Should().BeNull();
    }

    [Fact]
    public void InnerScope_HonoursTheOuterTokenAndLimits()
    {
        using CancellationTokenSource cts = new CancellationTokenSource();
        using PdfWorkScope outer = PdfWorkScope.Enter(new PdfLimits { MaxContentOperators = 10 }, cts.Token);
        using PdfWorkScope inner = PdfWorkScope.Enter(null);

        PdfWorkScope.CountOperators(10);
        Assert.Throws<PdfLimitExceededException>(() => PdfWorkScope.CountOperators());
        outer.ContentOperators.Should().Be(11);

        cts.Cancel();
        Assert.Throws<OperationCanceledException>(PdfWorkScope.ThrowIfCancellationRequested);
    }

    [Fact]
    public void Enter_WithACancelledToken_ThrowsWithoutLeavingAScope()
    {
        using CancellationTokenSource cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => PdfWorkScope.Enter(null, cts.Token));

        PdfWorkScope.Current.Should().BeNull();
    }

    [Fact]
    public void Limits_NameWhatWasExceeded()
    {
        using PdfWorkScope scope = PdfWorkScope.Enter(new PdfLimits
        {
            MaxMemoryBytes = 1000,
            MaxDecodedStreamBytes = 500,
            MaxOutputPixels = 100,
        });

        Assert.Throws<PdfLimitExceededException>(() => PdfWorkScope.CheckDecodedSize(501))
            .LimitName.Should().Be(nameof(PdfLimits.MaxDecodedStreamBytes));
        Assert.Throws<PdfLimitExceededException>(() => PdfWorkScope.ChargeRaster(20, 10))
            .LimitName.Should().Be(nameof(PdfLimits.MaxOutputPixels));
        PdfWorkScope.ChargeMemory(900);
        Assert.Throws<PdfLimitExceededException>(() => PdfWorkScope.ChargeMemory(200))
            .LimitName.Should().Be(nameof(PdfLimits.MaxMemoryBytes));
    }

    [Fact]
    public async Task TheScope_FlowsIntoTaskRun()
    {
        using CancellationTokenSource cts = new CancellationTokenSource();
        using PdfWorkScope scope = PdfWorkScope.Enter(null, cts.Token);
        cts.Cancel();

        Func<Task> act = () => Task.Run(PdfWorkScope.ThrowIfCancellationRequested);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void AScopeDisposedOutOfOrder_StopsApplying()
    {
        using CancellationTokenSource outerToken = new CancellationTokenSource();
        PdfWorkScope outer = PdfWorkScope.Enter(null, outerToken.Token);
        PdfWorkScope inner = PdfWorkScope.Enter(null);

        outer.Dispose();
        outerToken.Cancel();
        PdfWorkScope.ThrowIfCancellationRequested();
        inner.Dispose();

        PdfWorkScope.Current.Should().BeNull("both are disposed, whatever the order");
        PdfWorkScope.ThrowIfCancellationRequested();
    }

    [Fact]
    public void IsAbort_IsTrueOnlyForCancellationAndLimits()
    {
        PdfWorkScope.IsAbort(new OperationCanceledException()).Should().BeTrue();
        PdfWorkScope.IsAbort(new PdfLimitExceededException("x", 1, 2)).Should().BeTrue();
        PdfWorkScope.IsAbort(new PdfParseException("bad")).Should().BeFalse();
        PdfWorkScope.IsAbort(new InvalidOperationException()).Should().BeFalse();
    }
}
