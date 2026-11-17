// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.8.1 (byte range), §7.5.6 (incremental updates)
// PHASE: 3.35.0 (D-396, audit A-09) — what changed after a signature.

namespace Chuvadi.Pdf.Signatures.Verification;

/// <summary>
/// What the file holds after the bytes a signature covers (3.35.0).
/// </summary>
public enum SignatureModifications
{
    /// <summary>
    /// Nothing: the signature covers the file to its end (trailing white
    /// space aside), so no change after signing is possible.
    /// </summary>
    None = 0,

    /// <summary>
    /// Only later signatures and validation data were added after signing:
    /// new signature fields, their widgets and appearances, document
    /// timestamps and the <c>/DSS</c> dictionary. Viewers show such a
    /// signature as valid, with later signatures.
    /// </summary>
    SignaturesOnly = 1,

    /// <summary>
    /// The file was changed in another way after signing (page content, form
    /// values, annotations, metadata, a changed signed field), the signature's
    /// byte range leaves part of the signed revision unsigned, or the change
    /// could not be analysed. What a reader sees is not what was signed.
    /// </summary>
    Other = 2,
}
