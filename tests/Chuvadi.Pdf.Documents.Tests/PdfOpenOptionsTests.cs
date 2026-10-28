// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.24 — embedding API: password-required signal (A1) and open-time
// limits (A4).

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Documents.Tests;

public sealed class PdfOpenOptionsTests
{
    [Fact]
    public void EncryptedPdfOpenedWithoutPassword_ThrowsPasswordRequired()
    {
        using MemoryStream pdf = Pdf(1, EncryptionOptions.Aes256("secret"));

        PdfPasswordRequiredException ex = Assert.Throws<PdfPasswordRequiredException>(() => PdfDocument.Open(pdf, leaveOpen: true));

        ex.PasswordSupplied.Should().BeFalse();
        ex.Should().BeAssignableTo<PdfParseException>("callers catching PdfParseException before 3.24 still catch it");
    }

    [Fact]
    public void EncryptedPdfOpenedWithWrongPassword_SaysAPasswordWasSupplied()
    {
        using MemoryStream pdf = Pdf(1, EncryptionOptions.Aes128("secret"));

        PdfPasswordRequiredException ex = Assert.Throws<PdfPasswordRequiredException>(
            () => PdfDocument.Open(pdf, new PdfOpenOptions { Password = "wrong", LeaveOpen = true }));

        ex.PasswordSupplied.Should().BeTrue();
    }

    [Fact]
    public void TheOwnerPassword_OpensAnAes128File()
    {
        // R4 derives the key from the user password; the owner password
        // opens the file by recovering it from /O (Algorithm 7).
        using MemoryStream pdf = Pdf(1, EncryptionOptions.Aes128("user", "owner"));

        using PdfDocument doc = PdfDocument.Open(pdf, new PdfOpenOptions { Password = "owner", LeaveOpen = true });

        doc.PageCount.Should().Be(1);
    }

    [Fact]
    public void AnImpossibleKeyLength_IsAnEncryptionError()
    {
        string encrypt = "<</Filter/Standard/V 2/R 3/Length 9999/O<" + new string('0', 64) + ">/U<" + new string('0', 64) + ">/P -4>>";
        string[] bodies =
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[]/Count 0>>",
            encrypt,
        };
        System.Text.StringBuilder sb = new System.Text.StringBuilder("%PDF-1.7\n");
        List<int> offsets = new List<int>();
        for (int i = 0; i < bodies.Length; i++)
        {
            offsets.Add(sb.Length);
            sb.Append(i + 1).Append(" 0 obj\n").Append(bodies[i]).Append("\nendobj\n");
        }

