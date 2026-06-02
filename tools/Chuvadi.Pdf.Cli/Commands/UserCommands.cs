// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2 — Chuvadi.Pdf.Cli
// User-facing verb implementations.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Forms;
using Chuvadi.Pdf.Graphics;
using IoPath = System.IO.Path;
using Chuvadi.Pdf.Images;
using Chuvadi.Pdf.Operations;
using Chuvadi.Pdf.Redaction;
using Chuvadi.Pdf.Rendering;
using Chuvadi.Pdf.Text;
using Chuvadi.Pdf.Watermark;

namespace Chuvadi.Pdf.Cli.Commands;

// ── info ──────────────────────────────────────────────────────────────────

/// <summary>Prints basic metadata about a PDF.</summary>
public sealed class InfoCommand : ICommand
{
    /// <inheritdoc />
    public int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        ParsedArgs p = ArgParser.Parse(args);

        if (p.Positional.Count == 0)
        {
            stderr.WriteLine("Usage: chuvadi info <input.pdf>");
            return 2;
        }

        string path = p.Positional[0];

        using (FileStream fs = File.OpenRead(path))
        using (PdfDocument doc = PdfDocument.Open(fs, leaveOpen: false))
        {
            stdout.WriteLine($"File:        {path}");
            stdout.WriteLine($"Size:        {new FileInfo(path).Length} bytes");
            stdout.WriteLine($"Pages:       {doc.PageCount}");

            bool hasAcroForm = doc.Catalog.TryGetValue(
                Chuvadi.Pdf.Primitives.PdfName.Intern("AcroForm"), out _);
            stdout.WriteLine($"AcroForm:    {(hasAcroForm ? "yes" : "no")}");

            bool hasOutlines = doc.Catalog.TryGetValue(
                Chuvadi.Pdf.Primitives.PdfName.Outlines, out _);
            stdout.WriteLine($"Outlines:    {(hasOutlines ? "yes" : "no")}");
        }

        return 0;
    }
}

// ── render ────────────────────────────────────────────────────────────────

/// <summary>Rasterizes a page to PNG.</summary>
public sealed class RenderCommand : ICommand
{
    /// <inheritdoc />
    public int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        ParsedArgs p = ArgParser.Parse(args);

        if (p.Positional.Count == 0)
        {
            stderr.WriteLine("Usage: chuvadi render <input.pdf> --output <out.png> [--page N] [--dpi 150] [--supersample 3]");
            return 2;
        }

        string inputPath = p.Positional[0];
        string outputPath = p.Get("output") ?? throw new ArgumentException("--output is required");
        int pageIndex = int.Parse(p.Get("page", "0")!, CultureInfo.InvariantCulture);
        double dpi = double.Parse(p.Get("dpi", "150")!, CultureInfo.InvariantCulture);
        int superSample = int.Parse(p.Get("supersample", "1")!, CultureInfo.InvariantCulture);
        bool antiAlias = p.Get("aa", "1") != "0";

        using (FileStream fs = File.OpenRead(inputPath))
        using (PdfDocument doc = PdfDocument.Open(fs, leaveOpen: false))
        {
            if (pageIndex < 0 || pageIndex >= doc.PageCount)
            {
                stderr.WriteLine($"render: page index {pageIndex} out of range (document has {doc.PageCount} pages)");
                return 1;
            }

            RenderOptions options = new RenderOptions
            {
                Dpi = dpi,
                SuperSample = superSample,
                AntiAlias = antiAlias,
            };

            PageRasterizer rasterizer = new PageRasterizer(doc.Objects, options);
            PixelBuffer buffer = rasterizer.Rasterize(doc.Pages[pageIndex]);
            ImageFrame frame = new ImageFrame(buffer, ImageColorFormat.Rgba32);

            using (FileStream output = File.Create(outputPath))
            {
                PngEncoder.Encode(frame, output, includeAlpha: true);
            }

            stdout.WriteLine($"render: wrote {outputPath} ({buffer.Width}x{buffer.Height})");
        }

        return 0;
    }
}

// ── watermark ─────────────────────────────────────────────────────────────

/// <summary>Applies a text watermark.</summary>
public sealed class WatermarkCommand : ICommand
{
    /// <inheritdoc />
    public int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        ParsedArgs p = ArgParser.Parse(args);

        if (p.Positional.Count == 0)
        {
            stderr.WriteLine("Usage: chuvadi watermark <input.pdf> --output <out.pdf> --text TEXT [--size 48] [--opacity 0.3] [--rotation 45]");
            return 2;
        }

