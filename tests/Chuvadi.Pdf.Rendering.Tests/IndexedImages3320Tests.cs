// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.6.6.3 (Indexed colour spaces), §8.9.5.2 (Decode)
// PHASE: 3.32.0 (D-268) — the raster renderer draws Indexed (palette)
//        images of 1, 2, 4 and 8 bits per sample. Before 3.32.0 they were
//        skipped and nothing was drawn.

using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.Tests;

public sealed class IndexedImages3320Tests
{
    // Red, green, blue, white.
    private static readonly byte[] Palette4 = { 255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255 };

    private static readonly byte[] TwoBit = { 0x1B };

    private static readonly byte[] FourBit = { 0x01, 0x23 };

    private static readonly byte[] EightBit = { 0, 1, 2, 3 };

    private static readonly byte[] OneBit = { 0x50 };

    [Fact]
    public void TwoBitPalette_DrawsEachColour()
    {
        PixelBuffer page = Render(2, TwoBit, Palette4, hival: 3);

        Colours(page).Should().Equal((255, 0, 0), (0, 255, 0), (0, 0, 255), (255, 255, 255));
    }

    [Fact]
    public void FourBitPalette_DrawsEachColour()
    {
        PixelBuffer page = Render(4, FourBit, Palette4, hival: 3);

        Colours(page).Should().Equal((255, 0, 0), (0, 255, 0), (0, 0, 255), (255, 255, 255));
    }

    [Fact]
    public void EightBitPalette_DrawsEachColour()
    {
        PixelBuffer page = Render(8, EightBit, Palette4, hival: 3);

        Colours(page).Should().Equal((255, 0, 0), (0, 255, 0), (0, 0, 255), (255, 255, 255));
    }

    [Fact]
    public void OneBitPalette_DrawsBothColours()
    {
        PixelBuffer page = Render(1, OneBit, Palette4, hival: 1);

        Colours(page).Should().Equal((255, 0, 0), (0, 255, 0), (255, 0, 0), (0, 255, 0));
    }

    [Fact]
    public void Decode_ReversesTheIndices()
    {
        PixelBuffer page = Render(2, TwoBit, Palette4, hival: 3, decode: "3 0");

        Colours(page).Should().Equal((255, 255, 255), (0, 0, 255), (0, 255, 0), (255, 0, 0));
    }

    [Fact]
    public void IndexBeyondThePalette_IsClampedToTheLastEntry()
    {
        // 2-bit samples 0..3 against a two-entry palette (hival 1).
        PixelBuffer page = Render(2, TwoBit, Palette4, hival: 1);

        Colours(page).Should().Equal((255, 0, 0), (0, 255, 0), (0, 255, 0), (0, 255, 0));
    }

    [Fact]
    public void TooFewSamples_DrawsNothing_AndDoesNotThrow()
    {
        PixelBuffer page = Render(4, new byte[] { 0x01 }, Palette4, hival: 3);

        Colours(page).Should().Equal((255, 255, 255), (255, 255, 255), (255, 255, 255), (255, 255, 255));
    }

    [Fact]
    public void ImageWithInvalidFlate_IsSkipped_AndReportedAsDamaged()
    {
        // A zlib stream whose dynamic block has an over-subscribed code-length code (D-271).
        byte[] bad = { 0x78, 0x9C, 0x05, 0x00, 0x92, 0x04, 0, 0, 0, 0, 0, 0 };
        List<PdfWarning> warnings = new List<PdfWarning>();

        PixelBuffer page = Render(8, bad, Palette4, hival: 3, flate: true, warnings: warnings);

        Colours(page).Should().Equal((255, 255, 255), (255, 255, 255), (255, 255, 255), (255, 255, 255));
        PdfWarning warning = warnings.Should().ContainSingle().Subject;
        warning.Kind.Should().Be(PdfWarningKind.StreamDamaged);
        warning.Subject.Should().Be("image Im0 5 0 R");
        warning.Reason.Should().Contain("over-subscribed").And.Contain("skipped");
        warning.PageNumber.Should().Be(1);
    }

    // The colours at the centres of the four image columns (each 50 px wide at 72 dpi).
    private static List<(int R, int G, int B)> Colours(PixelBuffer page)
    {
        List<(int R, int G, int B)> colours = new List<(int R, int G, int B)>();
        for (int i = 0; i < 4; i++)
        {
            (byte b, byte g, byte r, _) = page.GetPixelBgra((i * 50) + 25, 100);
            colours.Add((r, g, b));
        }

        return colours;
    }

    // A 200 x 200 pt page filled by a 4 x 1 Indexed image.
    private static PixelBuffer Render(
        int bpc, byte[] samples, byte[] palette, int hival, string? decode = null, bool flate = false, List<PdfWarning>? warnings = null)
    {
        PdfDictionary image = new PdfDictionary();
        image.Set(PdfName.Type, PdfName.XObject);
        image.Set(PdfName.Intern("Subtype"), PdfName.Intern("Image"));
        image.Set(PdfName.Intern("Width"), 4);
        image.Set(PdfName.Intern("Height"), 1);
        image.Set(PdfName.Intern("BitsPerComponent"), bpc);
        image.Set(PdfName.Intern("ColorSpace"), new PdfArray(new PdfPrimitive[]
        {
            PdfName.Intern("Indexed"), PdfName.Intern("DeviceRGB"), new PdfInteger(hival), new PdfString(palette, preferHexForm: true),
        }));
        if (decode is not null)
        {
            string[] parts = decode.Split(' ');
            image.Set(PdfName.Intern("Decode"), new PdfArray(new PdfPrimitive[] { new PdfInteger(int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture)), new PdfInteger(int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture)) }));
        }

        if (flate)
        {
            image.Set(PdfName.Intern("Filter"), PdfName.Intern("FlateDecode"));
        }

        image.Set(PdfName.Length, samples.Length);

        PdfDictionary xobjects = new PdfDictionary();
        xobjects.Set(PdfName.Intern("Im0"), new PdfReference(5, 0));
        PdfDictionary resources = new PdfDictionary();
        resources.Set(PdfName.XObject, xobjects);

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
        page.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[] { new PdfInteger(0), new PdfInteger(0), new PdfInteger(200), new PdfInteger(200) }));
        page.Set(PdfName.Resources, resources);
        page.Set(PdfName.Contents, new PdfReference(4, 0));
        byte[] content = Encoding.ASCII.GetBytes("q 200 0 0 200 0 0 cm /Im0 Do Q");
        PdfDictionary contentDict = new PdfDictionary();
        contentDict.Set(PdfName.Length, content.Length);

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(new PdfObjectId(1, 0), catalog),
            new PdfIndirectObject(new PdfObjectId(2, 0), pages),
            new PdfIndirectObject(new PdfObjectId(3, 0), page),
            new PdfIndirectObject(new PdfObjectId(4, 0), new PdfStream(contentDict, content)),
            new PdfIndirectObject(new PdfObjectId(5, 0), new PdfStream(image, samples)),
        };
        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(1, 0));
        using MemoryStream output = new MemoryStream();
        PdfWriter.Write(output, objects, trailer);
        output.Position = 0;

        using PdfDocument doc = PdfDocument.Open(output, new PdfOpenOptions { OnWarning = w => warnings?.Add(w) });
        return new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72 }).Rasterize(doc.Pages[0]);
    }
}