        int xref = sb.Length;
        sb.Append("xref\n0 4\n0000000000 65535 f \n");
        foreach (int off in offsets)
        {
            sb.Append(off.ToString("D10", System.Globalization.CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        sb.Append("trailer\n<</Size 4/Root 1 0 R/Encrypt 3 0 R/ID[<00112233445566778899AABBCCDDEEFF><00112233445566778899AABBCCDDEEFF>]>>\nstartxref\n")
            .Append(xref).Append("\n%%EOF\n");
        using MemoryStream pdf = new MemoryStream(System.Text.Encoding.Latin1.GetBytes(sb.ToString()));

        Assert.Throws<PdfEncryptionException>(() => PdfDocument.RequiresPassword(pdf));
        Assert.Throws<PdfEncryptionException>(() => PdfDocument.Open(pdf, new PdfOpenOptions { LeaveOpen = true }));
    }

    [Fact]
    public void RequiresPassword_TellsBeforeOpening_AndRestoresThePosition()
    {
        using MemoryStream locked = Pdf(1, EncryptionOptions.Aes256("secret"));
        using MemoryStream open = Pdf(1, EncryptionOptions.Aes256(string.Empty));
        using MemoryStream plain = Pdf(1, null);
        locked.Position = 5;

        PdfDocument.RequiresPassword(locked).Should().BeTrue();
        locked.Position.Should().Be(5);
        PdfDocument.RequiresPassword(open).Should().BeFalse("the empty user password opens it");
        PdfDocument.RequiresPassword(plain).Should().BeFalse();
    }

    [Fact]
    public void OpenWithOptions_UsesThePasswordAndKeepsTheLimits()
    {
        using MemoryStream pdf = Pdf(2, EncryptionOptions.Aes256("secret"));
        PdfLimits limits = new PdfLimits { MaxPageCount = 5, MaxContentOperators = 1000 };

        using PdfDocument doc = PdfDocument.Open(pdf, new PdfOpenOptions { Password = "secret", Limits = limits, LeaveOpen = true });

        doc.PageCount.Should().Be(2);
        doc.Limits.Should().BeSameAs(limits);
        doc.Objects.Limits.Should().BeSameAs(limits);
    }

    [Fact]
    public void InputLargerThanMaxInputBytes_IsRefused()
    {
        using MemoryStream pdf = Pdf(1, null);

        PdfLimitExceededException ex = Assert.Throws<PdfLimitExceededException>(() => PdfDocument.Open(
            pdf, new PdfOpenOptions { Limits = new PdfLimits { MaxInputBytes = 100 }, LeaveOpen = true }));

        ex.LimitName.Should().Be(nameof(PdfLimits.MaxInputBytes));
        ex.Limit.Should().Be(100);
    }

    [Fact]
    public void MorePagesThanMaxPageCount_IsRefused()
    {
        using MemoryStream pdf = Pdf(3, null);

        PdfLimitExceededException ex = Assert.Throws<PdfLimitExceededException>(() => PdfDocument.Open(
            pdf, new PdfOpenOptions { Limits = new PdfLimits { MaxPageCount = 2 }, LeaveOpen = true }));

        ex.LimitName.Should().Be(nameof(PdfLimits.MaxPageCount));
        ex.Actual.Should().Be(3);
    }

    [Fact]
    public async Task OpenAsyncWithOptions_StopsReadingPastMaxInputBytes()
    {
        using MemoryStream pdf = Pdf(1, null);

        Func<Task> act = () => PdfDocument.OpenAsync(pdf, new PdfOpenOptions { Limits = new PdfLimits { MaxInputBytes = 64 } });

        await act.Should().ThrowAsync<PdfLimitExceededException>();
    }

    [Fact]
    public async Task OpenAsyncWithOptions_OpensAnEncryptedFile()
    {
        using MemoryStream pdf = Pdf(1, EncryptionOptions.Aes256("secret"));

        using PdfDocument doc = await PdfDocument.OpenAsync(pdf, new PdfOpenOptions { Password = "secret" });

        doc.PageCount.Should().Be(1);
    }

    private static MemoryStream Pdf(int pages, EncryptionOptions? encryption)
    {
        PdfObjectId catalogId = new(1, 0);
        PdfObjectId pagesId = new(2, 0);
        List<PdfIndirectObject> objects = new List<PdfIndirectObject>();
        List<PdfPrimitive> kids = new List<PdfPrimitive>();
        for (int i = 0; i < pages; i++)
        {
            PdfObjectId pageId = new(3 + i, 0);
            PdfDictionary page = new();
            page.Set(PdfName.Type, PdfName.Page);
            page.Set(PdfName.Parent, new PdfReference(pagesId));
            page.Set(PdfName.MediaBox, new PdfArray([new PdfInteger(0), new PdfInteger(0), new PdfInteger(200), new PdfInteger(200)]));
            objects.Add(new PdfIndirectObject(pageId, page));
            kids.Add(new PdfReference(pageId));
        }

        PdfDictionary pagesDict = new();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, new PdfArray(kids.ToArray()));
        pagesDict.Set(PdfName.Count, pages);
        PdfDictionary catalog = new();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(pagesId));
        objects.Add(new PdfIndirectObject(catalogId, catalog));
        objects.Add(new PdfIndirectObject(pagesId, pagesDict));

        PdfDictionary trailer = new();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));
        MemoryStream output = new MemoryStream();
        if (encryption is null)
        {
            PdfWriter.Write(output, objects, trailer);
        }
        else
        {
            PdfWriter.Write(output, objects, trailer, encryption);
        }

        output.Position = 0;
        return output;
    }
}