        string inputPath = p.Positional[0];
        string outputPath = p.Get("output") ?? throw new ArgumentException("--output is required");
        string text = p.Get("text") ?? throw new ArgumentException("--text is required");
        double size = double.Parse(p.Get("size", "48")!, CultureInfo.InvariantCulture);
        float opacity = float.Parse(p.Get("opacity", "0.3")!, CultureInfo.InvariantCulture);
        double rotation = double.Parse(p.Get("rotation", "45")!, CultureInfo.InvariantCulture);

        using (FileStream fs = File.OpenRead(inputPath))
        using (PdfDocument doc = PdfDocument.Open(fs, leaveOpen: false))
        using (FileStream outFs = File.Create(outputPath))
        {
            TextWatermarkOptions opts = new TextWatermarkOptions(text)
            {
                FontSize = size,
                Opacity = opacity,
                RotationDegrees = rotation,
            };
            WatermarkStamper.ApplyText(outFs, doc, opts);
        }

        stdout.WriteLine($"watermark: wrote {outputPath}");
        return 0;
    }
}

// ── redact ────────────────────────────────────────────────────────────────

/// <summary>Applies rectangle redactions. Each --rect is "page,x,y,w,h".</summary>
public sealed class RedactCommand : ICommand
{
    /// <inheritdoc />
    public int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        ParsedArgs p = ArgParser.Parse(args);

        if (p.Positional.Count == 0)
        {
            stderr.WriteLine("Usage: chuvadi redact <input.pdf> --output <out.pdf> --rect page,x,y,w,h [--rect ...]");
            return 2;
        }

        string inputPath = p.Positional[0];
        string outputPath = p.Get("output") ?? throw new ArgumentException("--output is required");
        IReadOnlyList<string> rectSpecs = p.GetAll("rect");

        if (rectSpecs.Count == 0)
        {
            stderr.WriteLine("redact: at least one --rect is required");
            return 2;
        }

        List<RedactionRect> rects = new List<RedactionRect>();

        foreach (string spec in rectSpecs)
        {
            string[] parts = spec.Split(',');

            if (parts.Length != 5)
            {
                stderr.WriteLine($"redact: malformed --rect '{spec}' (expected page,x,y,w,h)");
                return 2;
            }

            int pg = int.Parse(parts[0], CultureInfo.InvariantCulture);
            double x = double.Parse(parts[1], CultureInfo.InvariantCulture);
            double y = double.Parse(parts[2], CultureInfo.InvariantCulture);
            double w = double.Parse(parts[3], CultureInfo.InvariantCulture);
            double h = double.Parse(parts[4], CultureInfo.InvariantCulture);
            rects.Add(new RedactionRect(pg, new RectangleF(x, y, w, h)));
        }

        using (FileStream fs = File.OpenRead(inputPath))
        using (PdfDocument doc = PdfDocument.Open(fs, leaveOpen: false))
        using (FileStream outFs = File.Create(outputPath))
        {
            RedactionOptions opts = new RedactionOptions { Rectangles = rects };
            Redactor.Apply(outFs, doc, opts);
        }

        stdout.WriteLine($"redact: wrote {outputPath} ({rects.Count} rectangle(s))");
        return 0;
    }
}

// ── form-fill ─────────────────────────────────────────────────────────────

/// <summary>Fills AcroForm fields. Each --field is "name=value".</summary>
public sealed class FormFillCommand : ICommand
{
    /// <inheritdoc />
    public int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        ParsedArgs p = ArgParser.Parse(args);

        if (p.Positional.Count == 0)
        {
            stderr.WriteLine("Usage: chuvadi form-fill <input.pdf> --output <out.pdf> --field name=value [--field ...]");
            return 2;
        }

        string inputPath = p.Positional[0];
        string outputPath = p.Get("output") ?? throw new ArgumentException("--output is required");

        Dictionary<string, string> values = new Dictionary<string, string>();

        foreach (string fv in p.GetAll("field"))
        {
            int eq = fv.IndexOf('=');

            if (eq <= 0)
            {
                stderr.WriteLine($"form-fill: malformed --field '{fv}' (expected name=value)");
                return 2;
            }

            values[fv.Substring(0, eq)] = fv.Substring(eq + 1);
        }

