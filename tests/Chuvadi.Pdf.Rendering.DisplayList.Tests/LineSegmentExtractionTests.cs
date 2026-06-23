// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.5 (path construction & painting), §8.10 (marked content)
//
// Verifies PageDisplayList.ExtractLineSegments: page + raw endpoints, closing
// segments for closed subpaths, layer membership (composes OCG support), dash
// exposure, paint mode, and that finer tolerance subdivides curves more.

using System.Collections.Generic;
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

public sealed class LineSegmentExtractionTests
{
    [Fact]
    public void StraightLines_UnderCtm_CarryPageAndRawEndpoints()
    {
        using PdfDocument doc = BuildPage("2 0 0 2 10 20 cm\n5 5 m\n15 5 l\n25 15 l\nS");

        IReadOnlyList<LineSegment> segs = Extract(doc);
        segs.Should().HaveCount(2);

        LineSegment a = segs[0];
        // raw (5,5)->(15,5); page = 2*raw + (10,20)
        a.RawX0.Should().BeApproximately(5, 1e-9);
        a.RawY0.Should().BeApproximately(5, 1e-9);
        a.RawX1.Should().BeApproximately(15, 1e-9);
        a.RawY1.Should().BeApproximately(5, 1e-9);
        a.X0.Should().BeApproximately(20, 1e-9);
        a.Y0.Should().BeApproximately(30, 1e-9);
        a.X1.Should().BeApproximately(40, 1e-9);
        a.Y1.Should().BeApproximately(30, 1e-9);
        a.Mode.Should().Be(PaintMode.Stroke);
        a.Width.Should().BeApproximately(2.0, 1e-9); // default line width 1 * CTM scale 2
        a.Layers.Should().BeEmpty();
        a.Dash.Should().BeNull();

        LineSegment b = segs[1];
        b.RawX1.Should().BeApproximately(25, 1e-9);
        b.RawY1.Should().BeApproximately(15, 1e-9);
        b.X1.Should().BeApproximately(60, 1e-9); // 2*25+10
        b.Y1.Should().BeApproximately(50, 1e-9); // 2*15+20
    }

    [Fact]
    public void ClosedRectangle_YieldsClosingSegment()
    {
        using PdfDocument doc = BuildPage("10 10 20 20 re\nf");

        IReadOnlyList<LineSegment> segs = Extract(doc);

        // re -> MoveTo,3x LineTo,Close; flattened polyline has 5 points -> 4 segments.
        segs.Should().HaveCount(4);
        segs.Should().OnlyContain(s => s.Mode == PaintMode.Fill);
        segs.Should().OnlyContain(s => s.Width == 0.0);

        // closed: last segment ends where the first starts.
        LineSegment first = segs[0];
        LineSegment last = segs[^1];
        last.RawX1.Should().BeApproximately(first.RawX0, 1e-9);
        last.RawY1.Should().BeApproximately(first.RawY0, 1e-9);
    }

    [Fact]
    public void Segments_CarryOptionalContentLayers()
    {
        using PdfDocument doc = BuildPage(
            "/OC /MC0 BDC\n5 5 m\n15 5 l\nS\nEMC",
            ocgNames: new[] { "Wall" },
            properties: new[] { ("MC0", 0) });

        IReadOnlyList<LineSegment> segs = Extract(doc);

        segs.Should().HaveCount(1);
        segs[0].Layers.Should().Equal("Wall");
    }

    [Fact]
    public void DashedStroke_ExposesDashPattern()
    {
        using PdfDocument dashed = BuildPage("[3 2] 0 d\n5 5 m\n15 5 l\nS");
        LineSegment d = Extract(dashed).Single();
        d.Dash.Should().NotBeNull();
        d.Dash!.Should().Equal(3.0, 2.0);

        using PdfDocument solid = BuildPage("5 5 m\n15 5 l\nS");
        Extract(solid).Single().Dash.Should().BeNull();
    }

