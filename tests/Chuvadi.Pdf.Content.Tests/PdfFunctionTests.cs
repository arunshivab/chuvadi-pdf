// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.10 — Functions
// PHASE: Phase 2 — rendering conformance
// Exact-numeric coverage for the four PDF function types plus the array form.

using System.Text;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Content.Tests;

public sealed class PdfFunctionTests
{
    [Fact]
    public void Type2_LinearInterpolation_MapsMidpoint()
    {
        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Intern("FunctionType"), 2);
        dict.Set(PdfName.Intern("Domain"), Nums(0, 1));
        dict.Set(PdfName.Intern("C0"), Nums(0));
        dict.Set(PdfName.Intern("C1"), Nums(1));
        dict.Set(PdfName.Intern("N"), 1);

        PdfFunction fn = PdfFunction.Parse(dict, new PdfObjectStore());

        fn.OutputCount.Should().Be(1);
        fn.Evaluate(In(0.5))[0].Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void Type2_NonLinearExponent_AppliesPower()
    {
        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Intern("FunctionType"), 2);
        dict.Set(PdfName.Intern("Domain"), Nums(0, 1));
        dict.Set(PdfName.Intern("C0"), Nums(0));
        dict.Set(PdfName.Intern("C1"), Nums(1));
        dict.Set(PdfName.Intern("N"), 2);

        PdfFunction fn = PdfFunction.Parse(dict, new PdfObjectStore());

