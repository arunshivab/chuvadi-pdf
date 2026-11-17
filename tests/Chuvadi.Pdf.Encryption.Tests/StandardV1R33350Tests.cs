// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.6.3 (Table 20 /V, /Length), §7.6.4.3 (Algorithms 1, 2, 5)
// PHASE: 3.35.0 (52d) — /V 1 with /R 3, and /R 2 with no /V, open as 40-bit RC4.

using System;
using System.Security.Cryptography;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Encryption.Tests;

public sealed class StandardV1R33350Tests
{
    private static readonly byte[] FileId = { 0x81, 0x84, 0x8F, 0x4F, 0x84, 0xDD, 0xD7, 0xF6, 0x4A, 0xD9, 0xA6, 0x99, 0xFF, 0x6B, 0xAD, 0x71 };
    private static readonly byte[] Plain = { 0x42, 0x54, 0x20, 0x2F, 0x46, 0x31, 0x20, 0x31, 0x32, 0x20, 0x54, 0x66 };

    [Fact]
    public void V1WithR3_OpensAsFortyBitRc4()
    {
        // stressful-0009 style: /V 1 /R 3, and a /Length that /V 1 must ignore.
        PdfDictionary dict = Dictionary(v: 1, r: 3, length: 128);

        EncryptionDictionary meta = EncryptionDictionary.Parse(dict)!;
        meta.Should().NotBeNull();
        meta.Algorithm.Should().Be(EncryptionAlgorithm.Rc4_40);
        meta.KeyBits.Should().Be(40);

        Decryptor decryptor = PdfEncryption.TryOpen(dict, FileId, string.Empty)!;
        decryptor.Should().NotBeNull();
        decryptor.Decrypt(EncryptObject(dict, 3, 7, 0), 7, 0).Should().Equal(Plain);
    }

    [Fact]
    public void R2WithNoV_IsReadAsV1()
    {
        PdfDictionary dict = Dictionary(v: null, r: 2, length: null);

        EncryptionDictionary meta = EncryptionDictionary.Parse(dict)!;
        meta.V.Should().Be(1);
        meta.Algorithm.Should().Be(EncryptionAlgorithm.Rc4_40);
        PdfEncryption.TryOpen(dict, FileId, string.Empty).Should().NotBeNull();
    }

    [Fact]
    public void AnUnsupportedPair_NamesIt()
    {
        PdfDictionary dict = Dictionary(v: 3, r: 3, length: 40);

        Action open = () => PdfEncryption.TryOpen(dict, FileId, string.Empty);

        open.Should().Throw<PdfEncryptionException>().WithMessage("*standard security handler with /V 3 /R 3*");
    }

    // A /Standard dictionary whose /U matches the empty user password.
    private static PdfDictionary Dictionary(int? v, int r, int? length)
    {
        byte[] o = new byte[32];
        for (int i = 0; i < o.Length; i++)
        {
            o[i] = (byte)(0x30 + i);
        }

        const int permissions = -1036;
        byte[] key = StandardSecurityHandler.DeriveKeyR4(string.Empty, o, permissions, FileId, r, 5);
        byte[] u = StandardSecurityHandler.ComputeUValueR4(key, FileId, r);

        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Intern("Filter"), PdfName.Intern("Standard"));
        if (v is int vValue)
        {
            dict.Set(PdfName.Intern("V"), vValue);
        }

        dict.Set(PdfName.Intern("R"), r);
        if (length is int lengthValue)
        {
            dict.Set(PdfName.Intern("Length"), lengthValue);
        }

        dict.Set(PdfName.Intern("O"), new PdfString(o));
        dict.Set(PdfName.Intern("U"), new PdfString(u));
        dict.Set(PdfName.Intern("P"), permissions);
        return dict;
    }

    // Algorithm 1 with a 5-byte file key: RC4 under MD5(key, object, generation).
    private static byte[] EncryptObject(PdfDictionary dict, int revision, int objectNumber, int generation)
    {
        byte[] o = ((PdfString)dict[PdfName.Intern("O")]).Bytes;
        byte[] fileKey = StandardSecurityHandler.DeriveKeyR4(string.Empty, o, -1036, FileId, revision, 5);
        byte[] input = new byte[fileKey.Length + 5];
        fileKey.CopyTo(input, 0);
        input[5] = (byte)(objectNumber & 0xFF);
        input[6] = (byte)((objectNumber >> 8) & 0xFF);
        input[7] = (byte)((objectNumber >> 16) & 0xFF);
        input[8] = (byte)(generation & 0xFF);
        input[9] = (byte)((generation >> 8) & 0xFF);
        byte[] hash = MD5.HashData(input);
        byte[] objectKey = hash.AsSpan(0, Math.Min(fileKey.Length + 5, 16)).ToArray();
        return Rc4.Process(objectKey, Plain);
    }
}
