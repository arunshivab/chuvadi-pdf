// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.32.0 (D-234, Reader ask M2) — flatten only what a printer (or a
//        viewer) shows, by each annotation's flags.

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

namespace Chuvadi.Pdf.Operations.Tests;

public sealed class FlattenMode3320Tests
{
    // Annotation flags (Table 165).
    private const int Hidden = 2;
    private const int Print = 4;
    private const int NoView = 32;

    [Fact]
    public void PrintMode_BakesWhatPrints_AndMatchesThePrintRender()
    {
        byte[] source = BuildFixture();
        byte[] flat = Flatten(source, new AnnotationFlattenOptions { Mode = AnnotationMode.Print });

        PixelBuffer expected = Render(source, AnnotationMode.Print);
        PixelBuffer actual = Render(flat, AnnotationMode.None);
        actual.Pixels.SequenceEqual(expected.Pixels).Should().BeTrue("the flattened page shows exactly what prints");

        IsWhite(actual.GetPixelBgra(35, 265)).Should().BeTrue("no Print flag: not baked");
        IsGreen(actual.GetPixelBgra(95, 265)).Should().BeTrue("Print: baked");
        IsBlue(actual.GetPixelBgra(155, 265)).Should().BeTrue("Print + NoView (print-only): baked");
        IsWhite(actual.GetPixelBgra(215, 265)).Should().BeTrue("Hidden: not baked");
        IsWhite(actual.GetPixelBgra(35, 175)).Should().BeTrue("Popup: not baked");
    }

