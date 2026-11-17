// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.7.5.2 (predefined CMaps), §9.10.2 (CID to Unicode)
// PHASE: 3.35.0 (E1, D-352) — text of Type 0 fonts with predefined CJK CMaps
//        and no ToUnicode CMap.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Content;
using Chuvadi.Pdf.Documents;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Text.Tests;

public sealed class PredefinedCMapText3350Tests
{
    [Fact]
    public void ShiftJisCodes_ReadThroughTheCollection()
    {
        Extract("90ms-RKSJ-H", "Japan1", "BT /F1 12 Tf 50 300 Td <4182A0B1> Tj ET").Should().Be("Aあｱ");
    }

    [Fact]
    public void UnicodeCMaps_ReadTheirCodes()
    {
        Extract("UniGB-UTF16-H", "GB1", "BT /F1 12 Tf 50 300 Td <4E2D6587> Tj ET").Should().Be("中文");
    }

    [Fact]
    public void IdentityWithoutToUnicode_ReadsCidsThroughTheCollection()
    {
        Extract("Identity-H", "Japan1", "BT /F1 12 Tf 50 300 Td <034B> Tj ET").Should().Be("あ");
    }

    [Fact]
    public void Widths_FollowTheCids()
    {
        // /W gives CID 843 (あ) a width of 500 and the default is 1000: the
        // two-byte code and the one-byte "A" (CID 264, width 1000) make
        // 1500 thousandths, so the next fragment starts 18 units on at size 12.
        using PdfDocument doc = Open("90ms-RKSJ-H", "Japan1", "BT /F1 12 Tf 50 300 Td <82A041> Tj <82A0> Tj ET", "/W[843[500]]");
        List<TextFragment> fragments = new TextExtractor(doc.Objects).ExtractFragments(doc.Pages[0]);

        fragments.Should().HaveCount(2);
        fragments[1].X.Should().BeApproximately(68, 0.01);
    }

    private static string Extract(string encoding, string ordering, string content)
    {
        using PdfDocument doc = Open(encoding, ordering, content, string.Empty);
        return new TextExtractor(doc.Objects).ExtractText(doc.Pages[0]).Trim();
    }

    private static PdfDocument Open(string encoding, string ordering, string content, string widths)
    {
        List<string> bodies = new List<string>
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 400 400]/Resources<</Font<</F1 5 0 R>>>>/Contents 4 0 R>>",
            "<</Length " + Encoding.Latin1.GetByteCount(content).ToString(CultureInfo.InvariantCulture) + ">>\nstream\n" + content + "\nendstream",
            "<</Type/Font/Subtype/Type0/BaseFont/KozMinPr6N-Regular/Encoding/" + encoding + "/DescendantFonts[6 0 R]>>",
            "<</Type/Font/Subtype/CIDFontType0/BaseFont/KozMinPr6N-Regular/CIDSystemInfo<</Registry(Adobe)/Ordering(" + ordering
                + ")/Supplement 6>>/DW 1000" + widths + ">>",
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
