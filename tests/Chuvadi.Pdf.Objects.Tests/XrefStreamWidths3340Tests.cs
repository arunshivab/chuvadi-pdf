// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.5.8.2 (cross-reference stream /W and /Index)
// PHASE: 3.34.0 — a damaged /W or /Index is a PdfParseException (found by
//        the write-paths fuzz target), and 8-byte fields read in full.

using System;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Objects.Tests;

public sealed class XrefStreamWidths3340Tests
{
    [Theory]
    [InlineData("real")]
    [InlineData("negative")]
    [InlineData("too-wide")]
    [InlineData("name")]
    public void Parse_ADamagedWidth_IsAParseError(string damage)
    {
        PdfPrimitive bad = damage switch
        {
            "real" => new PdfReal(1.5),
            "negative" => new PdfInteger(-1),
            "too-wide" => new PdfInteger(9),
            _ => PdfName.Intern("W"),
        };
        PdfDictionary dict = Dict(new PdfArray(new PdfPrimitive[] { new PdfInteger(1), bad, new PdfInteger(1) }), size: 1);

        Action act = () => XrefStreamTable.Parse(dict, new byte[16]);

        act.Should().Throw<PdfParseException>().WithMessage("XRef stream /W entry 1 *");
    }

    [Fact]
    public void Parse_ADamagedIndex_IsAParseError()
    {
        PdfDictionary dict = Dict(new PdfArray(new PdfPrimitive[] { new PdfInteger(1), new PdfInteger(2), new PdfInteger(1) }), size: 1);
        dict.Set(PdfName.Intern("Index"), new PdfArray(new PdfPrimitive[] { new PdfReal(0.5), new PdfInteger(1) }));

        Action act = () => XrefStreamTable.Parse(dict, new byte[4]);

        act.Should().Throw<PdfParseException>().WithMessage("XRef stream /Index entry 0 *");
    }

    [Fact]
    public void Parse_EightByteOffsets_AreReadInFull()
    {
        // One in-use entry at offset 0x0000_0001_0000_0010 (beyond 4 GiB).
        PdfDictionary dict = Dict(new PdfArray(new PdfPrimitive[] { new PdfInteger(1), new PdfInteger(8), new PdfInteger(2) }), size: 1);
        byte[] data = { 1, 0, 0, 0, 1, 0, 0, 0, 0x10, 0, 0 };

        XrefStreamTable table = XrefStreamTable.Parse(dict, data);

        table.Entries[0].ByteOffset.Should().Be(0x1_0000_0010L);
    }

    private static PdfDictionary Dict(PdfArray w, int size)
    {
        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Intern("Type"), PdfName.Intern("XRef"));
        dict.Set(PdfName.Intern("W"), w);
        dict.Set(PdfName.Size, size);
        return dict;
    }
}
