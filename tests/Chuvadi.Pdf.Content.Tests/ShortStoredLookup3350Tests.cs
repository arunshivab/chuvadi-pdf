// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 1951 §3.2.4 (stored blocks); PDF 32000-1:2008 §8.6.6.3 (Indexed)
// PHASE: 3.35.0 (52c) — a stored Deflate block cut short keeps its bytes, and
//        an Indexed /Lookup decoded from it keeps the entries that are there.

using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Content.Tests;

public sealed class ShortStoredLookup3350Tests
{
    // zlib header, then one final stored block that claims 9 bytes (three RGB
    // entries) but holds only 6: red and green, with blue cut off.
    private static readonly byte[] ShortLookup =
    {
        0x78, 0x01,
        0x01, 0x09, 0x00, 0xF6, 0xFF,
        0xFF, 0x00, 0x00, 0x00, 0xFF, 0x00,
    };

    [Fact]
    public void AShortStoredBlock_KeepsTheEntriesThatAreThere()
    {
        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Filter, PdfName.Intern("FlateDecode"));
        PdfStream lookup = new PdfStream(dict, ShortLookup);
        PdfArray array = new PdfArray(new PdfPrimitive[]
        {
            PdfName.Intern("Indexed"), PdfName.Intern("DeviceRGB"), new PdfInteger(2), lookup,
        });

        ResolvedColorSpace cs = ResolvedColorSpace.Parse(array, new PdfObjectStore())!;

        cs.Should().NotBeNull();
        cs.ToRgb(new double[] { 0 }).Should().Equal(1.0, 0.0, 0.0);
        cs.ToRgb(new double[] { 1 }).Should().Equal(0.0, 1.0, 0.0);
        cs.ToRgb(new double[] { 2 }).Should().Equal(0.0, 0.0, 0.0);
    }
}
