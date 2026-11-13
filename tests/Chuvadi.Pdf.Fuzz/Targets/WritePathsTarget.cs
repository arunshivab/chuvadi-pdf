// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.34.0 (D-335 item 7b) — write paths join the fuzz run

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Forms;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Operations;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Redaction;
using Chuvadi.Pdf.Watermark;

namespace Chuvadi.Pdf.Fuzz.Targets;

/// <summary>
/// Opens a mutated PDF and runs it through the write paths: save, form fill,
/// merge, watermark, header/footer, flatten, compress, rotate and redact.
/// Since 3.34.0 every write path reports damaged input as a
/// <see cref="PdfException"/> (usually <see cref="PdfCorruptionException"/>),
/// so anything else escaping is a crash.
/// </summary>
internal sealed class WritePathsTarget : IFuzzTarget
{
    public string Name => "write-paths";

    public IReadOnlyList<Type> ExpectedExceptionTypes { get; } = new[]
    {
        // Every documented Chuvadi failure: corruption, parse, limit, form,
        // operations, redaction and watermark errors all derive from it.
        typeof(PdfException),
        // Opening (as in pdf-open): BCL-level rejections on truncated input
        // and empty PDF names (see FOLLOW-UPS.md).
        typeof(ArgumentException),
        typeof(EndOfStreamException),
        typeof(FormatException),
    };

    public void Run(byte[] input)
    {
        using MemoryStream stream = new(input, writable: false);
        using PdfDocument doc = PdfDocument.Open(stream, new PdfOpenOptions());
        if (doc.PageCount == 0)
        {
            return;
        }

        Write(output => doc.Save(output));

        Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (FormField field in FormReader.GetLeafFields(doc))
        {
            values[field.FullyQualifiedName] = "x";
        }

        Write(output => FormFiller.Fill(output, doc, values));
        Write(output => PageOperations.Merge(output, doc, doc));
        Write(output => WatermarkStamper.ApplyText(output, doc, new TextWatermarkOptions("FUZZ") { PlaceInDisplaySpace = true }));
        Write(output => HeaderFooter.Apply(output, doc, new HeaderFooterOptions { Footer = new BandText(center: "{page}/{total}") }));
        Write(output => AnnotationFlattener.Flatten(output, doc));
        Write(output => PdfCompressor.Compress(doc, output));
        Write(output => PageOperations.RotatePages(output, doc, 90));
        Write(output => Redactor.Apply(output, doc, new RedactionOptions
        {
            Rectangles = new List<RedactionRect> { new RedactionRect(0, new RectangleF(0, 0, 200, 200)) },
            MaxDegreeOfParallelism = 1,
        }));
    }

    private static void Write(Action<Stream> write)
    {
        using MemoryStream output = new MemoryStream();
        write(output);
    }
}