        using (FileStream fs = File.OpenRead(inputPath))
        using (PdfDocument doc = PdfDocument.Open(fs, leaveOpen: false))
        using (FileStream outFs = File.Create(outputPath))
        {
            FormFiller.Fill(outFs, doc, values);
        }

        stdout.WriteLine($"form-fill: wrote {outputPath} ({values.Count} field(s))");
        return 0;
    }
}

// ── extract-text ──────────────────────────────────────────────────────────

/// <summary>Extracts text from a PDF.</summary>
public sealed class ExtractTextCommand : ICommand
{
    /// <inheritdoc />
    public int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        ParsedArgs p = ArgParser.Parse(args);

        if (p.Positional.Count == 0)
        {
            stderr.WriteLine("Usage: chuvadi extract-text <input.pdf> [--strategy operator|layout] [--output text.txt]");
            return 2;
        }

        string inputPath = p.Positional[0];
        string strategyName = p.Get("strategy", "operator")!;
        string? outputPath = p.Get("output");

        ExtractionStrategy strategy = strategyName switch
        {
            "operator" => ExtractionStrategy.Operator,
            "layout" => ExtractionStrategy.Layout,
            _ => throw new ArgumentException($"unknown strategy '{strategyName}' (use 'operator' or 'layout')"),
        };

        using (FileStream fs = File.OpenRead(inputPath))
        using (PdfDocument doc = PdfDocument.Open(fs, leaveOpen: false))
        {
            TextExtractor extractor = new TextExtractor(doc.Objects, strategy);
            TextWriter sink = outputPath is null ? stdout : new StreamWriter(outputPath);

            try
            {
                for (int i = 0; i < doc.PageCount; i++)
                {
                    string text = extractor.ExtractText(doc.Pages[i]);
                    sink.WriteLine(text);

                    if (i < doc.PageCount - 1)
                    {
                        sink.WriteLine();
                    }
                }
            }
            finally
            {
                if (outputPath is not null)
                {
                    sink.Dispose();
                }
            }
        }

        if (outputPath is not null)
        {
            stdout.WriteLine($"extract-text: wrote {outputPath}");
        }

        return 0;
    }
}

// ── outlines ──────────────────────────────────────────────────────────────

/// <summary>Lists the bookmark tree.</summary>
public sealed class OutlinesCommand : ICommand
{
    /// <inheritdoc />
    public int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        ParsedArgs p = ArgParser.Parse(args);

        if (p.Positional.Count == 0)
        {
            stderr.WriteLine("Usage: chuvadi outlines <input.pdf>");
            return 2;
        }

        string inputPath = p.Positional[0];

        using (FileStream fs = File.OpenRead(inputPath))
        using (PdfDocument doc = PdfDocument.Open(fs, leaveOpen: false))
        {
            IReadOnlyList<OutlineItem> items = OutlineReader.GetOutlines(doc);

            if (items.Count == 0)
            {
                stdout.WriteLine("(no outlines)");
                return 0;
            }

            PrintOutlineTree(items, depth: 0, stdout);
        }

        return 0;
    }

    private static void PrintOutlineTree(
        IReadOnlyList<OutlineItem> items, int depth, TextWriter stdout)
    {
        string indent = new string(' ', depth * 2);

        foreach (OutlineItem item in items)
        {
            string pageNote = item.DestinationPageIndex >= 0
                ? $" → page {item.DestinationPageIndex + 1}"
                : string.Empty;
            stdout.WriteLine($"{indent}{item.Title}{pageNote}");
            PrintOutlineTree(item.Children, depth + 1, stdout);
        }
    }
}

// ── merge ─────────────────────────────────────────────────────────────────

/// <summary>Combines multiple PDFs into one.</summary>
public sealed class MergeCommand : ICommand
{
    /// <inheritdoc />
    public int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        ParsedArgs p = ArgParser.Parse(args);

        if (p.Positional.Count < 2)
        {
            stderr.WriteLine("Usage: chuvadi merge <in1.pdf> <in2.pdf> [...] --output <out.pdf>");
            return 2;
        }

        string outputPath = p.Get("output") ?? throw new ArgumentException("--output is required");

        List<PdfDocument> docs = new List<PdfDocument>();
        List<FileStream> fileStreams = new List<FileStream>();

