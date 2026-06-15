// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using Chuvadi.Benchmarks.Compression;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

/// <summary>
/// CI gate that locks compression effectiveness. Compresses the synthetic corpus
/// and asserts that no scenario's size ratio grew, nor its image quality dropped,
/// beyond tolerance versus the committed baseline. When a change intentionally
/// improves compression, regenerate the baseline with
/// <c>dotnet run --project benchmarks/Chuvadi.Benchmarks -c Release -- --update-compression-baseline</c>.
/// </summary>
public class CompressionRatioBaselineTests
{
    [Fact]
    public void Corpus_CompressesWithinCommittedBaseline()
    {
        CompressionBaseline baseline = CompressionBaseline.Load();
        IReadOnlyList<CompressionMeasurement> measurements = CompressionMeasure.MeasureAll();

        IReadOnlyList<string> regressions = baseline.FindRegressions(measurements);

        regressions.Should().BeEmpty(
            "compression must not regress against the committed baseline; "
            + "regenerate it with --update-compression-baseline after an intended improvement");
    }

    [Fact]
    public void Corpus_IsNonTrivial()
    {
        // Guards against the corpus silently degenerating to empty/no-op, which
        // would make the regression gate meaningless.
        IReadOnlyList<CompressionScenario> corpus = CompressionCorpus.All();

        corpus.Should().HaveCountGreaterThan(2);
        corpus.Should().OnlyContain(scenario => scenario.Pdf.Length > 0);
    }
}
