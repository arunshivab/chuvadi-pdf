// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.10.1 (form XObjects), §8.3.3 (CTM), §8.5.3.1 (re/f),
//        §9.4.2 (Tm text matrix)
// Regression tests for LA-19: HeaderFooterOptions.Background must fill the
// reserved band rectangle(s), not flood the whole page, under the reflow fit
// modes; the shared PageOverlay recolour wash must still fill the whole page;
// and the header text baseline must sit inside the header band.

using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

public sealed class HeaderFooterBackgroundTests
{
    private const int PageW = 595;
    private const int PageH = 842;
    private const int Band = 36;

    // Default HeaderBaselineOffset is -24 (24 pt below the top of the band).
    private const int HeaderBaselineDrop = 24;

    [Fact]
    public void ReserveAndScale_Background_ConfinedToHeaderAndFooterBands()
    {
        string ops = ApplyAndReadContent(new HeaderFooterOptions
        {
            Header = new BandText(center: "HEADER"),
            Footer = new BandText(center: "FOOTER {page}"),
            HeaderHeight = Band,
            FooterHeight = Band,
            Background = ColorF.FromRgb8(220, 40, 40),
            Fit = PageContentFit.ReserveAndScale,
        });

        // Header band at the page top (y = PageH - Band), footer band at the
        // page bottom (y = 0); both span the full page width.
        ops.Should().Contain($"0 {PageH - Band} {PageW} {Band} re");
        ops.Should().Contain($"0 0 {PageW} {Band} re");

        // The fill must NOT cover the whole page height (the LA-19 flood).
        ops.Should().NotContain($"{PageW} {PageH} re");
    }

    [Fact]
    public void ScaleIfIntruding_FooterOnly_Background_ConfinedToFooterBand()
    {
        string ops = ApplyAndReadContent(new HeaderFooterOptions
        {
            Footer = new BandText(center: "FOOTER {page}"),
            HeaderHeight = Band,
            FooterHeight = Band,
            Background = ColorF.FromRgb8(10, 90, 200),
            Fit = PageContentFit.ScaleIfIntruding,
        });

        // Footer-only: the footer band is filled, no header band is emitted,
        // and the page is not flooded.
        ops.Should().Contain($"0 0 {PageW} {Band} re");
        ops.Should().NotContain($"0 {PageH - Band} {PageW} {Band} re");
        ops.Should().NotContain($"{PageW} {PageH} re");
    }

    [Fact]
    public void ReserveAndScale_HeaderText_BaselineSitsInsideHeaderBand()
    {
        string ops = ApplyAndReadContent(new HeaderFooterOptions
        {
            Header = new BandText(center: "HEADER"),
            HeaderHeight = Band,
            Fit = PageContentFit.ReserveAndScale,
        });

        // Baseline is measured from the top of the band (PageH), so it lands
        // PageH - HeaderBaselineDrop = inside the band [PageH - Band, PageH].
        int baseline = PageH - HeaderBaselineDrop;
        baseline.Should().BeInRange(PageH - Band, PageH);
        ops.Should().Contain($" {baseline} Tm");

        // The old (buggy) baseline subtracted the band height, landing below
        // the band; it must not be emitted.
        int buggyBaseline = PageH - Band - HeaderBaselineDrop;
        ops.Should().NotContain($" {buggyBaseline} Tm");
    }

    [Fact]
    public void PageOverlay_Background_StillFillsWholePage()
    {
        using MemoryStream src = BuildTextPdf("Body");
        using PdfDocument doc = PdfDocument.Open(src, leaveOpen: true);

        using MemoryStream output = new MemoryStream();
        PageOverlay.Apply(output, doc, null, ColorF.FromRgb8(30, 30, 30), 1f);

        string ops = ReadPageContent(output);

        // The shared PageContentEditor recolour path must remain full-page.
        ops.Should().Contain($"0 0 {PageW} {PageH} re");
    }

    private static string ApplyAndReadContent(HeaderFooterOptions options)
    {
        using MemoryStream src = BuildTextPdf("Body line near the middle");
        using PdfDocument doc = PdfDocument.Open(src, leaveOpen: true);

        using MemoryStream output = new MemoryStream();
        HeaderFooter.Apply(output, doc, options);
        return ReadPageContent(output);
    }

    // Concatenates every content stream of page 0 (main fill/content plus any
    // header/footer overlay streams) into one decoded string.
    private static string ReadPageContent(MemoryStream output)
    {
        output.Position = 0;
        using PdfDocument result = PdfDocument.Open(output, leaveOpen: true);

        PdfPrimitive contents = result.Pages[0].Contents!;
        StringBuilder all = new StringBuilder();

        if (contents is PdfArray array)
        {
            for (int i = 0; i < array.Count; i++)
            {
                AppendStream(all, result, array[i]);
            }
        }
        else
        {
            AppendStream(all, result, contents);
        }

        return all.ToString();
    }

    private static void AppendStream(StringBuilder sb, PdfDocument doc, PdfPrimitive reference)
    {
        PdfStream stream = (PdfStream)doc.Objects.Resolve(reference);
        sb.Append(Encoding.Latin1.GetString(stream.RawBytes)).Append('\n');
    }

    private static MemoryStream BuildTextPdf(params string[] pageTexts)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);

        PdfArray kids = new PdfArray(new List<PdfPrimitive>());
        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, kids);
        pagesDict.Set(PdfName.Count, pageTexts.Length);
        pagesDict.Set(PdfName.MediaBox, new PdfArray(new List<PdfPrimitive>
        {
            new PdfInteger(0), new PdfInteger(0),
            new PdfInteger(PageW), new PdfInteger(PageH),
        }));

        PdfDictionary catalogDict = new PdfDictionary();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(catalogId, catalogDict),
            new PdfIndirectObject(pagesId, pagesDict),
        };

        int next = 3;
        foreach (string pageText in pageTexts)
        {
            PdfObjectId pageId = new PdfObjectId(next++, 0);
            PdfObjectId contentId = new PdfObjectId(next++, 0);

            byte[] content = Encoding.ASCII.GetBytes($"BT ({pageText}) Tj ET");
            PdfDictionary contentDict = new PdfDictionary();
            contentDict.Set(PdfName.Length, content.Length);

            PdfDictionary pageDict = new PdfDictionary();
            pageDict.Set(PdfName.Type, PdfName.Page);
            pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
            pageDict.Set(PdfName.Contents, new PdfReference(contentId));

            objects.Add(new PdfIndirectObject(pageId, pageDict));
            objects.Add(new PdfIndirectObject(contentId, new PdfStream(contentDict, content)));
            kids.Add(new PdfReference(pageId));
        }

        MemoryStream ms = new MemoryStream();
        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));
        Chuvadi.Pdf.IO.PdfWriter.Write(ms, objects, trailer);
        ms.Position = 0;
        return ms;
    }
}
