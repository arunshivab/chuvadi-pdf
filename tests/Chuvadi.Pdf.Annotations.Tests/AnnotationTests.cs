// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1 — Chuvadi.Pdf.Annotations tests
//        v2.0.0 R3 — adds ShapeAnnotation coverage and PdfDocument.GetAnnotations()

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Annotations.Tests;

// ── AnnotationException ────────────────────────────────────────────────────

public sealed class AnnotationExceptionTests
{
    [Fact]
    public void Default_HasMessage()
    {
        new AnnotationException().Message.Should().NotBeEmpty();
    }

    [Fact]
    public void Message_Preserved()
    {
        new AnnotationException("bad").Message.Should().Be("bad");
    }

    [Fact]
    public void Inner_Preserved()
    {
        Exception inner = new InvalidOperationException("x");
        new AnnotationException("outer", inner).InnerException.Should().BeSameAs(inner);
    }
}

// ── Model constructors ─────────────────────────────────────────────────────

public sealed class PdfAnnotationTests
{
    [Fact]
    public void Text_ConstructsWithDefaults()
    {
        TextAnnotation t = new(0, new RectangleF(10, 10, 20, 20), "hello");
        t.Type.Should().Be(AnnotationType.Text);
        t.Contents.Should().Be("hello");
        t.IconName.Should().Be("Note");
        t.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void Text_NegativePageIndex_Throws()
    {
        Action act = () => new TextAnnotation(-1, new RectangleF(0, 0, 1, 1), "x");
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Link_UriCtor_StoresUri()
    {
        Uri uri = new("https://example.com");
        LinkAnnotation l = new(0, new RectangleF(0, 0, 10, 10), uri);
        l.Uri.Should().Be(uri);
        l.DestinationPageIndex.Should().Be(-1);
    }

    [Fact]
    public void Link_NullUri_Throws()
    {
        Action act = () => new LinkAnnotation(0, new RectangleF(0, 0, 10, 10), (Uri)null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Link_PageDestination_StoresIndex()
    {
        LinkAnnotation l = new(0, new RectangleF(0, 0, 10, 10), destinationPageIndex: 3);
        l.DestinationPageIndex.Should().Be(3);
        l.Uri.Should().BeNull();
    }

    [Fact]
    public void Markup_BadQuadPointsLength_Throws()
    {
        Action act = () => new MarkupAnnotation(
            AnnotationType.Highlight, 0, new RectangleF(0, 0, 10, 10),
            new float[] { 1, 2, 3 });
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Markup_BadType_Throws()
    {
        Action act = () => new MarkupAnnotation(
            AnnotationType.Text, 0, new RectangleF(0, 0, 10, 10),
            new float[] { 0, 0, 1, 0, 1, 1, 0, 1 });
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Ink_EmptyStrokes_Throws()
    {
        Action act = () => new InkAnnotation(0, new RectangleF(0, 0, 10, 10),
            new List<IReadOnlyList<PointF>>());
        act.Should().Throw<ArgumentException>();
    }
}

// ── ShapeAnnotation ───────────────────────────────────────────────────────

public sealed class ShapeAnnotationTests
{
    [Fact]
    public void Square_ConstructsWithEmptyVertices()
    {
        ShapeAnnotation s = new(
            ShapeKind.Square, 0,
            new RectangleF(10, 10, 100, 50),
            Array.Empty<PointF>());

        s.Kind.Should().Be(ShapeKind.Square);
        s.Type.Should().Be(AnnotationType.Square);
        s.Vertices.Should().BeEmpty();
        s.BorderWidth.Should().Be(1f);
    }

    [Fact]
    public void Circle_ConstructsWithEmptyVertices()
    {
        ShapeAnnotation s = new(
            ShapeKind.Circle, 0,
            new RectangleF(10, 10, 100, 100),
            Array.Empty<PointF>());

        s.Kind.Should().Be(ShapeKind.Circle);
        s.Type.Should().Be(AnnotationType.Circle);
    }

    [Fact]
    public void Line_RequiresExactlyTwoVertices()
    {
        ShapeAnnotation s = new(
            ShapeKind.Line, 0,
            new RectangleF(0, 0, 50, 0),
            new[] { new PointF(0, 0), new PointF(50, 0) });

        s.Kind.Should().Be(ShapeKind.Line);
        s.Vertices.Should().HaveCount(2);
    }

    [Fact]
    public void Line_ThreeVertices_Throws()
    {
        Action act = () => new ShapeAnnotation(
            ShapeKind.Line, 0,
            new RectangleF(0, 0, 50, 0),
            new[] { new PointF(0, 0), new PointF(50, 0), new PointF(100, 0) });
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Line_OneVertex_Throws()
    {
        Action act = () => new ShapeAnnotation(
            ShapeKind.Line, 0,
            new RectangleF(0, 0, 50, 0),
            new[] { new PointF(0, 0) });
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Polyline_RequiresAtLeastTwoVertices()
    {
        ShapeAnnotation s = new(
            ShapeKind.Polyline, 0,
            new RectangleF(0, 0, 100, 100),
            new[] { new PointF(0, 0), new PointF(50, 50), new PointF(100, 100) });

        s.Kind.Should().Be(ShapeKind.Polyline);
        s.Vertices.Should().HaveCount(3);
    }

    [Fact]
    public void Polyline_OneVertex_Throws()
    {
        Action act = () => new ShapeAnnotation(
            ShapeKind.Polyline, 0,
            new RectangleF(0, 0, 100, 100),
            new[] { new PointF(0, 0) });
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Polygon_StoresVerticesAndInteriorColor()
    {
        ColorF fill = ColorF.FromRgb(0.5f, 0.5f, 0.5f);
        ShapeAnnotation s = new(
            ShapeKind.Polygon, 0,
            new RectangleF(0, 0, 100, 100),
            new[]
            {
                new PointF(0, 0),
                new PointF(100, 0),
                new PointF(50, 100),
            },
            interiorColor: fill,
            borderWidth: 2.5f);

        s.Kind.Should().Be(ShapeKind.Polygon);
        s.Vertices.Should().HaveCount(3);
        s.InteriorColor.Should().Be(fill);
        s.BorderWidth.Should().Be(2.5f);
    }

    [Fact]
    public void Constructor_NullVertices_Throws()
    {
        Action act = () => new ShapeAnnotation(
            ShapeKind.Square, 0,
            new RectangleF(0, 0, 10, 10),
            vertices: null!);
        act.Should().Throw<ArgumentNullException>();
    }
}

// ── Reader ────────────────────────────────────────────────────────────────

public sealed class AnnotationReaderTests
{
    [Fact]
    public void GetAnnotations_NullDocument_Throws()
    {
        Action act = () => AnnotationReader.GetAnnotations(null!, 0);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetAnnotations_OutOfRange_Throws()
    {
        using MemoryStream ms = TestBuilder.BuildPlainPdf();
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true);
        Action act = () => AnnotationReader.GetAnnotations(doc, 5);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void GetAnnotations_NoAnnots_ReturnsEmpty()
    {
        using MemoryStream ms = TestBuilder.BuildPlainPdf();
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true);
        IReadOnlyList<PdfAnnotation> annots = AnnotationReader.GetAnnotations(doc, 0);
        annots.Should().BeEmpty();
    }

    [Fact]
    public void GetAnnotations_TextAnnot_ReadsContents()
    {
        using MemoryStream ms = TestBuilder.BuildPdfWithTextAnnot("Patient note");
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true);
        IReadOnlyList<PdfAnnotation> annots = AnnotationReader.GetAnnotations(doc, 0);

        annots.Should().HaveCount(1);
        annots[0].Should().BeOfType<TextAnnotation>();
        annots[0].Contents.Should().Be("Patient note");
    }

    [Fact]
    public void GetAllAnnotations_ReturnsAcrossPages()
    {
        using MemoryStream ms = TestBuilder.BuildPdfWithTextAnnot("X");
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true);
        AnnotationReader.GetAllAnnotations(doc).Should().HaveCount(1);
    }

    [Fact]
    public void GetAnnotations_SquareSubtype_DecodesShapeAnnotation()
    {
        using MemoryStream ms = TestBuilder.BuildPdfWithShapeAnnot("Square");
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true);
        IReadOnlyList<PdfAnnotation> annots = AnnotationReader.GetAnnotations(doc, 0);

        annots.Should().HaveCount(1);
        annots[0].Should().BeOfType<ShapeAnnotation>();
        ((ShapeAnnotation)annots[0]).Kind.Should().Be(ShapeKind.Square);
    }

    [Fact]
    public void GetAnnotations_CircleSubtype_DecodesShapeAnnotation()
    {
        using MemoryStream ms = TestBuilder.BuildPdfWithShapeAnnot("Circle");
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true);
        IReadOnlyList<PdfAnnotation> annots = AnnotationReader.GetAnnotations(doc, 0);

        annots.Should().HaveCount(1);
        ((ShapeAnnotation)annots[0]).Kind.Should().Be(ShapeKind.Circle);
    }

    [Fact]
    public void GetAnnotations_LineSubtype_ReadsEndpointsFromL()
    {
        using MemoryStream ms = TestBuilder.BuildPdfWithLineAnnot();
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true);
        IReadOnlyList<PdfAnnotation> annots = AnnotationReader.GetAnnotations(doc, 0);

        annots.Should().HaveCount(1);
        ShapeAnnotation line = (ShapeAnnotation)annots[0];
        line.Kind.Should().Be(ShapeKind.Line);
        line.Vertices.Should().HaveCount(2);
        line.Vertices[0].X.Should().Be(10);
        line.Vertices[0].Y.Should().Be(20);
        line.Vertices[1].X.Should().Be(110);
        line.Vertices[1].Y.Should().Be(220);
    }

    [Fact]
    public void GetAnnotations_PolygonSubtype_ReadsVerticesFromVertices()
    {
        using MemoryStream ms = TestBuilder.BuildPdfWithPolygonAnnot();
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true);
        IReadOnlyList<PdfAnnotation> annots = AnnotationReader.GetAnnotations(doc, 0);

        annots.Should().HaveCount(1);
        ShapeAnnotation poly = (ShapeAnnotation)annots[0];
        poly.Kind.Should().Be(ShapeKind.Polygon);
        poly.Vertices.Should().HaveCount(3);
    }
}

// ── Writer ────────────────────────────────────────────────────────────────

public sealed class AnnotationWriterTests
{
    [Fact]
    public void Add_NullOutput_Throws()
    {
        using MemoryStream ms = TestBuilder.BuildPlainPdf();
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true);
        Action act = () => AnnotationWriter.Add(null!, doc, new List<PdfAnnotation>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Add_NullDocument_Throws()
    {
        Action act = () => AnnotationWriter.Add(new MemoryStream(), null!, new List<PdfAnnotation>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Add_NullAnnotations_Throws()
    {
        using MemoryStream ms = TestBuilder.BuildPlainPdf();
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true);
        Action act = () => AnnotationWriter.Add(new MemoryStream(), doc, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Add_OutOfRangePage_Throws()
    {
        using MemoryStream ms = TestBuilder.BuildPlainPdf();
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true);
        TextAnnotation t = new(5, new RectangleF(0, 0, 10, 10), "x");
        Action act = () => AnnotationWriter.Add(new MemoryStream(), doc, new[] { t });
        act.Should().Throw<AnnotationException>();
    }

    [Fact]
    public void Add_TextAnnotation_RoundTrips()
    {
        using MemoryStream source = TestBuilder.BuildPlainPdf();
        using PdfDocument doc = PdfDocument.Open(source, leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        TextAnnotation t = new(0, new RectangleF(100, 100, 20, 20), "Doctor note",
            iconName: "Comment", isOpen: true, color: ColorF.FromRgb(1f, 0f, 0f),
            author: "Dr Smith");
        AnnotationWriter.Add(output, doc, new[] { t });

        output.Seek(0, SeekOrigin.Begin);
        using PdfDocument result = PdfDocument.Open(output, leaveOpen: true);
        IReadOnlyList<PdfAnnotation> annots = AnnotationReader.GetAnnotations(result, 0);

        annots.Should().HaveCount(1);
        annots[0].Should().BeOfType<TextAnnotation>();
        TextAnnotation roundtripped = (TextAnnotation)annots[0];
        roundtripped.Contents.Should().Be("Doctor note");
        roundtripped.IconName.Should().Be("Comment");
        roundtripped.IsOpen.Should().BeTrue();
        roundtripped.Author.Should().Be("Dr Smith");
    }

    [Fact]
    public void Add_HighlightAnnotation_RoundTripsQuadPoints()
    {
        using MemoryStream source = TestBuilder.BuildPlainPdf();
        using PdfDocument doc = PdfDocument.Open(source, leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        float[] quads = { 100, 120, 300, 120, 100, 100, 300, 100 };
        MarkupAnnotation h = new(AnnotationType.Highlight, 0,
            new RectangleF(100, 100, 200, 20), quads,
            color: ColorF.FromRgb(1f, 1f, 0f));
        AnnotationWriter.Add(output, doc, new[] { h });

        output.Seek(0, SeekOrigin.Begin);
        using PdfDocument result = PdfDocument.Open(output, leaveOpen: true);
        IReadOnlyList<PdfAnnotation> annots = AnnotationReader.GetAnnotations(result, 0);

        annots.Should().HaveCount(1);
        annots[0].Should().BeOfType<MarkupAnnotation>();
        ((MarkupAnnotation)annots[0]).QuadPoints.Should().HaveCount(8);
        annots[0].Type.Should().Be(AnnotationType.Highlight);
    }

    [Fact]
    public void Add_StampAnnotation_PreservesName()
    {
        using MemoryStream source = TestBuilder.BuildPlainPdf();
        using PdfDocument doc = PdfDocument.Open(source, leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        StampAnnotation s = new(0, new RectangleF(50, 50, 100, 30), "Confidential");
        AnnotationWriter.Add(output, doc, new[] { s });

        output.Seek(0, SeekOrigin.Begin);
        using PdfDocument result = PdfDocument.Open(output, leaveOpen: true);
        IReadOnlyList<PdfAnnotation> annots = AnnotationReader.GetAnnotations(result, 0);

        annots.Should().HaveCount(1);
        annots[0].Should().BeOfType<StampAnnotation>();
        ((StampAnnotation)annots[0]).StampName.Should().Be("Confidential");
    }

    [Fact]
    public void Add_OriginalContentPreserved()
    {
        using MemoryStream source = TestBuilder.BuildPlainPdf();
        using PdfDocument doc = PdfDocument.Open(source, leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        TextAnnotation t = new(0, new RectangleF(0, 0, 10, 10), "x");
        AnnotationWriter.Add(output, doc, new[] { t });

        output.Seek(0, SeekOrigin.Begin);
        using PdfDocument result = PdfDocument.Open(output, leaveOpen: true);
        result.PageCount.Should().Be(1);
    }
}

// ── Extension methods ─────────────────────────────────────────────────────

public sealed class PdfDocumentAnnotationExtensionsTests
{
    [Fact]
    public void GetAnnotations_NullDocument_Throws()
    {
        Action act = () => PdfDocumentAnnotationExtensions.GetAnnotations(null!, 0);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetAnnotations_WrapsAnnotationReader()
    {
        using MemoryStream ms = TestBuilder.BuildPdfWithTextAnnot("hi");
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true);

        IReadOnlyList<PdfAnnotation> via_extension = doc.GetAnnotations(0);
        IReadOnlyList<PdfAnnotation> via_reader = AnnotationReader.GetAnnotations(doc, 0);

        via_extension.Should().HaveCount(via_reader.Count);
        via_extension[0].Contents.Should().Be(via_reader[0].Contents);
    }

    [Fact]
    public void GetAllAnnotations_NullDocument_Throws()
    {
        Action act = () => PdfDocumentAnnotationExtensions.GetAllAnnotations(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetAllAnnotations_WrapsAnnotationReader()
    {
        using MemoryStream ms = TestBuilder.BuildPdfWithTextAnnot("hi");
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true);

        doc.GetAllAnnotations().Should().HaveCount(1);
    }
}

// ── Test PDF builder ──────────────────────────────────────────────────────

internal static class TestBuilder
{
    internal static MemoryStream BuildPlainPdf()
    {
        PdfObjectId catalogId = new(1, 0);
        PdfObjectId pagesId = new(2, 0);
        PdfObjectId pageId = new(3, 0);

        PdfDictionary pageDict = new();
        pageDict.Set(PdfName.Type, PdfName.Page);
        pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
        pageDict.Set(PdfName.MediaBox, new PdfArray([
            new PdfInteger(0), new PdfInteger(0),
            new PdfInteger(612), new PdfInteger(792),
        ]));

        PdfDictionary pagesDict = new();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, new PdfArray([new PdfReference(pageId)]));
        pagesDict.Set(PdfName.Count, 1);

        PdfDictionary catalogDict = new();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

        List<PdfIndirectObject> objects = [
            new PdfIndirectObject(catalogId, catalogDict),
            new PdfIndirectObject(pagesId, pagesDict),
            new PdfIndirectObject(pageId, pageDict),
        ];

        PdfDictionary trailer = new();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new();
        PdfWriter.Write(ms, objects, trailer);
        return ms;
    }

    internal static MemoryStream BuildPdfWithTextAnnot(string contents)
    {
        PdfDictionary annotDict = new();
        annotDict.Set(PdfName.Type, PdfName.Intern("Annot"));
        annotDict.Set(PdfName.Intern("Subtype"), PdfName.Intern("Text"));
        annotDict.Set(PdfName.Intern("Rect"), new PdfArray([
            new PdfReal(100), new PdfReal(100),
            new PdfReal(200), new PdfReal(200),
        ]));
        annotDict.Set(PdfName.Intern("Contents"),
            new PdfString(Encoding.Latin1.GetBytes(contents)));

        return BuildPdfWithAnnot(annotDict);
    }

    internal static MemoryStream BuildPdfWithShapeAnnot(string subtype)
    {
        PdfDictionary annotDict = new();
        annotDict.Set(PdfName.Type, PdfName.Intern("Annot"));
        annotDict.Set(PdfName.Intern("Subtype"), PdfName.Intern(subtype));
        annotDict.Set(PdfName.Intern("Rect"), new PdfArray([
            new PdfReal(50), new PdfReal(50),
            new PdfReal(150), new PdfReal(150),
        ]));
        return BuildPdfWithAnnot(annotDict);
    }

    internal static MemoryStream BuildPdfWithLineAnnot()
    {
        PdfDictionary annotDict = new();
        annotDict.Set(PdfName.Type, PdfName.Intern("Annot"));
        annotDict.Set(PdfName.Intern("Subtype"), PdfName.Intern("Line"));
        annotDict.Set(PdfName.Intern("Rect"), new PdfArray([
            new PdfReal(10), new PdfReal(20),
            new PdfReal(110), new PdfReal(220),
        ]));
        annotDict.Set(PdfName.Intern("L"), new PdfArray([
            new PdfReal(10), new PdfReal(20),
            new PdfReal(110), new PdfReal(220),
        ]));
        return BuildPdfWithAnnot(annotDict);
    }

    internal static MemoryStream BuildPdfWithPolygonAnnot()
    {
        PdfDictionary annotDict = new();
        annotDict.Set(PdfName.Type, PdfName.Intern("Annot"));
        annotDict.Set(PdfName.Intern("Subtype"), PdfName.Intern("Polygon"));
        annotDict.Set(PdfName.Intern("Rect"), new PdfArray([
            new PdfReal(0), new PdfReal(0),
            new PdfReal(100), new PdfReal(100),
        ]));
        annotDict.Set(PdfName.Intern("Vertices"), new PdfArray([
            new PdfReal(0), new PdfReal(0),
            new PdfReal(100), new PdfReal(0),
            new PdfReal(50), new PdfReal(100),
        ]));
        return BuildPdfWithAnnot(annotDict);
    }

    private static MemoryStream BuildPdfWithAnnot(PdfDictionary annotDict)
    {
        PdfObjectId catalogId = new(1, 0);
        PdfObjectId pagesId = new(2, 0);
        PdfObjectId pageId = new(3, 0);
        PdfObjectId annotId = new(4, 0);

        PdfDictionary pageDict = new();
        pageDict.Set(PdfName.Type, PdfName.Page);
        pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
        pageDict.Set(PdfName.MediaBox, new PdfArray([
            new PdfInteger(0), new PdfInteger(0),
            new PdfInteger(612), new PdfInteger(792),
        ]));
        pageDict.Set(PdfName.Intern("Annots"),
            new PdfArray([new PdfReference(annotId)]));

        PdfDictionary pagesDict = new();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, new PdfArray([new PdfReference(pageId)]));
        pagesDict.Set(PdfName.Count, 1);

        PdfDictionary catalogDict = new();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

        List<PdfIndirectObject> objects = [
            new PdfIndirectObject(catalogId, catalogDict),
            new PdfIndirectObject(pagesId, pagesDict),
            new PdfIndirectObject(pageId, pageDict),
            new PdfIndirectObject(annotId, annotDict),
        ];

        PdfDictionary trailer = new();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new();
        PdfWriter.Write(ms, objects, trailer);
        return ms;
    }
}
