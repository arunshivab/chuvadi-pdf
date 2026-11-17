// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.3.4 (Tz), §9.3.6 (Tr), §11.6.4.4 (constant alpha)
// PHASE: 3.35.0 (D-384 T1-T3) — horizontal scaling, text stroke style and
//        text clips in the display list; shading opacity. The probes are
//        Print's, measured on 3.34.0.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Chuvadi.Pdf.Documents;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.DisplayList.Tests;

public sealed class PrintTextFields3350Tests
{
    // T1: Tz is in the advances and in HorizontalScaling, not in Transform.
    [Fact]
    public void HorizontalScaling_IsAPropertyNotPartOfTheTransform()
    {
        using PdfDocument doc = Open("BT /F1 12 Tf 10 150 Td 150 Tz 2 Ts 1 Tc 3 Tw (Hi y) Tj ET");

        TextOp text = DisplayListBuilder.Build(doc, 0).OfType<TextOp>().First();

        text.HorizontalScaling.Should().BeApproximately(1.5, 1e-12);
        text.Glyphs[0].Advance.Should().BeApproximately(((722 * 12 / 1000.0) + 1) * 1.5, 1e-9);
        text.Transform.A.Should().BeApproximately(1, 1e-12);
        text.Transform.D.Should().BeApproximately(1, 1e-12);
        text.Transform.E.Should().BeApproximately(10, 1e-9);
        text.Transform.F.Should().BeApproximately(152, 1e-9);
    }

    [Fact]
    public void HorizontalScaling_UnderATextMatrix_LeavesTheTransform()
    {
        using PdfDocument doc = Open("BT /F1 12 Tf 2 0 0.3 1 50 80 Tm 150 Tz (Hi y) Tj ET");

        TextOp text = DisplayListBuilder.Build(doc, 0).OfType<TextOp>().First();

        text.Transform.A.Should().BeApproximately(2, 1e-12);
        text.HorizontalScaling.Should().BeApproximately(1.5, 1e-12);
    }

    [Fact]
    public void HorizontalScaling_DefaultsToOne()
    {
        using PdfDocument doc = Open("BT /F1 12 Tf 10 150 Td (Hi) Tj ET");

        DisplayListBuilder.Build(doc, 0).OfType<TextOp>().First().HorizontalScaling.Should().Be(1.0);
    }

    // T2: stroking modes carry the page-space line attributes.
    [Fact]
    public void StrokeMode_CarriesTheStrokeStyle()
    {
        using PdfDocument doc = Open("q 0 0 1 RG 3 w BT 1 Tr /F1 20 Tf 10 120 Td (O) Tj ET Q");

        TextOp text = DisplayListBuilder.Build(doc, 0).OfType<TextOp>().First();

        text.RenderingMode.Should().Be(TextRenderingMode.Stroke);
        text.Stroke.Should().NotBeNull();
        text.Stroke!.LineWidth.Should().BeApproximately(3, 1e-12);
    }

    [Fact]
    public void StrokeWidth_IsScaledToPageSpace()
    {
        using PdfDocument doc = Open("q 2 0 0 2 0 0 cm 3 w BT 2 Tr /F1 20 Tf 10 60 Td (O) Tj ET Q");

        DisplayListBuilder.Build(doc, 0).OfType<TextOp>().First().Stroke!.LineWidth.Should().BeApproximately(6, 1e-9);
    }

    [Fact]
    public void FillMode_HasNoStrokeStyle()
    {
        using PdfDocument doc = Open("3 w BT /F1 20 Tf 10 120 Td (O) Tj ET");

        DisplayListBuilder.Build(doc, 0).OfType<TextOp>().First().Stroke.Should().BeNull();
    }

