// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.35.0 (DOC-01, OPS-08) — the walks over a file's objects are
//        iterative, so 20,000 bookmarks, side by side or nested each inside
//        the last, no longer overflow the stack when the document is saved,
//        flattened, merged, compressed, sanitised, rotated or has pages
//        deleted. The inputs follow the Checker's bookmark probe (D-394).

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Chuvadi.Pdf.Documents;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

public sealed class LongOutlines3350Tests
{
    private const int Count = 20_000;

    public static TheoryData<string, bool> Cases()
    {
        TheoryData<string, bool> data = new TheoryData<string, bool>();
        foreach (string op in new[] { "save", "flatten", "merge", "delete", "compress", "sanitize", "rotate" })
        {
            data.Add(op, false);
            data.Add(op, true);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void TwentyThousandBookmarks_SurviveTheOperation(string op, bool nested)
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(Outlines(nested)), leaveOpen: false);
        using MemoryStream output = new MemoryStream();

        switch (op)
        {
            case "save":
                doc.Save(output);
                break;
            case "flatten":
                AnnotationFlattener.Flatten(output, doc);
                break;
            case "merge":
                PageOperations.Merge(output, new[] { doc }, new MergeOptions { PreserveOutlines = true });
                break;
            case "delete":
                PageOperations.DeletePages(output, doc, new int[0]);
                break;
            case "compress":
                PdfCompressor.Compress(doc, output, PdfCompressionLevel.Lossless);
                break;
            case "sanitize":
                PdfSanitizer.Sanitize(doc, output);
                break;
            default:
                PageOperations.RotatePages(output, doc, 90);
                break;
        }

        output.Length.Should().BeGreaterThan(0);
        using PdfDocument reopened = PdfDocument.Open(new MemoryStream(output.ToArray()), leaveOpen: false);
        reopened.PageCount.Should().Be(1);
        string raw = Raw(output.ToArray());
        if (op == "merge" && nested)
        {
            // Merge rebuilds the outline from the bookmarks it reads, and
            // bookmarks are read 256 levels deep (OutlineReader).
            HasTitle(raw, "Item 256").Should().BeTrue();
            HasTitle(raw, "Item 257").Should().BeFalse();
        }
        else if (op is "save" or "flatten" or "compress" or "merge")
        {
            HasTitle(raw, "Item 19999").Should().BeTrue("the last bookmark is written");
        }
    }

    // A title written as a literal or as a hex string.
    private static bool HasTitle(string raw, string title) =>
        raw.Contains("(" + title + ")", System.StringComparison.Ordinal)
        || raw.Contains("<" + System.Convert.ToHexString(Encoding.Latin1.GetBytes(title)) + ">", System.StringComparison.Ordinal);

    // The file's bytes, with every Flate stream inflated.
    private static string Raw(byte[] pdf)
    {
        string s = Encoding.Latin1.GetString(pdf);
        StringBuilder sb = new StringBuilder(s);
        foreach (Match m in Regex.Matches(s, @"stream\r?\n(.*?)endstream", RegexOptions.Singleline))
        {
            try
            {
                using ZLibStream z = new ZLibStream(new MemoryStream(Encoding.Latin1.GetBytes(m.Groups[1].Value)), CompressionMode.Decompress);
                using MemoryStream o = new MemoryStream();
                z.CopyTo(o);
                sb.Append(Encoding.Latin1.GetString(o.ToArray()));
            }
            catch (InvalidDataException)
            {
                // Not Flate data: already in the raw text.
            }
        }

        return sb.ToString();
    }

    // Objects: 1 catalog, 2 pages, 3 page, 4 font, 5 content, 6 outline root,
    // 7 onwards the bookmarks.
    private static byte[] Outlines(bool nested)
    {
        const int root = 6;
        const int first = 7;
        const string data = "BT /F1 12 Tf 72 700 Td (Hello) Tj ET";
        List<string> objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R /Outlines 6 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            "<< /Length " + data.Length.ToString(CultureInfo.InvariantCulture) + " >>\nstream\n" + data + "\nendstream",
            nested
                ? $"<< /Type /Outlines /First {first} 0 R /Last {first} 0 R /Count {Count} >>"
                : $"<< /Type /Outlines /First {first} 0 R /Last {first + Count - 1} 0 R /Count {Count} >>",
        };
        for (int i = 0; i < Count; i++)
        {
            int me = first + i;
            StringBuilder item = new StringBuilder();
            item.Append(CultureInfo.InvariantCulture, $"<< /Title (Item {i}) /Dest [3 0 R /Fit] ");
            if (nested)
            {
                item.Append(CultureInfo.InvariantCulture, $"/Parent {(i == 0 ? root : me - 1)} 0 R ");
                if (i < Count - 1)
                {
                    item.Append(CultureInfo.InvariantCulture, $"/First {me + 1} 0 R /Last {me + 1} 0 R /Count 1 ");
                }
            }
            else
            {
                item.Append(CultureInfo.InvariantCulture, $"/Parent {root} 0 R ");
                if (i > 0)
                {
                    item.Append(CultureInfo.InvariantCulture, $"/Prev {me - 1} 0 R ");
                }

                if (i < Count - 1)
                {
                    item.Append(CultureInfo.InvariantCulture, $"/Next {me + 1} 0 R ");
                }
            }

            objects.Add(item.Append(">>").ToString());
        }

        StringBuilder sb = new StringBuilder("%PDF-1.7\n");
        List<int> offsets = new List<int>();
        for (int i = 0; i < objects.Count; i++)
        {
            offsets.Add(sb.Length);
            sb.Append(CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        int xref = sb.Length;
        sb.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (int offset in offsets)
        {
            sb.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        sb.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(sb.ToString());
    }
}
