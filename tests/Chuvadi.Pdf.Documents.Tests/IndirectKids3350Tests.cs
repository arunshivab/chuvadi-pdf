// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.3.10 (indirect objects), §7.7.3 (page tree)
// PHASE: 3.35.0 (B8, N9) — a page tree whose /Kids or /Count is an indirect
//        reference (stressful-0370).

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Documents.Tests;

public sealed class IndirectKids3350Tests
{
    [Fact]
    public void IndirectKids_FindsThePage()
    {
        using PdfDocument doc = Open("<</Type/Pages/Kids 4 0 R/Count 1>>", "[5 0 R]");

        doc.PageCount.Should().Be(1);
        doc.Pages[0].MediaBox.Width.Should().Be(300);
    }

    [Fact]
    public void IndirectCount_IsRead()
    {
        using PdfDocument doc = Open("<</Type/Pages/Kids[5 0 R]/Count 4 0 R>>", "1");

        doc.PageCount.Should().Be(1);
        doc.Pages[0].MediaBox.Width.Should().Be(300);
    }

    [Fact]
    public void IndirectKids_StreamsAllPages()
    {
        using PdfDocument doc = Open("<</Type/Pages/Kids 4 0 R/Count 1>>", "[5 0 R]");

        doc.Pages.EnumerateStreaming().Should().ContainSingle();
    }

    // Objects: 1 catalog, 2 page tree root (given), 3 contents, 4 given, 5 page.
    private static PdfDocument Open(string pages, string object4)
    {
        List<string> bodies = new List<string>
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            pages,
            "<</Length 0>>\nstream\n\nendstream",
            object4,
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 300 200]/Contents 3 0 R>>",
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
            .Append("/Root 1 0 R>>\nstartxref\n").Append(xref.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF");
        return PdfDocument.Open(new MemoryStream(Encoding.Latin1.GetBytes(sb.ToString())), leaveOpen: false);
    }
}
