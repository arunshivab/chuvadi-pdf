// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// VERIFIER: adversarial tests for VectorRedactionMode.SubtractPrecise (B15).
// These tests probe retention bugs: ink inside the redaction rectangle that
// survives at byte level in the rewritten content stream.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using Xunit;

namespace Chuvadi.Pdf.Redaction.Tests;

public sealed class AdversarialVectorRedactionVerifierTests
{
    // Region on page 0 in device space (same as VectorSubtractionTests).
    private const double RegX0 = 150;
    private const double RegY0 = 690;
    private const double RegX1 = 250;
    private const double RegY1 = 720;

    // ── 1. Malformed extra operands: model-vs-viewer divergence ───────────
    // "900 900 190 705 m": the subtractor models the FIRST two operands
    // (900,900) while stack-machine viewers (Acrobat/Ghostscript semantics)
    // consume the LAST two (190,705) — inside the region. If the subpath is
    // kept verbatim, ink inside the region survives at byte level.
    [Fact]
    public void ExtraOperands_SubpathKeptVerbatim_ByteRetentionInsideRegion()
    {
        string redacted = Redact(
            "50 700 m\n400 700 l\n900 900 190 705 m\n900 900 210 705 l\nS\n");

        bool retained = redacted.Contains("190 705", StringComparison.Ordinal)
            && redacted.Contains("210 705", StringComparison.Ordinal);

        // Legacy mode would have dropped the whole path (sibling subpath hits
        // the region); precise mode keeps the malformed subpath verbatim.
        Assert.False(retained,
            "RETENTION: operands '190 705' / '210 705' (inside the redaction "
            + "region under last-n operand semantics) survive in the output");
    }

    // ── 2. State operator between construction and paint ──────────────────
    // "w" inside a path object is illegal PDF but renders everywhere. The
    // path must stay buffered across it so the paint operator still sees the
    // region check. Legacy mode drops the whole path; precise mode may keep
    // split pieces, but no kept segment's INK (centreline expanded by half
    // the 5 pt width) may touch the region.
    [Theory]
    [InlineData(VectorRedactionMode.SubtractPrecise)]
    [InlineData(VectorRedactionMode.DropIntersecting)]
    public void StateOpMidPath_PathThroughRegion_Survives(VectorRedactionMode mode)
    {
        string redacted = Redact("50 700 m\n400 700 l\n5 w\nS\n", mode);

        if (mode == VectorRedactionMode.DropIntersecting)
        {
            Assert.DoesNotContain("50 700 m", redacted, StringComparison.Ordinal);
            return;
        }

        const double allowance = 2.5;
        List<(double X, double Y, string Op)> points = ExtractPathPoints(redacted);
        (double X, double Y)? prev = null;
        foreach ((double x, double y, string op) in points)
        {
            if (op == "l" && prev is (double px, double py))
            {
                Assert.False(
                    SegmentIntersectsRect(
                        px, py, x, y,
                        RegX0 - allowance, RegY0 - allowance,
                        RegX1 + allowance, RegY1 + allowance),
                    $"RETENTION (SubtractPrecise): kept segment ({px:0.##},{py:0.##})"
                    + $"->({x:0.##},{y:0.##}) puts stroke ink inside the region");
            }

            prev = (x, y);
        }
    }

    // ── 3. Rotated CTM: kept stroke pieces must miss the region ───────────
    [Fact]
    public void RotatedCtm_KeptStrokePieces_StayOutsideRegion()
    {
        // Under R(45°), user (639.93, 357.09) maps to device (200.0, 705.0),
        // the region centre. A horizontal user-space line through it crosses
        // the region in device space.
        string content =
            "q\n0.7071 0.7071 -0.7071 0.7071 0 0 cm\n"
            + "500 357.09 m\n800 357.09 l\nS\nQ\n";
        string redacted = Redact(content);

        // Collect every emitted m/l coordinate pair, map through the CTM, and
        // assert no kept segment intersects the raw region.
        List<(double X, double Y, string Op)> points = ExtractPathPoints(redacted);
        (double a, double b, double c, double d) = (0.7071, 0.7071, -0.7071, 0.7071);

        (double X, double Y)? prev = null;
        foreach ((double x, double y, string op) in points)
        {
            double dx = (a * x) + (c * y);
            double dy = (b * x) + (d * y);
            if (op == "l" && prev is (double px, double py))
            {
                Assert.False(
                    SegmentIntersectsRect(px, py, dx, dy, RegX0, RegY0, RegX1, RegY1),
                    $"RETENTION: kept segment ({px:0.##},{py:0.##})->({dx:0.##},{dy:0.##}) "
                    + "crosses the redaction region in device space");
            }

            prev = (dx, dy);
        }
    }

