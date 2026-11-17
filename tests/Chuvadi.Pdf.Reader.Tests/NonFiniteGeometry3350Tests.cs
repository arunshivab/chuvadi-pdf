// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.35.0 (GFX-01) — a number too long for a double no longer sends the
//        curve flattener into unbounded recursion: the page renders, the
//        non-finite path is skipped with a warning, and a DPI that is not a
//        positive finite number is refused.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Reader.Tests;

public sealed class NonFiniteGeometry3350Tests
{
    private static readonly string Big = new string('9', 400);

    public static TheoryData<string> OverLongContent => new TheoryData<string>
    {
        "0 0 m " + Big + " 100 200 0 300 100 c S",
        "0 0 m " + Big + " 100 l S",
        "0 0 " + Big + " 100 re f",
        Big + " 0 0 1 0 0 cm 0 0 m 100 100 200 0 300 100 c S",
        Big + " 0 0 1 0 0 cm 0 0 100 100 re f",
        "BT /F1 12 Tf " + Big + " 0 0 1 72 700 Tm (Hello) Tj ET",
        "BT /F1 " + Big + " Tf 72 700 Td (Hello) Tj ET",
    };

    [Theory]
    [MemberData(nameof(OverLongContent))]
    public void OverLongNumber_RendersWithAWarning(string content)
    {
        List<PdfWarning> warnings = new List<PdfWarning>();
        using PdfDocument doc = PdfDocument.Open(
            new MemoryStream(OnePagePdf(content + " 0 0 1 rg 10 10 50 50 re f")),
            new PdfOpenOptions { OnWarning = warnings.Add });

        byte[] png = doc.RenderPageToPng(0, 72);

        png.Length.Should().BeGreaterThan(8);
        warnings.Should().Contain(w => w.Kind == PdfWarningKind.ContentOperatorSkipped);
    }

    [Fact]
    public void LargeButFiniteCurve_Renders()
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(OnePagePdf("0 0 m 1" + new string('0', 30) + " 100 200 0 300 100 c S")), leaveOpen: false);

        doc.RenderPageToPng(0, 72).Length.Should().BeGreaterThan(8);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0.0)]
    [InlineData(-150.0)]
    public void InvalidDpi_Throws(double dpi)
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(OnePagePdf("0 0 m 100 100 l S")), leaveOpen: false);

        Action render = () => doc.RenderPageToPng(0, dpi);

        render.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static byte[] OnePagePdf(string content)
    {
        string[] objects =
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            "<< /Length " + content.Length.ToString(CultureInfo.InvariantCulture) + " >>\nstream\n" + content + "\nendstream",
        };
        StringBuilder sb = new StringBuilder("%PDF-1.7\n");
        List<int> offsets = new List<int>();
        for (int i = 0; i < objects.Length; i++)
        {
            offsets.Add(sb.Length);
            sb.Append(CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        int xref = sb.Length;
        sb.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (int offset in offsets)
        {
            sb.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        sb.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(sb.ToString());
    }
}
