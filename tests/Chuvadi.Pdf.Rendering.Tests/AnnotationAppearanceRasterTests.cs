// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.5.5 — Appearance streams
// PHASE: Chuvadi.Pdf.Rendering — annotation appearances in raster output.
//
// The raster display-list builder replays each visible annotation's /AP /N
// form after the page content, placed onto the annotation /Rect by the
// §12.5.5 algorithm. The fixture's widget rect is [100 100 300 120] on a
// 400×200 page; before the fix that region rasterized blank (hybrid XFA
// values were invisible), so the test asserts ink inside the rect.

using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.Tests;

public sealed class AnnotationAppearanceRasterTests
{
    private const string Fixture = "Fixtures/hybrid_xfa_widget.pdf";

    [Fact]
    public void WidgetAppearanceRasterizesInkInsideRect()
    {
        using FileStream input = File.OpenRead(Fixture);
        using PdfDocument document = PdfDocument.Open(input, leaveOpen: false);

        PageRasterizer rasterizer = new PageRasterizer(
            document.Objects, new RenderOptions { Dpi = 72 });
        PixelBuffer buffer = rasterizer.Rasterize(document.Pages[0]);

        // Page is 400×200 at 72 dpi → 1 pixel per point. The widget rect is
        // [100 100 300 120] in PDF space (origin bottom-left); device Y is
        // flipped, so the rect covers device rows 80..100.
        int ink = 0;

        for (int y = 80; y < 100; y++)
        {
            for (int x = 100; x < 300; x++)
            {
                (byte _, byte g, byte _, byte _) = buffer.GetPixelBgra(x, y);

                if (g < 200)
                {
                    ink++;
                }
            }
        }

        ink.Should().BeGreaterThan(0, "the widget appearance text should paint inside its /Rect");
    }
}
