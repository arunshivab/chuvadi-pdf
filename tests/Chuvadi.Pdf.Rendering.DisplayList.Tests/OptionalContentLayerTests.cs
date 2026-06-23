// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.11.3.2 — Optional content in content streams
//        PDF 32000-1:2008 §8.10.2 — Marked-content operators (BDC/BMC/EMC)
//
// Verifies that the display-list builder threads optional-content (layer)
// membership onto every emitted op via the marked-content stack, and exposes
// the document's optional-content groups on the page list. Built on tiny
// hand-constructed PDFs so the behaviour is pinned without any external asset.

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

public sealed class OptionalContentLayerTests
{
    [Fact]
    public void OptionalContentGroups_AreExposedOnThePage()
    {
        using PdfDocument doc = BuildOcgPdf(
            content: "70 70 20 20 re f",
            ocgNames: new[] { "Wall", "Door" },
            properties: System.Array.Empty<(string, int)>());

        PageDisplayList list = DisplayListBuilder.Build(doc, 0);

        list.OptionalContentGroups.Should().HaveCount(2);
        list.OptionalContentGroups.Select(g => g.Name).Should().Equal("Wall", "Door");
    }

    [Fact]
    public void OcMarkedContent_StampsLayerNameOnEnclosedOps()
    {
        // Two /OC sequences and one fill outside any optional content.
        string content =
            "/OC /MC0 BDC\n10 10 20 20 re f\nEMC\n" +
            "/OC /MC1 BDC\n40 40 20 20 re f\nEMC\n" +
            "70 70 20 20 re f";

        using PdfDocument doc = BuildOcgPdf(
            content,
            ocgNames: new[] { "Wall", "Door" },
            properties: new[] { ("MC0", 0), ("MC1", 1) });

        PageDisplayList list = DisplayListBuilder.Build(doc, 0);

        List<RenderOp> paths = list.Where(op => op.Kind == RenderOpKind.Path).ToList();
        paths.Should().HaveCount(3);
        paths[0].Layers.Should().Equal("Wall");
        paths[1].Layers.Should().Equal("Door");
        paths[2].Layers.Should().BeEmpty();
    }

    [Fact]
    public void NestedOc_StampsLayersOuterToInner()
    {
        string content =
            "/OC /MC0 BDC\n/OC /MC1 BDC\n10 10 20 20 re f\nEMC\nEMC";

        using PdfDocument doc = BuildOcgPdf(
            content,
            ocgNames: new[] { "Wall", "Door" },
            properties: new[] { ("MC0", 0), ("MC1", 1) });

        PageDisplayList list = DisplayListBuilder.Build(doc, 0);

        RenderOp path = list.Single(op => op.Kind == RenderOpKind.Path);
        path.Layers.Should().Equal("Wall", "Door");
    }

    [Fact]
    public void NonOptionalMarkedContent_AddsNoLayer()
    {
        // A plain marked-content sequence (tag other than /OC) contributes
        // no layer, and the fill after EMC is likewise unlayered.
        string content =
            "/Span BMC\n10 10 20 20 re f\nEMC\n40 40 20 20 re f";

        using PdfDocument doc = BuildOcgPdf(
            content,
            ocgNames: new[] { "Wall" },
            properties: System.Array.Empty<(string, int)>());

        PageDisplayList list = DisplayListBuilder.Build(doc, 0);

        list.Where(op => op.Kind == RenderOpKind.Path)
            .Should().OnlyContain(op => op.Layers.Count == 0);
    }

    [Fact]
    public void UnbalancedEmc_DoesNotThrow()
    {
        // A stray EMC with no matching BDC must be tolerated (real-world PDFs).
        string content = "EMC\n10 10 20 20 re f";

        using PdfDocument doc = BuildOcgPdf(
            content,
            ocgNames: new[] { "Wall" },
            properties: System.Array.Empty<(string, int)>());

        PageDisplayList list = DisplayListBuilder.Build(doc, 0);

        list.Single(op => op.Kind == RenderOpKind.Path).Layers.Should().BeEmpty();
    }

    // ── Helper ────────────────────────────────────────────────────────────

    // Builds a single-page PDF whose content stream is <paramref name="content"/>.
    // Each name in <paramref name="ocgNames"/> becomes an OCG declared in
    // /OCProperties/OCGs; each (propName, index) in <paramref name="properties"/>
    // adds a /Resources/Properties entry mapping propName to that OCG, so a
    // `/OC /propName BDC` in the content resolves to the OCG's name.
    private static PdfDocument BuildOcgPdf(
        string content,
        string[] ocgNames,
        (string PropName, int OcgIndex)[] properties)
    {
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
            ocg.Set(PdfName.Intern("Name"),
                new PdfString(Encoding.Latin1.GetBytes(ocgNames[i])));
            objects.Add(new PdfIndirectObject(ocgIds[i], ocg));
            ocgRefs.Add(new PdfReference(ocgIds[i]));
        }

        PdfDictionary ocProperties = new PdfDictionary();
        ocProperties.Set(PdfName.Intern("OCGs"), new PdfArray(ocgRefs));
        PdfDictionary defaultConfig = new PdfDictionary();
        defaultConfig.Set(PdfName.Intern("BaseState"), PdfName.Intern("ON"));
        ocProperties.Set(PdfName.Intern("D"), defaultConfig);

        PdfDictionary catalogDict = new PdfDictionary();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));
        catalogDict.Set(PdfName.Intern("OCProperties"), ocProperties);

        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, new PdfArray(new PdfPrimitive[] { new PdfReference(pageId) }));
        pagesDict.Set(PdfName.Count, 1);
        pagesDict.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(200), new PdfInteger(200),
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
