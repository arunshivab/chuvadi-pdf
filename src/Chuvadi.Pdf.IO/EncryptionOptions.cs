// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.5 (integration) — Chuvadi.Pdf.IO
//        v2.0.2 — corrected the "allow everything" default
//
// Configuration for writing encrypted PDFs.

using System;
using Chuvadi.Pdf.Encryption;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.IO;

/// <summary>
/// Options that drive encrypted PDF writing.
/// </summary>
/// <remarks>
/// Only AES-128 and AES-256 are supported for writing. Legacy RC4 is read-only.
/// Construct an instance for the chosen algorithm via the static factories.
/// </remarks>
public sealed class EncryptionOptions
{
    /// <summary>
    /// The /P permission mask that grants every action defined by PDF
    /// 32000-1:2008 §7.6.3.2, Table 22 (print, modify, copy, annotate,
    /// fill forms, accessibility extract, assemble, high-quality print).
    /// </summary>
    /// <remarks>
    /// Equal to <c>-4</c> (0xFFFFFFFC): all eight permission bits set,
    /// both reserved-must-be-1 bits set (bits 7 and 8), and all high
    /// reserved bits (13..32) set. Only bits 1 and 2 are clear because
    /// the spec reserves them to 0.
    /// </remarks>
    public const int AllPermissionsAllowed = -4;

    // The eight permission bits of /P (PDF 32000-1:2008 Table 22): bits 3, 4,
    // 5, 6, 9, 10, 11 and 12, counting from 1.
    private const int PrintBit = 1 << 2;
    private const int ModifyBit = 1 << 3;
    private const int CopyBit = 1 << 4;
    private const int AnnotateBit = 1 << 5;
    private const int FillFormsBit = 1 << 8;
    private const int AccessibilityBit = 1 << 9;
    private const int AssembleBit = 1 << 10;
    private const int PrintHighQualityBit = 1 << 11;
    private const int AllPermissionBits = PrintBit | ModifyBit | CopyBit | AnnotateBit
        | FillFormsBit | AccessibilityBit | AssembleBit | PrintHighQualityBit;

    /// <summary>
    /// Returns the <c>/P</c> value that grants exactly <paramref name="allowed"/>,
    /// for <see cref="Permissions"/> (3.32.0, D-230). <see cref="WithPermissions"/>
    /// sets it for you.
    /// </summary>
    /// <param name="allowed">The actions a user-password holder may take.</param>
    /// <returns>
    /// The permission mask: the bits for the allowed actions set, the other
    /// permission bits clear, bits 1 and 2 clear and every reserved bit set,
    /// as PDF 32000-1:2008 Table 22 requires.
    /// <see cref="PdfPermissions.All"/> gives <see cref="AllPermissionsAllowed"/>.
    /// </returns>
    /// <remarks>
    /// <see cref="PdfPermissions.PrintHighQuality"/> also sets the print bit,
    /// because the specification gives bit 12 meaning only together with
    /// bit 3.
    /// </remarks>
    public static int PermissionMask(PdfPermissions allowed)
    {
        int mask = AllPermissionsAllowed & ~AllPermissionBits;
        if ((allowed & (PdfPermissions.Print | PdfPermissions.PrintHighQuality)) != 0)
        {
            mask |= PrintBit;
        }

        if ((allowed & PdfPermissions.ModifyContents) != 0)
        {
            mask |= ModifyBit;
        }

        if ((allowed & PdfPermissions.CopyContents) != 0)
        {
            mask |= CopyBit;
        }

        if ((allowed & PdfPermissions.ModifyAnnotations) != 0)
        {
            mask |= AnnotateBit;
        }

        if ((allowed & PdfPermissions.FillForms) != 0)
        {
            mask |= FillFormsBit;
        }

        if ((allowed & PdfPermissions.ExtractAccessibility) != 0)
        {
            mask |= AccessibilityBit;
        }

        if ((allowed & PdfPermissions.Assemble) != 0)
        {
            mask |= AssembleBit;
        }

        if ((allowed & PdfPermissions.PrintHighQuality) != 0)
        {
            mask |= PrintHighQualityBit;
        }

        return mask;
    }

