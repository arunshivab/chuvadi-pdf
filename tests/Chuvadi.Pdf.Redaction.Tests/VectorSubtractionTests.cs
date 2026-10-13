// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.5 (paths), §8.5.3 (path-painting operators)
// PHASE: Redaction — precise vector subtraction (VectorRedactionMode.SubtractPrecise)
//
// Pre-3.18, a painted path whose bounding box touched a redaction region was
// dropped in its entirety — redacting one table cell erased the whole table
// border drawn as a single path. SubtractPrecise (the new default) keeps the
// geometry whose ink provably misses every region: untouched subpaths survive
// verbatim, straight stroked segments are split at (expanded) region
// boundaries, and axis-aligned rectangle fills decompose into the surviving
// sub-rectangles. Every doubtful construct (curves crossing a region,
// unresolvable ExtGState line width, rotated regions over rectangle fills)
// still drops — over-redaction in doubt, never retention.
//
// The redaction region in every test is (150, 690)–(250, 720) on page 0.
// Default stroke ink allowance: max(lineWidth, 1)/2 × (miterLimit + 1)
// = 0.5 × 11 = 5.5, so the region expands to x 144.5–255.5 for strokes.

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

namespace Chuvadi.Pdf.Redaction.Tests;

public sealed class VectorSubtractionTests
{
    [Fact]
    public void StrokeCrossingRegion_IsSplitAtExpandedBoundary()
    {
        // One horizontal stroke from x=50 to x=400 through the region.
        string redacted = Redact("50 700 m\n400 700 l\nS\n");

        redacted.Should().Contain("50 700 m",
            "the piece left of the region survives from its original start");
        redacted.Should().Contain("144.5 700 l",
            "the left piece ends at the region boundary expanded by the stroke ink allowance");
        redacted.Should().Contain("255.5 700 m",
            "the right piece starts at the expanded region boundary");
        redacted.Should().Contain("400 700 l",
            "the piece right of the region survives to its original end");
    }

    [Fact]
    public void StrokeSubpathOutsideRegion_SurvivesWhenSiblingIsRedacted()
    {
        // One path, two subpaths: a line through the region and a line far
        // below it. Pre-3.18 the whole path (both lines) was dropped.
        string redacted = Redact("50 700 m\n400 700 l\n50 100 m\n400 100 l\nS\n");

        redacted.Should().Contain("50 100 m",
            "a subpath whose ink cannot touch the region is kept verbatim");
        redacted.Should().Contain("400 100 l");
    }

    [Fact]
    public void StrokeSubpathFullyInsideRegion_IsRemoved()
    {
        // A short line fully inside the region plus one far below it.
        string redacted = Redact("170 700 m\n230 700 l\n50 100 m\n400 100 l\nS\n");

        redacted.Should().NotContain("170 700",
            "a subpath fully inside a region is removed at byte level");
        redacted.Should().NotContain("230 700");
        redacted.Should().Contain("50 100 m");
    }

    [Fact]
    public void RectangleFillOverlappingRegion_DecomposesIntoSurvivingPieces()
    {
        // A filled rectangle (100,650)–(300,750) overlapping the region.
        string redacted = Redact("100 650 200 100 re\nf\n");

        redacted.Should().NotContain("100 650 200 100 re",
            "the original rectangle must not survive intact");

        // The subtracted region is inflated by 1e-4 pt so coordinate rounding
        // can never nudge a piece edge back inside it: pieces pull back a hair.
        redacted.Should().Contain("100 650 49.9999 100 re",
            "the strip left of the region survives");
        redacted.Should().Contain("250.0001 650 49.9999 100 re",
            "the strip right of the region survives");
        redacted.Should().Contain("149.9999 650 100.0002 39.9999 re",
            "the strip below the region survives");
        redacted.Should().Contain("149.9999 720.0001 100.0002 29.9999 re",
            "the strip above the region survives");
    }

    [Fact]
    public void CurvedStrokeCrossingRegion_DropsThatSubpathOnly()
    {
        // A Bézier through the region plus a straight line far below it.
        string redacted = Redact(
            "50 700 m\n150 800 250 600 350 700 c\n50 100 m\n400 100 l\nS\n");

        redacted.Should().NotContain("150 800",
            "a curve crossing a region cannot be split safely and is removed whole");
        redacted.Should().NotContain("250 600");
        redacted.Should().Contain("50 100 m",
            "the untouched straight subpath still survives");
    }

