// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// VERIFIER: adversarial tests for the 3.18 Scrub-mode crop changes.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Filters;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Primitives;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

public sealed class ScrubAdversarialVerifierTests
{
    private static readonly RectangleF CropBox = new RectangleF(0, 0, 400, 400);

    // Inline-image data containing a whitespace-delimited "EI" inside the
    // binary: the scanner ends the image early, and the data tail is
    // re-tokenised as operators — leaking bytes of an off-box image that
    // Scrub promises to remove, and corrupting the stream.
    [Fact]
    public void Scrub_FalseEiInsideBinaryData_LeaksImageTail()
    {
        // 2x2 RGB = 12 data bytes: "AB EI ZLEAKZ" (false EI at bytes 3..4).
        byte[] content = Encoding.Latin1.GetBytes(
            "q 60 0 0 60 480 480 cm BI /W 2 /H 2 /CS /RGB /BPC 8 ID AB EI ZLEAKZ EI Q\n"
            + "50 50 100 100 re f\n");

        string scrubbed = ScrubContent(content);

        Assert.False(scrubbed.Contains("ZLEAKZ", StringComparison.Ordinal),
            "bytes of an off-box inline image leaked past a false EI match "
            + "inside the binary data");
    }

    // Two annotations sharing one appearance stream: dropping the off-box one
    // must not omit the appearance object still referenced by the kept one.
    [Fact]
    public void Scrub_SharedAppearanceStream_SurvivesWhenOneAnnotationIsKept()
    {
        byte[] content = Encoding.Latin1.GetBytes("50 50 100 100 re f\n");
        List<byte[]> bodies = new List<byte[]>
        {
            Encoding.Latin1.GetBytes("<</Type/Catalog/Pages 2 0 R>>"),
            Encoding.Latin1.GetBytes("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Encoding.Latin1.GetBytes(
                "<</Type/Page/Parent 2 0 R/MediaBox[0 0 600 842]/Resources<<>>"
                + "/Contents 4 0 R/Annots[5 0 R 6 0 R]>>"),
            Concat(
                Encoding.Latin1.GetBytes("<</Length " + content.Length + ">>\nstream\n"),
                content,
                Encoding.Latin1.GetBytes("\nendstream")),
            Encoding.Latin1.GetBytes(
                "<</Type/Annot/Subtype/Text/Rect[500 500 550 550]"
                + "/Contents(OFFBOX)/AP<</N 7 0 R>>>>"),
            Encoding.Latin1.GetBytes(
                "<</Type/Annot/Subtype/Text/Rect[50 50 100 100]"
                + "/Contents(INBOX)/AP<</N 7 0 R>>>>"),
            Encoding.Latin1.GetBytes(
                "<</Type/XObject/Subtype/Form/BBox[0 0 50 50]/Length 20>>\nstream\n"
                + "%SHAREDAPPEARANCE xx"
                + "\nendstream"),
        };

        byte[] pdf = Assemble(bodies);
        using PdfDocument source = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: true);
        using MemoryStream output = new MemoryStream();
        PageCrop[] crops = { new PageCrop(0, CropBox, PageCropMode.Scrub) };
        PageCropper.Crop(output, source, crops);
        string whole = Encoding.Latin1.GetString(output.ToArray());

        Assert.True(whole.Contains("SHAREDAPPEARANCE", StringComparison.Ordinal),
            "the appearance stream shared with a KEPT annotation was omitted "
            + "from the output, leaving the kept annotation's /AP dangling");
    }

    // A clip set and fully restored by Q must not suppress a later sh.
    [Fact]
    public void Scrub_ShAfterRestoredClip_IsKept()
    {
        byte[] content = Encoding.Latin1.GetBytes(
            "q 480 480 50 50 re W n Q\n/Sh0 sh\n");

        string scrubbed = ScrubContent(content);

        Assert.True(scrubbed.Contains("/Sh0 sh", StringComparison.Ordinal),
            "a shading painted after the off-box clip was restored by Q was "
            + "wrongly dropped");
    }

    // ── Helpers (mirrors PageCropScrubGapTests) ────────────────────────────

    private static string ScrubContent(byte[] content)
    {
        byte[] pdf = BuildPdf(content, "<<>>");
        using PdfDocument source = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: true);
        using MemoryStream output = new MemoryStream();
        PageCrop[] crops = { new PageCrop(0, CropBox, PageCropMode.Scrub) };
        PageCropper.Crop(output, source, crops);

        using PdfDocument result = PdfDocument.Open(
            new MemoryStream(output.ToArray()), leaveOpen: true);
        PdfPage page = result.Pages[0];
        PdfPrimitive contents = result.Objects.Resolve(
            page.Dictionary.GetAs<PdfPrimitive>(PdfName.Contents)!);
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

    private static void AppendStream(
        PdfStream? stream, FilterPipeline pipeline, StringBuilder text)
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

    private static byte[] BuildPdf(byte[] contentStream, string resources)
    {
        List<byte[]> bodies = new List<byte[]>
        {
            Encoding.Latin1.GetBytes("<</Type/Catalog/Pages 2 0 R>>"),
            Encoding.Latin1.GetBytes("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Encoding.Latin1.GetBytes(
                "<</Type/Page/Parent 2 0 R/MediaBox[0 0 600 842]/Resources"
                + resources + "/Contents 4 0 R>>"),
            Concat(
                Encoding.Latin1.GetBytes("<</Length " + contentStream.Length + ">>\nstream\n"),
                contentStream,
                Encoding.Latin1.GetBytes("\nendstream")),
        };

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
            Write(off.ToString("D10", System.Globalization.CultureInfo.InvariantCulture)
                + " 00000 n \n");
        }

        Write("trailer <</Size " + (bodies.Count + 1)
            + "/Root 1 0 R>>\nstartxref\n" + xref + "\n%%EOF");
        return ms.ToArray();
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
