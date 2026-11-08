// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.32.0 (D-221 P3, D-230 Q9) — stroked curves crossing a redaction
//        region are cut; only pieces whose ink provably misses every region
//        are kept.
//
// Every case is checked three ways: RedactionVerifier finds nothing inside the
// regions; a 4x render shows no ink inside any region (every pixel there is
// white to within 5 levels); and every solidly inked pixel of the original
// stroke beyond the cut's reach from every region is still inked. The reach is
// the widened region's corner (allowance x sqrt 2, as for straight lines),
// plus half the line width, plus 2 pt. (Anti-aliased edge pixels may shade slightly differently,
// because the kept pieces are flattened separately.)

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Rendering;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Redaction.Tests;

public sealed class VectorCurveCut3320Tests
{
    private const double Dpi = 288;

    // Cut reach for a 1 pt line: allowance 0.5 x (10 + 1) = 5.5, so
    // 5.5 x sqrt 2 + 0.5 + 2.
    private const double ThinReach = 10.3;

    private static readonly RectangleF Middle = new RectangleF(150, 690, 100, 30);

    // The four cubics of a circle, centre (200, 700), radius 60.
    private const string Circle =
        "260 700 m\n260 733.137 233.137 760 200 760 c\n166.863 760 140 733.137 140 700 c\n"
        + "140 666.863 166.863 640 200 640 c\n233.137 640 260 666.863 260 700 c\nh\nS\n";

    [Fact]
    public void SCurveThroughARegion_KeepsBothEnds()
    {
        string content = "50 700 m\n150 800 250 600 350 700 c\nS\n";

        string redacted = Check(content, new[] { Middle }, ThinReach);

        redacted.Should().Contain(" c\n", "pieces of the curve survive as curves");
    }

    [Fact]
    public void CircleCrossedByARegion_KeepsTheArcsOutside()
    {
        Check(Circle, new[] { new RectangleF(230, 680, 60, 40) }, ThinReach);
    }

    [Fact]
    public void RegionOverACorner_OfARoundedRectangle()
    {
        string rounded =
            "120 600 m\n280 600 l\n291 600 300 609 300 620 c\n300 780 l\n300 791 291 800 280 800 c\n"
            + "120 800 l\n109 800 100 791 100 780 c\n100 620 l\n100 609 109 600 120 600 c\nh\nS\n";

        Check(rounded, new[] { new RectangleF(285, 785, 30, 30) }, ThinReach);
    }

    [Fact]
    public void ThickStroke_UsesItsWiderAllowance()
    {
        // 6 w: allowance 3 x 11 = 33; reach 33 x sqrt 2 + 3 + 2.
        Check("6 w\n" + Circle, new[] { new RectangleF(230, 680, 60, 40) }, 51.7);
    }

    [Fact]
    public void RotatedMatrix_CutsInUserSpace()
    {
        string content = "q\n0.8660 0.5 -0.5 0.8660 200 300 cm\n-150 0 m\n-50 120 50 -120 150 0 c\nS\nQ\n";

        Check(content, new[] { new RectangleF(180, 280, 40, 40) }, ThinReach);
    }

    [Fact]
    public void TwoRegions_OnOneCurve()
    {
        string content = "50 700 m\n150 800 250 600 350 700 c\nS\n";

        Check(content, new[] { new RectangleF(80, 700, 30, 30), new RectangleF(280, 660, 30, 30) }, ThinReach);
    }

    [Fact]
    public void CurveWhoseHullCrossesButInkMisses_IsKeptWhole()
    {
        // The control points reach into the region's widened box, the curve
        // itself stays at least 15 pt below the region.
        string content = "100 600 m\n150 700 250 700 300 600 c\nS\n";

        string redacted = Check(content, new[] { new RectangleF(190, 690, 20, 20) }, ThinReach);
        redacted.Should().Contain(" c\n");
    }

    [Fact]
    public void CurveInsideTheRegion_IsRemoved()
    {
        string content = "160 700 m\n180 715 220 685 240 700 c\n50 100 m\n400 100 l\nS\n";

        string redacted = Check(content, new[] { Middle }, ThinReach);
        redacted.Should().NotContain("180 715");
        redacted.Should().Contain("50 100 m");
    }

    [Fact]
    public void CurvedFill_StillDropsWhole()
    {
        // D-230 Q9: only strokes are cut in 3.32.0.
        string fill = Circle.Replace("S\n", "f\n", StringComparison.Ordinal);

        string redacted = Redact(fill, new[] { new RectangleF(230, 680, 60, 40) });
        redacted.Should().NotContain(" c\n");
    }

    // ── helpers ────────────────────────────────────────────────────────────

