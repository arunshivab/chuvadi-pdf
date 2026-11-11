// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 Annex D (MacRomanEncoding)
// PHASE: 3.33.0 (D-318) — the two missing MacRoman entries (ã 0x8B, õ 0x9B);
//        before, every code after them was shifted by one or two.

using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Fonts.Tests;

public sealed class MacRomanEncoding3330Tests
{
    [Theory]
    [InlineData(0x80, 'Ä')] // Adieresis
    [InlineData(0x8A, 'ä')] // adieresis
    [InlineData(0x8B, 'ã')] // atilde (was missing)
    [InlineData(0x8C, 'å')] // aring
    [InlineData(0x9A, 'ö')] // odieresis
    [InlineData(0x9B, 'õ')] // otilde (was missing)
    [InlineData(0x9C, 'ú')] // uacute
    [InlineData(0xA5, '•')] // bullet
    [InlineData(0xA7, 'ß')] // germandbls
    [InlineData(0xAE, 'Æ')] // AE
    [InlineData(0xD2, '“')] // quotedblleft
    [InlineData(0xD4, '‘')] // quoteleft
    [InlineData(0xE1, '·')] // periodcentered
    [InlineData(0xFF, 'ˇ')] // caron
    public void MacRoman_HighCodes_MapToTheRightCharacter(int code, char expected)
    {
        PdfFontEncoding encoding = PdfFontEncoding.FromNamedEncoding("MacRomanEncoding");

        encoding.GetCharacter((byte)code).Should().Be(expected);
    }
}
