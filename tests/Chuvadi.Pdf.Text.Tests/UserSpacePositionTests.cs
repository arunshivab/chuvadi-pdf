// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.28 — text positions in user space (backlog 26). Fragments are
//        placed by the text matrix mapped through the CTM, text-space moves
//        (Td, TD, T*, advances, TJ numbers) are scaled with the text, and
//        form XObjects are placed by their /Matrix and the CTM at the Do.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Chuvadi.Pdf.Content;
using Chuvadi.Pdf.Documents;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Text.Tests;

public sealed class UserSpacePositionTests
{
    [Fact]
    public void TextScaledThroughTm_HasItsDrawnSizeAndPosition()
    {
        // "1 Tf" with a 12x text matrix: drawn at 12 pt, moved 2 units = 24 pt.
        List<TextFragment> f = Fragments("BT /F1 1 Tf 12 0 0 12 50 300 Tm (Hello) Tj 2 0 Td (lo) Tj ET");

        f[0].FontSize.Should().Be(12);
        f[0].X.Should().Be(50);
        f[0].Width.Should().BeApproximately(5 * 6, 1e-9);
        f[1].X.Should().BeApproximately(50 + 24, 1e-9);
    }

    [Fact]
    public void TextScaledThroughTm_KeepsItsWords()
    {
        // Each word one Tj, a real space between them, all in a 12x text matrix.
        string text = Extract("BT /F1 1 Tf 12 0 0 12 50 300 Tm (Hello) Tj 3 0 Td (world) Tj ET", ExtractionStrategy.Operator);

        text.Should().Be("Hello world");
    }

    [Theory]
    [InlineData(ExtractionStrategy.Operator)]
    [InlineData(ExtractionStrategy.Layout)]
    public void OneCmPerLine_KeepsTheLineBreaks(ExtractionStrategy strategy)
    {
        // Each line drawn under its own cm, the way some certificate PDFs are.
        string content =
            "q 1 0 0 1 50 700 cm BT /F1 12 Tf 0 0 Td (First line) Tj ET Q\n" +
            "q 1 0 0 1 50 680 cm BT /F1 12 Tf 0 0 Td (Second line) Tj ET Q\n" +
            "q 1 0 0 1 50 660 cm BT /F1 12 Tf 0 0 Td (Third line) Tj ET Q\n";

        Extract(content, strategy).Replace("\r\n", "\n", System.StringComparison.Ordinal)
            .Should().Be("First line\nSecond line\nThird line");
    }

    [Fact]
    public void FlippedCtm_GivesPagePositions()
    {
        // A top-down coordinate system (y grows downwards) with text matrices
        // that flip back: the fragment's Y is the page's Y.
        List<TextFragment> f = Fragments("1 0 0 -1 0 400 cm BT /F1 12 Tf 1 0 0 -1 50 100 Tm (Top) Tj ET");

        f[0].Y.Should().Be(300);
        f[0].FontSize.Should().Be(12);
    }

    [Fact]
    public void FormText_IsPlacedByTheFormMatrixAndTheCtm()
    {
        List<TextFragment> f = Fragments(
            "q 2 0 0 2 100 50 cm /Fm1 Do Q",
            form: "BT /F1 6 Tf 10 20 Td (In form) Tj ET",
            formMatrix: "1 0 0 1 5 5");

        f[0].X.Should().Be(100 + (2 * (10 + 5)));
        f[0].Y.Should().Be(50 + (2 * (20 + 5)));
        f[0].FontSize.Should().Be(12);
    }

    [Fact]
    public void TjNumbers_AreScaledWithTheText()
    {
        // -1000 in a TJ array moves one text-space em: 12 pt at 1 Tf under a 12x matrix.
        List<TextFragment> f = Fragments("BT /F1 1 Tf 12 0 0 12 50 300 Tm [(A) -1000 (B)] TJ (C) Tj ET");

        f[1].X.Should().BeApproximately(50 + 6 + 12 + 6, 1e-9);
    }

    [Theory]
    [InlineData(ExtractionStrategy.Operator)]
    [InlineData(ExtractionStrategy.Layout)]
    public void ASuperscriptTouchingAWord_StaysASeparateToken(ExtractionStrategy strategy)
    {
        // "Cionini" then a raised 7 pt "a", then ", Gardani" back on the baseline.
        // The 3.28 behaviour, kept by SuperscriptMode.Separate since 3.29.
        string content =
            "BT /F1 12 Tf 50 300 Td (Cionini) Tj ET\n" +
            "BT /F1 7 Tf 92 305 Td (a) Tj ET\n" +
            "BT /F1 12 Tf 95.5 300 Td (, Gardani) Tj ET\n";

        Extract(content, strategy, SuperscriptMode.Separate).Should().Be("Cionini a, Gardani");
    }

