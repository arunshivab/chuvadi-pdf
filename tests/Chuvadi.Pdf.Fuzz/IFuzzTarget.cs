// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2.3 — Fuzzing harness

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Fuzz.Targets;

namespace Chuvadi.Pdf.Fuzz;

/// <summary>
/// Contract for a fuzz target — a single API surface that the fuzzer feeds random bytes to.
/// </summary>
/// <remarks>
/// Implementations declare which exception types are "expected" — i.e., the target's
/// documented way of cleanly rejecting malformed input. Anything thrown outside that list
/// is recorded as a crash.
/// </remarks>
internal interface IFuzzTarget
{
    /// <summary>Short identifier used on the command line.</summary>
    string Name { get; }

    /// <summary>
    /// Exception types this target is documented to throw on malformed input.
    /// Throws of these types are treated as cleanly-rejected input, not crashes.
    /// </summary>
    IReadOnlyList<Type> ExpectedExceptionTypes { get; }

    /// <summary>
    /// Feeds an input buffer to the target. Any exception escaping this method
    /// is examined; matches against <see cref="ExpectedExceptionTypes"/> count as
    /// expected; anything else is a crash.
    /// </summary>
    void Run(byte[] input);
}

/// <summary>Registry of fuzz targets known to the harness.</summary>
internal static class FuzzTargets
{
    private static readonly Dictionary<string, Func<IFuzzTarget>> _registry = new(StringComparer.Ordinal)
    {
        ["pdf-open"] = () => new PdfOpenTarget(),
        ["content-stream"] = () => new ContentStreamTarget(),
        ["truetype"] = () => new TrueTypeTarget(),
        ["write-paths"] = () => new WritePathsTarget(),
    };

    public static IEnumerable<string> Names => _registry.Keys;

    public static bool TryGet(string name, out IFuzzTarget? target)
    {
        if (_registry.TryGetValue(name, out Func<IFuzzTarget>? factory))
        {
            target = factory();
            return true;
        }
        target = null;
        return false;
    }
}
