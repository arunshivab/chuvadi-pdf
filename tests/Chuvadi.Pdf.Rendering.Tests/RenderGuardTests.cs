// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.24 — embedding API: cancellation (A3) and limits (A4) on
// PageRasterizer.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Rendering.DisplayList;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.Tests;

public sealed class RenderGuardTests
{
    [Fact]
    public async Task Rasterize_StopsSoonAfterCancellation()
    {
        // 200,000 full-page fills: seconds of work at 150 DPI.
        using PdfDocument doc = Open(Repeat("0 0 400 400 re f\n", 200_000));
        PageRasterizer rasterizer = new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 150 });
        using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        Stopwatch watch = Stopwatch.StartNew();
        Task render = Task.Run(() => rasterizer.Rasterize(doc.Pages[0], cts.Token));
        Task finished = await Task.WhenAny(render, Task.Delay(TimeSpan.FromSeconds(20)));
        watch.Stop();

        finished.Should().BeSameAs(render, "the render must stop");
        Func<Task> outcome = () => render;
        await outcome.Should().ThrowAsync<OperationCanceledException>();
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1.5));
    }

    [Fact]
    public void RasterizeToPngAndRenderRegion_HonourAnAlreadyCancelledToken()
    {
        using PdfDocument doc = Open("0 0 10 10 re f");
        PageRasterizer rasterizer = new PageRasterizer(doc.Objects);
        using CancellationTokenSource cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => rasterizer.RasterizeToPng(doc.Pages[0], cts.Token));
        Assert.ThrowsAny<OperationCanceledException>(
            () => rasterizer.RenderRegion(doc.Pages[0], new Rect(0, 0, 50, 50), 72, cts.Token));
    }

    [Fact]
    public void PageComplexityLimit_StopsTheRender()
    {
        using PdfDocument doc = Open(Repeat("0 0 1 1 re f\n", 5_000), new PdfLimits { MaxContentOperators = 1_000 });

        PdfLimitExceededException ex = Assert.Throws<PdfLimitExceededException>(
            () => new PageRasterizer(doc.Objects).Rasterize(doc.Pages[0]));

        ex.LimitName.Should().Be(nameof(PdfLimits.MaxContentOperators));
    }

    [Fact]
    public void OutputPixelLimit_IsCheckedBeforeAllocating()
    {
        using PdfDocument doc = Open("0 0 10 10 re f", new PdfLimits { MaxOutputPixels = 1_000_000 });

        PdfLimitExceededException ex = Assert.Throws<PdfLimitExceededException>(
            () => new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 600 }).Rasterize(doc.Pages[0]));

        ex.LimitName.Should().Be(nameof(PdfLimits.MaxOutputPixels));
    }

    [Fact]
    public void DecompressionBomb_IsStoppedByTheDecodedSizeLimit()
    {
        // A content stream that inflates to 64 MiB of spaces.
        using PdfDocument doc = Open(new string(' ', 64 << 20), new PdfLimits { MaxDecodedStreamBytes = 8 << 20 }, flate: true);

        PdfLimitExceededException ex = Assert.Throws<PdfLimitExceededException>(
            () => new PageRasterizer(doc.Objects).Rasterize(doc.Pages[0]));

        ex.LimitName.Should().Be(nameof(PdfLimits.MaxDecodedStreamBytes));
        ex.Actual.Should().BeLessThan(16 << 20, "decoding stops soon after the limit");
    }

    [Fact]
    public void MemoryLimit_CountsTheOutputBuffer()
    {
        using PdfDocument doc = Open("0 0 10 10 re f", new PdfLimits { MaxMemoryBytes = 100_000 });

        Assert.Throws<PdfLimitExceededException>(
            () => new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72 }).Rasterize(doc.Pages[0]))
            .LimitName.Should().Be(nameof(PdfLimits.MaxMemoryBytes));
    }

    [Fact]
    public void WithoutLimits_RenderingIsUnchanged()
    {
        using PdfDocument doc = Open("1 0 0 rg 0 0 400 400 re f");

        PixelBuffer pixels = new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72 }).Rasterize(doc.Pages[0], CancellationToken.None);

        (byte b, byte _, byte r, byte _) = pixels.GetPixelBgra(10, 10);
        r.Should().Be(255);
        b.Should().Be(0);
    }

    [Fact]
    public void ADamagedFontProgram_DoesNotFailThePage()
    {
        // A CFF font whose charstrings were damaged (three bytes changed):
        // the bad glyphs draw nothing, the page still renders. Before 3.24 the
        // Type 2 interpreter's error failed the whole render.
        string path = System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "damaged_cff_glyph.pdf");
        using PdfDocument doc = PdfDocument.Open(path);

        PixelBuffer pixels = new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 36 }).Rasterize(doc.Pages[0], CancellationToken.None);

        pixels.Width.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData("/Shading<</S0 5 0 R>>", "/S0 sh", "<</ShadingType 2/ColorSpace/DeviceRGB/Coords[0 0 100 0]/Function 6 0 R>>",
        "<</FunctionType 3/Domain[0 1]/Functions[6 0 R]/Bounds[]/Encode[0 1]>>")]
    [InlineData("/ColorSpace<</C0 5 0 R>>", "/C0 cs 0 sc 0 0 10 10 re f", "[/Indexed 5 0 R 1 <000000>]", "null")]
    public void SelfReferentialFunctionsAndColourSpaces_DoNotOverflowTheStack(
        string resources, string content, string object5, string object6)
    {
        // Each of these recursed until the process died before 3.24.
        ArgumentNullException.ThrowIfNull(content);
        using PdfDocument doc = OpenWith(resources, content, object5, object6);

        Exception? thrown = Record.Exception(
            () => new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 36 }).Rasterize(doc.Pages[0], CancellationToken.None));

        (thrown is null || thrown is PdfException).Should().BeTrue(thrown?.ToString());
    }

    [Fact]
    public void DeeplyNestedType4Function_DoesNotOverflowTheStack()
    {
        string program = new string('{', 100_000) + new string('}', 100_000);
        using PdfDocument doc = OpenWith(
            "/Shading<</S0 5 0 R>>", "/S0 sh",
            "<</ShadingType 2/ColorSpace/DeviceRGB/Coords[0 0 100 0]/Function 6 0 R>>",
            "<</FunctionType 4/Domain[0 1]/Range[0 1 0 1 0 1]/Length " + program.Length + ">>\nstream\n" + program + "\nendstream");

        Exception? thrown = Record.Exception(
            () => new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 36 }).Rasterize(doc.Pages[0], CancellationToken.None));

        (thrown is null || thrown is PdfException).Should().BeTrue(thrown?.ToString());
    }

    [Fact]
    public void ObjectsNestedTooDeeply_AreRefusedAtParse()
    {
        string deep = new string('[', 50_000) + new string(']', 50_000);

        // In the page dictionary itself, so reading the page parses it.
        Assert.ThrowsAny<PdfParseException>(() => OpenWith("/X " + deep, "0 0 1 1 re f", "null", "null").Pages[0].Width.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task APageThatIsItsOwnParent_DoesNotHang()
    {
        // No MediaBox anywhere, and /Parent loops: the inherited lookup must
        // stop at the cycle.
        string[] bodies =
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1/Parent 2 0 R>>",
            "<</Type/Page/Parent 3 0 R/Contents 4 0 R>>",
            "<</Length 0>>\nstream\n\nendstream",
        };
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(Encoding.Latin1.GetBytes(Assemble(bodies))), new PdfOpenOptions());

        Task work = Task.Run(() => Record.Exception(() => _ = doc.Pages[0].Width));
        Task finished = await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(10)));

        finished.Should().BeSameAs(work, "the lookup must terminate");
    }

    private static PdfDocument OpenWith(string resources, string content, string object5, string object6)
    {
        string[] bodies =
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]/Resources<<" + resources + ">>/Contents 4 0 R>>",
            "<</Length " + content.Length + ">>\nstream\n" + content + "\nendstream",
            object5,
            object6,
        };
        return PdfDocument.Open(new MemoryStream(Encoding.Latin1.GetBytes(Assemble(bodies))), new PdfOpenOptions());
    }

    private static string Assemble(string[] bodies)
    {
        StringBuilder sb = new StringBuilder("%PDF-1.7\n");
        List<int> offsets = new List<int>();
        for (int i = 0; i < bodies.Length; i++)
        {
            offsets.Add(sb.Length);
            sb.Append(i + 1).Append(" 0 obj\n").Append(bodies[i]).Append("\nendobj\n");
        }

        int xref = sb.Length;
        sb.Append("xref\n0 ").Append(bodies.Length + 1).Append("\n0000000000 65535 f \n");
        foreach (int off in offsets)
        {
            sb.Append(off.ToString("D10", System.Globalization.CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        sb.Append("trailer\n<</Size ").Append(bodies.Length + 1).Append("/Root 1 0 R>>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        return sb.ToString();
    }

    private static string Repeat(string s, int n)
    {
        StringBuilder sb = new StringBuilder(s.Length * n);
        for (int i = 0; i < n; i++)
        {
            sb.Append(s);
        }

        return sb.ToString();
    }

    private static PdfDocument Open(string content, PdfLimits? limits = null, bool flate = false)
    {
        byte[] data = Encoding.Latin1.GetBytes(content);
        string filter = string.Empty;
        if (flate)
        {
            using MemoryStream z = new MemoryStream();
            using (System.IO.Compression.ZLibStream zs = new System.IO.Compression.ZLibStream(z, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            {
                zs.Write(data);
            }

            data = z.ToArray();
            filter = "/Filter/FlateDecode";
        }

        List<byte[]> bodies = new List<byte[]>
        {
            Encoding.Latin1.GetBytes("<</Type/Catalog/Pages 2 0 R>>"),
            Encoding.Latin1.GetBytes("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Encoding.Latin1.GetBytes("<</Type/Page/Parent 2 0 R/MediaBox[0 0 400 400]/Contents 4 0 R>>"),
            Concat(Encoding.Latin1.GetBytes("<<" + filter + "/Length " + data.Length + ">>\nstream\n"), data, Encoding.Latin1.GetBytes("\nendstream")),
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
            Write(off.ToString("D10", System.Globalization.CultureInfo.InvariantCulture) + " 00000 n \n");
        }

        Write("trailer\n<</Size " + (bodies.Count + 1) + "/Root 1 0 R>>\nstartxref\n" + xref + "\n%%EOF\n");
        return PdfDocument.Open(new MemoryStream(ms.ToArray()), new PdfOpenOptions { Limits = limits });
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
