// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.5.6 — Incremental updates (xref section chains)
// PHASE: Chuvadi.Pdf.IO — cross-reference chain precedence.
//
// An incrementally-updated file carries multiple xref sections chained by
// /Prev, walked newest-first. §7.5.6: the entry for an object in the most
// recent section supersedes all earlier ones — of any kind. Two consequences
// these tests pin down (both regressed before the fix):
//
//   1. An object REDEFINED in a newer section resolves to the new definition
//      even though an older section also defines it.
//   2. An object marked FREE in a newer section is deleted: an older
//      section's in-use entry must NOT resurrect it. Before the fix, the
//      merge treated free entries as overridable/overriding
//      (`!Contains(n) || entry.IsFree`), so a deleted annotation came back
//      from the dead and government-issued hybrid XFA certificates resolved
//      their pre-fill page tree — widgets without /V or /AP.
//
// Fixtures (hand-built classic-xref chains):
//   incremental_object_update.pdf  — object 4 (page content) OLD -> NEW.
//   incremental_free_shadowing.pdf — newest section rewrites page 3 without
//   /Annots and frees widget object 6; the base section still defines both.

using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.IO.Tests;

public sealed class XrefPrecedenceTests
{
    private const string UpdateFixture = "fixtures/incremental_object_update.pdf";
    private const string FreeFixture = "fixtures/incremental_free_shadowing.pdf";

    [Fact]
    public void NewestSectionDefinitionWins()
    {
        using FileStream input = File.OpenRead(UpdateFixture);
        using PdfDocument document = PdfDocument.Open(input, leaveOpen: false);

        PdfPrimitive resolved = document.Objects.Resolve(
            new PdfReference(new PdfObjectId(4, 0)));

        resolved.Should().BeOfType<PdfStream>();
        string content = System.Text.Encoding.Latin1.GetString(
            ((PdfStream)resolved).RawBytes);
        content.Should().Contain("(NEW)");
        content.Should().NotContain("(OLD)");
    }

    [Fact]
    public void FreeEntryInNewerSectionShadowsOlderDefinition()
    {
        using FileStream input = File.OpenRead(FreeFixture);
        using PdfDocument document = PdfDocument.Open(input, leaveOpen: false);

        // The newest section freed object 6; the base section's in-use
        // definition must not resurrect it.
        PdfPrimitive resolved = document.Objects.Resolve(
            new PdfReference(new PdfObjectId(6, 0)));

        resolved.Should().BeOfType<PdfNull>();
    }

    [Fact]
    public void PageUpdatedInNewerSectionDropsDeletedAnnotations()
    {
        using FileStream input = File.OpenRead(FreeFixture);
        using PdfDocument document = PdfDocument.Open(input, leaveOpen: false);

        // The newest generation of page 3 has no /Annots, so no annotation
        // appearances exist; before the fix the stale page (with the freed
        // widget) resolved instead.
        PageAnnotationAppearances
            .Collect(document.Pages[0], document.Objects)
            .Should().BeEmpty();
    }
}
