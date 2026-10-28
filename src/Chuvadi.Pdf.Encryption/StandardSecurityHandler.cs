// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.6.4 — Standard security handler (R≤4)
//        PDF 32000-2:2020 §7.6.4.3 — Algorithm 2.B (R=6, AES-256)
// PHASE: Phase 1.1.5 — Chuvadi.Pdf.Encryption
//
// Derives the file encryption key from a user or owner password per the
// PDF standard security handler. Each PDF revision (R=2, R=3, R=4, R=6)
// has its own key-derivation algorithm; this class implements all four.

using System;
using System.Security.Cryptography;
using System.Text;

namespace Chuvadi.Pdf.Encryption;

/// <summary>
/// Standard security handler: derives a file encryption key from a user/owner
/// password and the document's /Encrypt dictionary.
/// </summary>
public static class StandardSecurityHandler
{
    /// <summary>
    /// PDF's fixed 32-byte padding string (Algorithm 2.A, step 1 — §7.6.4.3.2).
    /// </summary>
    private static readonly byte[] Padding = new byte[]
    {
        0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41,
        0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
        0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80,
        0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A,
    };

    /// <summary>
    /// Computes the file encryption key for R≤4 from the user password.
    /// </summary>
    /// <param name="password">UTF-8 / PDFDocEncoding password. Empty if none.</param>
    /// <param name="oValue">/O entry from /Encrypt (32 bytes).</param>
    /// <param name="permissions">/P entry from /Encrypt.</param>
    /// <param name="firstFileId">First element of trailer /ID (16 bytes).</param>
    /// <param name="revision">/R entry: 2, 3, or 4.</param>
    /// <param name="keyLength">/Length entry in bytes (5 for R=2, 16 for R≥3).</param>
    /// <param name="encryptMetadata">/EncryptMetadata entry (default true).</param>
    /// <returns>The derived file encryption key.</returns>
    public static byte[] DeriveKeyR4(
        string password,
        byte[] oValue,
        int permissions,
        byte[] firstFileId,
        int revision,
        int keyLength,
        bool encryptMetadata = true)
    {
        ArgumentNullException.ThrowIfNull(oValue);
        ArgumentNullException.ThrowIfNull(firstFileId);

        // Algorithm 2 (§7.6.4.3.2): build hash input from padded password + O + P + ID
        byte[] paddedPassword = PadPassword(password);

        using MD5 md5 = MD5.Create();
        md5.TransformBlock(paddedPassword, 0, 32, null, 0);
        md5.TransformBlock(oValue, 0, oValue.Length, null, 0);

        byte[] pBytes = new byte[]
        {
            (byte)(permissions & 0xFF),
            (byte)((permissions >> 8) & 0xFF),
            (byte)((permissions >> 16) & 0xFF),
            (byte)((permissions >> 24) & 0xFF),
        };
        md5.TransformBlock(pBytes, 0, 4, null, 0);

        md5.TransformBlock(firstFileId, 0, firstFileId.Length, null, 0);

        // If revision >= 4 and /EncryptMetadata is false, append four 0xFF bytes
        if (revision >= 4 && !encryptMetadata)
        {
            byte[] tail = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF };
            md5.TransformBlock(tail, 0, 4, null, 0);
        }

        md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        byte[] hash = md5.Hash!;

        // For revision 3 and 4: iterate MD5 50 times over the first keyLength bytes
        if (revision >= 3)
        {
            for (int i = 0; i < 50; i++)
            {
                using MD5 inner = MD5.Create();
                hash = inner.ComputeHash(hash, 0, keyLength);
            }
        }

