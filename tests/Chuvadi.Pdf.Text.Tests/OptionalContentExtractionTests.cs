// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.11 — Optional content
// PHASE: 3.21 (PDF-14) — text extraction leaves out layers hidden by the
//        default configuration (as Acrobat does), with an all-layers option.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Text.Tests;

public sealed class OptionalContentExtractionTests
{
    private const string Content =
        "/OC /L1 BDC BT /F1 12 Tf 50 350 Td (SHOWNTEXT) Tj ET EMC "
        + "/OC /L2 BDC BT /F1 12 Tf 50 300 Td (HIDDENTEXT) Tj ET /Fm0 Do EMC "
        + "BT /F1 12 Tf 50 250 Td (PLAINTEXT) Tj ET";

    [Fact]
    public void DefaultExtraction_LeavesOutTheHiddenLayer()
    {
        using PdfDocument doc = Open();

        string text = new TextExtractor(doc.Objects).ExtractText(doc.Pages[0]);

        text.Should().Contain("SHOWNTEXT").And.Contain("PLAINTEXT");
        text.Should().NotContain("HIDDENTEXT").And.NotContain("FORMTEXT").And.NotContain("ANNOTTEXT");
    }

    [Fact]
    public void AllVisible_ExtractsEveryLayer()
    {
        using PdfDocument doc = Open();

        string text = new TextExtractor(doc.Objects) { OptionalContent = OptionalContentVisibility.AllVisible }
            .ExtractText(doc.Pages[0]);

        text.Should().Contain("HIDDENTEXT").And.Contain("FORMTEXT").And.Contain("ANNOTTEXT");
    }

    [Fact]
    public void Overrides_ShowTheLayerByName()
    {
        using PdfDocument doc = Open();
        OptionalContentVisibility visibility = OptionalContentVisibility.FromDocument(
            doc, new Dictionary<string, bool> { ["Secret"] = true });

        string text = new TextExtractor(doc.Objects) { OptionalContent = visibility }.ExtractText(doc.Pages[0]);

        text.Should().Contain("HIDDENTEXT");
    }

    private static PdfDocument Open()
    {
        List<string> bodies = new List<string>
        {
            "<</Type/Catalog/Pages 2 0 R/OCProperties<</OCGs[5 0 R 6 0 R]/D<</OFF[6 0 R]>>>>>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 400 400]/Resources<</Font<</F1 7 0 R>>"
                + "/Properties<</L1 5 0 R/L2 6 0 R>>/XObject<</Fm0 8 0 R>>>>/Annots[9 0 R]/Contents 4 0 R>>",
            Stream(string.Empty, Content),
            "<</Type/OCG/Name(Shown)>>",
            "<</Type/OCG/Name(Secret)>>",
            "<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>",
            Stream("/Type/XObject/Subtype/Form/BBox[0 0 400 400]/Resources<</Font<</F1 7 0 R>>>>",
                "BT /F1 12 Tf 50 200 Td (FORMTEXT) Tj ET"),
            "<</Type/Annot/Subtype/FreeText/Rect[50 100 250 130]/OC 6 0 R/AP<</N 10 0 R>>>>",
            Stream("/Type/XObject/Subtype/Form/BBox[0 0 200 30]/Resources<</Font<</F1 7 0 R>>>>",
                "BT /F1 12 Tf 5 10 Td (ANNOTTEXT) Tj ET"),
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

        sb.Append("trailer <</Size ").Append((bodies.Count + 1).ToString(CultureInfo.InvariantCulture))
            .Append("/Root 1 0 R>>\nstartxref\n").Append(xref.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF");
        return PdfDocument.Open(new MemoryStream(Encoding.Latin1.GetBytes(sb.ToString())), leaveOpen: false);
    }

    private static string Stream(string entries, string content) =>
        "<<" + entries + "/Length " + Encoding.Latin1.GetByteCount(content).ToString(CultureInfo.InvariantCulture)
        + ">>\nstream\n" + content + "\nendstream";
}
