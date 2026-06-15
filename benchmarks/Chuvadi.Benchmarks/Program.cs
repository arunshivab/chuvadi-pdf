// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2.2 stage 4 — benchmark harness

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

namespace Chuvadi.Benchmarks;

/// <summary>
/// Benchmark entry point. Run with <c>dotnet run -c Release -- [filter]</c> where
/// filter is a BenchmarkDotNet filter expression like <c>*Brotli*</c> or
/// <c>*Compression*</c>. Run with no arguments to launch the interactive selector.
/// </summary>
internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--compression-report")
        {
            return CompressionReport.Print();
        }

        if (args.Length > 0 && args[0] == "--update-compression-baseline")
        {
            CompressionReport.UpdateBaseline(CompressionReport.ResolveBaselinePath(args));
            return 0;
        }

        // Use BenchmarkSwitcher so users can pass --filter, --list, etc.
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, DefaultConfig.Instance);
        return 0;
    }
}
