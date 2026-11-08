// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.32.0 (D-208 item 4) — destination kind and coordinates on links,
//        outlines and named destinations

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Forms;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Annotations.Tests;

public sealed class Destinations3320Tests
{
    [Fact]
    public void XyzLink_HasLeftTopAndZoom()
    {
        PdfDestination d = Link("xyz").Destination!;

        d.PageIndex.Should().Be(1);
        d.Kind.Should().Be(PdfDestinationKind.XYZ);
        d.Left.Should().Be(10);
        d.Top.Should().Be(700);
        d.Zoom.Should().Be(1.5);
    }

    [Fact]
    public void XyzLink_WithNulls_LeavesThemNull()
    {
        PdfDestination d = Link("xyz-null").Destination!;

        d.Kind.Should().Be(PdfDestinationKind.XYZ);
        d.Left.Should().BeNull();
        d.Top.Should().Be(500);
        d.Zoom.Should().BeNull();
    }

    [Fact]
    public void FitRLink_HasTheRectangle()
    {
        PdfDestination d = Link("fitr").Destination!;

        d.Kind.Should().Be(PdfDestinationKind.FitR);
        (d.Left, d.Bottom, d.Right, d.Top).Should().Be((50.0, 60.0, 250.0, 360.0));
        d.Zoom.Should().BeNull();
    }

    [Theory]
    [InlineData("fit", PdfDestinationKind.Fit)]
    [InlineData("fith", PdfDestinationKind.FitH)]
    [InlineData("fitv", PdfDestinationKind.FitV)]
    [InlineData("fitb", PdfDestinationKind.FitB)]
    [InlineData("fitbh", PdfDestinationKind.FitBH)]
    [InlineData("fitbv", PdfDestinationKind.FitBV)]
    [InlineData("unknown-kind", PdfDestinationKind.XYZ)]
    public void EachKind_IsRead(string contents, PdfDestinationKind kind)
    {
        Link(contents).Destination!.Kind.Should().Be(kind);
    }

    [Fact]
    public void FitHAndFitV_CarryTheirOneCoordinate()
    {
        Link("fith").Destination!.Top.Should().Be(400);
        Link("fitv").Destination!.Left.Should().Be(30);
    }

    [Fact]
    public void NamedDestinationLink_HasTheNamedKind()
    {
        PdfDestination d = Link("named").Destination!;

        d.PageIndex.Should().Be(1);
        d.Kind.Should().Be(PdfDestinationKind.FitH);
        d.Top.Should().Be(123);
    }

    [Fact]
    public void Destination_AgreesWithDestinationPageIndex()
    {
        using PdfDocument doc = PdfDocument.Open(Build(), leaveOpen: false);
        foreach (LinkAnnotation link in AnnotationReader.GetAnnotations(doc, 0).OfType<LinkAnnotation>())
        {
            link.Destination!.PageIndex.Should().Be(link.DestinationPageIndex);
        }
    }

    [Fact]
    public void Outline_HasItsDestination()
    {
        using PdfDocument doc = PdfDocument.Open(Build(), leaveOpen: false);
        OutlineItem item = OutlineReader.GetOutlines(doc).Should().ContainSingle().Subject;

        item.DestinationPageIndex.Should().Be(1);
        item.Destination!.Kind.Should().Be(PdfDestinationKind.XYZ);
        item.Destination.Top.Should().Be(800);
    }

    [Fact]
    public void NamedDestinations_AreListed_NameTreeFirst()
    {
        using PdfDocument doc = PdfDocument.Open(Build(), leaveOpen: false);
        IReadOnlyDictionary<string, PdfDestination> named = DestinationReader.GetNamedDestinations(doc);

        named.Keys.Should().BeEquivalentTo(new[] { "chap2", "intro" });
        named["chap2"].Kind.Should().Be(PdfDestinationKind.FitH);
        named["intro"].PageIndex.Should().Be(0);
        named["intro"].Kind.Should().Be(PdfDestinationKind.Fit);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static LinkAnnotation Link(string contents)
    {
        using PdfDocument doc = PdfDocument.Open(Build(), leaveOpen: false);
        return AnnotationReader.GetAnnotations(doc, 0).OfType<LinkAnnotation>().Single(l => l.Contents == contents);
    }

    private static PdfArray Dest(int pageObject, string kind, params PdfPrimitive[] operands)
    {
        List<PdfPrimitive> items = new List<PdfPrimitive> { new PdfReference(new PdfObjectId(pageObject, 0)), PdfName.Intern(kind) };
        items.AddRange(operands);
        return new PdfArray(items.ToArray());
    }

    private static PdfPrimitive N(double v) => new PdfReal(v);

    private static MemoryStream Build()
    {
        const int Page0 = 5;
        const int Page1 = 6;

        PdfDictionary pages = new PdfDictionary();
        pages.Set(PdfName.Type, PdfName.Pages);
        pages.Set(PdfName.Kids, new PdfArray(new PdfPrimitive[] { new PdfReference(new PdfObjectId(Page0, 0)), new PdfReference(new PdfObjectId(Page1, 0)) }));
        pages.Set(PdfName.Count, 2);
        pages.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[] { new PdfInteger(0), new PdfInteger(0), new PdfInteger(600), new PdfInteger(800) }));

