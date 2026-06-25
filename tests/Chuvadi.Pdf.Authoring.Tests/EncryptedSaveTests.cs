// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.6 — encryption (AES-128 V4/R4, AES-256 V5/R6).
// Tests for encryption-on-write on the authoring builders (LA-26).

using System;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Authoring.Tests;

public sealed class EncryptedSaveTests
{
    [Fact]
    public void Builder_ToByteArray_Aes256_RoundTripsWithPassword()
    {
        byte[] pdf = OneRectangle().ToByteArray(EncryptionOptions.Aes256("user-secret"));

        IsEncrypted(pdf).Should().BeTrue("AES-256 save must emit an /Encrypt dictionary");
        using PdfDocument reopened = PdfDocument.Open(new MemoryStream(pdf), "user-secret", leaveOpen: true);
        reopened.PageCount.Should().Be(1, "the page content survives the encrypt/decrypt round-trip");
    }

    [Fact]
    public void Builder_ToByteArray_Aes128_RoundTripsWithPassword()
    {
        byte[] pdf = OneRectangle().ToByteArray(EncryptionOptions.Aes128("user-secret"));

        IsEncrypted(pdf).Should().BeTrue("AES-128 save must emit an /Encrypt dictionary");
        using PdfDocument reopened = PdfDocument.Open(new MemoryStream(pdf), "user-secret", leaveOpen: true);
        reopened.PageCount.Should().Be(1);
    }

    [Fact]
    public void Builder_ToByteArray_NoArgument_RemainsUnencrypted()
    {
        byte[] pdf = OneRectangle().ToByteArray();

        IsEncrypted(pdf).Should().BeFalse("the parameterless overload must keep the plaintext behaviour");
    }

    [Fact]
    public void Builder_Save_Aes256_WrongPasswordIsRejected()
    {
        byte[] pdf = OneRectangle().ToByteArray(EncryptionOptions.Aes256("correct-password"));

        Action open = () => PdfDocument.Open(new MemoryStream(pdf), "wrong-password");
        open.Should().Throw<Exception>("an incorrect password must not decrypt the document");
    }

    [Fact]
    public void Builder_Save_Stream_Aes256_RoundTrips()
    {
        using MemoryStream output = new MemoryStream();
        OneRectangle().Save(output, EncryptionOptions.Aes256("stream-secret"));

        byte[] pdf = output.ToArray();
        IsEncrypted(pdf).Should().BeTrue();
        using PdfDocument reopened = PdfDocument.Open(new MemoryStream(pdf), "stream-secret", leaveOpen: true);
        reopened.PageCount.Should().Be(1);
    }

    [Fact]
    public void Report_ToByteArray_Aes256_RoundTrips()
    {
        ReportBuilder report = ReportBuilder.Create()
            .AddHeading("Confidential")
            .AddParagraph("This report is encrypted at rest.");

        byte[] pdf = report.ToByteArray(EncryptionOptions.Aes256("report-secret"));

        IsEncrypted(pdf).Should().BeTrue();
        using PdfDocument reopened = PdfDocument.Open(new MemoryStream(pdf), "report-secret", leaveOpen: true);
        reopened.PageCount.Should().BeGreaterThanOrEqualTo(1);
    }

    private static PdfDocumentBuilder OneRectangle()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        builder.AddPage(PageSize.A4).DrawRectangle(50, 50, 200, 100, fill: new Color(0, 0, 255));
        return builder;
    }

    private static bool IsEncrypted(byte[] pdf)
    {
        return Encoding.Latin1.GetString(pdf).Contains("/Encrypt");
    }
}
