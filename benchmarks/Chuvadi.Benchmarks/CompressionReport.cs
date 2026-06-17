// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Globalization;
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

    /// <summary>
    /// Prints Chuvadi's per-scenario compression ratio alongside the ratios achieved
    /// by external tools (Ghostscript, qpdf, mutool) on the same corpus. Tools that
    /// are not installed show "n/a" and are noted as unavailable in the footer, so
    /// the scoreboard is informative on any machine.
    /// </summary>
    public static int PrintScoreboard()
    {
        IReadOnlyList<CompressionMeasurement> chuvadi = CompressionMeasure.MeasureAll();
        IReadOnlyList<ExternalToolResult> external = ExternalToolScoreboard.Measure();

        List<string> toolNames = new List<string>();
        foreach (ExternalToolResult result in external)
        {
            if (!toolNames.Contains(result.ToolName))
            {
                toolNames.Add(result.ToolName);
            }
        }

        Console.Write($"{"scenario",-20} {"input",10} {"chuvadi",9}");
        foreach (string toolName in toolNames)
        {
            Console.Write($" {toolName,9}");
        }

        Console.WriteLine();

        foreach (CompressionMeasurement measurement in chuvadi)
        {
            Console.Write(
                $"{measurement.Name,-20} {measurement.InputBytes,10} {measurement.Ratio,9:F4}");
            foreach (string toolName in toolNames)
            {
                ExternalToolResult? result = FindResult(external, measurement.Name, toolName);
                string cell = result is { Outcome: ExternalToolOutcome.Measured }
                    ? result.Ratio.ToString("F4", CultureInfo.InvariantCulture)
                    : "n/a";
                Console.Write($" {cell,9}");
            }

            Console.WriteLine();
        }

        PrintScoreboardFooter(external, toolNames);
        return 0;
    }

    private static void PrintScoreboardFooter(
        IReadOnlyList<ExternalToolResult> external, IReadOnlyList<string> toolNames)
    {
        Console.WriteLine();
        Console.WriteLine("Ratio = output / input (lower is better). 'n/a' = tool unavailable or failed.");
        foreach (string toolName in toolNames)
        {
            string state = StateFor(external, toolName);
            Console.WriteLine($"  {toolName,-8} {state}");
        }

        Console.WriteLine();
        Console.WriteLine(
            "Chuvadi ratios use the in-process compressor (lossy image recompression on");
        Console.WriteLine(
            "image scenarios). External tools use their standard optimize/clean modes;");
        Console.WriteLine(
            "Ghostscript recompresses images, while qpdf and mutool stay lossless on them.");
    }

    private static ExternalToolResult? FindResult(
        IReadOnlyList<ExternalToolResult> external, string scenario, string toolName)
    {
        foreach (ExternalToolResult result in external)
        {
            if (result.ScenarioName == scenario && result.ToolName == toolName)
            {
                return result;
            }
        }

        return null;
    }

    private static string StateFor(IReadOnlyList<ExternalToolResult> external, string toolName)
    {
        bool anyMeasured = false;
        bool anyAttempted = false;
        foreach (ExternalToolResult result in external)
        {
            if (result.ToolName != toolName)
            {
                continue;
            }

            if (result.Outcome == ExternalToolOutcome.Measured)
            {
                anyMeasured = true;
            }

            if (result.Outcome != ExternalToolOutcome.Unavailable)
            {
                anyAttempted = true;
            }
        }

        if (anyMeasured)
        {
            return "available";
        }

        return anyAttempted ? "found but failed" : "not installed";
    }
}