    [Fact]
    public void PrintMode_RemovesEveryAnnotationItLeavesOut()
    {
        byte[] flat = Flatten(BuildFixture(), new AnnotationFlattenOptions { Mode = AnnotationMode.Print });

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(flat), leaveOpen: false);
        doc.Pages[0].Dictionary.ContainsKey(PdfName.Intern("Annots")).Should().BeFalse();
        doc.Catalog.ContainsKey(PdfName.Intern("AcroForm")).Should().BeFalse("the only widget did not print and was removed");
    }

    [Fact]
    public void ViewMode_BakesWhatShowsOnScreen_AndMatchesTheViewRender()
    {
        byte[] source = BuildFixture();
        byte[] flat = Flatten(source, new AnnotationFlattenOptions { Mode = AnnotationMode.View });

        PixelBuffer expected = Render(source, AnnotationMode.View);
        PixelBuffer actual = Render(flat, AnnotationMode.None);
        actual.Pixels.SequenceEqual(expected.Pixels).Should().BeTrue();

        IsRed(actual.GetPixelBgra(35, 265)).Should().BeTrue("no flags: shown on screen");
        IsBlue(actual.GetPixelBgra(155, 265)).Should().BeFalse("NoView: not shown on screen");
    }

    [Fact]
    public void NoMode_KeepsTheEarlierRule()
    {
        byte[] flat = Flatten(BuildFixture(), AnnotationFlattenOptions.Default);
        PixelBuffer actual = Render(flat, AnnotationMode.None);

        IsRed(actual.GetPixelBgra(35, 265)).Should().BeTrue("without a mode the Print flag is not consulted");
        IsGreen(actual.GetPixelBgra(95, 265)).Should().BeTrue();
        IsWhite(actual.GetPixelBgra(155, 265)).Should().BeTrue("SkipHiddenAndNoView drops print-only annotations");
        IsWhite(actual.GetPixelBgra(215, 265)).Should().BeTrue();
    }

    [Fact]
    public void KindsStillApply_InPrintMode()
    {
        byte[] flat = Flatten(BuildFixture(), new AnnotationFlattenOptions { Mode = AnnotationMode.Print, Kinds = AnnotationFlattenKinds.FormFields });

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(flat), leaveOpen: false);
        PdfArray annots = (PdfArray)doc.Objects.Resolve(doc.Pages[0].Dictionary[PdfName.Intern("Annots")]);
        annots.Count.Should().Be(5, "the five non-widget annotations are not a selected kind and stay live");
    }

    [Theory]
    [InlineData(AnnotationMode.FormFieldsOnly)]
    [InlineData(AnnotationMode.None)]
    [InlineData((AnnotationMode)99)]
    public void OtherModes_Throw(AnnotationMode mode)
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(BuildFixture()), leaveOpen: false);
        Action act = () => AnnotationFlattener.Flatten(new MemoryStream(), doc, new AnnotationFlattenOptions { Mode = mode });
        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("options");
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static byte[] Flatten(byte[] pdf, AnnotationFlattenOptions options)
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        using MemoryStream output = new MemoryStream();
        AnnotationFlattener.Flatten(output, doc, options);
        return output.ToArray();
    }

    private static PixelBuffer Render(byte[] pdf, AnnotationMode mode)
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        return new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72, AnnotationMode = mode }).Rasterize(doc.Pages[0]);
    }

    private static bool IsRed((byte B, byte G, byte R, byte A) p) => p.R > 200 && p.G < 60 && p.B < 60;

    private static bool IsGreen((byte B, byte G, byte R, byte A) p) => p.G > 100 && p.R < 60 && p.B < 60;

    private static bool IsBlue((byte B, byte G, byte R, byte A) p) => p.B > 200 && p.R < 60 && p.G < 60;

    private static bool IsWhite((byte B, byte G, byte R, byte A) p) => p.R > 240 && p.G > 240 && p.B > 240;

    // A 300x300 page with six annotations, each drawing a filled square:
    //   red   Stamp, no flags            [10 10 60 60]   (y measured from the bottom)
    //   green Stamp, Print               [70 10 120 60]
    //   blue  Stamp, Print + NoView      [130 10 180 60]
    //   black Stamp, Hidden + Print      [190 10 240 60]
    //   yellow Popup, Print, with an /AP [10 100 60 150]
    //   magenta Widget, no flags         [70 100 120 150]  (the AcroForm's only field)
    private static byte[] BuildFixture()
    {
        List<PdfIndirectObject> objects = new List<PdfIndirectObject>();
        int next = 10;
        List<PdfPrimitive> annots = new List<PdfPrimitive>();
        PdfReference? widgetRef = null;

        void Add(string subtype, int flags, int x, int y, string colour)
        {
            PdfObjectId apId = new PdfObjectId(next++, 0);
            byte[] ap = Encoding.ASCII.GetBytes(colour + " rg 0 0 50 50 re f");
            PdfDictionary apDict = new PdfDictionary();
            apDict.Set(PdfName.Type, PdfName.XObject);
            apDict.Set(PdfName.Subtype, PdfName.Intern("Form"));
            apDict.Set(PdfName.Intern("BBox"), Box(0, 0, 50, 50));
            apDict.Set(PdfName.Length, ap.Length);
            objects.Add(new PdfIndirectObject(apId, new PdfStream(apDict, ap)));

            PdfDictionary n = new PdfDictionary();
            n.Set(PdfName.Intern("N"), new PdfReference(apId));
            PdfDictionary annot = new PdfDictionary();
            annot.Set(PdfName.Type, PdfName.Intern("Annot"));
            annot.Set(PdfName.Subtype, PdfName.Intern(subtype));
            annot.Set(PdfName.Intern("Rect"), Box(x, y, x + 50, y + 50));
            annot.Set(PdfName.Intern("F"), flags);
            annot.Set(PdfName.Intern("AP"), n);
            if (subtype == "Widget")
            {
                annot.Set(PdfName.Intern("T"), new PdfString(Encoding.ASCII.GetBytes("f1")));
            }

            PdfObjectId annotId = new PdfObjectId(next++, 0);
            objects.Add(new PdfIndirectObject(annotId, annot));
            annots.Add(new PdfReference(annotId));
            if (subtype == "Widget")
            {
                widgetRef = new PdfReference(annotId);
            }
        }

        Add("Stamp", 0, 10, 10, "1 0 0");
        Add("Stamp", Print, 70, 10, "0 0.6 0");
        Add("Stamp", Print | NoView, 130, 10, "0 0 1");
        Add("Stamp", Hidden | Print, 190, 10, "0 0 0");
        Add("Popup", Print, 10, 100, "1 1 0");
        Add("Widget", 0, 70, 100, "1 0 1");

        PdfDictionary acroForm = new PdfDictionary();
        acroForm.Set(PdfName.Intern("Fields"), new PdfArray(new PdfPrimitive[] { widgetRef! }));

        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(2, 0));
        catalog.Set(PdfName.Intern("AcroForm"), acroForm);

        PdfDictionary pages = new PdfDictionary();
        pages.Set(PdfName.Type, PdfName.Pages);
        pages.Set(PdfName.Kids, new PdfArray(new PdfPrimitive[] { new PdfReference(3, 0) }));
        pages.Set(PdfName.Count, 1);

        byte[] content = Encoding.ASCII.GetBytes("q Q");
        PdfDictionary contentDict = new PdfDictionary();
        contentDict.Set(PdfName.Length, content.Length);

        PdfDictionary page = new PdfDictionary();
        page.Set(PdfName.Type, PdfName.Page);
        page.Set(PdfName.Intern("Parent"), new PdfReference(2, 0));
        page.Set(PdfName.MediaBox, Box(0, 0, 300, 300));
        page.Set(PdfName.Resources, new PdfDictionary());
        page.Set(PdfName.Contents, new PdfReference(4, 0));
        page.Set(PdfName.Intern("Annots"), new PdfArray(annots));

        objects.Add(new PdfIndirectObject(new PdfObjectId(1, 0), catalog));
        objects.Add(new PdfIndirectObject(new PdfObjectId(2, 0), pages));
        objects.Add(new PdfIndirectObject(new PdfObjectId(3, 0), page));
        objects.Add(new PdfIndirectObject(new PdfObjectId(4, 0), new PdfStream(contentDict, content)));

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(1, 0));
        using MemoryStream output = new MemoryStream();
        PdfWriter.Write(output, objects, trailer);
        return output.ToArray();
    }

    private static PdfArray Box(int x1, int y1, int x2, int y2) =>
        new PdfArray(new PdfPrimitive[] { new PdfInteger(x1), new PdfInteger(y1), new PdfInteger(x2), new PdfInteger(y2) });
}
