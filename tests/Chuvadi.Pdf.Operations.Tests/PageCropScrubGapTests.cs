// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.5 (annotations); §8.9.7 (inline images);
//        §8.7.4.3 (sh shading operator)
// Tests for the Scrub-mode gaps closed in 3.18: off-box annotations are
// removed (with their appearance streams), inline images are consumed as a
// unit and dropped when fully off-box, and `sh` under a fully off-box clip is
// dropped as dead bytes.

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

public sealed class PageCropScrubGapTests
{
    private static readonly RectangleF CropBox = new RectangleF(0, 0, 400, 400);

    // ── Annotations ───────────────────────────────────────────────────────────

    [Fact]
    public void Scrub_OffBoxAnnotation_IsRemovedWithItsAppearance()
    {
        // PdfWriter serializes strings as hex, so leak checks cover both forms.
        byte[] pdf = BuildAnnotatedPdf();
        string whole = CropToString(pdf, PageCropMode.Scrub);

        whole.Should().NotContain("OFFBOXSECRET",
            "an annotation entirely outside the crop box must not leak its content");
        whole.Should().NotContain(Hex("OFFBOXSECRET"),
            "the content must not survive in hex-string form either");
        whole.Should().NotContain("APPAYLOAD",
            "the dropped annotation's appearance stream must be omitted too");
        whole.Should().Contain(Hex("INBOXNOTE"),
            "an annotation inside the crop box is preserved");
    }

    [Fact]
    public void ClipOnly_LeavesAnnotationsUntouched()
    {
        byte[] pdf = BuildAnnotatedPdf();
        string whole = CropToString(pdf, PageCropMode.ClipOnly);

        whole.Should().Contain(Hex("OFFBOXSECRET"),
            "ClipOnly is the lossless mode: bytes are hidden, never removed");
        whole.Should().Contain(Hex("INBOXNOTE"));
    }

    // ── Inline images ─────────────────────────────────────────────────────────

    [Fact]
    public void Scrub_InlineImages_DropsOffBoxKeepsInBox()
    {
        // Two 2×2 RGB inline images (12 raw bytes each, ASCII payloads so the
        // assertion can find them): one placed off-box, one in-box.
        byte[] content = Encoding.Latin1.GetBytes(
            "q 60 0 0 60 480 480 cm BI /W 2 /H 2 /CS /RGB /BPC 8 ID ZZOFFBOXDATA EI Q\n"
            + "q 60 0 0 60 100 100 cm BI /W 2 /H 2 /CS /RGB /BPC 8 ID QQINBOXDATA! EI Q\n");

        string scrubbed = ScrubContent(content);

        scrubbed.Should().NotContain("ZZOFFBOXDATA",
            "an inline image painting entirely outside the crop box is removed");
        scrubbed.Should().Contain("QQINBOXDATA!",
            "an inline image inside the crop box is preserved verbatim");
        scrubbed.Should().Contain("EI", "the kept inline image stays intact");
    }

    [Fact]
    public void Scrub_InlineImage_DoesNotCorruptFollowingContent()
    {
        // The binary data after ID is not tokenizable; the scrubber must
        // consume it as a unit so the operators after EI survive untouched.
        byte[] content = Encoding.Latin1.GetBytes(
            "q 60 0 0 60 480 480 cm BI /W 2 /H 2 /CS /RGB /BPC 8 ID ZZOFFBOXDATA EI Q\n"
            + "50 50 100 100 re f\n");

        string scrubbed = ScrubContent(content);

        scrubbed.Should().Contain("50 50 100 100 re",
            "content after an inline image is rewritten normally");
    }

    // ── sh (shading) ──────────────────────────────────────────────────────────

    [Fact]
    public void Scrub_Shading_DropsUnderOffBoxClipKeepsUnderInBoxClip()
    {
        byte[] content = Encoding.Latin1.GetBytes(
            "q 480 480 50 50 re W n /Sh0 sh Q\n"
            + "q 50 50 100 100 re W n /Sh0 sh Q\n");

        string scrubbed = ScrubContent(content);

        CountOccurrences(scrubbed, "/Sh0 sh").Should().Be(1,
            "the shading under the fully off-box clip is dead bytes and is dropped;"
            + " the one under the in-box clip is kept");
    }

    [Fact]
    public void Scrub_Shading_WithoutClipIsKept()
    {
        // No clip: the shading paints the whole page; the hard-clip backstop
        // confines it to the crop box, so the operator must survive.
        byte[] content = Encoding.Latin1.GetBytes("/Sh0 sh\n");

        string scrubbed = ScrubContent(content);

        scrubbed.Should().Contain("/Sh0 sh");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string ScrubContent(byte[] content)
    {
        byte[] pdf = BuildPdf(content, "<<>>");
        using PdfDocument source = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: true);
        using MemoryStream output = new MemoryStream();
        PageCrop[] crops = { new PageCrop(0, CropBox, PageCropMode.Scrub) };
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

    private static string CropToString(byte[] pdf, PageCropMode mode)
    {
        using PdfDocument source = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: true);
        using MemoryStream output = new MemoryStream();
        PageCrop[] crops = { new PageCrop(0, CropBox, mode) };
        PageCropper.Crop(output, source, crops);
        return Encoding.Latin1.GetString(output.ToArray());
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

    private static string Hex(string ascii)
    {
        StringBuilder sb = new StringBuilder(ascii.Length * 2);
        foreach (char c in ascii)
        {
            sb.Append(((byte)c).ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    private static int CountOccurrences(string text, string token)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(token, index, System.StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }

    private static byte[] BuildAnnotatedPdf()
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
                + "/Contents(OFFBOXSECRET)/AP<</N 7 0 R>>>>"),
            Encoding.Latin1.GetBytes(
                "<</Type/Annot/Subtype/Text/Rect[50 50 100 100]/Contents(INBOXNOTE)>>"),
            Encoding.Latin1.GetBytes(
                "<</Type/XObject/Subtype/Form/BBox[0 0 50 50]/Length 20>>\nstream\n"
                + "%APPAYLOAD xxxxxxxx\n"
                + "\nendstream"),
        };

        return Assemble(bodies);
    }

    private static byte[] BuildPdf(byte[] contentStream, string resources)
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
