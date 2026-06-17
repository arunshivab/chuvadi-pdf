// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.5.4 — Cross-reference table
//        PDF 32000-1:2008 §7.5.7 — Object streams
//        PDF 32000-1:2008 §7.5.8 — Cross-reference streams
// PHASE: Phase 1 — Chuvadi.Pdf.IO
// Selects how PdfWriter emits the cross-reference data and object bodies.

namespace Chuvadi.Pdf.IO;

/// <summary>
/// Selects the cross-reference format <see cref="PdfWriter"/> writes.
/// </summary>
/// <remarks>
/// Both styles produce valid PDFs. <see cref="Classic"/> maximises reader
/// compatibility; <see cref="Stream"/> produces smaller files by packing
/// objects into object streams and replacing the plaintext cross-reference
/// table with a compressed cross-reference stream.
/// </remarks>
public enum XrefStyle
{
    /// <summary>
    /// Classic 20-byte cross-reference table with a plaintext trailer
    /// (PDF 1.4+). Every object is written as a direct indirect object.
    /// Maximum reader compatibility. This is the default.
    /// PDF 32000-1:2008 §7.5.4.
    /// </summary>
    Classic = 0,

    /// <summary>
    /// Object streams plus a cross-reference stream (PDF 1.5+). Eligible
    /// objects are packed into compressed object streams and the
    /// cross-reference table is itself written as a compressed stream,
    /// producing a smaller file. PDF 32000-1:2008 §7.5.7 and §7.5.8.
    /// </summary>
    Stream = 1,
}
