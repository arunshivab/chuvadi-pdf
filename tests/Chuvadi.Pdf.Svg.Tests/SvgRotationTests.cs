// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// Tests for PDF-07: SVG display rotation and opt-in page /Rotate.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Svg.Tests;

public sealed class SvgRotationTests
{
    [Fact]
    public void Rotation90_SwapsCanvasAndRotatesContent()
    {
        string svg = Render("/MediaBox[0 0 200 100]", new SvgExportOptions { Rotation = 90 });

        svg.Should().Contain("width=\"100pt\" height=\"200pt\" viewBox=\"0 0 100 200\"");
        svg.Should().Contain("matrix(0 1 -1 0 100 0)", "page space maps into the turned canvas");
    }

    [Fact]
    public void Rotation180_KeepsCanvasSize()
    {
        string svg = Render("/MediaBox[0 0 200 100]", new SvgExportOptions { Rotation = 180 });

        svg.Should().Contain("width=\"200pt\" height=\"100pt\"");
        svg.Should().Contain("matrix(-1 0 0 -1 200 100)");
    }

    [Fact]
    public void NegativeRotation_NormalisesTo270()
    {
        new SvgExportOptions { Rotation = -90 }.Rotation.Should().Be(270);
        string svg = Render("/MediaBox[0 0 200 100]", new SvgExportOptions { Rotation = -90 });
        svg.Should().Contain("matrix(0 -1 1 0 0 200)");
    }

    [Fact]
    public void Rotation_NotAMultipleOf90_Throws()
    {
        Action act = () => _ = new SvgExportOptions { Rotation = 30 };
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void DefaultOutput_IsUnchanged_NoRotationGroup()
    {
        string svg = Render("/MediaBox[0 0 200 100]/Rotate 90", new SvgExportOptions());

        svg.Should().Contain("width=\"200pt\" height=\"100pt\"", "page /Rotate is not applied by default");
        svg.Should().NotContain("matrix(0 1 -1 0");
    }

    [Fact]
    public void ApplyPageRotation_HonoursPageRotate_AndAddsViewRotation()
    {
        string upright = Render(
            "/MediaBox[0 0 200 100]/Rotate 90", new SvgExportOptions { ApplyPageRotation = true });
        upright.Should().Contain("width=\"100pt\" height=\"200pt\"");
        upright.Should().Contain("matrix(0 1 -1 0 100 0)");

        string turned = Render(
            "/MediaBox[0 0 200 100]/Rotate 90",
            new SvgExportOptions { ApplyPageRotation = true, Rotation = 90 });
        turned.Should().Contain("width=\"200pt\" height=\"100pt\"", "90 + 90 = 180");
        turned.Should().Contain("matrix(-1 0 0 -1 200 100)");
    }

    private static string Render(string pageEntries, SvgExportOptions options)
    {
        byte[] content = Encoding.Latin1.GetBytes("1 0 0 rg 0 0 50 50 re f");
        List<byte[]> bodies = new List<byte[]>
        {
            Encoding.Latin1.GetBytes("<</Type/Catalog/Pages 2 0 R>>"),
            Encoding.Latin1.GetBytes("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Encoding.Latin1.GetBytes("<</Type/Page/Parent 2 0 R" + pageEntries + "/Contents 4 0 R>>"),
            Concat(Encoding.Latin1.GetBytes("<</Length " + content.Length + ">>\nstream\n"), content, Encoding.Latin1.GetBytes("\nendstream")),
        };

        using MemoryStream ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.Latin1.GetBytes(s));
        Write("%PDF-1.7\n");
        List<long> offsets = new List<long>();
        for (int i = 0; i < bodies.Count; i++)
        {
            offsets.Add(ms.Length);
            Write((i + 1) + " 0 obj\n");
            ms.Write(bodies[i]);
            Write("\nendobj\n");
        }

        long xref = ms.Length;
        Write("xref\n0 " + (bodies.Count + 1) + "\n0000000000 65535 f \n");
        foreach (long off in offsets)
        {
            Write(off.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        }

        Write("trailer <</Size " + (bodies.Count + 1) + "/Root 1 0 R>>\nstartxref\n" + xref + "\n%%EOF");
        ms.Position = 0;

        using PdfDocument document = PdfDocument.Open(ms, leaveOpen: true);
        return new SvgRenderer(options).RenderPage(document, 0);
    }

    private static byte[] Concat(params byte[][] parts)
    {
        using MemoryStream ms = new MemoryStream();
        foreach (byte[] p in parts)
        {
            ms.Write(p);
        }

        return ms.ToArray();
    }
}
