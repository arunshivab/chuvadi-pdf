// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.7.3.4 (inheritance of page attributes)
// PHASE: 3.34.0 — pages copied by merge, split and assemble keep the
//        /Resources, /MediaBox and /CropBox they inherit from their page-tree
//        nodes. Found by the 3.33.0-vs-3.34.0 before/after run on the test
//        bench's Govdocs1 files (D-369); the PDFs are built here.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Operations;
using Chuvadi.Pdf.Text;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Integration.Tests;

public sealed class MergeInheritance3340Tests
{
    [Fact]
    public void Merge_KeepsInheritedResourcesAndBoxes()
    {
        using PdfDocument source = TestPdf.Open(InheritingPdf());

        using MemoryStream merged = new MemoryStream();
        PageOperations.Merge(merged, source);
        using PdfDocument back = PdfDocument.Open(new MemoryStream(merged.ToArray()));

        back.Pages[0].MediaBox.Width.Should().Be(300);
        back.Pages[0].MediaBox.Height.Should().Be(200);
        back.Pages[0].CropBox.Width.Should().Be(250);
        new TextExtractor(back.Objects).ExtractText(back.Pages[0]).Should().Contain("Inherited");
        back.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void SplitPages_KeepsInheritedResourcesAndBoxes()
    {
        using PdfDocument source = TestPdf.Open(InheritingPdf());

        List<MemoryStream> parts = PageOperations.SplitPages(source);
        using PdfDocument back = PdfDocument.Open(new MemoryStream(parts[0].ToArray()));

        back.Pages[0].MediaBox.Width.Should().Be(300);
        new TextExtractor(back.Objects).ExtractText(back.Pages[0]).Should().Contain("Inherited");
    }

    // A page whose /Resources, /MediaBox and /CropBox sit on its /Pages node.
    private static byte[] InheritingPdf()
    {
        TestPdf pdf = new TestPdf();
        int catalog = pdf.Reserve();
        int pages = pdf.Reserve();
        int font = pdf.Add("<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>");
        int contents = pdf.AddStream("<<>>", "BT /F1 18 Tf 20 100 Td (Inherited) Tj ET");
        int page = pdf.Add("<</Type/Page/Parent " + N(pages) + " 0 R/Contents " + N(contents) + " 0 R>>");
        pdf.Set(pages, "<</Type/Pages/Kids[" + N(page) + " 0 R]/Count 1/MediaBox[0 0 300 200]/CropBox[0 0 250 200]/Resources<</Font<</F1 "
            + N(font) + " 0 R>>>>>>");
        pdf.Set(catalog, "<</Type/Catalog/Pages " + N(pages) + " 0 R>>");
        return pdf.Build(catalog);
    }

    private static string N(int n) => n.ToString(CultureInfo.InvariantCulture);
}
