// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.7.3.3 — MediaBox/CropBox; §7.7.3.4 — inheritance.
// Tests for the settable MediaBox/CropBox on a loaded page (LA-08).

using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Documents.Tests;

public sealed class PageBoxMutationTests
{
    [Fact]
    public void SetMediaBox_OverridesInheritedValue()
    {
        using MemoryStream stream = BuildPageWithInheritedMediaBox(0, 0, 612, 792);
        using PdfDocument doc = PdfDocument.Open(stream, leaveOpen: true);
        PdfPage page = doc.Pages[0];

        page.MediaBox.Width.Should().BeApproximately(612, 0.5, "inherited from /Pages");

        page.MediaBox = new PdfRectangle(10, 20, 210, 320);

        page.MediaBox.X1.Should().BeApproximately(10, 1e-6);
        page.MediaBox.Y1.Should().BeApproximately(20, 1e-6);
        page.MediaBox.Width.Should().BeApproximately(200, 1e-6);
        page.MediaBox.Height.Should().BeApproximately(300, 1e-6);
    }

    [Fact]
    public void SetMediaBox_WritesDirectEntryOnPageDictionary()
    {
        using MemoryStream stream = BuildPageWithInheritedMediaBox(0, 0, 612, 792);
        using PdfDocument doc = PdfDocument.Open(stream, leaveOpen: true);
        PdfPage page = doc.Pages[0];

        page.MediaBox = new PdfRectangle(5, 5, 105, 205);

        page.Dictionary.TryGetValue(PdfName.MediaBox, out PdfPrimitive? value).Should().BeTrue(
            "the setter persists a direct /MediaBox entry that re-saving will write");
        PdfArray array = (PdfArray)value!;
        array.GetNumber(0).Should().BeApproximately(5, 1e-6);
        array.GetNumber(2).Should().BeApproximately(105, 1e-6);
    }

    [Fact]
    public void SetCropBox_RoundTripsThroughGetter()
    {
        using MemoryStream stream = BuildPageWithInheritedMediaBox(0, 0, 612, 792);
        using PdfDocument doc = PdfDocument.Open(stream, leaveOpen: true);
        PdfPage page = doc.Pages[0];

        // No CropBox present: the getter defaults to MediaBox.
        page.CropBox.Width.Should().BeApproximately(612, 0.5);

        page.CropBox = new PdfRectangle(50, 60, 250, 360);

        page.CropBox.X1.Should().BeApproximately(50, 1e-6);
        page.CropBox.Y1.Should().BeApproximately(60, 1e-6);
        page.CropBox.Width.Should().BeApproximately(200, 1e-6);
        page.CropBox.Height.Should().BeApproximately(300, 1e-6);

        // Setting CropBox must not disturb MediaBox.
        page.MediaBox.Width.Should().BeApproximately(612, 0.5);
    }

    private static MemoryStream BuildPageWithInheritedMediaBox(
        double x1, double y1, double x2, double y2)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);

        // MediaBox lives on the /Pages node so the page inherits it.
        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, new PdfArray([new PdfReference(pageId)]));
        pagesDict.Set(PdfName.Count, 1);
        pagesDict.Set(PdfName.MediaBox, new PdfArray([
            new PdfReal(x1), new PdfReal(y1), new PdfReal(x2), new PdfReal(y2)
        ]));

        PdfDictionary pageDict = new PdfDictionary();
        pageDict.Set(PdfName.Type, PdfName.Page);
        pageDict.Set(PdfName.Parent, new PdfReference(pagesId));

        PdfDictionary catalogDict = new PdfDictionary();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

        List<PdfIndirectObject> objects = [
            new PdfIndirectObject(catalogId, catalogDict),
            new PdfIndirectObject(pagesId, pagesDict),
            new PdfIndirectObject(pageId, pageDict),
        ];

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        ms.Seek(0, SeekOrigin.Begin);
        return ms;
    }
}
