// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2.1 — internal stub resolver

using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>
/// No-op <see cref="IPdfObjectResolver"/> implementation. Resolves direct
/// references to themselves, reports no known indirect objects, and is
/// useful as a fallback resolver when no real object table is available.
/// </summary>
internal sealed class NullResolver : IPdfObjectResolver
{
    internal static NullResolver Instance { get; } = new();

    public PdfPrimitive Resolve(PdfPrimitive primitive) => primitive;

    public PdfPrimitive ResolveById(PdfObjectId id) => PdfNull.Value;

    public bool Contains(PdfObjectId id) => false;
}
