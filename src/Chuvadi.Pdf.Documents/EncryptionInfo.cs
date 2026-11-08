// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.6 — Encryption
//        PDF 32000-1:2008 §7.6.3.2, Table 22 — User access permissions
// PHASE: v2.0.0 — read-side encryption introspection

using Chuvadi.Pdf.Encryption;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Documents;

/// <summary>
/// Describes the encryption properties of a <see cref="PdfDocument"/>.
/// </summary>
/// <remarks>
/// <para>
/// Returned from <see cref="PdfDocument.Encryption"/> when the document is
/// encrypted; <c>null</c> when the document has no <c>/Encrypt</c> entry in
/// its trailer. The properties on this type expose what the document declares
/// about its security handler — they do not perform any cryptographic
/// operations themselves.
/// </para>
/// <para>
/// The permission decoder properties (<see cref="AllowPrint"/>,
/// <see cref="AllowModify"/>, etc.) interpret the <see cref="Permissions"/>
/// bit mask per PDF 32000-1:2008 §7.6.3.2, Table 22. These flags are advisory
/// only: a viewer is free to honour or ignore them, and an owner password
/// bypasses them entirely.
/// </para>
/// </remarks>
public sealed class EncryptionInfo
{
    /// <summary>Initialises an <see cref="EncryptionInfo"/>.</summary>
    /// <param name="algorithm">Algorithm in use.</param>
    /// <param name="keyLength">Key length in bytes.</param>
    /// <param name="revision">PDF /R revision (2..6).</param>
    /// <param name="version">PDF /V version (1..5).</param>
    /// <param name="permissions">PDF /P permission bit mask.</param>
    /// <param name="encryptMetadata">Whether /Metadata streams are encrypted.</param>
    public EncryptionInfo(
        EncryptionAlgorithm algorithm,
        int keyLength,
        int revision,
        int version,
        int permissions,
        bool encryptMetadata)
    {
        Algorithm = algorithm;
        KeyLength = keyLength;
        Revision = revision;
        Version = version;
        Permissions = permissions;
        EncryptMetadata = encryptMetadata;
    }

    // ── Declared properties ───────────────────────────────────────────────

    /// <summary>Gets the encryption algorithm in use.</summary>
    public EncryptionAlgorithm Algorithm { get; }

    /// <summary>Gets the key length in bytes (5 for RC4-40, 16 for AES-128, 32 for AES-256).</summary>
    public int KeyLength { get; }

    /// <summary>Gets the /R revision value (2..6).</summary>
    public int Revision { get; }

    /// <summary>Gets the /V version value (1..5).</summary>
    public int Version { get; }

    /// <summary>Gets the raw /P permission bit mask.</summary>
    /// <remarks>
    /// Use the <c>Allow*</c> properties for individual permission decoding.
    /// PDF 32000-1:2008 §7.6.3.2, Table 22 — User access permissions.
    /// </remarks>
    public int Permissions { get; }

    /// <summary>Gets whether the /Metadata stream is encrypted.</summary>
    public bool EncryptMetadata { get; }

    // ── Permission decoders (Table 22) ────────────────────────────────────

    /// <summary>Bit 3 — Print the document (possibly at low quality).</summary>
    public bool AllowPrint => (Permissions & PrintBit) != 0;

    /// <summary>Bit 4 — Modify the contents of the document.</summary>
    public bool AllowModify => (Permissions & ModifyBit) != 0;

    /// <summary>Bit 5 — Copy or extract text and graphics from the document.</summary>
    public bool AllowCopy => (Permissions & CopyBit) != 0;

    /// <summary>Bit 6 — Add or modify text annotations and fill in interactive form fields.</summary>
    public bool AllowAnnotate => (Permissions & AnnotateBit) != 0;

    /// <summary>Bit 9 — Fill in existing interactive form fields (R≥3).</summary>
    public bool AllowFillForms => (Permissions & FillFormsBit) != 0;

    /// <summary>Bit 10 — Extract text and graphics for accessibility (R≥3, deprecated in PDF 2.0).</summary>
    public bool AllowAccessibilityExtract => (Permissions & AccessibilityBit) != 0;

    /// <summary>Bit 11 — Assemble the document: insert, rotate, or delete pages (R≥3).</summary>
    public bool AllowAssemble => (Permissions & AssembleBit) != 0;

    /// <summary>Bit 12 — Print the document at high quality (R≥3).</summary>
    public bool AllowPrintHighQuality => (Permissions & PrintHighQualityBit) != 0;

    /// <summary>
    /// Gets which password opened the document (3.32.0, D-208 item 3), or
    /// null when this instance was not made by <see cref="PdfDocument"/>.
    /// </summary>
    /// <remarks>
    /// A password is tested as the owner password first, then as the user
    /// password, so one that is both reports
    /// <see cref="PdfPasswordKind.Owner"/>. The owner password lifts the
    /// <see cref="Permissions"/> restrictions for whoever holds it.
    /// </remarks>
    public PdfPasswordKind? OpenedWith { get; init; }

    /// <summary>
    /// Gets the allowed actions as flags (3.32.0): the same eight answers as
    /// the <c>Allow…</c> properties, decoded from <see cref="Permissions"/>.
    /// </summary>
    public PdfPermissions Flags =>
        (AllowPrint ? PdfPermissions.Print : PdfPermissions.None)
        | (AllowModify ? PdfPermissions.ModifyContents : PdfPermissions.None)
        | (AllowCopy ? PdfPermissions.CopyContents : PdfPermissions.None)
        | (AllowAnnotate ? PdfPermissions.ModifyAnnotations : PdfPermissions.None)
        | (AllowFillForms ? PdfPermissions.FillForms : PdfPermissions.None)
        | (AllowAccessibilityExtract ? PdfPermissions.ExtractAccessibility : PdfPermissions.None)
        | (AllowAssemble ? PdfPermissions.Assemble : PdfPermissions.None)
        | (AllowPrintHighQuality ? PdfPermissions.PrintHighQuality : PdfPermissions.None);

    /// <summary>
    /// Gets what whoever opened the document may do (3.32.0, D-279):
    /// <see cref="PdfPermissions.All"/> when it was opened with the owner
    /// password, otherwise <see cref="Flags"/>.
    /// </summary>
    /// <remarks>
    /// The permissions in <see cref="Permissions"/> restrict a user who opens
    /// the document with the user password (or with none); the owner password
    /// gives full access (PDF 32000-1:2008 §7.6.3.1). So an app that offers
    /// printing, copying or editing should check this property, not
    /// <see cref="Flags"/> or the <c>Allow…</c> properties, which report what
    /// the file states whichever password was used. When
    /// <see cref="OpenedWith"/> is null this is <see cref="Flags"/>.
    /// </remarks>
    public PdfPermissions EffectiveFlags => OpenedWith == PdfPasswordKind.Owner ? PdfPermissions.All : Flags;

    // ── Permission-bit constants ──────────────────────────────────────────
    // PDF spec uses 1-indexed bit positions; the constant is (1 << (N - 1)).

    private const int PrintBit = 1 << 2;
    private const int ModifyBit = 1 << 3;
    private const int CopyBit = 1 << 4;
    private const int AnnotateBit = 1 << 5;
    private const int FillFormsBit = 1 << 8;
    private const int AccessibilityBit = 1 << 9;
    private const int AssembleBit = 1 << 10;
    private const int PrintHighQualityBit = 1 << 11;
}
