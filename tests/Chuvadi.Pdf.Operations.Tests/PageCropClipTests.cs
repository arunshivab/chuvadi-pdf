// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.5.4 (clipping), §8.10.1 (form BBox), §7.7.3 (boxes)
// Tests for clip-on-place and settable compose-time CropBox (LA-08).

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

public sealed class PageCropClipTests
{
    [Fact]
    public void PlacePage_WithDestinationClip_EmitsClipPathInContent()
    {
        using MemoryStream srcStream = BuildTextPdf("ALPHA");
        using PdfDocument source = OpenPdf(srcStream);

        PageComposer composer = new PageComposer();
        composer.AddPage(PageSize.A4);
        PlacePageOptions options = new PlacePageOptions
        {
            DestinationClip = new RectangleF(0, 0, 200, 300),
        };
        composer.PlacePage(source, 0, Transform.Identity, options);

        string content = ComposedBytesAsText(composer);
        content.Should().Contain("0 0 200 300 re", "the destination clip rectangle is emitted");
        content.Should().Contain("W n", "the clip is established with W n");
    }

    [Fact]
    public void PlacePage_WithoutOptions_EmitsNoClipPath()
    {
        using MemoryStream srcStream = BuildTextPdf("ALPHA");
        using PdfDocument source = OpenPdf(srcStream);

        PageComposer composer = new PageComposer();
        composer.AddPage(PageSize.A4);
        composer.PlacePage(source, 0, Transform.Identity);

        string content = ComposedBytesAsText(composer);
        content.Should().NotContain("W n", "no clip must be added when no options are supplied");
    }

    [Fact]
    public void PlacePage_WithSourceClip_SetsFormBBoxToClip()
    {
        using MemoryStream srcStream = BuildTextPdf("ALPHA");
        using PdfDocument source = OpenPdf(srcStream);

        PageComposer composer = new PageComposer();
        composer.AddPage(PageSize.A4);
        PlacePageOptions options = new PlacePageOptions
        {
            SourceClip = new RectangleF(100, 150, 200, 250),
        };
        composer.PlacePage(source, 0, Transform.Identity, options);

        using MemoryStream output = new MemoryStream();
        composer.Write(output);
        using PdfDocument result = OpenPdf(output);

        PdfArray bbox = FormBBox(result, 0, "Fm0");
        bbox.GetNumber(0).Should().BeApproximately(100, 1e-6);
        bbox.GetNumber(1).Should().BeApproximately(150, 1e-6);
        bbox.GetNumber(2).Should().BeApproximately(300, 1e-6, "X + Width");
        bbox.GetNumber(3).Should().BeApproximately(400, 1e-6, "Y + Height");
    }

    [Fact]
    public void SetCropBox_EmitsCropBoxOnComposedPage()
    {
        using MemoryStream srcStream = BuildTextPdf("ALPHA");
        using PdfDocument source = OpenPdf(srcStream);

        PageComposer composer = new PageComposer();
        composer.AddPage(PageSize.A4);
        composer.SetCropBox(new RectangleF(20, 30, 200, 400));
        composer.PlacePage(source, 0, Transform.Identity);

        using MemoryStream output = new MemoryStream();
        composer.Write(output);
        using PdfDocument result = OpenPdf(output);

        PdfRectangle crop = result.Pages[0].CropBox;
        crop.X1.Should().BeApproximately(20, 1e-6);
        crop.Y1.Should().BeApproximately(30, 1e-6);
        crop.Width.Should().BeApproximately(200, 1e-6);
        crop.Height.Should().BeApproximately(400, 1e-6);

        // CropBox must be a true subset, distinct from the full MediaBox.
        result.Pages[0].MediaBox.Width.Should().BeApproximately(PageSize.A4.Width, 0.5);
    }

    [Fact]
    public void SetCropBox_BeforeAddPage_Throws()
    {
        PageComposer composer = new PageComposer();
        Action act = () => composer.SetCropBox(new RectangleF(0, 0, 10, 10));
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void PlacePage_NullOptions_Throws()
    {
        using MemoryStream srcStream = BuildTextPdf("ALPHA");
        using PdfDocument source = OpenPdf(srcStream);

        PageComposer composer = new PageComposer();
        composer.AddPage(PageSize.A4);
        Action act = () => composer.PlacePage(source, 0, Transform.Identity, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static string ComposedBytesAsText(PageComposer composer)
    {
        using MemoryStream output = new MemoryStream();
        composer.Write(output);
        return Encoding.Latin1.GetString(output.ToArray());
    }

    private static PdfArray FormBBox(PdfDocument doc, int pageIndex, string formName)
    {
        PdfDictionary? resources = doc.Pages[pageIndex].Resources;
        resources.Should().NotBeNull();
        PdfDictionary xobjects = (PdfDictionary)doc.Objects.Resolve(resources![PdfName.XObject]);
        PdfStream form = (PdfStream)doc.Objects.Resolve(xobjects[PdfName.Intern(formName)]);
        return (PdfArray)doc.Objects.Resolve(form.Dictionary[PdfName.Intern("BBox")]);
    }

    private static PdfDocument OpenPdf(MemoryStream ms)
    {
        ms.Seek(0, SeekOrigin.Begin);
        return PdfDocument.Open(ms, leaveOpen: true);
    }

    private static MemoryStream BuildTextPdf(string pageText)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);
        PdfObjectId contentId = new PdfObjectId(4, 0);

        byte[] content = Encoding.ASCII.GetBytes($"BT ({pageText}) Tj ET");
        PdfDictionary contentDict = new PdfDictionary();
        contentDict.Set(PdfName.Length, content.Length);

        PdfDictionary pageDict = new PdfDictionary();
        pageDict.Set(PdfName.Type, PdfName.Page);
        pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
        pageDict.Set(PdfName.Contents, new PdfReference(contentId));
        pageDict.Set(PdfName.MediaBox, new PdfArray([
            new PdfInteger(0), new PdfInteger(0),
            new PdfInteger(595), new PdfInteger(842)
        ]));

        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, new PdfArray([new PdfReference(pageId)]));
        pagesDict.Set(PdfName.Count, 1);

        PdfDictionary catalogDict = new PdfDictionary();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

        List<PdfIndirectObject> objects = [
            new PdfIndirectObject(catalogId, catalogDict),
            new PdfIndirectObject(pagesId, pagesDict),
            new PdfIndirectObject(pageId, pageDict),
            new PdfIndirectObject(contentId, new PdfStream(contentDict, content)),
        ];

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        return ms;
    }
}
