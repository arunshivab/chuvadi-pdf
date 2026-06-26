// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Authoring.Tests;

public sealed class LipiFontTests
{
    [Fact]
    public void Classify_MapsIndicBlocksAndDefaultsToLatin()
    {
        ScriptClassifier.Classify('A').Should().Be(LipiScript.Latin);
        ScriptClassifier.Classify(0x0B95).Should().Be(LipiScript.Tamil);
        ScriptClassifier.Classify(0x0915).Should().Be(LipiScript.Devanagari);
        ScriptClassifier.Classify(0x0C15).Should().Be(LipiScript.Telugu);
        ScriptClassifier.Classify(0x0D15).Should().Be(LipiScript.Malayalam);
    }

    [Fact]
    public void Split_SegmentsMixedScriptText()
    {
        var runs = ScriptClassifier.Split("Hi \u0BA4\u0BAE\u0BBF\u0BB4\u0BCD ok");

        runs.Should().HaveCount(3);
        runs[0].Script.Should().Be(LipiScript.Latin);
        runs[1].Script.Should().Be(LipiScript.Tamil);
        runs[2].Script.Should().Be(LipiScript.Latin);
        runs[1].Text.Should().Be("\u0BA4\u0BAE\u0BBF\u0BB4\u0BCD ");
    }

    [Fact]
    public void UseLipiFonts_EmbedsPerScriptFacesForMixedText()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        builder.UseLipiFonts();
        PageBuilder page = builder.AddPage(PageSize.A4);
        page.DrawText("Hello \u0BA4\u0BAE\u0BBF\u0BB4\u0BCD", 50, 100, "Lipi", 24, Color.FromHex("#000000"));

        byte[] pdf = builder.ToByteArray();
        string body = Encoding.Latin1.GetString(pdf);

        body.Should().Contain("LiPi-Sans-Latin");
        body.Should().Contain("LiPi-Sans-Tamil");
        body.Should().Contain("Identity-H");
        body.Should().Contain("CIDFontType2");
        pdf.Length.Should().BeGreaterThan(2000);
    }
}
