// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §14.11.2 (page boundaries); §8.5.4 (clipping)
// Tests for PageCropper clip-crop (C1): box reset + hard clip wrap.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

public sealed class PageCropperTests
{
    private static MemoryStream BuildPdf(int pages)
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        for (int i = 0; i < pages; i++)
        {
            builder.AddPage(PageSize.A4).DrawRectangle(0, 0, 595, 842, fill: Colors.Red);
        }

        return new MemoryStream(builder.ToByteArray());
    }

    private static PdfDocument Crop(PdfDocument source, params PageCrop[] crops)
    {
        MemoryStream output = new MemoryStream();
        PageCropper.Crop(output, source, crops);
        output.Position = 0;
        return PdfDocument.Open(output, leaveOpen: true);
    }

    private static string FirstContentStreamText(PdfPage page, PdfObjectStore store)
    {
        page.Dictionary.TryGetValue(PdfName.Contents, out PdfPrimitive? contentsPrim).Should().BeTrue();
        PdfPrimitive resolved = store.Resolve(contentsPrim!);
        PdfArray array = resolved.Should().BeOfType<PdfArray>().Subject;
        PdfStream first = store.Resolve(array[0]).Should().BeOfType<PdfStream>().Subject;
        return Encoding.Latin1.GetString(first.RawBytes);
    }

    [Fact]
    public void Crop_ResetsMediaBoxAndCropBox_ToCropRect()
    {
        using MemoryStream src = BuildPdf(1);
        using PdfDocument source = PdfDocument.Open(src, leaveOpen: true);

        using PdfDocument cropped = Crop(source, new PageCrop(0, new RectangleF(50, 60, 200, 300)));
        PdfPage page = cropped.Pages[0];

        page.MediaBox.X1.Should().BeApproximately(50, 0.01);
        page.MediaBox.Y1.Should().BeApproximately(60, 0.01);
        page.MediaBox.Width.Should().BeApproximately(200, 0.01);
        page.MediaBox.Height.Should().BeApproximately(300, 0.01);
        page.CropBox.Width.Should().BeApproximately(200, 0.01);
        page.CropBox.Height.Should().BeApproximately(300, 0.01);
    }

    [Fact]
    public void Crop_WrapsExistingContent_InHardClip()
    {
        using MemoryStream src = BuildPdf(1);
        using PdfDocument source = PdfDocument.Open(src, leaveOpen: true);

        using PdfDocument cropped = Crop(source, new PageCrop(0, new RectangleF(0, 0, 297.5, 842)));
        string firstStream = FirstContentStreamText(cropped.Pages[0], cropped.Objects);

        firstStream.Should().Contain("re W n", "the crop rectangle is established as a clip");
        firstStream.Should().Contain("297.5", "the clip uses the crop rectangle dimensions");
    }

    [Fact]
    public void Crop_LeavesUnlistedPages_Untouched()
    {
        using MemoryStream src = BuildPdf(2);
        using PdfDocument source = PdfDocument.Open(src, leaveOpen: true);

        using PdfDocument cropped = Crop(source, new PageCrop(0, new RectangleF(0, 0, 100, 100)));

        cropped.Pages[0].MediaBox.Width.Should().BeApproximately(100, 0.01);
        cropped.Pages[1].MediaBox.Width.Should().BeApproximately(595, 0.01, "page 1 was not in the crop list");
    }

    [Fact]
    public void Crop_OutOfRangePageIndex_IsIgnored()
    {
        using MemoryStream src = BuildPdf(1);
        using PdfDocument source = PdfDocument.Open(src, leaveOpen: true);

        using PdfDocument cropped = Crop(source, new PageCrop(7, new RectangleF(0, 0, 100, 100)));

        cropped.Pages[0].MediaBox.Width.Should().BeApproximately(595, 0.01, "no in-range page was cropped");
    }

    [Fact]
    public void Crop_NullArguments_Throw()
    {
        using MemoryStream src = BuildPdf(1);
        using PdfDocument source = PdfDocument.Open(src, leaveOpen: true);
        List<PageCrop> crops = new List<PageCrop> { new PageCrop(0, new RectangleF(0, 0, 10, 10)) };

        Action nullOutput = () => PageCropper.Crop(null!, source, crops);
        Action nullDoc = () => PageCropper.Crop(new MemoryStream(), null!, crops);
        Action nullCrops = () => PageCropper.Crop(new MemoryStream(), source, null!);

        nullOutput.Should().Throw<ArgumentNullException>();
        nullDoc.Should().Throw<ArgumentNullException>();
        nullCrops.Should().Throw<ArgumentNullException>();
    }
}
