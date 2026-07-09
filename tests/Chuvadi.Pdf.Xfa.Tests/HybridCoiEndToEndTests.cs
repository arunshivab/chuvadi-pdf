// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.5.5 — Appearance streams; §12.7.8 — XFA Forms;
//        §7.5.6 — Incremental updates
// PHASE: Chuvadi.Pdf.Xfa — hybrid XFA end-to-end (open / extract / geometry /
//        flatten "just work" through the normal APIs).
//
// The fixture livecycle-coi-redacted.pdf is a sanitised Government-of-India
// MCA Certificate of Incorporation: a hybrid XFA whose AcroForm widgets carry
// the filled values in /V and /AP /N appearance streams. A consumer opening
// it through the ordinary APIs must see the finished certificate — values in
// extraction, geometry on the data fields, and a static non-XFA flatten —
// with no XFA-specific handling of its own.

using System.IO;
using System.Linq;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Operations;
using Chuvadi.Pdf.Text;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Xfa.Tests;

public sealed class HybridCoiEndToEndTests
{
    private const string Fixture = "Fixtures/livecycle-coi-redacted.pdf";

    [Fact]
    public void OpensAsHybridXfa()
    {
        using PdfDocument document = Open();

        document.PageCount.Should().Be(1);
        document.IsXfa.Should().BeTrue();
        document.XfaKind.Should().Be(XfaKind.Hybrid);
    }

    [Fact]
    public void CollectFindsVisibleWidgetAppearances()
    {
        using PdfDocument document = Open();

        // 27 widgets on the page; 23 carry /AP; 12 are NoView data carriers
        // and are skipped — 11 visible appearances draw.
        PageAnnotationAppearances
            .Collect(document.Pages[0], document.Objects)
            .Should().HaveCount(11);
    }

    [Fact]
    public void ExtractTextIncludesFieldValues()
    {
        using PdfDocument document = Open();

        TextExtractor extractor = new TextExtractor(
            document.Objects, ExtractionStrategy.Layout);
        string text = extractor.ExtractText(document.Pages[0]);

        // Values live in widget appearance streams, not the page content.
        // (The fixture's redaction rewrote the composed Text1 sentence, so
        // the assertions use the identifier values, which are stable across
        // the appearance streams and the datasets packet.)
        text.Should().Contain("U00000XX0000XXX000000");
        text.Should().Contain("AAAAA0000A");
    }

    [Fact]
    public void DataFieldsCarryWidgetGeometry()
    {
        using PdfDocument document = Open();

        System.Collections.Generic.IReadOnlyList<XfaDataField> fields =
            document.Xfa!.DataFields;

        XfaDataField company = fields.First(
            f => f.NodePath.EndsWith("COMPANY_NAME", System.StringComparison.Ordinal));

        company.Value.Should().Be("EXAMPLE COMPANY PRIVATE LIMITED");
        company.Geometry.Should().NotBeNull(
            "the template bind map correlates the dataset node to the widget rect");
        company.Geometry!.PageIndex.Should().Be(0);
        company.Geometry.Rectangle.Width.Should().BeGreaterThan(0);

        fields.Count(f => f.Geometry is not null).Should().BeGreaterThanOrEqualTo(10);
    }

    [Fact]
    public void FlattenProducesStaticPdfWithValues()
    {
        using PdfDocument document = Open();

        using MemoryStream output = new MemoryStream();
        AnnotationFlattener.Flatten(output, document);
        output.Position = 0;

        using PdfDocument flat = PdfDocument.Open(output);

        flat.IsXfa.Should().BeFalse();
        flat.Pages[0].HasContent.Should().BeTrue();

        TextExtractor extractor = new TextExtractor(
            flat.Objects, ExtractionStrategy.Layout);
        string text = extractor.ExtractText(flat.Pages[0]);

        text.Should().Contain("U00000XX0000XXX000000");
        text.Should().Contain("AAAAA0000A");
    }

    private static PdfDocument Open()
    {
        FileStream input = File.OpenRead(Fixture);
        return PdfDocument.Open(input, leaveOpen: false);
    }
}
