// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.31.2 (D-207, D-219) — links to named destinations, and page
//        indexes taken from the page tree

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

namespace Chuvadi.Pdf.Annotations.Tests;

public sealed class LinkDestination3312Tests
{
    // Pages are objects 5, 6 and 7, but the page tree lists them as
    // [7 6 5]: object 7 is page 0 and object 5 is page 2. Since 3.31.2 the
    // index comes from the page tree itself. (Before, it came from the order
    // the object store held the /Page objects, which PdfDocument.Open happens
    // to fill in tree order, so this test also passes on 3.31.1.)
    private const int PageTwoObject = 5;
    private const int PageOneObject = 6;
    private const int PageZeroObject = 7;

    [Fact]
    public void ExplicitDestination_UsesThePageTreeOrder()
    {
        LinkAnnotation link = ReadLink("explicit");
        link.DestinationPageIndex.Should().Be(2);
    }

    [Fact]
    public void NamedDestination_InTheNamesTree_IsResolved()
    {
        LinkAnnotation link = ReadLink("names-tree");
        link.DestinationPageIndex.Should().Be(1);
    }

    [Fact]
    public void NamedDestination_InTheCatalogDests_ThroughAGoToAction_IsResolved()
    {
        LinkAnnotation link = ReadLink("catalog-dests");
        link.DestinationPageIndex.Should().Be(2);
    }

    [Fact]
    public void UnknownNamedDestination_IsStillDropped()
    {
        using PdfDocument doc = PdfDocument.Open(Build(), leaveOpen: false);
        IReadOnlyList<PdfAnnotation> annots = AnnotationReader.GetAnnotations(doc, 0);
        annots.OfType<LinkAnnotation>().Select(l => l.Contents).Should().NotContain("unknown");
    }

    [Fact]
    public void GetAllAnnotations_ResolvesTheSameAsPerPage()
    {
        using PdfDocument doc = PdfDocument.Open(Build(), leaveOpen: false);
        IEnumerable<int> perPage = AnnotationReader.GetAnnotations(doc, 0)
            .OfType<LinkAnnotation>().Select(l => l.DestinationPageIndex);
        IEnumerable<int> all = AnnotationReader.GetAllAnnotations(doc)
            .OfType<LinkAnnotation>().Select(l => l.DestinationPageIndex);
        all.Should().Equal(perPage);
    }

    private static LinkAnnotation ReadLink(string contents)
    {
        using PdfDocument doc = PdfDocument.Open(Build(), leaveOpen: false);
        return AnnotationReader.GetAnnotations(doc, 0).OfType<LinkAnnotation>().Single(l => l.Contents == contents);
    }

    private static MemoryStream Build()
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId namesId = new PdfObjectId(3, 0);
        PdfObjectId legacyDestsId = new PdfObjectId(4, 0);

        PdfDictionary pages = new PdfDictionary();
        pages.Set(PdfName.Type, PdfName.Pages);
        pages.Set(PdfName.Kids, new PdfArray(new PdfPrimitive[]
        {
            Ref(PageZeroObject), Ref(PageOneObject), Ref(PageTwoObject),
        }));
        pages.Set(PdfName.Count, 3);
        pages.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(600), new PdfInteger(800),
        }));

        // /Names /Dests: (chap2) -> page 1.
        PdfDictionary destsTree = new PdfDictionary();
        destsTree.Set(PdfName.Intern("Names"), new PdfArray(new PdfPrimitive[]
        {
            new PdfString(Encoding.ASCII.GetBytes("chap2")),
            new PdfArray(new PdfPrimitive[] { Ref(PageOneObject), PdfName.Intern("Fit") }),
        }));
        PdfDictionary names = new PdfDictionary();
        names.Set(PdfName.Intern("Dests"), destsTree);

        // Catalog /Dests: /intro -> << /D [page 2 /XYZ null null null] >>.
        PdfDictionary introDest = new PdfDictionary();
        introDest.Set(PdfName.Intern("D"), new PdfArray(new PdfPrimitive[]
        {
            Ref(PageTwoObject), PdfName.Intern("XYZ"), PdfNull.Value, PdfNull.Value, PdfNull.Value,
        }));
        PdfDictionary legacyDests = new PdfDictionary();
        legacyDests.Set(PdfName.Intern("intro"), introDest);

        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(pagesId));
        catalog.Set(PdfName.Intern("Names"), new PdfReference(namesId));
        catalog.Set(PdfName.Intern("Dests"), new PdfReference(legacyDestsId));

        PdfArray annots = new PdfArray(new PdfPrimitive[]
        {
            Link("explicit", PdfName.Intern("Dest"), new PdfArray(new PdfPrimitive[] { Ref(PageTwoObject), PdfName.Intern("Fit") })),
            Link("names-tree", PdfName.Intern("Dest"), new PdfString(Encoding.ASCII.GetBytes("chap2"))),
            Link("catalog-dests", PdfName.Intern("A"), GoTo(PdfName.Intern("intro"))),
            Link("unknown", PdfName.Intern("Dest"), new PdfString(Encoding.ASCII.GetBytes("nowhere"))),
        });

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(catalogId, catalog),
            new PdfIndirectObject(pagesId, pages),
            new PdfIndirectObject(namesId, names),
            new PdfIndirectObject(legacyDestsId, legacyDests),
            new PdfIndirectObject(new PdfObjectId(PageTwoObject, 0), Page(pagesId, null)),
            new PdfIndirectObject(new PdfObjectId(PageOneObject, 0), Page(pagesId, null)),
            new PdfIndirectObject(new PdfObjectId(PageZeroObject, 0), Page(pagesId, annots)),
        };

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));
        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects.ToArray(), trailer);
        ms.Position = 0;
        return ms;
    }

    private static PdfReference Ref(int objectNumber) => new PdfReference(new PdfObjectId(objectNumber, 0));

    private static PdfDictionary Page(PdfObjectId parent, PdfArray? annots)
    {
        PdfDictionary page = new PdfDictionary();
        page.Set(PdfName.Type, PdfName.Page);
        page.Set(PdfName.Intern("Parent"), new PdfReference(parent));
        if (annots is not null)
        {
            page.Set(PdfName.Intern("Annots"), annots);
        }

        return page;
    }

    private static PdfDictionary GoTo(PdfPrimitive destination)
    {
        PdfDictionary action = new PdfDictionary();
        action.Set(PdfName.Intern("S"), PdfName.Intern("GoTo"));
        action.Set(PdfName.Intern("D"), destination);
        return action;
    }

    private static PdfDictionary Link(string contents, PdfName key, PdfPrimitive value)
    {
        PdfDictionary link = new PdfDictionary();
        link.Set(PdfName.Type, PdfName.Intern("Annot"));
        link.Set(PdfName.Intern("Subtype"), PdfName.Intern("Link"));
        link.Set(PdfName.Intern("Rect"), new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(10), new PdfInteger(10), new PdfInteger(100), new PdfInteger(30),
        }));
        link.Set(PdfName.Intern("Contents"), new PdfString(Encoding.ASCII.GetBytes(contents)));
        link.Set(key, value);
        return link;
    }
}
