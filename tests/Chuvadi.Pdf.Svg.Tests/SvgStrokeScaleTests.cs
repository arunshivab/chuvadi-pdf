// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// 3.25: stroke width and dash lengths in SVG output scale with the CTM.
// Dashes kept their user-space lengths under a scaling CTM, and a rotated
// CTM (a = d = 0) gave a stroke width of 0.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Svg.Tests;

public sealed class SvgStrokeScaleTests
{
    [Fact]
    public void ScaledCtm_ScalesWidthAndDashes()
    {
        string svg = Render("0.1 0 0 0.1 0 0 cm 0 0 0 RG 40 w [100 50] 20 d 0 3000 m 2000 3000 l S");

        svg.Should().Contain("stroke-width=\"4\"").And.Contain("stroke-dasharray=\"10,5\"");
    }

    [Fact]
    public void RotatedCtm_KeepsTheWidth()
    {
        string svg = Render("0 1 -1 0 300 0 cm 0 0 0 RG 6 w 0 100 m 200 100 l S");

        svg.Should().Contain("stroke-width=\"6\"");
    }

    private static string Render(string content)
    {
        List<string> bodies = new List<string>
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 400 400]/Contents 4 0 R>>",
            "<</Length " + content.Length.ToString(CultureInfo.InvariantCulture) + ">>\nstream\n" + content + "\nendstream",
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
