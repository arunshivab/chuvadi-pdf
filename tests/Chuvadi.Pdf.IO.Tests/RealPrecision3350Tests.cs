// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.3.3 (numeric objects)
// PHASE: 3.35.0 (backlog 66, D-385) — reals keep up to 9 significant digits
//        when 6 would change them beyond single precision.

using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.IO.Tests;

public sealed class RealPrecision3350Tests
{
    [Theory]
    [InlineData(1.0833334d, "1.0833334")]       // issue18529's /BBox value
    [InlineData(0.100000001490116d, "0.1")]     // 0.1f widened to double
    [InlineData(612.5d, "612.5")]
    [InlineData(0.333333333333d, "0.3333333")]  // single precision at 7 digits
    [InlineData(1.00000012d, "1.0000001")]      // the float just above 1 needs 8
    [InlineData(123456.75d, "123456.75")]
    public void Reals_KeepSinglePrecision_InTheFewestDigits(double value, string expected)
    {
        Write(value).Should().Contain("/V " + expected + "\n");
    }

    [Fact]
    public void ARealWrittenAndReadBack_KeepsSinglePrecision()
    {
        string text = Write(1.0833334d);
        int start = text.IndexOf("/V ", System.StringComparison.Ordinal) + 3;
        string written = text.Substring(start, text.IndexOf('\n', start) - start);

        double back = double.Parse(written, System.Globalization.CultureInfo.InvariantCulture);
        ((float)back).Should().Be((float)1.0833334d);
    }

    private static string Write(double value)
    {
        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Intern("V"), new PdfReal(value));
        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(1, 0));
        using MemoryStream output = new MemoryStream();
        PdfWriter.Write(output, new List<PdfIndirectObject> { new PdfIndirectObject(new PdfObjectId(1, 0), dict) }, trailer);
        return Encoding.ASCII.GetString(output.ToArray());
    }
}
