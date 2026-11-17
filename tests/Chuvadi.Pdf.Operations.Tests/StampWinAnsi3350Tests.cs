// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ISO 32000-1 Annex D.2 (WinAnsiEncoding)
// PHASE: 3.35.0 (AUT-01) — stamped text is encoded through the WinAnsi table
//        of the Helvetica it is drawn with; a character the table lacks is an
//        error that points to embedding a font, and nothing is written.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

public sealed class StampWinAnsi3350Tests
{
    [Fact]
    public void Stamp_EuroDashAndQuotes_AreWrittenAsWinAnsiCodes()
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(OnePagePdf()), leaveOpen: false);
        using MemoryStream output = new MemoryStream();

        TextStamper.Apply(output, doc, null, "€5 – “ok”", StampAnchor.BottomCenter, 36, 24, 9, ColorF.Black);

        string written = Encoding.Latin1.GetString(output.ToArray());
        written.Should().Contain("(\\2005 \\226 \\223ok\\224)", "the euro is 0x80, the en dash 0x96, the quotes 0x93 and 0x94");
    }

    [Fact]
    public void Stamp_Tamil_ThrowsAndWritesNothing()
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(OnePagePdf()), leaveOpen: false);
        using MemoryStream output = new MemoryStream();

        Action stamp = () => TextStamper.Apply(output, doc, null, "வணக்கம்", StampAnchor.BottomCenter, 36, 24, 9, ColorF.Black);

        stamp.Should().Throw<ArgumentException>().WithMessage("*AddFont*");
        output.Length.Should().Be(0);
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
