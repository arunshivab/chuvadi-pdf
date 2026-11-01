// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// 3.27 (D-63): a skipped shading is reported on the SVG path too.

using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Svg.Tests;

public sealed class SvgFallbackWarningTests
{
    [Fact]
    public void ASkippedShading_IsReported()
    {
        string content = "/S0 sh";
        string[] bodies =
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]/Resources<</Shading<</S0 5 0 R>>>>/Contents 4 0 R>>",
            "<</Length " + content.Length + ">>\nstream\n" + content + "\nendstream",
            "<</ShadingType 2/ColorSpace/DeviceRGB/Coords[0 0 /Wrong 0]/Function<</FunctionType 2/Domain[0 1]/C0[0 0 0]/C1[1 1 1]/N 1>>>>",
        };
        StringBuilder sb = new StringBuilder("%PDF-1.7\n");
        List<int> offsets = new List<int>();
        for (int i = 0; i < bodies.Length; i++)
        {
            offsets.Add(sb.Length);
            sb.Append(i + 1).Append(" 0 obj\n").Append(bodies[i]).Append("\nendobj\n");
        }

        int xref = sb.Length;
        sb.Append("xref\n0 ").Append(bodies.Length + 1).Append("\n0000000000 65535 f \n");
        foreach (int off in offsets)
        {
            sb.Append(off.ToString("D10", System.Globalization.CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        sb.Append("trailer\n<</Size ").Append(bodies.Length + 1).Append("/Root 1 0 R>>\nstartxref\n").Append(xref).Append("\n%%EOF\n");

        List<PdfWarning> warnings = new List<PdfWarning>();
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(Encoding.Latin1.GetBytes(sb.ToString())), new PdfOpenOptions { OnWarning = warnings.Add });

        string svg = new SvgRenderer().RenderPage(doc, 0);

        svg.Should().Contain("<svg");
        PdfWarning w = warnings.Should().ContainSingle().Subject;
        w.Kind.Should().Be(PdfWarningKind.ShadingSkipped);
        w.PageNumber.Should().Be(1);
        w.Subject.Should().Be("shading 5 0 R");
    }
}