    [Theory]
    [InlineData(ExtractionStrategy.Operator)]
    [InlineData(ExtractionStrategy.Layout)]
    public void ASubscriptTouchingAWord_StaysASeparateToken(ExtractionStrategy strategy)
    {
        string content =
            "BT /F1 12 Tf 50 300 Td (CO) Tj ET\n" +
            "BT /F1 7 Tf 62 296 Td (2) Tj ET\n";

        Extract(content, strategy, SuperscriptMode.Separate).Should().Be("CO 2");
    }

    [Theory]
    [InlineData(ExtractionStrategy.Operator)]
    [InlineData(ExtractionStrategy.Layout)]
    public void TextRotated90Degrees_IsReadAsOneLineOfItsOwn(ExtractionStrategy strategy)
    {
        // A caption running up the page in two pieces, beside a normal line.
        string content =
            "BT /F1 12 Tf 50 700 Td (Heading) Tj ET\n" +
            "BT /F1 6 Tf 0 1 -1 0 300 100 Tm (Courtesy of the) Tj ET\n" +
            "BT /F1 6 Tf 0 1 -1 0 300 150 Tm (author) Tj ET\n";

        string text = Extract(content, strategy).Replace("\r\n", "\n", System.StringComparison.Ordinal);

        text.Split('\n').Should().BeEquivalentTo(new[] { "Heading", "Courtesy of the author" });
    }

    [Fact]
    public void RotatedFragments_CarryTheirBaselineAngle()
    {
        List<TextFragment> f = Fragments("BT /F1 6 Tf 0 1 -1 0 300 100 Tm (Up) Tj ET BT /F1 6 Tf 1 0 0 1 50 50 Tm (Flat) Tj ET");

        f[0].Angle.Should().BeApproximately(90, 1e-9);
        f[1].Angle.Should().Be(0);
    }

    private static List<TextFragment> Fragments(string content, string? form = null, string? formMatrix = null)
    {
        using PdfDocument doc = Open(content, form, formMatrix);
        return new TextExtractor(doc.Objects).ExtractFragments(doc.Pages[0]);
    }

    private static string Extract(string content, ExtractionStrategy strategy, SuperscriptMode superscripts = SuperscriptMode.Unicode)
    {
        using PdfDocument doc = Open(content, null, null);
        return new TextExtractor(doc.Objects, strategy) { Superscripts = superscripts }.ExtractText(doc.Pages[0]).Trim();
    }

    private static PdfDocument Open(string content, string? form, string? formMatrix)
    {
        string font = "<</Type/Font/Subtype/Type1/BaseFont/Helvetica/FirstChar 32/LastChar 126/Widths[" + string.Concat(Enumerable.Repeat("500 ", 95)) + "]>>";
        string xobject = form is null ? string.Empty : "/XObject<</Fm1 6 0 R>>";
        List<string> bodies = new List<string>
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 400 800]/Resources<</Font<</F1 5 0 R>>" + xobject + ">>/Contents 4 0 R>>",
            "<</Length " + Encoding.Latin1.GetByteCount(content).ToString(CultureInfo.InvariantCulture) + ">>\nstream\n" + content + "\nendstream",
            font,
        };
        if (form is not null)
        {
            bodies.Add("<</Type/XObject/Subtype/Form/BBox[0 0 400 400]/Matrix[" + formMatrix + "]/Resources<</Font<</F1 5 0 R>>>>/Length "
                + Encoding.Latin1.GetByteCount(form).ToString(CultureInfo.InvariantCulture) + ">>\nstream\n" + form + "\nendstream");
        }

        StringBuilder sb = new StringBuilder("%PDF-1.7\n");
        List<int> offsets = new List<int>();
        for (int i = 0; i < bodies.Count; i++)
        {
            offsets.Add(sb.Length);
            sb.Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append(" 0 obj\n").Append(bodies[i]).Append("\nendobj\n");
        }

        int xref = sb.Length;
        sb.Append("xref\n0 ").Append((bodies.Count + 1).ToString(CultureInfo.InvariantCulture)).Append("\n0000000000 65535 f \n");
        foreach (int off in offsets)
        {
            sb.Append(off.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        sb.Append("trailer\n<</Size ").Append((bodies.Count + 1).ToString(CultureInfo.InvariantCulture))
          .Append("/Root 1 0 R>>\nstartxref\n").Append(xref.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF\n");
        return PdfDocument.Open(new MemoryStream(Encoding.Latin1.GetBytes(sb.ToString())));
    }
}
