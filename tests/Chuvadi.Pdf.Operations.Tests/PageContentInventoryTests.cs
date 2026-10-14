// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// Tests for PDF-08: does a page paint anything?

using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Rendering.DisplayList;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

public sealed class PageContentInventoryTests
{
    private const string Box = "/MediaBox[0 0 200 200]";
    private const string FontRes = "/Resources<</Font<</F1 5 0 R>>>>";

    [Fact]
    public void EmptyContent_IsNotDrawable()
    {
        Inventory(Box, string.Empty).HasDrawableContent.Should().BeFalse();
    }

    [Fact]
    public void ClipOnly_IsNotDrawable()
    {
        Inventory(Box, "q 0 0 100 100 re W n Q").HasDrawableContent.Should().BeFalse();
    }

    [Fact]
    public void FilledRectangle_IsDrawable()
    {
        PageContentInventory inv = Inventory(Box, "0 0 1 rg 10 10 50 50 re f");
        inv.HasDrawableContent.Should().BeTrue();
        inv.Paths.Should().Be(1);
        inv.HasVisibleText.Should().BeFalse();
    }

    [Fact]
    public void VisibleText_IsCounted_InvisibleTextIsNot()
    {
        PageContentInventory visible = Inventory(
            Box + FontRes, "BT /F1 12 Tf 20 100 Td (Hello) Tj ET", Helvetica());
        visible.HasVisibleText.Should().BeTrue();
        visible.TextRuns.Should().Be(1);

        PageContentInventory hidden = Inventory(
            Box + FontRes, "BT 3 Tr /F1 12 Tf 20 100 Td (OCR layer) Tj ET", Helvetica());
        hidden.HasDrawableContent.Should().BeFalse("render mode 3 paints nothing (e.g. an OCR text layer)");
    }

    [Fact]
    public void ContentInsideFormXObject_IsCounted()
    {
        byte[] form = RawPdf.Stream("/Type/XObject/Subtype/Form/BBox[0 0 100 100]", "1 0 0 rg 0 0 40 40 re f");
        PageContentInventory inv = Inventory(
            Box + "/Resources<</XObject<</Fm 5 0 R>>>>", "q /Fm Do Q", form);
        inv.Paths.Should().Be(1);
    }

    [Fact]
    public void FullyTransparentFill_IsNotDrawable()
    {
        byte[] gs = RawPdf.Obj("<</Type/ExtGState/ca 0>>");
        Inventory(Box + "/Resources<</ExtGState<</G0 5 0 R>>>>", "/G0 gs 0 0 1 rg 10 10 50 50 re f", gs)
            .HasDrawableContent.Should().BeFalse();
    }

    [Fact]
    public void ExtensionMethod_MatchesInventory()
    {
        using PdfDocument doc = Open(RawPdf.SinglePage(Box, "0 0 0 RG 0 0 m 100 100 l S"));
        doc.HasDrawableContent(0).Should().BeTrue();
        doc.GetContentInventory(0).Paths.Should().Be(1);
    }

    private static byte[] Helvetica() => RawPdf.Obj("<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>");

    private static PageContentInventory Inventory(string pageEntries, string content, params byte[][] extra)
    {
        using PdfDocument doc = Open(RawPdf.SinglePage(pageEntries, content, extra));
        return PageContentInventory.Inspect(doc, 0);
    }

    private static PdfDocument Open(byte[] bytes) => PdfDocument.Open(new MemoryStream(bytes), leaveOpen: false);
}
