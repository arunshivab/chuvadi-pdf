// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using Chuvadi.Benchmarks.Compression;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

/// <summary>
/// Unit coverage for the external-tool compression scoreboard that does not depend
/// on Ghostscript/qpdf/mutool being installed: argument construction, ratio math,
/// and the graceful "tool not found" path — so CI runners without the tools pass.
/// </summary>
public class ExternalToolScoreboardTests
{
    private static readonly string[] MissingToolCandidates = { "chuvadi-nonexistent-tool-zzz" };
    private static readonly string[] VersionArguments = { "--version" };
    private static readonly int[] SuccessZero = { 0 };

    [Fact]
    public void Measure_WhenToolMissing_ReportsUnavailableForEveryScenario()
    {
        ExternalTool missing = new ExternalTool(
            "ghost-tool",
            MissingToolCandidates,
            VersionArguments,
            static (input, output, _) => new List<string> { input, output },
            SuccessZero);
        IReadOnlyList<CompressionScenario> corpus = CompressionCorpus.All();

        IReadOnlyList<ExternalToolResult> results =
            ExternalToolScoreboard.Measure(new List<ExternalTool> { missing }, corpus);

        results.Should().HaveCount(corpus.Count);
        results.Should().OnlyContain(result => result.Outcome == ExternalToolOutcome.Unavailable);
        results.Should().OnlyContain(result => result.ToolName == "ghost-tool");
        results.Should().OnlyContain(result => result.InputBytes > 0);
        results.Should().OnlyContain(result => result.Ratio == 1.0);
    }

    [Fact]
    public void DefaultTools_AreGhostscriptQpdfAndMutool()
    {
        IReadOnlyList<ExternalTool> tools = ExternalToolScoreboard.DefaultTools();

        tools.Should().HaveCount(3);
        tools[0].Name.Should().Be("gs");
        tools[1].Name.Should().Be("qpdf");
        tools[2].Name.Should().Be("mutool");
    }

    [Fact]
    public void QpdfArguments_AddImageOptimizationOnlyWhenLossy()
    {
        ExternalTool qpdf = ExternalToolScoreboard.DefaultTools()[1];

        IReadOnlyList<string> lossless = qpdf.BuildArguments("in.pdf", "out.pdf", false);
        IReadOnlyList<string> lossy = qpdf.BuildArguments("in.pdf", "out.pdf", true);

        lossless.Should().NotContain("--optimize-images");
        lossy.Should().Contain("--optimize-images");
        lossless.Should().Contain("in.pdf");
        lossless.Should().Contain("out.pdf");
    }

    [Fact]
    public void GhostscriptArguments_TargetPdfwrite()
    {
        ExternalTool gs = ExternalToolScoreboard.DefaultTools()[0];

        IReadOnlyList<string> arguments = gs.BuildArguments("in.pdf", "out.pdf", false);

        arguments.Should().Contain("-sDEVICE=pdfwrite");
        arguments.Should().Contain("out.pdf");
    }

    [Fact]
    public void Ratio_IsOutputOverInput_WhenMeasured()
    {
        ExternalToolResult measured = new ExternalToolResult
        {
            ScenarioName = "s",
            ToolName = "t",
            Outcome = ExternalToolOutcome.Measured,
            InputBytes = 1000,
            OutputBytes = 250,
        };
        ExternalToolResult unavailable = new ExternalToolResult
        {
            ScenarioName = "s",
            ToolName = "t",
            Outcome = ExternalToolOutcome.Unavailable,
            InputBytes = 1000,
            OutputBytes = 0,
        };

        measured.Ratio.Should().BeApproximately(0.25, 1e-9);
        unavailable.Ratio.Should().Be(1.0);
    }
}
