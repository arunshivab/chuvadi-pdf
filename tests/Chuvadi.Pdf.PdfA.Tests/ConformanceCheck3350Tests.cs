// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ISO 19005-1 §6.1.10, §6.1.11, §6.1.13, §6.2.3.3, §6.4, §6.5.2;
//        ISO 19005-2 §6.1.7.2, §6.2.4.3, §6.3.3, §6.8, §6.9
// PHASE: 3.35.0 (PDA-01) — what the writer cannot fix fails closed: the
//        result lists it and nothing is written. The inputs follow the
//        Checker's PDF/A probes I05–I14 (D-394), rebuilt here. D-403:
//        transparency and colour count only where a page uses them (run 3:
//        safedocs-0295, govdocs1-0063), and a PDF/A-2b annotation without an
//        appearance fails (probe I08).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.PdfA.Tests;

public sealed class ConformanceCheck3350Tests
{
    private const string Text = "BT /F1 18 Tf 72 700 Td (PDF/A probe) Tj ET ";

    [Theory]
    [InlineData(PdfAConformance.PdfA1B, false)]
    [InlineData(PdfAConformance.PdfA2B, true)]
    public void ConstantOpacity_IsOnlyAllowedInPartTwo(PdfAConformance level, bool allowed)
    {
        byte[] pdf = Page("/GS1 gs 1 0 0 rg 72 500 200 100 re f " + Text, extraResources: "/ExtGState << /GS1 << /Type /ExtGState /ca 0.5 /CA 0.5 >> >>");

        Expect(pdf, level, allowed, "opacity");
    }

    [Theory]
    [InlineData(PdfAConformance.PdfA1B, false)]
    [InlineData(PdfAConformance.PdfA2B, true)]
    public void BlendMode_IsOnlyAllowedInPartTwo(PdfAConformance level, bool allowed)
    {
        byte[] pdf = Page("/GS1 gs 1 0 0 rg 72 500 200 100 re f " + Text, extraResources: "/ExtGState << /GS1 << /Type /ExtGState /BM /Multiply >> >>");

        Expect(pdf, level, allowed, "blend mode");
    }

    [Fact]
    public void NormalBlendMode_IsAllowedInPartOne()
    {
        byte[] pdf = Page("/GS1 gs " + Text, extraResources: "/ExtGState << /GS1 << /Type /ExtGState /BM /Normal /CA 1 /ca 1.0 /SMask /None >> >>");

        Expect(pdf, PdfAConformance.PdfA1B, true, null);
    }

    [Fact]
    public void ImageSoftMask_FailsPartOne()
    {
        byte[] pdf = Page(
            Text + "q 10 0 0 10 72 500 cm /Im1 Do Q",
            extraResources: "/XObject << /Im1 6 0 R >>",
            extraObjects: new[]
            {
                Stream("/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8 /SMask 7 0 R", "ÿ\u0000\u0000"),
                Stream("/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8", "\u0080"),
            });

        Expect(pdf, PdfAConformance.PdfA1B, false, "soft mask");
    }

    [Fact]
    public void TransparencyGroup_FailsPartOne()
    {
        byte[] pdf = Page(Text, pageExtra: "/Group << /S /Transparency /CS /DeviceRGB >>");

        Expect(pdf, PdfAConformance.PdfA1B, false, "transparency group");
    }

    [Theory]
    [InlineData(PdfAConformance.PdfA1B)]
    [InlineData(PdfAConformance.PdfA2B)]
    public void CmykFillOperator_FailsWithAnRgbIntent(PdfAConformance level)
    {
        byte[] pdf = Page("0 1 1 0 k 72 500 200 100 re f " + Text);

        Expect(pdf, level, false, "DeviceCMYK");
    }