    // ── 4. Negative-width rectangle fill decomposition ─────────────────────
    [Fact]
    public void NegativeWidthRect_Decomposition_LeavesNoInkInRegion()
    {
        // Same rectangle as the suite's positive test, specified with negative
        // width/height: (300,750) + (-200,-100) == (100,650)-(300,750).
        string redacted = Redact("300 750 -200 -100 re\nf\n");

        foreach ((double x, double y, double w, double h) in ExtractRects(redacted))
        {
            double minX = Math.Min(x, x + w);
            double maxX = Math.Max(x, x + w);
            double minY = Math.Min(y, y + h);
            double maxY = Math.Max(y, y + h);
            bool overlaps = minX < RegX1 && maxX > RegX0 && minY < RegY1 && maxY > RegY0;
            Assert.False(overlaps,
                $"RETENTION: emitted fill rect ({x},{y},{w},{h}) overlaps the region");
        }
    }

    // ── 5. Closing edge of an s-closed subpath crosses the region ─────────
    [Fact]
    public void ImplicitCloseEdge_CrossingRegion_IsSubtracted()
    {
        // Triangle whose only region-crossing edge is the implicit closing
        // edge added by "s".
        string redacted = Redact("50 650 m\n400 650 l\n400 760 l\ns\n");

        List<(double X, double Y, string Op)> points = ExtractPathPoints(redacted);
        (double X, double Y)? prev = null;
        foreach ((double x, double y, string op) in points)
        {
            if (op == "l" && prev is (double px, double py))
            {
                Assert.False(
                    SegmentIntersectsRect(px, py, x, y, RegX0, RegY0, RegX1, RegY1),
                    $"RETENTION: kept segment ({px:0.##},{py:0.##})->({x:0.##},{y:0.##}) "
                    + "crosses the redaction region");
            }

            prev = (x, y);
        }
    }

