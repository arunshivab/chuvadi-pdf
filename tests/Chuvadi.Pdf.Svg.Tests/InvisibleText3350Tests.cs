// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.3.6 (text rendering mode)
// PHASE: 3.35.0 — invisible text (mode 3, a scanned page's text layer) and
//        clip-only text (mode 7) are kept in the SVG but not shown.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Svg.Tests;

public sealed class InvisibleText3350Tests
{
    [Theory]
    [InlineData(3)]
    [InlineData(7)]
    public void HiddenModes_AreKeptButNotShown(int mode)
    {
        string svg = Svg("BT /F1 24 Tf " + mode.ToString(CultureInfo.InvariantCulture) + " Tr 20 100 Td (HIDDEN) Tj ET");

        int group = svg.IndexOf("<g opacity=\"0\">", System.StringComparison.Ordinal);
        group.Should().BeGreaterThan(0);
        svg.IndexOf("HIDDEN", System.StringComparison.Ordinal).Should().BeGreaterThan(group, "the text stays, for search and selection");
    }

    [Fact]
    public void FilledText_IsShown()
    {
        Svg("BT /F1 24 Tf 20 100 Td (SHOWN) Tj ET").Should().Contain("SHOWN").And.NotContain("opacity=\"0\"");
    }

    private static string Svg(string content)
    {
        List<string> bodies = new List<string>
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]/Resources<</Font<</F1 5 0 R>>>>/Contents 4 0 R>>",
            "<</Length " + content.Length.ToString(CultureInfo.InvariantCulture) + ">>\nstream\n" + content + "\nendstream",
            "<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>",
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
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(Encoding.Latin1.GetBytes(sb.ToString())), leaveOpen: false);
        return new SvgRenderer().RenderPage(doc, 0);
    }
}
