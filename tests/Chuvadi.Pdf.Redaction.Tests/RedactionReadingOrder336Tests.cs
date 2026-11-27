// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ISO 32000-1 §9.4 (text showing)
// PHASE: 3.36.0 (D-410 A, RED-18, decided D-415(1)=b) — pattern matching runs
//        in both draw order and reading order and redacts the union, so a value
//        emitted out of stream order but laid out left to right on the page is
//        still caught. Draw order alone never joins its pieces.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using Chuvadi.Pdf.Documents;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Redaction.Tests;

public sealed class RedactionReadingOrder336Tests
{
    [Fact]
    public void Ssn_DrawnOutOfStreamOrder_IsCaughtInReadingOrder()
    {
        // "6789" is drawn first but to the right; "SSN 123-45-" is drawn second
        // but to the left. In draw order the text reads "6789SSN 123-45-" (no
        // SSN); in reading order it reads "SSN 123-45-6789" (an SSN).
        byte[] input = OnePage(
            "BT /F1 12 Tf 130 100 Td (6789) Tj ET BT /F1 12 Tf 50 100 Td (SSN 123-45-) Tj ET");

        byte[] output = RedactSsn(input);

        AssertGone(output, "123-45-6789", "6789", "123-45-");
        VerifyClean(output);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static byte[] RedactSsn(byte[] input)
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(input), leaveOpen: false);
        using MemoryStream output = new MemoryStream();
        RedactionOptions options = new RedactionOptions
        {
            Patterns = new List<PatternRule> { new PatternRule(CommonPatterns.UsSsn) },
        };
        Redactor.ApplyWithReport(output, doc, options);
        return output.ToArray();
    }

    private static void VerifyClean(byte[] output)
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(output), leaveOpen: false);
        RedactionVerification result = RedactionVerifier.VerifyPatterns(
            doc, new[] { new PatternRule(CommonPatterns.UsSsn) });
        result.IsClean.Should().BeTrue();
    }

    private static void AssertGone(byte[] pdf, params string[] pieces)
    {
        string all = AllText(pdf);
        foreach (string piece in pieces)
        {
            all.Should().NotContain(piece);
        }
    }

    private static string AllText(byte[] pdf)
    {
        string raw = Encoding.Latin1.GetString(pdf);
        StringBuilder sb = new StringBuilder(raw);
        int at = 0;
        while ((at = raw.IndexOf("stream", at, StringComparison.Ordinal)) >= 0)
        {
            int start = at + 6;
            if (start < raw.Length && raw[start] == '\r')
            {
                start++;
            }

            if (start < raw.Length && raw[start] == '\n')
            {
                start++;
            }

            int end = raw.IndexOf("endstream", start, StringComparison.Ordinal);
            if (end < 0)
            {
                break;
            }

            byte[] data = new byte[end - start];
            Array.Copy(pdf, start, data, 0, data.Length);
            try
            {
                using MemoryStream inputStream = new MemoryStream(data);
                using ZLibStream z = new ZLibStream(inputStream, CompressionMode.Decompress);
                using MemoryStream inflated = new MemoryStream();
                z.CopyTo(inflated);
                sb.Append('\n').Append(Encoding.Latin1.GetString(inflated.ToArray()));
            }
            catch (InvalidDataException)
            {
                // Not Flate data: already present in the raw bytes above.
            }

            at = end + 9;
        }

        return sb.ToString();
    }

    private static byte[] OnePage(string content)
    {
        List<byte[]> bodies = new List<byte[]>
        {
            Obj("<</Type/Catalog/Pages 2 0 R>>"),
            Obj("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Obj("<</Type/Page/Parent 2 0 R/MediaBox[0 0 400 400]/Resources<</Font<</F1 5 0 R>>>>/Contents 4 0 R>>"),
            Stream(content),
            Obj("<</Type/Font/Subtype/Type1/BaseFont/Helvetica/Encoding/WinAnsiEncoding>>"),
        };
        return Build(bodies);
    }

    private static byte[] Obj(string body) => Encoding.Latin1.GetBytes(body);

    private static byte[] Stream(string content)
    {
        byte[] bytes = Encoding.Latin1.GetBytes(content);
        using MemoryStream ms = new MemoryStream();
        ms.Write(Encoding.Latin1.GetBytes(
            "<</Length " + bytes.Length.ToString(CultureInfo.InvariantCulture) + ">>\nstream\n"));
        ms.Write(bytes);
        ms.Write(Encoding.Latin1.GetBytes("\nendstream"));
        return ms.ToArray();
    }

    private static byte[] Build(List<byte[]> bodies)
    {
        using MemoryStream ms = new MemoryStream();
        ms.Write(Encoding.Latin1.GetBytes("%PDF-1.7\n"));
        List<long> offsets = new List<long>();
        for (int i = 0; i < bodies.Count; i++)
        {
            offsets.Add(ms.Length);
            ms.Write(Encoding.Latin1.GetBytes((i + 1).ToString(CultureInfo.InvariantCulture) + " 0 obj\n"));
            ms.Write(bodies[i]);
            ms.Write(Encoding.Latin1.GetBytes("\nendobj\n"));
        }

        long xref = ms.Length;
        ms.Write(Encoding.Latin1.GetBytes(
            "xref\n0 " + (bodies.Count + 1).ToString(CultureInfo.InvariantCulture) + "\n0000000000 65535 f \n"));
        foreach (long off in offsets)
        {
            ms.Write(Encoding.Latin1.GetBytes(off.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n"));
        }

        ms.Write(Encoding.Latin1.GetBytes(
            "trailer <</Size " + (bodies.Count + 1).ToString(CultureInfo.InvariantCulture)
            + "/Root 1 0 R>>\nstartxref\n" + xref.ToString(CultureInfo.InvariantCulture) + "\n%%EOF"));
        return ms.ToArray();
    }
}