    // ── 6. Form XObject: quads must map through BaseCtm correctly ─────────
    [Fact]
    public void FormXObjectWithMatrix_StrokeThroughRegion_IsSubtractedInFormStream()
    {
        // Form /Matrix [2 0 0 2 0 0]; form-space line (25,350)->(200,350)
        // maps to device (50,700)->(400,700), crossing the region. The region
        // in form space is x 75..125 at y 350.
        using MemoryStream source = BuildFormPdf(
            pageContent: "/Fm1 Do\n",
            formContent: "25 350 m\n200 350 l\nS\n",
            formMatrix: "[2 0 0 2 0 0]");
        string redacted = RedactStream(source);

        List<(double X, double Y, string Op)> points = ExtractPathPoints(redacted);
        (double X, double Y)? prev = null;
        bool sawAnySegment = false;
        foreach ((double x, double y, string op) in points)
        {
            if (op == "l" && prev is (double px, double py))
            {
                sawAnySegment = true;
                // Map form space -> device: ×2.
                Assert.False(
                    SegmentIntersectsRect(
                        px * 2, py * 2, x * 2, y * 2, RegX0, RegY0, RegX1, RegY1),
                    $"RETENTION: kept form segment ({px},{py})->({x},{y}) crosses "
                    + "the region in device space");
            }

            prev = (x, y);
        }

        // The pieces left and right of the region should survive (precision
        // check, not a safety check): if nothing survived that is safe
        // over-redaction, so only log via assertion message when absent.
        Assert.True(sawAnySegment || !redacted.Contains("25 350", StringComparison.Ordinal),
            "no kept segments and original coordinates linger");
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static List<(double X, double Y, string Op)> ExtractPathPoints(string stream)
    {
        List<(double X, double Y, string Op)> result = new List<(double X, double Y, string Op)>();
        string[] lines = stream.Split('\n');
        foreach (string line in lines)
        {
            string[] parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 3 && (parts[2] == "m" || parts[2] == "l")
                && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
            {
                result.Add((x, y, parts[2]));
            }
        }

        return result;
    }

    private static List<(double X, double Y, double W, double H)> ExtractRects(string stream)
    {
        List<(double X, double Y, double W, double H)> result =
            new List<(double X, double Y, double W, double H)>();
        foreach (string line in stream.Split('\n'))
        {
            string[] parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 5 && parts[4] == "re"
                && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y)
                && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double w)
                && double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double h))
            {
                // Skip the page-sized overlay rect and anything page-wide.
                if (Math.Abs(w) >= 500)
                {
                    continue;
                }

                result.Add((x, y, w, h));
            }
        }

        return result;
    }

    private static bool SegmentIntersectsRect(
        double x0, double y0, double x1, double y1,
        double rx0, double ry0, double rx1, double ry1)
    {
        // Liang-Barsky clip of the segment against the rect interior.
        double t0 = 0, t1 = 1;
        double dx = x1 - x0, dy = y1 - y0;
        double[] p = { -dx, dx, -dy, dy };
        double[] q = { x0 - rx0, rx1 - x0, y0 - ry0, ry1 - y0 };
        for (int i = 0; i < 4; i++)
        {
            if (Math.Abs(p[i]) < 1e-12)
            {
                if (q[i] < 0)
                {
                    return false;
                }

                continue;
            }

            double r = q[i] / p[i];
            if (p[i] < 0)
            {
                if (r > t1) { return false; }
                if (r > t0) { t0 = r; }
            }
            else
            {
                if (r < t0) { return false; }
                if (r < t1) { t1 = r; }
            }
        }

        // Require a non-degenerate overlap (strictly inside, > 1e-6 long).
        return t1 - t0 > 1e-6;
    }

    private static string Redact(
        string contentStream, VectorRedactionMode mode = VectorRedactionMode.SubtractPrecise)
    {
        using MemoryStream source = BuildVectorPdf(contentStream);
        return RedactStream(source, mode);
    }

    private static string RedactStream(
        MemoryStream source, VectorRedactionMode mode = VectorRedactionMode.SubtractPrecise)
    {
        using PdfDocument doc = PdfDocument.Open(source, leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        RedactionOptions options = new RedactionOptions
        {
            Rectangles = new List<RedactionRect>
            {
                new RedactionRect(0, new RectangleF(150, 690, 100, 30)),
            },
            VectorMode = mode,
            DrawOverlay = false,
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

    private static MemoryStream BuildFormPdf(
        string pageContent, string formContent, string formMatrix)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);
        PdfObjectId contentId = new PdfObjectId(4, 0);
        PdfObjectId formId = new PdfObjectId(5, 0);

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

        PdfDictionary xobjects = new PdfDictionary();
        xobjects.Set(PdfName.Intern("Fm1"), new PdfReference(formId));
        PdfDictionary resources = new PdfDictionary();
        resources.Set(PdfName.Intern("XObject"), xobjects);

        PdfDictionary page = new PdfDictionary();
        page.Set(PdfName.Type, PdfName.Page);
        page.Set(PdfName.Parent, new PdfReference(pagesId));
        page.Set(PdfName.Contents, new PdfReference(contentId));
        page.Set(PdfName.Intern("Resources"), resources);

        byte[] content = Encoding.ASCII.GetBytes(pageContent);
        PdfDictionary contentDict = new PdfDictionary();
        contentDict.Set(PdfName.Length, content.Length);

        byte[] formBytes = Encoding.ASCII.GetBytes(formContent);
        PdfDictionary formDict = new PdfDictionary();
        formDict.Set(PdfName.Type, PdfName.Intern("XObject"));
        formDict.Set(PdfName.Intern("Subtype"), PdfName.Intern("Form"));
        formDict.Set(PdfName.Length, formBytes.Length);
        formDict.Set(PdfName.Intern("BBox"), new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(612), new PdfInteger(792),
        }));

        // Parse the matrix string "[a b c d e f]".
        string trimmed = formMatrix.Trim('[', ']');
        string[] nums = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        PdfPrimitive[] matrixVals = new PdfPrimitive[6];
        for (int i = 0; i < 6; i++)
        {
            matrixVals[i] = new PdfReal(double.Parse(nums[i], CultureInfo.InvariantCulture));
        }

        formDict.Set(PdfName.Intern("Matrix"), new PdfArray(matrixVals));

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(catalogId, catalog),
            new PdfIndirectObject(pagesId, pages),
            new PdfIndirectObject(pageId, page),
            new PdfIndirectObject(contentId, new PdfStream(contentDict, content)),
            new PdfIndirectObject(formId, new PdfStream(formDict, formBytes)),
        };

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        ms.Position = 0;
        return ms;
    }
}
