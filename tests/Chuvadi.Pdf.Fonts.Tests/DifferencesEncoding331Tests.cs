// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.31 (D-172) — a /Differences encoding: every Adobe Glyph List name
//        is applied, and without /BaseEncoding the base is the standard 14
//        font's built-in encoding (PDF 32000-1:2008 §9.6.6.1, Annex D).

using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Fonts.Tests;

public sealed class DifferencesEncoding331Tests
{
    // Codes A (0x41), quotesingle/quoteright (0x27), grave/quoteleft (0x60) and 0xE1.
    private static readonly byte[] Probe = { 0x41, 0x27, 0x60, 0xE1 };

    [Theory]
    [InlineData("Courier")]
    [InlineData("Helvetica-Bold")]
    [InlineData("Times-Roman")]
    [InlineData("ABCDEF+Times-Italic")]
    public void ATextFont_WithoutBaseEncoding_StartsFromStandardEncoding_AndAppliesTheDifferences(string baseFont)
    {
        PdfFont font = Font(baseFont, Differences(0x41, "B"), baseEncoding: null);

        font.Decode(Probe).Should().Be("B’‘Æ", "StandardEncoding has quoteright, quoteleft and AE at these codes");
    }

    [Fact]
    public void Symbol_WithoutBaseEncoding_StartsFromItsOwnEncoding()
    {
        PdfFont font = Font("Symbol", Differences(0x42, "B"), baseEncoding: null);

        font.Decode(new byte[] { 0x41, 0x42, 0x61, 0x27 }).Should().Be("ΑBα∋");
    }

    [Fact]
    public void ZapfDingbats_WithoutBaseEncoding_StartsFromItsOwnEncoding()
    {
        PdfFont font = Font("ZapfDingbats", Differences(0x42, "B"), baseEncoding: null);

        font.Decode(new byte[] { 0x21, 0x42, 0x41 }).Should().Be("✁B✡");
    }

    [Fact]
    public void AnExplicitBaseEncoding_IsStillTheBase()
    {
        PdfFont font = Font("Courier", Differences(0x41, "eacute"), baseEncoding: "WinAnsiEncoding");

        font.Decode(Probe).Should().Be("é'`á");
    }

    [Fact]
    public void ANonSymbolicFontThatIsNotEmbedded_StartsFromStandardEncoding()
    {
        // D-176, Table 114: "for a nonsymbolic font, it shall be StandardEncoding".
        PdfFont font = Font("Arial", Differences(0x41, "B"), baseEncoding: null);

        font.Decode(Probe).Should().Be("B\u2019\u2018\u00C6");
    }

    [Fact]
    public void ASymbolicFontWhoseBuiltInEncodingIsUnknown_KeepsWinAnsiAsTheBase()
    {
        PdfFont font = Font("Wingdings", Differences(0x41, "B"), baseEncoding: null, flags: 4);

        font.Decode(Probe).Should().Be("B'`\u00E1");
    }

    [Fact]
    public void AnEmbeddedType1Font_StartsFromItsProgramsBuiltInEncoding()
    {
        // The cleartext part of a Type 1 program: code 0x42 is "Eacute",
        // 0x61 is "one"; codes the program does not list are unmapped.
        string cleartext = "%!PS-AdobeFont-1.0: Test\n/Encoding 256 array\n0 1 255 {1 index exch /.notdef put} for\n"
            + "dup 66 /Eacute put\ndup 97 /one put\nreadonly def\ncurrentfile eexec\n";
        PdfStream program = new PdfStream(new PdfDictionary(), System.Text.Encoding.Latin1.GetBytes(cleartext));
        PdfFont font = Font("ABCDEF+CMR10", Differences(0x41, "B"), baseEncoding: null, fontFile: ("FontFile", program));

        font.GetEncodingCharacter(0x41).Should().Be('B');
        font.GetEncodingCharacter(0x42).Should().Be('\u00C9');
        font.GetEncodingCharacter(0x61).Should().Be('1');
        font.GetEncodingCharacter(0x43).Should().Be('\0');
    }

    [Fact]
    public void AnEmbeddedCffFont_StartsFromItsCustomEncoding()
    {
        // A synthetic CFF (fontTools) whose custom Encoding maps 0x41 A,
        // 0x42 B and 0x61 eacute.
        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Intern("Subtype"), PdfName.Intern("Type1C"));
        PdfStream program = new PdfStream(dict, System.Convert.FromBase64String(CustomEncodingCff));
        PdfFont font = Font("ABCDEF+Tst", Differences(0x42, "C"), baseEncoding: null, fontFile: ("FontFile3", program));

