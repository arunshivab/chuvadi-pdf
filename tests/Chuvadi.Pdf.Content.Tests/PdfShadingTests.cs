// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.7.4.5 — Shadings (Type 2 axial, Type 3 radial)
// PHASE: Phase 2 — rendering conformance
// Exact-numeric coverage for shading geometry parsing and colour evaluation.

using System.Text;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Content.Tests;

public sealed class PdfShadingTests
{
    [Fact]
    public void Axial_Parse_ExposesGeometryAndDefaults()
    {
        PdfShading shading = PdfShading.Parse(Axial(Nums(0, 0, 1, 0), RgbRamp()), new PdfObjectStore());

        shading.ShadingType.Should().Be(2);
        shading.IsAxial.Should().BeTrue();
        shading.IsRadial.Should().BeFalse();
        shading.Coords.Should().Equal(new[] { 0.0, 0.0, 1.0, 0.0 });
        shading.DomainStart.Should().Be(0.0);
        shading.DomainEnd.Should().Be(1.0);
        shading.ExtendStart.Should().BeFalse();
        shading.ExtendEnd.Should().BeFalse();
    }

    [Fact]
    public void Axial_EvaluateRgb_RampsBlackToWhite()
    {
        PdfShading shading = PdfShading.Parse(Axial(Nums(0, 0, 1, 0), RgbRamp()), new PdfObjectStore());

        shading.EvaluateRgb(0.0).Should().Be((0.0, 0.0, 0.0));
        shading.EvaluateRgb(1.0).Should().Be((1.0, 1.0, 1.0));

        (double r, double g, double b) = shading.EvaluateRgb(0.5);
        r.Should().BeApproximately(0.5, 1e-9);
        g.Should().BeApproximately(0.5, 1e-9);
        b.Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void Radial_Parse_ExposesSixCoords()
    {
        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Intern("ShadingType"), 3);
        dict.Set(PdfName.Intern("ColorSpace"), PdfName.Intern("DeviceRGB"));
        dict.Set(PdfName.Intern("Coords"), Nums(0, 0, 0, 2, 2, 5));
        dict.Set(PdfName.Intern("Function"), RgbRamp());

        PdfShading shading = PdfShading.Parse(dict, new PdfObjectStore());

        shading.IsRadial.Should().BeTrue();
        shading.Coords.Should().Equal(new[] { 0.0, 0.0, 0.0, 2.0, 2.0, 5.0 });
    }

    [Fact]
    public void Extend_IsParsed()
    {
        PdfDictionary dict = Axial(Nums(0, 0, 1, 0), RgbRamp());
        dict.Set(PdfName.Intern("Extend"), new PdfArray(new PdfPrimitive[]
        {
            PdfBoolean.True, PdfBoolean.False,
        }));

        PdfShading shading = PdfShading.Parse(dict, new PdfObjectStore());

        shading.ExtendStart.Should().BeTrue();
        shading.ExtendEnd.Should().BeFalse();
    }

    [Fact]
    public void Domain_MapsNormalizedPositionOntoFunctionInput()
    {
        // Identity Type 4 function over [0,2]; the normalized position s should
        // map to t = 0 + s*(2-0), so s=0.5 -> t=1.0.
        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Intern("ShadingType"), 2);
        dict.Set(PdfName.Intern("ColorSpace"), PdfName.Intern("DeviceGray"));
        dict.Set(PdfName.Intern("Coords"), Nums(0, 0, 1, 0));
        dict.Set(PdfName.Intern("Domain"), Nums(0, 2));
        dict.Set(PdfName.Intern("Function"), Identity(Nums(0, 2), Nums(0, 2)));

        PdfShading shading = PdfShading.Parse(dict, new PdfObjectStore());

