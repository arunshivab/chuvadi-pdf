// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.32.0 (D-240 R1) — the CompressToTarget rasterize fallback keeps a
//        rotated page the right way up.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Rendering;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

public sealed class CompressRotate3320Tests
{
    [Theory]
    [InlineData(90)]
    [InlineData(270)]
    public void RasterFallback_ShowsARotatedPageAsTheSourceDoes(int rotate)
    {
        byte[] source = Build(rotate);
        byte[] compressed = Compress(source);

        using PdfDocument result = PdfDocument.Open(new MemoryStream(compressed), leaveOpen: false);
        PdfPage page = result.Pages[0];
        page.Rotate.Should().Be(0);
        page.Width.Should().BeApproximately(100, 1, "the 200 x 100 page is shown rotated, 100 wide");
        page.Height.Should().BeApproximately(200, 1);

        using PdfDocument original = PdfDocument.Open(new MemoryStream(source), leaveOpen: false);
        PixelBuffer expected = new PageRasterizer(original.Objects, new RenderOptions { Dpi = 72, ApplyPageRotation = true }).Rasterize(original.Pages[0]);
        PixelBuffer actual = new PageRasterizer(result.Objects, new RenderOptions { Dpi = 72 }).Rasterize(page);

        actual.Width.Should().Be(expected.Width);
        actual.Height.Should().Be(expected.Height);
        foreach ((int x, int y) in Probes)
        {
            IsRed(actual.GetPixelBgra(x, y)).Should().Be(IsRed(expected.GetPixelBgra(x, y)), $"pixel {x},{y}");
        }
    }

    [Fact]
    public void RasterFallback_UnrotatedPage_KeepsItsSize()
    {
        byte[] compressed = Compress(Build(0));

        using PdfDocument result = PdfDocument.Open(new MemoryStream(compressed), leaveOpen: false);
        result.Pages[0].Width.Should().BeApproximately(200, 1);
        result.Pages[0].Height.Should().BeApproximately(100, 1);
    }

    private static readonly (int X, int Y)[] Probes = { (50, 50), (50, 150), (20, 20), (80, 180) };

    private static byte[] Compress(byte[] source)
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(source), leaveOpen: false);
        using MemoryStream output = new MemoryStream();
        CompressToTargetResult r = PdfCompressor.CompressToTarget(doc, output, 20_000, new CompressToTargetOptions
        {
            AllowRasterizeFallback = true,
            RasterizeDpi = 72,
            MinRasterizeDpi = 72,
        });
        r.UsedRasterizeFallback.Should().BeTrue("the vector content is far over the target");
        return output.ToArray();
    }

    private static bool IsRed((byte B, byte G, byte R, byte A) p) => p.R > 180 && p.G < 90 && p.B < 90;

    // A 200 x 100 page: many tiny random blue marks on the right half (so the
    // vector file cannot meet the target) and a red square on the left half.
    private static byte[] Build(int rotate)
    {
        Random random = new Random(42);
        StringBuilder content = new StringBuilder("0 0 1 rg\n");
        for (int i = 0; i < 40_000; i++)
        {
            double x = 110 + (random.NextDouble() * 85);
            double y = 5 + (random.NextDouble() * 90);
            content.Append(x.ToString("0.###", CultureInfo.InvariantCulture)).Append(' ')
                .Append(y.ToString("0.###", CultureInfo.InvariantCulture)).Append(" 0.2 0.2 re f\n");
        }

        content.Append("1 0 0 rg 0 0 100 100 re f\n");
        byte[] bytes = Encoding.ASCII.GetBytes(content.ToString());

        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(2, 0));
        PdfDictionary pages = new PdfDictionary();
        pages.Set(PdfName.Type, PdfName.Pages);
        pages.Set(PdfName.Kids, new PdfArray(new PdfPrimitive[] { new PdfReference(3, 0) }));
        pages.Set(PdfName.Count, 1);
        PdfDictionary page = new PdfDictionary();
        page.Set(PdfName.Type, PdfName.Page);
        page.Set(PdfName.Intern("Parent"), new PdfReference(2, 0));
        page.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[] { new PdfInteger(0), new PdfInteger(0), new PdfInteger(200), new PdfInteger(100) }));
        page.Set(PdfName.Intern("Rotate"), rotate);
        page.Set(PdfName.Resources, new PdfDictionary());
        page.Set(PdfName.Contents, new PdfReference(4, 0));
        PdfDictionary contentDict = new PdfDictionary();
        contentDict.Set(PdfName.Length, bytes.Length);

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(new PdfObjectId(1, 0), catalog),
            new PdfIndirectObject(new PdfObjectId(2, 0), pages),
            new PdfIndirectObject(new PdfObjectId(3, 0), page),
            new PdfIndirectObject(new PdfObjectId(4, 0), new PdfStream(contentDict, bytes)),
        };
        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(1, 0));
        using MemoryStream output = new MemoryStream();
        PdfWriter.Write(output, objects, trailer);
        return output.ToArray();
    }
}
