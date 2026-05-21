// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: v2.0.0 R2 — SVG renderer tests

using System;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using Xunit;

namespace Chuvadi.Pdf.Svg.Tests;

/// <summary>
/// Structural tests for <see cref="SvgRenderer"/>. Each test authors a
/// tiny PDF in memory using <see cref="Chuvadi.Pdf.Authoring"/>, opens it
/// back, renders to SVG, and asserts on properties of the resulting
/// markup. No external fixture files; the tests are self-contained.
/// </summary>
public class SvgRendererTests
{
    // ── Helpers ───────────────────────────────────────────────────────────

    private static PdfDocument BuildAndOpen(Action<PdfDocumentBuilder> setup)
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        setup(builder);
        byte[] bytes = builder.ToByteArray();
        MemoryStream ms = new MemoryStream(bytes);
        return PdfDocument.Open(ms, leaveOpen: false);
    }

    private static int CountSubstrings(string source, string needle)
    {
        int count = 0;
        int idx = 0;

        while ((idx = source.IndexOf(needle, idx, StringComparison.Ordinal)) != -1)
        {
            count++;
            idx += needle.Length;
        }

        return count;
    }

    // ── Root structure ────────────────────────────────────────────────────

    [Fact]
    public void RenderPage_EmptyA4_ProducesValidSvgRoot()
    {
        using PdfDocument doc = BuildAndOpen(b => b.AddPage(PageSize.A4));
        string svg = new SvgRenderer().RenderPage(doc, 0);

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-8\"?>", svg);
        Assert.Contains("<svg ", svg, StringComparison.Ordinal);
        Assert.Contains("xmlns=\"http://www.w3.org/2000/svg\"", svg);
        Assert.Contains("viewBox=\"0 0 595 842\"", svg);
        Assert.Contains("width=\"595\"", svg);
        Assert.Contains("height=\"842\"", svg);
        Assert.EndsWith("</svg>", svg.TrimEnd());
    }

    [Fact]
    public void RenderPage_AppliesPageLevelYFlip()
    {
        using PdfDocument doc = BuildAndOpen(b => b.AddPage(PageSize.A4));
        string svg = new SvgRenderer().RenderPage(doc, 0);

        // matrix(1 0 0 -1 0 pageHeight) flips PDF Y-up to SVG Y-down.
        Assert.Contains("transform=\"matrix(1 0 0 -1 0 842)\"", svg);
    }

    // ── Drawing ───────────────────────────────────────────────────────────

    [Fact]
    public void RenderPage_FilledRedRectangle_EmitsHexFill()
    {
        using PdfDocument doc = BuildAndOpen(b =>
            b.AddPage(PageSize.A4).DrawRectangle(50, 100, 200, 100, fill: Colors.Red));

        string svg = new SvgRenderer().RenderPage(doc, 0);

        Assert.Contains("fill=\"#ff0000\"", svg);
        // Pure black isn't pure black — the rectangle path begins with "M".
        Assert.Contains("<path ", svg);
        Assert.Contains("d=\"M", svg);
    }

    [Fact]
    public void RenderPage_BlackText_EmitsBlackFill()
    {
        using PdfDocument doc = BuildAndOpen(b =>
            b.AddPage(PageSize.A4)
                .DrawText("Hi", 50, 50, StandardFonts.Helvetica, 18, Colors.Black));

        string svg = new SvgRenderer().RenderPage(doc, 0);

        // Black should be emitted as the named SVG colour, not "#000000".
        Assert.Contains("fill=\"black\"", svg);
        // Two distinct glyphs ⇒ at least two <path> elements.
        Assert.True(CountSubstrings(svg, "<path ") >= 2);
    }

    [Fact]
    public void RenderPage_RepeatedGlyphs_DedupsViaDefsAndUse()
    {
        // "HHHHH" — one distinct glyph repeated five times.
        using PdfDocument doc = BuildAndOpen(b =>
            b.AddPage(PageSize.A4)
                .DrawText("HHHHH", 50, 100, StandardFonts.Helvetica, 18, Colors.Black));

        string svg = new SvgRenderer().RenderPage(doc, 0);

        Assert.Contains("<defs>", svg);
        // First repeated glyph is allocated id "g0" (clip ids come from the
        // same counter; with no clips in this fixture, g0 is guaranteed).
        Assert.Contains("id=\"g0\"", svg);
        // Four <use> elements (five glyphs, one defs entry + four references).
        Assert.True(
            CountSubstrings(svg, "<use ") >= 4,
            $"Expected ≥4 <use> elements for 5 repeated H's, got {CountSubstrings(svg, "<use ")}");
    }

    // ── Determinism ───────────────────────────────────────────────────────

    [Fact]
    public void RenderPage_TwoIdenticalCalls_ProduceIdenticalOutput()
    {
        using PdfDocument doc = BuildAndOpen(b =>
            b.AddPage(PageSize.A4)
                .DrawText("Hello World", 50, 100, StandardFonts.Helvetica, 12, Colors.Black)
                .DrawRectangle(50, 200, 100, 50, fill: Colors.Blue));

        string a = new SvgRenderer().RenderPage(doc, 0);
        string b = new SvgRenderer().RenderPage(doc, 0);

        Assert.Equal(a, b);
    }

    [Fact]
    public void RenderPage_StreamOverload_MatchesStringOverload()
    {
        using PdfDocument doc = BuildAndOpen(b =>
            b.AddPage(PageSize.A4)
                .DrawText("Test", 50, 50, StandardFonts.Helvetica, 12, Colors.Black));

        string asString = new SvgRenderer().RenderPage(doc, 0);

        using MemoryStream ms = new MemoryStream();
        new SvgRenderer().RenderPage(doc, 0, ms);
        string asStream = Encoding.UTF8.GetString(ms.ToArray());

        Assert.Equal(asString, asStream);
    }

    // ── Thumbnail ─────────────────────────────────────────────────────────

    [Fact]
    public void RenderThumbnail_ScalesWidthAndHeightButNotViewBox()
    {
        using PdfDocument doc = BuildAndOpen(b => b.AddPage(PageSize.A4));

        string svg = new SvgRenderer().RenderThumbnail(doc, 0, 200);

        // Longer side (height = 842) maps to 200; shorter scales proportionally.
        Assert.Contains("height=\"200\"", svg);
        // viewBox always carries unscaled PDF user-space units.
        Assert.Contains("viewBox=\"0 0 595 842\"", svg);
    }

    // ── Argument validation ──────────────────────────────────────────────

    [Fact]
    public void RenderPage_NullDocument_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new SvgRenderer().RenderPage(null!, 0));
    }

    [Fact]
    public void RenderPage_NegativePageIndex_Throws()
    {
        using PdfDocument doc = BuildAndOpen(b => b.AddPage(PageSize.A4));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SvgRenderer().RenderPage(doc, -1));
    }

    [Fact]
    public void RenderPage_PageIndexBeyondCount_Throws()
    {
        using PdfDocument doc = BuildAndOpen(b => b.AddPage(PageSize.A4));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SvgRenderer().RenderPage(doc, 5));
    }

    [Fact]
    public void Constructor_WithWoff2Embedding_Throws()
    {
        SvgRenderOptions options = new SvgRenderOptions
        {
            FontEmbedding = FontEmbedding.Woff2DataUri,
        };

        Assert.Throws<NotSupportedException>(() => new SvgRenderer(options));
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new SvgRenderer(null!));
    }

    [Fact]
    public void DecimalPrecision_NegativeValue_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SvgRenderOptions { DecimalPrecision = -1 });
    }

    [Fact]
    public void DecimalPrecision_TooLarge_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SvgRenderOptions { DecimalPrecision = 13 });
    }

    [Fact]
    public void RenderThumbnail_ZeroDimension_Throws()
    {
        using PdfDocument doc = BuildAndOpen(b => b.AddPage(PageSize.A4));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SvgRenderer().RenderThumbnail(doc, 0, 0));
    }
}