        byte[] key = new byte[keyLength];
        Array.Copy(hash, key, keyLength);
        return key;
    }

    /// <summary>
    /// Verifies a user password by comparing the computed U value against /U.
    /// </summary>
    /// <returns>true if the password is the user password.</returns>
    public static bool ValidateUserPasswordR4(
        byte[] derivedKey,
        byte[] uValue,
        byte[] firstFileId,
        int revision)
    {
        ArgumentNullException.ThrowIfNull(derivedKey);
        ArgumentNullException.ThrowIfNull(uValue);
        ArgumentNullException.ThrowIfNull(firstFileId);

        byte[] computedU = ComputeUValueR4(derivedKey, firstFileId, revision);

        // R=2 compares full 32 bytes; R≥3 compares first 16 bytes
        int compareLen = revision == 2 ? 32 : 16;

        for (int i = 0; i < compareLen; i++)
        {
            if (computedU[i] != uValue[i])
            {
                return false;
            }
        }

        return true;
    }

    private static byte[] ComputeUValueR4(byte[] key, byte[] firstFileId, int revision)
    {
        if (revision == 2)
        {
            // R=2: U = RC4(key, padding)
            return Rc4.Process(key, Padding);
        }

        // R≥3: hash padding + ID, then RC4 with the key, then RC4 19 more times
        // with key XORed by iteration index.
        using MD5 md5 = MD5.Create();
        md5.TransformBlock(Padding, 0, 32, null, 0);
        md5.TransformBlock(firstFileId, 0, firstFileId.Length, null, 0);
        md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        byte[] hash = md5.Hash!;

        byte[] u = Rc4.Process(key, hash);

        for (int i = 1; i <= 19; i++)
        {
            byte[] iterKey = new byte[key.Length];
            for (int k = 0; k < key.Length; k++)
            {
                iterKey[k] = (byte)(key[k] ^ i);
            }
            u = Rc4.Process(iterKey, u);
        }

        // U is 16 bytes; pad to 32
        byte[] padded = new byte[32];
        Array.Copy(u, padded, 16);
        return padded;
    }

    /// <summary>
    /// Recovers the (padded) user password from an owner password for R≤4
    /// (PDF 32000-1:2008 §7.6.4.4, Algorithm 7): the owner password's key
    /// decrypts /O. Validate the result as a user password; it opens the
    /// document only when the owner password was right.
    /// </summary>
    /// <param name="ownerPassword">The candidate owner password.</param>
    /// <param name="oValue">/O entry from /Encrypt (32 bytes).</param>
    /// <param name="revision">/R entry: 2, 3, or 4.</param>
    /// <param name="keyLength">/Length entry in bytes (5 for R=2, 16 for R≥3).</param>
    /// <returns>The 32-byte padded user password, as Latin-1 text.</returns>
    public static string RecoverUserPasswordR4(string ownerPassword, byte[] oValue, int revision, int keyLength)
    {
        ArgumentNullException.ThrowIfNull(oValue);

        // Algorithm 3, steps a–d: the RC4 key from the padded owner password.
        byte[] hash = MD5.HashData(PadPassword(ownerPassword));
        if (revision >= 3)
        {
            for (int i = 0; i < 50; i++)
            {
                hash = MD5.HashData(hash);
            }
        }

        int n = revision == 2 ? 5 : Math.Clamp(keyLength, 5, 16);
        byte[] key = new byte[n];
        Array.Copy(hash, key, n);

        byte[] user = new byte[32];
        Array.Copy(oValue, user, Math.Min(32, oValue.Length));
        if (revision == 2)
        {
            user = Rc4.Process(key, user);
        }
        else
        {
            for (int i = 19; i >= 0; i--)
            {
                byte[] iterKey = new byte[n];
                for (int k = 0; k < n; k++)
                {
                    iterKey[k] = (byte)(key[k] ^ i);
                }

                user = Rc4.Process(iterKey, user);
            }
        }

        return Encoding.Latin1.GetString(user);
    }

    private static byte[] PadPassword(string password)
    {
        byte[] result = new byte[32];

        // Use UTF-8 with fallback to Latin-1 for compatibility with legacy PDFs.
        // PDF 1.7 used PDFDocEncoding, but for ASCII passwords (the overwhelming
        // majority) the difference doesn't matter.
        byte[] pwd = Encoding.Latin1.GetBytes(password ?? string.Empty);

        int pwdLen = Math.Min(pwd.Length, 32);
        Array.Copy(pwd, result, pwdLen);

        if (pwdLen < 32)
        {
            Array.Copy(Padding, 0, result, pwdLen, 32 - pwdLen);
        }

        return result;
    }

    // ── R=6 / AES-256 (ISO 32000-2 §7.6.4.3) ──────────────────────────────

    /// <summary>
    /// Validates a password against an R=6 /U value (ISO 32000-2 Algorithm 2.B).
    /// Returns the derived file encryption key on success, or null on failure.
    /// </summary>
    public static byte[]? ValidatePasswordR6(
        string password,
        byte[] uValue,      // 48 bytes: 32-byte hash, 8-byte validation salt, 8-byte key salt
        byte[] ueValue,     // 32 bytes: encrypted file key
        byte[] oValue,
        byte[] oeValue,
        bool tryOwnerPassword)
    {
        ArgumentNullException.ThrowIfNull(uValue);
        ArgumentNullException.ThrowIfNull(ueValue);
        ArgumentNullException.ThrowIfNull(oValue);
        ArgumentNullException.ThrowIfNull(oeValue);

        byte[] pwd = Encoding.UTF8.GetBytes(password ?? string.Empty);
        if (pwd.Length > 127)
        {
            Array.Resize(ref pwd, 127);
        }

        if (uValue.Length < 48 || ueValue.Length < 32)
        {
            return null;
        }

        // Try user password first
        byte[] validationSalt = new byte[8];
        byte[] keySalt = new byte[8];
        Array.Copy(uValue, 32, validationSalt, 0, 8);
        Array.Copy(uValue, 40, keySalt, 0, 8);

        byte[] expectedHash = ComputeHashR6(pwd, validationSalt, Array.Empty<byte>());

        bool matches = true;
        for (int i = 0; i < 32; i++)
        {
            if (expectedHash[i] != uValue[i])
            {
                matches = false;
                break;
            }
        }

        if (matches)
        {
            byte[] intermediate = ComputeHashR6(pwd, keySalt, Array.Empty<byte>());
            return DecryptFileKeyR6(intermediate, ueValue);
        }

        if (!tryOwnerPassword)
        {
            return null;
        }

        // Owner password path requires the user value as additional input
        Array.Copy(oValue, 32, validationSalt, 0, 8);
        Array.Copy(oValue, 40, keySalt, 0, 8);

        byte[] uFirst48 = new byte[48];
        Array.Copy(uValue, uFirst48, 48);

        expectedHash = ComputeHashR6(pwd, validationSalt, uFirst48);
        matches = true;
        for (int i = 0; i < 32; i++)
        {
            if (expectedHash[i] != oValue[i])
            {
                matches = false;
                break;
            }
        }

        if (matches)
        {
            byte[] intermediate = ComputeHashR6(pwd, keySalt, uFirst48);
            return DecryptFileKeyR6(intermediate, oeValue);
        }

        return null;
    }

    private static byte[] ComputeHashR6(byte[] password, byte[] salt, byte[] uData)
    {
        // ISO 32000-2 Algorithm 2.B (iterated hash with AES feedback)
        using SHA256 sha256Init = SHA256.Create();
        sha256Init.TransformBlock(password, 0, password.Length, null, 0);
        sha256Init.TransformBlock(salt, 0, salt.Length, null, 0);
        if (uData.Length > 0)
        {
            sha256Init.TransformBlock(uData, 0, uData.Length, null, 0);
        }
        sha256Init.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        byte[] k = sha256Init.Hash!;

        int round = 0;
        while (true)
        {
            // Build K1 = repeated (password || K || uData) × 64
            int blockLen = password.Length + k.Length + uData.Length;
            byte[] k1 = new byte[blockLen * 64];
            for (int i = 0; i < 64; i++)
            {
                int offset = i * blockLen;
                Buffer.BlockCopy(password, 0, k1, offset, password.Length);
                Buffer.BlockCopy(k, 0, k1, offset + password.Length, k.Length);
                if (uData.Length > 0)
                {
                    Buffer.BlockCopy(uData, 0, k1, offset + password.Length + k.Length, uData.Length);
                }
            }

            // AES-128-CBC encrypt K1 using first 16 bytes of K as key, next 16 as IV
            byte[] keyBytes = new byte[16];
            byte[] iv = new byte[16];
            Array.Copy(k, 0, keyBytes, 0, 16);
            Array.Copy(k, 16, iv, 0, 16);

            using Aes aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;
            aes.Key = keyBytes;
            aes.IV = iv;
            using ICryptoTransform enc = aes.CreateEncryptor();
            byte[] e = enc.TransformFinalBlock(k1, 0, k1.Length);

            // Modulo 3 over first 16 bytes of E (treated as big-endian integer)
            int mod3 = 0;
            for (int i = 0; i < 16; i++)
            {
                mod3 = ((mod3 << 8) | e[i]) % 3;
            }

            HashAlgorithm next = mod3 switch
            {
                0 => SHA256.Create(),
                1 => SHA384.Create(),
                _ => SHA512.Create(),
            };

            k = next.ComputeHash(e);
            next.Dispose();

            round++;

            // Stop when round >= 64 AND the last byte of e is <= round - 32
            if (round >= 64 && e[e.Length - 1] <= round - 32)
            {
                break;
            }
        }

        byte[] result = new byte[32];
        Array.Copy(k, result, 32);
        return result;
    }

    private static byte[] DecryptFileKeyR6(byte[] intermediateKey, byte[] encryptedKey)
    {
        // AES-256-CBC, no padding, zero IV
        using Aes aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        aes.Key = intermediateKey;
        aes.IV = new byte[16];

        using ICryptoTransform dec = aes.CreateDecryptor();
        return dec.TransformFinalBlock(encryptedKey, 0, encryptedKey.Length);
    }
}
