// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.3.4 (CTM), §9.4 (text objects)
// PHASE: 3.36.0 (D-419) — TextOp exposes Ctm, the current transformation
//        matrix (user space → page space) in effect at the text-showing
//        operation, as PathOp.Ctm does for a path. It is the CTM alone, so it
//        differs from Transform (which also folds in the text matrix), letting
//        a vector exporter place stroked-glyph geometry in page space itself.

using System.IO;
using System.Linq;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.DisplayList.Tests;

public sealed class TextOpCtm336Tests
{
    // CTM [2 0 0 2 10 20] from `cm`, then text translated by Td [5 7].
    private const string ScaledText =
        "q\n2 0 0 2 10 20 cm\nBT\n/F1 12 Tf\n5 7 Td\n(Hi) Tj\nET\nQ";

    private const string PlainText = "BT\n/F1 12 Tf\n5 7 Td\n(Hi) Tj\nET";

    [Fact]
    public void Ctm_IsTheTransformInEffectAtTheShowOperation()
    {
        using PdfDocument doc = BuildPage(ScaledText);

        TextOp op = DisplayListBuilder.Build(doc, 0).OfType<TextOp>().Single();

        op.Ctm.A.Should().BeApproximately(2.0, 1e-9);
        op.Ctm.D.Should().BeApproximately(2.0, 1e-9);
        op.Ctm.E.Should().BeApproximately(10.0, 1e-9);
        op.Ctm.F.Should().BeApproximately(20.0, 1e-9);
    }

    [Fact]
    public void Ctm_IsTheCtmAlone_NotTheTextTransform()
    {
        using PdfDocument doc = BuildPage(ScaledText);

        TextOp op = DisplayListBuilder.Build(doc, 0).OfType<TextOp>().Single();

        // Transform folds in the Td translation (×2 by the CTM) → E = 10 + 5*2 = 20,
        // so it must differ from the CTM alone (E = 10).
        op.Transform.E.Should().BeApproximately(20.0, 1e-9);
        op.Ctm.E.Should().NotBe(op.Transform.E);
    }

    [Fact]
    public void Ctm_DefaultsToIdentity_WithNoTransform()
    {
        using PdfDocument doc = BuildPage(PlainText);

        TextOp op = DisplayListBuilder.Build(doc, 0).OfType<TextOp>().Single();

        op.Ctm.A.Should().BeApproximately(1.0, 1e-9);
        op.Ctm.D.Should().BeApproximately(1.0, 1e-9);
        op.Ctm.E.Should().BeApproximately(0.0, 1e-9);
        op.Ctm.F.Should().BeApproximately(0.0, 1e-9);
    }

    // ── Helper ────────────────────────────────────────────────────────────

    private static PdfDocument BuildPage(string content)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);
        PdfObjectId contentId = new PdfObjectId(4, 0);
        PdfObjectId fontId = new PdfObjectId(5, 0);

        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(pagesId));

        PdfDictionary pages = new PdfDictionary();
        pages.Set(PdfName.Type, PdfName.Pages);
        pages.Set(PdfName.Kids, new PdfArray(new PdfPrimitive[] { new PdfReference(pageId) }));
        pages.Set(PdfName.Count, 1);
        pages.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(200), new PdfInteger(200),
        }));

        PdfDictionary font = new PdfDictionary();
        font.Set(PdfName.Type, PdfName.Font);
        font.Set(PdfName.Subtype, PdfName.Intern("Type1"));
        font.Set(PdfName.Intern("BaseFont"), PdfName.Intern("Helvetica"));

        PdfDictionary fonts = new PdfDictionary();
        fonts.Set(PdfName.Intern("F1"), new PdfReference(fontId));
        PdfDictionary resources = new PdfDictionary();
        resources.Set(PdfName.Intern("Font"), fonts);

        PdfDictionary pageDict = new PdfDictionary();
        pageDict.Set(PdfName.Type, PdfName.Page);
        pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
        pageDict.Set(PdfName.Contents, new PdfReference(contentId));
        pageDict.Set(PdfName.Intern("Resources"), resources);

        byte[] bytes = Encoding.ASCII.GetBytes(content);
        PdfDictionary contentDict = new PdfDictionary();
        contentDict.Set(PdfName.Length, bytes.Length);
        PdfStream contentStream = new PdfStream(contentDict, bytes);

        PdfIndirectObject[] objects =
        {
            new PdfIndirectObject(catalogId, catalog),
            new PdfIndirectObject(pagesId, pages),
            new PdfIndirectObject(pageId, pageDict),
            new PdfIndirectObject(contentId, contentStream),
            new PdfIndirectObject(fontId, font),
        };

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        ms.Seek(0, SeekOrigin.Begin);
        return PdfDocument.Open(ms, leaveOpen: true);
    }
}
