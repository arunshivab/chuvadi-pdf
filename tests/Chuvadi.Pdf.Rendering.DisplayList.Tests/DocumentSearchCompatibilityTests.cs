// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.29 — search matches Unicode compatibility forms (D-104): "H2O"
//        finds "H₂O", "x2" finds "x²", "fi" finds the "ﬁ" ligature.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Chuvadi.Pdf.Documents;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.DisplayList.Tests;

public sealed class DocumentSearchCompatibilityTests
{
    // Byte 0x7E shows "ﬁ" and 0x7F shows "₂" (by the font's ToUnicode map);
    // 0xB2 is "²" in WinAnsiEncoding.
    private const string Content =
        "BT /F1 12 Tf 50 300 Td (Water is H\\177O) Tj ET\n" +
        "BT /F1 12 Tf 50 280 Td (Area x\\262) Tj ET\n" +
        "BT /F1 12 Tf 50 260 Td (A \\176eld test) Tj ET\n";

    [Theory]
    [InlineData("H2O", "H₂O")]
    [InlineData("x2", "x²")]
    [InlineData("field", "ﬁeld")]
    [InlineData("h2o", "H₂O")]
    public async Task APlainQuery_FindsTheCompatibilityForm(string query, string found)
    {
        System.ArgumentNullException.ThrowIfNull(found);
        List<SearchMatch> matches = await Search(query, new SearchOptions());

        matches.Should().ContainSingle();
        matches[0].Length.Should().Be(found.Length);
    }

    [Fact]
    public async Task AQueryInCompatibilityForm_FindsItself()
    {
        List<SearchMatch> matches = await Search("H₂O", new SearchOptions());

        matches.Should().ContainSingle();
    }

    [Fact]
    public async Task MatchOffsets_StayInThePageText()
    {
        List<SearchMatch> matches = await Search("field", new SearchOptions());
        List<SearchMatch> exact = await Search("ﬁeld", new SearchOptions { MatchCompatibilityForms = false });

        matches[0].CharacterOffset.Should().Be(exact[0].CharacterOffset);
        matches[0].Length.Should().Be(4);
        matches[0].BoundingBoxes.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ExactMatching_CanStillBeAskedFor()
    {
        List<SearchMatch> matches = await Search("H2O", new SearchOptions { MatchCompatibilityForms = false });

        matches.Should().BeEmpty();
    }

    [Fact]
    public async Task WholeWord_IsJudgedOnThePageText()
    {
        List<SearchMatch> matches = await Search("field", new SearchOptions { WholeWord = true });

        matches.Should().ContainSingle();
    }

    private static async Task<List<SearchMatch>> Search(string query, SearchOptions options)
    {
        using PdfDocument doc = Open();
        List<SearchMatch> matches = new List<SearchMatch>();
        await foreach (SearchMatch m in DocumentSearch.SearchAsync(doc, query, options))
        {
            matches.Add(m);
        }

        return matches;
    }

    private static PdfDocument Open()
    {
        string cmap =
            "/CIDInit /ProcSet findresource begin 12 dict begin begincmap\n" +
            "/CMapName /Test def 1 begincodespacerange <00> <FF> endcodespacerange\n" +
            "2 beginbfchar <7E> <FB01> <7F> <2082> endbfchar\n" +
            "1 beginbfrange <20> <7D> <0020> endbfrange\n" +
            "1 beginbfchar <B2> <00B2> endbfchar\n" +
            "endcmap CMapName currentdict /CMap defineresource pop end end";
        List<string> bodies = new List<string>
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 400 400]/Resources<</Font<</F1 5 0 R>>>>/Contents 4 0 R>>",
            "<</Length " + Content.Length.ToString(CultureInfo.InvariantCulture) + ">>\nstream\n" + Content + "\nendstream",
            "<</Type/Font/Subtype/Type1/BaseFont/Helvetica/Encoding/WinAnsiEncoding/ToUnicode 6 0 R>>",
            "<</Length " + cmap.Length.ToString(CultureInfo.InvariantCulture) + ">>\nstream\n" + cmap + "\nendstream",
        };

        StringBuilder pdf = new StringBuilder("%PDF-1.7\n");
        List<int> offsets = new List<int>();
        for (int i = 0; i < bodies.Count; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append(" 0 obj\n").Append(bodies[i]).Append("\nendobj\n");
        }

        int xref = pdf.Length;
        pdf.Append("xref\n0 7\n0000000000 65535 f \n");
        foreach (int off in offsets)
        {
            pdf.Append(off.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        pdf.Append("trailer <</Size 7/Root 1 0 R>>\nstartxref\n").Append(xref.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF");
        return PdfDocument.Open(new MemoryStream(Encoding.Latin1.GetBytes(pdf.ToString())), leaveOpen: false);
    }
}
