// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.4.4 (text rise: Trm = [Tfs·Th 0 0 Tfs 0 Trise] · Tm · CTM)
// Tests for v3.20: text rise (Ts) moves a run's glyphs up in the text display
// list, for both Tj and TJ (superscripts land where they are drawn).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Chuvadi.Pdf.Documents;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.DisplayList.Tests;

public sealed class TextRiseTests
{
    [Theory]
    [InlineData("BT /F1 12 Tf 100 200 Td 7 Ts (X) Tj ET")]
    [InlineData("BT /F1 12 Tf 100 200 Td 7 Ts [(X) -20 (Y)] TJ ET")]
    public void Rise_ShiftsTheRunUp(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        using PdfDocument doc = Open(content);

        TextOp text = DisplayListBuilder.Build(doc, 0).OfType<TextOp>().First();

        text.Transform.F.Should().BeApproximately(207, 1e-9);
        text.Transform.E.Should().BeApproximately(100, 1e-9);
    }

    [Fact]
    public void ZeroRise_LeavesTheBaseline()
    {
        using PdfDocument doc = Open("BT /F1 12 Tf 100 200 Td (X) Tj ET");

        DisplayListBuilder.Build(doc, 0).OfType<TextOp>().First().Transform.F.Should().BeApproximately(200, 1e-9);
    }

    private static PdfDocument Open(string content)
    {
        List<string> bodies = new List<string>
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 400 400]/Resources<</Font<</F1 5 0 R>>>>/Contents 4 0 R>>",
            "<</Length " + content.Length.ToString(CultureInfo.InvariantCulture) + ">>\nstream\n" + content + "\nendstream",
            "<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>",
        };

        StringBuilder pdf = new StringBuilder("%PDF-1.7\n");
        List<int> offsets = new List<int>();
        for (int i = 0; i < bodies.Count; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append(" 0 obj\n").Append(bodies[i]).Append("\nendobj\n");
        }

        int xref = pdf.Length;
        pdf.Append("xref\n0 6\n0000000000 65535 f \n");
        foreach (int off in offsets)
        {
            pdf.Append(off.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        pdf.Append("trailer <</Size 6/Root 1 0 R>>\nstartxref\n").Append(xref.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF");
        return PdfDocument.Open(new MemoryStream(Encoding.Latin1.GetBytes(pdf.ToString())), leaveOpen: false);
    }
}
