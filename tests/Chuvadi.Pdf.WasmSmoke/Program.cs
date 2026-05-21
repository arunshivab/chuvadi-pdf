// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2.1 — WASM smoke test

using System;
using System.IO;
using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Rendering.DisplayList;
using Chuvadi.Pdf.Svg;
using Chuvadi.Pdf.Text;

// Exercise the full pipeline: author a PDF, open it, build a display list, render to SVG.
// Any OS-coupling (Process, Registry, File.*, System.Drawing.*) will fail the WASM build.

PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
builder.AddPage(PageSize.A4)
    .DrawText("Chuvadi on WASM", 50, 50, StandardFonts.Helvetica, 18, Colors.Black)
    .DrawRectangle(50, 100, 200, 100, fill: Colors.Red);
byte[] pdfBytes = builder.ToByteArray();

using MemoryStream ms = new(pdfBytes);
using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: false);

PageDisplayList list = doc.BuildDisplayList(0);
Console.WriteLine($"Display list: {list.Count} ops");

string svg = new SvgRenderer().RenderPage(doc, 0);
Console.WriteLine($"SVG: {svg.Length} chars");

System.Collections.Generic.IReadOnlyList<TextRun> runs = doc.GetTextRuns(0);
Console.WriteLine($"Text runs: {runs.Count}");
