// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.27 — synthetic table-of-contents lines (D-72). Word processors draw
//        "Label<tab>....... 3" with the leader starting exactly where the
//        label ends; the tab is not drawn, so there is no gap to read a space
//        from. Each case runs with the font's /Widths (measured advances) and
//        without them (the 0.6 em estimate), through both extractors.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Text.Tests;

public sealed class TocLeaderExtractionTests
{
    // Every character 500/1000 em wide when /Widths is present: 6 pt at 12 pt.
    private const double Measured = 6.0;

    // The parser's estimate without widths: 0.6 em, 7.2 pt at 12 pt.
    private const double Estimated = 7.2;

    [Theory]
    [InlineData(true, ExtractionStrategy.Operator)]
    [InlineData(true, ExtractionStrategy.Layout)]
    [InlineData(false, ExtractionStrategy.Operator)]
    [InlineData(false, ExtractionStrategy.Layout)]
    public void LeaderTouchingTheLabel_IsSeparated(bool widths, ExtractionStrategy strategy)
    {
        double w = widths ? Measured : Estimated;
        string content = Line(("ROOMS", 50), ("........", 50 + (5 * w)), ("3", 50 + (13 * w) + 12));

        Extract(content, widths, strategy).Should().Be("ROOMS ........ 3");
    }

    [Theory]
    [InlineData(true, ExtractionStrategy.Operator)]
    [InlineData(true, ExtractionStrategy.Layout)]
    [InlineData(false, ExtractionStrategy.Operator)]
    [InlineData(false, ExtractionStrategy.Layout)]
    public void PageNumberTouchingTheLeader_IsSeparated(bool widths, ExtractionStrategy strategy)
    {
        double w = widths ? Measured : Estimated;
        string content = Line(("ROOMS", 50), ("........", 50 + (5 * w)), ("12", 50 + (13 * w)));

        Extract(content, widths, strategy).Should().Be("ROOMS ........ 12");
    }

    [Theory]
    [InlineData(true, ExtractionStrategy.Operator)]
    [InlineData(true, ExtractionStrategy.Layout)]
    [InlineData(false, ExtractionStrategy.Operator)]
    [InlineData(false, ExtractionStrategy.Layout)]
    public void LabelEndingInASpace_GetsNoSecondSpace(bool widths, ExtractionStrategy strategy)
    {
        double w = widths ? Measured : Estimated;
        string content = Line(("ROOMS ", 50), ("........", 50 + (6 * w)), (" 3", 50 + (14 * w)));

        Extract(content, widths, strategy).Should().Be("ROOMS ........ 3");
    }

    [Theory]
    [InlineData(true, ExtractionStrategy.Operator)]
    [InlineData(true, ExtractionStrategy.Layout)]
    [InlineData(false, ExtractionStrategy.Operator)]
    [InlineData(false, ExtractionStrategy.Layout)]
    public void AnEllipsisTouchingAWord_IsNotALeader(bool widths, ExtractionStrategy strategy)
    {
        double w = widths ? Measured : Estimated;
        string content = Line(("Wait", 50), ("...", 50 + (4 * w)));

        Extract(content, widths, strategy).Should().Be("Wait...");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ALeaderInsideOneString_IsLeftAsDrawn(bool widths)
    {
        string content = Line(("ROOMS........3", 50));

        Extract(content, widths, ExtractionStrategy.Operator).Should().Be("ROOMS........3");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnUnderscoreFillLine_IsSeparated(bool widths)
    {
        double w = widths ? Measured : Estimated;
        string content = Line(("Name:", 50), ("__________", 50 + (5 * w)));

        Extract(content, widths, ExtractionStrategy.Operator).Should().Be("Name: __________");
    }

    // One text object per fragment, each placed with its own Tm, the way
    // word processors write table-of-contents lines.
    private static string Line(params (string Text, double X)[] parts)
    {
        StringBuilder sb = new StringBuilder();
        foreach ((string text, double x) in parts)
        {
            sb.Append("BT /F1 12 Tf 1 0 0 1 ")
              .Append(x.ToString("0.###", CultureInfo.InvariantCulture))
              .Append(" 300 Tm (").Append(text).Append(") Tj ET\n");
        }

        return sb.ToString();
    }

    private static string Extract(string content, bool widths, ExtractionStrategy strategy)
    {
        using PdfDocument doc = Open(content, widths);
        return new TextExtractor(doc.Objects, strategy).ExtractText(doc.Pages[0]).Trim();
    }

    private static PdfDocument Open(string content, bool widths)
    {
        string font = widths
            ? "<</Type/Font/Subtype/Type1/BaseFont/Helvetica/FirstChar 32/LastChar 126/Widths[" + Repeat("500 ", 95) + "]>>"
            : "<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>";
        List<string> bodies = new List<string>
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 400 400]/Resources<</Font<</F1 5 0 R>>>>/Contents 4 0 R>>",
            "<</Length " + Encoding.Latin1.GetByteCount(content).ToString(CultureInfo.InvariantCulture) + ">>\nstream\n" + content + "\nendstream",
            font,
        };

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

    private static string Repeat(string s, int n)
    {
        StringBuilder sb = new StringBuilder(s.Length * n);
        for (int i = 0; i < n; i++)
        {
            sb.Append(s);
        }

        return sb.ToString();
    }
}
