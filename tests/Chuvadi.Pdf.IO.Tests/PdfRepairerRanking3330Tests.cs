// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.5.6 (incremental updates), §7.5.7 (object streams)
// PHASE: 3.33.0 (ticket 48, D-307) — repair keeps the newest definition of an
//        object, whether it is direct or inside an object stream.

using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.IO.Tests;

public sealed class PdfRepairerRanking3330Tests
{
    private const string Catalog =
        "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n"
        + "2 0 obj\n<< /Type /Pages /Kids [5 0 R] /Count 1 >>\nendobj\n"
        + "5 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>\nendobj\n";

    [Fact]
    public void Repair_ObjectStreamAfterDirectDefinition_KeepsTheNewerCompressedObject()
    {
        // The original file defines object 3 directly; an incremental update
        // appended later redefines it inside an object stream.
        string pdf = "%PDF-1.5\n" + Catalog
            + "3 0 obj\n<< /Title (Old) >>\nendobj\n"
            + "trailer\n<< /Size 6 /Root 1 0 R /Info 3 0 R >>\n"
            + ObjectStream(4, 3, "<< /Title (New) >>")
            + "trailer\n<< /Size 6 /Root 1 0 R /Info 3 0 R >>\nstartxref\n9\n%%EOF\n";

        TitleAfterRepair(pdf).Should().Be("New");
    }

    [Fact]
    public void Repair_DirectDefinitionAfterObjectStream_KeepsTheNewerDirectObject()
    {
        // The object stream comes first; a later update redefines object 3
        // directly. Before and after 3.33.0 the direct definition wins here.
        string pdf = "%PDF-1.5\n" + Catalog
            + ObjectStream(4, 3, "<< /Title (Old) >>")
            + "3 0 obj\n<< /Title (New) >>\nendobj\n"
            + "trailer\n<< /Size 6 /Root 1 0 R /Info 3 0 R >>\nstartxref\n9\n%%EOF\n";

        TitleAfterRepair(pdf).Should().Be("New");
    }

    [Fact]
    public void Repair_TwoObjectStreams_KeepsTheLaterOne()
    {
        string pdf = "%PDF-1.5\n" + Catalog
            + ObjectStream(4, 3, "<< /Title (Old) >>")
            + ObjectStream(6, 3, "<< /Title (New) >>")
            + "trailer\n<< /Size 7 /Root 1 0 R /Info 3 0 R >>\nstartxref\n9\n%%EOF\n";

        TitleAfterRepair(pdf).Should().Be("New");
    }

    [Fact]
    public void Repair_ObjectOnlyInObjectStream_IsRecoveredAndCounted()
    {
        string pdf = "%PDF-1.5\n" + Catalog
            + ObjectStream(4, 3, "<< /Title (Only) >>")
            + "trailer\n<< /Size 6 /Root 1 0 R /Info 3 0 R >>\nstartxref\n9\n%%EOF\n";

        using MemoryStream input = new MemoryStream(Encoding.Latin1.GetBytes(pdf), writable: false);
        using MemoryStream output = new MemoryStream();
        RepairReport report = PdfRepairer.Repair(input, output);

        report.ObjectsFromObjectStreams.Should().Be(1);
        output.Position = 0;
        using PdfDocument document = PdfDocument.Open(output);
        document.Title.Should().Be("Only");
    }

    // An uncompressed object stream holding one object.
    private static string ObjectStream(int streamNumber, int objectNumber, string value)
    {
        string header = objectNumber + " 0 ";
        string data = header + value;
        return streamNumber + " 0 obj\n<< /Type /ObjStm /N 1 /First " + header.Length + " /Length " + data.Length
            + " >>\nstream\n" + data + "\nendstream\nendobj\n";
    }

    private static string? TitleAfterRepair(string pdf)
    {
        using MemoryStream input = new MemoryStream(Encoding.Latin1.GetBytes(pdf), writable: false);
        using MemoryStream output = new MemoryStream();
        RepairReport report = PdfRepairer.Repair(input, output);
        report.Repaired.Should().BeTrue();
        output.Position = 0;
        using PdfDocument document = PdfDocument.Open(output);
        return document.Title;
    }
}
