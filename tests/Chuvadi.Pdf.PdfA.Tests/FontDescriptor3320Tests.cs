// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.32.0 (D-244, D-245, D-263) — /StemV and /Flags of the PDF/A
//        substitute fonts.

using System.Linq;
using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.PdfA.Tests;

public sealed class FontDescriptor3320Tests
{
    [Theory]
    [InlineData("LiberationSans-Regular", false, 88, 32)]
    [InlineData("LiberationSans-BoldItalic", false, 166, 32 | 64)]
    [InlineData("LiberationSerif-Regular", true, 88, 32 | 2)]
    [InlineData("LiberationSerif-Bold", true, 166, 32 | 2)]
    [InlineData("LiberationMono-Regular", false, 88, 32 | 1)]
    [InlineData("LiberationMono-Italic", false, 88, 32 | 1 | 64)]
    public void Substitute_DescriptorFollowsThePolicy(string face, bool serif, int stemV, int flags)
    {
        PdfDictionary descriptor = BuildDescriptor(face, serif);

        IntOf(descriptor, "StemV").Should().Be(stemV);
        IntOf(descriptor, "Flags").Should().Be(flags);
    }

    [Fact]
    public void Substitute_SerifChoiceStillSetsSerif_ForASansFace()
    {
        // The caller's serif choice is kept as before 3.32.0, on top of the policy.
        IntOf(BuildDescriptor("LiberationSans-Regular", serif: true), "Flags").Should().Be(32 | 2);
    }

    private static PdfDictionary BuildDescriptor(string face, bool serif)
    {
        byte[] ttf = LiberationFaces.Get(face)!;
        EmbeddableFont program = SimpleFontProgram.Build(ttf, WinAnsiEncoding.CodeToUnicode());
        int next = 10;
        SimpleFontEmbedder.FontObjects built = SimpleFontEmbedder.BuildSimpleTrueTypeFont(
            ttf, program, face, serif, () => new PdfObjectId(next++, 0));

        return built.Objects
            .Select(o => o.Value)
            .OfType<PdfDictionary>()
            .Single(d => d.TryGetValue(PdfName.Type, out PdfPrimitive? t) && t is PdfName n && n.Value == "FontDescriptor");
    }

    private static int IntOf(PdfDictionary dict, string key) =>
        ((PdfInteger)dict[PdfName.Intern(key)]).Value;
}
