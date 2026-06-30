// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Xfa.Model;
using Chuvadi.Pdf.Xfa.Parse;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Xfa.Tests;

public sealed class XfaTemplateParserTests
{
    private static readonly string FixturesDir =
        Path.Combine(System.AppContext.BaseDirectory, "Fixtures");

    private static byte[] Fixture(string name) =>
        File.ReadAllBytes(Path.Combine(FixturesDir, name));

    // ── Synthetic positioned fixture ─────────────────────────────────────────

    [Fact]
    public void Parse_PositionedForm_BuildsExpectedTree()
    {
        XfaSubform? root = XfaTemplateParser.Parse(Fixture("synthetic-positioned.xml"));

        root.Should().NotBeNull();
        root!.Name.Should().Be("root");
        root.Layout.Should().Be(XfaLayout.Position);

        XfaSubform body = (XfaSubform)FindByName(root, "body");
        body.Name.Should().Be("body");
        body.Layout.Should().Be(XfaLayout.TopToBottom);
        body.Width!.Value.Points.Should().BeApproximately(200.0 / 25.4 * 72.0, 0.01);
        body.Margin.Should().NotBeNull();
        body.Margin!.Left.Points.Should().BeApproximately(2.0 / 25.4 * 72.0, 0.01);
    }

    [Fact]
    public void Parse_Draw_CarriesTextAndFont()
    {
        XfaSubform root = XfaTemplateParser.Parse(Fixture("synthetic-positioned.xml"))!;
        XfaSubform body = (XfaSubform)FindByName(root, "body");

        XfaDraw title = (XfaDraw)FindByName(body, "title");
        title.HAlign.Should().Be(XfaHAlign.Center);
        title.Value!.Text.Should().Be("Sample Form Title");
        title.Font!.Bold.Should().BeTrue();
        title.Font.Size.Should().BeApproximately(18.0, 0.01);
    }

    [Fact]
    public void Parse_Field_CapturesCaptionUiValueBorder()
    {
        XfaSubform root = XfaTemplateParser.Parse(Fixture("synthetic-positioned.xml"))!;
        XfaSubform body = (XfaSubform)FindByName(root, "body");

        XfaField name = (XfaField)FindByName(body, "fullName");
        name.Caption!.Text.Should().Be("Full Name:");
        name.Caption.Placement.Should().Be(XfaCaptionPlacement.Left);
        name.Ui!.Kind.Should().Be(XfaUiKind.TextEdit);
        name.Value!.Text.Should().Be("placeholder");
        name.HAlign.Should().Be(XfaHAlign.Left);
        name.Border!.HasEdge.Should().BeTrue();
        name.Border.EdgeColor.Should().Be("0,0,0");
    }

    [Theory]
    [InlineData("agree", XfaUiKind.CheckButton)]
    [InlineData("country", XfaUiKind.ChoiceList)]
    [InlineData("dob", XfaUiKind.DateTimeEdit)]
    [InlineData("amount", XfaUiKind.NumericEdit)]
    public void Parse_FieldUiKinds_AreRecognised(string fieldName, XfaUiKind expected)
    {
        XfaSubform root = XfaTemplateParser.Parse(Fixture("synthetic-positioned.xml"))!;
        XfaSubform body = (XfaSubform)FindByName(root, "body");

        XfaField field = (XfaField)FindByName(body, fieldName);
        field.Ui!.Kind.Should().Be(expected);
    }

    [Fact]
    public void Parse_CaptionPlacements_AreRecognised()
    {
        XfaSubform root = XfaTemplateParser.Parse(Fixture("synthetic-positioned.xml"))!;
        XfaSubform body = (XfaSubform)FindByName(root, "body");

        ((XfaField)FindByName(body, "agree")).Caption!.Placement
            .Should().Be(XfaCaptionPlacement.Right);
        ((XfaField)FindByName(body, "country")).Caption!.Placement
            .Should().Be(XfaCaptionPlacement.Top);
    }

    // ── Synthetic flowed fixture ─────────────────────────────────────────────

    [Fact]
    public void Parse_FlowedForm_HandlesNestingAndExclGroup()
    {
        XfaSubform root = XfaTemplateParser.Parse(Fixture("synthetic-flowed.xml"))!;
        root.Layout.Should().Be(XfaLayout.TopToBottom);

        XfaSubform section1 = (XfaSubform)FindByName(root, "section1");
        section1.Layout.Should().Be(XfaLayout.LeftRightTopToBottom);

        XfaExclGroup choice = (XfaExclGroup)FindByName(root, "choice");
        choice.Children.Should().HaveCount(2);
        choice.Layout.Should().Be(XfaLayout.TopToBottom);

        // Deeply nested field two subforms down.
        XfaSubform nested1 = (XfaSubform)FindByName(root, "nested1");
        XfaSubform nested2 = (XfaSubform)FindByName(nested1, "nested2");
        XfaField deep = (XfaField)FindByName(nested2, "deep");
        deep.Value!.Text.Should().Be("deep");
    }

    // ── Redacted real LiveCycle form ─────────────────────────────────────────

    [Fact]
    public void Parse_RealLiveCycleForm_ProducesExpectedCounts()
    {
        using PdfDocument doc = PdfDocument.Open(
            Path.Combine(FixturesDir, "livecycle-coi-redacted.pdf"));

        doc.IsXfa.Should().BeTrue();
        doc.Xfa.Should().NotBeNull();
        doc.Xfa!.Template.Should().NotBeNull();

        XfaSubform? root = XfaTemplateParser.Parse(doc.Xfa.Template!.Xml);
        root.Should().NotBeNull();
        root!.Name.Should().Be("data");
        root.Layout.Should().Be(XfaLayout.TopToBottom);

        (int subforms, int fields, int draws) = CountNodes(root);
        subforms.Should().Be(2);
        fields.Should().Be(27);
        draws.Should().Be(14);
    }

    private static (int Subforms, int Fields, int Draws) CountNodes(XfaNode node)
    {
        int subforms = node is XfaSubform ? 1 : 0;
        int fields = node is XfaField ? 1 : 0;
        int draws = node is XfaDraw ? 1 : 0;

        foreach (XfaNode child in node.Children)
        {
            (int s, int f, int d) = CountNodes(child);
            subforms += s;
            fields += f;
            draws += d;
        }

        return (subforms, fields, draws);
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
