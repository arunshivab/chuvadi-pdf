// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.18 — automated benchmark regression diffing.
//
// Extends the compression-ratio baseline pattern to wall-clock timings: a
// small deterministic micro-suite (open, rasterize, SVG render, compress —
// all on the synthetic compression corpus, so no external corpus is needed)
// is timed, and each median is compared against the committed baseline with a
// generous multiplicative tolerance. The gate is meant to catch pathological
// regressions (an accidental O(n²), a dropped cache), not single-digit
// percentage noise — CI runners vary too much for that — which is why the
// matching CI job is non-required.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Chuvadi.Benchmarks.Compression;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Operations;
using Chuvadi.Pdf.Rendering;
using Chuvadi.Pdf.Svg;

namespace Chuvadi.Benchmarks;

/// <summary>One timed scenario: a stable name and the median wall time.</summary>
public sealed record PerfMeasurement(string Name, double MedianMs);

/// <summary>Runs the perf micro-suite over the synthetic corpus.</summary>
public static class PerfMeasure
{
    private const int Iterations = 5;

    /// <summary>Times every scenario and returns the medians, in a stable order.</summary>
    public static IReadOnlyList<PerfMeasurement> MeasureAll()
    {
        byte[] pdf = FindMixedScenario();

        List<PerfMeasurement> results = new List<PerfMeasurement>
        {
            Time("open-mixed", () =>
            {
                using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
                _ = doc.PageCount;
            }),
            Time("rasterize-mixed-72dpi", () =>
            {
                using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
                PageRasterizer rasterizer = new PageRasterizer(
                    doc.Objects, new RenderOptions { Dpi = 72 });
                _ = rasterizer.Rasterize(doc.Pages[0]);
            }),
            Time("svg-mixed", () =>
            {
                using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
                _ = new SvgRenderer().RenderPage(doc, 0);
            }),
            Time("compress-mixed", () =>
            {
                using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
                using MemoryStream output = new MemoryStream();
                PdfCompressor.Compress(doc, output, new CompressionOptions());
            }),
        };

        return results;
    }

    private static byte[] FindMixedScenario()
    {
        foreach (CompressionScenario scenario in CompressionCorpus.All())
        {
            if (scenario.Name == "mixed")
            {
                return scenario.Pdf;
            }
        }

        throw new InvalidOperationException("The 'mixed' corpus scenario is missing.");
    }

    private static PerfMeasurement Time(string name, Action action)
    {
        // One warm-up (JIT, caches), then the median of the timed iterations.
        action();

        double[] samples = new double[Iterations];
        for (int i = 0; i < Iterations; i++)
        {
            Stopwatch sw = Stopwatch.StartNew();
            action();
            sw.Stop();
            samples[i] = sw.Elapsed.TotalMilliseconds;
        }

        Array.Sort(samples);
        return new PerfMeasurement(name, samples[Iterations / 2]);
    }
}

/// <summary>
/// The committed timing baseline: per-scenario median milliseconds plus a
/// multiplicative tolerance. A fresh measurement regresses when it exceeds
/// baseline × tolerance.
/// </summary>
public sealed class PerfBaseline
{
    private const string ResourceSuffix = "perf-baseline.json";

    private PerfBaseline(double toleranceFactor, IReadOnlyDictionary<string, double> scenarios)
    {
        ToleranceFactor = toleranceFactor;
        Scenarios = scenarios;
    }

    /// <summary>Multiplicative tolerance applied to every comparison.</summary>
    public double ToleranceFactor { get; }

    /// <summary>Per-scenario baseline medians (ms), keyed by scenario name.</summary>
    public IReadOnlyDictionary<string, double> Scenarios { get; }

