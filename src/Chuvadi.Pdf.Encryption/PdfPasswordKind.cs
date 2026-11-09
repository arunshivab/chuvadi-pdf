// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.6.3.3–4 (R2–4); ISO 32000-2:2020 Algorithm 2.A (R6)
// PHASE: 3.32.0 (D-208 item 3, PDF-16) — which password opened the document

namespace Chuvadi.Pdf.Encryption;

/// <summary>
/// Which password opened an encrypted document (3.32.0).
/// </summary>
/// <remarks>
/// A password is tested as the owner password first and then as the user
/// password, so a password that is both reports <see cref="Owner"/>.
/// </remarks>
public enum PdfPasswordKind
{
    /// <summary>
    /// The empty user password opened it: the document opens without asking.
    /// </summary>
    NoPassword = 0,

    /// <summary>A non-empty user password opened it.</summary>
    User = 1,

    /// <summary>The owner password opened it.</summary>
    Owner = 2,
}
