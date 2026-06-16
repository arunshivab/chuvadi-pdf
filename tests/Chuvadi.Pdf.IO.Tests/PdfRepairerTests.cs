// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.5 — File structure, §7.5.7 — Object streams
// PHASE: Phase 1 — Chuvadi.Pdf.IO tests
//
// Exercises PdfRepairer across the structural-damage classes it targets:
// broken startxref, missing cross-reference/trailer, leading junk, truncation,
// corrupt stream /Length, and incremental-update duplication. Each scenario
// starts from a writer-produced two-page PDF, damages the bytes, and asserts
// the repaired output reopens with content intact.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.IO.Tests;

public sealed class PdfRepairerTests
{
    // ── Fixture ───────────────────────────────────────────────────────────

    private static byte[] BuildTwoPagePdf()
    {
        PdfObjectId catalogId = new(1, 0);
        PdfObjectId pagesId = new(2, 0);
        PdfObjectId page1Id = new(3, 0);
        PdfObjectId page2Id = new(4, 0);
        PdfObjectId content1Id = new(5, 0);
        PdfObjectId content2Id = new(6, 0);
        PdfObjectId fontId = new(7, 0);
        PdfObjectId infoId = new(8, 0);

        byte[] content1 = Encoding.ASCII.GetBytes("BT /F1 24 Tf 72 700 Td (PAGE ONE) Tj ET");
        byte[] content2 = Encoding.ASCII.GetBytes("BT /F1 24 Tf 72 700 Td (PAGE TWO) Tj ET");

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>();

        PdfDictionary contentDict1 = new();
        contentDict1.Set(PdfName.Length, content1.Length);
        objects.Add(new PdfIndirectObject(content1Id, new PdfStream(contentDict1, content1)));

        PdfDictionary contentDict2 = new();
        contentDict2.Set(PdfName.Length, content2.Length);
        objects.Add(new PdfIndirectObject(content2Id, new PdfStream(contentDict2, content2)));

        PdfDictionary fontDict = new();
        fontDict.Set(PdfName.Type, PdfName.Intern("Font"));
        fontDict.Set(PdfName.Intern("Subtype"), PdfName.Intern("Type1"));
        fontDict.Set(PdfName.Intern("BaseFont"), PdfName.Intern("Helvetica"));
        objects.Add(new PdfIndirectObject(fontId, fontDict));

        PdfDictionary infoDict = new();
        infoDict.Set(PdfName.Intern("Title"), new PdfString("RepairFixture"));
        objects.Add(new PdfIndirectObject(infoId, infoDict));

        AddPage(objects, page1Id, pagesId, content1Id, fontId);
        AddPage(objects, page2Id, pagesId, content2Id, fontId);

        PdfArray kids = new(new PdfPrimitive[] { new PdfReference(page1Id), new PdfReference(page2Id) });
        PdfDictionary pagesDict = new();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, kids);
        pagesDict.Set(PdfName.Count, 2);
        objects.Add(new PdfIndirectObject(pagesId, pagesDict));

