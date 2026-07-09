// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.5.5 — Appearance streams; §12.7.8 — XFA Forms
// PHASE: Chuvadi.Pdf.Documents — annotation appearance resolution + XFA
//        data-field geometry binding.
//
// The fixture hybrid_xfa_widget.pdf is a minimal hybrid XFA/AcroForm file:
// one page whose content shows a label, one text widget carrying /V,
// /Rect [100 100 300 120], and an /AP /N form (BBox [0 0 200 20]) that shows
// the value, and an /XFA whose template binds field "NameField" to dataset
// node ROOT.NAME_FIELD.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Documents.Tests;

public sealed class AnnotationAppearanceTests
{
    private const string Fixture = "fixtures/hybrid_xfa_widget.pdf";

    [Fact]
    public void CollectFindsWidgetAppearance()
    {
        using FileStream input = File.OpenRead(Fixture);
        using PdfDocument document = PdfDocument.Open(input, leaveOpen: false);

        IReadOnlyList<AnnotationAppearance> appearances =
            PageAnnotationAppearances.Collect(document.Pages[0], document.Objects);

        appearances.Should().HaveCount(1);
        appearances[0].Rect.X1.Should().Be(100);
        appearances[0].Rect.Y1.Should().Be(100);
        appearances[0].Rect.X2.Should().Be(300);
        appearances[0].Rect.Y2.Should().Be(120);
    }

    [Fact]
    public void PlacementMapsBBoxOntoRect()
    {
        using FileStream input = File.OpenRead(Fixture);
        using PdfDocument document = PdfDocument.Open(input, leaveOpen: false);

        AnnotationAppearance appearance =
            PageAnnotationAppearances.Collect(document.Pages[0], document.Objects)[0];

        // BBox [0 0 200 20] onto Rect [100 100 300 120]: scale 1, offset 100.
        appearance.ScaleX.Should().Be(1.0);
        appearance.ScaleY.Should().Be(1.0);
        appearance.OffsetX.Should().Be(100.0);
        appearance.OffsetY.Should().Be(100.0);
    }

    [Fact]
    public void XfaDataFieldGeometryResolvesThroughTemplateBind()
    {
        using FileStream input = File.OpenRead(Fixture);
        using PdfDocument document = PdfDocument.Open(input, leaveOpen: false);

        document.IsXfa.Should().BeTrue();
        document.XfaKind.Should().Be(XfaKind.Hybrid);

        XfaDataField? field = document.Xfa!.DataFields
            .FirstOrDefault(f => f.NodePath.EndsWith("NAME_FIELD", System.StringComparison.Ordinal));

        field.Should().NotBeNull();
        field!.Value.Should().Be("HELLO-VALUE");

        // The dataset node name (NAME_FIELD) differs from the widget's field
        // name (NameField[0]); the geometry resolves through the template's
        // <bind ref="$record.ROOT.NAME_FIELD"/> mapping.
        field.Geometry.Should().NotBeNull();
        field.Geometry!.PageIndex.Should().Be(0);
        field.Geometry.Rectangle.X1.Should().Be(100);
        field.Geometry.Rectangle.Y1.Should().Be(100);
        field.Geometry.Rectangle.X2.Should().Be(300);
        field.Geometry.Rectangle.Y2.Should().Be(120);
    }
}
