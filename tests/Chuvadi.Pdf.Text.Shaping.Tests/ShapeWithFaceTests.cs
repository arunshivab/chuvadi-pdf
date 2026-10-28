// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// TextShaper.Shape(FontFace, ...) (3.25.0): same output as the byte overload,
// for TrueType and CFF faces, with the face's lookups cached.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Chuvadi.Pdf.Fonts.Rendering;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Text.Shaping.Tests;

public sealed class ShapeWithFaceTests
{
    private static byte[] Fixture(params string[] path) =>
        File.ReadAllBytes(Path.Combine(new[] { AppContext.BaseDirectory, "Fixtures" }.Concat(path).ToArray()));

    private static byte[] Fixture(string relative) => Fixture(relative.Split('/'));

    public static TheoryData<string, string, LipiScript> Runs => new TheoryData<string, string, LipiScript>
    {
        { "Carlito-Regular.ttf", "office affine AVATAR, Typography: fi fl.", LipiScript.Latin },
        { "LiPi-Sans-Tamil.ttf", "தமிழ் நாட்டின் தலைநகரம் சென்னை", LipiScript.Tamil },
        { "Fonts/ChuvadiTest-Glyf.ttf", "fi AfiH Wx", LipiScript.Latin },
        { "Fonts/ChuvadiTest-Cff.otf", "fi AfiH Wx", LipiScript.Latin },
        { "Fonts/ChuvadiTest-Cid.otf", "fi AfiH Wx", LipiScript.Latin },
    };

    [Theory]
    [MemberData(nameof(Runs))]
    public void ShapeWithFace_EqualsShapeWithBytes(string font, string text, LipiScript script)
    {
        ArgumentNullException.ThrowIfNull(font);
        byte[] bytes = Fixture(font);
        FontFace face = FontFace.Load(bytes);

        IReadOnlyList<ShapedGlyph> fromBytes = TextShaper.Shape(bytes, text, script);
        IReadOnlyList<ShapedGlyph> fromFace = TextShaper.Shape(face, text, script);
        IReadOnlyList<ShapedGlyph> again = TextShaper.Shape(face, text, script);

        fromFace.Should().Equal(fromBytes);
        again.Should().Equal(fromBytes, "the cached lookups give the same result");
    }

    [Theory]
    [InlineData("Fonts/ChuvadiTest-Cff.otf")]
    [InlineData("Fonts/ChuvadiTest-Glyf.ttf")]
    public void ShapeWithFace_AppliesTheLigature(string font)
    {
        ArgumentNullException.ThrowIfNull(font);
        FontFace face = FontFace.Load(Fixture(font));

        IReadOnlyList<ShapedGlyph> glyphs = TextShaper.Shape(face, "fi", LipiScript.Latin);

        glyphs.Should().ContainSingle().Which.GlyphId.Should().Be(9, "f + i become the f_i ligature");
        glyphs[0].XAdvance.Should().Be(540);
    }

    [Fact]
    public void ShapeWithFace_CollectionFace()
    {
        FontFace cff = FontFace.Load(Fixture("Fonts", "ChuvadiTest.ttc"), 1);

        TextShaper.Shape(cff, "AfiW", LipiScript.Latin).Select(g => g.GlyphId)
            .Should().Equal(2, 9, 8);
    }

    [Fact]
    public void ShapeWithFace_IsSafeToShareAcrossThreads()
    {
        FontFace face = FontFace.Load(Fixture("Carlito-Regular.ttf"));
        IReadOnlyList<ShapedGlyph> expected = TextShaper.Shape(face, "office affine", LipiScript.Latin);

        Parallel.For(0, 32, i =>
        {
            TextShaper.Shape(face, "office affine", LipiScript.Latin).Should().Equal(expected);
        });
    }

    [Fact]
    public void ShapeWithFace_RejectsBadArguments()
    {
        FontFace face = FontFace.Load(Fixture("Fonts", "ChuvadiTest-Cff.otf"));

        Action nullFace = () => TextShaper.Shape((FontFace)null!, "a", LipiScript.Latin);
        Action nullText = () => TextShaper.Shape(face, null!, LipiScript.Latin);
        Action empty = () => TextShaper.Shape(face, string.Empty, LipiScript.Latin);

        nullFace.Should().Throw<ArgumentNullException>();
        nullText.Should().Throw<ArgumentNullException>();
        empty.Should().Throw<ArgumentException>();
    }
}
