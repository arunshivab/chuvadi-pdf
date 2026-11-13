// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.6.2 — Algorithm 1 (per-object key derivation)
//        PDF 32000-2:2020 §7.6.2.2 — AES-256 uses the file key directly
// PHASE: Phase 1.1.5 — Chuvadi.Pdf.Encryption
//
// Applies the file encryption key to individual strings and streams, deriving
// the per-object key per Algorithm 1 (for R≤4) or using the file key directly
// (for R=6 / AES-256).

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Encryption;

/// <summary>
/// Decrypts individual strings and streams in an encrypted PDF.
/// </summary>
/// <remarks>
/// For R≤4 the per-object key is derived from the file key plus the indirect
/// object's number and generation per PDF Algorithm 1. AES-128 uses a slightly
/// extended key (with the "sAlT" salt suffix per §7.6.2). For R=6 / AES-256
/// the file key is used directly without per-object derivation.
/// </remarks>
public sealed class Decryptor
{
    private readonly byte[] _fileKey;
    private readonly EncryptionAlgorithm _algorithm;
    private readonly EncryptionAlgorithm _stringAlgorithm;
    private readonly EncryptionAlgorithm _embeddedFileAlgorithm;
    private readonly IReadOnlyDictionary<string, EncryptionAlgorithm>? _cryptFilters;

    /// <summary>Constructs a decryptor for the given file key and algorithm.</summary>
    public Decryptor(byte[] fileKey, EncryptionAlgorithm algorithm)
        : this(fileKey, algorithm, algorithm, algorithm)
    {
    }

    // 3.34.0 (#52d): streams, strings and embedded files can use different
    // crypt filters (/StmF, /StrF, /EFF; ISO 32000-1 §7.6.5).
    internal Decryptor(
        byte[] fileKey,
        EncryptionAlgorithm streamAlgorithm,
        EncryptionAlgorithm stringAlgorithm,
        EncryptionAlgorithm embeddedFileAlgorithm,
        IReadOnlyDictionary<string, EncryptionAlgorithm>? cryptFilters = null)
    {
        _fileKey = fileKey ?? throw new ArgumentNullException(nameof(fileKey));
        _algorithm = streamAlgorithm;
        _stringAlgorithm = stringAlgorithm;
        _embeddedFileAlgorithm = embeddedFileAlgorithm;
        _cryptFilters = cryptFilters;
    }

    /// <summary>
    /// Gets which password opened the document (3.32.0); set by
    /// <see cref="PdfEncryption.TryOpen"/>. A decryptor built directly from a
    /// file key reports <see cref="PdfPasswordKind.NoPassword"/>.
    /// </summary>
    public PdfPasswordKind OpenedWith { get; init; }

    /// <summary>Decrypts data belonging to a specific indirect object.</summary>
    /// <param name="data">Encrypted bytes (string contents or stream payload).</param>
    /// <param name="objectNumber">/N field of the indirect object.</param>
    /// <param name="generation">/G field of the indirect object.</param>
    public byte[] Decrypt(byte[] data, int objectNumber, int generation)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        return Decrypt(_algorithm, data, objectNumber, generation);
    }

    // Decrypts a string's bytes with the /StrF crypt filter.
    internal byte[] DecryptString(byte[] data, int objectNumber, int generation) =>
        Decrypt(_stringAlgorithm, data, objectNumber, generation);

    // Decrypts a stream's data with the /StmF crypt filter, or with /EFF for
    // an embedded file stream.
    internal byte[] DecryptStream(byte[] data, int objectNumber, int generation, bool embeddedFile) =>
        Decrypt(embeddedFile ? _embeddedFileAlgorithm : _algorithm, data, objectNumber, generation);

    // Decrypts a stream's data with the crypt filter its own /Crypt filter
    // names (ISO 32000-1 §7.4.10), in place of /StmF. False when the name is
    // not a crypt filter the encryption dictionary defines.
    internal bool TryDecryptWithCryptFilter(string name, byte[] data, int objectNumber, int generation, out byte[] decrypted)
    {
        EncryptionAlgorithm algorithm = EncryptionAlgorithm.None;
        if (name != "Identity" && (_cryptFilters is null || !_cryptFilters.TryGetValue(name, out algorithm)))
        {
            decrypted = data;
            return false;
        }

        decrypted = Decrypt(algorithm, data, objectNumber, generation);
        return true;
    }

    private byte[] Decrypt(EncryptionAlgorithm algorithm, byte[] data, int objectNumber, int generation)
    {
        return algorithm switch
        {
            EncryptionAlgorithm.None => data,
            EncryptionAlgorithm.Rc4_40 => DecryptRc4PerObject(data, objectNumber, generation),
            EncryptionAlgorithm.Rc4_128 => DecryptRc4PerObject(data, objectNumber, generation),
            EncryptionAlgorithm.Aes_128 => DecryptAes128PerObject(data, objectNumber, generation),
            EncryptionAlgorithm.Aes_256 => AesCrypto.Decrypt(_fileKey, data),
            _ => throw new PdfEncryptionException($"Cannot decrypt: unsupported algorithm {algorithm}."),
        };
    }

    // ── R≤4 per-object key (Algorithm 1) ──────────────────────────────────

    private byte[] DerivePerObjectKey(int objectNumber, int generation, bool aes)
    {
        // Key bytes: file key + 3 bytes obj number + 2 bytes generation + ("sAlT" if AES)
        int extra = aes ? 9 : 5;
        byte[] input = new byte[_fileKey.Length + extra];
        Array.Copy(_fileKey, input, _fileKey.Length);

        int p = _fileKey.Length;
        input[p++] = (byte)(objectNumber & 0xFF);
        input[p++] = (byte)((objectNumber >> 8) & 0xFF);
        input[p++] = (byte)((objectNumber >> 16) & 0xFF);
        input[p++] = (byte)(generation & 0xFF);
        input[p++] = (byte)((generation >> 8) & 0xFF);

        if (aes)
        {
            input[p++] = (byte)'s';
            input[p++] = (byte)'A';
            input[p++] = (byte)'l';
            input[p++] = (byte)'T';
        }

        using MD5 md5 = MD5.Create();
        byte[] hash = md5.ComputeHash(input);

        int keyLen = Math.Min(_fileKey.Length + 5, 16);
        byte[] key = new byte[keyLen];
        Array.Copy(hash, key, keyLen);
        return key;
    }

    private byte[] DecryptRc4PerObject(byte[] data, int objectNumber, int generation)
    {
        byte[] objKey = DerivePerObjectKey(objectNumber, generation, aes: false);
        return Rc4.Process(objKey, data);
    }

    private byte[] DecryptAes128PerObject(byte[] data, int objectNumber, int generation)
    {
        byte[] objKey = DerivePerObjectKey(objectNumber, generation, aes: true);
        return AesCrypto.Decrypt(objKey, data);
    }
}
