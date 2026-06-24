// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.3.3 — Document outline (bookmarks)
// PHASE: Document operations — merge options.

using System.Collections.Generic;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// Options for
/// <see cref="PageOperations.Merge(System.IO.Stream, System.Collections.Generic.IReadOnlyList{Chuvadi.Pdf.Documents.PdfDocument}, MergeOptions)"/>.
/// Controls whether and how each input document's outline (bookmark) tree is
/// carried into the merged output.
/// </summary>
public sealed class MergeOptions
{
    /// <summary>
    /// Gets or initialises whether each input's outline is carried into the
    /// merged output with its destination page indices re-based to the merged
    /// page offsets. Bookmarks whose destination cannot be resolved are carried
    /// as title-only entries. Default: <see langword="false"/> (no outline,
    /// matching the parameterless merge overload).
    /// </summary>
    public bool PreserveOutlines { get; init; }

    /// <summary>
    /// Gets or initialises whether each input's top-level bookmarks are nested
    /// under one synthetic per-document parent node, whose destination is that
    /// document's first merged page. Has no effect when
    /// <see cref="PreserveOutlines"/> is <see langword="false"/>, and no parent
    /// node is emitted for an input that contributes no bookmarks. Default:
    /// <see langword="false"/>.
    /// </summary>
    public bool WrapPerDocument { get; init; }

    /// <summary>
    /// Gets or initialises the titles used for the per-document parent nodes when
    /// <see cref="WrapPerDocument"/> is set. Indexed positionally against the
    /// merge input list; a null, empty, or missing entry falls back to the
    /// document's <see cref="Chuvadi.Pdf.Documents.PdfDocument.Title"/>, then to
    /// "Document N" (one-based). Default: <see langword="null"/>.
    /// </summary>
    public IReadOnlyList<string?>? DocumentTitles { get; init; }
}
