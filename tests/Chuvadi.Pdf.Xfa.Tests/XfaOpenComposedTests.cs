// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  XFA 3.3 — dynamic forms (catalog /NeedsRendering); PDF 32000-1:2008
//        §12.7.8 — XFA entry in the interactive form dictionary.
// PHASE: Chuvadi.Pdf.Xfa — dynamic XFA "just works" open facade.
//
// A dynamic XFA document's pages are placeholders ("please update your
// viewer"); the real form exists only as an XFA template. XfaRenderer
// .OpenComposed composes such documents transparently, while hybrid, static
// and non-XFA documents come back untouched.

using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Text;
using Chuvadi.Pdf.Xfa.Render;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Xfa.Tests;

public sealed class XfaOpenComposedTests
{
    private static readonly string FixturesDir =
        Path.Combine(System.AppContext.BaseDirectory, "Fixtures");

    [Fact]
    public void OpenComposed_DynamicXfa_ComposesTheTemplate()
    {
        byte[] pdf = BuildDynamicXfaPdf(
            File.ReadAllBytes(Path.Combine(FixturesDir, "synthetic-positioned.xml")));

        using MemoryStream input = new MemoryStream(pdf);
        using PdfDocument document = XfaRenderer.OpenComposed(input);

        document.XfaKind.Should().Be(XfaKind.None,
            "the returned document is the composed static render, not the shell");
        document.PageCount.Should().BeGreaterThanOrEqualTo(1);

        TextExtractor extractor = new TextExtractor(
            document.Objects, ExtractionStrategy.Layout);
        string text = extractor.ExtractText(document.Pages[0]);
        text.Should().Contain("Sample Form Title",
            "the composed page carries the template's content, not the placeholder");
    }

    [Fact]
    public void OpenComposed_HybridXfa_ReturnsOriginalDocument()
    {
        using FileStream input = File.OpenRead(
            Path.Combine(FixturesDir, "livecycle-coi-redacted.pdf"));
        using PdfDocument document = XfaRenderer.OpenComposed(input);

        document.XfaKind.Should().Be(XfaKind.Hybrid,
            "hybrid documents already carry their content in widget appearances"
            + " and must come back untouched");
    }

    [Fact]
    public void OpenComposed_NonXfa_ReturnsOriginalDocument()
    {
        Chuvadi.Pdf.Authoring.PdfDocumentBuilder builder =
            Chuvadi.Pdf.Authoring.PdfDocumentBuilder.Create();
        builder.AddPage(Chuvadi.Pdf.Authoring.PageSize.A4);
        byte[] plain = builder.ToByteArray();

        using MemoryStream input = new MemoryStream(plain);
        using PdfDocument document = XfaRenderer.OpenComposed(input);

        document.IsXfa.Should().BeFalse();
        document.PageCount.Should().Be(1);
    }

    [Fact]
    public void OpenComposed_DynamicWithoutTemplate_FallsBackToOriginal()
    {
        // /NeedsRendering true but the XFA array carries only a datasets
        // packet: composition is impossible, and the open must still succeed
        // with the placeholder document.
        byte[] pdf = BuildDynamicXfaPdf(templateXml: null);

        using MemoryStream input = new MemoryStream(pdf);
        using PdfDocument document = XfaRenderer.OpenComposed(input);

        document.XfaKind.Should().Be(XfaKind.Dynamic,
            "the fallback is the original (placeholder) document");
        document.PageCount.Should().Be(1);
    }

    // ── Fixture: a minimal dynamic-XFA shell PDF ──────────────────────────────

    private static byte[] BuildDynamicXfaPdf(byte[]? templateXml)
    {
        byte[] datasets = System.Text.Encoding.UTF8.GetBytes(
            "<xfa:datasets xmlns:xfa=\"http://www.xfa.org/schema/xfa-data/1.0/\">"
            + "<xfa:data/></xfa:datasets>");
        byte[] placeholder = System.Text.Encoding.Latin1.GetBytes(
            "BT /F0 12 Tf 50 700 Td (Please update your viewer) Tj ET\n");

        System.Collections.Generic.List<byte[]> bodies =
            new System.Collections.Generic.List<byte[]>
        {
            System.Text.Encoding.Latin1.GetBytes(
                "<</Type/Catalog/Pages 2 0 R/NeedsRendering true/AcroForm 5 0 R>>"),
            System.Text.Encoding.Latin1.GetBytes("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            System.Text.Encoding.Latin1.GetBytes(
                "<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]/Resources<<>>/Contents 4 0 R>>"),
            Concat(
                System.Text.Encoding.Latin1.GetBytes(
                    "<</Length " + placeholder.Length + ">>\nstream\n"),
                placeholder,
                System.Text.Encoding.Latin1.GetBytes("\nendstream")),
            System.Text.Encoding.Latin1.GetBytes(
                templateXml is null
                    ? "<</XFA[(datasets) 6 0 R]>>"
                    : "<</XFA[(template) 7 0 R (datasets) 6 0 R]>>"),
            Concat(
                System.Text.Encoding.Latin1.GetBytes(
                    "<</Length " + datasets.Length + ">>\nstream\n"),
                datasets,
                System.Text.Encoding.Latin1.GetBytes("\nendstream")),
        };

        if (templateXml is not null)
        {
            bodies.Add(Concat(
                System.Text.Encoding.Latin1.GetBytes(
                    "<</Length " + templateXml.Length + ">>\nstream\n"),
                templateXml,
                System.Text.Encoding.Latin1.GetBytes("\nendstream")));
        }

        return Assemble(bodies);
    }

    private static byte[] Assemble(System.Collections.Generic.List<byte[]> bodies)
    {
        using MemoryStream ms = new MemoryStream();
        void Write(string s) => ms.Write(System.Text.Encoding.Latin1.GetBytes(s));

        Write("%PDF-1.7\n");
        System.Collections.Generic.List<long> offsets =
            new System.Collections.Generic.List<long>();
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