    [Fact]
    public void CmykColourSpaceResource_Fails()
    {
        byte[] pdf = Page("/CS0 cs 0 1 1 0 scn 72 500 200 100 re f " + Text, extraResources: "/ColorSpace << /CS0 [/Indexed /DeviceCMYK 0 <00000000>] >>");

        Expect(pdf, PdfAConformance.PdfA2B, false, "DeviceCMYK");
    }

    [Fact]
    public void CmykInlineImage_Fails()
    {
        byte[] pdf = Page(Text + "q 10 0 0 10 72 500 cm BI /W 1 /H 1 /CS /CMYK /BPC 8 ID \u0000ÿÿ\u0000 EI Q");

        Expect(pdf, PdfAConformance.PdfA2B, false, "DeviceCMYK");
    }

    [Fact]
    public void LzwInlineImage_Fails()
    {
        byte[] pdf = Page(Text + "q 10 0 0 10 72 500 cm BI /W 1 /H 1 /CS /G /BPC 8 /F /LZW ID \u0080\u000b`P EI Q");

        Expect(pdf, PdfAConformance.PdfA2B, false, "LZW");
    }

    [Fact]
    public void RgbAndGrayContent_WithInlineImage_Passes()
    {
        byte[] pdf = Page("1 0 0 rg 0.5 g 0 0 1 RG " + Text + "q 10 0 0 10 72 500 cm BI /W 1 /H 1 /CS /RGB /BPC 8 ID k K EI Q");

        Expect(pdf, PdfAConformance.PdfA2B, true, null);
    }

