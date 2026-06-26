// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.PdfA.Tests;

internal static class TestPdf
{
    // Builds a minimal one-page PDF whose only font is a non-embedded simple font
    // with the given /BaseFont and WinAnsiEncoding.
    internal static byte[] WithSimpleFont(string baseFont)
    {
        int number = 1;
        List<PdfIndirectObject> objects = new List<PdfIndirectObject>();
        PdfObjectId Allocate() => new PdfObjectId(number++, 0);

        PdfObjectId fontId = Allocate();
        PdfDictionary font = new PdfDictionary();
        font.Set(PdfName.Type, PdfName.Intern("Font"));
        font.Set(PdfName.Intern("Subtype"), PdfName.Intern("Type1"));
        font.Set(PdfName.Intern("BaseFont"), PdfName.Intern(baseFont));
        font.Set(PdfName.Intern("Encoding"), PdfName.Intern("WinAnsiEncoding"));
        objects.Add(new PdfIndirectObject(fontId, font));

        PdfObjectId contentId = Allocate();
        byte[] content = Encoding.ASCII.GetBytes("BT /F1 24 Tf 72 700 Td (PDF/A conformance test) Tj ET");
        objects.Add(new PdfIndirectObject(contentId, new PdfStream(new PdfDictionary(), content)));

        PdfDictionary fontResources = new PdfDictionary();
        fontResources.Set(PdfName.Intern("F1"), new PdfReference(fontId));
        PdfDictionary resources = new PdfDictionary();
        resources.Set(PdfName.Intern("Font"), fontResources);

        PdfObjectId pagesId = Allocate();
        PdfObjectId pageId = Allocate();
        PdfDictionary page = new PdfDictionary();
        page.Set(PdfName.Type, PdfName.Intern("Page"));
        page.Set(PdfName.Intern("Parent"), new PdfReference(pagesId));
        page.Set(PdfName.Intern("MediaBox"), new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0),
            new PdfInteger(0),
            new PdfInteger(612),
            new PdfInteger(792),
        }));
        page.Set(PdfName.Intern("Resources"), resources);
        page.Set(PdfName.Intern("Contents"), new PdfReference(contentId));
        objects.Add(new PdfIndirectObject(pageId, page));

        PdfDictionary pages = new PdfDictionary();
        pages.Set(PdfName.Type, PdfName.Intern("Pages"));
        pages.Set(PdfName.Intern("Kids"), new PdfArray(new PdfPrimitive[] { new PdfReference(pageId) }));
        pages.Set(PdfName.Intern("Count"), 1);
        objects.Add(new PdfIndirectObject(pagesId, pages));

        PdfObjectId catalogId = Allocate();
        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Intern("Catalog"));
        catalog.Set(PdfName.Intern("Pages"), new PdfReference(pagesId));
        objects.Add(new PdfIndirectObject(catalogId, catalog));

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));
        trailer.Set(PdfName.Intern("Size"), number);

        using MemoryStream stream = new MemoryStream();
        PdfWriter.Write(stream, objects, trailer, null);
        return stream.ToArray();
    }
}
