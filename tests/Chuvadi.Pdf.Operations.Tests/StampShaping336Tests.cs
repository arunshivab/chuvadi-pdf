// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.7 (composite fonts), §9.4 (text showing)
// PHASE: 3.36.0 (D-412) — TextStamper draws through the shared mark-text
//        composer: Latin, punctuation and Indic scripts are shaped and
//        subset-embedded as Type0/Identity-H fonts, replacing the former
//        Helvetica WinAnsi path. Text outside the WinAnsi repertoire (Tamil)
//        now stamps instead of throwing, and every stamp carries an embedded
//        font.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

public sealed class StampShaping336Tests
{
    [Fact]
    public void Stamp_LatinAndPunctuation_EmbedsType0FontAndWritesNoWinAnsiLiteral()
    {
        byte[] output = Stamp("€5 – “ok”");

        string written = Encoding.Latin1.GetString(output);
        written.Should().Contain("Type0", "stamp text is now subset-embedded as a composite font");
        written.Should().Contain("Identity-H", "composite stamp fonts use Identity-H encoding");

        // The retired AUT-01 path wrote the euro as the WinAnsi literal (\200 …);
        // the composer draws hex glyph ids instead, so that literal is gone.
        written.Should().NotContain("(\\2005 \\226 \\223ok\\224)");

        AssertPageHasFontAndReopens(output);
    }

    [Fact]
    public void Stamp_Tamil_StampsWithEmbeddedFontInsteadOfThrowing()
    {
        byte[] output = Stamp("வணக்கம்");

        string written = Encoding.Latin1.GetString(output);
        written.Should().Contain("Type0");
        written.Should().Contain("Identity-H");

        AssertPageHasFontAndReopens(output);
    }

    private static byte[] Stamp(string template)
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(OnePagePdf()), leaveOpen: false);
        using MemoryStream output = new MemoryStream();
        TextStamper.Apply(output, doc, null, template, StampAnchor.BottomCenter, 36, 24, 9, ColorF.Black);
        output.Length.Should().BeGreaterThan(0);
        return output.ToArray();
    }

    private static void AssertPageHasFontAndReopens(byte[] output)
    {
        using PdfDocument result = PdfDocument.Open(new MemoryStream(output), leaveOpen: false);
        result.PageCount.Should().Be(1);
        PdfDictionary? resources = result.Pages[0].Resources;
        resources.Should().NotBeNull();
        resources!.ContainsKey(PdfName.Font).Should().BeTrue();
    }

    private static byte[] OnePagePdf()
    {
        string content = "BT /F1 12 Tf 72 700 Td (Body) Tj ET";
        string[] objects =
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
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
