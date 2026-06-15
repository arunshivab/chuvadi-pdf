// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 0 — compression measurement foundations

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Chuvadi.Benchmarks.Compression;

/// <summary>A committed baseline target for one scenario.</summary>
public sealed record CompressionBaselineEntry
{
    /// <summary>Expected output-to-input size ratio.</summary>
    public required double Ratio { get; init; }

    /// <summary>Expected lossy image quality (SSIM); 1.0 for lossless scenarios.</summary>
    public required double Quality { get; init; }
}

/// <summary>
/// The committed compression baseline: per-scenario ratio and quality targets
/// plus an absolute tolerance. <see cref="FindRegressions"/> turns a fresh set
/// of measurements into a list of human-readable regressions (empty = all good),
/// which the CI ratio-regression test asserts is empty. <see cref="ToJson"/>
/// regenerates the file after an intended improvement.
/// </summary>
public sealed class CompressionBaseline
{
    private const string ResourceSuffix = "compression-baseline.json";

    private CompressionBaseline(double tolerance, IReadOnlyDictionary<string, CompressionBaselineEntry> scenarios)
    {
        Tolerance = tolerance;
        Scenarios = scenarios;
    }

    /// <summary>Absolute tolerance applied to ratio and quality comparisons.</summary>
    public double Tolerance { get; }

    /// <summary>Per-scenario baseline targets, keyed by scenario name.</summary>
    public IReadOnlyDictionary<string, CompressionBaselineEntry> Scenarios { get; }

    /// <summary>Loads the baseline embedded in this assembly.</summary>
    public static CompressionBaseline Load()
    {
        Assembly assembly = typeof(CompressionBaseline).Assembly;
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
            throw new InvalidOperationException("Embedded compression baseline not found.");
        }

        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("Embedded compression baseline stream is null.");
        using StreamReader reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>Parses a baseline from its JSON text.</summary>
    /// <param name="json">The baseline JSON.</param>
    public static CompressionBaseline Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        double tolerance = root.TryGetProperty("tolerance", out JsonElement toleranceElement)
            ? toleranceElement.GetDouble()
            : 0.02;

        Dictionary<string, CompressionBaselineEntry> scenarios = new Dictionary<string, CompressionBaselineEntry>(StringComparer.Ordinal);
        if (root.TryGetProperty("scenarios", out JsonElement scenariosElement))
        {
            foreach (JsonProperty property in scenariosElement.EnumerateObject())
            {
                scenarios[property.Name] = new CompressionBaselineEntry
                {
                    Ratio = property.Value.GetProperty("ratio").GetDouble(),
                    Quality = property.Value.GetProperty("quality").GetDouble(),
                };
            }
        }

        return new CompressionBaseline(tolerance, scenarios);
    }

    /// <summary>
    /// Compares <paramref name="measurements"/> against this baseline and returns
    /// one message per regression: a missing baseline entry, an unexpected guard
    /// skip, a ratio that grew beyond tolerance, or a quality that dropped beyond
    /// tolerance. An empty list means no regression.
    /// </summary>
    /// <param name="measurements">The fresh measurements to check.</param>
    public IReadOnlyList<string> FindRegressions(IReadOnlyList<CompressionMeasurement> measurements)
    {
        ArgumentNullException.ThrowIfNull(measurements);

        List<string> regressions = new List<string>();
        foreach (CompressionMeasurement measurement in measurements)
        {
            if (measurement.Skipped)
            {
                regressions.Add($"{measurement.Name}: rewrite was unexpectedly skipped.");
                continue;
            }

            if (!Scenarios.TryGetValue(measurement.Name, out CompressionBaselineEntry? entry))
            {
                regressions.Add($"{measurement.Name}: no baseline entry (regenerate the baseline).");
                continue;
            }

            if (measurement.Ratio > entry.Ratio + Tolerance)
            {
                regressions.Add(
                    $"{measurement.Name}: ratio {measurement.Ratio:F4} exceeds baseline {entry.Ratio:F4} + {Tolerance:F4}.");
            }

            if (measurement.Quality < entry.Quality - Tolerance)
            {
                regressions.Add(
                    $"{measurement.Name}: quality {measurement.Quality:F4} below baseline {entry.Quality:F4} - {Tolerance:F4}.");
            }
        }

        return regressions;
    }

    /// <summary>
    /// Serialises <paramref name="measurements"/> as a baseline JSON document with
    /// the given tolerance. Used to regenerate the committed baseline after an
    /// intended compression improvement.
    /// </summary>
    /// <param name="measurements">The measurements to record.</param>
    /// <param name="tolerance">The absolute tolerance to record.</param>
    public static string ToJson(IReadOnlyList<CompressionMeasurement> measurements, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(measurements);

        StringBuilder builder = new StringBuilder();
        builder.Append("{\n");
        builder.Append(
            string.Create(CultureInfo.InvariantCulture, $"  \"tolerance\": {tolerance:0.####},\n"));
        builder.Append("  \"scenarios\": {\n");
        for (int i = 0; i < measurements.Count; i++)
        {
            CompressionMeasurement measurement = measurements[i];
            string comma = i < measurements.Count - 1 ? "," : "";
            builder.Append(string.Create(
                CultureInfo.InvariantCulture,
                $"    \"{measurement.Name}\": {{ \"ratio\": {measurement.Ratio:0.####}, \"quality\": {measurement.Quality:0.####} }}{comma}\n"));
        }

        builder.Append("  }\n");
        builder.Append("}\n");
        return builder.ToString();
    }
}
