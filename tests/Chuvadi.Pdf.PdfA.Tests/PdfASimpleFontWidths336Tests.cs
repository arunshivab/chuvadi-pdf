// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ISO 19005-2 §6.2.11.5 (glyph width consistency)
// PHASE: 3.36.0 (D-410 D, PDA-01 part 3, decided D-417(2)=a) — a simple
//        embedded TrueType font whose /Widths disagree with its program's
//        glyph advances for a glyph it RENDERS is a PDF/A violation. Only
//        rendered glyphs count (matching veraPDF): a wrong width for a glyph
//        the content never shows, or a glyph drawn in the invisible text
//        rendering mode, is not a conformance failure.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Fonts;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Fonts.Rendering;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.PdfA.Tests;

public sealed class PdfASimpleFontWidths336Tests
{
    // 'A' — a Latin glyph the bundled LiPi Sans face maps through its Unicode cmap.
    private const int RenderedCode = 0x41;

    [Fact]
    public void RenderedGlyphWithWrongWidth_IsReportedAsViolation()
    {
        byte[] program = LatinProgram();
        byte[] input = SimpleTrueTypeDoc(program, declaredWidth: 1, renderGlyph: true);

        PdfAResult result = Convert(input);

        result.Violations.Should().Contain(v => v.Contains("simple TrueType", StringComparison.Ordinal));
    }

    [Fact]
    public void RenderedGlyphWithCorrectWidth_IsNotReported()
    {
        byte[] program = LatinProgram();
        byte[] input = SimpleTrueTypeDoc(program, declaredWidth: ExpectedWidth(program, RenderedCode), renderGlyph: true);

        PdfAResult result = Convert(input);

        result.Violations.Should().NotContain(v => v.Contains("simple TrueType", StringComparison.Ordinal));
    }

    [Fact]
    public void WrongWidthButGlyphNotRendered_IsNotReported()
    {
        // Same wrong width, but the glyph is drawn in text rendering mode 3
        // (invisible), so it is not a rendered glyph and must not be flagged.
        byte[] program = LatinProgram();
        byte[] input = SimpleTrueTypeDoc(program, declaredWidth: 1, renderGlyph: false);

        PdfAResult result = Convert(input);

        result.Violations.Should().NotContain(v => v.Contains("simple TrueType", StringComparison.Ordinal));
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static byte[] LatinProgram() => FontPack.ReadAllBytes("LiPi-Sans-Latin-Regular.ttf");

    private static int ExpectedWidth(byte[] program, int code)
    {
        TrueTypeLoader loader = new TrueTypeLoader(program);
        int gid = loader.GetGlyphIndexFromCmap(code, symbolic: true);
        return (int)Math.Round(loader.GetGlyphMetrics(gid).AdvanceWidth * 1000.0 / loader.UnitsPerEm);
    }

    private static PdfAResult Convert(byte[] input)
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(input), leaveOpen: false);
        using MemoryStream output = new MemoryStream();
        return PdfAWriter.Write(output, doc, new PdfAOptions { Conformance = PdfAConformance.PdfA2B });
    }

    private static byte[] SimpleTrueTypeDoc(byte[] program, int declaredWidth, bool renderGlyph)
    {
        string show = renderGlyph
            ? "BT /F1 12 Tf 72 700 Td (A) Tj ET"
            : "BT /F1 12 Tf 3 Tr 72 700 Td (A) Tj ET";
        string widthText = declaredWidth.ToString(CultureInfo.InvariantCulture);

        using MemoryStream ms = new MemoryStream();
        List<long> offsets = new List<long>();
        ms.Write(Latin1("%PDF-1.7\n"));

        WriteObject(ms, offsets, 1, Latin1("<</Type/Catalog/Pages 2 0 R>>"));
        WriteObject(ms, offsets, 2, Latin1("<</Type/Pages/Kids[3 0 R]/Count 1>>"));
        WriteObject(ms, offsets, 3, Latin1(
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]"
            + "/Resources<</Font<</F1 5 0 R>>>>/Contents 4 0 R>>"));
        WriteObject(ms, offsets, 4, StreamBody(Latin1(show)));
        // Symbolic (Flags 4) with no /Encoding: the width check covers symbolic
        // TrueType fonts, whose glyphs come straight from the font's cmap.
        WriteObject(ms, offsets, 5, Latin1(
            "<</Type/Font/Subtype/TrueType/BaseFont/AAAAAA+LiPiSans"
            + "/FirstChar 65/LastChar 65/Widths[" + widthText + "]"
            + "/FontDescriptor 6 0 R>>"));
        WriteObject(ms, offsets, 6, Latin1(
            "<</Type/FontDescriptor/FontName/AAAAAA+LiPiSans/Flags 4"
            + "/FontBBox[0 0 1000 1000]/ItalicAngle 0/Ascent 800/Descent -200"
            + "/CapHeight 700/StemV 80/FontFile2 7 0 R>>"));
        WriteObject(ms, offsets, 7, FontFileBody(program));

        long xref = ms.Length;
        ms.Write(Latin1("xref\n0 8\n0000000000 65535 f \n"));
        foreach (long off in offsets)
        {
            ms.Write(Latin1(off.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n"));
        }

        ms.Write(Latin1(
            "trailer <</Size 8/Root 1 0 R>>\nstartxref\n"
            + xref.ToString(CultureInfo.InvariantCulture) + "\n%%EOF"));
        return ms.ToArray();
    }

    private static void WriteObject(MemoryStream ms, List<long> offsets, int number, byte[] body)
    {
        offsets.Add(ms.Length);
        ms.Write(Latin1(number.ToString(CultureInfo.InvariantCulture) + " 0 obj\n"));
        ms.Write(body);
        ms.Write(Latin1("\nendobj\n"));
    }

    private static byte[] StreamBody(byte[] content)
    {
        using MemoryStream ms = new MemoryStream();
        ms.Write(Latin1("<</Length " + content.Length.ToString(CultureInfo.InvariantCulture) + ">>\nstream\n"));
        ms.Write(content);
        ms.Write(Latin1("\nendstream"));
        return ms.ToArray();
    }

    private static byte[] FontFileBody(byte[] program)
    {
        string length = program.Length.ToString(CultureInfo.InvariantCulture);
        using MemoryStream ms = new MemoryStream();
        ms.Write(Latin1("<</Length " + length + "/Length1 " + length + ">>\nstream\n"));
        ms.Write(program);
        ms.Write(Latin1("\nendstream"));
        return ms.ToArray();
    }

    private static byte[] Latin1(string text) => Encoding.Latin1.GetBytes(text);
}
