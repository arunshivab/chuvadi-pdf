// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2.3 — Fuzzing harness corpus generator

using System;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Authoring;

namespace Chuvadi.Pdf.Fuzz;

/// <summary>
/// Helper to (re)generate seed corpus files programmatically. Run via
/// <c>dotnet run -c Release --project tests/Chuvadi.Pdf.Fuzz -- --regenerate-corpus</c>.
/// </summary>
/// <remarks>
/// Most corpus seeds are checked in as <c>.bin</c> files under <c>corpus/&lt;target&gt;/</c>.
/// For the PDF target we synthesize a few representative documents via
/// <see cref="PdfDocumentBuilder"/> so the corpus stays current even when the writer
/// changes. For content streams we ship hand-crafted byte sequences covering common
/// operators. The TrueType corpus is not pre-populated — Chuvadi has no TTF writer,
/// so the fuzzer falls back to a zero-byte default for that target. Drop a valid TTF
/// into <c>corpus/truetype/</c> to seed deeper exploration.
/// </remarks>
internal static class CorpusGenerator
{
    public static void RegenerateAll(string corpusRoot)
    {
        RegeneratePdfCorpus(Path.Combine(corpusRoot, "pdf-open"));
        RegeneratePdfCorpus(Path.Combine(corpusRoot, "write-paths"));
        RegenerateContentStreamCorpus(Path.Combine(corpusRoot, "content-stream"));
        // TrueType corpus is not regenerated — Chuvadi has no TTF writer. See README.

        // Also copy under AppContext.BaseDirectory so the running fuzzer (which reads
        // from bin/Release at runtime) picks the seeds up without a rebuild.
        string runtimeCorpus = Path.Combine(AppContext.BaseDirectory, "corpus");
        if (!string.Equals(Path.GetFullPath(corpusRoot), Path.GetFullPath(runtimeCorpus), StringComparison.Ordinal))
        {
            MirrorDirectory(corpusRoot, runtimeCorpus);
        }
    }

    private static void MirrorDirectory(string source, string dest)
    {
        if (!Directory.Exists(source)) { return; }
        Directory.CreateDirectory(dest);
        foreach (string srcFile in Directory.EnumerateFiles(source, "*.bin", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, srcFile);
            string destFile = Path.Combine(dest, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
            File.Copy(srcFile, destFile, overwrite: true);
        }
    }

    private static void RegeneratePdfCorpus(string dir)
    {
        Directory.CreateDirectory(dir);

        // The smallest valid PDF must contain at least one page; a document
        // with zero pages cannot be serialized (PdfDocumentBuilder.Save
        // throws InvalidOperationException). This seed approximates the
        // "minimal valid" boundary.
        WritePdf(dir, "minimal.bin", () =>
        {
            PdfDocumentBuilder doc = PdfDocumentBuilder.Create()
                .SetTitle("Minimal seed");
            doc.AddPage(PageSize.A4);
            return doc;
        });

        WritePdf(dir, "single-page.bin", () =>
        {
            PdfDocumentBuilder doc = PdfDocumentBuilder.Create()
                .SetTitle("Single page seed")
                .SetAuthor("Chuvadi.Pdf.Fuzz");
            PageBuilder page = doc.AddPage(PageSize.A4);
            page.DrawText("Hello", x: 50, y: 50,
                font: StandardFonts.Helvetica, size: 12, color: Colors.Black);
            return doc;
        });

        WritePdf(dir, "multi-page.bin", () =>
        {
            PdfDocumentBuilder doc = PdfDocumentBuilder.Create()
                .SetTitle("Multi-page seed");
            for (int i = 0; i < 5; i++)
            {
                PageBuilder page = doc.AddPage(PageSize.A4);
                page.DrawText($"Page {i + 1}", x: 50, y: 50,
                    font: StandardFonts.HelveticaBold, size: 14, color: Colors.Black);
            }
            return doc;
        });
    }

    private static void WritePdf(string dir, string name, Func<PdfDocumentBuilder> factory)
    {
        PdfDocumentBuilder doc = factory();
        using MemoryStream ms = new();
        doc.Save(ms);
        File.WriteAllBytes(Path.Combine(dir, name), ms.ToArray());
    }

    private static void RegenerateContentStreamCorpus(string dir)
    {
        Directory.CreateDirectory(dir);

        // Minimal text-showing stream
        WriteAscii(dir, "text-show.bin",
            "BT\n/F1 12 Tf\n50 700 Td\n(Hello, world) Tj\nET\n");

        // Path drawing with state save/restore
        WriteAscii(dir, "path-state.bin",
            "q\n1 0 0 1 100 100 cm\n0 0 m\n100 0 l\n100 100 l\n0 100 l\nh\nS\nQ\n");

        // Graphics state changes
        WriteAscii(dir, "graphics-state.bin",
            "q\n0.5 g\n0 0 100 100 re\nf\nQ\n0.8 G\n1 w\n50 50 m\n150 150 l\nS\n");

        // Inline image (briefly — full inline images require resources we don't have)
        WriteAscii(dir, "operators-mix.bin",
            "q\nBT\n/F1 10 Tf\n10 800 Td\n(line1) Tj\n0 -12 Td\n(line2) '\nET\nQ\n");
    }

    private static void WriteAscii(string dir, string name, string content)
    {
        File.WriteAllBytes(Path.Combine(dir, name), Encoding.ASCII.GetBytes(content));
    }
}
