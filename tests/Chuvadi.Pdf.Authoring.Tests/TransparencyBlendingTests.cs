// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// 3.26: transparency composites in sRGB, the blending colour space Acrobat
// and pdf.js use, on every rasterizer path (anti-aliased and aliased fills,
// strokes, glyphs, translucent and alpha images, blend modes): 50% black over
// white is 128. Linear light is kept for anti-aliased edge coverage only.

using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Images;
using Chuvadi.Pdf.Rendering;
using FluentAssertions;
using Xunit;
using static Chuvadi.Pdf.Authoring.Tests.AuthoringTestKit;

namespace Chuvadi.Pdf.Authoring.Tests;

public sealed class TransparencyBlendingTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HalfBlackOverWhite_Is128_WithAndWithoutAntiAliasing(bool antiAlias)
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create().AddFont("T", Font("ChuvadiTest-Glyf.ttf"));
        PageBuilder page = builder.AddPage(PageSize.A4);
        page.PushOpacity(0.5);
        page.DrawRectangle(0, 0, 50, 50, fill: Colors.Black);
        page.DrawRectangle(100, 0, 50, 50, fill: null, stroke: Colors.Black, new LineStyle { Width = 20 });
        page.DrawText("AAAA", 0, 100, "T", 100, Colors.Black);
        page.PopOpacity();

        PixelBuffer px = Render(builder.ToByteArray(), antiAlias);

        Gray(px, 25, 25).Should().BeInRange(126, 130, "fill");
        Gray(px, 100, 25).Should().BeInRange(126, 130, "stroke");
        Gray(px, 10, 160).Should().BeInRange(126, 130, "glyph");
    }

    [Fact]
    public void TranslucentImage_And_AlphaImage_BlendInSrgb()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        PageBuilder page = builder.AddPage(PageSize.A4);
        page.DrawImage(Png(Solid(4, 4, 0, 0, 0, 255), alpha: false), 0, 0, 40, 40, 0.5);
        page.DrawImage(Png(Solid(4, 4, 0, 0, 0, 128), alpha: true), 100, 0, 40, 40);

        PixelBuffer px = Render(builder.ToByteArray(), antiAlias: true);

        Gray(px, 20, 20).Should().BeInRange(125, 131, "image at /ca 0.5");
        Gray(px, 120, 20).Should().BeInRange(125, 131, "image with a 50% alpha channel");
    }

    private static PixelBuffer Render(byte[] pdf, bool antiAlias)
    {
        using PdfDocument doc = Open(pdf);
        return new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72, AntiAlias = antiAlias }).Rasterize(doc.Pages[0]);
    }

    private static int Gray(PixelBuffer p, int x, int y) => Rgb(p, x, y).R;

    private static ImageFrame Solid(int w, int h, byte r, byte g, byte b, byte a)
    {
        ImageFrame frame = ImageFrame.Create(w, h, ImageColorFormat.Rgba32);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                frame.Pixels.SetPixelBgra(x, y, b, g, r, a);
            }
        }

        return frame;
    }

    private static byte[] Png(ImageFrame frame, bool alpha)
    {
        using MemoryStream ms = new MemoryStream();
        PngEncoder.Encode(frame, ms, alpha);
        return ms.ToArray();
    }
}
