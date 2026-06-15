// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.10.1 (form XObjects), §8.3.3-§8.3.4 (transforms)
// Tests for page composition: PageComposer (new-doc), PageStamper
// (existing-doc overlay/underlay), and the Placement transform helpers.

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
using Chuvadi.Pdf.Text;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

public sealed class PageCompositionTests
{
    // ── PageComposer ────────────────────────────────────────────────────────

    [Fact]
    public void Compose_TwoUpOnOneSheet_ProducesOneLargerPageWithBothForms()
    {
        using MemoryStream srcStream = BuildTextPdf("ALPHAPAGE", "BETAPAGE");
        using PdfDocument source = OpenPdf(srcStream);

        double w = source.Pages[0].EffectiveSize.Width;

        PageComposer composer = new PageComposer();
        composer.AddPage(PageSize.A3.Height, PageSize.A3.Width); // A3 landscape
        composer.PlacePage(source, 0, Transform.CreateTranslation(0, 0));
        composer.PlacePage(source, 1, Transform.CreateTranslation(w, 0));

        using MemoryStream output = new MemoryStream();
        composer.Write(output);

        using PdfDocument result = OpenPdf(output);
        result.PageCount.Should().Be(1);
        result.Pages[0].Width.Should().BeApproximately(PageSize.A3.Height, 0.5);

        PdfDictionary xobjects = XObjectsOf(result, 0);
        xobjects.ContainsKey(PdfName.Intern("Fm0")).Should().BeTrue();
        xobjects.ContainsKey(PdfName.Intern("Fm1")).Should().BeTrue();
    }

    [Fact]
    public void Compose_PlacedText_IsExtractable()
    {
        using MemoryStream srcStream = BuildTextPdf("ALPHAPAGE", "BETAPAGE");
        using PdfDocument source = OpenPdf(srcStream);

        double w = source.Pages[0].EffectiveSize.Width;

        PageComposer composer = new PageComposer();
        composer.AddPage(PageSize.A3.Height, PageSize.A3.Width);
        composer.PlacePage(source, 0, Transform.CreateTranslation(0, 0));
        composer.PlacePage(source, 1, Transform.CreateTranslation(w, 0));

        using MemoryStream output = new MemoryStream();
        composer.Write(output);

        using PdfDocument result = OpenPdf(output);
        string text = new TextExtractor(result.Objects).ExtractText(result.Pages[0]);

        text.Should().Contain("ALPHAPAGE").And.Contain("BETAPAGE");
    }

    [Fact]
    public void Compose_ResizeOntoCustomSheet_KeepsTextAndTargetSize()
    {
        using MemoryStream srcStream = BuildTextPdf("RESIZEME");
        using PdfDocument source = OpenPdf(srcStream);

        (double sw, double sh) = source.Pages[0].EffectiveSize;

        PageComposer composer = new PageComposer();
        composer.AddPage(300, 400); // arbitrary, non-standard size
        composer.PlacePage(source, 0, Placement.ScaleToFit(sw, sh, 300, 400));

        using MemoryStream output = new MemoryStream();
        composer.Write(output);

        using PdfDocument result = OpenPdf(output);
        result.Pages[0].Width.Should().BeApproximately(300, 0.5);
        result.Pages[0].Height.Should().BeApproximately(400, 0.5);
        new TextExtractor(result.Objects).ExtractText(result.Pages[0])
            .Should().Contain("RESIZEME");
    }

    [Fact]
    public void Compose_WithoutAddPage_Throws()
    {
        using MemoryStream srcStream = BuildTextPdf("X");
        using PdfDocument source = OpenPdf(srcStream);

        PageComposer composer = new PageComposer();
        Action act = () => composer.PlacePage(source, 0, Transform.Identity);

        act.Should().Throw<InvalidOperationException>();
    }

    // ── PageStamper ─────────────────────────────────────────────────────────

    [Fact]
    public void Stamp_Overlay_PreservesOriginalTextAndAddsStamp()
    {
        using MemoryStream targetStream = BuildTextPdf("TARGETA", "TARGETB", "TARGETC");
        using MemoryStream sourceStream = BuildTextPdf("STAMPMARK");
        using PdfDocument target = OpenPdf(targetStream);
        using PdfDocument source = OpenPdf(sourceStream);

        using MemoryStream output = new MemoryStream();
        PageStamper.Place(output, target, 1, source, 0,
            Transform.CreateScale(0.25), StampPlacement.Overlay);

        using PdfDocument result = OpenPdf(output);
        result.PageCount.Should().Be(3);

        TextExtractor extractor = new TextExtractor(result.Objects);
        string stamped = extractor.ExtractText(result.Pages[1]);
        stamped.Should().Contain("TARGETB", "the original content is preserved");
        stamped.Should().Contain("STAMPMARK", "the stamp text is added");

        extractor.ExtractText(result.Pages[0]).Should().NotContain("STAMPMARK",
            "unstamped pages are untouched");
    }

