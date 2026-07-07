// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.5.8.4 — Compatibility with Applications That Do
//                          Not Support Compressed Reference Streams (hybrid files)
// PHASE: Chuvadi.Pdf.IO — hybrid-reference (/XRefStm) resolution.
//
// A hybrid-reference file has a classic xref table whose trailer also carries
// /XRefStm, pointing to a cross-reference stream that lists the compressed
// (Type 2) objects the classic table cannot represent. Writers such as Word and
// other Office tools emit this shape, placing catalog entries — notably
// /StructTreeRoot and /MarkInfo — inside an object stream referenced only from
// the /XRefStm. Before the fix, PdfReader followed /Prev but ignored /XRefStm,
// so those compressed objects were invisible: HasStructTree and IsTagged
// wrongly reported false, and StructTreeRoot resolved to null.
//
// The fixture hybrid_xrefstm_structtree.pdf is a minimal hand-built hybrid file:
//   - object 1 (catalog, classic) references /StructTreeRoot 5 and /MarkInfo 6
//   - objects 5 and 6 live inside object stream 4 (compressed)
//   - the classic xref table lists 1..4 and 7 but NOT 5 or 6
//   - the trailer's /XRefStm points to xref stream 7, which lists 5 and 6
// Objects 5 and 6 are therefore reachable only through the /XRefStm path.

using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.IO.Tests;

public sealed class HybridXrefStreamTests
{
    private const string FixturePath = "fixtures/hybrid_xrefstm_structtree.pdf";

    [Fact]
    public void HybridFile_Opens_WithSinglePage()
    {
        File.Exists(FixturePath).Should().BeTrue(
            "the hybrid fixture must be deployed to the test output directory");

        using PdfDocument doc = PdfDocument.Open(FixturePath);
        doc.PageCount.Should().Be(1);
    }

    [Fact]
    public void HybridFile_ResolvesCompressedStructTreeRoot()
    {
        using PdfDocument doc = PdfDocument.Open(FixturePath);

        // Object 5 (the structure-tree root) is listed only in the /XRefStm and
        // stored compressed inside object stream 4. It must resolve.
        doc.HasStructTree.Should().BeTrue(
            "the /StructTreeRoot object is reachable through the hybrid /XRefStm");
        doc.StructTreeRoot.Should().NotBeNull();
    }

    [Fact]
    public void HybridFile_ResolvesCompressedMarkInfo()
    {
        using PdfDocument doc = PdfDocument.Open(FixturePath);

        // Object 6 (/MarkInfo with /Marked true) is also only in the /XRefStm.
        doc.IsTagged.Should().BeTrue(
            "the /MarkInfo object is reachable through the hybrid /XRefStm");
    }

    [Fact]
    public void HybridFile_CompressedObjectsResolveById()
    {
        using PdfDocument doc = PdfDocument.Open(FixturePath);

        PdfPrimitive structRoot = doc.Objects.ResolveById(new PdfObjectId(5, 0));
        structRoot.Should().BeOfType<PdfDictionary>(
            "object 5 is the /StructTreeRoot dict, resolvable only via /XRefStm");

        PdfPrimitive markInfo = doc.Objects.ResolveById(new PdfObjectId(6, 0));
        markInfo.Should().BeOfType<PdfDictionary>(
            "object 6 is the /MarkInfo dict, resolvable only via /XRefStm");
    }
}
