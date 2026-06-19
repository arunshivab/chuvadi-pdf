// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §11.6.5.2 — Soft-mask images (SMask)
//
// Audit coverage: an image XObject carrying a per-pixel /SMask must composite
// its alpha in the rasteriser - opaque samples paint the image colour, fully
// transparent samples leave the page background showing through. The embed
// path (DrawImage_RgbaPng_EmitsSoftMask) and the SVG sink are already covered;
// this closes the raster image-alpha gap.

using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.Tests;

public sealed class RasterImageSoftMaskTests
{
    [Fact]
    public void ImageSoftMask_TransparentSamples_ShowBackground()
    {
        // 2x1 red image; SMask makes the left sample opaque and the right
        // sample fully transparent. Drawn across a 20x10 white page, the left
        // half must be red and the right half must remain white.
        using MemoryStream pdf = BuildImageWithSoftMask();
        using PdfDocument doc = PdfDocument.Open(pdf, leaveOpen: true);
        PageRasterizer rasterizer = new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72 });
        PixelBuffer buffer = rasterizer.Rasterize(doc.Pages[0]);

        int midY = buffer.Height / 2;
        (byte _, byte _, byte leftR, byte _) = buffer.GetPixelBgra(buffer.Width / 4, midY);
        (byte rb, byte rg, byte rr, byte _) =
            buffer.GetPixelBgra(buffer.Width * 3 / 4, midY);

        // Left (opaque): strongly red.
        leftR.Should().BeGreaterThan(200, "the opaque image sample paints red");

        // Right (transparent): page background shows through (white).
        rr.Should().BeGreaterThan(230);
        rg.Should().BeGreaterThan(230);
        rb.Should().BeGreaterThan(230);
    }

    private static MemoryStream BuildImageWithSoftMask()
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);
        PdfObjectId contentId = new PdfObjectId(4, 0);
        PdfObjectId imageId = new PdfObjectId(5, 0);
        PdfObjectId smaskId = new PdfObjectId(6, 0);

        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Intern("Catalog"));
        catalog.Set(PdfName.Intern("Pages"), new PdfReference(pagesId));

        PdfDictionary pages = new PdfDictionary();
        pages.Set(PdfName.Type, PdfName.Intern("Pages"));
        pages.Set(PdfName.Intern("Kids"), new PdfArray(new PdfPrimitive[] { new PdfReference(pageId) }));
        pages.Set(PdfName.Intern("Count"), 1);

        PdfDictionary page = new PdfDictionary();
        page.Set(PdfName.Type, PdfName.Intern("Page"));
        page.Set(PdfName.Intern("Parent"), new PdfReference(pagesId));
        page.Set(PdfName.Intern("MediaBox"), new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(20), new PdfInteger(10),
        }));
        page.Set(PdfName.Intern("Contents"), new PdfReference(contentId));

        // Soft mask: DeviceGray 2x1, [255, 0] -> left opaque, right transparent.
        PdfDictionary smaskDict = new PdfDictionary();
        smaskDict.Set(PdfName.Type, PdfName.Intern("XObject"));
        smaskDict.Set(PdfName.Intern("Subtype"), PdfName.Intern("Image"));
        smaskDict.Set(PdfName.Intern("Width"), 2);
        smaskDict.Set(PdfName.Intern("Height"), 1);
        smaskDict.Set(PdfName.Intern("ColorSpace"), PdfName.Intern("DeviceGray"));
        smaskDict.Set(PdfName.Intern("BitsPerComponent"), 8);
        byte[] smaskData = new byte[] { 0xFF, 0x00 };
        smaskDict.Set(PdfName.Length, smaskData.Length);

        // Image: DeviceRGB 2x1, both red.
        PdfDictionary imageDict = new PdfDictionary();
        imageDict.Set(PdfName.Type, PdfName.Intern("XObject"));
        imageDict.Set(PdfName.Intern("Subtype"), PdfName.Intern("Image"));
        imageDict.Set(PdfName.Intern("Width"), 2);
        imageDict.Set(PdfName.Intern("Height"), 1);
        imageDict.Set(PdfName.Intern("ColorSpace"), PdfName.Intern("DeviceRGB"));
        imageDict.Set(PdfName.Intern("BitsPerComponent"), 8);
        imageDict.Set(PdfName.Intern("SMask"), new PdfReference(smaskId));
        byte[] imageData = new byte[] { 0xFF, 0x00, 0x00, 0xFF, 0x00, 0x00 };
        imageDict.Set(PdfName.Length, imageData.Length);

        PdfDictionary xobjects = new PdfDictionary();
        xobjects.Set(PdfName.Intern("Im"), new PdfReference(imageId));
        PdfDictionary resources = new PdfDictionary();
        resources.Set(PdfName.Intern("XObject"), xobjects);
        page.Set(PdfName.Intern("Resources"), resources);

        byte[] content = System.Text.Encoding.ASCII.GetBytes("q 20 0 0 10 0 0 cm /Im Do Q");
        PdfDictionary contentDict = new PdfDictionary();
        contentDict.Set(PdfName.Length, content.Length);

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(catalogId, catalog),
            new PdfIndirectObject(pagesId, pages),
            new PdfIndirectObject(pageId, page),
            new PdfIndirectObject(contentId, new PdfStream(contentDict, content)),
            new PdfIndirectObject(imageId, new PdfStream(imageDict, imageData)),
            new PdfIndirectObject(smaskId, new PdfStream(smaskDict, smaskData)),
        };

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream output = new MemoryStream();
        PdfWriter.Write(output, objects, trailer);
        output.Position = 0;
        return output;
    }
}
