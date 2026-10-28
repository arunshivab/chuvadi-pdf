// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.5 — Chuvadi.Pdf.Encryption
//
// High-level entry point. Inspects /Encrypt + /ID and a password, then
// produces a configured Decryptor ready to be applied to objects.

using System;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Encryption;

/// <summary>
/// Top-level helper for decrypting an encrypted PDF.
/// </summary>
public static class PdfEncryption
{
    /// <summary>
    /// Builds a <see cref="Decryptor"/> for the given trailer's /Encrypt entry
    /// and /ID array, using the given password.
    /// </summary>
    /// <param name="encryptDict">The trailer's /Encrypt dictionary, resolved.</param>
    /// <param name="firstFileId">First element of the trailer /ID array (16 bytes).</param>
    /// <param name="password">Empty for unencrypted-or-default; user/owner password otherwise.</param>
    /// <returns>A configured Decryptor, or null if the password is wrong.</returns>
    public static Decryptor? TryOpen(
        PdfDictionary encryptDict,
        byte[] firstFileId,
        string password)
    {
        if (encryptDict is null)
        {
            throw new ArgumentNullException(nameof(encryptDict));
        }

        if (firstFileId is null)
        {
            throw new ArgumentNullException(nameof(firstFileId));
        }

        EncryptionDictionary meta = EncryptionDictionary.Parse(encryptDict)
            ?? throw new PdfEncryptionException(
                "Unsupported /Encrypt dictionary (not a standard security handler).");

        byte[]? fileKey;

        if (meta.R == 6)
        {
            // AES-256 path
            fileKey = StandardSecurityHandler.ValidatePasswordR6(
                password,
                meta.U,
                meta.UE,
                meta.O,
                meta.OE,
                tryOwnerPassword: true);

            if (fileKey is null)
            {
                return null;
            }
        }
        else if (meta.R >= 2 && meta.R <= 4)
        {
            // /Length is 40 to 128 bits (5 to 16 bytes) for these revisions.
            if (meta.KeyBytes < 5 || meta.KeyBytes > 16)
            {
                throw new PdfEncryptionException($"Invalid encryption key /Length {meta.KeyBits} bits for /R {meta.R}.");
            }

            // R=2/3/4 path
            byte[] derived = StandardSecurityHandler.DeriveKeyR4(
                password,
                meta.O,
                meta.Permissions,
                firstFileId,
                meta.R,
                meta.KeyBytes,
                meta.EncryptMetadata);

            bool ok = StandardSecurityHandler.ValidateUserPasswordR4(
                derived, meta.U, firstFileId, meta.R);

            if (!ok && !string.IsNullOrEmpty(password))
            {
                // Not the user password: try it as the owner password.
                string user = StandardSecurityHandler.RecoverUserPasswordR4(password, meta.O, meta.R, meta.KeyBytes);
                derived = StandardSecurityHandler.DeriveKeyR4(
                    user, meta.O, meta.Permissions, firstFileId, meta.R, meta.KeyBytes, meta.EncryptMetadata);
                ok = StandardSecurityHandler.ValidateUserPasswordR4(derived, meta.U, firstFileId, meta.R);
            }

            if (!ok)
            {
                return null;
            }

            fileKey = derived;
        }
        else
        {
            throw new PdfEncryptionException($"Unsupported encryption revision /R = {meta.R}.");
        }

        return new Decryptor(fileKey, meta.Algorithm);
    }
}
