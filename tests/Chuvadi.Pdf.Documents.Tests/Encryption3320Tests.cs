// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.32.0 (D-208 items 2 and 3, D-230) — permission flags and which
//        password opened the document

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Encryption;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Documents.Tests;

public sealed class Encryption3320Tests
{
    public static TheoryData<bool> Algorithms => new TheoryData<bool> { true, false };

    // ── item 3: OpenedWith ───────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Algorithms))]
    public void OpenedWith_ReportsUserOrOwner(bool aes256)
    {
        byte[] pdf = Write(Options(aes256, "user-pw", "owner-pw"));

        OpenedWith(pdf, "user-pw").Should().Be(PdfPasswordKind.User);
        OpenedWith(pdf, "owner-pw").Should().Be(PdfPasswordKind.Owner);
    }

    [Theory]
    [MemberData(nameof(Algorithms))]
    public void OpenedWith_EmptyUserPassword_IsNoPassword(bool aes256)
    {
        byte[] pdf = Write(Options(aes256, string.Empty, "owner-pw"));

        OpenedWith(pdf, string.Empty).Should().Be(PdfPasswordKind.NoPassword);
        OpenedWith(pdf, "owner-pw").Should().Be(PdfPasswordKind.Owner);
    }

    [Theory]
    [MemberData(nameof(Algorithms))]
    public void OpenedWith_SamePasswordForUserAndOwner_IsOwner(bool aes256)
    {
        // Aes128/Aes256 use the user password as the owner password when
        // none is given (D-230 Q4: the owner check comes first).
        byte[] pdf = Write(Options(aes256, "only-pw", null));

        OpenedWith(pdf, "only-pw").Should().Be(PdfPasswordKind.Owner);
    }

    [Theory]
    [MemberData(nameof(Algorithms))]
    public void WrongPassword_StillThrows(bool aes256)
    {
        byte[] pdf = Write(Options(aes256, "user-pw", "owner-pw"));

        Action act = () => PdfDocument.Open(new MemoryStream(pdf), "wrong", leaveOpen: false).Dispose();

        act.Should().Throw<PdfPasswordRequiredException>();
    }

    [Theory]
    [MemberData(nameof(Algorithms))]
    public void UserAndOwnerPasswords_DecryptTheSameContent(bool aes256)
    {
        byte[] pdf = Write(Options(aes256, "user-pw", "owner-pw"));

        using PdfDocument asUser = PdfDocument.Open(new MemoryStream(pdf), "user-pw", leaveOpen: false);
        using PdfDocument asOwner = PdfDocument.Open(new MemoryStream(pdf), "owner-pw", leaveOpen: false);

        asUser.Title.Should().Be("Secret title");
        asOwner.Title.Should().Be("Secret title");
    }

    [Fact]
    public void UnencryptedDocument_HasNoEncryptionInfo()
    {
        byte[] pdf = Write(null);
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);

