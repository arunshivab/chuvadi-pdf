// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// Tests for v3.20: a /ToUnicode CMap stream is decoded through its whole
// /Filter chain (an array of filters, not only a single name).

using System;
using System.Text;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Fonts.Tests;

public sealed class ToUnicodeFilterChainTests
{
    private const string CMap = "/CIDInit /ProcSet findresource begin\nbeginbfchar\n<41> <0058>\nendbfchar\nend";

    [Fact]
    public void FilterArray_IsDecodedInOrder()
    {
        string hex = Convert.ToHexString(Encoding.ASCII.GetBytes(CMap)) + ">";
        PdfDictionary streamDict = new PdfDictionary();
        streamDict.Set(PdfName.Filter, new PdfArray { PdfName.Intern("ASCIIHexDecode") });
        PdfStream toUnicode = new PdfStream(streamDict, Encoding.ASCII.GetBytes(hex));

        PdfDictionary fontDict = new PdfDictionary();
        fontDict.Set(PdfName.Intern("Type"), PdfName.Intern("Font"));
        fontDict.Set(PdfName.Intern("Subtype"), PdfName.Intern("Type1"));
        fontDict.Set(PdfName.Intern("BaseFont"), PdfName.Intern("Helvetica"));
        fontDict.Set(PdfName.Intern("ToUnicode"), toUnicode);

        PdfFont font = PdfFont.FromDictionary(fontDict, new DirectResolver());

        font.Decode(new byte[] { 0x41 }).Should().Be("X");
    }

    private sealed class DirectResolver : IPdfObjectResolver
    {
        public PdfPrimitive Resolve(PdfPrimitive primitive) => primitive;

        public PdfPrimitive ResolveById(PdfObjectId id) => PdfNull.Value;

        public bool Contains(PdfObjectId id) => false;
    }
}
