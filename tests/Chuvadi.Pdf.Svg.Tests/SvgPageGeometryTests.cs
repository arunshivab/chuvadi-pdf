// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: v3.x — SVG export hardening (cross-engine page geometry + background)

using System.IO;
using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Svg.Tests;

/// <summary>
/// Regression tests for two SVG page-geometry fixes:
/// <list type="bullet">
/// <item>The root <c>width</c>/<c>height</c> carry an explicit <c>pt</c> unit.
/// Emitting them unitless let some viewers treat them as points and rescale the
/// canvas by 96/72 while leaving the content at 1 unit = 1px, pushing the page
/// off-centre.</item>
/// <item>An opaque page-background rectangle is emitted by default, so the SVG
/// matches the rasteriser's white paper instead of being transparent.</item>
/// </list>
/// </summary>
public sealed class SvgPageGeometryTests
{
    private static string RenderTextPage(SvgExportOptions options)
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        builder.AddPage(PageSize.A4).DrawText(
            "Hello",
            50, 50,
            StandardFonts.Helvetica,
            12,
            Colors.Black);
        byte[] pdf = builder.ToByteArray();

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        return new SvgRenderer(options).RenderPage(doc, 0);
    }

    [Fact]
    public void RootElement_HasExplicitPointUnitsOnWidthAndHeight()
    {
        string svg = RenderTextPage(new SvgExportOptions());
        string root = svg.Substring(0, svg.IndexOf('>') + 1);

        root.Should().Contain("pt\"");
        root.Should().Contain("viewBox=\"0 0 ");
        // The viewBox stays unitless while width/height are points.
        root.Should().MatchRegex("width=\"[0-9.]+pt\" height=\"[0-9.]+pt\"");
    }

    [Fact]
    public void DefaultOptions_EmitOpaqueWhitePageBackground()
    {
        string svg = RenderTextPage(new SvgExportOptions());

        svg.Should().Contain("<rect x=\"0\" y=\"0\"");
        svg.Should().Contain("fill=\"#FFFFFF\"");
    }

    [Fact]
    public void NullBackground_EmitsNoPageBackgroundRect()
    {
        // A text-only page produces no fill rectangles, so the absence of any
        // <rect> means the page background was correctly suppressed.
        string svg = RenderTextPage(new SvgExportOptions { Background = null });

        svg.Should().NotContain("<rect x=\"0\" y=\"0\"");
    }
}
