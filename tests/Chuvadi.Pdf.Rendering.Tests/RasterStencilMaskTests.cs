// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.9.6.2 — Stencil masking (/ImageMask true)
// PHASE: 3.18 — stencil masks in the raster pipeline.
//
// A stencil mask is a 1-bpc image with no colour space of its own: samples
// select where the CURRENT FILL COLOUR paints and where nothing paints at
// all. Before 3.18 both display-list builders skipped such images entirely.

using System.Linq;
using System.Text;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Rendering.Raster;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.Tests;

public sealed class RasterStencilMaskTests
{
    private static PdfStream StencilImage(byte[] samples, bool invertDecode = false)
    {
        PdfDictionary dict = new();
        dict.Set(PdfName.Intern("Type"), PdfName.Intern("XObject"));
        dict.Set(PdfName.Intern("Subtype"), PdfName.Intern("Image"));
        dict.Set(PdfName.Intern("Width"), 2);
        dict.Set(PdfName.Intern("Height"), 2);
        dict.Set(PdfName.Intern("BitsPerComponent"), 1);
        dict.Set(PdfName.Intern("ImageMask"), true);
        if (invertDecode)
        {
            dict.Set(PdfName.Intern("Decode"), new PdfArray(new PdfPrimitive[]
            {
                new PdfInteger(1), new PdfInteger(0),
            }));
        }

        return new PdfStream(dict, samples);
    }

    private static DrawImageOp BuildStencil(PdfStream image, string colorOps)
    {
        PdfDictionary xobjects = new();
        xobjects.Set(PdfName.Intern("Im1"), image);
        PdfDictionary resources = new();
        resources.Set(PdfName.Intern("XObject"), xobjects);

        byte[] content = Encoding.ASCII.GetBytes(colorOps + " q 100 0 0 100 0 0 cm /Im1 Do Q");
        PdfObjectStore store = new();
        PageDisplayList list = DisplayListBuilder.Build(content, resources, store, 612, 792);

        DrawImageOp[] images = list.Ops.OfType<DrawImageOp>().ToArray();
        images.Should().HaveCount(1, "the stencil must be emitted, not skipped");
        return images[0];
    }

    [Fact]
    public void Stencil_PaintsFillColourWhereSampleIsZero()
    {
        // Default /Decode [0 1]: sample 0 paints. Row byte 0b0100_0000:
        // x=0 bit 0 → paint, x=1 bit 1 → masked.
        DrawImageOp op = BuildStencil(StencilImage(new byte[] { 0x40, 0x40 }), "1 0 0 rg");

        op.Image.Pixels.GetPixelBgra(0, 0).Should().Be(((byte)0, (byte)0, (byte)255, (byte)255),
            "the painted sample takes the current fill colour (red) at full alpha");
        op.Image.Pixels.GetPixelBgra(1, 0).A.Should().Be(0,
            "the masked sample paints nothing (fully transparent)");
    }

    [Fact]
    public void Stencil_DecodeOneZero_InvertsThePaintedSamples()
    {
        DrawImageOp op = BuildStencil(
            StencilImage(new byte[] { 0x40, 0x40 }, invertDecode: true), "0 0 1 rg");

        op.Image.Pixels.GetPixelBgra(0, 0).A.Should().Be(0,
            "with /Decode [1 0] sample 0 is masked");
        op.Image.Pixels.GetPixelBgra(1, 0).Should().Be(((byte)255, (byte)0, (byte)0, (byte)255),
            "with /Decode [1 0] sample 1 paints the fill colour (blue)");
    }
}
