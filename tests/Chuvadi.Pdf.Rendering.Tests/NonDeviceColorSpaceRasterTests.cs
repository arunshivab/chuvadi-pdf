// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.6.6.4 (Separation), §8.6.6.3 (Indexed)
// PHASE: Phase 2 — item 13, raster honours non-device colour spaces
//
// The raster display-list builder previously marked any non-device colour space
// invalid and suppressed the paint. It now resolves the space against the page
// resources and converts sc / scn through the shared colour-space model, so the
// fill op carries the correct device-RGB colour.

using System.Linq;
using System.Text;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Rendering.Raster;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.Tests;

public sealed class NonDeviceColorSpaceRasterTests
{
    [Fact]
    public void SeparationFill_ResolvesTintTransformToRed()
    {
        PdfDictionary tint = new PdfDictionary();
        tint.Set(PdfName.Intern("FunctionType"), 2);
        tint.Set(PdfName.Intern("Domain"), Nums(0, 1));
        tint.Set(PdfName.Intern("C0"), Nums(1, 1, 1));
        tint.Set(PdfName.Intern("C1"), Nums(1, 0, 0));
        tint.Set(PdfName.Intern("N"), 1);

        PdfArray separation = new PdfArray(new PdfPrimitive[]
        {
            PdfName.Intern("Separation"),
            PdfName.Intern("Spot"),
            PdfName.Intern("DeviceRGB"),
            tint,
        });

        FillPathOp op = FirstFill(
            "/CS0 cs 1 scn 0 0 10 10 re f", Resources("CS0", separation));

        op.Color.Space.Should().Be(ColorSpace.Rgb);
        op.Color.R.Should().BeApproximately(1.0f, 0.01f);
        op.Color.G.Should().BeApproximately(0.0f, 0.01f);
        op.Color.B.Should().BeApproximately(0.0f, 0.01f);
    }

    [Fact]
    public void IndexedFill_ResolvesPaletteEntryToBlue()
    {
        PdfArray indexed = new PdfArray(new PdfPrimitive[]
        {
            PdfName.Intern("Indexed"),
            PdfName.Intern("DeviceRGB"),
            new PdfInteger(1),
            new PdfString(new byte[] { 255, 0, 0, 0, 0, 255 }),
        });

        FillPathOp op = FirstFill(
            "/CS0 cs 1 scn 0 0 10 10 re f", Resources("CS0", indexed));

        op.Color.Space.Should().Be(ColorSpace.Rgb);
        op.Color.R.Should().BeApproximately(0.0f, 0.01f);
        op.Color.G.Should().BeApproximately(0.0f, 0.01f);
        op.Color.B.Should().BeApproximately(1.0f, 0.01f);
    }

    private static FillPathOp FirstFill(string content, PdfDictionary resources)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(content);
        PdfObjectStore store = new PdfObjectStore();
        PageDisplayList list = DisplayListBuilder.Build(bytes, resources, store, 612, 792);
        return list.Ops.OfType<FillPathOp>().First();
    }

    private static PdfDictionary Resources(string name, PdfArray colorSpace)
    {
        PdfDictionary colorSpaces = new PdfDictionary();
        colorSpaces.Set(PdfName.Intern(name), colorSpace);
        PdfDictionary resources = new PdfDictionary();
        resources.Set(PdfName.Intern("ColorSpace"), colorSpaces);
        return resources;
    }

    private static PdfArray Nums(params double[] values)
    {
        PdfPrimitive[] items = new PdfPrimitive[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            items[i] = new PdfReal(values[i]);
        }

        return new PdfArray(items);
    }
}
