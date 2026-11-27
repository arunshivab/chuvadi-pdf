// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.6 — Encryption
// PHASE: 3.36.0 (D-410 D, item 4, decided D-417(3)) — an opaque carrier for the
//        encryption of a document that was opened from an encrypted file, so the
//        same protection can be written back onto a rewrite (for example a
//        compressed copy) without knowing either password. The raw file key is
//        never readable from this type; only the host assembly (the writer) can
//        reach it to re-encrypt.

using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Encryption;

/// <summary>
/// Captures the encryption of a source document so a rewrite can keep it
/// byte-for-byte: the same <c>/Encrypt</c> dictionary (and therefore the same
/// owner and user passwords, permissions and key derivation) and the same file
/// identifier. Obtain one from <c>PdfDocument.CaptureEncryptionLock()</c> and
/// pass it to a save that preserves encryption.
/// </summary>
/// <remarks>
/// The raw file encryption key is held internally and is never exposed by any
/// public member — the lock can re-apply the encryption but cannot hand the key
/// out. A lock keeps a document's protection; it does not weaken it, so a
/// consumer refuses to write it back with a legacy RC4 key length that is not a
/// standard 128-bit key (see <see cref="KeyBits"/>).
/// </remarks>
public sealed class SourceEncryptionLock
{
    private readonly byte[] _fileKey;
    private readonly PdfDictionary _encryptDictionary;
    private readonly PdfArray? _id;

    internal SourceEncryptionLock(
        EncryptionAlgorithm algorithm, int keyBits, byte[] fileKey, PdfDictionary encryptDictionary, PdfArray? id)
    {
        Algorithm = algorithm;
        KeyBits = keyBits;
        _fileKey = (byte[])fileKey.Clone();
        _encryptDictionary = encryptDictionary;
        _id = id;
    }

    /// <summary>Gets the source document's stream encryption algorithm.</summary>
    public EncryptionAlgorithm Algorithm { get; }

    /// <summary>Gets the source document's file-key length in bits (the <c>/Length</c> entry).</summary>
    public int KeyBits { get; }

    // The file encryption key, copied so the caller cannot mutate the stored
    // one. Internal only — the key never leaves the assembly.
    internal byte[] CopyFileKey() => (byte[])_fileKey.Clone();

    // The source /Encrypt dictionary, written back verbatim so every password
    // and permission entry is preserved exactly.
    internal PdfDictionary EncryptDictionary => _encryptDictionary;

    // The source trailer's /ID array, preserved so a viewer re-derives the same
    // file key from a password (the R≤4 key derivation hashes /ID[0]).
    internal PdfArray? Id => _id;
}
