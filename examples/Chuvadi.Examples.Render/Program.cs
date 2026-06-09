// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
//
// Example: rasterize every page of a PDF to PNG at a given DPI.
// Uses Chuvadi's zero-dependency scanline rasterizer - no native libraries.
using System;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Rendering;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: Chuvadi.Examples.Render <input.pdf> <output-dir> [dpi] [--hint|--hint-light|--hint-full]");
    Console.Error.WriteLine("Example: Chuvadi.Examples.Render report.pdf pages 150");
    Console.Error.WriteLine("Example: Chuvadi.Examples.Render report.pdf pages 96 --hint");
    return 1;
}

string inputPath = args[0];
string outputDir = args[1];

bool hintLight = Array.Exists(args, a => a == "--hint") || Array.Exists(args, a => a == "--hint-light");
bool hintFull = Array.Exists(args, a => a == "--hint-full");
HintingMode hintMode = hintFull ? HintingMode.Full : (hintLight ? HintingMode.Light : HintingMode.Off);

double dpi = 96.0;

for (int i = 2; i < args.Length; i++)
{
    if (args[i] == "--hint" || args[i] == "--hint-light" || args[i] == "--hint-full")
    {
        continue;
    }

    if (double.TryParse(args[i], out double parsed))
    {
        dpi = parsed;
    }
}

Directory.CreateDirectory(outputDir);

using FileStream input = File.OpenRead(inputPath);
using PdfDocument document = PdfDocument.Open(input, leaveOpen: false);

RenderOptions options = new()
{
    Dpi = dpi,
    Hinting = hintMode,
};

PageRasterizer rasterizer = new(document.Objects, options);

Console.WriteLine($"Rendering {document.PageCount} page(s) at {dpi} DPI, hinting {hintMode}.");

for (int i = 0; i < document.PageCount; i++)
{
    byte[] png = rasterizer.RasterizeToPng(document.Pages[i]);
    string outPath = Path.Combine(outputDir, $"page_{i + 1:D3}.png");
    File.WriteAllBytes(outPath, png);
    Console.WriteLine($"Wrote {outPath}");
}

return 0;
