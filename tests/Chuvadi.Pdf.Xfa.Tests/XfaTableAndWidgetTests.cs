// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chuvadi.Pdf.Xfa.Layout;
using Chuvadi.Pdf.Xfa.Model;
using Chuvadi.Pdf.Xfa.Parse;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Xfa.Tests;

public sealed class XfaTableAndWidgetTests
{
    private static readonly string FixturesDir =
        Path.Combine(System.AppContext.BaseDirectory, "Fixtures");

    private static XfaSubform ParseFixture(string name) =>
        XfaTemplateParser.Parse(
            File.ReadAllBytes(Path.Combine(FixturesDir, name)))!;

    // ── Table / row layout ────────────────────────────────────────────────────

    [Fact]
    public void Parse_ColumnWidths_AreCaptured()
    {
        XfaSubform root = ParseFixture("synthetic-table.xml");
        XfaSubform grid = (XfaSubform)FindByName(root, "grid");

        grid.Layout.Should().Be(XfaLayout.Table);
        grid.ColumnWidths.Should().NotBeNull();
        grid.ColumnWidths!.Should().HaveCount(3);
        grid.ColumnWidths![0].Points.Should().BeApproximately(100, 0.01);
        grid.ColumnWidths![1].Points.Should().BeApproximately(150, 0.01);
        grid.ColumnWidths![2].Points.Should().BeApproximately(100, 0.01);
    }

    [Fact]
    public void Layout_Table_PlacesCellsByColumnWidthAndStacksRows()
    {
        XfaSubform root = ParseFixture("synthetic-table.xml");
        XfaSubform grid = (XfaSubform)FindByName(root, "grid");

        IReadOnlyList<XfaBox> boxes = XfaLayoutEngine.Layout(grid, 0, 0);

        // Row 1 columns start at 0 / 100 / 250 (per columnWidths).
        FindText(boxes, "alpha").X.Should().BeApproximately(0, 0.01);
        FindText(boxes, "beta").X.Should().BeApproximately(100, 0.01);
        FindText(boxes, "gamma").X.Should().BeApproximately(250, 0.01);
        FindText(boxes, "alpha").Y.Should().BeApproximately(0, 0.01);

        // Row 2 sits below row 1's 20pt height, same columns.
        FindText(boxes, "delta").Y.Should().BeApproximately(20, 0.01);
        FindText(boxes, "epsilon").X.Should().BeApproximately(100, 0.01);
        FindText(boxes, "zeta").X.Should().BeApproximately(250, 0.01);
        FindText(boxes, "zeta").Y.Should().BeApproximately(20, 0.01);
    }

    // ── Widgets ───────────────────────────────────────────────────────────────

    [Fact]
    public void Layout_ExclGroupMembers_AreRadioButtons()
    {
        IReadOnlyList<XfaBox> boxes =
            XfaLayoutEngine.Layout(ParseFixture("synthetic-widgets.xml"), 0, 0);

        List<XfaBox> radios = boxes
            .Where(b => b.Widget == XfaUiKind.CheckButton)
            .OrderBy(b => b.Y)
            .ToList();

        radios.Should().HaveCount(2);
        radios.Should().OnlyContain(b => b.WidgetRound, "exclGroup members render round");
        radios[0].WidgetChecked.Should().BeTrue("optA carries value 1");
        radios[1].WidgetChecked.Should().BeFalse("optB carries value 0");
    }

    [Fact]
    public void Layout_PasswordEdit_MasksValue()
    {
        IReadOnlyList<XfaBox> boxes =
            XfaLayoutEngine.Layout(ParseFixture("synthetic-widgets.xml"), 0, 0);

        XfaBox pwd = boxes.First(b => b.Widget == XfaUiKind.PasswordEdit);
        pwd.Text.Should().Be("******", "the six-character secret masks to six asterisks");
    }

    [Fact]
    public void Layout_ImageEdit_DecodesBase64Payload()
    {
        IReadOnlyList<XfaBox> boxes =
            XfaLayoutEngine.Layout(ParseFixture("synthetic-widgets.xml"), 0, 0);

        List<XfaBox> images = boxes
            .Where(b => b.Widget == XfaUiKind.ImageEdit)
            .OrderBy(b => b.X)
            .ToList();

        images.Should().HaveCount(2);
        images[0].ImageBytes.Should().NotBeNull("the fixture embeds a base64 PNG");
        images[0].ImageBytes!.Value.Length.Should().BeGreaterThan(0);
        images[1].ImageBytes.Should().BeNull("the second image field carries no payload");
    }

    [Fact]
    public void Layout_BarcodeAndChoiceList_RenderValueText()
    {
        IReadOnlyList<XfaBox> boxes =
            XfaLayoutEngine.Layout(ParseFixture("synthetic-widgets.xml"), 0, 0);

        boxes.First(b => b.Widget == XfaUiKind.Barcode).Text.Should().Be("1234567890");
        boxes.First(b => b.Widget == XfaUiKind.ChoiceList).Text.Should().Be("India");
    }

    [Fact]
    public void Layout_Signature_IsEmittedAsWidgetBox()
    {
        IReadOnlyList<XfaBox> boxes =
            XfaLayoutEngine.Layout(ParseFixture("synthetic-widgets.xml"), 0, 0);

        XfaBox sig = boxes.First(b => b.Widget == XfaUiKind.Signature);
        sig.Width.Should().BeApproximately(180, 0.01);
        sig.Height.Should().BeApproximately(40, 0.01);
    }

    private static XfaBox FindText(IReadOnlyList<XfaBox> boxes, string text) =>
        boxes.First(b => b.Text == text);

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
