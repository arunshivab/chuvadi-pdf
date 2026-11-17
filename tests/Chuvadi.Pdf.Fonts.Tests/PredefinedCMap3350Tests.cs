// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.7.5.2 (predefined CMaps), §9.10.2
// PHASE: 3.35.0 (E1, D-352) — expected CIDs read from the source CMap files
//        of cmap-resources 20231115.

using System;
using System.Text;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Fonts.Tests;

public sealed class PredefinedCMap3350Tests
{
    private static readonly byte[] Rksj = { 0x41, 0x82, 0xA0, 0xB1 };

    [Fact]
    public void ShiftJis_MixesOneAndTwoByteCodes()
    {
        PredefinedCMap cmap = PredefinedCMap.Get("90ms-RKSJ-H")!;

        cmap.Next(Rksj, out int code, out int cid).Should().Be(1);
        (code, cid).Should().Be((0x41, 264));
        cmap.Next(new ReadOnlySpan<byte>(Rksj, 1, Rksj.Length - 1), out code, out cid).Should().Be(2);
        (code, cid).Should().Be((0x82A0, 843));
        cmap.Next(new ReadOnlySpan<byte>(Rksj, 3, Rksj.Length - 3), out _, out cid).Should().Be(1);
        cid.Should().Be(343, "half-width katakana");
        cmap.IsVertical.Should().BeFalse();
        cmap.IsUnicode.Should().BeFalse();
    }

    [Fact]
    public void UnicodeCMaps_InEveryForm_MapTheSameCharacters()
    {
        foreach (string form in new[] { "UTF16", "UTF8", "UTF32" })
        {
            PredefinedCMap cmap = PredefinedCMap.Get("UniJIS-" + form + "-H")!;
            byte[] hiragana = Encode(form, "あ");
            byte[] astral = Encode(form, "\U00020B9F");

            cmap.Next(hiragana, out _, out int cid).Should().Be(hiragana.Length, form);
            cid.Should().Be(843, form);
            cmap.Next(astral, out _, out cid).Should().Be(astral.Length, form);
            cid.Should().Be(13803, form);
            cmap.IsUnicode.Should().BeTrue();
            cmap.CodeToUnicode(hiragana).Should().Be("あ");
        }
    }

    [Fact]
    public void VerticalCMaps_UseTheirHorizontalParent()
    {
        PredefinedCMap cmap = PredefinedCMap.Get("UniJIS-UTF16-V")!;

        cmap.IsVertical.Should().BeTrue();
        cmap.Next(new byte[] { 0x30, 0x01 }, out _, out int cid).Should().Be(2);
        cid.Should().Be(7887, "the vertical form of the ideographic comma");
        cmap.Next(new byte[] { 0x30, 0x42 }, out _, out cid);
        cid.Should().Be(843, "from the horizontal CMap it uses");
    }

    [Theory]
    [InlineData("KSCms-UHC-H", 0xB0A1, 1086)]
    [InlineData("GBK-EUC-H", 0xB0A1, 940)]
    [InlineData("GBK-EUC-H", 0x41, 846)]
    [InlineData("Identity-H", 0x1234, 0x1234)]
    public void OtherCollections_Map(string name, int code, int expected)
    {
        byte[] bytes = code > 0xFF ? new[] { (byte)(code >> 8), (byte)code } : new[] { (byte)code };

        PredefinedCMap.Get(name)!.Next(bytes, out int read, out int cid).Should().Be(bytes.Length);
        read.Should().Be(code);
        cid.Should().Be(expected);
    }

    [Fact]
    public void CidsMapBackToUnicode()
    {
        PredefinedCMap.CidToUnicode("Japan1", 843).Should().Be("あ");
        PredefinedCMap.CidToUnicode("Japan1", 13803).Should().Be("\U00020B9F");
        PredefinedCMap.CidToUnicode("Japan1", 264).Should().Be("A", "a half-width CID, from UniJIS-UCS2-HW-H");
        PredefinedCMap.CidToUnicode("Japan1", 7887).Should().Be("\u3001", "a vertical form reads as its character, not its presentation form");
        PredefinedCMap.CidToUnicode("Japan1", 3284).Should().Be("\u65E5", "日, not the Kangxi radical ⽇ the CID also maps from");
        PredefinedCMap.CidToUnicode("Identity", 843).Should().BeNull();
    }

    [Fact]
    public void UnknownNames_GiveNull()
    {
        PredefinedCMap.Get("No-Such-CMap").Should().BeNull();
    }

    private static byte[] Encode(string form, string text) => form switch
    {
        "UTF8" => Encoding.UTF8.GetBytes(text),
        "UTF32" => new UTF32Encoding(true, false).GetBytes(text),
        _ => Encoding.BigEndianUnicode.GetBytes(text),
    };
}
