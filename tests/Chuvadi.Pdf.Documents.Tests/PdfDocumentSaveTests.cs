// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.6 — encryption.
// Tests for PdfDocument.Save(Stream, EncryptionOptions?) (LA-26).

using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.IO;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Documents.Tests;

public sealed class PdfDocumentSaveTests
{
    [Fact]
    public void Save_Aes256_RoundTripsWithPassword()
    {
        byte[] encrypted;
        using (PdfDocument source = PdfDocument.Open(new MemoryStream(MinimalPdf()), leaveOpen: true))
        using (MemoryStream output = new MemoryStream())
        {
            source.Save(output, EncryptionOptions.Aes256("doc-secret"));
            encrypted = output.ToArray();
        }

        IsEncrypted(encrypted).Should().BeTrue("Save with options must emit an /Encrypt dictionary");
        using PdfDocument reopened = PdfDocument.Open(new MemoryStream(encrypted), "doc-secret", leaveOpen: true);
        reopened.PageCount.Should().Be(1);
    }

    [Fact]
    public void Save_NullEncryption_OnPlaintextSource_StaysPlaintext()
    {
        using PdfDocument source = PdfDocument.Open(new MemoryStream(MinimalPdf()), leaveOpen: true);
        using MemoryStream output = new MemoryStream();
        source.Save(output);

        IsEncrypted(output.ToArray()).Should().BeFalse("a null-encryption save must not add encryption");
    }

    [Fact]
    public void Save_NullEncryption_OnEncryptedSource_ProducesDecryptedCopy()
    {
        byte[] encrypted;
        using (PdfDocument source = PdfDocument.Open(new MemoryStream(MinimalPdf()), leaveOpen: true))
        using (MemoryStream firstPass = new MemoryStream())
        {
            source.Save(firstPass, EncryptionOptions.Aes256("doc-secret"));
            encrypted = firstPass.ToArray();
        }

        byte[] decrypted;
        using (PdfDocument enc = PdfDocument.Open(new MemoryStream(encrypted), "doc-secret", leaveOpen: true))
        using (MemoryStream secondPass = new MemoryStream())
        {
            enc.Save(secondPass, null);
            decrypted = secondPass.ToArray();
        }

        IsEncrypted(decrypted).Should().BeFalse("Save(stream, null) on an encrypted source yields a decrypted copy");
        using PdfDocument reopened = PdfDocument.Open(new MemoryStream(decrypted), leaveOpen: true);
        reopened.PageCount.Should().Be(1);
    }

    private static bool IsEncrypted(byte[] pdf)
    {
        return Encoding.Latin1.GetString(pdf).Contains("/Encrypt");
    }

    private static byte[] MinimalPdf()
    {
        List<byte[]> bodies = new List<byte[]>
        {
            Encoding.Latin1.GetBytes("<</Type/Catalog/Pages 2 0 R>>"),
            Encoding.Latin1.GetBytes("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Encoding.Latin1.GetBytes("<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]/Contents 4 0 R>>"),
            Encoding.Latin1.GetBytes("<</Length 25>>\nstream\n1 0 0 rg 10 10 50 50 re f\nendstream"),
        };

        using MemoryStream ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.Latin1.GetBytes(s));

        Write("%PDF-1.7\n");
        List<long> offsets = new List<long>();
        for (int i = 0; i < bodies.Count; i++)
        {
            offsets.Add(ms.Length);
            Write((i + 1) + " 0 obj ");
            ms.Write(bodies[i]);
            Write(" endobj\n");
        }

        long xref = ms.Length;
        Write("xref\n0 " + (bodies.Count + 1) + "\n0000000000 65535 f \n");
        foreach (long off in offsets)
        {
            Write(off.ToString("D10") + " 00000 n \n");
        }

        Write("trailer <</Size " + (bodies.Count + 1) + "/Root 1 0 R>>\nstartxref\n" + xref + "\n%%EOF");
        return ms.ToArray();
    }
}
