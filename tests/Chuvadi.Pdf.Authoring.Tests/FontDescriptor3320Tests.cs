// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.32.0 (D-244, D-245, D-263) — /StemV and /Flags of embedded fonts.

using System;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Authoring.Tests;

public sealed class FontDescriptor3320Tests
{
    [Theory]
    [InlineData(100, 52)]
    [InlineData(400, 88)]
    [InlineData(700, 166)]
    [InlineData(900, 242)]
    public void StemV_FollowsTheWeightFormula(int weight, int expected)
    {
        FontDescriptorPolicy.StemV(weight).Should().Be(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1001)]
    public void StemV_MissingOrInvalidWeight_CountsAsRegular(int weight)
    {
        FontDescriptorPolicy.StemV(weight).Should().Be(88);
    }

    [Theory]
    [InlineData(0x0105, true)]
    [InlineData(0x0500, true)]
    [InlineData(0x0700, true)]
    [InlineData(0x0600, false)]
    [InlineData(0x0805, false)]
    [InlineData(0x0A00, false)]
    [InlineData(0x0000, false)]
    public void IsSerifClass_UsesTheHighByte(int familyClass, bool expected)
    {
        FontDescriptorPolicy.IsSerifClass(familyClass).Should().Be(expected);
    }

    [Fact]
    public void Flags_CombineTheRules_AndNeverSetForceBold()
    {
        FontDescriptorPolicy.Flags(symbolic: true, italic: false, fixedPitch: false, familyClass: 0).Should().Be(4);
        FontDescriptorPolicy.Flags(symbolic: false, italic: false, fixedPitch: false, familyClass: 0).Should().Be(32);
        FontDescriptorPolicy.Flags(symbolic: true, italic: true, fixedPitch: true, familyClass: 0x0105).Should().Be(4 | 64 | 1 | 2);
        FontDescriptorPolicy.Flags(symbolic: false, italic: true, fixedPitch: true, familyClass: 0x0805).Should().Be(32 | 64 | 1);
        int all = FontDescriptorPolicy.Flags(symbolic: true, italic: true, fixedPitch: true, familyClass: 0x0105);
        (all & (1 << 18)).Should().Be(0, "ForceBold is never set (D-245)");
    }

    [Fact]
    public void Read_FixtureFont_GivesItsTableValues()
    {
        FontDescriptorPolicy.Inputs inputs = FontDescriptorPolicy.Read(LoadFixtureFont());

        inputs.WeightClass.Should().Be(400);
        inputs.FamilyClass.Should().Be(0x0105);
        inputs.FixedPitch.Should().BeFalse();
        inputs.MacStyleItalic.Should().BeFalse();
        inputs.ItalicAngle.Should().Be(0);
    }

    [Fact]
    public void Read_NotAnSfnt_GivesTheDefaults()
    {
        FontDescriptorPolicy.Inputs expected = new FontDescriptorPolicy.Inputs(400, 0, false, false, 0);

        FontDescriptorPolicy.Read(Array.Empty<byte>()).Should().Be(expected);
        FontDescriptorPolicy.Read(new byte[] { 1, 0, 4, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }).Should().Be(expected);
    }

    [Fact]
    public void Read_TableDirectoryPointingPastTheEnd_GivesTheDefaults()
    {
        // sfnt header, one table record "OS/2" at offset 1000, length 96.
        byte[] font = new byte[28];
        font[1] = 1;
        font[5] = 1;
        font[12] = (byte)'O';
        font[13] = (byte)'S';
        font[14] = (byte)'/';
        font[15] = (byte)'2';
        font[22] = 0x03;
        font[23] = 0xE8;
        font[27] = 96;

        FontDescriptorPolicy.Read(font).Should().Be(new FontDescriptorPolicy.Inputs(400, 0, false, false, 0));
    }

    [Fact]
    public void Read_Null_Throws()
    {
        Action act = () => FontDescriptorPolicy.Read(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddTrueTypeFont_DescriptorHasComputedStemVAndSerifFlag()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddTrueTypeFont("Body", LoadFixtureFont());
        builder.AddPage(PageSize.A4).DrawText("Stem", 50, 50, "Body", 12, Colors.Black);
        byte[] bytes = builder.ToByteArray();

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(bytes), leaveOpen: false);
        PdfDictionary descriptor = Descriptor(doc, "Body");

        // Liberation Serif Regular: weight 400, family class 0x0105 (serif).
        IntOf(descriptor, "StemV").Should().Be(88, "50 + (400 / 65)^2, rounded (was 80 before 3.32.0)");
        IntOf(descriptor, "Flags").Should().Be(4 | 2, "Symbolic for the Identity-encoded composite font, plus Serif");
    }

    private static PdfDictionary Descriptor(PdfDocument doc, string key)
    {
        PdfObjectStore store = doc.Objects;
        PdfDictionary resources = store.ResolveAs<PdfDictionary>(doc.Pages[0].Dictionary[PdfName.Intern("Resources")])!;
        PdfDictionary fonts = store.ResolveAs<PdfDictionary>(resources[PdfName.Intern("Font")])!;
        PdfDictionary type0 = store.ResolveAs<PdfDictionary>(fonts[PdfName.Intern(key)])!;
        PdfArray descendants = store.ResolveAs<PdfArray>(type0[PdfName.Intern("DescendantFonts")])!;
        PdfDictionary cidFont = store.ResolveAs<PdfDictionary>(descendants[0])!;
        return store.ResolveAs<PdfDictionary>(cidFont[PdfName.Intern("FontDescriptor")])!;
    }

    private static int IntOf(PdfDictionary dict, string key) =>
        ((PdfInteger)dict[PdfName.Intern(key)]).Value;

    private static byte[] LoadFixtureFont() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "LiberationSerif-Regular.ttf"));
}
