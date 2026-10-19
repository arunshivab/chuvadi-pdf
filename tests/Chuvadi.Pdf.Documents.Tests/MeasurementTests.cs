// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.3.2.3 (UserUnit), §12.9 (viewports, measure
//        dictionaries, number formats)
// PHASE: 3.21 (PDF-15) — /UserUnit and /VP /Measure exposure.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Documents.Tests;

public sealed class MeasurementTests
{
    // 1 point = 1/72 in; at "1 in = 10 ft", 1 point = 10/72 ft.
    private const string FeetScale = "<</U(ft)/C 0.1388888889/D 100>>";

    [Theory]
    [InlineData("", 1.0)]
    [InlineData("/UserUnit 2.5", 2.5)]
    [InlineData("/UserUnit 10", 10.0)]
    [InlineData("/UserUnit -3", 1.0)]
    [InlineData("/UserUnit/Bad", 1.0)]
    public void UserUnit_DefaultsToOne_AndRejectsNonPositiveValues(string entry, double expected)
    {
        using PdfDocument doc = Open(entry);

        doc.Pages[0].UserUnit.Should().Be(expected);
    }

    [Fact]
    public void Viewport_IsRead_WithNameBoundsAndScale()
    {
        using PdfDocument doc = Open(Viewport("0 0 300 300", "Plan", "(1 in = 10 ft)", "[" + FeetScale + "]"));

        PdfViewport viewport = doc.Pages[0].Viewports.Should().ContainSingle().Subject;
        viewport.Name.Should().Be("Plan");
        viewport.Bounds.Width.Should().Be(300);
        viewport.Measure!.ScaleRatio.Should().Be("1 in = 10 ft");
        viewport.Measure.IsRectilinear.Should().BeTrue();
    }

    [Fact]
    public void Distance_ConvertsUserSpaceToDrawingUnits()
    {
        using PdfDocument doc = Open(Viewport("0 0 300 300", "Plan", "(1 in = 10 ft)", "[" + FeetScale + "]"));

        PdfMeasurement? d = doc.Pages[0].MeasureDistance(100, 100, 172, 100);

        d.Should().NotBeNull();
        d!.Value.Value.Should().BeApproximately(10, 1e-6);
        d.Value.Unit.Should().Be("ft");
        d.Value.Formatted.Should().Be("10.00 ft");
    }

    [Fact]
    public void UnitChain_FormatsWholeUnitsThenTheRemainder()
    {
        // 75.6 pt = 10.5 ft = 10 ft 6 in.
        using PdfDocument doc = Open(Viewport("0 0 300 300", "Plan", "(1 in = 10 ft)",
            "[<</U(ft)/C 0.1388888889/D 1>> <</U(in)/C 12/D 1>>]"));

        doc.Pages[0].MeasureDistance(0, 0, 75.6, 0)!.Value.Formatted.Should().Be("10 ft 6 in");
    }

    [Fact]
    public void FractionFormat_ShowsReducedFractions()
    {
        // 74.7 pt = 10.375 ft = 10 3/8 ft.
        using PdfDocument doc = Open(Viewport("0 0 300 300", "Plan", "(1 in = 10 ft)",
            "[<</U(ft)/C 0.1388888889/F/F/D 8>>]"));

        doc.Pages[0].MeasureDistance(0, 0, 74.7, 0)!.Value.Formatted.Should().Be("10 3/8 ft");
    }

    [Fact]
    public void LaterViewport_WinsWhereViewportsOverlap()
    {
        string first = ViewportDict("0 0 400 400", "Sheet", "(1:1)", "[<</U(pt)/C 1/D 1>>]");
        string second = ViewportDict("100 100 200 200", "Detail", "(1:10)", "[<</U(pt)/C 10/D 1>>]");
        using PdfDocument doc = Open("/VP[" + first + " " + second + "]");
        PdfPage page = doc.Pages[0];

        page.FindViewport(150, 150)!.Name.Should().Be("Detail");
        page.FindViewport(50, 50)!.Name.Should().Be("Sheet");
        page.FindViewport(500, 500).Should().BeNull();
        page.MeasureDistance(110, 150, 120, 150)!.Value.Formatted.Should().Be("100 pt");
        page.MeasureDistance(150, 150, 300, 150).Should().BeNull("the points are in different viewports");
    }

