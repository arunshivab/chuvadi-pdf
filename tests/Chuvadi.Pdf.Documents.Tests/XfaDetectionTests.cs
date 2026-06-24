// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.7.8 (XFA forms), §7.7.2 (/NeedsRendering)
// Regression coverage for PdfDocument.IsXfa / XfaKind — lets consumers detect
// XFA forms (whose content lives outside standard page content) and tell static
// or hybrid (renderable) XFA apart from dynamic XFA, without reaching into the
// catalog themselves.

using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Documents.Tests;

public sealed class XfaDetectionTests
{
    [Fact]
    public void IsXfa_True_WhenAcroFormCarriesXfa()
    {
        using MemoryStream ms = BuildPdf(withXfa: true);
        using PdfDocument doc = OpenPdf(ms);

        doc.IsXfa.Should().BeTrue();
    }

    [Fact]
    public void IsXfa_False_WhenNoAcroForm()
    {
        using MemoryStream ms = BuildPdf(withXfa: false);
        using PdfDocument doc = OpenPdf(ms);

        doc.IsXfa.Should().BeFalse();
    }

    [Fact]
    public void XfaKind_None_WhenNoXfa()
    {
        using MemoryStream ms = BuildPdf(withXfa: false);
        using PdfDocument doc = OpenPdf(ms);

        doc.XfaKind.Should().Be(XfaKind.None);
        doc.IsXfa.Should().Be(doc.XfaKind != XfaKind.None);
    }

    [Fact]
    public void XfaKind_Static_WhenXfaWithoutFieldsOrNeedsRendering()
    {
        using MemoryStream ms = BuildPdf(withXfa: true);
        using PdfDocument doc = OpenPdf(ms);

        doc.XfaKind.Should().Be(XfaKind.Static);
        doc.IsXfa.Should().BeTrue();
    }

    [Fact]
    public void XfaKind_Hybrid_WhenXfaAlongsideTraditionalFields()
    {
        using MemoryStream ms = BuildPdf(withXfa: true, withFields: true);
        using PdfDocument doc = OpenPdf(ms);

        doc.XfaKind.Should().Be(XfaKind.Hybrid);
    }

    [Fact]
    public void XfaKind_Dynamic_WhenCatalogNeedsRendering()
    {
        using MemoryStream ms = BuildPdf(withXfa: true, needsRendering: true);
        using PdfDocument doc = OpenPdf(ms);

        doc.XfaKind.Should().Be(XfaKind.Dynamic);
    }

    [Fact]
    public void XfaKind_Dynamic_TakesPrecedenceOverFields()
    {
        using MemoryStream ms = BuildPdf(withXfa: true, withFields: true, needsRendering: true);
        using PdfDocument doc = OpenPdf(ms);

        doc.XfaKind.Should().Be(XfaKind.Dynamic);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static MemoryStream BuildPdf(
        bool withXfa,
        bool withFields = false,
        bool needsRendering = false)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);
        PdfObjectId acroId = new PdfObjectId(4, 0);

        PdfDictionary catalogDict = new PdfDictionary();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

        if (needsRendering)
        {
            catalogDict.Set(PdfName.Intern("NeedsRendering"), true);
        }

        PdfArray kids = new PdfArray([]);
        kids.Add(new PdfReference(pageId));

        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, kids);
        pagesDict.Set(PdfName.Count, 1);
        pagesDict.Set(PdfName.MediaBox, new PdfArray([
            new PdfInteger(0), new PdfInteger(0),
            new PdfInteger(200), new PdfInteger(200)
        ]));

        PdfDictionary pageDict = new PdfDictionary();
        pageDict.Set(PdfName.Type, PdfName.Page);
        pageDict.Set(PdfName.Parent, new PdfReference(pagesId));

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(catalogId, catalogDict),
            new PdfIndirectObject(pagesId, pagesDict),
            new PdfIndirectObject(pageId, pageDict),
        };

        if (withXfa)
        {
            PdfDictionary acroDict = new PdfDictionary();
            acroDict.Set(PdfName.Intern("XFA"), new PdfArray([]));

            if (withFields)
            {
                acroDict.Set(PdfName.Intern("Fields"), new PdfArray([
                    new PdfReference(pageId)
                ]));
            }

            objects.Add(new PdfIndirectObject(acroId, acroDict));
            catalogDict.Set(PdfName.Intern("AcroForm"), new PdfReference(acroId));
        }

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        return ms;
    }

    private static PdfDocument OpenPdf(MemoryStream ms)
    {
        ms.Seek(0, SeekOrigin.Begin);
        return PdfDocument.Open(ms, leaveOpen: true);
    }
}
