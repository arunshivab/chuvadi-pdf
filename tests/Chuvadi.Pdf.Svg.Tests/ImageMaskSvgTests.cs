// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.9.6.2 — Stencil masking (/ImageMask true)
// PHASE: 3.18 — stencil masks in the SVG pipeline.
//
// Before 3.18 the SVG renderer skipped /ImageMask true stencils (their 1-bpc
// data failed the 8-bpc guard). They now emit as fill-coloured RGBA PNGs via
// the /SMask machinery.

using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Svg.Tests;

public sealed class ImageMaskSvgTests
{
    [Fact]
    public void RenderPage_StencilMask_EmitsAnImage()
    {
        byte[] pdf = BuildStencilPdf();
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);

        string svg = new SvgRenderer().RenderPage(doc, 0);

        svg.Should().Contain("data:image/png;base64,",
            "the stencil renders as an RGBA PNG painted with the fill colour");
        svg.Should().Contain("<image",
            "the stencil is placed like any other image");
    }

    private static byte[] BuildStencilPdf()
    {
        // 2×2 stencil, row byte 0b0100_0000: left column paints, right is
        // masked. Painted with fill colour red.
        byte[] stencilBits = { 0x40, 0x40 };
        byte[] content = Encoding.Latin1.GetBytes(
            "1 0 0 rg q 100 0 0 100 100 600 cm /Im0 Do Q\n");

        List<byte[]> bodies = new List<byte[]>
        {
            Encoding.Latin1.GetBytes("<</Type/Catalog/Pages 2 0 R>>"),
            Encoding.Latin1.GetBytes("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Encoding.Latin1.GetBytes(
                "<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]"
                + "/Resources<</XObject<</Im0 5 0 R>>>>/Contents 4 0 R>>"),
            Concat(
                Encoding.Latin1.GetBytes("<</Length " + content.Length + ">>\nstream\n"),
                content,
                Encoding.Latin1.GetBytes("\nendstream")),
            Concat(
                Encoding.Latin1.GetBytes(
                    "<</Type/XObject/Subtype/Image/Width 2/Height 2/BitsPerComponent 1"
                    + "/ImageMask true/Length " + stencilBits.Length + ">>\nstream\n"),
                stencilBits,
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
