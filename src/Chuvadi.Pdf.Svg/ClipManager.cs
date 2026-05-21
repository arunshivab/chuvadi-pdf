// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  W3C SVG 1.1 §14 — Clipping, masking and compositing
// PHASE: v2.0.0 R2 — SVG renderer

using System.Collections.Generic;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Rendering.DisplayList;

namespace Chuvadi.Pdf.Svg;

/// <summary>
/// Allocates stable SVG ids for clip paths and emits the <c>&lt;clipPath&gt;</c>
/// definitions into a deferred <c>&lt;defs&gt;</c> section.
/// </summary>
/// <remarks>
/// <para>
/// PDF clip semantics are intersection: a point is painted only when it
/// lies inside every clip path active for the op. SVG has no native
/// intersection primitive on a single element, so each entry in a
/// <see cref="RenderOp.Clips"/> list becomes one nesting level of
/// <c>&lt;g clip-path="url(#…)"&gt;</c> in the output. The clip path
/// geometries themselves are deduplicated: two ops referencing the same
/// underlying <see cref="Chuvadi.Pdf.Graphics.Path"/> reference share one
/// <c>&lt;clipPath&gt;</c> in <c>&lt;defs&gt;</c>.
/// </para>
/// </remarks>
internal sealed class ClipManager
{
    private readonly Dictionary<Path, string> _idByPath;
    private readonly List<ClipEntry> _entries;

    internal ClipManager()
    {
        _idByPath = new Dictionary<Path, string>(ReferenceEqualityComparer<Path>.Default);
        _entries = new List<ClipEntry>();
    }

    /// <summary>
    /// Returns the stable id for <paramref name="clip"/>, allocating one
    /// and queuing the <c>&lt;clipPath&gt;</c> definition on first sight.
    /// </summary>
    internal string GetOrAllocateId(ClipPath clip, SvgWriter writer)
    {
        if (_idByPath.TryGetValue(clip.Path, out string? existing))
        {
            return existing;
        }

        string id = writer.NextClipId();
        _idByPath[clip.Path] = id;
        _entries.Add(new ClipEntry(id, clip));
        return id;
    }

    /// <summary>
    /// Returns the queued clip definitions in the order they were first
    /// observed. Stable ordering is part of the determinism contract.
    /// </summary>
    internal IReadOnlyList<ClipEntry> Entries => _entries;

    internal readonly struct ClipEntry
    {
        internal ClipEntry(string id, ClipPath clip)
        {
            Id = id;
            Clip = clip;
        }

        internal string Id { get; }

        internal ClipPath Clip { get; }
    }

    /// <summary>
    /// Reference-equality comparer used so that two ops referring to the
    /// same <see cref="Path"/> instance share one clipPath, while two
    /// structurally equal paths constructed independently do not.
    /// Reference identity is exactly what we want here: the builder
    /// captures a snapshot of the active clip list as a per-op
    /// <see cref="ClipPath"/> struct whose <see cref="ClipPath.Path"/>
    /// field is a shared reference when the clip is still active.
    /// </summary>
    private sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T>
        where T : class
    {
        internal static ReferenceEqualityComparer<T> Default { get; } =
            new ReferenceEqualityComparer<T>();

        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

        public int GetHashCode(T obj) =>
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
