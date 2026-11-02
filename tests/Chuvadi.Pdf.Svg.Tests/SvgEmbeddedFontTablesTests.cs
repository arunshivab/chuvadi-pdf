// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.30 — fonts embedded in SVG carry the tables browsers require
//        (D-105): OS/2, name and post, and a Windows Unicode cmap.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Svg.Tests;

public sealed class SvgEmbeddedFontTablesTests
{
    private static readonly string[] Required = { "OS/2", "cmap", "head", "hhea", "hmtx", "maxp", "name", "post" };

    [Fact]
    public void AnAuthoredSubset_GetsTheTablesBrowsersRequire()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create().UseLipiFonts();
        builder.AddPage(PageSize.A4).DrawText("Hello WORLD Rag", 40, 100, "Lipi", 24, Colors.Black);
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(builder.ToByteArray()));

        byte[] font = EmbeddedFonts(new SvgRenderer().RenderPage(doc, 0))[0];

        Tables(font).Should().Contain(Required);
    }

    [Fact]
    public void ASimpleTrueTypeFontWithOnlyAMacintoshCmap_GetsAWindowsUnicodeCmap()
    {
        // The repo fixture embeds Times New Roman with a (1,0) cmap only.
        using PdfDocument doc = PdfDocument.Open(Path.Combine(AppContext.BaseDirectory, "fixtures", "testPDF_JBIG2.pdf"));

        byte[] font = EmbeddedFonts(new SvgRenderer().RenderPage(doc, 0))[0];

        Tables(font).Should().Contain(Required);
        CmapPlatforms(font).Should().Contain((3, 1));
    }

    private static List<byte[]> EmbeddedFonts(string svg)
    {
        List<byte[]> fonts = new List<byte[]>();
        foreach (Match m in Regex.Matches(svg, "base64,([A-Za-z0-9+/=]+)\\)"))
        {
            fonts.Add(Convert.FromBase64String(m.Groups[1].Value));
        }

        fonts.Should().NotBeEmpty();
        return fonts;
    }

    private static List<string> Tables(byte[] font)
    {
        List<string> tags = new List<string>();
        int count = (font[4] << 8) | font[5];
        for (int i = 0; i < count; i++)
        {
            int at = 12 + (i * 16);
            tags.Add(System.Text.Encoding.ASCII.GetString(font, at, 4));
            (Offset(font, at) % 4).Should().Be(0, "every table starts on a 4-byte boundary");
        }

        return tags;
    }

    private static List<(int Platform, int Encoding)> CmapPlatforms(byte[] font)
    {
        int count = (font[4] << 8) | font[5];
        for (int i = 0; i < count; i++)
        {
            int at = 12 + (i * 16);
            if (System.Text.Encoding.ASCII.GetString(font, at, 4) == "cmap")
            {
                int cmap = Offset(font, at);
                int subtables = (font[cmap + 2] << 8) | font[cmap + 3];
                List<(int, int)> result = new List<(int, int)>();
                for (int k = 0; k < subtables; k++)
                {
                    int record = cmap + 4 + (k * 8);
                    result.Add(((font[record] << 8) | font[record + 1], (font[record + 2] << 8) | font[record + 3]));
                }

                return result;
            }
        }

        return new List<(int, int)>();
    }

    private static int Offset(byte[] font, int entry) =>
        (font[entry + 8] << 24) | (font[entry + 9] << 16) | (font[entry + 10] << 8) | font[entry + 11];
}