    [Fact]
    public void Area_UsesTheAreaChain_OrTheAxisFactors()
    {
        using PdfDocument withArea = Open(Viewport("0 0 300 300", "Plan", "(1:1)",
            "[<</U(m)/C 0.01/D 100>>]/A[<</U(sq m)/C 0.0001/D 100>>]"));
        using PdfDocument axesOnly = Open(Viewport("0 0 300 300", "Plan", "(1:1)", "[<</U(m)/C 0.01/D 100>>]"));

        withArea.Pages[0].Viewports[0].Measure!.Area(10000).Formatted.Should().Be("1.00 sq m");
        axesOnly.Pages[0].Viewports[0].Measure!.Area(10000).Value.Should().BeApproximately(1, 1e-9);
    }

    [Fact]
    public void Coordinates_AreMeasuredFromTheOrigin()
    {
        using PdfDocument doc = Open(Viewport("0 0 300 300", "Plan", "(1:1)",
            "[<</U(mm)/C 1/D 1>>]/O[100 50]"));

        (PdfMeasurement x, PdfMeasurement y) = doc.Pages[0].Viewports[0].Measure!.Coordinates(130, 70);

        x.Value.Should().Be(30);
        y.Value.Should().Be(20);
    }

    [Fact]
    public void GeospatialMeasure_IsReportedButNotConverted()
    {
        using PdfDocument doc = Open("/VP[<</Type/Viewport/BBox[0 0 300 300]/Measure<</Type/Measure/Subtype/GEO>>>>]");

        PdfMeasure measure = doc.Pages[0].Viewports[0].Measure!;
        measure.IsRectilinear.Should().BeFalse();
        Action convert = () => measure.Distance(0, 0, 10, 10);
        convert.Should().Throw<InvalidOperationException>();
        doc.Pages[0].MeasureDistance(0, 0, 10, 10).Should().BeNull();
    }

    [Fact]
    public void PageWithoutViewports_HasNone()
    {
        using PdfDocument doc = Open(string.Empty);

        doc.Pages[0].Viewports.Should().BeEmpty();
        doc.Pages[0].FindViewport(1, 1).Should().BeNull();
    }

    private static string Viewport(string bbox, string name, string ratio, string x) =>
        "/VP[" + ViewportDict(bbox, name, ratio, x) + "]";

    private static string ViewportDict(string bbox, string name, string ratio, string x) =>
        "<</Type/Viewport/BBox[" + bbox + "]/Name(" + name + ")/Measure<</Type/Measure/Subtype/RL/R"
        + ratio + "/X" + x + ">>>>";

    private static PdfDocument Open(string pageEntries)
    {
        List<string> bodies = new List<string>
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 400 400]" + pageEntries + ">>",
        };

        StringBuilder sb = new StringBuilder("%PDF-1.7\n");
        List<int> offsets = new List<int>();
        for (int i = 0; i < bodies.Count; i++)
        {
            offsets.Add(sb.Length);
            sb.Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append(" 0 obj\n").Append(bodies[i]).Append("\nendobj\n");
        }

        int xref = sb.Length;
        sb.Append("xref\n0 4\n0000000000 65535 f \n");
        foreach (int off in offsets)
        {
            sb.Append(off.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        sb.Append("trailer <</Size 4/Root 1 0 R>>\nstartxref\n").Append(xref.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF");
        return PdfDocument.Open(new MemoryStream(Encoding.Latin1.GetBytes(sb.ToString())), leaveOpen: false);
    }
}
