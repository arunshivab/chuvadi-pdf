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

    /// <summary>
    /// Gets or initialises how the inputs' form fields are carried (3.34.0,
    /// D-335). Default: <see cref="MergeFormFields.RenameOnConflict"/>: every
    /// input's fields are kept, and a top-level field whose name is already
    /// taken is renamed <c>name_2</c>, <c>name_3</c>, and so on. Before 3.34.0
    /// a merge dropped the form (the widgets stayed as plain annotations),
    /// which is <see cref="MergeFormFields.Drop"/>.
    /// </summary>
    public MergeFormFields FormFields { get; init; } = MergeFormFields.RenameOnConflict;

    /// <summary>
    /// Gets or initialises the encryption applied to the merged output, or
    /// null (the default) to write it unencrypted (3.34.0, D-335). The inputs'
    /// own encryption is never carried over.
    /// </summary>
    public Chuvadi.Pdf.IO.EncryptionOptions? Encryption { get; init; }
}
