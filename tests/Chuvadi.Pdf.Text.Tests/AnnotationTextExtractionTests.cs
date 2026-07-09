// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.5.5 — Appearance streams; §7.3.4.2 — Literal
//        strings (escape sequences)
// PHASE: Chuvadi.Pdf.Text — annotation appearance text + escape decoding.
//
// Hybrid XFA/AcroForm documents (e.g. Government-of-India MCA certificates)
// carry their filled field values in widget appearance streams, not in the
// page content, so extraction used to return the static template labels
// without the values. The extractor now appends fragments from each visible
// annotation's /AP /N stream, placed into page space per §12.5.5.
//
// The fixture's page content also exercises octal escapes: the label is
// written as (Label\050X\051:) and must extract as "Label(X):".

using System.IO;
using Chuvadi.Pdf.Documents;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Text.Tests;

public sealed class AnnotationTextExtractionTests
{
    private const string Fixture = "fixtures/hybrid_xfa_widget.pdf";

    [Fact]
    public void ExtractTextIncludesWidgetAppearanceValues()
    {
        using FileStream input = File.OpenRead(Fixture);
        using PdfDocument document = PdfDocument.Open(input, leaveOpen: false);

        TextExtractor extractor = new TextExtractor(
            document.Objects, ExtractionStrategy.Layout);
        string text = extractor.ExtractText(document.Pages[0]);

        text.Should().Contain("HELLO-VALUE");
    }

    [Fact]
    public void ExtractFragmentsIncludesWidgetAppearanceFragments()
    {
        using FileStream input = File.OpenRead(Fixture);
        using PdfDocument document = PdfDocument.Open(input, leaveOpen: false);

        TextExtractor extractor = new TextExtractor(
            document.Objects, ExtractionStrategy.Layout);

        // One fragment from the page content, one from the widget appearance,
        // the latter positioned inside the widget /Rect [100 100 300 120].
        System.Collections.Generic.List<Chuvadi.Pdf.Content.TextFragment> fragments =
            extractor.ExtractFragments(document.Pages[0]);

        fragments.Should().Contain(f => f.Text.Contains("HELLO-VALUE"));
        Chuvadi.Pdf.Content.TextFragment value = fragments.Find(
            f => f.Text.Contains("HELLO-VALUE"))!;
        value.X.Should().BeInRange(100, 300);
        value.Y.Should().BeInRange(100, 120);
    }

    [Fact]
    public void LiteralStringOctalEscapesDecode()
    {
        using FileStream input = File.OpenRead(Fixture);
        using PdfDocument document = PdfDocument.Open(input, leaveOpen: false);

        TextExtractor extractor = new TextExtractor(
            document.Objects, ExtractionStrategy.Layout);
        string text = extractor.ExtractText(document.Pages[0]);

        // Page content shows (Label\050X\051:) — octal 050/051 are the
        // parentheses (§7.3.4.2).
        text.Should().Contain("Label(X):");
        text.Should().NotContain("\\050");
    }
}
