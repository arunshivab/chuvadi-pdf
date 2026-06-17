// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 0 — compression measurement foundations (external-tool scoreboard)

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace Chuvadi.Benchmarks.Compression;

/// <summary>The outcome of attempting to measure one external tool on one scenario.</summary>
public enum ExternalToolOutcome
{
    /// <summary>The tool ran and produced a valid PDF whose size was measured.</summary>
    Measured,

    /// <summary>The tool executable was not found on this machine.</summary>
    Unavailable,

    /// <summary>The tool was found but did not produce a valid PDF.</summary>
    Failed,
}

/// <summary>One external tool's measured outcome for one corpus scenario.</summary>
public sealed record ExternalToolResult
{
    /// <summary>The scenario's stable name.</summary>
    public required string ScenarioName { get; init; }

    /// <summary>The external tool's display name.</summary>
    public required string ToolName { get; init; }

    /// <summary>What happened when the tool was run.</summary>
    public required ExternalToolOutcome Outcome { get; init; }

    /// <summary>Input PDF size in bytes.</summary>
    public required long InputBytes { get; init; }

    /// <summary>
    /// Output PDF size in bytes; 0 unless <see cref="Outcome"/> is
    /// <see cref="ExternalToolOutcome.Measured"/>.
    /// </summary>
    public required long OutputBytes { get; init; }

    /// <summary>An optional human-readable detail (skip reason or error).</summary>
    public string? Detail { get; init; }

    /// <summary>
    /// Output-to-input size ratio (lower is better); 1.0 when the tool did not
    /// produce a measured result.
    /// </summary>
    public double Ratio => Outcome != ExternalToolOutcome.Measured || InputBytes == 0
        ? 1.0
        : (double)OutputBytes / InputBytes;
}

/// <summary>
/// Describes an external command-line PDF tool: how to locate its executable, how
/// to probe its version, and how to build its compression command for a scenario.
/// </summary>
public sealed class ExternalTool
{
    /// <summary>Creates an external tool descriptor.</summary>
    /// <param name="name">Display name used in the scoreboard.</param>
    /// <param name="executableCandidates">
    /// Candidate executable names, tried in order (covers cross-platform naming such
    /// as <c>gs</c> versus <c>gswin64c</c>).
    /// </param>
    /// <param name="versionArguments">Arguments that make the tool print its version and exit.</param>
    /// <param name="buildArguments">
    /// Builds the compression command arguments for (input path, output path, lossy).
    /// </param>
    /// <param name="successExitCodes">Exit codes that indicate the tool produced usable output.</param>
    public ExternalTool(
        string name,
        IReadOnlyList<string> executableCandidates,
        IReadOnlyList<string> versionArguments,
        Func<string, string, bool, IReadOnlyList<string>> buildArguments,
        IReadOnlyCollection<int> successExitCodes)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(executableCandidates);
        ArgumentNullException.ThrowIfNull(versionArguments);
        ArgumentNullException.ThrowIfNull(buildArguments);
        ArgumentNullException.ThrowIfNull(successExitCodes);

        Name = name;
        ExecutableCandidates = executableCandidates;
        VersionArguments = versionArguments;
        BuildArguments = buildArguments;
        SuccessExitCodes = successExitCodes;
    }

    /// <summary>Display name used in the scoreboard.</summary>
    public string Name { get; }

    /// <summary>Candidate executable names, tried in order.</summary>
    public IReadOnlyList<string> ExecutableCandidates { get; }

    /// <summary>Arguments that make the tool print its version and exit.</summary>
    public IReadOnlyList<string> VersionArguments { get; }

    /// <summary>Builds the compression command arguments for (input path, output path, lossy).</summary>
    public Func<string, string, bool, IReadOnlyList<string>> BuildArguments { get; }

    /// <summary>Exit codes that indicate the tool produced usable output.</summary>
    public IReadOnlyCollection<int> SuccessExitCodes { get; }
}

