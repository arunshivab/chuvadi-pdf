// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 0 — compression measurement foundations

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Operations;

namespace Chuvadi.Benchmarks.Compression;

/// <summary>One scenario's measured compression outcome.</summary>
public sealed record CompressionMeasurement
{
    /// <summary>Scenario name (baseline key).</summary>
    public required string Name { get; init; }

    /// <summary>Input PDF size in bytes.</summary>
    public required long InputBytes { get; init; }

    /// <summary>Output PDF size in bytes (equal to input when skipped).</summary>
    public required long OutputBytes { get; init; }

    /// <summary>Post-recompression image quality (1.0 for lossless scenarios).</summary>
    public required double Quality { get; init; }

    /// <summary>Whether a safety guard skipped the rewrite.</summary>
    public required bool Skipped { get; init; }

    /// <summary>Output-to-input size ratio (lower is better).</summary>
    public double Ratio => InputBytes == 0 ? 1.0 : (double)OutputBytes / InputBytes;
}

/// <summary>
/// Runs <see cref="PdfCompressor"/> over the corpus and reports size ratio and
/// (for lossy scenarios) image quality. Shared by the BenchmarkDotNet timing
/// scenario, the report generator, and the CI ratio-regression test.
/// </summary>
public static class CompressionMeasure
{
    /// <summary>Measures one scenario.</summary>
    /// <param name="scenario">The scenario to compress and measure.</param>
    public static CompressionMeasurement Measure(CompressionScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        using MemoryStream input = new MemoryStream(scenario.Pdf);
        using PdfDocument document = PdfDocument.Open(input, leaveOpen: true);

        CompressionOptions options = new CompressionOptions { RecompressImages = scenario.Lossy };
        using MemoryStream output = new MemoryStream();
        CompressionResult result = PdfCompressor.Compress(document, output, options);

        double quality = 1.0;
        if (scenario.Lossy && scenario.ImageRgb is not null)
        {
            quality = Ssim.RoundTripQuality(
                scenario.ImageRgb, scenario.ImageWidth, scenario.ImageHeight, options.JpegQuality);
        }

        return new CompressionMeasurement
        {
            Name = scenario.Name,
            InputBytes = scenario.Pdf.Length,
            OutputBytes = result.Skipped ? scenario.Pdf.Length : output.Length,
            Quality = quality,
            Skipped = result.Skipped,
        };
    }

    /// <summary>Measures every corpus scenario in order.</summary>
    public static IReadOnlyList<CompressionMeasurement> MeasureAll()
    {
        List<CompressionMeasurement> results = new List<CompressionMeasurement>();
        foreach (CompressionScenario scenario in CompressionCorpus.All())
        {
            results.Add(Measure(scenario));
        }

        return results;
    }
}
