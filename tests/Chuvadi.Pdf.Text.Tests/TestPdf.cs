// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.29 — a one-page PDF built in memory for extraction tests.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Chuvadi.Pdf.Documents;

namespace Chuvadi.Pdf.Text.Tests;

/// <summary>
/// Builds a one-page PDF: object 1 the catalog, 2 the page tree, 3 the page,
/// 4 the content stream, 5 Helvetica with every width 500; extra objects are
/// numbered from 6.
/// </summary>
internal static class TestPdf
{
    internal static PdfDocument Open(string content, string catalogExtra = "", IReadOnlyList<string>? extraObjects = null)
    {
        string font = "<</Type/Font/Subtype/Type1/BaseFont/Helvetica/FirstChar 32/LastChar 126/Widths[" + string.Concat(Enumerable.Repeat("500 ", 95)) + "]>>";
        List<string> bodies = new List<string>
        {
            "<</Type/Catalog/Pages 2 0 R" + catalogExtra + ">>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 600 800]/Resources<</Font<</F1 5 0 R>>>>/Contents 4 0 R>>",
            "<</Length " + Encoding.Latin1.GetByteCount(content).ToString(CultureInfo.InvariantCulture) + ">>\nstream\n" + content + "\nendstream",
            font,
        };
        if (extraObjects is not null)
        {
            bodies.AddRange(extraObjects);
        }

        StringBuilder sb = new StringBuilder("%PDF-1.7\n");
        List<int> offsets = new List<int>();
        for (int i = 0; i < bodies.Count; i++)
        {
            offsets.Add(sb.Length);
            sb.Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append(" 0 obj\n").Append(bodies[i]).Append("\nendobj\n");
        }

        int xref = sb.Length;
        sb.Append("xref\n0 ").Append((bodies.Count + 1).ToString(CultureInfo.InvariantCulture)).Append("\n0000000000 65535 f \n");
        foreach (int off in offsets)
        {
            sb.Append(off.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        sb.Append("trailer\n<</Size ").Append((bodies.Count + 1).ToString(CultureInfo.InvariantCulture))
          .Append("/Root 1 0 R>>\nstartxref\n").Append(xref.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF\n");
        return PdfDocument.Open(new MemoryStream(Encoding.Latin1.GetBytes(sb.ToString())));
    }

    /// <summary>One line of text at (x, y) in 10 pt Helvetica.</summary>
    internal static string Line(double x, double y, string text) =>
        string.Create(CultureInfo.InvariantCulture, $"BT /F1 10 Tf {x} {y} Td ({text}) Tj ET\n");

    /// <summary>Splits extracted text into lines, whatever the platform's newline.</summary>
    internal static string[] Lines(string text) =>
        text.Replace("\r\n", "\n", System.StringComparison.Ordinal).Split('\n');
}