        PdfDictionary catalogDict = new();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));
        objects.Add(new PdfIndirectObject(catalogId, catalogDict));

        PdfDictionary trailer = new();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));
        trailer.Set(PdfName.Intern("Info"), new PdfReference(infoId));

        using MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        return ms.ToArray();
    }

    private static void AddPage(
        List<PdfIndirectObject> objects, PdfObjectId pageId, PdfObjectId pagesId,
        PdfObjectId contentId, PdfObjectId fontId)
    {
        PdfDictionary page = new();
        page.Set(PdfName.Type, PdfName.Page);
        page.Set(PdfName.Parent, new PdfReference(pagesId));
        page.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(612), new PdfInteger(792),
        }));
        page.Set(PdfName.Intern("Contents"), new PdfReference(contentId));
        PdfDictionary fonts = new();
        fonts.Set(PdfName.Intern("F1"), new PdfReference(fontId));
        PdfDictionary resources = new();
        resources.Set(PdfName.Intern("Font"), fonts);
        page.Set(PdfName.Intern("Resources"), resources);
        objects.Add(new PdfIndirectObject(pageId, page));
    }

    private static RepairReport Repair(byte[] broken, out byte[] repaired)
    {
        using MemoryStream input = new MemoryStream(broken, writable: false);
        using MemoryStream output = new MemoryStream();
        RepairReport report = PdfRepairer.Repair(input, output);
        repaired = output.ToArray();
        return report;
    }

    private static int PageCount(byte[] pdf)
    {
        using MemoryStream ms = new MemoryStream(pdf, writable: false);
        using PdfDocument document = PdfDocument.Open(ms, leaveOpen: true);
        return document.PageCount;
    }

    private static int LastIndexOf(byte[] bytes, string marker)
    {
        string text = Encoding.Latin1.GetString(bytes);
        return text.LastIndexOf(marker, StringComparison.Ordinal);
    }

    private static void OverwriteLineAfter(byte[] bytes, int markerOffset)
    {
        int lineStart = Array.IndexOf(bytes, (byte)'\n', markerOffset) + 1;
        for (int i = lineStart; i < bytes.Length && bytes[i] != (byte)'\n'; i++)
        {
            bytes[i] = (byte)'9';
        }
    }

    // ── Scenarios ─────────────────────────────────────────────────────────

    [Fact]
    public void Repair_BrokenStartxref_ReopensWithContent()
    {
        byte[] broken = BuildTwoPagePdf();
        OverwriteLineAfter(broken, LastIndexOf(broken, "startxref"));

        RepairReport report = Repair(broken, out byte[] repaired);

        report.Repaired.Should().BeTrue();
        report.CatalogFound.Should().BeTrue();
        PageCount(repaired).Should().Be(2);
        Encoding.Latin1.GetString(repaired).Should().Contain("PAGE ONE").And.Contain("PAGE TWO");
    }

    [Fact]
    public void Repair_NoXrefOrTrailer_RebuildsAndReopens()
    {
        byte[] good = BuildTwoPagePdf();
        int cut = LastIndexOf(good, "endobj") + "endobj".Length;
        byte[] broken = new byte[cut + 1];
        Array.Copy(good, broken, cut);
        broken[cut] = (byte)'\n';

        RepairReport report = Repair(broken, out byte[] repaired);

        report.TrailerReconstructed.Should().BeTrue();
        report.CatalogFound.Should().BeTrue();
        PageCount(repaired).Should().Be(2);
    }

    [Fact]
    public void Repair_JunkBeforeHeader_IsDetectedAndReopens()
    {
        byte[] good = BuildTwoPagePdf();
        byte[] junk = Encoding.ASCII.GetBytes("%!leading junk before header\nnoise noise\n");
        byte[] broken = new byte[junk.Length + good.Length];
        Array.Copy(junk, broken, junk.Length);
        Array.Copy(good, 0, broken, junk.Length, good.Length);

        RepairReport report = Repair(broken, out byte[] repaired);

        report.HeaderRelocated.Should().BeTrue();
        PageCount(repaired).Should().Be(2);
    }

    [Fact]
    public void Repair_Truncated_IsDetectedAndRecoversSurvivingPages()
    {
        byte[] good = BuildTwoPagePdf();
        byte[] broken = new byte[(int)(good.Length * 0.70)];
        Array.Copy(good, broken, broken.Length);

        RepairReport report = Repair(broken, out byte[] repaired);

        report.TruncationDetected.Should().BeTrue();
        PageCount(repaired).Should().Be(2);
    }

    [Fact]
    public void Repair_CorruptStreamLength_RecoversFullContent()
    {
        byte[] broken = BuildTwoPagePdf();

        // The PAGE ONE content stream is 39 bytes; lie about it in the dictionary.
        int lengthDecl = Encoding.Latin1.GetString(broken).IndexOf("/Length 39", StringComparison.Ordinal);
        lengthDecl.Should().BeGreaterThan(0);
        byte[] wrong = Encoding.ASCII.GetBytes("/Length 5 ");
        Array.Copy(wrong, 0, broken, lengthDecl, wrong.Length);

        // Also break the xref so repair must scan rather than trust offsets.
        OverwriteLineAfter(broken, LastIndexOf(broken, "startxref"));

        RepairReport report = Repair(broken, out byte[] repaired);

        report.Repaired.Should().BeTrue();
        PageCount(repaired).Should().Be(2);
        // Despite the wrong /Length, the full stream is recovered by endstream scan.
        Encoding.Latin1.GetString(repaired).Should().Contain("PAGE ONE");
    }

    [Fact]
    public void Repair_IncrementalRedefinition_KeepsLatestDefinition()
    {
        byte[] good = BuildTwoPagePdf();
        byte[] update = Encoding.ASCII.GetBytes(
            "\n5 0 obj\n<< /Length 31 >>\nstream\nBT /F1 24 Tf 72 700 Td (NEW5) Tj ET\nendstream\nendobj\n");
        byte[] broken = new byte[good.Length + update.Length];
        Array.Copy(good, broken, good.Length);
        Array.Copy(update, 0, broken, good.Length, update.Length);

        RepairReport report = Repair(broken, out byte[] repaired);

        report.DuplicateObjectsResolved.Should().BeGreaterThanOrEqualTo(1);
        string text = Encoding.Latin1.GetString(repaired);
        text.Should().Contain("NEW5");
        text.Should().NotContain("PAGE ONE");
    }

    [Fact]
    public void Repair_UndamagedInput_StillProducesOpenableOutput()
    {
        byte[] good = BuildTwoPagePdf();

        RepairReport report = Repair(good, out byte[] repaired);

        report.Repaired.Should().BeTrue();
        report.OriginalByteCount.Should().Be(good.Length);
        report.OutputByteCount.Should().BeGreaterThan(0);
        PageCount(repaired).Should().Be(2);
    }

    [Fact]
    public void Repair_TotalGarbage_DoesNotThrowAndReportsNoCatalog()
    {
        byte[] garbage = Encoding.ASCII.GetBytes("this is not a pdf at all, just plain text without any objects");

        RepairReport report = Repair(garbage, out byte[] repaired);

        report.CatalogFound.Should().BeFalse();
        report.Warnings.Should().NotBeEmpty();
        repaired.Should().NotBeNull();
    }
}
