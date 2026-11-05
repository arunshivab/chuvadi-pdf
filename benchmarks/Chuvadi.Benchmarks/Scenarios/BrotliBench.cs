// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2.2 stage 4 — Brotli ratio benchmark

// Fonts.Bundled and Fonts.Woff2 are deprecated (3.31.0) but still ship and are still measured.
#pragma warning disable CS0618

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using BenchmarkDotNet.Attributes;

namespace Chuvadi.Benchmarks.Scenarios;

/// <summary>
/// Measures the size of Chuvadi's Brotli output compared to <see cref="BrotliStream"/>
/// at <see cref="CompressionLevel.Optimal"/> on representative font-table-like inputs.
/// </summary>
/// <remarks>
/// Both encoders are valid Brotli; smaller output is better. The gap between Chuvadi
/// and the reference indicates how much of the spec's compression vocabulary
/// (context modeling, static dictionary, multi-block tuning) is still unimplemented.
/// This is an output-size benchmark, not a throughput benchmark; see
/// <see cref="BrotliThroughputBench"/> for encode-time measurements.
/// </remarks>
[MemoryDiagnoser]
public class BrotliRatioBench
{
    private byte[]? _data;

    [Params(
        nameof(SampleData.LoremIpsumSmall),
        nameof(SampleData.EnglishProseMedium),
        nameof(SampleData.HighlyRepetitive),
        nameof(SampleData.ModerateRepetition),
        nameof(SampleData.SfntLikeBinary),
        nameof(SampleData.RandomIncompressible))]
    public string Scenario { get; set; } = nameof(SampleData.LoremIpsumSmall);

    [GlobalSetup]
    public void Setup()
    {
        _data = SampleData.Get(Scenario);
    }

    [Benchmark(Baseline = true, Description = "Chuvadi.BrotliEncoder")]
    public int Chuvadi_EncodeSize()
    {
        byte[] encoded = Chuvadi.Pdf.Fonts.Woff2.BrotliEncoder.Encode(_data!);
        return encoded.Length;
    }

    [Benchmark(Description = "System.IO.Compression.BrotliStream Optimal")]
    public int DotNet_EncodeSize()
    {
        using MemoryStream output = new();
        using (BrotliStream bs = new(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            bs.Write(_data!, 0, _data!.Length);
        }
        return (int)output.Length;
    }

    [Benchmark(Description = "System.IO.Compression.BrotliStream Fastest")]
    public int DotNet_EncodeSize_Fastest()
    {
        using MemoryStream output = new();
        using (BrotliStream bs = new(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            bs.Write(_data!, 0, _data!.Length);
        }
        return (int)output.Length;
    }
}

/// <summary>
/// Measures how long Chuvadi takes to encode each scenario vs the .NET reference.
/// </summary>
[MemoryDiagnoser]
public class BrotliThroughputBench
{
    private byte[]? _data;

    [Params(
        nameof(SampleData.LoremIpsumSmall),
        nameof(SampleData.EnglishProseMedium),
        nameof(SampleData.SfntLikeBinary))]
    public string Scenario { get; set; } = nameof(SampleData.LoremIpsumSmall);

    [GlobalSetup]
    public void Setup()
    {
        _data = SampleData.Get(Scenario);
    }

    [Benchmark(Baseline = true, Description = "Chuvadi encode")]
    public byte[] Chuvadi_Encode()
    {
        return Chuvadi.Pdf.Fonts.Woff2.BrotliEncoder.Encode(_data!);
    }

    [Benchmark(Description = ".NET BrotliStream Optimal encode")]
    public byte[] DotNet_EncodeOptimal()
    {
        using MemoryStream output = new();
        using (BrotliStream bs = new(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            bs.Write(_data!, 0, _data!.Length);
        }
        return output.ToArray();
    }

    [Benchmark(Description = ".NET BrotliStream Fastest encode")]
    public byte[] DotNet_EncodeFastest()
    {
        using MemoryStream output = new();
        using (BrotliStream bs = new(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            bs.Write(_data!, 0, _data!.Length);
        }
        return output.ToArray();
    }
}

/// <summary>Sample inputs that exercise typical compression scenarios.</summary>
internal static class SampleData
{
    private static readonly Dictionary<string, Lazy<byte[]>> Cache = new()
    {
        [nameof(LoremIpsumSmall)] = new(LoremIpsumSmall),
        [nameof(EnglishProseMedium)] = new(EnglishProseMedium),
        [nameof(HighlyRepetitive)] = new(HighlyRepetitive),
        [nameof(ModerateRepetition)] = new(ModerateRepetition),
        [nameof(SfntLikeBinary)] = new(SfntLikeBinary),
        [nameof(RandomIncompressible)] = new(RandomIncompressible),
    };

    internal static byte[] Get(string name) => Cache[name].Value;

    internal static byte[] LoremIpsumSmall()
    {
        string phrase = "Lorem ipsum dolor sit amet, consectetur adipiscing elit. ";
        return System.Text.Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat(phrase, 18)));
    }

    internal static byte[] EnglishProseMedium()
    {
        string phrase = "The quick brown fox jumps over the lazy dog. ";
        return System.Text.Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat(phrase, 110)));
    }

    internal static byte[] HighlyRepetitive()
    {
        byte[] data = new byte[1024];
        for (int i = 0; i < data.Length; i++) { data[i] = (byte)('A' + (i % 4)); }
        return data;
    }

    internal static byte[] ModerateRepetition()
    {
        byte[] data = new byte[1024];
        for (int i = 0; i < data.Length; i++) { data[i] = (byte)('A' + (i % 20)); }
        return data;
    }

    /// <summary>
    /// SFNT-table-like binary: mostly-zero padding interspersed with small ASCII strings,
    /// imitating the structure of a TTF/OTF table such as <c>name</c> or <c>post</c>.
    /// </summary>
    internal static byte[] SfntLikeBinary()
    {
        byte[] data = new byte[4096];
        // Big-endian-ish length-prefixed mixed binary/ASCII.
        Random rnd = new(20260519);
        rnd.NextBytes(data);
        // Stomp in some ASCII font-name fragments to simulate real table content.
        string[] names = { "Regular", "Italic", "Bold", "BoldItalic", "Roboto", "OpenSans", "Inter", "Helvetica" };
        for (int i = 0; i < 32; i++)
        {
            string n = names[rnd.Next(names.Length)];
            byte[] bytes = System.Text.Encoding.ASCII.GetBytes(n);
            int offset = rnd.Next(data.Length - bytes.Length);
            Array.Copy(bytes, 0, data, offset, bytes.Length);
        }
        return data;
    }

    internal static byte[] RandomIncompressible()
    {
        byte[] data = new byte[8192];
        new Random(20260519).NextBytes(data);
        return data;
    }
}