    /// <summary>Loads the baseline embedded in this assembly.</summary>
    public static PerfBaseline Load()
    {
        Assembly assembly = typeof(PerfBaseline).Assembly;
        string? resourceName = null;
        foreach (string candidate in assembly.GetManifestResourceNames())
        {
            if (candidate.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            {
                resourceName = candidate;
                break;
            }
        }

        if (resourceName is null)
        {
            return new PerfBaseline(3.0, new Dictionary<string, double>());
        }

        using Stream stream = assembly.GetManifestResourceStream(resourceName)!;
        using JsonDocument json = JsonDocument.Parse(stream);
        JsonElement root = json.RootElement;

        double tolerance = root.TryGetProperty("toleranceFactor", out JsonElement t)
            ? t.GetDouble()
            : 3.0;

        Dictionary<string, double> scenarios = new Dictionary<string, double>(StringComparer.Ordinal);
        if (root.TryGetProperty("scenarios", out JsonElement s))
        {
            foreach (JsonProperty entry in s.EnumerateObject())
            {
                scenarios[entry.Name] = entry.Value.GetProperty("medianMs").GetDouble();
            }
        }

        return new PerfBaseline(tolerance, scenarios);
    }

    /// <summary>
    /// Returns the regressions in <paramref name="measurements"/> against this
    /// baseline (empty = all good). A scenario missing from the baseline is
    /// reported, so new scenarios force a baseline refresh.
    /// </summary>
    public IReadOnlyList<string> FindRegressions(IReadOnlyList<PerfMeasurement> measurements)
    {
        ArgumentNullException.ThrowIfNull(measurements);

        List<string> regressions = new List<string>();
        foreach (PerfMeasurement measurement in measurements)
        {
            if (!Scenarios.TryGetValue(measurement.Name, out double baselineMs))
            {
                regressions.Add(
                    $"{measurement.Name}: no baseline entry — run --update-perf-baseline.");
                continue;
            }

            double limit = baselineMs * ToleranceFactor;
            if (measurement.MedianMs > limit)
            {
                regressions.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: median {1:F2} ms exceeds baseline {2:F2} ms × {3:F1} = {4:F2} ms",
                    measurement.Name, measurement.MedianMs, baselineMs, ToleranceFactor, limit));
            }
        }

        return regressions;
    }

    /// <summary>Serialises fresh measurements as the new baseline JSON.</summary>
    public static string ToJson(IReadOnlyList<PerfMeasurement> measurements, double toleranceFactor)
    {
        ArgumentNullException.ThrowIfNull(measurements);

        StringBuilder sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append(string.Format(
            CultureInfo.InvariantCulture, "  \"toleranceFactor\": {0:F1},\n", toleranceFactor));
        sb.Append("  \"scenarios\": {\n");
        for (int i = 0; i < measurements.Count; i++)
        {
            PerfMeasurement m = measurements[i];
            sb.Append(string.Format(
                CultureInfo.InvariantCulture,
                "    \"{0}\": {{ \"medianMs\": {1:F3} }}{2}\n",
                m.Name, m.MedianMs, i < measurements.Count - 1 ? "," : string.Empty));
        }

        sb.Append("  }\n");
        sb.Append("}\n");
        return sb.ToString();
    }
}

/// <summary>
/// Prints the perf report and regenerates the committed baseline. Invoked
/// from <see cref="Program"/> via <c>--perf-report</c> and
/// <c>--update-perf-baseline [path]</c>.
/// </summary>
internal static class PerfReport
{
    private const string DefaultBaselinePath =
        "benchmarks/Chuvadi.Benchmarks/perf-baseline.json";

    public static int Print()
    {
        IReadOnlyList<PerfMeasurement> measurements = PerfMeasure.MeasureAll();

        Console.WriteLine($"{"scenario",-24} {"median ms",12}");
        foreach (PerfMeasurement measurement in measurements)
        {
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0,-24} {1,12:F3}", measurement.Name, measurement.MedianMs));
        }

        PerfBaseline baseline = PerfBaseline.Load();
        IReadOnlyList<string> regressions = baseline.FindRegressions(measurements);

        Console.WriteLine();
        if (regressions.Count == 0)
        {
            Console.WriteLine("No perf regressions against the committed baseline.");
            return 0;
        }

        Console.WriteLine($"{regressions.Count} perf regression(s) against the committed baseline:");
        foreach (string regression in regressions)
        {
            Console.WriteLine("  " + regression);
        }

        return 1;
    }

    public static void UpdateBaseline(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        IReadOnlyList<PerfMeasurement> measurements = PerfMeasure.MeasureAll();
        PerfBaseline current = PerfBaseline.Load();
        string json = PerfBaseline.ToJson(measurements, current.ToleranceFactor);
        File.WriteAllText(path, json);
        Console.WriteLine($"Wrote perf baseline ({measurements.Count} scenarios) to {path}");
    }

    public static string ResolvePath(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args.Length > 1 ? args[1] : DefaultBaselinePath;
    }
}