    [Fact]
    public void UnresolvableExtGState_FallsBackToWholeStrokeDrop()
    {
        // /GS1 is not in the (empty) resources, so the line width after gs is
        // unknown and no stroked geometry may be kept.
        string redacted = Redact("/GS1 gs\n50 700 m\n400 700 l\nS\n");

        redacted.Should().NotContain("50 700 m",
            "with an unknown stroke width the whole intersecting path must drop");
        redacted.Should().NotContain("400 700 l");
    }

    [Fact]
    public void RotatedRegionOverRectangleFill_DropsWholeRectangle()
    {
        // Under a rotated CTM the region maps to a rotated quad in user space;
        // no exact axis-aligned decomposition exists, so the rectangle drops.
        // (540,307)+(200,100) in this rotated user space covers the region's
        // device-space centre (≈200, 705).
        string redacted = Redact(
            "q\n0.7071 0.7071 -0.7071 0.7071 0 0 cm\n540 307 200 100 re\nf\nQ\n");

        redacted.Should().NotContain("540 307 200 100 re",
            "a rotated region over a rectangle fill cannot decompose exactly — doubt drops");
    }

    [Fact]
    public void DropIntersectingMode_PreservesLegacyWholePathDrop()
    {
        string redacted = Redact(
            "50 700 m\n400 700 l\n50 100 m\n400 100 l\nS\n",
            VectorRedactionMode.DropIntersecting);

        redacted.Should().NotContain("50 700 m",
            "legacy mode drops the whole intersecting path");
        redacted.Should().NotContain("50 100 m",
            "legacy mode drops untouched sibling subpaths too");
    }

    [Fact]
    public void WideLineWidth_ExpandsTheSubtractedSpan()
    {
        // 20 w: ink allowance = 10 × 11 = 110, so the region expands to
        // x 40–360 and only the piece right of x=360 survives.
        string redacted = Redact("20 w\n50 700 m\n400 700 l\nS\n");

        redacted.Should().NotContain("50 700 m",
            "the left piece is consumed by the widened ink allowance");
        redacted.Should().Contain("360 700 m",
            "the surviving right piece starts at the widened boundary");
        redacted.Should().Contain("400 700 l");
    }

    private static string Redact(
        string contentStream, VectorRedactionMode mode = VectorRedactionMode.SubtractPrecise)
    {
        using MemoryStream source = BuildVectorPdf(contentStream);
        using PdfDocument doc = PdfDocument.Open(source, leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        RedactionOptions options = new RedactionOptions
        {
            Rectangles = new List<RedactionRect>
            {
                new RedactionRect(0, new RectangleF(150, 690, 100, 30)),
            },
            VectorMode = mode,
        };
        Redactor.Apply(output, doc, options);

        return Encoding.Latin1.GetString(output.ToArray());
    }

    private static MemoryStream BuildVectorPdf(string contentStream)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);
        PdfObjectId contentId = new PdfObjectId(4, 0);

        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(pagesId));

        PdfDictionary pages = new PdfDictionary();
        pages.Set(PdfName.Type, PdfName.Pages);
        pages.Set(PdfName.Kids, new PdfArray(new PdfPrimitive[] { new PdfReference(pageId) }));
        pages.Set(PdfName.Count, 1);
        pages.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(612), new PdfInteger(792),
        }));

        PdfDictionary page = new PdfDictionary();
        page.Set(PdfName.Type, PdfName.Page);
        page.Set(PdfName.Parent, new PdfReference(pagesId));
        page.Set(PdfName.Contents, new PdfReference(contentId));
        page.Set(PdfName.Intern("Resources"), new PdfDictionary());

        byte[] content = Encoding.ASCII.GetBytes(contentStream);
        PdfDictionary contentDict = new PdfDictionary();
        contentDict.Set(PdfName.Length, content.Length);

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(catalogId, catalog),
            new PdfIndirectObject(pagesId, pages),
            new PdfIndirectObject(pageId, page),
            new PdfIndirectObject(contentId, new PdfStream(contentDict, content)),
        };

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        ms.Position = 0;
        return ms;
    }
}