    [Fact]
    public void Stamp_PlaceOnAll_StampsEveryPage()
    {
        using MemoryStream targetStream = BuildTextPdf("ONE", "TWO", "THREE");
        using MemoryStream sourceStream = BuildTextPdf("SEAL");
        using PdfDocument target = OpenPdf(targetStream);
        using PdfDocument source = OpenPdf(sourceStream);

        using MemoryStream output = new MemoryStream();
        PageStamper.PlaceOnAll(output, target, source, 0,
            Transform.CreateScale(0.2), StampPlacement.Underlay);

        using PdfDocument result = OpenPdf(output);
        TextExtractor extractor = new TextExtractor(result.Objects);
        for (int i = 0; i < result.PageCount; i++)
        {
            extractor.ExtractText(result.Pages[i]).Should().Contain("SEAL");
        }
    }

    // ── Placement ───────────────────────────────────────────────────────────

    [Fact]
    public void ScaleToFit_FitsWithinAndCentres()
    {
        // 100x50 into 400x400 -> uniform scale 4, centred vertically.
        Transform t = Placement.ScaleToFit(100, 50, 400, 400);
        t.A.Should().BeApproximately(4, 1e-9);
        t.D.Should().BeApproximately(4, 1e-9);
        t.F.Should().BeApproximately((400 - 50 * 4) / 2.0, 1e-9); // = 100
    }

    [Fact]
    public void RotatedSize_NinetyDegrees_SwapsDimensions()
    {
        (double width, double height) = Placement.RotatedSize(90, 200, 100);
        width.Should().BeApproximately(100, 1e-6);
        height.Should().BeApproximately(200, 1e-6);
    }

    [Fact]
    public void RotateIntoBox_KeepsContentInPositiveQuadrant()
    {
        Transform t = Placement.RotateIntoBox(90, 200, 100);
        // All four corners of [0,200]x[0,100] must map to >= 0 coordinates.
        ReadOnlySpan<(double X, double Y)> corners = stackalloc (double, double)[]
        {
            (0, 0), (200, 0), (0, 100), (200, 100)
        };

        foreach ((double cx, double cy) in corners)
        {
            PointF p = t.TransformPoint(new PointF(cx, cy));
            p.X.Should().BeGreaterThanOrEqualTo(-1e-6);
            p.Y.Should().BeGreaterThanOrEqualTo(-1e-6);
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static PdfDocument OpenPdf(MemoryStream ms)
    {
        ms.Seek(0, SeekOrigin.Begin);
        return PdfDocument.Open(ms, leaveOpen: true);
    }

    private static PdfDictionary XObjectsOf(PdfDocument doc, int pageIndex)
    {
        PdfDictionary? resources = doc.Pages[pageIndex].Resources;
        resources.Should().NotBeNull();
        PdfPrimitive xobj = doc.Objects.Resolve(resources![PdfName.XObject]);
        return (PdfDictionary)xobj;
    }

    private static MemoryStream BuildTextPdf(params string[] pageTexts)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);

        PdfArray kids = new PdfArray([]);
        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, kids);
        pagesDict.Set(PdfName.Count, pageTexts.Length);
        pagesDict.Set(PdfName.MediaBox, new PdfArray([
            new PdfInteger(0), new PdfInteger(0),
            new PdfInteger(595), new PdfInteger(842)
        ]));

        PdfDictionary catalogDict = new PdfDictionary();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(catalogId, catalogDict),
            new PdfIndirectObject(pagesId, pagesDict),
        };

        int next = 3;
        foreach (string pageText in pageTexts)
        {
            PdfObjectId pageId = new PdfObjectId(next++, 0);
            PdfObjectId contentId = new PdfObjectId(next++, 0);

            byte[] content = Encoding.ASCII.GetBytes($"BT ({pageText}) Tj ET");
            PdfDictionary contentDict = new PdfDictionary();
            contentDict.Set(PdfName.Length, content.Length);

            PdfDictionary pageDict = new PdfDictionary();
            pageDict.Set(PdfName.Type, PdfName.Page);
            pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
            pageDict.Set(PdfName.Contents, new PdfReference(contentId));

            objects.Add(new PdfIndirectObject(pageId, pageDict));
            objects.Add(new PdfIndirectObject(contentId, new PdfStream(contentDict, content)));
            kids.Add(new PdfReference(pageId));
        }

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        return ms;
    }
}