    // Redacts, then checks the three properties; returns the redacted bytes.
    private static string Check(string content, RectangleF[] regions, double reach)
    {
        byte[] original = Build(content);
        byte[] redacted = RedactBytes(original, regions);

        using (PdfDocument doc = PdfDocument.Open(new MemoryStream(redacted), leaveOpen: false))
        {
            RedactionVerification verification = RedactionVerifier.Verify(doc, Rects(regions));
            verification.IsClean.Should().BeTrue("nothing may remain inside a region");
        }

        PixelBuffer before = Render(original);
        PixelBuffer after = Render(redacted);
        int kept = 0;
        for (int y = 0; y < after.Height; y++)
        {
            for (int x = 0; x < after.Width; x++)
            {
                double px = (x + 0.5) * 72 / Dpi;
                double py = 792 - ((y + 0.5) * 72 / Dpi);
                double distance = double.MaxValue;
                foreach (RectangleF r in regions)
                {
                    distance = Math.Min(distance, DistanceTo(r, px, py));
                }

                if (distance <= 0)
                {
                    IsWhite(after.GetPixelBgra(x, y)).Should().BeTrue($"pixel {x},{y} is inside a region");
                }
                else if (distance > reach && IsSolid(before.GetPixelBgra(x, y)))
                {
                    IsInk(after.GetPixelBgra(x, y)).Should().BeTrue($"pixel {x},{y} is {distance:0.0} pt from every region");
                    kept++;
                }
            }
        }

        kept.Should().BeGreaterThan(0, "some of the stroke lies away from the regions");
        return Encoding.Latin1.GetString(redacted);
    }

    private static string Redact(string content, RectangleF[] regions) =>
        Encoding.Latin1.GetString(RedactBytes(Build(content), regions));

    private static byte[] RedactBytes(byte[] pdf, RectangleF[] regions)
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        using MemoryStream output = new MemoryStream();
        Redactor.Apply(output, doc, new RedactionOptions
        {
            Rectangles = Rects(regions),
            DrawOverlay = false,
            VectorMode = VectorRedactionMode.SubtractPrecise,
        });
        return output.ToArray();
    }

    private static List<RedactionRect> Rects(RectangleF[] regions)
    {
        List<RedactionRect> list = new List<RedactionRect>();
        foreach (RectangleF r in regions)
        {
            list.Add(new RedactionRect(0, r));
        }

        return list;
    }

    private static PixelBuffer Render(byte[] pdf)
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        return new PageRasterizer(doc.Objects, new RenderOptions { Dpi = Dpi }).Rasterize(doc.Pages[0]);
    }

    private static bool IsInk((byte B, byte G, byte R, byte A) p) => p.R < 200 || p.G < 200 || p.B < 200;

    private static bool IsSolid((byte B, byte G, byte R, byte A) p) => p.R < 128 && p.G < 128 && p.B < 128;

    private static bool IsWhite((byte B, byte G, byte R, byte A) p) => p.R >= 250 && p.G >= 250 && p.B >= 250;

    private static double DistanceTo(RectangleF r, double x, double y)
    {
        double dx = Math.Max(Math.Max(r.X - x, 0), x - (r.X + r.Width));
        double dy = Math.Max(Math.Max(r.Y - y, 0), y - (r.Y + r.Height));
        if (dx == 0 && dy == 0)
        {
            return 0;
        }

        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static byte[] Build(string contentStream)
    {
        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(2, 0));
        PdfDictionary pages = new PdfDictionary();
        pages.Set(PdfName.Type, PdfName.Pages);
        pages.Set(PdfName.Kids, new PdfArray(new PdfPrimitive[] { new PdfReference(3, 0) }));
        pages.Set(PdfName.Count, 1);
        PdfDictionary page = new PdfDictionary();
        page.Set(PdfName.Type, PdfName.Page);
        page.Set(PdfName.Parent, new PdfReference(2, 0));
        page.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[] { new PdfInteger(0), new PdfInteger(0), new PdfInteger(612), new PdfInteger(792) }));
        page.Set(PdfName.Contents, new PdfReference(4, 0));
        page.Set(PdfName.Resources, new PdfDictionary());
        byte[] content = Encoding.ASCII.GetBytes(contentStream);
        PdfDictionary contentDict = new PdfDictionary();
        contentDict.Set(PdfName.Length, content.Length);

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(new PdfObjectId(1, 0), catalog),
            new PdfIndirectObject(new PdfObjectId(2, 0), pages),
            new PdfIndirectObject(new PdfObjectId(3, 0), page),
            new PdfIndirectObject(new PdfObjectId(4, 0), new PdfStream(contentDict, content)),
        };
        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(1, 0));
        using MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        return ms.ToArray();
    }
}
