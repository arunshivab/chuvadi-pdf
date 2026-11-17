// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.6.2.2 (standard Type 1 fonts), Annex D.2 (WinAnsiEncoding)
// PHASE: 3.35.0 (D-400 A1) — accented letters of the non-embedded standard
//        fonts are drawn; the outline bundle had them empty.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.Tests;

public sealed class AccentedStandardFonts3350Tests
{
    [Theory]
    [InlineData("Helvetica")]
    [InlineData("Times-Roman")]
    [InlineData("Courier")]
    [InlineData("Arial")]
    [InlineData("TimesNewRoman")]
    public void AccentedLetters_AreDrawn(string baseFont)
    {
        // 0xC0 A-grave, 0xE9 e-acute, 0xFC u-diaeresis, 0xD1 N-tilde.
        foreach (string code in new[] { "\\300", "\\351", "\\374", "\\321" })
        {
            Ink(Render("BT /F1 60 Tf 100 150 Td (" + code + ") Tj ET", baseFont))
                .Should().BeGreaterThan(50, "glyph " + code + " of " + baseFont + " is drawn");
        }
    }

    [Fact]
    public void AccentedLetter_HasMoreInkThanItsBaseLetter()
    {
        Ink(Render("BT /F1 60 Tf 100 150 Td (\\351) Tj ET", "Helvetica"))
            .Should().BeGreaterThan(Ink(Render("BT /F1 60 Tf 100 150 Td (e) Tj ET", "Helvetica")), "the accent adds ink");
    }

    private static int Ink(PixelBuffer px)
    {
        int count = 0;
        for (int y = 0; y < px.Height; y++)
        {
            for (int x = 0; x < px.Width; x++)
            {
                (byte b, byte g, byte r, byte _) = px.GetPixelBgra(x, y);
                if (r < 128 && g < 128 && b < 128)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static PixelBuffer Render(string content, string baseFont)
    {
        List<string> bodies = new List<string>
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 300 300]/Resources<</Font<</F1<</Type/Font/Subtype/Type1/BaseFont/"
                + baseFont + "/Encoding/WinAnsiEncoding>>>>>>/Contents 4 0 R>>",
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
        return new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72 }).Rasterize(doc.Pages[0]);
    }
}
