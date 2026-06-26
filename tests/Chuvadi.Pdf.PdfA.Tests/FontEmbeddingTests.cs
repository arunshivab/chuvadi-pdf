// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.PdfA.Tests;

public sealed class FontEmbeddingTests
{
    [Fact]
    public void SimpleFontProgram_Build_SubsetsAndMapsAscii()
    {
        byte[]? ttf = LiberationFontProvider.Get("LiberationSans-Regular");
        ttf.Should().NotBeNull();

        EmbeddableFont program = SimpleFontProgram.Build(ttf!, WinAnsiEncoding.CodeToUnicode());

        // Valid TrueType sfnt (0x00010000), with glyphs and widths for ASCII.
        program.Sfnt.Should().HaveCountGreaterThan(4);
        (program.Sfnt[0], program.Sfnt[1], program.Sfnt[2], program.Sfnt[3]).Should().Be(((byte)0x00, (byte)0x01, (byte)0x00, (byte)0x00));
        program.GidByCode['A'].Should().BeGreaterThan(0);
        program.GidByCode['a'].Should().BeGreaterThan(0);
        program.GidByCode['0'].Should().BeGreaterThan(0);
        program.WidthByCode['A'].Should().BeGreaterThan(0);
        program.UnitsPerEm.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Standard14Map_MapsFacesAndToleratesSubsetPrefix()
    {
        Standard14Map.Lookup("Helvetica").Should().Be(new Standard14Substitute("LiberationSans-Regular", false));
        Standard14Map.Lookup("Times-Bold").Should().Be(new Standard14Substitute("LiberationSerif-Bold", true));
        Standard14Map.Lookup("Courier").Should().Be(new Standard14Substitute("LiberationMono-Regular", false));
        Standard14Map.Lookup("ABCDEF+Helvetica").Should().Be(new Standard14Substitute("LiberationSans-Regular", false));
        Standard14Map.Lookup("NoSuchFont").Should().BeNull();
    }
}
