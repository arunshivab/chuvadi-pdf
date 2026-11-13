// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.6.5 (crypt filters), §7.4.10 (the Crypt filter)
// PHASE: 3.34.0 (#52d) — /StmF, /StrF and /EFF select their own crypt filters.

using System;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Encryption.Tests;

public sealed class CryptFilters3340Tests
{
    private static readonly byte[] Data = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17 };

    [Fact]
    public void StreamsAndStrings_UseTheFiltersTheyName()
    {
        EncryptionDictionary e = EncryptionDictionary.Parse(V4("/StmF/StdCF/StrF/RC4CF/EFF/Identity"))!;

        e.Algorithm.Should().Be(EncryptionAlgorithm.Aes_128);
        e.StreamAlgorithm.Should().Be(EncryptionAlgorithm.Aes_128);
        e.StringAlgorithm.Should().Be(EncryptionAlgorithm.Rc4_128);
        e.EmbeddedFileAlgorithm.Should().Be(EncryptionAlgorithm.None);
        e.OnlyEmbeddedFilesEncrypted.Should().BeFalse();
    }

    [Fact]
    public void MissingStmFAndStrF_KeepTheDefaultFilter_AndEffFollowsStmF()
    {
        EncryptionDictionary e = EncryptionDictionary.Parse(V4(string.Empty))!;

        e.StreamAlgorithm.Should().Be(EncryptionAlgorithm.Aes_128);
        e.StringAlgorithm.Should().Be(EncryptionAlgorithm.Aes_128);
        e.EmbeddedFileAlgorithm.Should().Be(EncryptionAlgorithm.Aes_128);
    }

    [Fact]
    public void IdentityStreamsAndStrings_MeanOnlyEmbeddedFilesAreEncrypted()
    {
        EncryptionDictionary e = EncryptionDictionary.Parse(V4("/StmF/Identity/StrF/Identity/EFF/StdCF"))!;

        e.OnlyEmbeddedFilesEncrypted.Should().BeTrue();
        e.EmbeddedFileAlgorithm.Should().Be(EncryptionAlgorithm.Aes_128);
    }

    [Fact]
    public void Decryptor_AppliesEachFilterToItsOwnKind()
    {
        byte[] key = new byte[16];
        Decryptor d = new Decryptor(key, EncryptionAlgorithm.None, EncryptionAlgorithm.Rc4_128, EncryptionAlgorithm.None);

        d.DecryptStream(Data, 4, 0, embeddedFile: false).Should().Equal(Data);
        byte[] once = d.DecryptString(Data, 4, 0);
        once.Should().NotEqual(Data);
        d.DecryptString(once, 4, 0).Should().Equal(Data); // RC4 is its own inverse
    }

    [Fact]
    public void CryptFilterOnAStream_NamesItsFilter()
    {
        System.Collections.Generic.Dictionary<string, EncryptionAlgorithm> filters = new() { ["RC4CF"] = EncryptionAlgorithm.Rc4_128 };
        Decryptor d = new Decryptor(new byte[16], EncryptionAlgorithm.Aes_128, EncryptionAlgorithm.Aes_128, EncryptionAlgorithm.Aes_128, filters);

        d.TryDecryptWithCryptFilter("Identity", Data, 4, 0, out byte[] identity).Should().BeTrue();
        identity.Should().Equal(Data);
        d.TryDecryptWithCryptFilter("RC4CF", Data, 4, 0, out byte[] rc4).Should().BeTrue();
        rc4.Should().NotEqual(Data);
        d.TryDecryptWithCryptFilter("Unknown", Data, 4, 0, out byte[] _).Should().BeFalse();
    }

    private static PdfDictionary V4(string selectors)
    {
        PdfDictionary std = new PdfDictionary();
        std.Set(PdfName.Intern("CFM"), PdfName.Intern("AESV2"));
        PdfDictionary rc4 = new PdfDictionary();
        rc4.Set(PdfName.Intern("CFM"), PdfName.Intern("V2"));
        PdfDictionary cf = new PdfDictionary();
        cf.Set(PdfName.Intern("StdCF"), std);
        cf.Set(PdfName.Intern("RC4CF"), rc4);

        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Intern("Filter"), PdfName.Intern("Standard"));
        dict.Set(PdfName.Intern("V"), 4);
        dict.Set(PdfName.Intern("R"), 4);
        dict.Set(PdfName.Intern("Length"), 128);
        dict.Set(PdfName.Intern("CF"), cf);
        string[] parts = selectors.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i + 1 < parts.Length; i += 2)
        {
            dict.Set(PdfName.Intern(parts[i]), PdfName.Intern(parts[i + 1]));
        }

        return dict;
    }
}