        try
        {
            foreach (string inPath in p.Positional)
            {
                FileStream fs = File.OpenRead(inPath);
                fileStreams.Add(fs);
                docs.Add(PdfDocument.Open(fs, leaveOpen: true));
            }

            using (FileStream outFs = File.Create(outputPath))
            {
                PageOperations.Merge(outFs, docs.ToArray());
            }
        }
        finally
        {
            foreach (PdfDocument d in docs)
            {
                d.Dispose();
            }

            foreach (FileStream f in fileStreams)
            {
                f.Dispose();
            }
        }

        stdout.WriteLine($"merge: wrote {outputPath} ({p.Positional.Count} input(s))");
        return 0;
    }
}

// ── split ─────────────────────────────────────────────────────────────────

/// <summary>Splits a PDF into one file per page.</summary>
public sealed class SplitCommand : ICommand
{
    /// <inheritdoc />
    public int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        ParsedArgs p = ArgParser.Parse(args);

        if (p.Positional.Count == 0)
        {
            stderr.WriteLine("Usage: chuvadi split <input.pdf> --output-dir <dir>");
            return 2;
        }

        string inputPath = p.Positional[0];
        string outputDir = p.Get("output-dir") ?? ".";
        Directory.CreateDirectory(outputDir);

        using (FileStream fs = File.OpenRead(inputPath))
        using (PdfDocument doc = PdfDocument.Open(fs, leaveOpen: false))
        {
            List<MemoryStream> pages = PageOperations.SplitPages(doc);

            for (int i = 0; i < pages.Count; i++)
            {
                string outPath = IoPath.Combine(outputDir, $"page_{i + 1:D3}.pdf");

                using (FileStream outFs = File.Create(outPath))
                {
                    pages[i].Seek(0, SeekOrigin.Begin);
                    pages[i].CopyTo(outFs);
                }

                pages[i].Dispose();
            }

            stdout.WriteLine($"split: wrote {pages.Count} file(s) to {outputDir}");
        }

        return 0;
    }
}

// ── delete ────────────────────────────────────────────────────────────────

/// <summary>Deletes pages from a PDF. --pages "1,3,5" (1-based).</summary>
public sealed class DeleteCommand : ICommand
{
    /// <inheritdoc />
    public int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        ParsedArgs p = ArgParser.Parse(args);

        if (p.Positional.Count == 0)
        {
            stderr.WriteLine("Usage: chuvadi delete <input.pdf> --pages 1,3,5 --output <out.pdf>");
            return 2;
        }

        string inputPath = p.Positional[0];
        string outputPath = p.Get("output") ?? throw new ArgumentException("--output is required");
        string pages = p.Get("pages") ?? throw new ArgumentException("--pages is required");

        List<int> indices = new List<int>();

        foreach (string s in pages.Split(','))
        {
            indices.Add(int.Parse(s, CultureInfo.InvariantCulture) - 1);
        }

        using (FileStream fs = File.OpenRead(inputPath))
        using (PdfDocument doc = PdfDocument.Open(fs, leaveOpen: false))
        using (FileStream outFs = File.Create(outputPath))
        {
            PageOperations.DeletePages(outFs, doc, indices);
        }

        stdout.WriteLine($"delete: wrote {outputPath} ({indices.Count} page(s) deleted)");
        return 0;
    }
}

// ── rotate ────────────────────────────────────────────────────────────────

/// <summary>Rotates pages by a multiple of 90 degrees.</summary>
public sealed class RotateCommand : ICommand
{
    /// <inheritdoc />
    public int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        ParsedArgs p = ArgParser.Parse(args);

        if (p.Positional.Count == 0)
        {
            stderr.WriteLine("Usage: chuvadi rotate <input.pdf> --degrees 90 [--page N] --output <out.pdf>");
            return 2;
        }

        string inputPath = p.Positional[0];
        string outputPath = p.Get("output") ?? throw new ArgumentException("--output is required");
        int degrees = int.Parse(p.Get("degrees", "90")!, CultureInfo.InvariantCulture);
        string? pageOpt = p.Get("page");

        List<int>? pageIndices = null;

        if (pageOpt is not null)
        {
            pageIndices = new List<int> { int.Parse(pageOpt, CultureInfo.InvariantCulture) };
        }

        using (FileStream fs = File.OpenRead(inputPath))
        using (PdfDocument doc = PdfDocument.Open(fs, leaveOpen: false))
        using (FileStream outFs = File.Create(outputPath))
        {
            PageOperations.RotatePages(outFs, doc, degrees, pageIndices);
        }

        stdout.WriteLine($"rotate: wrote {outputPath} ({degrees}°)");
        return 0;
    }
}
