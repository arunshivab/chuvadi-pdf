// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.7 — Optional content tests

using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Documents.Tests;

public sealed class OptionalContentReaderTests
{
    [Fact]
    public void NoOCProperties_ReturnsEmpty()
    {
        using PdfDocument doc = BuildMinimalDocument(catalogExtras: null);
        OptionalContentReader.GetGroups(doc).Should().BeEmpty();
    }

    [Fact]
    public void ReadsLayerName()
    {
        // Two OCGs, both visible by default
        using PdfDocument doc = BuildDocumentWithOcgs(
            ocgNames: ["Anatomy", "Labels"],
            offIndices: System.Array.Empty<int>(),
            baseState: "ON");

        IReadOnlyList<OptionalContentGroup> groups = OptionalContentReader.GetGroups(doc);
        groups.Should().HaveCount(2);
        groups[0].Name.Should().Be("Anatomy");
        groups[1].Name.Should().Be("Labels");
        groups[0].IsVisibleByDefault.Should().BeTrue();
        groups[1].IsVisibleByDefault.Should().BeTrue();
    }

    [Fact]
    public void ResolvesVisibility_BaseStateOff()
    {
        // BaseState OFF, /ON only first OCG → only first visible
        using PdfDocument doc = BuildDocumentWithOcgs(
            ocgNames: ["A", "B"],
            offIndices: System.Array.Empty<int>(),
            baseState: "OFF",
            onIndices: new[] { 0 });

        IReadOnlyList<OptionalContentGroup> groups = OptionalContentReader.GetGroups(doc);
        groups[0].IsVisibleByDefault.Should().BeTrue();
        groups[1].IsVisibleByDefault.Should().BeFalse();
    }

    [Fact]
    public void ResolvesVisibility_OffArray()
    {
        // BaseState ON, /OFF lists index 1 → only first visible
        using PdfDocument doc = BuildDocumentWithOcgs(
            ocgNames: ["X", "Y"],
            offIndices: new[] { 1 },
            baseState: "ON");

        IReadOnlyList<OptionalContentGroup> groups = OptionalContentReader.GetGroups(doc);
        groups[0].IsVisibleByDefault.Should().BeTrue();
        groups[1].IsVisibleByDefault.Should().BeFalse();
    }

    [Fact]
    public void DefaultConfigurationName_Read()
    {
        using PdfDocument doc = BuildDocumentWithOcgs(
            ocgNames: ["Layer 1"],
            offIndices: System.Array.Empty<int>(),
            baseState: "ON",
            defaultName: "Print View");

        OptionalContentReader.GetDefaultConfigurationName(doc).Should().Be("Print View");
    }

    [Fact]
    public void DefaultConfigurationName_AbsentReturnsNull()
    {
        using PdfDocument doc = BuildDocumentWithOcgs(
            ocgNames: ["Single"],
            offIndices: System.Array.Empty<int>(),
            baseState: "ON");

        OptionalContentReader.GetDefaultConfigurationName(doc).Should().BeNull();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    [Fact]
    public void SetVisibility_HidesLayerByName()
    {
        using PdfDocument doc = BuildDocumentWithOcgs(
            new[] { "Layer A", "Layer B" }, new int[0], "ON");

        using MemoryStream output = new MemoryStream();
        OptionalContentWriter.SetVisibility(
            output, doc, new Dictionary<string, bool> { ["Layer B"] = false });

        output.Seek(0, SeekOrigin.Begin);
        using PdfDocument modified = PdfDocument.Open(output, leaveOpen: true);
        IReadOnlyList<OptionalContentGroup> after = OptionalContentReader.GetGroups(modified);

        after.Should().HaveCount(2);
        after[0].Name.Should().Be("Layer A");
        after[0].IsVisibleByDefault.Should().BeTrue();
        after[1].Name.Should().Be("Layer B");
        after[1].IsVisibleByDefault.Should().BeFalse();
    }

    [Fact]
    public void SetVisibility_ReShowsHiddenLayer()
    {
        using PdfDocument doc = BuildDocumentWithOcgs(new[] { "Layer A" }, new[] { 0 }, "ON");
        OptionalContentReader.GetGroups(doc)[0].IsVisibleByDefault.Should().BeFalse();

        using MemoryStream output = new MemoryStream();
        OptionalContentWriter.SetVisibility(
            output, doc, new Dictionary<string, bool> { ["Layer A"] = true });

        output.Seek(0, SeekOrigin.Begin);
        using PdfDocument modified = PdfDocument.Open(output, leaveOpen: true);
        OptionalContentReader.GetGroups(modified)[0].IsVisibleByDefault.Should().BeTrue();
    }

    [Fact]
    public void SetVisibility_UnknownLayerName_IsIgnored()
    {
        using PdfDocument doc = BuildDocumentWithOcgs(new[] { "Layer A" }, new int[0], "ON");

        using MemoryStream output = new MemoryStream();
        OptionalContentWriter.SetVisibility(
            output, doc, new Dictionary<string, bool> { ["Nonexistent"] = false });

        output.Seek(0, SeekOrigin.Begin);
        using PdfDocument modified = PdfDocument.Open(output, leaveOpen: true);
        OptionalContentReader.GetGroups(modified)[0].IsVisibleByDefault.Should().BeTrue();
    }

    private static PdfDocument BuildMinimalDocument(PdfDictionary? catalogExtras)
    {
        PdfObjectId catalogId = new(1, 0);
        PdfObjectId pagesId = new(2, 0);

        PdfDictionary catalogDict = new();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

        if (catalogExtras is not null)
        {
            foreach (KeyValuePair<PdfName, PdfPrimitive> kvp in catalogExtras)
            {
                catalogDict.Set(kvp.Key, kvp.Value);
            }
        }

        PdfDictionary pagesDict = new();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, new PdfArray([]));
        pagesDict.Set(PdfName.Count, 0);

        List<PdfIndirectObject> objects = [
            new(catalogId, catalogDict),
            new(pagesId, pagesDict),
        ];

        PdfDictionary trailer = new();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new();
        PdfWriter.Write(ms, objects, trailer);
        ms.Seek(0, SeekOrigin.Begin);
        return PdfDocument.Open(ms, leaveOpen: true);
    }

