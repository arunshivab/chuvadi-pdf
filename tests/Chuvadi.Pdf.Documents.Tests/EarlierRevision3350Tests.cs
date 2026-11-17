// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.5.6 (incremental updates), §7.7.3 (page tree)
// PHASE: 3.35.0 (B8, D-399, stressful-0123) — with RepairIfDamaged, a file
//        whose latest revision's page tree mostly cannot be read opens its
//        intact earlier revision, with a warning.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Documents.Tests;

public sealed class EarlierRevision3350Tests
{
    private static readonly PdfOpenOptions Repair = new PdfOpenOptions { RepairIfDamaged = true };

    [Fact]
    public void BrokenLatestRevision_OpensTheEarlierOne_WithAWarning()
    {
        byte[] file = WithUpdate(("2", "<</Type/Pages/Kids[3 0 R 6 0 R 7 0 R]/Count 3>>"), ("6", Stream("x")), ("7", Stream("y")));

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(file), Repair);

        doc.PageCount.Should().Be(2);
        doc.Pages[0].MediaBox.Width.Should().Be(300);
        doc.Pages[1].MediaBox.Width.Should().Be(400);
        doc.Warnings.Should().Contain(w => w.Contains("earlier revision", StringComparison.Ordinal));
    }

    [Fact]
    public void BrokenLatestRevision_WithoutRepair_IsNotReplaced()
    {
        byte[] file = WithUpdate(("2", "<</Type/Pages/Kids[3 0 R 6 0 R 7 0 R]/Count 3>>"), ("6", Stream("x")), ("7", Stream("y")));

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(file), leaveOpen: false);
        Action first = () => _ = doc.Pages[0];

        first.Should().Throw<Chuvadi.Pdf.Primitives.PdfException>();
        doc.Warnings.Should().NotContain(w => w.Contains("earlier revision", StringComparison.Ordinal));
    }

    [Fact]
    public void LatestRevisionWithNoPages_IsKept()
    {
        // Deleting every page is a change, not damage: the earlier pages are
        // not brought back.
        byte[] file = WithUpdate(("2", "<</Type/Pages/Kids[]/Count 0>>"));

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(file), Repair);

        doc.PageCount.Should().Be(0);
        doc.Warnings.Should().NotContain(w => w.Contains("earlier revision", StringComparison.Ordinal));
    }

    [Fact]
    public void LatestRevisionWithAMinorityBroken_IsKept()
    {
        byte[] file = WithUpdate(("2", "<</Type/Pages/Kids[4 0 R 5 0 R 3 0 R]/Count 3>>"));

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(file), Repair);

        doc.Pages[0].MediaBox.Width.Should().Be(300);
        doc.Warnings.Should().NotContain(w => w.Contains("earlier revision", StringComparison.Ordinal));
    }

    [Fact]
    public void SoundFile_IsUnchanged()
    {
        byte[] file = WithUpdate(("2", "<</Type/Pages/Kids[5 0 R 4 0 R]/Count 2>>"));

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(file), Repair);

        doc.PageCount.Should().Be(2);
        doc.Pages[0].MediaBox.Width.Should().Be(400, "the latest revision's order");
        doc.Warnings.Should().NotContain(w => w.Contains("earlier revision", StringComparison.Ordinal));
    }

    private static string Stream(string content) =>
        "<</Length " + content.Length.ToString(CultureInfo.InvariantCulture) + ">>\nstream\n" + content + "\nendstream";

    // Revision 1: catalog 1, pages 2 (two kids), contents 3, pages 4 and 5
    // (300 and 400 wide). Then one incremental update with the given objects.
    private static byte[] WithUpdate(params (string Number, string Body)[] update)
    {
        List<string> bodies = new List<string>
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[4 0 R 5 0 R]/Count 2>>",
            Stream(string.Empty),
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 300 200]/Contents 3 0 R>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 400 200]/Contents 3 0 R>>",
        };

        StringBuilder sb = new StringBuilder("%PDF-1.7\n");
        List<int> offsets = new List<int>();
        for (int i = 0; i < bodies.Count; i++)
        {
            offsets.Add(sb.Length);
            sb.Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append(" 0 obj\n").Append(bodies[i]).Append("\nendobj\n");
        }

        int xref = sb.Length;
        sb.Append("xref\n0 ").Append((bodies.Count + 1).ToString(CultureInfo.InvariantCulture)).Append("\n0000000000 65535 f \n");
        foreach (int off in offsets)
        {
            sb.Append(off.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        sb.Append("trailer <</Size ").Append((bodies.Count + 1).ToString(CultureInfo.InvariantCulture))
            .Append("/Root 1 0 R>>\nstartxref\n").Append(xref.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF\n");

        Dictionary<int, int> updated = new Dictionary<int, int>();
        foreach ((string number, string body) in update)
        {
            updated[int.Parse(number, CultureInfo.InvariantCulture)] = sb.Length;
            sb.Append(number).Append(" 0 obj\n").Append(body).Append("\nendobj\n");
        }

        int xref2 = sb.Length;
        int size = Math.Max(bodies.Count + 1, updated.Keys.Max() + 1);
        sb.Append("xref\n0 1\n0000000000 65535 f \n");
        foreach (KeyValuePair<int, int> entry in updated.OrderBy(e => e.Key))
        {
            sb.Append(entry.Key.ToString(CultureInfo.InvariantCulture)).Append(" 1\n")
                .Append(entry.Value.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        sb.Append("trailer <</Size ").Append(size.ToString(CultureInfo.InvariantCulture))
            .Append("/Root 1 0 R/Prev ").Append(xref.ToString(CultureInfo.InvariantCulture))
            .Append(">>\nstartxref\n").Append(xref2.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF\n");
        return Encoding.Latin1.GetBytes(sb.ToString());
    }
}
