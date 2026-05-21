// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.6 — Encryption
//        ISO 32000-2:2020 §7.6 — Encryption (AES-256 / R=6)
// PHASE: v2.0.0 R3 — DocumentInfo aggregate

using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Documents;

/// <summary>
/// Snapshot of the security state of an opened PDF document.
/// </summary>
/// <remarks>
/// <para>
/// Populated from the document's <c>/Encrypt</c> dictionary, when
/// present. For unencrypted documents the helper factory
/// <see cref="None"/> returns an instance with
/// <see cref="IsEncrypted"/> false and all permissions granted.
/// </para>
/// <para>
/// PDF 32000-1:2008 §7.6.3.2 lays out the permissions bitfield (the
/// <c>/P</c> entry in the encryption dictionary); ISO 32000-2:2020 adds
/// the AES-256 algorithm (revision 6).
/// </para>
/// </remarks>
public sealed class EncryptionInfo
{
    /// <summary>Initialises an <see cref="EncryptionInfo"/>.</summary>
    /// <param name="isEncrypted">Whether the document is encrypted at all.</param>
    /// <param name="algorithm">Human-readable algorithm name, or null.</param>
    /// <param name="keyLengthBits">Encryption key length in bits, or null.</param>
    /// <param name="permissions">The granted permission set.</param>
    public EncryptionInfo(
        bool isEncrypted,
        string? algorithm,
        int? keyLengthBits,
        PdfPermissions permissions)
    {
        IsEncrypted = isEncrypted;
        Algorithm = algorithm;
        KeyLengthBits = keyLengthBits;
        Permissions = permissions;
    }

    /// <summary>
    /// A sentinel <see cref="EncryptionInfo"/> describing an unencrypted
    /// document: all permissions granted, no algorithm, no key length.
    /// </summary>
    public static EncryptionInfo None { get; } = new EncryptionInfo(
        isEncrypted: false,
        algorithm: null,
        keyLengthBits: null,
        permissions: AllPermissions);

    /// <summary>The union of every defined permission flag.</summary>
    private static PdfPermissions AllPermissions =>
        PdfPermissions.Print |
        PdfPermissions.ModifyContents |
        PdfPermissions.CopyContents |
        PdfPermissions.ModifyAnnotations |
        PdfPermissions.FillForms |
        PdfPermissions.ExtractAccessibility |
        PdfPermissions.Assemble |
        PdfPermissions.PrintHighQuality;

    /// <summary>Gets whether the document is encrypted.</summary>
    public bool IsEncrypted { get; }

    /// <summary>
    /// Gets a short human-readable identifier for the encryption
    /// algorithm. Standard values:
    /// <c>RC4-40</c>, <c>RC4-128</c>, <c>AES-128</c>, <c>AES-256</c>.
    /// Null when <see cref="IsEncrypted"/> is false.
    /// </summary>
    public string? Algorithm { get; }

    /// <summary>
    /// Gets the encryption key length in bits, or null when unknown or
    /// the document is unencrypted.
    /// </summary>
    public int? KeyLengthBits { get; }

    /// <summary>
    /// Gets the granted permission set as a <see cref="PdfPermissions"/>
    /// flag combination. For unencrypted documents every permission is
    /// granted.
    /// </summary>
    public PdfPermissions Permissions { get; }
}
