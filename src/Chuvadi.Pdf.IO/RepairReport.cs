// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.5 — File structure, §7.5.7 — Object streams
// PHASE: Phase 1 — Chuvadi.Pdf.IO

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.IO;

/// <summary>
/// Describes what <see cref="PdfRepairer"/> did while reconstructing a damaged
/// PDF. Repair is best-effort: it always produces the cleanest file it can and
/// records here what was recovered, what was rebuilt, and any content that could
/// not be salvaged, rather than throwing on damaged input.
/// </summary>
public sealed class RepairReport
{
    /// <summary>
    /// True when reconstruction completed and an output file was written. False
    /// only when the input was too damaged to recover any usable structure.
    /// </summary>
    public bool Repaired { get; init; }

    /// <summary>Number of indirect objects recovered by scanning the raw bytes.</summary>
    public int ObjectsRecovered { get; init; }

    /// <summary>Objects recovered from inside compressed object streams (/ObjStm).</summary>
    public int ObjectsFromObjectStreams { get; init; }

    /// <summary>
    /// Objects that were defined more than once (e.g. across incremental updates);
    /// the latest definition was kept and the earlier ones discarded.
    /// </summary>
    public int DuplicateObjectsResolved { get; init; }

    /// <summary>True when a fresh trailer was built because the original was missing or unusable.</summary>
    public bool TrailerReconstructed { get; init; }

    /// <summary>True when the document catalog (/Root) had to be located by scanning.</summary>
    public bool RootRecovered { get; init; }

    /// <summary>True when the document catalog (/Type /Catalog) was found.</summary>
    public bool CatalogFound { get; init; }

    /// <summary>True when the %PDF- header was not at offset 0 (leading junk was skipped).</summary>
    public bool HeaderRelocated { get; init; }

    /// <summary>True when the input appeared truncated (trailing content was missing).</summary>
    public bool TruncationDetected { get; init; }

    /// <summary>Size in bytes of the original input.</summary>
    public long OriginalByteCount { get; init; }

    /// <summary>Size in bytes of the repaired output.</summary>
    public long OutputByteCount { get; init; }

    /// <summary>
    /// Human-readable notes about damage encountered and content that could not be
    /// recovered. Empty when the repair was clean.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}
