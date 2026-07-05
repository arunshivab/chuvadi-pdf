// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Xfa.Layout;
using Chuvadi.Pdf.Xfa.Model;
using Chuvadi.Pdf.Xfa.Parse;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Xfa.Tests;

public sealed class XfaMergeAndFlowTests
{
    private static readonly string FixturesDir =
        Path.Combine(System.AppContext.BaseDirectory, "Fixtures");

    // ── Datasets merge ────────────────────────────────────────────────────────
    //
    // XfaDataField has an internal constructor (owned by Chuvadi.Pdf.Documents),
    // so these tests drive the merge through the real form's DataFields rather
    // than fabricating XfaDataField instances. This keeps Documents unchanged.

    [Fact]
    public void Merge_RealForm_FillsBoundFieldFromDatasets()
    {
        using PdfDocument doc = PdfDocument.Open(
            Path.Combine(FixturesDir, "livecycle-coi-redacted.pdf"));

        XfaSubform root = XfaTemplateParser.Parse(doc.Xfa!.Template!.Xml)!;

        XfaField? bound = FindBoundField(root, "COMPANY_NAME");
        bound.Should().NotBeNull("the template has a field bound to COMPANY_NAME");

        XfaDataMerge.Apply(root, doc.Xfa.DataFields);

        bound!.Value.Should().NotBeNull();
        bound.Value!.Text.Should().Contain("EXAMPLE COMPANY");
    }

    [Fact]
    public void Merge_RealForm_PopulatesKnownFields()
    {
        using PdfDocument doc = PdfDocument.Open(
            Path.Combine(FixturesDir, "livecycle-coi-redacted.pdf"));

        XfaSubform root = XfaTemplateParser.Parse(doc.Xfa!.Template!.Xml)!;
        XfaDataMerge.Apply(root, doc.Xfa.DataFields);

        List<string> values = new List<string>();
        CollectFieldValues(root, values);

        values.Should().Contain(v => v.Contains("EXAMPLE COMPANY", System.StringComparison.Ordinal));
        values.Should().Contain(v => v.Contains("limited by shares", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Merge_NullArguments_Throw()
    {
        XfaSubform root = new XfaSubform { Name = "root" };
        System.Action nullData = () => XfaDataMerge.Apply(root, null!);
        nullData.Should().Throw<System.ArgumentNullException>();
    }

    // ── Flowed layout ─────────────────────────────────────────────────────────

    [Fact]
    public void Layout_TopToBottom_StacksChildrenByHeight()
    {
        XfaSubform root = new XfaSubform { Name = "root", Layout = XfaLayout.TopToBottom };
        for (int i = 0; i < 3; i++)
        {
            XfaDraw draw = new XfaDraw
            {
                Width = new XfaMeasurement(100),
                Height = new XfaMeasurement(20),
            };
            draw.Value = new XfaValue
            {
                Text = "line" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
            root.AddChild(draw);
        }

        IReadOnlyList<XfaBox> boxes = XfaLayoutEngine.Layout(root, 0, 0);
        List<XfaBox> texts = boxes.Where(b => b.Text is not null).OrderBy(b => b.Y).ToList();

        texts.Should().HaveCount(3);
        texts[0].Y.Should().BeApproximately(0, 0.01);
        texts[1].Y.Should().BeApproximately(20, 0.01);
        texts[2].Y.Should().BeApproximately(40, 0.01);
    }

    [Fact]
    public void Layout_LeftRightTopToBottom_WrapsOnWidth()
    {
        XfaSubform root = new XfaSubform
        {
            Name = "root",
            Layout = XfaLayout.LeftRightTopToBottom,
            Width = new XfaMeasurement(150),
        };
        for (int i = 0; i < 3; i++)
        {
            XfaDraw draw = new XfaDraw
            {
                Width = new XfaMeasurement(60),
                Height = new XfaMeasurement(20),
            };
            draw.Value = new XfaValue
            {
                Text = "cell" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
            root.AddChild(draw);
        }

        IReadOnlyList<XfaBox> boxes = XfaLayoutEngine.Layout(root, 0, 0);
        List<XfaBox> texts = boxes.Where(b => b.Text is not null).ToList();

        XfaBox cell0 = texts.First(b => b.Text == "cell0");
        XfaBox cell1 = texts.First(b => b.Text == "cell1");
        XfaBox cell2 = texts.First(b => b.Text == "cell2");

        cell0.X.Should().BeApproximately(0, 0.01);
        cell0.Y.Should().BeApproximately(0, 0.01);
        cell1.X.Should().BeApproximately(60, 0.01);
        cell1.Y.Should().BeApproximately(0, 0.01);
        cell2.X.Should().BeApproximately(0, 0.01);
        cell2.Y.Should().BeApproximately(20, 0.01);
    }

    [Fact]
    public void Parse_BindRef_IsCaptured()
    {
        using PdfDocument doc = PdfDocument.Open(
            Path.Combine(FixturesDir, "livecycle-coi-redacted.pdf"));

        XfaSubform root = XfaTemplateParser.Parse(doc.Xfa!.Template!.Xml)!;

        List<string> refs = new List<string>();
        CollectDataRefs(root, refs);

        refs.Should().NotBeEmpty("the real form declares dataRef bindings");
        refs.Should().Contain(r => r.Contains("COMPANY_NAME", System.StringComparison.Ordinal));
    }

    private static XfaField? FindBoundField(XfaNode node, string refContains)
    {
        if (node is XfaField field
            && field.DataRef is { } dataRef
            && dataRef.Contains(refContains, System.StringComparison.Ordinal))
        {
            return field;
        }

        foreach (XfaNode child in node.Children)
        {
            XfaField? found = FindBoundField(child, refContains);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static void CollectFieldValues(XfaNode node, List<string> values)
    {
        if (node is XfaField { Value.Text: { Length: > 0 } text })
        {
            values.Add(text);
        }

        foreach (XfaNode child in node.Children)
        {
            CollectFieldValues(child, values);
        }
    }

    private static void CollectDataRefs(XfaNode node, List<string> refs)
    {
        if (node is XfaField { DataRef: { Length: > 0 } dataRef })
        {
            refs.Add(dataRef);
        }

        foreach (XfaNode child in node.Children)
        {
            CollectDataRefs(child, refs);
        }
    }
}