        font.GetEncodingCharacter(0x41).Should().Be('A');
        font.GetEncodingCharacter(0x42).Should().Be('C');
        font.GetEncodingCharacter(0x61).Should().Be('\u00E9');
        font.GetEncodingCharacter(0x62).Should().Be('\0');
    }

    [Theory]
    [InlineData("Helvetica")]
    [InlineData("Courier-Bold")]
    public void AStandard14TextFont_WithNoEncoding_UsesStandardEncoding(string baseFont)
    {
        // D-176: the built-in encoding (before 3.31, WinAnsiEncoding).
        PdfFont font = FontWithoutEncoding(baseFont);

        font.Decode(Probe).Should().Be("A\u2019\u2018\u00C6");
    }

    [Fact]
    public void SymbolAndZapfDingbats_WithNoEncoding_UseTheirOwn()
    {
        FontWithoutEncoding("Symbol").Decode(new byte[] { 0x61, 0x27 }).Should().Be("\u03B1\u220B");
        FontWithoutEncoding("ZapfDingbats").Decode(new byte[] { 0x21 }).Should().Be("\u2701");
    }

    [Theory]
    [InlineData("A", "A")]
    [InlineData("eacute", "é")]
    [InlineData("one", "1")]
    [InlineData("Alpha", "\u0391")]
    [InlineData("uni0BB5", "வ")]
    [InlineData("notAGlyphName", "")]
    public void EveryAdobeGlyphListName_InDifferences_IsApplied(string glyph, string expected)
    {
        PdfFont font = Font("Helvetica", Differences(0x80, glyph), baseEncoding: "WinAnsiEncoding");

        font.DecodeCode(0x80).Should().Be(expected);
    }

    [Fact]
    public void TheStandardEncodingName_HasAllItsCodes()
    {
        PdfFontEncoding standard = PdfFontEncoding.FromNamedEncoding("StandardEncoding");

        standard.GetCharacter(0xE1).Should().Be('Æ');
        standard.GetCharacter(0xFB).Should().Be('ß');
        standard.GetCharacter(0xA4).Should().Be('⁄');
        standard.GetCharacter(0x27).Should().Be('’');
        standard.IsMapped(0xA0).Should().BeFalse();
    }

    private const string CustomEncodingCff = "AQAEAQABAQEEVHN0AAEBARL4GwKLi+/vBbcPvhCL6xLDEQABAQEEVHN0AAAAACIAIwDPAANBQmEABAEBCREZIfiIixbv7wYO+IiLFu/vBg74iIsW7+8GDviIixbv7wYO";

    private static PdfFont FontWithoutEncoding(string baseFont)
    {
        PdfDictionary font = new PdfDictionary();
        font.Set(PdfName.Intern("Type"), PdfName.Intern("Font"));
        font.Set(PdfName.Intern("Subtype"), PdfName.Intern("Type1"));
        font.Set(PdfName.Intern("BaseFont"), PdfName.Intern(baseFont));
        return PdfFont.FromDictionary(font, new DirectResolver());
    }

    private static PdfArray Differences(int code, string name) =>
        new PdfArray { new PdfInteger(code), PdfName.Intern(name) };

    private static PdfFont Font(string baseFont, PdfArray differences, string? baseEncoding, int flags = 32, (string Key, PdfStream Program)? fontFile = null)
    {
        PdfDictionary encoding = new PdfDictionary();
        encoding.Set(PdfName.Intern("Type"), PdfName.Intern("Encoding"));
        encoding.Set(PdfName.Intern("Differences"), differences);
        if (baseEncoding is not null)
        {
            encoding.Set(PdfName.Intern("BaseEncoding"), PdfName.Intern(baseEncoding));
        }

        PdfDictionary font = new PdfDictionary();
        font.Set(PdfName.Intern("Type"), PdfName.Intern("Font"));
        font.Set(PdfName.Intern("Subtype"), PdfName.Intern("Type1"));
        font.Set(PdfName.Intern("BaseFont"), PdfName.Intern(baseFont));
        font.Set(PdfName.Intern("Encoding"), encoding);
        PdfDictionary descriptor = new PdfDictionary();
        descriptor.Set(PdfName.Intern("Type"), PdfName.Intern("FontDescriptor"));
        descriptor.Set(PdfName.Intern("Flags"), (PdfPrimitive)new PdfInteger(flags));
        if (fontFile is (string key, PdfStream program))
        {
            descriptor.Set(PdfName.Intern(key), program);
        }

        font.Set(PdfName.Intern("FontDescriptor"), descriptor);
        return PdfFont.FromDictionary(font, new DirectResolver());
    }

    private sealed class DirectResolver : IPdfObjectResolver
    {
        public PdfPrimitive Resolve(PdfPrimitive primitive) => primitive;

        public PdfPrimitive ResolveById(PdfObjectId id) => PdfNull.Value;

        public bool Contains(PdfObjectId id) => false;
    }
}