        // x^2 at 0.5 = 0.25
        fn.Evaluate(In(0.5))[0].Should().BeApproximately(0.25, 1e-9);
    }

    [Fact]
    public void Type0_TwoSamples_InterpolatesMidpoint()
    {
        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Intern("FunctionType"), 0);
        dict.Set(PdfName.Intern("Domain"), Nums(0, 1));
        dict.Set(PdfName.Intern("Range"), Nums(0, 1));
        dict.Set(PdfName.Intern("Size"), new PdfArray(new PdfPrimitive[] { new PdfInteger(2) }));
        dict.Set(PdfName.Intern("BitsPerSample"), 8);
        dict.Set(PdfName.Intern("Length"), 2);

        // samples: 0x00 -> 0.0, 0xFF -> 1.0 ; midpoint -> 0.5
        PdfStream stream = new PdfStream(dict, new byte[] { 0x00, 0xFF });
        PdfFunction fn = PdfFunction.Parse(stream, new PdfObjectStore());

        fn.Evaluate(In(0.0))[0].Should().BeApproximately(0.0, 1e-6);
        fn.Evaluate(In(1.0))[0].Should().BeApproximately(1.0, 1e-6);
        fn.Evaluate(In(0.5))[0].Should().BeApproximately(0.5, 1e-6);
    }

    [Fact]
    public void Type0_TwoOutputs_ReadsInterleavedSamples()
    {
        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Intern("FunctionType"), 0);
        dict.Set(PdfName.Intern("Domain"), Nums(0, 1));
        dict.Set(PdfName.Intern("Range"), Nums(0, 1, 0, 1));
        dict.Set(PdfName.Intern("Size"), new PdfArray(new PdfPrimitive[] { new PdfInteger(2) }));
        dict.Set(PdfName.Intern("BitsPerSample"), 8);
        dict.Set(PdfName.Intern("Length"), 4);

        // sample0 = (0x00, 0xFF), sample1 = (0xFF, 0x00)
        PdfStream stream = new PdfStream(dict, new byte[] { 0x00, 0xFF, 0xFF, 0x00 });
        PdfFunction fn = PdfFunction.Parse(stream, new PdfObjectStore());

        fn.OutputCount.Should().Be(2);
        double[] result = fn.Evaluate(In(0.0));
        result[0].Should().BeApproximately(0.0, 1e-6);
        result[1].Should().BeApproximately(1.0, 1e-6);
    }

    [Fact]
    public void Type3_Stitching_SelectsSubfunctionByBound()
    {
        PdfObjectStore store = new PdfObjectStore();
        PdfDictionary stitch = new PdfDictionary();
        stitch.Set(PdfName.Intern("FunctionType"), 3);
        stitch.Set(PdfName.Intern("Domain"), Nums(0, 1));
        stitch.Set(PdfName.Intern("Functions"), new PdfArray(new PdfPrimitive[]
        {
            Type2(0, 1, 1),
            Type2(1, 2, 1),
        }));
        stitch.Set(PdfName.Intern("Bounds"), Nums(0.5));
        stitch.Set(PdfName.Intern("Encode"), Nums(0, 1, 0, 1));

        PdfFunction fn = PdfFunction.Parse(stitch, store);

        // x=0.25 -> sub0 at encoded 0.5 -> 0.5
        fn.Evaluate(In(0.25))[0].Should().BeApproximately(0.5, 1e-9);
        // x=0.75 -> sub1 at encoded 0.5 -> 1.5
        fn.Evaluate(In(0.75))[0].Should().BeApproximately(1.5, 1e-9);
    }

    [Fact]
    public void Type4_Multiply_EvaluatesProgram()
    {
        PdfFunction fn = BuildPs("{ 2 mul }", Nums(0, 1), Nums(0, 2));
        fn.Evaluate(In(0.5))[0].Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void Type4_DupMul_SquaresInput()
    {
        PdfFunction fn = BuildPs("{ dup mul }", Nums(0, 1), Nums(0, 1));
        fn.Evaluate(In(0.3))[0].Should().BeApproximately(0.09, 1e-9);
    }

    [Fact]
    public void Type4_IfElse_SelectsBranch()
    {
        PdfFunction fn = BuildPs("{ 2 1 gt { 10 } { 20 } ifelse }", Nums(0, 1), Nums(0, 100));
        fn.Evaluate(In(0.5))[0].Should().BeApproximately(10.0, 1e-9);
    }

    [Fact]
    public void Type4_Exch_SwapsTwoInputs()
    {
        PdfFunction fn = BuildPs("{ exch }", Nums(0, 1, 0, 1), Nums(0, 1, 0, 1));
        double[] result = fn.Evaluate(In(0.2, 0.8));
        result[0].Should().BeApproximately(0.8, 1e-9);
        result[1].Should().BeApproximately(0.2, 1e-9);
    }

    [Fact]
    public void ArrayFunction_CombinesSingleOutputs()
    {
        PdfObjectStore store = new PdfObjectStore();
        PdfArray array = new PdfArray(new PdfPrimitive[]
        {
            Type2(0, 1, 1),
            Type2(1, 0, 1),
        });

        PdfFunction fn = PdfFunction.Parse(array, store);

        fn.OutputCount.Should().Be(2);
        double[] result = fn.Evaluate(In(0.25));
        result[0].Should().BeApproximately(0.25, 1e-9);
        result[1].Should().BeApproximately(0.75, 1e-9);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static double[] In(params double[] values) => values;

    private static PdfArray Nums(params double[] values)
    {
        PdfPrimitive[] items = new PdfPrimitive[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            items[i] = new PdfReal(values[i]);
        }
        return new PdfArray(items);
    }

    private static PdfDictionary Type2(double c0, double c1, double n)
    {
        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Intern("FunctionType"), 2);
        dict.Set(PdfName.Intern("Domain"), Nums(0, 1));
        dict.Set(PdfName.Intern("C0"), Nums(c0));
        dict.Set(PdfName.Intern("C1"), Nums(c1));
        dict.Set(PdfName.Intern("N"), new PdfReal(n));
        return dict;
    }

    private static PdfFunction BuildPs(string program, PdfArray domain, PdfArray range)
    {
        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Intern("FunctionType"), 4);
        dict.Set(PdfName.Intern("Domain"), domain);
        dict.Set(PdfName.Intern("Range"), range);
        byte[] bytes = Encoding.ASCII.GetBytes(program);
        dict.Set(PdfName.Intern("Length"), bytes.Length);
        PdfStream stream = new PdfStream(dict, bytes);
        return PdfFunction.Parse(stream, new PdfObjectStore());
    }
}
