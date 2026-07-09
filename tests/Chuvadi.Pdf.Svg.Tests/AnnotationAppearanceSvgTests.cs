// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.5.5 — Appearance streams
// PHASE: Chuvadi.Pdf.Svg — annotation appearances in SVG output.
//
// Hybrid XFA/AcroForm documents carry their filled field values in widget
// appearance streams. The display-list builder replays each visible
// annotation's /AP /N form after the page content, so the SVG output shows
// the values the way an interactive viewer paints them.

using System.IO;
using Chuvadi.Pdf.Documents;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Svg.Tests;

public sealed class AnnotationAppearanceSvgTests
{
    private const string Fixture = "fixtures/hybrid_xfa_widget.pdf";

    [Fact]
    public void SvgIncludesWidgetAppearanceValue()
    {
        using FileStream input = File.OpenRead(Fixture);
        using PdfDocument document = PdfDocument.Open(input, leaveOpen: false);

        string svg = new SvgRenderer().RenderPage(document, 0);

        svg.Should().Contain("HELLO-VALUE");
    }
}
