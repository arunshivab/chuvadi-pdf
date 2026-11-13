// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.3.10 (a reference to no object is null)
// PHASE: 3.34.0 (#53, D-353) — the stamper and composer write a reference
//        they did not copy as null, not as a number of an unrelated object.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Operations;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Integration.Tests;

public sealed class UnmappedReferences3340Tests
{
    [Fact]
    public void Stamper_WritesAnUncopiedReferenceAsNull()
    {
        using PdfDocument source = TestPdf.Open(Source());
        using PdfDocument target = TestPdf.Open(TestPdf.OnePage("0 0 1 rg 0 0 10 10 re f", "<<>>", System.Array.Empty<string>()));
        using MemoryStream output = new MemoryStream();

        PageStamper.Place(output, target, 0, source, 0, Transform.Identity);

        output.Position = 0;
        using PdfDocument stamped = PdfDocument.Open(output, leaveOpen: true);
        MarkedParent(stamped).Should().BeOfType<PdfNull>();
    }

    [Fact]
    public void Composer_WritesAnUncopiedReferenceAsNull()
    {
        using PdfDocument source = TestPdf.Open(Source());
        using MemoryStream output = new MemoryStream();

        new PageComposer().AddPage(200, 200).PlacePage(source, 0, Transform.Identity).Write(output);

        output.Position = 0;
        using PdfDocument composed = PdfDocument.Open(output, leaveOpen: true);
        MarkedParent(composed).Should().BeOfType<PdfNull>();
    }

    // A page whose form XObject (marked /Marker true) has a /Parent pointing
    // at the source's page tree, which the importers never copy.
    private static byte[] Source()
    {
        TestPdf pdf = new TestPdf();
        int catalog = pdf.Reserve();
        int pages = pdf.Reserve();
        int form = pdf.AddStream("<</Type/XObject/Subtype/Form/BBox[0 0 50 50]/Marker true/Parent " + N(pages) + " 0 R>>", "0 1 0 rg 0 0 50 50 re f");
        int contents = pdf.AddStream("<<>>", "/Fx Do");
        int page = pdf.Add("<</Type/Page/Parent " + N(pages) + " 0 R/MediaBox[0 0 200 200]/Resources<</XObject<</Fx " + N(form) + " 0 R>>>>/Contents " + N(contents) + " 0 R>>");
        pdf.Set(pages, "<</Type/Pages/Kids[" + N(page) + " 0 R]/Count 1>>");
        pdf.Set(catalog, "<</Type/Catalog/Pages " + N(pages) + " 0 R>>");
        return pdf.Build(catalog);
    }

    // The /Parent of the marked form, resolved, found by walking from page 1.
    private static PdfPrimitive? MarkedParent(PdfDocument doc)
    {
        Stack<PdfPrimitive> todo = new Stack<PdfPrimitive>();
        HashSet<int> seen = new HashSet<int>();
        PdfDictionary page = doc.Pages[0].Dictionary;
        foreach (KeyValuePair<PdfName, PdfPrimitive> entry in page)
        {
            if (entry.Key != PdfName.Parent)
            {
                todo.Push(entry.Value);
            }
        }

        while (todo.Count > 0)
        {
            PdfPrimitive item = todo.Pop();
            if (item is PdfReference r)
            {
                if (seen.Add(r.ObjectId.ObjectNumber))
                {
                    todo.Push(doc.Objects.Resolve(r));
                }

                continue;
            }

            PdfDictionary? dict = item as PdfDictionary ?? (item as PdfStream)?.Dictionary;
            if (dict is not null)
            {
                if (dict.TryGetValue(PdfName.Intern("Marker"), out PdfPrimitive? _))
                {
                    return dict.TryGetValue(PdfName.Parent, out PdfPrimitive? parent) ? doc.Objects.Resolve(parent) : null;
                }

                foreach (KeyValuePair<PdfName, PdfPrimitive> entry in dict)
                {
                    todo.Push(entry.Value);
                }
            }
            else if (item is PdfArray array)
            {
                foreach (PdfPrimitive element in array)
                {
                    todo.Push(element);
                }
            }
        }

        throw new Xunit.Sdk.XunitException("The marked form was not found.");
    }

    private static string N(int n) => n.ToString(CultureInfo.InvariantCulture);
}
