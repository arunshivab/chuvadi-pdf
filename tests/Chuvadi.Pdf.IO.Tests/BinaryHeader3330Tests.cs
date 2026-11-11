// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.5.2 (file header); ISO 19005-1 §6.1.2
// PHASE: 3.33.0 (D-318) — the binary comment on line 2 has four bytes above
//        127; before, it was written as "%????".

using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.IO.Tests;

public sealed class BinaryHeader3330Tests
{
    private static readonly byte[] ExpectedHeader =
    {
        (byte)'%', (byte)'P', (byte)'D', (byte)'F', (byte)'-', (byte)'1', (byte)'.', (byte)'7', (byte)'\n',
        (byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n',
    };

    [Fact]
    public void PdfWriter_WritesTheBinaryComment()
    {
        using MemoryStream output = new MemoryStream();
        PdfWriter.Write(output, OnePage(out PdfDictionary trailer), trailer);

        output.ToArray()[..ExpectedHeader.Length].Should().Equal(ExpectedHeader);
    }

    [Fact]
    public void LinearizedWriter_WritesTheBinaryComment()
    {
        using MemoryStream output = new MemoryStream();
        LinearizedWriter.Write(output, OnePage(out PdfDictionary trailer), trailer);

        output.ToArray()[..ExpectedHeader.Length].Should().Equal(ExpectedHeader);
    }

    private static List<PdfIndirectObject> OnePage(out PdfDictionary trailer)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);

        PdfDictionary page = new PdfDictionary();
        page.Set(PdfName.Type, PdfName.Page);
        page.Set(PdfName.Parent, new PdfReference(pagesId));
        page.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0),
            new PdfInteger(0),
            new PdfInteger(612),
            new PdfInteger(792),
        }));

        PdfDictionary pages = new PdfDictionary();
        pages.Set(PdfName.Type, PdfName.Pages);
        pages.Set(PdfName.Kids, new PdfArray(new PdfPrimitive[] { new PdfReference(pageId) }));
        pages.Set(PdfName.Count, 1);

        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(pagesId));

        trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));
        trailer.Set(PdfName.Size, 4);
        return new List<PdfIndirectObject>
        {
            new PdfIndirectObject(catalogId, catalog),
            new PdfIndirectObject(pagesId, pages),
            new PdfIndirectObject(pageId, page),
        };
    }
}
