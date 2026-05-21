// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: v2.0.0 R1 D3c-1 — DisplayList types

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>
/// Abstract base for all operations in a <see cref="PageDisplayList"/>.
/// </summary>
/// <remarks>
/// <para>
/// Each <see cref="RenderOp"/> describes one painting action in PDF user
/// space (Y up, origin at the bottom-left of the MediaBox). The CTM in
/// effect at the moment the op was emitted has already been applied to the
/// op's geometry — consumers do not need to track a CTM stack.
/// </para>
/// <para>
/// Clipping is also pre-baked: <see cref="Clips"/> contains the list of
/// clip paths active when this op was emitted. Empty when no clip is in
/// effect (shares a single empty-array sentinel).
/// </para>
/// <para>
/// Subclasses are sealed; the hierarchy is closed.
/// </para>
/// </remarks>
public abstract class RenderOp
{
    private static readonly ClipPath[] NoClips = Array.Empty<ClipPath>();

    /// <summary>
    /// Initialises a <see cref="RenderOp"/> with an optional clip list.
    /// </summary>
    /// <param name="clips">
    /// Clip paths active when this op was emitted. May be null or empty,
    /// in which case the op is unclipped. When non-empty the list is
    /// copied defensively so subsequent mutation by the caller does not
    /// leak into the op.
    /// </param>
    protected RenderOp(IReadOnlyList<ClipPath>? clips)
    {
        if (clips is null || clips.Count == 0)
        {
            Clips = NoClips;
            return;
        }

        ClipPath[] copy = new ClipPath[clips.Count];

        for (int i = 0; i < clips.Count; i++)
        {
            copy[i] = clips[i];
        }

        Clips = copy;
    }

    /// <summary>
    /// Gets the clip paths active when this op was emitted. Empty when
    /// no clip is in effect.
    /// </summary>
    public IReadOnlyList<ClipPath> Clips { get; }
}