    private static PdfDocument BuildDocumentWithOcgs(
        string[] ocgNames,
        int[] offIndices,
        string baseState,
        int[]? onIndices = null,
        string? defaultName = null)
    {
        // Object IDs: 1 catalog, 2 pages, 3..3+N-1 OCG dicts
        PdfObjectId catalogId = new(1, 0);
        PdfObjectId pagesId = new(2, 0);
        PdfObjectId[] ocgIds = new PdfObjectId[ocgNames.Length];

        for (int i = 0; i < ocgNames.Length; i++)
        {
            ocgIds[i] = new(3 + i, 0);
        }

        // Build OCG dicts
        List<PdfIndirectObject> objects = new();
        List<PdfPrimitive> ocgRefs = new();

        for (int i = 0; i < ocgNames.Length; i++)
        {
            PdfDictionary ocg = new();
            ocg.Set(PdfName.Type, PdfName.Intern("OCG"));
            ocg.Set(PdfName.Intern("Name"),
                new PdfString(System.Text.Encoding.Latin1.GetBytes(ocgNames[i])));
            objects.Add(new(ocgIds[i], ocg));
            ocgRefs.Add(new PdfReference(ocgIds[i]));
        }

        // Build OCProperties
        PdfDictionary ocProperties = new();
        ocProperties.Set(PdfName.Intern("OCGs"), new PdfArray(ocgRefs));

        PdfDictionary d = new();
        d.Set(PdfName.Intern("BaseState"), PdfName.Intern(baseState));

        List<PdfPrimitive> offRefs = new();
        foreach (int idx in offIndices)
        {
            offRefs.Add(new PdfReference(ocgIds[idx]));
        }

        if (offRefs.Count > 0)
        {
            d.Set(PdfName.Intern("OFF"), new PdfArray(offRefs));
        }

        if (onIndices is not null)
        {
            List<PdfPrimitive> onRefs = new();
            foreach (int idx in onIndices)
            {
                onRefs.Add(new PdfReference(ocgIds[idx]));
            }
            d.Set(PdfName.Intern("ON"), new PdfArray(onRefs));
        }

        if (defaultName is not null)
        {
            d.Set(PdfName.Intern("Name"),
                new PdfString(System.Text.Encoding.Latin1.GetBytes(defaultName)));
        }

        ocProperties.Set(PdfName.Intern("D"), d);

        PdfDictionary catalogDict = new();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));
        catalogDict.Set(PdfName.Intern("OCProperties"), ocProperties);

        PdfDictionary pagesDict = new();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, new PdfArray([]));
        pagesDict.Set(PdfName.Count, 0);

        objects.Insert(0, new(catalogId, catalogDict));
        objects.Insert(1, new(pagesId, pagesDict));

        PdfDictionary trailer = new();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new();
        PdfWriter.Write(ms, objects, trailer);
        ms.Seek(0, SeekOrigin.Begin);
        return PdfDocument.Open(ms, leaveOpen: true);
    }
}