        doc.Encryption.Should().BeNull();
        doc.Reader.OpenedWith.Should().BeNull();
    }

    // ── item 2: permission flags ─────────────────────────────────────────

    [Fact]
    public void PermissionMask_All_IsTheAllowEverythingValue()
    {
        EncryptionOptions.PermissionMask(PdfPermissions.All).Should().Be(EncryptionOptions.AllPermissionsAllowed);
    }

    [Fact]
    public void PermissionMask_None_ClearsOnlyThePermissionBits()
    {
        int mask = EncryptionOptions.PermissionMask(PdfPermissions.None);

        // Bits 3–6 and 9–12 clear; bits 1–2 clear; bits 7–8 and 13–32 set.
        (mask & 0b1111_0011_1100).Should().Be(0);
        (mask & 0b11).Should().Be(0);
        (mask & 0b1100_0000).Should().Be(0b1100_0000);
        (mask & ~0xFFF).Should().Be(~0xFFF);
    }

    [Fact]
    public void PermissionMask_PrintHighQuality_AlsoAllowsPrint()
    {
        int mask = EncryptionOptions.PermissionMask(PdfPermissions.PrintHighQuality);

        (mask & (1 << 2)).Should().NotBe(0);
        (mask & (1 << 11)).Should().NotBe(0);
    }

    [Theory]
    [MemberData(nameof(PermissionCases))]
    public void WithPermissions_RoundTrips_ThroughFlags(bool aes256, PdfPermissions allowed, PdfPermissions expected)
    {
        byte[] pdf = Write(Options(aes256, "user-pw", "owner-pw").WithPermissions(allowed));
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), "user-pw", leaveOpen: false);

        doc.Encryption!.Flags.Should().Be(expected);
        doc.Encryption.Permissions.Should().Be(EncryptionOptions.PermissionMask(allowed));
    }

    [Fact]
    public void WithPermissions_ReturnsACopy_AndKeepsTheOtherSettings()
    {
        EncryptionOptions original = EncryptionOptions.Aes256("u", "o");
        EncryptionOptions copy = original.WithPermissions(PdfPermissions.Print);

        copy.Should().NotBeSameAs(original);
        original.Permissions.Should().Be(EncryptionOptions.AllPermissionsAllowed);
        copy.Permissions.Should().Be(EncryptionOptions.PermissionMask(PdfPermissions.Print));
        copy.Algorithm.Should().Be(original.Algorithm);
        copy.FileKey.Should().Equal(original.FileKey);
        copy.UserPassword.Should().Be("u");
        copy.OwnerPassword.Should().Be("o");
        copy.EncryptMetadata.Should().Be(original.EncryptMetadata);
    }

    [Theory]
    [MemberData(nameof(Algorithms))]
    public void WithEncryptMetadata_False_LeavesTheMetadataReadable_AndTheDocumentOpens(bool aes256)
    {
        EncryptionOptions options = Options(aes256, "user-pw", "owner-pw").WithEncryptMetadata(false);
        options.EncryptMetadata.Should().BeFalse();
        byte[] pdf = Write(options);

        string raw = System.Text.Encoding.Latin1.GetString(pdf);
        raw.Should().Contain("/EncryptMetadata false");
        raw.Should().Contain("xpacket", "the XMP metadata stream is written in the clear");

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), "user-pw", leaveOpen: false);
        doc.Title.Should().Be("Secret title");
    }

    [Fact]
    public void WithEncryptMetadata_KeepsThePermissions()
    {
        EncryptionOptions options = EncryptionOptions.Aes256("u", "o")
            .WithPermissions(PdfPermissions.Print)
            .WithEncryptMetadata(false);

        options.Permissions.Should().Be(EncryptionOptions.PermissionMask(PdfPermissions.Print));
        options.EncryptMetadata.Should().BeFalse();
    }

    [Fact]
    public void Default_StillAllowsEverything()
    {
        byte[] pdf = Write(EncryptionOptions.Aes256("user-pw", "owner-pw"));
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), "user-pw", leaveOpen: false);

        doc.Encryption!.Flags.Should().Be(PdfPermissions.All);
    }

    public static TheoryData<bool, PdfPermissions, PdfPermissions> PermissionCases() => new TheoryData<bool, PdfPermissions, PdfPermissions>
    {
        { false, PdfPermissions.None, PdfPermissions.None },
        { true, PdfPermissions.None, PdfPermissions.None },
        { false, PdfPermissions.Print | PdfPermissions.CopyContents, PdfPermissions.Print | PdfPermissions.CopyContents },
        { true, PdfPermissions.Print | PdfPermissions.CopyContents, PdfPermissions.Print | PdfPermissions.CopyContents },
        { true, PdfPermissions.FillForms | PdfPermissions.ExtractAccessibility, PdfPermissions.FillForms | PdfPermissions.ExtractAccessibility },
        { true, PdfPermissions.PrintHighQuality, PdfPermissions.Print | PdfPermissions.PrintHighQuality },
        { true, PdfPermissions.All, PdfPermissions.All },
    };

    // ── D-279: EffectiveFlags ────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EffectiveFlags_OwnerGetsEverything_UserGetsTheFileFlags(bool aes256)
    {
        PdfPermissions allowed = PdfPermissions.Print | PdfPermissions.CopyContents;
        byte[] pdf = Write(Options(aes256, "user-pw", "owner-pw").WithPermissions(allowed));

        using (PdfDocument owner = PdfDocument.Open(new MemoryStream(pdf), "owner-pw", leaveOpen: false))
        {
            owner.Encryption!.EffectiveFlags.Should().Be(PdfPermissions.All);
            owner.Encryption.Flags.Should().Be(allowed, "Flags still reports what the file states");
        }

        using (PdfDocument user = PdfDocument.Open(new MemoryStream(pdf), "user-pw", leaveOpen: false))
        {
            user.Encryption!.EffectiveFlags.Should().Be(allowed);
        }
    }

    [Fact]
    public void EffectiveFlags_EmptyUserPassword_GetsTheFileFlags()
    {
        byte[] pdf = Write(Options(true, string.Empty, "owner-pw").WithPermissions(PdfPermissions.None));

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);

        doc.Encryption!.OpenedWith.Should().Be(PdfPasswordKind.NoPassword);
        doc.Encryption.EffectiveFlags.Should().Be(PdfPermissions.None);
    }

    // ── D-278: /P written unsigned ───────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnsignedP_OpensAndDecrypts_WithTheSameFlags(bool aes256)
    {
        PdfPermissions allowed = PdfPermissions.Print | PdfPermissions.CopyContents;
        int signed = EncryptionOptions.PermissionMask(allowed);
        string unsigned = unchecked((uint)signed).ToString(System.Globalization.CultureInfo.InvariantCulture);
        byte[] pdf = ReplaceAndFixOffsets(
            Write(Options(aes256, "user-pw", "owner-pw").WithPermissions(allowed)),
            "/P " + signed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "/P " + unsigned);

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), "user-pw", leaveOpen: false);

        doc.Encryption!.Permissions.Should().Be(signed);
        doc.Encryption.Flags.Should().Be(allowed);
        PdfDictionary info = doc.Objects.ResolveAs<PdfDictionary>(doc.Reader.Trailer[PdfName.Intern("Info")])!;
        ((PdfString)info[PdfName.Intern("Title")]).Bytes.Should().Equal(System.Text.Encoding.ASCII.GetBytes("Secret title"), "the file key uses /P");
    }

    [Fact]
    public void UnsignedP_InTheEncryptionDictionary_IsReadFromItsLow32Bits()
    {
        PdfDictionary dict = new();
        dict.Set(PdfName.Intern("Filter"), PdfName.Intern("Standard"));
        dict.Set(PdfName.Intern("V"), 2);
        dict.Set(PdfName.Intern("R"), 3);
        dict.Set(PdfName.Intern("P"), new PdfReal(4294963392));

        EncryptionDictionary.Parse(dict)!.Permissions.Should().Be(-3904);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    // Replaces one ASCII run in a classic-xref file and rewrites the xref
    // offsets and startxref to match, so the edited file is well formed.
    private static byte[] ReplaceAndFixOffsets(byte[] pdf, string find, string replace)
    {
        string text = System.Text.Encoding.Latin1.GetString(pdf);
        text.Should().Contain(find);
        text = text.Replace(find, replace, StringComparison.Ordinal);

        int xref = text.LastIndexOf("\nxref\n", StringComparison.Ordinal) + 1;
        xref.Should().BePositive("the test writer uses a classic xref table");
        System.Text.StringBuilder fixedText = new System.Text.StringBuilder(text.Substring(0, xref));
        string[] lines = text.Substring(xref).Split('\n');
        int first = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.Length == 19 && line[10] == ' ' && line.EndsWith(" n", StringComparison.Ordinal) && first > 0)
            {
                int number = first++;
                int offset = text.IndexOf("\n" + number.ToString(System.Globalization.CultureInfo.InvariantCulture) + " 0 obj", StringComparison.Ordinal) + 1;
                line = offset.ToString("D10", System.Globalization.CultureInfo.InvariantCulture) + line.Substring(10);
            }
            else if (line.Length == 19 && line.EndsWith(" f", StringComparison.Ordinal))
            {
                first = 1;
            }
            else if (i > 0 && lines[i - 1] == "startxref")
            {
                line = xref.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            fixedText.Append(line);
            if (i < lines.Length - 1)
            {
                fixedText.Append('\n');
            }
        }

        return System.Text.Encoding.Latin1.GetBytes(fixedText.ToString());
    }

    private static EncryptionOptions Options(bool aes256, string user, string? owner) =>
        aes256 ? EncryptionOptions.Aes256(user, owner) : EncryptionOptions.Aes128(user, owner);

    private static PdfPasswordKind? OpenedWith(byte[] pdf, string password)
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), password, leaveOpen: false);
        doc.Reader.OpenedWith.Should().Be(doc.Encryption!.OpenedWith);
        return doc.Encryption.OpenedWith;
    }

    private static byte[] Write(EncryptionOptions? encryption)
    {
        PdfObjectId catalogId = new(1, 0);
        PdfObjectId pagesId = new(2, 0);
        PdfObjectId pageId = new(3, 0);
        PdfObjectId infoId = new(4, 0);

        PdfDictionary pageDict = new();
        pageDict.Set(PdfName.Type, PdfName.Page);
        pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
        pageDict.Set(PdfName.MediaBox, new PdfArray([
            new PdfInteger(0), new PdfInteger(0),
            new PdfInteger(612), new PdfInteger(792),
        ]));

        PdfDictionary pagesDict = new();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, new PdfArray([new PdfReference(pageId)]));
        pagesDict.Set(PdfName.Count, 1);

        PdfDictionary catalogDict = new();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

        PdfDictionary infoDict = new();
        infoDict.Set(PdfName.Intern("Title"), new PdfString(System.Text.Encoding.ASCII.GetBytes("Secret title")));

        List<PdfIndirectObject> objects = [
            new PdfIndirectObject(catalogId, catalogDict),
            new PdfIndirectObject(pagesId, pagesDict),
            new PdfIndirectObject(pageId, pageDict),
            new PdfIndirectObject(infoId, infoDict),
        ];

        PdfDictionary trailer = new();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));
        trailer.Set(PdfName.Intern("Info"), new PdfReference(infoId));

        using MemoryStream output = new MemoryStream();
        PdfWriter.Write(output, objects, trailer, encryption);
        return output.ToArray();
    }
}
