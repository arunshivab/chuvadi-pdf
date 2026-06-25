// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.5 (paths); §9.4 (text showing); §8.9.5 (image XObjects)
// Tests for PageCropMode.Scrub: region-aware content removal (C3 + C3b).

using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Filters;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

public sealed class PageCropScrubTests
{
    [Fact]
    public void Scrub_Fills_KeepsInsideRemovesOutsideClipsCrossing()
    {
        byte[] content = Encoding.Latin1.GetBytes(
            "1 0 0 rg\n50 50 100 100 re f\n500 500 100 100 re f\n350 50 100 100 re f\n");
        string scrubbed = ScrubAndDecode(
            BuildPdf(content, "<<>>"), new RectangleF(0, 0, 400, 400));

        scrubbed.Should().Contain("50 50 100 100 re", "the fully in-box fill is preserved verbatim");
        scrubbed.Should().NotContain("500 500", "the fully off-box fill is physically removed");
        scrubbed.Should().NotContain("600", "no off-box coordinates remain");
        scrubbed.Should().NotContain("450", "the crossing fill is clipped to the crop boundary");
        scrubbed.Should().Contain("400", "the crossing fill gains a vertex on the crop edge");
    }

    [Fact]
    public void Scrub_Text_DropsOffBoxGlyphsKeepsInBox()
    {
        byte[] content = Encoding.Latin1.GetBytes(
            "BT\n/F1 20 Tf\n50 500 Td\n(ABCDEFGHIJKLMNOP) Tj\nET\n");
        string resources = "<</Font<</F1 5 0 R>>>>";
        string font = "<</Type/Font/Subtype/Type1/BaseFont/Helvetica/FirstChar 32/LastChar 126/Widths["
            + AllSixHundred() + "]>>";

        string scrubbed = ScrubAndDecode(
            BuildPdf(content, resources, Encoding.Latin1.GetBytes(font)), new RectangleF(0, 0, 150, 842));

        scrubbed.Should().Contain("/F1 20 Tf", "the font name keeps its leading solidus");
        scrubbed.Should().Contain("ABCDEFGH", "in-box glyphs are retained");
        scrubbed.Should().NotContain("IJKLMNOP", "off-box glyphs are removed");
    }

    [Fact]
    public void Scrub_Image_DropsOutsideAndCropsCrossing()
    {
        byte[] image = BuildHalfRedHalfGreen(4, 4);
        byte[] content = Encoding.Latin1.GetBytes(
            "q 200 0 0 200 100 100 cm /Im0 Do Q\nq 60 0 0 60 320 600 cm /Im0 Do Q\n");
        string resources = "<</XObject<</Im0 5 0 R>>>>";
        byte[] imageObj = Concat(
            Encoding.Latin1.GetBytes(
                "<</Type/XObject/Subtype/Image/Width 4/Height 4/ColorSpace/DeviceRGB/BitsPerComponent 8/Length "
                + image.Length + ">>\nstream\n"),
            image,
            Encoding.Latin1.GetBytes("\nendstream"));

        string scrubbed = ScrubAndDecode(
            BuildPdf(content, resources, imageObj), new RectangleF(0, 0, 200, 400));

        scrubbed.Should().Contain("ScrubIm0 Do", "the crossing image is cropped and re-embedded");
        scrubbed.Should().NotContain("/Im0 Do", "the original image draw is replaced or dropped");
    }

    [Fact]
    public void Scrub_ClipOnly_PreservesOriginalContentUnderHardClip()
    {
        byte[] content = Encoding.Latin1.GetBytes("1 0 0 rg\n500 500 100 100 re f\n");
        string scrubbed = ScrubAndDecode(
            BuildPdf(content, "<<>>"),
            new RectangleF(0, 0, 400, 400),
            PageCropMode.ClipOnly);

        scrubbed.Should().Contain("re W n", "ClipOnly wraps content in a hard clip");
        scrubbed.Should().Contain("500 500", "ClipOnly keeps the original off-box bytes (hidden, not removed)");
    }

