// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using System.IO;
using BenchmarkDotNet.Attributes;
using Chuvadi.Benchmarks.Compression;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Operations;

namespace Chuvadi.Benchmarks.Scenarios;

/// <summary>
/// Compression throughput benchmark: opening and rewriting the synthetic
/// compression corpus, lossless and with lossy image recompression. This is the
/// per-release speed signal that complements the ratio-regression baseline
/// enforced in the test suite. Run with
/// <c>dotnet run -c Release -- --filter *Compression*</c>.
/// </summary>
[MemoryDiagnoser]
public class CompressionBench
{
    private IReadOnlyList<CompressionScenario> _corpus = System.Array.Empty<CompressionScenario>();

    [GlobalSetup]
    public void Setup()
    {
        _corpus = CompressionCorpus.All();
    }

    [Benchmark(Baseline = true, Description = "Compress corpus (lossless)")]
    public long CompressLossless()
    {
        return CompressCorpus(recompressImages: false);
    }

    [Benchmark(Description = "Compress corpus (lossy images)")]
    public long CompressLossy()
    {
        return CompressCorpus(recompressImages: true);
    }

    private long CompressCorpus(bool recompressImages)
    {
        long total = 0;
        foreach (CompressionScenario scenario in _corpus)
        {
            using MemoryStream input = new MemoryStream(scenario.Pdf);
            using PdfDocument document = PdfDocument.Open(input, leaveOpen: true);
            CompressionOptions options = new CompressionOptions { RecompressImages = recompressImages };
            using MemoryStream output = new MemoryStream();
            PdfCompressor.Compress(document, output, options);
            total += output.Length;
        }

        return total;
    }
}