    // T3: clip modes emit a ClipOp after the text object, carried in later
    // ops' Clips until the matching Q.
    [Fact]
    public void ClipMode_EmitsAClipAndCarriesIt()
    {
        using PdfDocument doc = Open("BT 7 Tr /F1 30 Tf 10 40 Td (W) Tj ET 0 g 0 0 300 200 re f");

        List<RenderOp> ops = DisplayListBuilder.Build(doc, 0).ToList();
        ClipOp clip = ops.OfType<ClipOp>().Single();
        PathOp fill = ops.OfType<PathOp>().Single();

        clip.FillRule.Should().Be(FillRule.NonZero);
        clip.Geometry.IsEmpty.Should().BeFalse();
        fill.Clips.Should().ContainSingle().Which.Should().BeSameAs(clip.Geometry);
        ops.IndexOf(clip).Should().BeLessThan(ops.IndexOf(fill));

        // The outline lies where the W is drawn: from x 10, on the baseline 40.
        double minX = clip.Geometry.Segments.Where(s => s.Command != PathCommand.Close).Min(s => s.X1);
        double minY = clip.Geometry.Segments.Where(s => s.Command != PathCommand.Close).Min(s => s.Y1);
        double maxY = clip.Geometry.Segments.Where(s => s.Command != PathCommand.Close).Max(s => s.Y1);
        minX.Should().BeInRange(9, 12);
        minY.Should().BeInRange(39, 41);
        maxY.Should().BeInRange(58, 62);
    }

    [Fact]
    public void TextClip_EndsAtTheMatchingRestore()
    {
        using PdfDocument doc = Open("q BT 7 Tr /F1 30 Tf 10 40 Td (W) Tj ET Q 0 g 0 0 300 200 re f");

        DisplayListBuilder.Build(doc, 0).OfType<PathOp>().Single().Clips.Should().BeEmpty();
    }

    [Fact]
    public void FillMode_AddsNoClip()
    {
        using PdfDocument doc = Open("BT /F1 30 Tf 10 40 Td (W) Tj ET 0 g 0 0 300 200 re f");

        DisplayListBuilder.Build(doc, 0).OfType<ClipOp>().Should().BeEmpty();
    }

    // sh honours the constant fill alpha.
    [Fact]
    public void Shading_CarriesTheFillAlpha()
    {
        using PdfDocument doc = Open("/H gs /S sh");

        DisplayListBuilder.Build(doc, 0).OfType<ShadingOp>().Single().Opacity.Should().BeApproximately(0.5, 1e-12);
    }

    [Fact]
    public void Shading_IsOpaqueByDefault()
    {
        using PdfDocument doc = Open("/S sh");

        DisplayListBuilder.Build(doc, 0).OfType<ShadingOp>().Single().Opacity.Should().Be(1.0);
    }

    private static PdfDocument Open(string content)
    {
        List<string> bodies = new List<string>
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 300 200]/Resources<</Font<</F1 5 0 R>>/ExtGState<</H<</ca 0.5>>>>"
                + "/Shading<</S<</ShadingType 2/ColorSpace/DeviceRGB/Coords[0 0 300 0]/Function<</FunctionType 2/Domain[0 1]/C0[1 0 0]/C1[0 0 1]/N 1>>>>>>>>/Contents 4 0 R>>",
            "<</Length " + content.Length.ToString(CultureInfo.InvariantCulture) + ">>\nstream\n" + content + "\nendstream",
            "<</Type/Font/Subtype/Type1/BaseFont/Helvetica/Encoding/WinAnsiEncoding>>",
        };

        StringBuilder pdf = new StringBuilder("%PDF-1.7\n");
        List<int> offsets = new List<int>();
        for (int i = 0; i < bodies.Count; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append(" 0 obj\n").Append(bodies[i]).Append("\nendobj\n");
        }

        int xref = pdf.Length;
        pdf.Append("xref\n0 6\n0000000000 65535 f \n");
        foreach (int off in offsets)
        {
            pdf.Append(off.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        pdf.Append("trailer <</Size 6/Root 1 0 R>>\nstartxref\n").Append(xref.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF");
        return PdfDocument.Open(new MemoryStream(Encoding.Latin1.GetBytes(pdf.ToString())), leaveOpen: false);
    }
}