    [Fact]
    public void PageCrop_TwoArgConstructor_DefaultsToClipOnly()
    {
        PageCrop crop = new PageCrop(0, new RectangleF(0, 0, 10, 10));
        crop.Mode.Should().Be(PageCropMode.ClipOnly, "the two-argument constructor preserves C1 behaviour");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string ScrubAndDecode(byte[] pdf, RectangleF cropBox, PageCropMode mode = PageCropMode.Scrub)
    {
        using PdfDocument source = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: true);
        using MemoryStream output = new MemoryStream();
        PageCrop[] crops = { new PageCrop(0, cropBox, mode) };
        PageCropper.Crop(output, source, crops);

        using PdfDocument result = PdfDocument.Open(new MemoryStream(output.ToArray()), leaveOpen: true);
        PdfPage page = result.Pages[0];
        PdfPrimitive contents = result.Objects.Resolve(page.Dictionary.GetAs<PdfPrimitive>(PdfName.Contents)!);
        FilterPipeline pipeline = FilterRegistry.CreateDefaultPipeline();
        StringBuilder text = new StringBuilder();

        if (contents is PdfArray array)
        {
            foreach (PdfPrimitive item in array)
            {
                AppendStream(result.Objects.Resolve(item) as PdfStream, pipeline, text);
            }
        }
        else
        {
            AppendStream(contents as PdfStream, pipeline, text);
        }

        return text.ToString();
    }

    private static void AppendStream(PdfStream? stream, FilterPipeline pipeline, StringBuilder text)
    {
        if (stream is null)
        {
            return;
        }

        byte[] decoded = stream.Filter is PdfName filter
            ? pipeline.Decode(FilterRegistry.ResolveAlias(filter.Value), stream.RawBytes, null)
            : stream.RawBytes;
        text.Append(Encoding.Latin1.GetString(decoded)).Append('\n');
    }

    private static byte[] BuildPdf(byte[] contentStream, string resources, params byte[][] extraObjects)
    {
        List<byte[]> bodies = new List<byte[]>
        {
            Encoding.Latin1.GetBytes("<</Type/Catalog/Pages 2 0 R>>"),
            Encoding.Latin1.GetBytes("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Encoding.Latin1.GetBytes(
                "<</Type/Page/Parent 2 0 R/MediaBox[0 0 600 842]/Resources" + resources + "/Contents 4 0 R>>"),
            Concat(
                Encoding.Latin1.GetBytes("<</Length " + contentStream.Length + ">>\nstream\n"),
                contentStream,
                Encoding.Latin1.GetBytes("\nendstream")),
        };

        foreach (byte[] extra in extraObjects)
        {
            bodies.Add(extra);
        }

        return Assemble(bodies);
    }

    private static byte[] Assemble(List<byte[]> bodies)
    {
        using MemoryStream ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.Latin1.GetBytes(s));

        Write("%PDF-1.7\n");
        List<long> offsets = new List<long>();
        for (int i = 0; i < bodies.Count; i++)
        {
            offsets.Add(ms.Length);
            Write((i + 1) + " 0 obj ");
            ms.Write(bodies[i]);
            Write(" endobj\n");
        }

        long xref = ms.Length;
        Write("xref\n0 " + (bodies.Count + 1) + "\n0000000000 65535 f \n");
        foreach (long off in offsets)
        {
            Write(off.ToString("D10") + " 00000 n \n");
        }

        Write("trailer <</Size " + (bodies.Count + 1) + "/Root 1 0 R>>\nstartxref\n" + xref + "\n%%EOF");
        return ms.ToArray();
    }

    private static byte[] BuildHalfRedHalfGreen(int width, int height)
    {
        byte[] pixels = new byte[width * height * 3];
        int p = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (x < width / 2)
                {
                    pixels[p++] = 255;
                    pixels[p++] = 0;
                    pixels[p++] = 0;
                }
                else
                {
                    pixels[p++] = 0;
                    pixels[p++] = 255;
                    pixels[p++] = 0;
                }
            }
        }

        return pixels;
    }

    private static string AllSixHundred()
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < 95; i++)
        {
            sb.Append(i == 0 ? "600" : " 600");
        }

        return sb.ToString();
    }

    private static byte[] Concat(params byte[][] parts)
    {
        using MemoryStream ms = new MemoryStream();
        foreach (byte[] part in parts)
        {
            ms.Write(part);
        }

        return ms.ToArray();
    }
}
