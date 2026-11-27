// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.7 (composite fonts), §9.4 (text showing)
// PHASE: 3.36.0 (D-412) — a text watermark is shaped and subset-embedded through
//        the shared mark-text composer. Text outside the former Helvetica WinAnsi
//        repertoire (Tamil) now watermarks with an embedded face instead of
//        throwing, and punctuation such as the euro sign and curly quotes is
//        drawn as embedded glyphs, never as a WinAnsi literal.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Watermark.Tests;

public sealed class WatermarkShaping336Tests
{
    [Fact]
    public void Watermark_EuroDashAndQuotes_EmbedsFontAndWritesNoWinAnsiLiteral()
    {
        byte[] output = Watermark("€5 – “ok”");

        string written = Encoding.Latin1.GetString(output);
        written.Should().Contain("Type0");
        written.Should().Contain("Identity-H");
        written.Should().NotContain("(\\2005 \\226 \\223ok\\224)");

        AssertPageHasFont(output);
    }

    [Fact]
    public void Watermark_Tamil_EmbedsFontInsteadOfThrowing()
    {
        byte[] output = Watermark("வணக்கம்");

        string written = Encoding.Latin1.GetString(output);
        written.Should().Contain("Type0");
        written.Should().Contain("Identity-H");

        AssertPageHasFont(output);
    }

    private static byte[] Watermark(string text)
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(OnePagePdf()), leaveOpen: false);
        using MemoryStream output = new MemoryStream();
        WatermarkStamper.ApplyText(output, doc, new TextWatermarkOptions(text));
        output.Length.Should().BeGreaterThan(0);
        return output.ToArray();
    }

    private static void AssertPageHasFont(byte[] output)
    {
        using PdfDocument result = PdfDocument.Open(new MemoryStream(output), leaveOpen: false);
        PdfObjectStore store = result.Objects;
        PdfDictionary resources =
            (store.Resolve(result.Pages[0].Dictionary[PdfName.Intern("Resources")]) as PdfDictionary)!;
        resources.ContainsKey(PdfName.Font).Should().BeTrue();
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
