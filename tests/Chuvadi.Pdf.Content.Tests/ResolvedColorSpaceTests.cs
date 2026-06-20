// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.6 — Colour spaces
// PHASE: Phase 2 — item 13, non-device colour spaces
//
// Verifies that each colour-space family parses and converts its components to
// sRGB: device spaces directly, Indexed via a palette lookup, Separation /
// DeviceN through a tint transform, ICCBased via its alternate or component
// count, and Lab through the CIE conversion. CalGray / CalRGB use an
// XYZ -> sRGB approximation, so they are checked at their black/white anchors.

using System;
using System.Text;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Content.Tests;

public sealed class PdfColorSpaceTests
{
    [Fact]
    public void DeviceCmyk_PureCyan_IsCyan()
    {
        ResolvedColorSpace cs = ParseName("DeviceCMYK");
        double[] rgb = Convert(cs, 1, 0, 0, 0);

        rgb[0].Should().BeApproximately(0.0, 1e-9);
        rgb[1].Should().BeApproximately(1.0, 1e-9);
        rgb[2].Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void DeviceCmyk_PureBlack_IsBlack()
    {
        ResolvedColorSpace cs = ParseName("DeviceCMYK");
        double[] rgb = Convert(cs, 0, 0, 0, 1);

        rgb.Should().AllSatisfy(v => v.Should().BeApproximately(0.0, 1e-9));
    }

    [Fact]
    public void DeviceGray_Midtone_IsEqualChannels()
    {
        ResolvedColorSpace cs = ParseName("DeviceGray");
        double[] rgb = Convert(cs, 0.5);

        rgb[0].Should().BeApproximately(0.5, 1e-9);
        rgb[1].Should().BeApproximately(0.5, 1e-9);
        rgb[2].Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void DeviceRgb_PassesComponentsThrough()
    {
        ResolvedColorSpace cs = ParseName("DeviceRGB");
        double[] rgb = Convert(cs, 0.1, 0.2, 0.3);

        rgb[0].Should().BeApproximately(0.1, 1e-9);
        rgb[1].Should().BeApproximately(0.2, 1e-9);
        rgb[2].Should().BeApproximately(0.3, 1e-9);
    }

    [Fact]
    public void Lab_LightnessHundred_IsWhite()
    {
        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Intern("WhitePoint"), Nums(0.9505, 1.0, 1.0890));
        PdfArray array = new PdfArray(new PdfPrimitive[] { PdfName.Intern("Lab"), dict });

        ResolvedColorSpace cs = ResolvedColorSpace.Parse(array, new PdfObjectStore())!;
        double[] rgb = Convert(cs, 100, 0, 0);

        rgb.Should().AllSatisfy(v => v.Should().BeApproximately(1.0, 0.02));
    }

    [Fact]
    public void Lab_LightnessZero_IsBlack()
    {
        PdfArray array = new PdfArray(new PdfPrimitive[]
        {
            PdfName.Intern("Lab"), new PdfDictionary(),
        });

        ResolvedColorSpace cs = ResolvedColorSpace.Parse(array, new PdfObjectStore())!;
        double[] rgb = Convert(cs, 0, 0, 0);

        rgb.Should().AllSatisfy(v => v.Should().BeApproximately(0.0, 0.02));
    }

    [Fact]
    public void Indexed_ResolvesPaletteEntriesThroughBase()
    {
        PdfArray array = new PdfArray(new PdfPrimitive[]
        {
            PdfName.Intern("Indexed"),
            PdfName.Intern("DeviceRGB"),
            new PdfInteger(1),
            new PdfString(new byte[] { 255, 0, 0, 0, 0, 255 }),
        });

        ResolvedColorSpace cs = ResolvedColorSpace.Parse(array, new PdfObjectStore())!;
        cs.ComponentCount.Should().Be(1);

        double[] zero = Convert(cs, 0);
        zero[0].Should().BeApproximately(1.0, 1e-9);
        zero[1].Should().BeApproximately(0.0, 1e-9);
        zero[2].Should().BeApproximately(0.0, 1e-9);

        double[] one = Convert(cs, 1);
        one[0].Should().BeApproximately(0.0, 1e-9);
        one[1].Should().BeApproximately(0.0, 1e-9);
        one[2].Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void Indexed_ClampsOutOfRangeIndex()
    {
        PdfArray array = new PdfArray(new PdfPrimitive[]
        {
            PdfName.Intern("Indexed"),
            PdfName.Intern("DeviceRGB"),
            new PdfInteger(1),
            new PdfString(new byte[] { 255, 0, 0, 0, 0, 255 }),
        });

        ResolvedColorSpace cs = ResolvedColorSpace.Parse(array, new PdfObjectStore())!;
        double[] high = Convert(cs, 9);

        high[2].Should().BeApproximately(1.0, 1e-9, "index clamps to hival (the blue entry)");
    }

    [Fact]
    public void Separation_RunsTintTransformThroughAlternate()
    {
        // Tint Type 2: 1 input -> 3 RGB outputs, white at 0 ink, red at full ink.
        PdfDictionary tint = new PdfDictionary();
        tint.Set(PdfName.Intern("FunctionType"), 2);
        tint.Set(PdfName.Intern("Domain"), Nums(0, 1));
        tint.Set(PdfName.Intern("C0"), Nums(1, 1, 1));
        tint.Set(PdfName.Intern("C1"), Nums(1, 0, 0));
        tint.Set(PdfName.Intern("N"), 1);

        PdfArray array = new PdfArray(new PdfPrimitive[]
        {
            PdfName.Intern("Separation"),
            PdfName.Intern("Spot"),
            PdfName.Intern("DeviceRGB"),
            tint,
        });

        ResolvedColorSpace cs = ResolvedColorSpace.Parse(array, new PdfObjectStore())!;
        cs.ComponentCount.Should().Be(1);

        double[] full = Convert(cs, 1);
        full[0].Should().BeApproximately(1.0, 1e-6);
        full[1].Should().BeApproximately(0.0, 1e-6);
        full[2].Should().BeApproximately(0.0, 1e-6);

        double[] none = Convert(cs, 0);
        none.Should().AllSatisfy(v => v.Should().BeApproximately(1.0, 1e-6));
    }

    [Fact]
    public void DeviceN_TwoColorants_RunsTintTransform()
    {
        // Type 4: ignore both inputs, emit green in the DeviceRGB alternate.
        PdfDictionary fnDict = new PdfDictionary();
        fnDict.Set(PdfName.Intern("FunctionType"), 4);
        fnDict.Set(PdfName.Intern("Domain"), Nums(0, 1, 0, 1));
        fnDict.Set(PdfName.Intern("Range"), Nums(0, 1, 0, 1, 0, 1));
        byte[] program = Encoding.ASCII.GetBytes("{ pop pop 0 1 0 }");
        fnDict.Set(PdfName.Intern("Length"), program.Length);
        PdfStream fnStream = new PdfStream(fnDict, program);

        PdfArray names = new PdfArray(new PdfPrimitive[]
        {
            PdfName.Intern("Ink1"), PdfName.Intern("Ink2"),
        });
        PdfArray array = new PdfArray(new PdfPrimitive[]
        {
            PdfName.Intern("DeviceN"),
            names,
            PdfName.Intern("DeviceRGB"),
            fnStream,
        });

        ResolvedColorSpace cs = ResolvedColorSpace.Parse(array, new PdfObjectStore())!;
        cs.ComponentCount.Should().Be(2);

        double[] rgb = Convert(cs, 0.5, 0.5);
        rgb[0].Should().BeApproximately(0.0, 1e-6);
        rgb[1].Should().BeApproximately(1.0, 1e-6);
        rgb[2].Should().BeApproximately(0.0, 1e-6);
    }

    [Fact]
    public void IccBased_NoAlternate_FallsBackByComponentCount()
    {
        PdfDictionary streamDict = new PdfDictionary();
        streamDict.Set(PdfName.Intern("N"), 3);
        streamDict.Set(PdfName.Length, 0);
        PdfStream stream = new PdfStream(streamDict, Array.Empty<byte>());

        PdfArray array = new PdfArray(new PdfPrimitive[]
        {
            PdfName.Intern("ICCBased"), stream,
        });

        ResolvedColorSpace cs = ResolvedColorSpace.Parse(array, new PdfObjectStore())!;
        cs.ComponentCount.Should().Be(3);

        double[] rgb = Convert(cs, 1, 0, 0);
        rgb[0].Should().BeApproximately(1.0, 1e-9);
        rgb[1].Should().BeApproximately(0.0, 1e-9);
        rgb[2].Should().BeApproximately(0.0, 1e-9);
    }

    [Fact]
    public void IccBased_WithCmykAlternate_UsesAlternate()
    {
        PdfDictionary streamDict = new PdfDictionary();
        streamDict.Set(PdfName.Intern("N"), 4);
        streamDict.Set(PdfName.Intern("Alternate"), PdfName.Intern("DeviceCMYK"));
        streamDict.Set(PdfName.Length, 0);
        PdfStream stream = new PdfStream(streamDict, Array.Empty<byte>());

        PdfArray array = new PdfArray(new PdfPrimitive[]
        {
            PdfName.Intern("ICCBased"), stream,
        });

        ResolvedColorSpace cs = ResolvedColorSpace.Parse(array, new PdfObjectStore())!;
        cs.ComponentCount.Should().Be(4);

        double[] rgb = Convert(cs, 1, 0, 0, 0);
        rgb[1].Should().BeApproximately(1.0, 1e-9, "cyan ink leaves green and blue");
        rgb[2].Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void CalGray_AnchorsAtBlackAndWhite()
    {
        PdfArray array = new PdfArray(new PdfPrimitive[]
        {
            PdfName.Intern("CalGray"), new PdfDictionary(),
        });

        ResolvedColorSpace cs = ResolvedColorSpace.Parse(array, new PdfObjectStore())!;
        cs.ComponentCount.Should().Be(1);

        Convert(cs, 0).Should().AllSatisfy(v => v.Should().BeApproximately(0.0, 0.02));
        Convert(cs, 1).Should().AllSatisfy(v => v.Should().BeApproximately(1.0, 0.05));
    }

    [Fact]
    public void CalRgb_Black_IsBlack()
    {
        PdfArray array = new PdfArray(new PdfPrimitive[]
        {
            PdfName.Intern("CalRGB"), new PdfDictionary(),
        });

        ResolvedColorSpace cs = ResolvedColorSpace.Parse(array, new PdfObjectStore())!;
        cs.ComponentCount.Should().Be(3);

        Convert(cs, 0, 0, 0).Should().AllSatisfy(v => v.Should().BeApproximately(0.0, 1e-9));
    }

    [Fact]
    public void Parse_PatternName_IsPattern()
    {
        ResolvedColorSpace cs = ParseName("Pattern");
        cs.IsPattern.Should().BeTrue();
    }

    [Fact]
    public void Parse_UnknownName_ReturnsNull()
    {
        ResolvedColorSpace.Parse(PdfName.Intern("Nonsense"), new PdfObjectStore()).Should().BeNull();
    }

    private static ResolvedColorSpace ParseName(string name)
    {
        return ResolvedColorSpace.Parse(PdfName.Intern(name), new PdfObjectStore())!;
    }

    private static double[] Convert(ResolvedColorSpace cs, params double[] components)
    {
        return cs.ToRgb(components);
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