        PdfDictionary destsTree = new PdfDictionary();
        destsTree.Set(PdfName.Intern("Names"), new PdfArray(new PdfPrimitive[]
        {
            new PdfString(Encoding.ASCII.GetBytes("chap2")), Dest(Page1, "FitH", N(123)),
        }));
        PdfDictionary names = new PdfDictionary();
        names.Set(PdfName.Intern("Dests"), destsTree);

        PdfDictionary legacy = new PdfDictionary();
        legacy.Set(PdfName.Intern("intro"), Dest(Page0, "Fit"));

        PdfDictionary outlineItem = new PdfDictionary();
        outlineItem.Set(PdfName.Intern("Title"), new PdfString(Encoding.ASCII.GetBytes("Second")));
        outlineItem.Set(PdfName.Intern("Dest"), Dest(Page1, "XYZ", PdfNull.Value, N(800), PdfNull.Value));
        PdfDictionary outlines = new PdfDictionary();
        outlines.Set(PdfName.Type, PdfName.Outlines);
        outlines.Set(PdfName.Intern("First"), new PdfReference(new PdfObjectId(7, 0)));
        outlines.Set(PdfName.Intern("Last"), new PdfReference(new PdfObjectId(7, 0)));
        outlineItem.Set(PdfName.Intern("Parent"), new PdfReference(new PdfObjectId(8, 0)));

        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(new PdfObjectId(2, 0)));
        catalog.Set(PdfName.Intern("Names"), new PdfReference(new PdfObjectId(3, 0)));
        catalog.Set(PdfName.Intern("Dests"), new PdfReference(new PdfObjectId(4, 0)));
        catalog.Set(PdfName.Outlines, new PdfReference(new PdfObjectId(8, 0)));

        PdfArray annots = new PdfArray(new PdfPrimitive[]
        {
            Link("xyz", Dest(Page1, "XYZ", N(10), N(700), N(1.5))),
            Link("xyz-null", Dest(Page1, "XYZ", PdfNull.Value, N(500), new PdfInteger(0))),
            Link("fitr", Dest(Page1, "FitR", N(50), N(60), N(250), N(360))),
            Link("fit", Dest(Page1, "Fit")),
            Link("fith", Dest(Page1, "FitH", N(400))),
            Link("fitv", Dest(Page1, "FitV", N(30))),
            Link("fitb", Dest(Page1, "FitB")),
            Link("fitbh", Dest(Page1, "FitBH", N(1))),
            Link("fitbv", Dest(Page1, "FitBV", N(2))),
            Link("unknown-kind", Dest(Page1, "Zoomy", N(3))),
            Link("named", new PdfString(Encoding.ASCII.GetBytes("chap2"))),
        });

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(new PdfObjectId(1, 0), catalog),
            new PdfIndirectObject(new PdfObjectId(2, 0), pages),
            new PdfIndirectObject(new PdfObjectId(3, 0), names),
            new PdfIndirectObject(new PdfObjectId(4, 0), legacy),
            new PdfIndirectObject(new PdfObjectId(Page0, 0), Page(annots)),
            new PdfIndirectObject(new PdfObjectId(Page1, 0), Page(null)),
            new PdfIndirectObject(new PdfObjectId(7, 0), outlineItem),
            new PdfIndirectObject(new PdfObjectId(8, 0), outlines),
        };

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(new PdfObjectId(1, 0)));
        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects.ToArray(), trailer);
        ms.Position = 0;
        return ms;
    }

    private static PdfDictionary Page(PdfArray? annots)
    {
        PdfDictionary page = new PdfDictionary();
        page.Set(PdfName.Type, PdfName.Page);
        page.Set(PdfName.Intern("Parent"), new PdfReference(new PdfObjectId(2, 0)));
        if (annots is not null)
        {
            page.Set(PdfName.Intern("Annots"), annots);
        }

        return page;
    }

    private static PdfDictionary Link(string contents, PdfPrimitive dest)
    {
        PdfDictionary link = new PdfDictionary();
        link.Set(PdfName.Type, PdfName.Intern("Annot"));
        link.Set(PdfName.Intern("Subtype"), PdfName.Intern("Link"));
        link.Set(PdfName.Intern("Rect"), new PdfArray(new PdfPrimitive[] { new PdfInteger(10), new PdfInteger(10), new PdfInteger(100), new PdfInteger(30) }));
        link.Set(PdfName.Intern("Contents"), new PdfString(Encoding.ASCII.GetBytes(contents)));
        link.Set(PdfName.Intern("Dest"), dest);
        return link;
    }
}
