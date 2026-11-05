// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.30 — one shared copy of the bundled fonts (D-87).

// Fonts.Bundled and Fonts.Woff2 are deprecated (3.31.0) but still ship and are still tested.
#pragma warning disable CS0618

using System;
using Chuvadi.Pdf.Fonts.Bundled;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.Tests;

public sealed class BundledFontsTests
{
    [Fact]
    public void TenFaces_AreBundled_AndEachDecodesToATrueTypeProgram()
    {
        BundledFonts.FaceNames.Should().HaveCount(10).And.Contain("LiPi-Sans-Latin").And.Contain("LiPi-Sans-Malayalam");
        foreach (string face in BundledFonts.FaceNames)
        {
            byte[] program = BundledFonts.GetFontProgram(face);

            program.Length.Should().BeGreaterThan(1000);
            ((program[0] << 24) | (program[1] << 16) | (program[2] << 8) | program[3]).Should().Be(0x00010000, face);
        }
    }

    [Fact]
    public void AFace_IsDecodedOnce_AndShared()
    {
        BundledFonts.GetFontProgram("LiPi-Sans-Tamil").Should().BeSameAs(BundledFonts.GetFontProgram("LiPi-Sans-Tamil"));
    }

    [Fact]
    public void FaceForScript_NamesTheFace_OrNothing()
    {
        BundledFonts.FaceForScript("Odia").Should().Be("LiPi-Sans-Odia");
        BundledFonts.FaceForScript("Arabic").Should().BeNull();
    }

    [Fact]
    public void AnUnknownFace_Throws()
    {
        Action load = () => BundledFonts.GetFontProgram("Arial");

        load.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("faceName");
    }
}
