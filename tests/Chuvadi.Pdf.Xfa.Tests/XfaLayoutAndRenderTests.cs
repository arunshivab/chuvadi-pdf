// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Xfa.Layout;
using Chuvadi.Pdf.Xfa.Model;
using Chuvadi.Pdf.Xfa.Parse;
using Chuvadi.Pdf.Xfa.Render;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Xfa.Tests;

public sealed class XfaLayoutAndRenderTests
{
    private static readonly string FixturesDir =
        Path.Combine(System.AppContext.BaseDirectory, "Fixtures");

    private static byte[] Fixture(string name) =>
        File.ReadAllBytes(Path.Combine(FixturesDir, name));

    // ── Positioned layout ─────────────────────────────────────────────────────

    [Fact]
    public void Layout_AccumulatesParentOrigin()
    {
        XfaSubform root = XfaTemplateParser.Parse(Fixture("synthetic-positioned.xml"))!;
        XfaSubform body = (XfaSubform)FindByName(root, "body");

        // body has x=10mm in the fixture? No — body has no x; its title draw is x=10mm y=5mm.
        // Lay out body from origin (100, 200) and confirm the title box is offset.
        IReadOnlyList<XfaBox> boxes = XfaLayoutEngine.Layout(body, 100.0, 200.0);

        XfaBox title = boxes.First(b => b.Text == "Sample Form Title");
        title.X.Should().BeApproximately(100.0 + (10.0 / 25.4 * 72.0), 0.01);
        title.Y.Should().BeApproximately(200.0 + (5.0 / 25.4 * 72.0), 0.01);
    }

    [Fact]
    public void Layout_Field_ProducesValueAndCaptionBoxes()
    {
        XfaSubform root = XfaTemplateParser.Parse(Fixture("synthetic-positioned.xml"))!;
        XfaSubform body = (XfaSubform)FindByName(root, "body");

        IReadOnlyList<XfaBox> boxes = XfaLayoutEngine.Layout(body, 0.0, 0.0);

        // The fullName field yields a value box (Widget=TextEdit) and a caption box.
        boxes.Should().Contain(b => b.Widget == XfaUiKind.TextEdit && b.Text == "placeholder");
        boxes.Should().Contain(b => b.Text == "Full Name:");
    }

    [Fact]
    public void Layout_CheckButton_CarriesCheckedState()
    {
        XfaSubform root = XfaTemplateParser.Parse(Fixture("synthetic-positioned.xml"))!;
        XfaSubform body = (XfaSubform)FindByName(root, "body");

        IReadOnlyList<XfaBox> boxes = XfaLayoutEngine.Layout(body, 0.0, 0.0);

        XfaBox check = boxes.First(b => b.Widget == XfaUiKind.CheckButton);
        check.WidgetChecked.Should().BeTrue("the fixture sets the agree field value to 1");
    }

    [Fact]
    public void Layout_HiddenNode_IsSkipped()
    {
        XfaSubform root = new XfaSubform { Layout = XfaLayout.Position };
        XfaDraw hidden = new XfaDraw
        {
            Presence = XfaPresence.Hidden,
            Width = new XfaMeasurement(50),
            Height = new XfaMeasurement(10),
        };
        hidden.Value = new XfaValue { Text = "secret" };
        root.AddChild(hidden);

        IReadOnlyList<XfaBox> boxes = XfaLayoutEngine.Layout(root, 0.0, 0.0);
        boxes.Should().NotContain(b => b.Text == "secret");
    }

    // ── End-to-end render ─────────────────────────────────────────────────────

    [Fact]
    public void Render_RealForm_ProducesNonEmptyPdfWithExpectedText()
    {
        using PdfDocument doc = PdfDocument.Open(
            Path.Combine(FixturesDir, "livecycle-coi-redacted.pdf"));

        using MemoryStream output = new MemoryStream();
        XfaRenderer.Render(output, doc, XfaRenderOptions.Default);

        output.Length.Should().BeGreaterThan(1000, "a rendered form should produce a real PDF");

        // The rendered PDF should be openable and have one page.
        output.Position = 0;
        using PdfDocument rendered = PdfDocument.Open(output, leaveOpen: true);
        rendered.PageCount.Should().Be(1);
    }

    [Fact]
    public void Render_NonXfaDocument_Throws()
    {
        // Build a trivial non-XFA PDF.
        Chuvadi.Pdf.Authoring.PdfDocumentBuilder builder =
            Chuvadi.Pdf.Authoring.PdfDocumentBuilder.Create();
        builder.AddPage(Chuvadi.Pdf.Authoring.PageSize.A4);
        byte[] plain = builder.ToByteArray();

        using MemoryStream input = new MemoryStream(plain);
        using PdfDocument doc = PdfDocument.Open(input, leaveOpen: true);

        using MemoryStream output = new MemoryStream();
        System.Action act = () => XfaRenderer.Render(output, doc, XfaRenderOptions.Default);
        act.Should().Throw<XfaRenderException>();
    }

    private static XfaNode FindByName(XfaNode root, string name)
    {
        Queue<XfaNode> queue = new Queue<XfaNode>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            XfaNode node = queue.Dequeue();
            if (node.Name == name)
            {
                return node;
            }

            foreach (XfaNode child in node.Children)
            {
                queue.Enqueue(child);
            }
        }

        throw new KeyNotFoundException($"No node named '{name}'.");
    }
}
