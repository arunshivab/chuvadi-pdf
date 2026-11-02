// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.29 — raised and lowered text: BaselineShift on fragments and the
//        three superscript modes (D-104).

using System.Collections.Generic;
using Chuvadi.Pdf.Content;
using Chuvadi.Pdf.Documents;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Text.Tests;

public sealed class SuperscriptModeTests
{
    // "Cionini" (12 pt, 6 pt per character), a raised 7 pt "a" touching it,
    // then ", Gardani" back on the baseline.
    private const string Affiliation =
        "BT /F1 12 Tf 50 300 Td (Cionini) Tj ET\n" +
        "BT /F1 7 Tf 92 305 Td (a) Tj ET\n" +
        "BT /F1 12 Tf 95.5 300 Td (, Gardani) Tj ET\n";

    // Every character with an exact Unicode superscript form.
    private const string SuperscriptCharacters = "0123456789+-=()abcdefghijklmnoprstuvwxyzABDEGHIJKLMNOPRTUVW";

    // Every character with an exact Unicode subscript form.
    private const string SubscriptCharacters = "0123456789+-=()aehijklmnoprstuvx";

    [Theory]
    [InlineData(ExtractionStrategy.Operator)]
    [InlineData(ExtractionStrategy.Layout)]
    public void ByDefault_ARaisedLetterIsWrittenAsItsSuperscript(ExtractionStrategy strategy)
    {
        Extract(Affiliation, strategy).Should().Be("Cioniniᵃ, Gardani");
    }

    [Theory]
    [InlineData(ExtractionStrategy.Operator)]
    [InlineData(ExtractionStrategy.Layout)]
    public void JoinMode_JoinsRaisedTextUnchanged(ExtractionStrategy strategy)
    {
        Extract(Affiliation, strategy, SuperscriptMode.Join).Should().Be("Cioninia, Gardani");
    }

    [Theory]
    [InlineData(ExtractionStrategy.Operator)]
    [InlineData(ExtractionStrategy.Layout)]
    public void SeparateMode_KeepsTheRaisedLetterApart(ExtractionStrategy strategy)
    {
        Extract(Affiliation, strategy, SuperscriptMode.Separate).Should().Be("Cionini a, Gardani");
    }

    [Theory]
    [InlineData(ExtractionStrategy.Operator)]
    [InlineData(ExtractionStrategy.Layout)]
    public void ARaisedOrdinalEnding_JoinsItsNumberAsTyped(ExtractionStrategy strategy)
    {
        string content =
            "BT /F1 12 Tf 50 300 Td (Born 09) Tj ET\n" +
            "BT /F1 7 Tf 92 305 Td (th) Tj ET\n" +
            "BT /F1 12 Tf 99 300 Td ( September) Tj ET\n";

        Extract(content, strategy).Should().Be("Born 09th September");
    }

    [Theory]
    [InlineData(ExtractionStrategy.Operator)]
    [InlineData(ExtractionStrategy.Layout)]
    public void ExponentsAndSubscripts_UseTheirUnicodeForms(ExtractionStrategy strategy)
    {
        string content =
            "BT /F1 12 Tf 50 300 Td (E = mc) Tj ET\n" +
            "BT /F1 7 Tf 86 305 Td (2) Tj ET\n" +
            "BT /F1 12 Tf 50 270 Td (CO) Tj ET\n" +
            "BT /F1 7 Tf 62 267.5 Td (2) Tj ET\n" +
            "BT /F1 12 Tf 50 240 Td (10) Tj ET\n" +
            "BT /F1 7 Tf 62 245 Td (-3) Tj ET\n";

        TestPdf.Lines(Extract(content, strategy)).Should().Equal("E = mc²", "CO₂", "10⁻³");
    }

    [Fact]
    public void RaisedTextWithNoExactForm_StaysASeparateWord()
    {
        // "q" has no Unicode superscript form: the 3.28 rule applies.
        string content =
            "BT /F1 12 Tf 50 300 Td (Model) Tj ET\n" +
            "BT /F1 7 Tf 80 305 Td (q) Tj ET\n";

        Extract(content, ExtractionStrategy.Layout).Should().Be("Model q");
    }

    [Fact]
    public void ARaisedWordOfMoreThanThreeLetters_StaysAWord()
    {
        string content =
            "BT /F1 12 Tf 50 300 Td (Note) Tj ET\n" +
            "BT /F1 7 Tf 74 305 Td (beta) Tj ET\n";

        Extract(content, ExtractionStrategy.Layout).Should().Be("Note beta");
    }