        shading.EvaluateColor(0.0)[0].Should().BeApproximately(0.0, 1e-9);
        shading.EvaluateColor(0.5)[0].Should().BeApproximately(1.0, 1e-9);
        shading.EvaluateColor(1.0)[0].Should().BeApproximately(2.0, 1e-9);
    }

    [Fact]
    public void Gray_ToRgb_ReplicatesComponent()
    {
        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Intern("ShadingType"), 2);
        dict.Set(PdfName.Intern("ColorSpace"), PdfName.Intern("DeviceGray"));
        dict.Set(PdfName.Intern("Coords"), Nums(0, 0, 1, 0));
        dict.Set(PdfName.Intern("Function"), Type2(Nums(0), Nums(1)));

        PdfShading shading = PdfShading.Parse(dict, new PdfObjectStore());

        shading.EvaluateRgb(1.0).Should().Be((1.0, 1.0, 1.0));
        shading.EvaluateRgb(0.0).Should().Be((0.0, 0.0, 0.0));
    }

    [Fact]
    public void Cmyk_ToRgb_ConvertsPureCyan()
    {
        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Intern("ShadingType"), 2);
        dict.Set(PdfName.Intern("ColorSpace"), PdfName.Intern("DeviceCMYK"));
        dict.Set(PdfName.Intern("Coords"), Nums(0, 0, 1, 0));
        dict.Set(PdfName.Intern("Function"), Type2(Nums(0, 0, 0, 0), Nums(1, 0, 0, 0)));

        PdfShading shading = PdfShading.Parse(dict, new PdfObjectStore());

        // Pure cyan (1,0,0,0) -> RGB (0,1,1)
        (double r, double g, double b) = shading.EvaluateRgb(1.0);
        r.Should().BeApproximately(0.0, 1e-9);
        g.Should().BeApproximately(1.0, 1e-9);
        b.Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void UnsupportedShadingType_Throws()
    {
        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Intern("ShadingType"), 1);
        dict.Set(PdfName.Intern("Coords"), Nums(0, 0, 1, 0));
        dict.Set(PdfName.Intern("Function"), RgbRamp());

        System.Action act = () => PdfShading.Parse(dict, new PdfObjectStore());
        act.Should().Throw<ContentException>();
    }

    [Fact]
    public void MissingCoords_Throws()
    {
        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Intern("ShadingType"), 2);
        dict.Set(PdfName.Intern("Function"), RgbRamp());

        System.Action act = () => PdfShading.Parse(dict, new PdfObjectStore());
        act.Should().Throw<ContentException>();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static PdfArray Nums(params double[] values)
    {
        PdfPrimitive[] items = new PdfPrimitive[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            items[i] = new PdfReal(values[i]);
        }
        return new PdfArray(items);
    }

    private static PdfDictionary Axial(PdfArray coords, PdfDictionary function)
    {
        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Intern("ShadingType"), 2);
        dict.Set(PdfName.Intern("ColorSpace"), PdfName.Intern("DeviceRGB"));
        dict.Set(PdfName.Intern("Coords"), coords);
        dict.Set(PdfName.Intern("Function"), function);
        return dict;
    }

    private static PdfDictionary Type2(PdfArray c0, PdfArray c1)
    {
        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Intern("FunctionType"), 2);
        dict.Set(PdfName.Intern("Domain"), Nums(0, 1));
        dict.Set(PdfName.Intern("C0"), c0);
        dict.Set(PdfName.Intern("C1"), c1);
        dict.Set(PdfName.Intern("N"), 1);
        return dict;
    }

    private static PdfDictionary RgbRamp() => Type2(Nums(0, 0, 0), Nums(1, 1, 1));

    private static PdfStream Identity(PdfArray domain, PdfArray range)
    {
        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Intern("FunctionType"), 4);
        dict.Set(PdfName.Intern("Domain"), domain);
        dict.Set(PdfName.Intern("Range"), range);
        byte[] bytes = Encoding.ASCII.GetBytes("{ }");
        dict.Set(PdfName.Intern("Length"), bytes.Length);
        return new PdfStream(dict, bytes);
    }
}
