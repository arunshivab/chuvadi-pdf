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
        PdfPasswordKind openedWith;

        if (meta.R == 6)
        {
            // AES-256 path. 3.32.0: the owner password is tested first, as
            // ISO 32000-2 Algorithm 2.A orders it; the file key is the same
            // either way.
            fileKey = StandardSecurityHandler.ValidatePasswordR6(
                password,
                meta.U,
                meta.UE,
                meta.O,
                meta.OE,
                tryOwnerPassword: true,
                out bool owner);

            if (fileKey is null)
            {
                return null;
            }

            openedWith = Kind(owner, password);
        }
        else if (meta.R >= 2 && meta.R <= 4)
        {
            // /Length is 40 to 128 bits (5 to 16 bytes) for these revisions.
            if (meta.KeyBytes < 5 || meta.KeyBytes > 16)
            {
                throw new PdfEncryptionException($"Invalid encryption key /Length {meta.KeyBits} bits for /R {meta.R}.");
            }

            // R=2/3/4 path. 3.32.0: a non-empty password is tried as the owner
            // password first (the order ISO 32000-2 gives for R6; PDF 32000-1
            // gives none for R2–4), then as the user password. Both give the
            // same file key; only OpenedWith sees the order. The empty
            // password is tried as the user password only, as before.
            byte[]? derived = null;
            bool owner = false;

            if (!string.IsNullOrEmpty(password))
            {
                string user = StandardSecurityHandler.RecoverUserPasswordR4(password, meta.O, meta.R, meta.KeyBytes);
                byte[] candidate = StandardSecurityHandler.DeriveKeyR4(
                    user, meta.O, meta.Permissions, firstFileId, meta.R, meta.KeyBytes, meta.EncryptMetadata);
                if (StandardSecurityHandler.ValidateUserPasswordR4(candidate, meta.U, firstFileId, meta.R))
                {
                    derived = candidate;
                    owner = true;
                }
            }

            if (derived is null)
            {
                byte[] candidate = StandardSecurityHandler.DeriveKeyR4(
                    password,
                    meta.O,
                    meta.Permissions,
                    firstFileId,
                    meta.R,
                    meta.KeyBytes,
                    meta.EncryptMetadata);
                if (StandardSecurityHandler.ValidateUserPasswordR4(candidate, meta.U, firstFileId, meta.R))
                {
                    derived = candidate;
                }
            }

            if (derived is null)
            {
                return null;
            }

            fileKey = derived;
            openedWith = Kind(owner, password);
        }
        else
        {
            throw new PdfEncryptionException($"Unsupported encryption revision /R = {meta.R}.");
        }

        return new Decryptor(fileKey, meta.Algorithm) { OpenedWith = openedWith };
    }

    private static PdfPasswordKind Kind(bool owner, string password) =>
        owner ? PdfPasswordKind.Owner
        : string.IsNullOrEmpty(password) ? PdfPasswordKind.NoPassword
        : PdfPasswordKind.User;
}
