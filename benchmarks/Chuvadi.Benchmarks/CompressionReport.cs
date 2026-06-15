// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Benchmarks.Compression;

namespace Chuvadi.Benchmarks;

/// <summary>
/// Prints the compression ratio/quality report and regenerates the committed
/// baseline. Invoked from <see cref="Program"/> via <c>--compression-report</c>
/// and <c>--update-compression-baseline [path]</c>.
/// </summary>
internal static class CompressionReport
{
    private const string DefaultBaselinePath =
        "benchmarks/Chuvadi.Benchmarks.Compression/compression-baseline.json";

    public static int Print()
    {
        IReadOnlyList<CompressionMeasurement> measurements = CompressionMeasure.MeasureAll();

        Console.WriteLine(
            $"{"scenario",-20} {"input",10} {"output",10} {"ratio",8} {"quality",8} {"skipped",8}");
        foreach (CompressionMeasurement measurement in measurements)
        {
            Console.WriteLine(
                $"{measurement.Name,-20} {measurement.InputBytes,10} {measurement.OutputBytes,10} " +
                $"{measurement.Ratio,8:F4} {measurement.Quality,8:F4} {measurement.Skipped,8}");
        }

        CompressionBaseline baseline = CompressionBaseline.Load();
        IReadOnlyList<string> regressions = baseline.FindRegressions(measurements);

        Console.WriteLine();
        if (regressions.Count == 0)
        {
            Console.WriteLine("No regressions against the committed baseline.");
            return 0;
        }

        Console.WriteLine($"{regressions.Count} regression(s) against the committed baseline:");
        foreach (string regression in regressions)
        {
            Console.WriteLine("  " + regression);
        }

        return 1;
    }

    public static void UpdateBaseline(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        IReadOnlyList<CompressionMeasurement> measurements = CompressionMeasure.MeasureAll();
        CompressionBaseline current = CompressionBaseline.Load();
        string json = CompressionBaseline.ToJson(measurements, current.Tolerance);
        File.WriteAllText(path, json);
        Console.WriteLine($"Wrote baseline ({measurements.Count} scenarios) to {path}");
    }

    public static string ResolveBaselinePath(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args.Length > 1 ? args[1] : DefaultBaselinePath;
    }
}