/// <summary>
/// Runs external command-line PDF tools (Ghostscript, qpdf, mutool) over the
/// synthetic compression corpus and reports each tool's output-size ratio, so
/// Chuvadi's own compression can be measured against real-world baselines. Tools
/// that are not installed are reported as
/// <see cref="ExternalToolOutcome.Unavailable"/> rather than failing the run, so the
/// scoreboard degrades gracefully on machines that have only some (or none) of them.
/// </summary>
public static class ExternalToolScoreboard
{
    private const int ProbeTimeoutMs = 15000;
    private const int RunTimeoutMs = 60000;

    private static readonly string[] GhostscriptCandidates = { "gs", "gswin64c", "gswin32c" };
    private static readonly string[] QpdfCandidates = { "qpdf" };
    private static readonly string[] MutoolCandidates = { "mutool", "mutool.exe" };
    private static readonly string[] VersionFlagLong = { "--version" };
    private static readonly string[] VersionFlagShort = { "-v" };
    private static readonly int[] ExitZero = { 0 };
    private static readonly int[] ExitZeroOrWarning = { 0, 3 };

    /// <summary>
    /// The default external tools, each with a verified standard "optimize/clean"
    /// invocation. Ghostscript re-encodes through <c>pdfwrite</c> (and so recompresses
    /// images); qpdf and mutool are lossless on image data unless their lossy flag is
    /// enabled.
    /// </summary>
    public static IReadOnlyList<ExternalTool> DefaultTools()
    {
        return new List<ExternalTool>
        {
            new ExternalTool("gs", GhostscriptCandidates, VersionFlagLong, BuildGhostscriptArguments, ExitZero),
            new ExternalTool("qpdf", QpdfCandidates, VersionFlagLong, BuildQpdfArguments, ExitZeroOrWarning),
            new ExternalTool("mutool", MutoolCandidates, VersionFlagShort, BuildMutoolArguments, ExitZero),
        };
    }

    /// <summary>Measures the default tools over the full synthetic corpus.</summary>
    public static IReadOnlyList<ExternalToolResult> Measure()
    {
        return Measure(DefaultTools(), CompressionCorpus.All());
    }

    /// <summary>Measures the given tools over the given corpus, one result per (tool, scenario).</summary>
    /// <param name="tools">The external tools to run.</param>
    /// <param name="corpus">The scenarios to compress with each tool.</param>
    public static IReadOnlyList<ExternalToolResult> Measure(
        IReadOnlyList<ExternalTool> tools, IReadOnlyList<CompressionScenario> corpus)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(corpus);

        List<ExternalToolResult> results = new List<ExternalToolResult>();
        foreach (ExternalTool tool in tools)
        {
            string? executable = ResolveExecutable(tool);
            foreach (CompressionScenario scenario in corpus)
            {
                results.Add(MeasureOne(tool, executable, scenario));
            }
        }

