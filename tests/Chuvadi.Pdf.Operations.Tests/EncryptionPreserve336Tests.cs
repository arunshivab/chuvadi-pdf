// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.6 — Encryption
// PHASE: 3.36.0 (D-410 D, item 4, decided D-417(3)) — compressing an encrypted
//        document with CompressionOptions.PreserveEncryption keeps its exact
//        protection: both the owner and user passwords still open the output,
//        without either being known. A legacy RC4 key length that is not a
//        standard 128-bit key is refused rather than re-written weakly.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Encryption;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

public sealed class EncryptionPreserve336Tests
{
    // The 32-byte password padding (ISO 32000-1:2008 Algorithm 2, step a).
    private static readonly byte[] PasswordPadding =
    {
        0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41, 0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
        0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80, 0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A,
    };

    private static readonly byte[] FileId =
    {
        0x81, 0x84, 0x8F, 0x4F, 0x84, 0xDD, 0xD7, 0xF6, 0x4A, 0xD9, 0xA6, 0x99, 0xFF, 0x6B, 0xAD, 0x71,
    };

    private const int Permissions = -44;

    [Fact]
    public void PreserveEncryption_KeepsBothPasswords()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        builder.AddPage(PageSize.A4);
        byte[] encrypted = builder.ToByteArray(EncryptionOptions.Aes128("user", "owner"));

        using PdfDocument document = PdfDocument.Open(new MemoryStream(encrypted), "user");
        using MemoryStream output = new MemoryStream();
        CompressionResult result = PdfCompressor.Compress(
            document, output, new CompressionOptions { PreserveEncryption = true });
        byte[] written = output.ToArray();

        result.SkipReason.Should().Be(CompressionSkipReason.None);
        Opens(written, "user").Should().BeTrue();
        Opens(written, "owner").Should().BeTrue();
        Opens(written, string.Empty).Should().BeFalse();
        Opens(written, "wrong").Should().BeFalse();
        StillEncrypted(written).Should().BeTrue();
    }

    [Fact]
    public void PreserveEncryption_RefusesWeakRc4()
    {
        byte[] rc4 = Rc4Document();

        using PdfDocument document = PdfDocument.Open(new MemoryStream(rc4), string.Empty);
        using MemoryStream output = new MemoryStream();
        Action compress = () => PdfCompressor.Compress(
            document, output, new CompressionOptions { PreserveEncryption = true });

        compress.Should().Throw<PdfEncryptionException>().WithMessage("*RC4*");
    }

    [Fact]
    public void WithoutPreserveOrAllow_EncryptedDocumentIsSkipped()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        builder.AddPage(PageSize.A4);
        byte[] encrypted = builder.ToByteArray(EncryptionOptions.Aes128("user", "owner"));

        using PdfDocument document = PdfDocument.Open(new MemoryStream(encrypted), "user");
        using MemoryStream output = new MemoryStream();
        CompressionResult result = PdfCompressor.Compress(document, output, new CompressionOptions());

        result.SkipReason.Should().Be(CompressionSkipReason.Encrypted);
        output.Length.Should().Be(0);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static bool Opens(byte[] pdf, string password)
    {
        try
        {
            using PdfDocument document = PdfDocument.Open(new MemoryStream(pdf), password);
            return document.PageCount >= 0;
        }
        catch (PdfEncryptionException)
        {
            return false;
        }
        catch (PdfPasswordRequiredException)
        {
            return false;
        }
    }

    private static bool StillEncrypted(byte[] pdf)
    {
        using PdfDocument document = PdfDocument.Open(new MemoryStream(pdf), "user");
        return document.Encryption is not null;
    }

    // Builds a minimal V1/R2 (40-bit RC4) document that opens with the empty
    // user password, using only the public key-derivation and RC4 primitives.
    private static byte[] Rc4Document()
    {
        byte[] ownerBase = new byte[5];
        Array.Copy(MD5.HashData(PasswordPadding), ownerBase, 5);
        byte[] o = Rc4.Process(ownerBase, PasswordPadding);
        byte[] fileKey = StandardSecurityHandler.DeriveKeyR4(string.Empty, o, Permissions, FileId, 2, 5);
        byte[] u = Rc4.Process(fileKey, PasswordPadding);

        byte[] content = Rc4.Process(PerObjectKey(fileKey, 4, 0), Encoding.Latin1.GetBytes("BT /F1 12 Tf 72 700 Td (secret) Tj ET"));

        List<byte[]> bodies = new List<byte[]>
        {
            Latin1("<</Type/Catalog/Pages 2 0 R>>"),
            Latin1("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Latin1("<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]/Resources<</Font<</F1 5 0 R>>>>/Contents 4 0 R>>"),
            StreamBody(content),
            Latin1("<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>"),
            Latin1("<</Filter/Standard/V 1/R 2/Length 40/P " + Permissions.ToString(CultureInfo.InvariantCulture)
                + "/O <" + Hex(o) + ">/U <" + Hex(u) + ">>>"),
        };

        using MemoryStream ms = new MemoryStream();
        ms.Write(Latin1("%PDF-1.4\n"));
        List<long> offsets = new List<long>();
        for (int i = 0; i < bodies.Count; i++)
        {
            offsets.Add(ms.Length);
            ms.Write(Latin1((i + 1).ToString(CultureInfo.InvariantCulture) + " 0 obj\n"));
            ms.Write(bodies[i]);
            ms.Write(Latin1("\nendobj\n"));
        }

        long xref = ms.Length;
        ms.Write(Latin1("xref\n0 " + (bodies.Count + 1).ToString(CultureInfo.InvariantCulture) + "\n0000000000 65535 f \n"));
        foreach (long offset in offsets)
        {
            ms.Write(Latin1(offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n"));
        }

        string idHex = Hex(FileId);
        ms.Write(Latin1(
            "trailer <</Size " + (bodies.Count + 1).ToString(CultureInfo.InvariantCulture)
            + "/Root 1 0 R/Encrypt 6 0 R/ID[<" + idHex + "><" + idHex + ">]>>\nstartxref\n"
            + xref.ToString(CultureInfo.InvariantCulture) + "\n%%EOF"));
        return ms.ToArray();
    }

    private static byte[] PerObjectKey(byte[] fileKey, int objectNumber, int generation)
    {
        byte[] input = new byte[fileKey.Length + 5];
        Array.Copy(fileKey, input, fileKey.Length);
        int p = fileKey.Length;
        input[p++] = (byte)(objectNumber & 0xFF);
        input[p++] = (byte)((objectNumber >> 8) & 0xFF);
        input[p++] = (byte)((objectNumber >> 16) & 0xFF);
        input[p++] = (byte)(generation & 0xFF);
        input[p++] = (byte)((generation >> 8) & 0xFF);
        byte[] hash = MD5.HashData(input);
        byte[] key = new byte[Math.Min(fileKey.Length + 5, 16)];
        Array.Copy(hash, key, key.Length);
        return key;
    }

    private static byte[] StreamBody(byte[] content)
    {
        using MemoryStream ms = new MemoryStream();
        ms.Write(Latin1("<</Length " + content.Length.ToString(CultureInfo.InvariantCulture) + ">>\nstream\n"));
        ms.Write(content);
        ms.Write(Latin1("\nendstream"));
        return ms.ToArray();
    }

    private static string Hex(byte[] bytes)
    {
        StringBuilder sb = new StringBuilder(bytes.Length * 2);
        foreach (byte b in bytes)
        {
            sb.Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    private static byte[] Latin1(string text) => Encoding.Latin1.GetBytes(text);
}
