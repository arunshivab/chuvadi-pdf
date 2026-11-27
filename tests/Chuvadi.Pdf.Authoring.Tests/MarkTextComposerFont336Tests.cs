// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.36.0 (D-412) — the mark-text composer's optional chosen primary font:
//        when caller font bytes are supplied they are the primary face for every
//        code point they cover, so the drawn glyph ids come from that font rather
//        than the default LiPi Sans face.

using System.Globalization;
using System.IO;
using Chuvadi.Pdf.Fonts.Rendering;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Authoring.Tests;

public sealed class MarkTextComposerFont336Tests
{
    [Fact]
    public void Compose_WithChosenFont_DrawsGlyphsFromThatFont()
    {
        byte[] chosen = File.ReadAllBytes(System.IO.Path.Combine(
            System.AppContext.BaseDirectory, "Fixtures", "LiberationSerif-Regular.ttf"));

        // The chosen font's own glyph id for 'R' — what its content must show.
        FontFace face = FontFace.Load(chosen);
        int gid = face.GetGlyphIndex('R');
        gid.Should().BeGreaterThan(0);
        string chosenHex = (gid & 0xFFFF).ToString("X4", CultureInfo.InvariantCulture);

        int next = 10;
        string withChosen = new MarkTextComposer(chosen)
            .Compose("R", 24, ColorF.Black, () => new PdfObjectId(next++, 0), "Cf")
            .ContentFragment;
        string withDefault = new MarkTextComposer()
            .Compose("R", 24, ColorF.Black, () => new PdfObjectId(next++, 0), "Df")
            .ContentFragment;

        withChosen.Should().Contain("<" + chosenHex + ">", "the chosen font supplies the glyph");
        withChosen.Should().NotBe(withDefault, "the chosen font changes the drawn glyph ids");
    }
}