        return results;
    }

    private static IReadOnlyList<string> BuildGhostscriptArguments(string input, string output, bool lossy)
    {
        // pdfwrite re-encodes the whole document and inherently recompresses images,
        // so the lossy flag does not change the invocation.
        _ = lossy;
        return new List<string>
        {
            "-q",
            "-dNOPAUSE",
            "-dBATCH",
            "-dSAFER",
            "-sDEVICE=pdfwrite",
            "-dCompatibilityLevel=1.5",
            "-o",
            output,
            input,
        };
    }

    private static IReadOnlyList<string> BuildQpdfArguments(string input, string output, bool lossy)
    {
        List<string> arguments = new List<string>
        {
            "--object-streams=generate",
            "--compress-streams=y",
            "--recompress-flate",
        };
        if (lossy)
        {
            arguments.Add("--optimize-images");
        }

        arguments.Add(input);
        arguments.Add(output);
        return arguments;
    }

    private static IReadOnlyList<string> BuildMutoolArguments(string input, string output, bool lossy)
    {
        // 'mutool clean' is lossless and does not recompress image data, so the lossy
        // flag does not change the invocation.
        _ = lossy;
        return new List<string>
        {
            "clean",
            "-z",
            "-ggg",
            input,
            output,
        };
    }

    private static string? ResolveExecutable(ExternalTool tool)
    {
        foreach (string candidate in tool.ExecutableCandidates)
        {
            if (CanRun(candidate, tool.VersionArguments))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool CanRun(string executable, IReadOnlyList<string> versionArguments)
    {
        try
        {
            using Process process = CreateProcess(executable, versionArguments);
            process.Start();
            DrainAndWait(process, ProbeTimeoutMs);
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static ExternalToolResult MeasureOne(ExternalTool tool, string? executable, CompressionScenario scenario)
    {
        long inputBytes = scenario.Pdf.Length;
        if (executable is null)
        {
            return new ExternalToolResult
            {
                ScenarioName = scenario.Name,
                ToolName = tool.Name,
                Outcome = ExternalToolOutcome.Unavailable,
                InputBytes = inputBytes,
                OutputBytes = 0,
                Detail = "executable not found",
            };
        }

        string workDir = Path.Combine(
            Path.GetTempPath(), "chuvadi-scoreboard-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(workDir);
            string inputPath = Path.Combine(workDir, "input.pdf");
            string outputPath = Path.Combine(workDir, "output.pdf");
            File.WriteAllBytes(inputPath, scenario.Pdf);

            IReadOnlyList<string> arguments = tool.BuildArguments(inputPath, outputPath, scenario.Lossy);

            int? exitCode;
            try
            {
                using Process process = CreateProcess(executable, arguments);
                process.Start();
                exitCode = DrainAndWait(process, RunTimeoutMs);
            }
            catch (Win32Exception ex)
            {
                return Failed(tool, scenario, inputBytes, "could not start: " + ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return Failed(tool, scenario, inputBytes, "could not run: " + ex.Message);
            }

            if (exitCode is null)
            {
                return Failed(tool, scenario, inputBytes, "timed out");
            }

            if (!ContainsCode(tool.SuccessExitCodes, exitCode.Value))
            {
                return Failed(
                    tool, scenario, inputBytes,
                    "exit code " + exitCode.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (!IsValidPdf(outputPath, out long outputBytes))
            {
                return Failed(tool, scenario, inputBytes, "output is not a valid PDF");
            }

            return new ExternalToolResult
            {
                ScenarioName = scenario.Name,
                ToolName = tool.Name,
                Outcome = ExternalToolOutcome.Measured,
                InputBytes = inputBytes,
                OutputBytes = outputBytes,
            };
        }
        catch (IOException ex)
        {
            return Failed(tool, scenario, inputBytes, "io error: " + ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Failed(tool, scenario, inputBytes, "access error: " + ex.Message);
        }
        finally
        {
            TryDeleteDirectory(workDir);
        }
    }

    private static Process CreateProcess(string executable, IReadOnlyList<string> arguments)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return new Process { StartInfo = startInfo };
    }

    private static int? DrainAndWait(Process process, int timeoutMs)
    {
        // Tool output (version banners, clean chatter) is small, so draining both
        // streams to end before waiting cannot deadlock on the pipe buffer here.
        _ = process.StandardOutput.ReadToEnd();
        _ = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(timeoutMs))
        {
            TryKill(process);
            return null;
        }

        return process.ExitCode;
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited; nothing to terminate.
        }
        catch (Win32Exception)
        {
            // Could not terminate; nothing more to do.
        }
    }

    private static bool ContainsCode(IReadOnlyCollection<int> codes, int value)
    {
        foreach (int code in codes)
        {
            if (code == value)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsValidPdf(string path, out long length)
    {
        length = 0;
        if (!File.Exists(path))
        {
            return false;
        }

        FileInfo info = new FileInfo(path);
        if (info.Length < 5)
        {
            return false;
        }

        byte[] header = new byte[5];
        using (FileStream stream = File.OpenRead(path))
        {
            stream.ReadExactly(header, 0, header.Length);
        }

        // "%PDF-"
        if (header[0] != (byte)'%' ||
            header[1] != (byte)'P' ||
            header[2] != (byte)'D' ||
            header[3] != (byte)'F' ||
            header[4] != (byte)'-')
        {
            return false;
        }

        length = info.Length;
        return true;
    }

    private static ExternalToolResult Failed(
        ExternalTool tool, CompressionScenario scenario, long inputBytes, string detail)
    {
        return new ExternalToolResult
        {
            ScenarioName = scenario.Name,
            ToolName = tool.Name,
            Outcome = ExternalToolOutcome.Failed,
            InputBytes = inputBytes,
            OutputBytes = 0,
            Detail = detail,
        };
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup.
        }
    }
}
