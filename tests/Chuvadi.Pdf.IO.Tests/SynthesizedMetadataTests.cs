// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §14.3.3 (document info), §14.3.2 (metadata streams)
// PHASE: Phase 1 — Chuvadi.Pdf.IO tests
//
// Verifies that PdfWriter's SynthesizedMetadata flag controls which absent
// document-level metadata it synthesises. The file identifier (/ID) is always
// written regardless of the flag, because it carries no document content.

using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.IO.Tests;

public sealed class SynthesizedMetadataTests
{
    private static readonly PdfName MetadataKey = PdfName.Intern("Metadata");
    private static readonly PdfName InfoKey = PdfName.Intern("Info");
    private static readonly PdfName IdKey = PdfName.Intern("ID");

    private static byte[] WriteMinimal(SynthesizedMetadata synthesized)
    {
        PdfObjectId catalogId = new(1, 0);
        PdfObjectId pagesId = new(2, 0);

        PdfDictionary pagesDict = new();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, new PdfArray(new PdfPrimitive[0]));
        pagesDict.Set(PdfName.Count, 0);

        PdfDictionary catalogDict = new();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(pagesId, pagesDict),
            new PdfIndirectObject(catalogId, catalogDict),
        };

        PdfDictionary trailer = new();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        using MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer, null, synthesized);
        return ms.ToArray();
    }

    private static (bool Metadata, bool Info, bool Id) Inspect(byte[] pdf)
    {
        using MemoryStream ms = new MemoryStream(pdf, writable: false);
        using PdfDocument document = PdfDocument.Open(ms, leaveOpen: true);
        bool hasMetadata = document.Catalog is not null && document.Catalog.ContainsKey(MetadataKey);
        bool hasInfo = document.Trailer.ContainsKey(InfoKey);
        bool hasId = document.Trailer.ContainsKey(IdKey);
        return (hasMetadata, hasInfo, hasId);
    }

    [Fact]
    public void All_SynthesisesBothInfoAndMetadata()
    {
        (bool metadata, bool info, bool id) = Inspect(WriteMinimal(SynthesizedMetadata.All));

        metadata.Should().BeTrue();
        info.Should().BeTrue();
        id.Should().BeTrue();
    }

    [Fact]
    public void None_SuppressesBothButKeepsId()
    {
        (bool metadata, bool info, bool id) = Inspect(WriteMinimal(SynthesizedMetadata.None));

        metadata.Should().BeFalse();
        info.Should().BeFalse();
        id.Should().BeTrue();
    }

    [Fact]
    public void InfoOnly_SynthesisesInfoNotMetadata()
    {
        (bool metadata, bool info, bool id) = Inspect(WriteMinimal(SynthesizedMetadata.Info));

        info.Should().BeTrue();
        metadata.Should().BeFalse();
        id.Should().BeTrue();
    }

    [Fact]
    public void MetadataOnly_SynthesisesMetadataNotInfo()
    {
        (bool metadata, bool info, bool id) = Inspect(WriteMinimal(SynthesizedMetadata.Metadata));

        metadata.Should().BeTrue();
        info.Should().BeFalse();
        id.Should().BeTrue();
    }

    [Fact]
    public void DefaultOverload_PreservesAllBehaviour()
    {
        // The three-argument Write must behave as SynthesizedMetadata.All.
        PdfObjectId catalogId = new(1, 0);
        PdfObjectId pagesId = new(2, 0);
        PdfDictionary pagesDict = new();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, new PdfArray(new PdfPrimitive[0]));
        pagesDict.Set(PdfName.Count, 0);
        PdfDictionary catalogDict = new();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));
        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(pagesId, pagesDict),
            new PdfIndirectObject(catalogId, catalogDict),
        };
        PdfDictionary trailer = new();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        using MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);

        (bool metadata, bool info, bool id) = Inspect(ms.ToArray());
        metadata.Should().BeTrue();
        info.Should().BeTrue();
        id.Should().BeTrue();
    }
}