    private EncryptionOptions(EncryptionAlgorithm algorithm, byte[] fileKey, string userPassword, string ownerPassword)
    {
        Algorithm = algorithm;
        FileKey = fileKey;
        UserPassword = userPassword;
        OwnerPassword = ownerPassword;
        Permissions = AllPermissionsAllowed;
        EncryptMetadata = true;
    }

    /// <summary>Gets the chosen encryption algorithm.</summary>
    public EncryptionAlgorithm Algorithm { get; }

    /// <summary>Gets the file encryption key. Random unless overridden.</summary>
    public byte[] FileKey { get; }

    /// <summary>Gets the user password used to derive the U/UE entries.</summary>
    public string UserPassword { get; }

    /// <summary>Gets the owner password used to derive the O/OE entries.</summary>
    public string OwnerPassword { get; }

    /// <summary>
    /// Gets or initialises the permission bit mask written to /P. Defaults to
    /// <see cref="AllPermissionsAllowed"/> (every action permitted). Set a
    /// narrower value to restrict what user-password holders may do.
    /// </summary>
    public int Permissions { get; init; }

    /// <summary>
    /// Gets or initialises whether /Metadata streams should be encrypted.
    /// Default: true. Setting false matches /EncryptMetadata=false in the spec.
    /// </summary>
    public bool EncryptMetadata { get; init; }

    /// <summary>
    /// Returns a copy of these options that grants a user-password holder
    /// exactly <paramref name="allowed"/> (3.32.0, D-240): <see cref="Permissions"/>
    /// is <see cref="PermissionMask(PdfPermissions)"/> of it. The algorithm, file
    /// key, passwords and <see cref="EncryptMetadata"/> are kept; this instance
    /// is not changed.
    /// </summary>
    /// <param name="allowed">The actions a user-password holder may take.</param>
    /// <returns>The new options.</returns>
    /// <remarks>
    /// The owner password always grants every action (PDF 32000-1:2008
    /// §7.6.3.2), and the permissions are honoured by the viewer, not enforced
    /// by the encryption. Example:
    /// <c>EncryptionOptions.Aes256("user", "owner").WithPermissions(PdfPermissions.Print | PdfPermissions.CopyContents)</c>.
    /// </remarks>
    public EncryptionOptions WithPermissions(PdfPermissions allowed) =>
        new EncryptionOptions(Algorithm, FileKey, UserPassword, OwnerPassword)
        {
            Permissions = PermissionMask(allowed),
            EncryptMetadata = EncryptMetadata,
        };

    /// <summary>
    /// Returns a copy of these options with <see cref="EncryptMetadata"/> set to
    /// <paramref name="encryptMetadata"/> (3.32.0). False leaves the document's
    /// <c>/Metadata</c> stream readable without a password (written as
    /// <c>/EncryptMetadata false</c>), so search tools can index it. The
    /// algorithm, file key, passwords and <see cref="Permissions"/> are kept;
    /// this instance is not changed.
    /// </summary>
    /// <param name="encryptMetadata">Whether <c>/Metadata</c> streams are encrypted.</param>
    /// <returns>The new options.</returns>
    public EncryptionOptions WithEncryptMetadata(bool encryptMetadata) =>
        new EncryptionOptions(Algorithm, FileKey, UserPassword, OwnerPassword)
        {
            Permissions = Permissions,
            EncryptMetadata = encryptMetadata,
        };

    /// <summary>
    /// Creates options for AES-128 encryption (V=4, R=4, AESV2 crypt filter).
    /// Generates a random 16-byte file key.
    /// </summary>
    public static EncryptionOptions Aes128(string userPassword, string? ownerPassword = null)
    {
        ArgumentNullException.ThrowIfNull(userPassword);
        return new EncryptionOptions(
            EncryptionAlgorithm.Aes_128,
            Encryptor.GenerateFileKeyAes128(),
            userPassword,
            ownerPassword ?? userPassword);
    }

    /// <summary>
    /// Creates options for AES-256 encryption (V=5, R=6, ISO 32000-2 standardised).
    /// Generates a random 32-byte file key.
    /// </summary>
    public static EncryptionOptions Aes256(string userPassword, string? ownerPassword = null)
    {
        ArgumentNullException.ThrowIfNull(userPassword);
        return new EncryptionOptions(
            EncryptionAlgorithm.Aes_256,
            Encryptor.GenerateFileKeyAes256(),
            userPassword,
            ownerPassword ?? userPassword);
    }
}
