// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.5.4 (clipping path operators), §8.4.4 (q/Q)
//
// Verifies RenderOp.Clips: ops carry the active clip stack (outermost-first,
// page-space), nested clips accumulate, and a clip set inside a q/Q scope is
// dropped on Q.

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

public sealed class ClipAccessTests
{
    [Fact]
    public void OpInsideClip_CarriesClipPath_OpOutside_HasNone()
    {
        using PdfDocument doc = BuildPage(
            "q\n50 50 100 100 re W n\n200 200 m 250 250 l S\nQ\n300 300 m 350 350 l S");

        PathOp[] strokes = StrokePaths(doc);
        strokes.Should().HaveCount(2);
        strokes[0].Clips.Should().ContainSingle();
        strokes[1].Clips.Should().BeEmpty();

        Rect clip = PathGeometryAccessors.Bounds(strokes[0].Clips[0]);
        clip.Width.Should().BeApproximately(100, 1e-9);
        clip.Height.Should().BeApproximately(100, 1e-9);
    }

    [Fact]
    public void NestedClips_AccumulateOutermostFirst_AndDropOnRestore()
    {
        using PdfDocument doc = BuildPage(
            "q\n0 0 400 400 re W n\n" +
            "q\n50 50 100 100 re W n\n200 200 m 250 250 l S\n" +
            "Q\n210 210 m 260 260 l S\n" +
            "Q\n220 220 m 270 270 l S");

        PathOp[] strokes = StrokePaths(doc);
        strokes.Should().HaveCount(3);

        strokes[0].Clips.Should().HaveCount(2);
        PathGeometryAccessors.Bounds(strokes[0].Clips[0]).Width.Should().BeApproximately(400, 1e-9);
        PathGeometryAccessors.Bounds(strokes[0].Clips[1]).Width.Should().BeApproximately(100, 1e-9);

        strokes[1].Clips.Should().ContainSingle();
        PathGeometryAccessors.Bounds(strokes[1].Clips[0]).Width.Should().BeApproximately(400, 1e-9);

        strokes[2].Clips.Should().BeEmpty();
    }

    [Fact]
    public void ClipGeometry_IsPageSpace_UnderCtm()
    {
        using PdfDocument doc = BuildPage(
            "q\n2 0 0 2 0 0 cm\n10 10 50 50 re W n\n100 100 m 120 120 l S\nQ");

        PathOp stroke = StrokePaths(doc).Single();
        Rect clip = PathGeometryAccessors.Bounds(stroke.Clips.Single());
        // 10,10 50x50 under scale-2 CTM -> 20,20 100x100 in page space.
        clip.X.Should().BeApproximately(20, 1e-9);
        clip.Y.Should().BeApproximately(20, 1e-9);
        clip.Width.Should().BeApproximately(100, 1e-9);
        clip.Height.Should().BeApproximately(100, 1e-9);
    }

    private static PathOp[] StrokePaths(PdfDocument doc)
        => DisplayListBuilder.Build(doc, 0)
            .OfType<PathOp>()
            .Where(p => p.Mode == PaintMode.Stroke)
            .ToArray();

    private static PdfDocument BuildPage(string content)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);
        PdfObjectId contentId = new PdfObjectId(4, 0);

        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(pagesId));

        PdfDictionary pages = new PdfDictionary();
        pages.Set(PdfName.Type, PdfName.Intern("Pages"));
        pages.Set(PdfName.Kids, new PdfArray(new PdfPrimitive[] { new PdfReference(pageId) }));
        pages.Set(PdfName.Count, 1);
        pages.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(400), new PdfInteger(400),
        }));

        PdfDictionary pageDict = new PdfDictionary();
        pageDict.Set(PdfName.Type, PdfName.Intern("Page"));
        pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
        pageDict.Set(PdfName.Contents, new PdfReference(contentId));
        pageDict.Set(PdfName.Intern("Resources"), new PdfDictionary());

        byte[] contentBytes = Encoding.ASCII.GetBytes(content);
        PdfDictionary contentDict = new PdfDictionary();
        contentDict.Set(PdfName.Length, contentBytes.Length);

        PdfIndirectObject[] objects =
        {
            new PdfIndirectObject(catalogId, catalog),
            new PdfIndirectObject(pagesId, pages),
            new PdfIndirectObject(pageId, pageDict),
            new PdfIndirectObject(contentId, new PdfStream(contentDict, contentBytes)),
        };

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        ms.Seek(0, SeekOrigin.Begin);
        return PdfDocument.Open(ms, leaveOpen: true);
    }
}
