// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.3.3 (numeric objects)
// PHASE: 3.32.0 (D-278) — integers outside the 32-bit range are read as
//        real numbers instead of failing the object, and reals are written
//        without an exponent.

using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.IO.Tests;

public sealed class LargeIntegers3320Tests
{
    [Fact]
    public void IntegersOutside32Bits_AreRead_AsReals()
    {
        PdfDictionary page = OpenPage("/A 4294963392 /B -2147483649 /C 2147483647 /D -2147483648 /E [99999999999 1]");

        page[PdfName.Intern("A")].Should().BeOfType<PdfReal>().Which.Value.Should().Be(4294963392d);
        page[PdfName.Intern("B")].Should().BeOfType<PdfReal>().Which.Value.Should().Be(-2147483649d);
        page[PdfName.Intern("C")].Should().BeOfType<PdfInteger>().Which.Value.Should().Be(int.MaxValue);
        page[PdfName.Intern("D")].Should().BeOfType<PdfInteger>().Which.Value.Should().Be(int.MinValue);
        PdfArray e = (PdfArray)page[PdfName.Intern("E")];
        e[0].Should().BeOfType<PdfReal>().Which.Value.Should().Be(99999999999d);
        e[1].Should().BeOfType<PdfInteger>();
    }

    [Fact]
    public void PageWithALargeInteger_StillOpens()
    {
        // Before 3.32.0 the page object failed to parse ("must be a valid
        // 32-bit signed integer").
        OpenPage("/X 4294963392").Should().ContainKey(PdfName.Intern("MediaBox"));
    }

    [Theory]
    [InlineData(4294963392d, "4294963392.0")]
    [InlineData(1000000d, "1000000.0")]
    [InlineData(-2147483649d, "-2147483649.0")]
    [InlineData(0.00001d, "0.00001")]
    [InlineData(1.5d, "1.5")]
    [InlineData(12.0d, "12.0")]
    public void Reals_AreWritten_WithoutAnExponent(double value, string expected)
    {
        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Intern("V"), new PdfReal(value));
        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(1, 0));
        using MemoryStream output = new MemoryStream();
        PdfWriter.Write(output, new List<PdfIndirectObject> { new PdfIndirectObject(new PdfObjectId(1, 0), dict) }, trailer);

        string text = Encoding.ASCII.GetString(output.ToArray());
        text.Should().Contain("/V " + expected + "\n").And.NotContain("E+").And.NotContain("E-");
    }

    [Fact]
    public void LargeIntegerRead_RoundTripsThroughTheWriter()
    {
        PdfDictionary page = OpenPage("/X 4294963392");
        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Intern("X"), page[PdfName.Intern("X")]);
        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(1, 0));
        using MemoryStream output = new MemoryStream();
        PdfWriter.Write(output, new List<PdfIndirectObject> { new PdfIndirectObject(new PdfObjectId(1, 0), dict) }, trailer);

        Encoding.ASCII.GetString(output.ToArray()).Should().Contain("/X 4294963392");
    }

    private static PdfDictionary OpenPage(string extra)
    {
        List<string> objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] " + extra + " >>",
        };
        StringBuilder file = new StringBuilder("%PDF-1.4\n");
        List<int> offsets = new List<int>();
        for (int i = 0; i < objects.Count; i++)
        {
            offsets.Add(file.Length);
            file.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");
        }

        int xref = file.Length;
        file.Append("xref\n0 4\n0000000000 65535 f \n");
        foreach (int offset in offsets)
        {
            file.Append(offset.ToString("D10", System.Globalization.CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        file.Append("trailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        PdfDocument doc = PdfDocument.Open(new MemoryStream(Encoding.ASCII.GetBytes(file.ToString())), leaveOpen: false);
        return doc.Pages[0].Dictionary;
    }
}
