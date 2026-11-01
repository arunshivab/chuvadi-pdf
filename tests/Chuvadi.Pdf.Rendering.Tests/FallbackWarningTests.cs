// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// 3.27 (D-62, D-63): fallbacks are reported through PdfOpenOptions.OnWarning.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.Tests;

public sealed class FallbackWarningTests
{
    private const string BadShadingResources = "/Shading<</S0 5 0 R>>";
    private const string BadShading = "<</ShadingType 2/ColorSpace/DeviceRGB/Coords[0 0 /Wrong 0]/Function<</FunctionType 2/Domain[0 1]/C0[0 0 0]/C1[1 1 1]/N 1>>>>";

    [Fact]
    public void ASkippedShading_IsReportedOnceWithPageAndReason()
    {
        List<PdfWarning> warnings = new List<PdfWarning>();
        using PdfDocument doc = OpenWith(BadShadingResources, "/S0 sh /S0 sh", BadShading, "null", warnings.Add);

        Render(doc);

        PdfWarning w = warnings.Should().ContainSingle().Subject;
        w.Kind.Should().Be(PdfWarningKind.ShadingSkipped);
        w.PageNumber.Should().Be(1);
        w.Subject.Should().Be("shading 5 0 R");
        w.Reason.Should().Contain("not a number").And.EndWith("The paint is skipped.");
    }

    [Fact]
    public void AnUnreadableFontProgram_IsReportedAsSubstituted()
    {
        List<PdfWarning> warnings = new List<PdfWarning>();
        using PdfDocument doc = OpenWith(
            "/Font<</F1 5 0 R>>",
            "BT /F1 12 Tf 10 10 Td (Hi) Tj ET",
            "<</Type/Font/Subtype/TrueType/BaseFont/Broken/FirstChar 32/LastChar 126/Widths[]/FontDescriptor 6 0 R>>",
            "<</Type/FontDescriptor/FontName/Broken/Flags 32/FontBBox[0 0 1000 1000]/ItalicAngle 0/Ascent 800/Descent -200/CapHeight 700/StemV 80/FontFile2 7 0 R>>",
            warnings.Add,
            "<</Length 12>>\nstream\nnot-a-font!!\nendstream");

        Render(doc);

        PdfWarning w = warnings.Should().ContainSingle().Subject;
        w.Kind.Should().Be(PdfWarningKind.FontSubstituted);
        w.PageNumber.Should().Be(1);
        w.Subject.Should().Be("font /F1");
        w.Reason.Should().Contain("substitute");
    }

    [Fact]
    public void ADamagedGlyph_IsReportedOncePerFont()
    {
        List<PdfWarning> warnings = new List<PdfWarning>();
        string path = System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "damaged_cff_glyph.pdf");
        using PdfDocument doc = PdfDocument.Open(path, new PdfOpenOptions { OnWarning = warnings.Add });

        Render(doc);

        warnings.Should().NotBeEmpty();
        warnings.Should().OnlyContain(w => w.Kind == PdfWarningKind.GlyphSkipped && w.PageNumber == 1);
        warnings.Select(w => w.Subject).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void WithoutAReceiver_TheFallbackIsUnchanged()
    {
        using PdfDocument doc = OpenWith(BadShadingResources, "/S0 sh", BadShading, "null", null);

        PixelBuffer pixels = Render(doc);

        doc.OnWarning.Should().BeNull();
        pixels.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TheReceiverIsKeptOnTheDocument_ForThePathOverloadToo()
    {
        string file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "chuvadi-warn-" + Guid.NewGuid().ToString("N") + ".pdf");
        File.WriteAllBytes(file, Encoding.Latin1.GetBytes(Assemble(Bodies(BadShadingResources, "/S0 sh", BadShading, "null", null))));
        try
        {
            List<PdfWarning> warnings = new List<PdfWarning>();
            using PdfDocument doc = PdfDocument.Open(file, new PdfOpenOptions { OnWarning = warnings.Add });

            Render(doc);

            warnings.Should().ContainSingle(w => w.Kind == PdfWarningKind.ShadingSkipped);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void ToString_GivesOneLine()
    {
        new PdfWarning(PdfWarningKind.ShadingSkipped, 3, "shading 5 0 R", "Bad entry. The paint is skipped.").ToString()
            .Should().Be("page 3: ShadingSkipped: shading 5 0 R: Bad entry. The paint is skipped.");
    }

    private static PixelBuffer Render(PdfDocument doc) =>
        new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 36 }).Rasterize(doc.Pages[0], CancellationToken.None);

    private static PdfDocument OpenWith(string resources, string content, string object5, string object6, Action<PdfWarning>? onWarning, string? object7 = null) =>
        PdfDocument.Open(
            new MemoryStream(Encoding.Latin1.GetBytes(Assemble(Bodies(resources, content, object5, object6, object7)))),
            new PdfOpenOptions { OnWarning = onWarning });

    private static string[] Bodies(string resources, string content, string object5, string object6, string? object7)
    {
        List<string> bodies = new List<string>
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]/Resources<<" + resources + ">>/Contents 4 0 R>>",
            "<</Length " + content.Length + ">>\nstream\n" + content + "\nendstream",
            object5,
            object6,
        };
        if (object7 is not null)
        {
            bodies.Add(object7);
        }

        return bodies.ToArray();
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
}
