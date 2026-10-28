// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Rendering;
using Chuvadi.Pdf.Text;

namespace Chuvadi.Pdf.Authoring.Tests;

/// <summary>
/// Helpers for tests that author a PDF and read it back: the synthetic font
/// fixtures, text extraction and rasterising at 72 dpi (1 px = 1 pt).
/// </summary>
internal static class AuthoringTestKit
{
    // Glyph ids shared by every ChuvadiTest fixture (tools/make_font_fixtures.py).
    internal const int GidA = 2;
    internal const int GidH = 4;
    internal const int GidX = 5;
    internal const int GidW = 8;
    internal const int GidFi = 9;

    internal static byte[] Font(string name) =>
        File.ReadAllBytes(System.IO.Path.Combine(System.AppContext.BaseDirectory, "Fixtures", "Fonts", name));

    internal static PdfDocument Open(byte[] pdf) => PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);

    internal static string ExtractedText(byte[] pdf, int page = 0)
    {
        using PdfDocument doc = Open(pdf);
        return new TextExtractor(doc.Objects).ExtractText(doc.Pages[page]).Trim();
    }

    internal static PixelBuffer Raster(byte[] pdf, int page = 0)
    {
        using PdfDocument doc = Open(pdf);
        return new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72 }).Rasterize(doc.Pages[page]);
    }

    internal static bool IsDark(PixelBuffer p, int x, int y)
    {
        (byte b, byte g, byte r, byte _) = p.GetPixelBgra(x, y);
        return r < 80 && g < 80 && b < 80;
    }

    internal static bool IsWhite(PixelBuffer p, int x, int y)
    {
        (byte b, byte g, byte r, byte _) = p.GetPixelBgra(x, y);
        return r > 240 && g > 240 && b > 240;
    }

    internal static (byte R, byte G, byte B) Rgb(PixelBuffer p, int x, int y)
    {
        (byte b, byte g, byte r, byte _) = p.GetPixelBgra(x, y);
        return (r, g, b);
    }
}
