// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ISO 32000-1 §7.3.4.2 (literal-string escapes)
// PHASE: 3.36.0 (D-410 A, RED-16/17) — the redactor decodes a show string's
//        escapes before it decides, glyph by glyph, what to keep or cut. An
//        undecoded escape (\( \\ an octal code, or a line continuation) used
//        to be counted as several characters, shifting every later cut and
//        leaving matched content on the page or double-escaping survivors.

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

public sealed class RedactionEscape336Tests
{
    private const string Keep = "BT /F1 12 Tf 50 300 Td (KEEPME) Tj ET ";

    // The SSN's digits written as octal escapes (\061='1' ... \071='9'): a
    // matcher that does not decode sees backslashes and digits, not "123-45-6789".
    private const string SsnOctal = "SSN \\061\\062\\063-\\064\\065-\\066\\067\\070\\071";

    [Fact]
    public void Ssn_WrappedInEscapedParens_IsRemovedAndSurvivorsNotDoubleEscaped()
    {
        byte[] input = OnePage(Keep + "BT /F1 12 Tf 50 100 Td (\\(SSN 123-45-6789\\)) Tj ET");

        byte[] output = RedactSsn(input);

        AssertGone(output, "123-45-6789", "6789");
        VerifyClean(output);
        string shown = PageText(output);
        shown.Should().Contain("KEEPME");
        // The literal parentheses survive as one character each, not as a
        // stray reverse solidus left by re-escaping an already-escaped byte.
        shown.Should().NotContain("\\");
    }

    [Fact]
    public void Ssn_WrittenAsOctalEscapes_IsDecodedThenRemoved()
    {
        byte[] input = OnePage(Keep + "BT /F1 12 Tf 50 100 Td (" + SsnOctal + ") Tj ET");

        byte[] output = RedactSsn(input);

        AssertGone(output, "123-45-6789", "6789");
        VerifyClean(output);
        PageText(output).Should().Contain("KEEPME");
    }

    [Fact]
    public void Ssn_SplitByLineContinuation_IsDecodedThenRemoved()
    {
        // A reverse solidus at end of line continues the string (§7.3.4.2); the
        // SSN is one value "123-45-6789" once the continuation is resolved.
        byte[] input = OnePage(Keep + "BT /F1 12 Tf 50 100 Td (SSN 123-45-\\\n6789) Tj ET");

        byte[] output = RedactSsn(input);

        AssertGone(output, "123-45-6789", "6789");
        VerifyClean(output);
        PageText(output).Should().Contain("KEEPME");
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

    private static string PageText(byte[] pdf)
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        StringBuilder sb = new StringBuilder();
        foreach (RedactionSurvivor survivor in RedactionVerifier.ExtractWithin(
            doc, 0, new Chuvadi.Pdf.Graphics.RectangleF(0, 0, 400, 400)))
        {
            if (survivor.Kind == RedactionSurvivorKind.Text)
            {
                sb.Append(survivor.Text);
            }
        }

        return sb.ToString();
    }

    private static void AssertGone(byte[] pdf, params string[] pieces)
    {
        string all = AllText(pdf);
        foreach (string piece in pieces)
        {
            all.Should().NotContain(piece);
            all.Should().NotContain(Convert.ToHexString(Encoding.Latin1.GetBytes(piece)));
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