    [Fact]
    public void RaisedActualText_IsNeverRewritten()
    {
        // The raised "2" comes from /ActualText: the source text wins.
        string content =
            "BT /F1 12 Tf 50 300 Td (E = mc) Tj ET\n" +
            "/Span <</ActualText (2)>> BDC BT /F1 7 Tf 86 305 Td (x) Tj ET EMC\n";

        Extract(content, ExtractionStrategy.Layout).Should().Be("E = mc 2");
    }

    [Fact]
    public void Fragments_CarryTheirBaselineShift()
    {
        string content =
            "BT /F1 12 Tf 50 300 Td (CO) Tj ET\n" +
            "BT /F1 7 Tf 62 297 Td (2) Tj ET\n" +
            "BT /F1 12 Tf 50 270 Td (x) Tj ET\n" +
            "BT /F1 7 Tf 56 275 Td (2) Tj ET\n";
        using PdfDocument doc = TestPdf.Open(content);

        List<TextFragment> f = new TextExtractor(doc.Objects).ExtractFragments(doc.Pages[0]);

        f[0].BaselineShift.Should().Be(BaselineShift.None);
        f[1].BaselineShift.Should().Be(BaselineShift.Lowered);
        f[1].BaselineShiftAmount.Should().BeApproximately(-3, 1e-9);
        f[1].Text.Should().Be("2");
        f[3].BaselineShift.Should().Be(BaselineShift.Raised);
        f[3].BaselineShiftAmount.Should().BeApproximately(5, 1e-9);
    }

    [Fact]
    public void SmallTextOnItsOwnLine_IsNotRaised()
    {
        // A footnote line in small type is on its own baseline.
        string content =
            "BT /F1 12 Tf 50 300 Td (Body text) Tj ET\n" +
            "BT /F1 7 Tf 50 280 Td (A footnote) Tj ET\n";
        using PdfDocument doc = TestPdf.Open(content);

        List<TextFragment> f = new TextExtractor(doc.Objects).ExtractFragments(doc.Pages[0]);

        f[1].BaselineShift.Should().Be(BaselineShift.None);
    }

    [Fact]
    public void EverySuperscriptForm_NormalisesBackToItsCharacter()
    {
        foreach (char c in SuperscriptCharacters)
        {
            string written = new LayoutExtractor().Extract(Around(c, 5));
            string form = written.Substring(1, written.Length - 2);

            form.Should().NotBe(c.ToString(), "'{0}' has a superscript form", c);
            form.Normalize(System.Text.NormalizationForm.FormKC).Should().Be(c == '-' ? "−" : c.ToString());
        }
    }

    [Fact]
    public void EverySubscriptForm_NormalisesBackToItsCharacter()
    {
        foreach (char c in SubscriptCharacters)
        {
            string written = new LayoutExtractor().Extract(Around(c, -3));
            string form = written.Substring(1, written.Length - 2);

            form.Should().NotBe(c.ToString(), "'{0}' has a subscript form", c);
            form.Normalize(System.Text.NormalizationForm.FormKC).Should().Be(c == '-' ? "−" : c.ToString());
        }
    }

    [Fact]
    public void ASourceSuperscriptCharacter_IsKeptAndJoined()
    {
        // The PDF's own text is already "²" (from its ToUnicode map).
        List<TextFragment> fragments = new List<TextFragment>
        {
            new TextFragment("mc", 50, 300, 12, 12),
            new TextFragment("²", 62, 305, 7, 3.5),
        };

        new LayoutExtractor().Extract(fragments).Should().Be("mc²");
    }

    // "X", the character raised or lowered by shift at 7 pt, then "Y".
    private static List<TextFragment> Around(char c, double shift) => new List<TextFragment>
    {
        new TextFragment("X", 50, 300, 12, 6),
        new TextFragment(c.ToString(), 56, 300 + shift, 7, 3.5),
        new TextFragment("Y", 59.5, 300, 12, 6),
    };

    private static string Extract(string content, ExtractionStrategy strategy, SuperscriptMode mode = SuperscriptMode.Unicode)
    {
        using PdfDocument doc = TestPdf.Open(content);
        return new TextExtractor(doc.Objects, strategy) { Superscripts = mode }.ExtractText(doc.Pages[0]).Trim();
    }
}
