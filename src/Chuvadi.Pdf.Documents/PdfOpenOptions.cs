// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.24 — embedding API (A1, A4)

using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Documents;

/// <summary>Options for opening a PDF with <see cref="PdfDocument.Open(System.IO.Stream, PdfOpenOptions)"/>.</summary>
public sealed class PdfOpenOptions
{
    /// <summary>
    /// Gets the user or owner password. Null or empty: open without one (an
    /// encrypted file that needs one throws <see cref="PdfPasswordRequiredException"/>).
    /// </summary>
    public string? Password { get; init; }

    /// <summary>
    /// Gets the resource limits. <see cref="PdfLimits.MaxInputBytes"/> and
    /// <see cref="PdfLimits.MaxPageCount"/> are checked while opening; all of
    /// them are kept on the document (<see cref="PdfDocument.Limits"/>) and
    /// applied by <c>PageRasterizer</c> and <c>TextExtractor</c>. Null: none.
    /// </summary>
    public PdfLimits? Limits { get; init; }

    /// <summary>
    /// Gets a value indicating whether to leave the stream open when the
    /// document is disposed. Ignored by the asynchronous and path overloads,
    /// which never own the caller's stream.
    /// </summary>
    public bool LeaveOpen { get; init; }

    /// <summary>
    /// Gets the receiver of warnings. Where Chuvadi meets something it cannot
    /// use and carries on without it (a damaged embedded font replaced by a
    /// substitute, a damaged glyph that draws nothing, a shading that is
    /// skipped), it calls this with a <see cref="PdfWarning"/> saying what,
    /// on which page and why. It is kept on the document
    /// (<see cref="PdfDocument.OnWarning"/>) and called on the thread doing the
    /// work, so a handler shared between threads must be thread-safe. An
    /// exception it throws passes to the caller of that work. Null: warnings
    /// are not reported.
    /// </summary>
    public System.Action<PdfWarning>? OnWarning { get; init; }

    /// <summary>
    /// Gets a value indicating whether to repair a damaged file while opening
    /// it (3.32.1, D-280). When true and the file's structure cannot be read
    /// (a wrong <c>startxref</c>, a broken cross-reference table or trailer,
    /// junk before the header, a truncated file), the file is repaired in
    /// memory with <c>PdfRepairer</c> and the repaired copy is opened. The
    /// file on disk is not changed. <see cref="PdfDocument.Warnings"/> then
    /// starts with a line saying the file was repaired, why, and what was
    /// recovered (so <see cref="PdfDocument.IsRecovered"/> is true). Default
    /// false: such a file throws <see cref="PdfParseException"/>, as before.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only a structure failure triggers the repair. A wrong or missing
    /// password (<see cref="PdfPasswordRequiredException"/>), a limit
    /// (<see cref="PdfLimitExceededException"/>), an unsupported security
    /// handler and cancellation are thrown as before. When the repair cannot
    /// find a document catalog, <see cref="PdfParseException"/> is thrown with
    /// both reasons.
    /// </para>
    /// <para>
    /// The repaired copy is held in memory (about the size of the file). The
    /// caller's stream is read from its start and, unless
    /// <see cref="LeaveOpen"/> is set, disposed once the repaired copy is open.
    /// An encrypted file keeps its encryption, but objects stored inside its
    /// object streams cannot be recovered.
    /// </para>
    /// </remarks>
    public bool RepairIfDamaged { get; init; }
}