    [Fact]
    public void CmykWithDefaultCmyk_IsAllowed()
    {
        byte[] pdf = Page(
            "0 1 1 0 k 72 500 200 100 re f " + Text,
            extraResources: "/ColorSpace << /DefaultCMYK [/ICCBased 6 0 R] >>",
            extraObjects: new[] { Stream("/N 4", "not a real profile") });

        PdfAResult result = Write(pdf, PdfAConformance.PdfA2B, out _);

        result.Violations.Should().NotContain(v => v.Contains("DeviceCMYK", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(PdfAConformance.PdfA1B, "optional content")]
    [InlineData(PdfAConformance.PdfA2B, "/Name")]
    public void UnnamedOptionalContent_Fails(PdfAConformance level, string expected)
    {
        byte[] pdf = Page(
            "/OC /L1 BDC " + Text + "EMC",
            extraResources: "/Properties << /L1 6 0 R >>",
            catalogExtra: "/OCProperties << /OCGs [6 0 R] /D << /Order [6 0 R] >> >>",
            extraObjects: new[] { "<< /Type /OCG /Name (Layer 1) >>" });

        Expect(pdf, level, false, expected);
    }

    [Fact]
    public void NamedOptionalContent_PassesPartTwo()
    {
        byte[] pdf = Page(
            "/OC /L1 BDC " + Text + "EMC",
            extraResources: "/Properties << /L1 6 0 R >>",
            catalogExtra: "/OCProperties << /OCGs [6 0 R] /D << /Name (Default) /Order [6 0 R] >> >>",
            extraObjects: new[] { "<< /Type /OCG /Name (Layer 1) >>" });

        Expect(pdf, PdfAConformance.PdfA2B, true, null);
    }

    [Theory]
    [InlineData(PdfAConformance.PdfA1B)]
    [InlineData(PdfAConformance.PdfA2B)]
    public void EmbeddedFile_Fails(PdfAConformance level)
    {
        byte[] pdf = Page(
            Text,
            catalogExtra: "/Names << /EmbeddedFiles << /Names [(a.txt) << /Type /Filespec /F (a.txt) /UF (a.txt) /EF << /F 6 0 R >> >>] >> >>",
            extraObjects: new[] { Stream("/Type /EmbeddedFile", "hello") });

        Expect(pdf, level, false, "embedded file");
    }

    [Fact]
    public void FileAttachmentAnnotation_FailsPartOne()
    {
        byte[] pdf = Page(Text, pageExtra: "/Annots [<< /Type /Annot /Subtype /FileAttachment /Rect [72 600 90 620] /F 4 /FS << /Type /Filespec /F (a.txt) >> >>]");

        Expect(pdf, PdfAConformance.PdfA1B, false, "file attachment");
    }

    [Theory]
    [InlineData(PdfAConformance.PdfA1B)]
    [InlineData(PdfAConformance.PdfA2B)]
    public void LzwStream_Fails(PdfAConformance level)
    {
        byte[] pdf = Page(
            Text,
            extraResources: "/XObject << /Im1 6 0 R >>",
            extraObjects: new[] { Stream("/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /LZWDecode", "\u0080\u000b`P") });

        Expect(pdf, level, false, "LZW");
    }

    [Fact]
    public void UnusedTransparentGraphicsState_IsAllowedInPartOne()
    {
        byte[] pdf = Page(Text, extraResources: "/ExtGState << /GS0 << /Type /ExtGState /ca 0.498 /CA 0.498 /BM /Multiply >> >>");

        Expect(pdf, PdfAConformance.PdfA1B, true, null);
    }

    [Fact]
    public void UnusedCmykColourSpace_IsAllowed()
    {
        byte[] pdf = Page("/CS2 cs /P0 scn " + Text, extraResources: "/ColorSpace << /CS2 [/Pattern /DeviceRGB] /CS3 [/Pattern /DeviceCMYK] >>");

        Expect(pdf, PdfAConformance.PdfA2B, true, null);
    }

    [Fact]
    public void UnusedCmykImage_IsAllowed()
    {
        byte[] pdf = Page(
            Text,
            extraResources: "/XObject << /Im1 6 0 R >>",
            extraObjects: new[] { Stream("/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceCMYK /BitsPerComponent 8", "\u0000\u00ff\u00ff\u0000") });

        Expect(pdf, PdfAConformance.PdfA2B, true, null);
    }

    [Fact]
    public void CmykInsideADrawnForm_Fails()
    {
        string inner = "0 1 1 0 k 0 0 10 10 re f";
        byte[] pdf = Page(
            Text + "/Fm1 Do",
            extraResources: "/XObject << /Fm1 6 0 R >>",
            extraObjects: new[] { Stream("/Type /XObject /Subtype /Form /BBox [0 0 10 10]", inner) });

        Expect(pdf, PdfAConformance.PdfA2B, false, "DeviceCMYK");
    }

    [Fact]
    public void CmykShading_Fails()
    {
        byte[] pdf = Page(
            Text + "/Sh1 sh",
            extraResources: "/Shading << /Sh1 << /ShadingType 2 /ColorSpace /DeviceCMYK /Coords [0 0 100 0] /Function << /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0 1 1 0] /N 1 >> >> >>");

        Expect(pdf, PdfAConformance.PdfA2B, false, "DeviceCMYK");
    }

    [Fact]
    public void TransparentGraphicsStateInAnAppearance_FailsPartOne()
    {
        string appearance = "/GS1 gs 1 0 0 rg 0 0 18 18 re f";
        byte[] pdf = Page(
            Text,
            pageExtra: "/Annots [<< /Type /Annot /Subtype /Square /Rect [72 600 90 618] /F 4 /AP << /N 6 0 R >> >>]",
            extraObjects: new[] { Stream("/Type /XObject /Subtype /Form /BBox [0 0 18 18] /Resources << /ExtGState << /GS1 << /ca 0.5 >> >> >>", appearance) });

        Expect(pdf, PdfAConformance.PdfA1B, false, "opacity");
    }

    [Fact]
    public void TextAnnotationWithoutAppearance_FailsPartTwo()
    {
        byte[] pdf = Page(Text, pageExtra: "/Annots [<< /Type /Annot /Subtype /Text /Rect [72 600 90 618] /F 4 /Contents (note) >>]");

        Expect(pdf, PdfAConformance.PdfA2B, false, "appearance");
    }

    [Fact]
    public void TextAnnotationWithAnAppearance_PassesPartTwo()
    {
        byte[] pdf = Page(
            Text,
            pageExtra: "/Annots [<< /Type /Annot /Subtype /Text /Rect [72 600 90 618] /F 4 /Contents (note) /AP << /N 6 0 R >> >>]",
            extraObjects: new[] { Stream("/Type /XObject /Subtype /Form /BBox [0 0 18 18]", "1 1 0 rg 0 0 18 18 re f") });

        Expect(pdf, PdfAConformance.PdfA2B, true, null);
    }

    [Theory]
    [InlineData("/Subtype /Link /Rect [72 600 200 620] /Border [0 0 0] /F 4 /A << /S /URI /URI (https://example.org) >>")]
    [InlineData("/Subtype /Text /Rect [72 600 72 600] /F 4")]
    public void LinkOrZeroSizeAnnotationWithoutAppearance_PassesPartTwo(string annotation)
    {
        byte[] pdf = Page(Text, pageExtra: "/Annots [<< /Type /Annot " + annotation + " >>]");

        Expect(pdf, PdfAConformance.PdfA2B, true, null);
    }

    [Fact]
    public void SeveralProblems_AreAllListed()
    {
        byte[] pdf = Page(
            "/GS1 gs 0 1 1 0 k 72 500 200 100 re f " + Text,
            extraResources: "/ExtGState << /GS1 << /ca 0.5 /BM /Screen >> >>");

        PdfAResult result = Write(pdf, PdfAConformance.PdfA1B, out long written);

        result.Succeeded.Should().BeFalse();
        written.Should().Be(0);
        result.Violations.Should().HaveCount(3);
    }

    private static void Expect(byte[] pdf, PdfAConformance level, bool allowed, string? expected)
    {
        PdfAResult result = Write(pdf, level, out long written);
        if (allowed)
        {
            result.Succeeded.Should().BeTrue(string.Join("; ", result.Violations));
            written.Should().BeGreaterThan(0);
            return;
        }

        result.Succeeded.Should().BeFalse();
        written.Should().Be(0, "nothing is written when the file would not conform");
        result.Violations.Should().ContainSingle(v => v.Contains(expected!, StringComparison.OrdinalIgnoreCase));
    }

    private static PdfAResult Write(byte[] pdf, PdfAConformance level, out long written)
    {
        using PdfDocument document = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        using MemoryStream output = new MemoryStream();
        PdfAResult result = PdfAWriter.Write(output, document, new PdfAOptions { Conformance = level });
        written = output.Length;
        return result;
    }

    private static string Stream(string dict, string data) =>
        "<< " + dict + " /Length " + Encoding.Latin1.GetByteCount(data).ToString(CultureInfo.InvariantCulture) + " >>\nstream\n" + data + "\nendstream";

    // Objects 1–5 are the catalog, page tree, page, font and content; extra
    // objects follow from 6.
    private static byte[] Page(
        string content,
        string extraResources = "",
        string pageExtra = "",
        string catalogExtra = "",
        IReadOnlyList<string>? extraObjects = null)
    {
        List<string> objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R " + catalogExtra + " >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> " + extraResources + " >> /Contents 5 0 R " + pageExtra + " >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            Stream(string.Empty, content),
        };
        if (extraObjects is not null)
        {
            objects.AddRange(extraObjects);
        }

        StringBuilder sb = new StringBuilder("%PDF-1.7\n%âãÏÓ\n");
        List<int> offsets = new List<int>();
        for (int i = 0; i < objects.Count; i++)
        {
            offsets.Add(Encoding.Latin1.GetByteCount(sb.ToString()));
            sb.Append(CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        int xref = Encoding.Latin1.GetByteCount(sb.ToString());
        sb.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (int offset in offsets)
        {
            sb.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        sb.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(sb.ToString());
    }
}