    [Fact]
    public void FinerTolerance_SubdividesCurvesMore()
    {
        const string curve = "0 0 m\n0 100 100 100 100 0 c\nS";

        int coarse = BuildAndCount(curve, 5.0);
        int fine = BuildAndCount(curve, 0.05);

        fine.Should().BeGreaterThan(coarse);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static IReadOnlyList<LineSegment> Extract(PdfDocument doc)
        => DisplayListBuilder.Build(doc, 0).ExtractLineSegments();

    private static int BuildAndCount(string content, double tolerance)
    {
        using PdfDocument doc = BuildPage(content);
        return DisplayListBuilder.Build(doc, 0).ExtractLineSegments(tolerance).Count;
    }

    // Builds a one-page PDF with the given content stream. When ocgNames/
    // properties are supplied, declares OCGs and /Resources/Properties so that
    // `/OC /<propName> BDC` resolves to an OCG name.
    private static PdfDocument BuildPage(
        string content,
        string[]? ocgNames = null,
        (string PropName, int OcgIndex)[]? properties = null)
    {
        ocgNames ??= System.Array.Empty<string>();
        properties ??= System.Array.Empty<(string, int)>();

        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);
        PdfObjectId contentId = new PdfObjectId(4, 0);

        PdfObjectId[] ocgIds = new PdfObjectId[ocgNames.Length];
        List<PdfPrimitive> ocgRefs = new List<PdfPrimitive>();
        List<PdfIndirectObject> objects = new List<PdfIndirectObject>();
        for (int i = 0; i < ocgNames.Length; i++)
        {
            ocgIds[i] = new PdfObjectId(5 + i, 0);
            PdfDictionary ocg = new PdfDictionary();
            ocg.Set(PdfName.Type, PdfName.Intern("OCG"));
            ocg.Set(PdfName.Intern("Name"), new PdfString(Encoding.Latin1.GetBytes(ocgNames[i])));
            objects.Add(new PdfIndirectObject(ocgIds[i], ocg));
            ocgRefs.Add(new PdfReference(ocgIds[i]));
        }

        PdfDictionary catalogDict = new PdfDictionary();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));
        if (ocgNames.Length > 0)
        {
            PdfDictionary ocProps = new PdfDictionary();
            ocProps.Set(PdfName.Intern("OCGs"), new PdfArray(ocgRefs));
            PdfDictionary cfg = new PdfDictionary();
            cfg.Set(PdfName.Intern("BaseState"), PdfName.Intern("ON"));
            ocProps.Set(PdfName.Intern("D"), cfg);
            catalogDict.Set(PdfName.Intern("OCProperties"), ocProps);
        }

        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, new PdfArray(new PdfPrimitive[] { new PdfReference(pageId) }));
        pagesDict.Set(PdfName.Count, 1);
        pagesDict.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(400), new PdfInteger(400),
        }));

        PdfDictionary resources = new PdfDictionary();
        if (properties.Length > 0)
        {
            PdfDictionary props = new PdfDictionary();
            foreach ((string propName, int ocgIndex) in properties)
            {
                props.Set(PdfName.Intern(propName), new PdfReference(ocgIds[ocgIndex]));
            }
            resources.Set(PdfName.Intern("Properties"), props);
        }

        PdfDictionary pageDict = new PdfDictionary();
        pageDict.Set(PdfName.Type, PdfName.Page);
        pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
        pageDict.Set(PdfName.Contents, new PdfReference(contentId));
        pageDict.Set(PdfName.Intern("Resources"), resources);

        byte[] contentBytes = Encoding.ASCII.GetBytes(content);
        PdfDictionary contentDict = new PdfDictionary();
        contentDict.Set(PdfName.Length, contentBytes.Length);
        PdfStream contentStream = new PdfStream(contentDict, contentBytes);

        objects.Insert(0, new PdfIndirectObject(catalogId, catalogDict));
        objects.Insert(1, new PdfIndirectObject(pagesId, pagesDict));
        objects.Insert(2, new PdfIndirectObject(pageId, pageDict));
        objects.Insert(3, new PdfIndirectObject(contentId, contentStream));

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        ms.Seek(0, SeekOrigin.Begin);
        return PdfDocument.Open(ms, leaveOpen: true);
    }
}
