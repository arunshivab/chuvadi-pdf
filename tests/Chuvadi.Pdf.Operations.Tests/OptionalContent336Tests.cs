// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ISO 32000-1 §8.11 (optional content)
// PHASE: 3.36.0 (D-410 A, OPS-02) — page operations carry /OCProperties, so a
//        layer that was hidden by default stays hidden in the output instead
//        of defaulting to visible; only the layers the written pages use are
//        kept.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

public sealed class OptionalContent336Tests
{
    [Fact]
    public void Extract_KeepingThePageThatUsesALayer_KeepsItHidden()
    {
        byte[] input = TwoPageWithHiddenLayer();
        using MemoryStream output = new MemoryStream();

        using (PdfDocument doc = Open(input))
        {
            PageOperations.ExtractPages(output, doc, 0, 1);
        }

        OffLayerNames(output.ToArray()).Should().Contain("Hidden");
    }

    [Fact]
    public void Rotate_KeepsTheHiddenLayerHidden()
    {
        byte[] input = TwoPageWithHiddenLayer();
        using MemoryStream output = new MemoryStream();

        using (PdfDocument doc = Open(input))
        {
            PageOperations.RotatePages(output, doc, 90);
        }

        OffLayerNames(output.ToArray()).Should().Contain("Hidden");
    }

    [Fact]
    public void Reorder_KeepsTheHiddenLayerHidden()
    {
        byte[] input = TwoPageWithHiddenLayer();
        using MemoryStream output = new MemoryStream();

        using (PdfDocument doc = Open(input))
        {
            PageOperations.ReorderPages(output, doc, new[] { 1, 0 });
        }

        OffLayerNames(output.ToArray()).Should().Contain("Hidden");
    }

    [Fact]
    public void Extract_OfThePageThatDoesNotUseTheLayer_DropsIt()
    {
        byte[] input = TwoPageWithHiddenLayer();
        using MemoryStream output = new MemoryStream();

        using (PdfDocument doc = Open(input))
        {
            PageOperations.ExtractPages(output, doc, 1, 1);
        }

        // Page 2 uses no layer, so the output carries no optional content.
        using PdfDocument result = Open(output.ToArray());
        result.Catalog.ContainsKey(PdfName.Intern("OCProperties")).Should().BeFalse();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static PdfDocument Open(byte[] bytes) => PdfDocument.Open(new MemoryStream(bytes), leaveOpen: false);

    // The /Name of each layer in the output's /OCProperties /D /OFF list.
    private static List<string> OffLayerNames(byte[] pdf)
    {
        List<string> names = new List<string>();
        using PdfDocument doc = Open(pdf);
        if (doc.Catalog.TryGetValue(PdfName.Intern("OCProperties"), out PdfPrimitive? ocpValue)
            && doc.Objects.Resolve(ocpValue) is PdfDictionary ocp
            && ocp.TryGetValue(PdfName.Intern("D"), out PdfPrimitive? dValue)
            && doc.Objects.Resolve(dValue) is PdfDictionary config
            && config.TryGetValue(PdfName.Intern("OFF"), out PdfPrimitive? offValue)
            && doc.Objects.Resolve(offValue) is PdfArray off)
        {
            foreach (PdfPrimitive item in off)
            {
                if (doc.Objects.Resolve(item) is PdfDictionary ocg
                    && ocg.TryGetValue(PdfName.Intern("Name"), out PdfPrimitive? nameValue)
                    && nameValue is PdfString name)
                {
                    names.Add(Encoding.Latin1.GetString(name.Bytes));
                }
            }
        }

        return names;
    }

    // Two pages: page 1 draws inside an OCG named "Hidden" that the catalog's
    // /OCProperties marks OFF by default; page 2 uses no optional content.
    private static byte[] TwoPageWithHiddenLayer()
    {
        List<byte[]> bodies = new List<byte[]>
        {
            Obj("<</Type/Catalog/Pages 2 0 R/OCProperties<</OCGs[6 0 R]/D<</Name(Default)/OFF[6 0 R]>>>>>>"),
            Obj("<</Type/Pages/Kids[3 0 R 4 0 R]/Count 2>>"),
            Obj("<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]"
                + "/Resources<</Font<</F1 5 0 R>>/Properties<</MC0 6 0 R>>>>/Contents 7 0 R>>"),
            Obj("<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]/Resources<</Font<</F1 5 0 R>>>>/Contents 8 0 R>>"),
            Obj("<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>"),
            Obj("<</Type/OCG/Name(Hidden)>>"),
            Stream("/OC /MC0 BDC BT /F1 12 Tf 72 700 Td (secret) Tj ET EMC"),
            Stream("BT /F1 12 Tf 72 700 Td (public) Tj ET"),
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
