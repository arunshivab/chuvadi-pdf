// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// Tests for AnnotationFlattener (LA-28): bakes annotation/form appearance
// streams into page content, drops live annotations, strips a fully-baked
// AcroForm, and honours the kind/keep-vs-remove options.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

public sealed class AnnotationFlattenTests
{
    // ── Default flatten ───────────────────────────────────────────────────

    [Fact]
    public void Default_BakesWidgetAndStamp_RemovesAcroForm_KeepsLinkLive()
    {
        byte[] output = Flatten(BuildFixture(), AnnotationFlattenOptions.Default);

        using MemoryStream ms = new MemoryStream(output);
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true);
        PdfPage page = doc.Pages[0];

        PdfArray? annots = AnnotsOf(doc, page);
        annots.Should().NotBeNull();
        annots!.Count.Should().Be(1);

        PdfDictionary keptLink = (PdfDictionary)doc.Objects.Resolve(annots[0]);
        keptLink.GetName(PdfName.Subtype)!.Value.Should().Be("Link");

        doc.Catalog.ContainsKey(PdfName.Intern("AcroForm")).Should().BeFalse();
    }

    [Fact]
    public void Default_PlacesAppearancesPerRectMapping()
    {
        byte[] output = Flatten(BuildFixture(), AnnotationFlattenOptions.Default);

        using MemoryStream ms = new MemoryStream(output);
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true);
        string content = ReadAllContent(doc, doc.Pages[0]);

        // Widget BBox [0 0 50 15] -> Rect [100 100 150 130]: sx=1, sy=2, t=(100,100).
        content.Should().Contain("1 0 0 2 100 100 cm");

        // Stamp BBox [0 0 75 50], Matrix [1 0 0 1 10 10] -> Rect [250 600 400 700]:
        // transformed box [10 10 85 60]; sx=2, sy=2, t=(230,580).
        content.Should().Contain("2 0 0 2 230 580 cm");
        content.Should().Contain("Do");
    }

    [Fact]
    public void Default_PreservesAppearanceFormXObjects()
    {
        byte[] output = Flatten(BuildFixture(), AnnotationFlattenOptions.Default);

        using MemoryStream ms = new MemoryStream(output);
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true);
        PdfDictionary? xobjects = PageXObjects(doc, doc.Pages[0]);

        xobjects.Should().NotBeNull();
        xobjects!.Count.Should().Be(2);

        bool foundWidgetAppearance = false;
        foreach (PdfName key in xobjects.Keys)
        {
            PdfStream form = (PdfStream)doc.Objects.Resolve(xobjects[key]);
            form.Dictionary.GetName(PdfName.Subtype)!.Value.Should().Be("Form");
            form.Dictionary.ContainsKey(PdfName.Intern("BBox")).Should().BeTrue();

            if (Encoding.Latin1.GetString(form.RawBytes).Contains("0 0 50 15 re", StringComparison.Ordinal))
            {
                foundWidgetAppearance = true;
            }
        }

        foundWidgetAppearance.Should().BeTrue();
    }

    [Fact]
    public void Default_PreservesExistingPageContentInsideGuard()
    {
        byte[] output = Flatten(BuildFixture(), AnnotationFlattenOptions.Default);

        using MemoryStream ms = new MemoryStream(output);
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true);
        string content = ReadAllContent(doc, doc.Pages[0]);

        // Original page content survives verbatim, wrapped in a balanced q … Q.
        content.Should().Contain("0 0 100 100 re");
        content.Should().Contain("q");
        content.Should().Contain("Q");
    }

    // ── Kind selection ────────────────────────────────────────────────────

    [Fact]
    public void MarkupOnly_BakesStamp_KeepsWidgetAndAcroForm()
    {
        AnnotationFlattenOptions options = new AnnotationFlattenOptions
        {
            Kinds = AnnotationFlattenKinds.Markup,
        };

        byte[] output = Flatten(BuildFixture(), options);

        using MemoryStream ms = new MemoryStream(output);
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true);
        PdfPage page = doc.Pages[0];

        PdfArray? annots = AnnotsOf(doc, page);
        annots.Should().NotBeNull();
        annots!.Count.Should().Be(2); // widget + link kept live

        doc.Catalog.ContainsKey(PdfName.Intern("AcroForm")).Should().BeTrue();

        string content = ReadAllContent(doc, page);
        content.Should().Contain("2 0 0 2 230 580 cm");      // stamp baked
        content.Should().NotContain("1 0 0 2 100 100 cm");   // widget left live
    }

    [Fact]
    public void FormFieldsOnly_BakesWidget_RemovesAcroForm_KeepsStampAndLink()
    {
        AnnotationFlattenOptions options = new AnnotationFlattenOptions
        {
            Kinds = AnnotationFlattenKinds.FormFields,
        };

        byte[] output = Flatten(BuildFixture(), options);

        using MemoryStream ms = new MemoryStream(output);
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true);
        PdfPage page = doc.Pages[0];

        PdfArray? annots = AnnotsOf(doc, page);
        annots.Should().NotBeNull();
        annots!.Count.Should().Be(2); // stamp + link kept live

        doc.Catalog.ContainsKey(PdfName.Intern("AcroForm")).Should().BeFalse();

        string content = ReadAllContent(doc, page);
        content.Should().Contain("1 0 0 2 100 100 cm");      // widget baked
        content.Should().NotContain("2 0 0 2 230 580 cm");   // stamp left live
    }

    // ── Keep-vs-remove ────────────────────────────────────────────────────

    [Fact]
    public void DropRemainingAnnotations_RemovesEverythingLive()
    {
        AnnotationFlattenOptions options = new AnnotationFlattenOptions
        {
            Kinds = AnnotationFlattenKinds.All,
            DropRemainingAnnotations = true,
        };

        byte[] output = Flatten(BuildFixture(), options);

        using MemoryStream ms = new MemoryStream(output);
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true);
        PdfPage page = doc.Pages[0];

        page.Dictionary.ContainsKey(PdfName.Intern("Annots")).Should().BeFalse();
        doc.Catalog.ContainsKey(PdfName.Intern("AcroForm")).Should().BeFalse();
    }

    // ── Invisible annotations ─────────────────────────────────────────────

    [Fact]
    public void HiddenAnnotation_IsDropped_NotBaked()
    {
        byte[] output = Flatten(BuildHiddenStampFixture(), AnnotationFlattenOptions.Default);

        using MemoryStream ms = new MemoryStream(output);
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true);
        PdfPage page = doc.Pages[0];

        page.Dictionary.ContainsKey(PdfName.Intern("Annots")).Should().BeFalse();

        string content = ReadAllContent(doc, page);
        content.Should().NotContain("Do");
        content.Should().NotContain("CvFlatAp");
    }

    // ── Robustness ────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrips_PreservesPageCount()
    {
        byte[] output = Flatten(BuildFixture(), AnnotationFlattenOptions.Default);

        using MemoryStream ms = new MemoryStream(output);
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true);
        doc.PageCount.Should().Be(1);
    }

    [Fact]
    public void NullArguments_Throw()
    {
        using MemoryStream input = new MemoryStream(BuildFixture());
        using PdfDocument doc = PdfDocument.Open(input, leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        ((Action)(() => AnnotationFlattener.Flatten(null!, doc))).Should().Throw<ArgumentNullException>();
        ((Action)(() => AnnotationFlattener.Flatten(output, null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => AnnotationFlattener.Flatten(output, doc, null!))).Should().Throw<ArgumentNullException>();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    // ── Orphan cleanup ─────────────────────────────────────────────────────

    [Fact]
    public void Default_DropsOrphanedAppearanceStreams()
    {
        // After flattening, the widget annotation's /Annots reference is removed.
        // Its appearance stream (the widget-AP object) becomes unreachable from the
        // page graph and must not be carried into the output. Every object the
        // writer emits should be reachable from the catalog.
        byte[] output = Flatten(BuildFixture(), AnnotationFlattenOptions.Default);

        using MemoryStream ms = new MemoryStream(output);
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true);

        HashSet<int> reachable = CollectReachable(doc);

        foreach (PdfIndirectObject obj in doc.Objects.Objects)
        {
            reachable.Should().Contain(
                obj.Id.ObjectNumber,
                "object {0} is present in the output and must be reachable from the catalog",
                obj.Id.ObjectNumber);
        }
    }

    private static HashSet<int> CollectReachable(PdfDocument doc)
    {
        HashSet<int> visited = new HashSet<int>();
        PdfDictionary catalog = doc.Catalog;

        // Mark the catalog's own number, then walk its references.
        foreach (PdfIndirectObject obj in doc.Objects.Objects)
        {
            if (ReferenceEquals(obj.Value, catalog))
            {
                visited.Add(obj.Id.ObjectNumber);
                break;
            }
        }

        VisitReachable(doc.Objects, catalog, visited);
        return visited;
    }

    private static void VisitReachable(PdfObjectStore store, PdfPrimitive? primitive, HashSet<int> visited)
    {
        if (primitive is null)
        {
            return;
        }

        if (primitive is PdfReference reference)
        {
            if (!visited.Add(reference.ObjectId.ObjectNumber))
            {
                return;
            }

            VisitReachable(store, store.Resolve(reference), visited);
            return;
        }

        switch (primitive)
        {
            case PdfDictionary dictionary:
                foreach (KeyValuePair<PdfName, PdfPrimitive> entry in dictionary)
                {
                    VisitReachable(store, entry.Value, visited);
                }

                break;
            case PdfArray array:
                for (int i = 0; i < array.Count; i++)
                {
                    VisitReachable(store, array[i], visited);
                }

                break;
            case PdfStream stream:
                foreach (KeyValuePair<PdfName, PdfPrimitive> entry in stream.Dictionary)
                {
                    VisitReachable(store, entry.Value, visited);
                }

                break;
            default:
                break;
        }
    }

    private static byte[] Flatten(byte[] pdf, AnnotationFlattenOptions options)
    {
        using MemoryStream input = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Open(input, leaveOpen: true);
        using MemoryStream output = new MemoryStream();
        AnnotationFlattener.Flatten(output, doc, options);
        return output.ToArray();
    }

    private static PdfArray? AnnotsOf(PdfDocument doc, PdfPage page)
    {
        return page.Dictionary.TryGetValue(PdfName.Intern("Annots"), out PdfPrimitive? prim)
            ? doc.Objects.ResolveAs<PdfArray>(prim)
            : null;
    }

    private static PdfDictionary? PageXObjects(PdfDocument doc, PdfPage page)
    {
        return page.Resources is PdfDictionary resources
            && resources.TryGetValue(PdfName.XObject, out PdfPrimitive? prim)
            ? doc.Objects.ResolveAs<PdfDictionary>(prim)
            : null;
    }

    private static string ReadAllContent(PdfDocument doc, PdfPage page)
    {
        StringBuilder builder = new StringBuilder();
        PdfPrimitive? contents = page.Contents;
        if (contents is null)
        {
            return string.Empty;
        }

        PdfPrimitive resolved = doc.Objects.Resolve(contents);
        if (resolved is PdfArray array)
        {
            for (int i = 0; i < array.Count; i++)
            {
                if (doc.Objects.Resolve(array[i]) is PdfStream stream)
                {
                    builder.Append(Encoding.Latin1.GetString(stream.RawBytes));
                    builder.Append('\n');
                }
            }
        }
        else if (resolved is PdfStream single)
        {
            builder.Append(Encoding.Latin1.GetString(single.RawBytes));
        }

        return builder.ToString();
    }

    // ── Fixtures ──────────────────────────────────────────────────────────

    private static byte[] BuildFixture()
    {
        // Object ids: 1 catalog, 2 pages, 3 page, 4 content, 5 widget, 6 stamp,
        // 7 link, 8 widget-AP, 9 stamp-AP, 10 AcroForm.
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);
        PdfObjectId contentId = new PdfObjectId(4, 0);
        PdfObjectId widgetId = new PdfObjectId(5, 0);
        PdfObjectId stampId = new PdfObjectId(6, 0);
        PdfObjectId linkId = new PdfObjectId(7, 0);
        PdfObjectId widgetApId = new PdfObjectId(8, 0);
        PdfObjectId stampApId = new PdfObjectId(9, 0);
        PdfObjectId acroFormId = new PdfObjectId(10, 0);

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>();

        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(pagesId));
        catalog.Set(PdfName.Intern("AcroForm"), new PdfReference(acroFormId));
        objects.Add(new PdfIndirectObject(catalogId, catalog));

        PdfDictionary pages = new PdfDictionary();
        pages.Set(PdfName.Type, PdfName.Pages);
        pages.Set(PdfName.Kids, new PdfArray([new PdfReference(pageId)]));
        pages.Set(PdfName.Count, 1);
        pages.Set(PdfName.MediaBox, Box(0, 0, 612, 792));
        objects.Add(new PdfIndirectObject(pagesId, pages));

        PdfDictionary page = new PdfDictionary();
        page.Set(PdfName.Type, PdfName.Page);
        page.Set(PdfName.Parent, new PdfReference(pagesId));
        page.Set(PdfName.MediaBox, Box(0, 0, 612, 792));
        page.Set(PdfName.Contents, new PdfReference(contentId));
        page.Set(PdfName.Resources, new PdfDictionary());
        page.Set(PdfName.Intern("Annots"), new PdfArray([
            new PdfReference(widgetId), new PdfReference(stampId), new PdfReference(linkId)
        ]));
        objects.Add(new PdfIndirectObject(pageId, page));

        objects.Add(ContentStream(contentId, "0 0 0 rg 0 0 100 100 re f"));

        // Widget (form field) with a normal appearance.
        PdfDictionary widget = new PdfDictionary();
        widget.Set(PdfName.Type, PdfName.Intern("Annot"));
        widget.Set(PdfName.Subtype, PdfName.Intern("Widget"));
        widget.Set(PdfName.Intern("FT"), PdfName.Intern("Tx"));
        widget.Set(PdfName.Intern("T"), new PdfString("field1"));
        widget.Set(PdfName.Intern("Rect"), Box(100, 100, 150, 130));
        widget.Set(PdfName.Intern("AP"), AppearanceDict(widgetApId));
        objects.Add(new PdfIndirectObject(widgetId, widget));

        // Stamp (markup) with a non-identity appearance matrix.
        PdfDictionary stamp = new PdfDictionary();
        stamp.Set(PdfName.Type, PdfName.Intern("Annot"));
        stamp.Set(PdfName.Subtype, PdfName.Intern("Stamp"));
        stamp.Set(PdfName.Intern("Name"), PdfName.Intern("Draft"));
        stamp.Set(PdfName.Intern("Rect"), Box(250, 600, 400, 700));
        stamp.Set(PdfName.Intern("AP"), AppearanceDict(stampApId));
        objects.Add(new PdfIndirectObject(stampId, stamp));

        // Link with no appearance (cannot be baked).
        PdfDictionary link = new PdfDictionary();
        link.Set(PdfName.Type, PdfName.Intern("Annot"));
        link.Set(PdfName.Subtype, PdfName.Intern("Link"));
        link.Set(PdfName.Intern("Rect"), Box(50, 50, 100, 70));
        objects.Add(new PdfIndirectObject(linkId, link));

        objects.Add(FormXObject(widgetApId, Box(0, 0, 50, 15), null, "1 0 0 rg 0 0 50 15 re f"));
        objects.Add(FormXObject(stampApId, Box(0, 0, 75, 50), Box(1, 0, 0, 1), "0 0 1 rg 0 0 75 50 re f", 10, 10));

        PdfDictionary acroForm = new PdfDictionary();
        acroForm.Set(PdfName.Intern("Fields"), new PdfArray([new PdfReference(widgetId)]));
        acroForm.Set(PdfName.Intern("NeedAppearances"), true);
        objects.Add(new PdfIndirectObject(acroFormId, acroForm));

        return Write(objects, catalogId);
    }

    private static byte[] BuildHiddenStampFixture()
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);
        PdfObjectId contentId = new PdfObjectId(4, 0);
        PdfObjectId stampId = new PdfObjectId(5, 0);
        PdfObjectId stampApId = new PdfObjectId(6, 0);

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>();

        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(pagesId));
        objects.Add(new PdfIndirectObject(catalogId, catalog));

        PdfDictionary pages = new PdfDictionary();
        pages.Set(PdfName.Type, PdfName.Pages);
        pages.Set(PdfName.Kids, new PdfArray([new PdfReference(pageId)]));
        pages.Set(PdfName.Count, 1);
        pages.Set(PdfName.MediaBox, Box(0, 0, 612, 792));
        objects.Add(new PdfIndirectObject(pagesId, pages));

        PdfDictionary page = new PdfDictionary();
        page.Set(PdfName.Type, PdfName.Page);
        page.Set(PdfName.Parent, new PdfReference(pagesId));
        page.Set(PdfName.MediaBox, Box(0, 0, 612, 792));
        page.Set(PdfName.Contents, new PdfReference(contentId));
        page.Set(PdfName.Resources, new PdfDictionary());
        page.Set(PdfName.Intern("Annots"), new PdfArray([new PdfReference(stampId)]));
        objects.Add(new PdfIndirectObject(pageId, page));

        objects.Add(ContentStream(contentId, "0 0 0 rg 0 0 100 100 re f"));

        PdfDictionary stamp = new PdfDictionary();
        stamp.Set(PdfName.Type, PdfName.Intern("Annot"));
        stamp.Set(PdfName.Subtype, PdfName.Intern("Stamp"));
        stamp.Set(PdfName.Intern("Rect"), Box(250, 600, 400, 700));
        stamp.Set(PdfName.Intern("F"), 2); // Hidden flag
        stamp.Set(PdfName.Intern("AP"), AppearanceDict(stampApId));
        objects.Add(new PdfIndirectObject(stampId, stamp));

        objects.Add(FormXObject(stampApId, Box(0, 0, 75, 50), null, "0 0 1 rg 0 0 75 50 re f"));

        return Write(objects, catalogId);
    }

    private static PdfDictionary AppearanceDict(PdfObjectId normalId)
    {
        PdfDictionary ap = new PdfDictionary();
        ap.Set(PdfName.Intern("N"), new PdfReference(normalId));
        return ap;
    }

    private static PdfIndirectObject ContentStream(PdfObjectId id, string content)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(content);
        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Length, bytes.Length);
        return new PdfIndirectObject(id, new PdfStream(dict, bytes));
    }

    private static PdfIndirectObject FormXObject(
        PdfObjectId id, PdfArray bbox, PdfArray? matrixDirection, string content, double tx = 0, double ty = 0)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(content);
        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Type, PdfName.XObject);
        dict.Set(PdfName.Subtype, PdfName.Intern("Form"));
        dict.Set(PdfName.Intern("BBox"), bbox);
        if (matrixDirection is not null)
        {
            dict.Set(PdfName.Intern("Matrix"), new PdfArray([
                matrixDirection[0], matrixDirection[1], matrixDirection[2], matrixDirection[3],
                new PdfReal(tx), new PdfReal(ty)
            ]));
        }

        dict.Set(PdfName.Resources, new PdfDictionary());
        dict.Set(PdfName.Length, bytes.Length);
        return new PdfIndirectObject(id, new PdfStream(dict, bytes));
    }

    private static PdfArray Box(double a, double b, double c, double d)
    {
        return new PdfArray([new PdfReal(a), new PdfReal(b), new PdfReal(c), new PdfReal(d)]);
    }

    private static byte[] Write(List<PdfIndirectObject> objects, PdfObjectId catalogId)
    {
        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        using MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        return ms.ToArray();
    }
}
