// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ISO 19005-1 §6.6.1, §6.6.2 (actions), §6.3.5-3 (CIDSet);
//        ISO 19005-2 §6.6.1, §6.2.11.4.2
// PHASE: 3.36.0 (D-410 A, PDA-01) — the writer removes the actions PDF/A
//        forbids (and /AA) with a warning, and rebuilds a subset CID font's
//        /CIDSet from its embedded program.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.PdfA.Tests;

public sealed class PdfAActionsAndCidSet336Tests
{
    [Fact]
    public void ForbiddenFormActions_AreRemoved_WithAWarning()
    {
        byte[] input = WidgetWithResetAndAdditionalActions();

        (byte[] output, PdfAResult result) = ToPdfA(input, PdfAConformance.PdfA2B);

        result.Succeeded.Should().BeTrue(string.Join("; ", result.Violations));
        string text = Encoding.Latin1.GetString(output);
        text.Should().NotContain("ResetForm");
        text.Should().NotContain("/AA");
        text.Should().NotContain("JavaScript");
        result.Warnings.Should().Contain(w => w.Contains("ResetForm"));
        result.Warnings.Should().Contain(w => w.Contains("/AA"));
    }

    [Fact]
    public void SubsetCidFont_GetsACidSetRebuilt()
    {
        PdfDocumentBuilder doc = PdfDocumentBuilder.Create();
        doc.AddTrueTypeFont("Emb", LiberationFaces.Get("LiberationSans-Regular")!);
        doc.AddPage(PageSize.A4).DrawText("Hello 123", 50, 50, "Emb", 12, Colors.Black);
        byte[] input = doc.ToByteArray();

        (byte[] output, PdfAResult result) = ToPdfA(input, PdfAConformance.PdfA2B);

        result.Succeeded.Should().BeTrue(string.Join("; ", result.Violations));
        CidSetLength(output).Should().BeGreaterThan(0);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static (byte[] Output, PdfAResult Result) ToPdfA(byte[] input, PdfAConformance conformance)
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(input), leaveOpen: false);
        using MemoryStream output = new MemoryStream();
        PdfAResult result = PdfAWriter.Write(output, doc, new PdfAOptions { Conformance = conformance });
        return (output.ToArray(), result);
    }

    // The length of the first embedded CID font descriptor's /CIDSet stream, or
    // zero when there is none.
    private static int CidSetLength(byte[] pdf)
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        for (int i = 0; i < doc.PageCount; i++)
        {
            if (doc.Pages[i].Resources is not PdfDictionary resources
                || doc.Objects.Resolve(Lookup(resources, "Font")) is not PdfDictionary fonts)
            {
                continue;
            }

            foreach (PdfPrimitive value in fonts.Values)
            {
                if (doc.Objects.Resolve(value) is not PdfDictionary type0
                    || doc.Objects.Resolve(Lookup(type0, "DescendantFonts")) is not PdfArray descendants
                    || descendants.Count == 0
                    || doc.Objects.Resolve(descendants[0]) is not PdfDictionary cidFont
                    || doc.Objects.Resolve(Lookup(cidFont, "FontDescriptor")) is not PdfDictionary descriptor
                    || doc.Objects.Resolve(Lookup(descriptor, "CIDSet")) is not PdfStream cidSet)
                {
                    continue;
                }

                return cidSet.RawBytes.Length;
            }
        }

        return 0;
    }

    private static PdfPrimitive Lookup(PdfDictionary dict, string key) =>
        dict.TryGetValue(PdfName.Intern(key), out PdfPrimitive? value) && value is not null ? value : PdfNull.Value;

    private static byte[] WidgetWithResetAndAdditionalActions()
    {
        List<byte[]> bodies = new List<byte[]>
        {
            Obj("<</Type/Catalog/Pages 2 0 R/AcroForm 6 0 R>>"),
            Obj("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Obj("<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]"
                + "/Resources<</Font<</F1 5 0 R>>>>/Contents 7 0 R/Annots[4 0 R]>>"),
            Obj("<</Type/Annot/Subtype/Widget/Rect[72 700 200 720]/FT/Btn/T(reset)/P 3 0 R/F 4"
                + "/A<</S/ResetForm>>/AA<</U<</S/JavaScript/JS(app.alert\\(1\\))>>>>>>"),
            Obj("<</Type/Font/Subtype/Type1/BaseFont/Helvetica/Encoding/WinAnsiEncoding>>"),
            Obj("<</Fields[4 0 R]>>"),
            Stream("BT /F1 12 Tf 72 600 Td (body) Tj ET"),
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
