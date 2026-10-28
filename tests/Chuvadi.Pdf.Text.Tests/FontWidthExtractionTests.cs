// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.2.4, §9.4.4 — glyph widths and displacement.
// PHASE: 3.26 — fragments are advanced and measured with the font's widths,
//        and the extractors judge word gaps from them.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Text.Tests;

public sealed class FontWidthExtractionTests
{
    // Helvetica widths for the letters used; 500 for the rest of 32..122.
    private static readonly string Widths = BuildWidths();

    [Fact]
    public void FontWidths_DecideWordGaps()
    {
        // Two words drawn one Tj each, 4 pt apart after the real width of "Hello".
        string text = Extract("BT /F1 12 Tf 50 300 Td (Hello) Tj 31.336 0 Td (lo) Tj ET");

        text.Should().Be("Hello lo");
    }

    private static string BuildWidths()
    {
        StringBuilder sb = new StringBuilder("/FirstChar 32 /LastChar 122 /Widths [");
        for (int c = 32; c <= 122; c++)
        {
            int w = c switch
            {
                ' ' => 278,
                'H' => 722,
                'e' => 556,
                'l' => 222,
                'o' => 556,
                _ => 500,
            };
            sb.Append(w.ToString(CultureInfo.InvariantCulture)).Append(' ');
        }

        return sb.Append(']').ToString();
    }

    private static string Extract(string content)
    {
        using PdfDocument doc = Open(content);
        return new TextExtractor(doc.Objects).ExtractText(doc.Pages[0]).Trim();
    }

    private static PdfDocument Open(string content)
    {
        List<string> bodies = new List<string>
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 400 400]/Resources<</Font<</F1 5 0 R>>>>/Contents 4 0 R>>",
            "<</Length " + Encoding.Latin1.GetByteCount(content).ToString(CultureInfo.InvariantCulture) + ">>\nstream\n" + content + "\nendstream",
            "<</Type/Font/Subtype/Type1/BaseFont/Helvetica " + Widths + ">>",
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
