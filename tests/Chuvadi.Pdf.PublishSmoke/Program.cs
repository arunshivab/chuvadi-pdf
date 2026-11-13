// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.34.0 (D-335 item 8) — publish smoke test: trimmed and ReadyToRun.

using System;
using System.IO;
using System.Threading;
using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Rendering;
using Chuvadi.Pdf.Rendering.DisplayList;
using Chuvadi.Pdf.Text;

// Exercise the paths that load embedded resources or use generics heavily:
// the Liberation substitute (Chuvadi.Fonts.Liberation), the standard-14
// outlines, the PNG encoder, text extraction and search.
PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
builder.EmbedStandardFonts();
builder.AddPage(PageSize.A4)
    .DrawText("Chuvadi publish smoke", 50, 750, StandardFonts.Helvetica, 18, Colors.Black)
    .DrawRectangle(50, 600, 200, 100, fill: Colors.Red);
byte[] pdfBytes = builder.ToByteArray();

using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdfBytes));

byte[] png = new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72 }).RasterizeToPng(doc.Pages[0]);
Check(png.Length > 100 && png[1] == (byte)'P' && png[2] == (byte)'N' && png[3] == (byte)'G', "PNG output");

string text = new TextExtractor(doc.Objects).ExtractText(doc.Pages[0]);
Check(text.Contains("Chuvadi publish smoke", StringComparison.Ordinal), "text extraction");

int hits = 0;
await foreach (SearchMatch match in DocumentSearch.SearchAsync(doc, "publish", new SearchOptions(), CancellationToken.None))
{
    hits += match.BoundingBoxes.Count > 0 ? 1 : 0;
}

Check(hits == 1, "search");

Console.WriteLine("Publish smoke: OK");
return 0;

static void Check(bool condition, string what)
{
    if (!condition)
    {
        Console.Error.WriteLine("Publish smoke FAILED: " + what);
        Environment.Exit(1);
    }
}
