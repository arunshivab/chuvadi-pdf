// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System.IO;
using BenchmarkDotNet.Attributes;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Rendering;

namespace Chuvadi.Benchmarks.Scenarios;

/// <summary>
/// Rasterizer hot-path benchmark: rendering a page to pixels at common DPIs.
/// This is the dominant cost in the PNG/JPEG/BMP/TIFF render facade, so it is a
/// useful per-release regression signal. Run with
/// <c>dotnet run -c Release -- --filter *Rasterize*</c>.
/// </summary>
[MemoryDiagnoser]
public class RasterizeBench
{
    private byte[] _pdf = System.Array.Empty<byte>();

    [GlobalSetup]
    public void Setup()
    {
        _pdf = ParserCorpus.SyntheticSinglePage();
    }

    [Benchmark(Baseline = true, Description = "Rasterize one page @ 150 DPI")]
    public int RasterizeAt150()
    {
        return RasterizePixels(150);
    }

    [Benchmark(Description = "Rasterize one page @ 300 DPI")]
    public int RasterizeAt300()
    {
        return RasterizePixels(300);
    }

    private int RasterizePixels(double dpi)
    {
        using MemoryStream ms = new MemoryStream(_pdf);
        using PdfDocument doc = PdfDocument.Open(ms);
        PageRasterizer rasterizer = new PageRasterizer(doc.Objects, new RenderOptions { Dpi = dpi });
        PixelBuffer buffer = rasterizer.Rasterize(doc.Pages[0]);
        return buffer.Width * buffer.Height;
    }
}
