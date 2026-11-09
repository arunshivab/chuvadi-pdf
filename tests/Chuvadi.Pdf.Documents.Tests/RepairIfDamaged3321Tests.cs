// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.5 (file structure)
// PHASE: 3.32.1 (D-280) — PdfOpenOptions.RepairIfDamaged: a file whose
//        structure cannot be read is repaired in memory and opened.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Chuvadi.Pdf.Encryption;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Documents.Tests;

public sealed class RepairIfDamaged3321Tests
{
    private static readonly PdfOpenOptions Repair = new() { RepairIfDamaged = true };

    // ── damage that the repair fixes ─────────────────────────────────────

    [Fact]
    public void WrongStartXref_ThrowsByDefault_AndOpensWithRepair()
    {
        byte[] pdf = WrongStartXref(Write(pages: 3));

        Action plain = () => PdfDocument.Open(new MemoryStream(pdf), new PdfOpenOptions()).Dispose();
        plain.Should().Throw<PdfParseException>("without the option nothing changes");

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), Repair);
        doc.PageCount.Should().Be(3);
        doc.Title.Should().Be("Repair test", "the file's own /Info is kept");
        doc.IsRecovered.Should().BeTrue();
        doc.Warnings[0].Should().Contain("repaired in memory").And.Contain("objects recovered");
    }

    [Fact]
    public void JunkBeforeTheHeader_AndNoXref_OpensWithRepair()
    {
        byte[] good = Write(pages: 2);
        string text = Encoding.Latin1.GetString(good);
        string cut = text.Substring(0, text.LastIndexOf("xref", StringComparison.Ordinal));
        byte[] pdf = Encoding.Latin1.GetBytes("JUNK JUNK\n" + cut);

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), Repair);

        doc.PageCount.Should().Be(2);
        doc.Warnings[0].Should().Contain("repaired in memory").And.Contain("junk before the header");
    }

    [Fact]
    public void RepairedDocument_HasNoAddedMetadata()
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(WrongStartXref(Write(pages: 1))), Repair);

        doc.Info!.ContainsKey(PdfName.Intern("Producer")).Should().BeFalse("the in-memory repair adds no /Info entries");
        doc.Objects.ResolveAs<PdfDictionary>(doc.Reader.Trailer[PdfName.Root])!
            .ContainsKey(PdfName.Intern("Metadata")).Should().BeFalse("nor an XMP stream");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EncryptedFile_KeepsItsEncryption(bool aes256)
    {
        EncryptionOptions encryption = aes256 ? EncryptionOptions.Aes256("user-pw", "owner-pw") : EncryptionOptions.Aes128("user-pw", "owner-pw");
        byte[] pdf = WrongStartXref(Write(pages: 1, encryption));

        using (PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), new PdfOpenOptions { Password = "user-pw", RepairIfDamaged = true }))
        {
            doc.Title.Should().Be("Repair test", "the title decrypts, so /Encrypt and /ID were kept");
            doc.Encryption!.OpenedWith.Should().Be(PdfPasswordKind.User);
        }

        Action noPassword = () => PdfDocument.Open(new MemoryStream(pdf), Repair).Dispose();
        noPassword.Should().Throw<PdfPasswordRequiredException>();
    }

    // ── what does not trigger a repair ───────────────────────────────────

    [Fact]
    public void UndamagedFile_OpensAsBefore_WithNoRepairWarning()
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(Write(pages: 2)), Repair);

        doc.PageCount.Should().Be(2);
        doc.Warnings.Should().BeEmpty();
        doc.IsRecovered.Should().BeFalse();
    }

    [Fact]
    public void MissingPassword_OnAnUndamagedFile_IsNotRepaired()
    {
        byte[] pdf = Write(pages: 1, EncryptionOptions.Aes256("user-pw", "owner-pw"));

        Action act = () => PdfDocument.Open(new MemoryStream(pdf), Repair).Dispose();

        act.Should().Throw<PdfPasswordRequiredException>();
    }

    [Fact]
    public void Limits_StillApply()
    {
        byte[] pdf = WrongStartXref(Write(pages: 3));

        Action act = () => PdfDocument.Open(
            new MemoryStream(pdf), new PdfOpenOptions { RepairIfDamaged = true, Limits = new PdfLimits { MaxPageCount = 2 } }).Dispose();

        act.Should().Throw<PdfLimitExceededException>();
    }

    [Fact]
    public void NothingRecoverable_ThrowsWithBothReasons()
    {
        byte[] junk = Encoding.ASCII.GetBytes("%PDF-1.4\nthis is not a pdf at all\n%%EOF\n");

        Action act = () => PdfDocument.Open(new MemoryStream(junk), Repair).Dispose();

        act.Should().Throw<PdfParseException>().WithMessage("*could not be read*could not be repaired*");
    }

    // ── streams and overloads ────────────────────────────────────────────

    [Fact]
    public void CallerStream_IsDisposed_UnlessLeaveOpen()
    {
        byte[] pdf = WrongStartXref(Write(pages: 1));

        MemoryStream owned = new MemoryStream(pdf);
        using (PdfDocument doc = PdfDocument.Open(owned, Repair))
        {
            doc.PageCount.Should().Be(1);
        }

        owned.CanRead.Should().BeFalse("the document owned the caller's stream");

        MemoryStream kept = new MemoryStream(pdf);
        using (PdfDocument doc = PdfDocument.Open(kept, new PdfOpenOptions { RepairIfDamaged = true, LeaveOpen = true }))
        {
            doc.PageCount.Should().Be(1);
        }

        kept.CanRead.Should().BeTrue();
    }

    [Fact]
    public async Task PathAndAsyncOverloads_CarryTheOption()
    {
        byte[] pdf = WrongStartXref(Write(pages: 2));
        string path = Path.Combine(Path.GetTempPath(), "chuvadi-repair-" + Guid.NewGuid().ToString("N") + ".pdf");
        File.WriteAllBytes(path, pdf);
        try
        {
            using (PdfDocument doc = PdfDocument.Open(path, Repair))
            {
                doc.PageCount.Should().Be(2);
            }

            using PdfDocument fromAsync = await PdfDocument.OpenAsync(new MemoryStream(pdf), Repair);
            fromAsync.PageCount.Should().Be(2);
            File.ReadAllBytes(path).Should().Equal(pdf, "the file on disk is not changed");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PublicRepair_IsUnchanged_AndStillAddsXmpMetadata()
    {
        using MemoryStream output = new MemoryStream();
        RepairReport report = PdfRepairer.Repair(new MemoryStream(WrongStartXref(Write(pages: 1))), output);

        report.Repaired.Should().BeTrue();
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(output.ToArray()), leaveOpen: false);
        doc.Objects.ResolveAs<PdfDictionary>(doc.Reader.Trailer[PdfName.Root])!
            .ContainsKey(PdfName.Intern("Metadata")).Should().BeTrue("PdfRepairer.Repair still adds the XMP stream, as before");
    }

    // ── helpers ──────────────────────────────────────────────────────────

    // Points startxref at offset 9, inside the first object.
    private static byte[] WrongStartXref(byte[] pdf)
    {
        string text = Encoding.Latin1.GetString(pdf);
        int at = text.LastIndexOf("startxref", StringComparison.Ordinal);
        at.Should().BePositive();
        int lineStart = text.IndexOf('\n', at) + 1;
        int lineEnd = text.IndexOf('\n', lineStart);
        string broken = text.Substring(0, lineStart) + "9".PadLeft(lineEnd - lineStart) + text.Substring(lineEnd);
        return Encoding.Latin1.GetBytes(broken);
    }

    // A document with the given number of pages, a /Title and optional encryption.
    private static byte[] Write(int pages, EncryptionOptions? encryption = null)
    {
        List<PdfIndirectObject> objects = new List<PdfIndirectObject>();
        PdfArray kids = new PdfArray();
        int next = 4;
        for (int i = 0; i < pages; i++)
        {
            PdfDictionary page = new PdfDictionary();
            page.Set(PdfName.Type, PdfName.Page);
            page.Set(PdfName.Parent, new PdfReference(2, 0));
            page.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[] { new PdfInteger(0), new PdfInteger(0), new PdfInteger(200), new PdfInteger(200) }));
            PdfObjectId id = new PdfObjectId(next++, 0);
            objects.Add(new PdfIndirectObject(id, page));
            kids.Add(new PdfReference(id));
        }

        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(2, 0));
        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, kids);
        pagesDict.Set(PdfName.Count, pages);
        PdfDictionary info = new PdfDictionary();
        info.Set(PdfName.Intern("Title"), new PdfString(Encoding.ASCII.GetBytes("Repair test")));
        objects.Insert(0, new PdfIndirectObject(new PdfObjectId(3, 0), info));
        objects.Insert(0, new PdfIndirectObject(new PdfObjectId(2, 0), pagesDict));
        objects.Insert(0, new PdfIndirectObject(new PdfObjectId(1, 0), catalog));

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(1, 0));
        trailer.Set(PdfName.Intern("Info"), new PdfReference(3, 0));
        using MemoryStream output = new MemoryStream();
        PdfWriter.Write(output, objects, trailer, encryption, SynthesizedMetadata.None);
        return output.ToArray();
    }
}
