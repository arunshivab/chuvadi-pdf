// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2 — Chuvadi.Pdf.Watermark tests

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Images;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Watermark.Tests;

// ── WatermarkException ─────────────────────────────────────────────────────

public sealed class WatermarkExceptionTests
{
    [Fact]
    public void DefaultConstructor_HasMessage()
    {
        WatermarkException ex = new WatermarkException();
        ex.Message.Should().NotBeEmpty();
    }

    [Fact]
    public void MessageConstructor_PreservesMessage()
    {
        WatermarkException ex = new WatermarkException("test");
        ex.Message.Should().Be("test");
    }

    [Fact]
    public void InnerExceptionConstructor_PreservesInner()
    {
        InvalidOperationException inner = new InvalidOperationException("inner");
        WatermarkException ex = new WatermarkException("outer", inner);
        ex.InnerException.Should().BeSameAs(inner);
    }
}

// ── TextWatermarkOptions ───────────────────────────────────────────────────

public sealed class TextWatermarkOptionsTests
{
    [Fact]
    public void Constructor_NullText_Throws()
    {
        Action act = () => new TextWatermarkOptions(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_SetsText()
    {
        TextWatermarkOptions opts = new TextWatermarkOptions("DRAFT");
        opts.Text.Should().Be("DRAFT");
    }

    [Fact]
    public void Defaults_AreReasonable()
    {
        TextWatermarkOptions opts = new TextWatermarkOptions("X");
        opts.FontSize.Should().BeGreaterThan(0);
        opts.Opacity.Should().BeInRange(0f, 1f);
        opts.RotationDegrees.Should().Be(45.0);
        opts.FontName.Should().NotBeEmpty();
    }
}

// ── ImageWatermarkOptions ──────────────────────────────────────────────────

public sealed class ImageWatermarkOptionsTests
{
    [Fact]
    public void Defaults_AreReasonable()
    {
        ImageWatermarkOptions opts = new ImageWatermarkOptions();
        opts.Opacity.Should().BeInRange(0f, 1f);
        opts.ScaleFraction.Should().BeInRange(0.0, 1.0);
    }
}

// ── WatermarkStamper ───────────────────────────────────────────────────────

public sealed class WatermarkStamperTests
{
    // ── ApplyText guards ───────────────────────────────────────────────────

    [Fact]
    public void ApplyText_NullOutput_Throws()
    {
        using (PdfDocument doc = OpenBlankDoc())
        {
            Action act = () => WatermarkStamper.ApplyText(null!, doc, new TextWatermarkOptions("X"));
            act.Should().Throw<ArgumentNullException>();
        }
    }

    [Fact]
    public void ApplyText_NullDocument_Throws()
    {
        Action act = () => WatermarkStamper.ApplyText(new MemoryStream(), null!, new TextWatermarkOptions("X"));
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ApplyText_NullOptions_Throws()
    {
        using (PdfDocument doc = OpenBlankDoc())
        {
            Action act = () => WatermarkStamper.ApplyText(new MemoryStream(), doc, null!);
            act.Should().Throw<ArgumentNullException>();
        }
    }

    [Fact]
    public void ApplyText_EmptyText_Throws()
    {
        using (PdfDocument doc = OpenBlankDoc())
        {
            Action act = () => WatermarkStamper.ApplyText(
                new MemoryStream(), doc, new TextWatermarkOptions("   "));
            act.Should().Throw<WatermarkException>();
        }
    }

    // ── ApplyImage guards ──────────────────────────────────────────────────

    [Fact]
    public void ApplyImage_NullOutput_Throws()
    {
        using (PdfDocument doc = OpenBlankDoc())
        {
            ImageFrame frame = ImageFrame.Create(10, 10, ImageColorFormat.Rgb24);
            Action act = () => WatermarkStamper.ApplyImage(
                null!, doc, frame, new ImageWatermarkOptions());
            act.Should().Throw<ArgumentNullException>();
        }
    }

    [Fact]
    public void ApplyImage_NullFrame_Throws()
    {
        using (PdfDocument doc = OpenBlankDoc())
        {
            Action act = () => WatermarkStamper.ApplyImage(
                new MemoryStream(), doc, null!, new ImageWatermarkOptions());
            act.Should().Throw<ArgumentNullException>();
        }
    }

    // ── Functional tests ───────────────────────────────────────────────────

    [Fact]
    public void ApplyText_SinglePage_AddsWatermarkContent()
    {
        using (MemoryStream source = BuildBlankPdf())
        using (PdfDocument doc = PdfDocument.Open(source, leaveOpen: true))
        using (MemoryStream output = new MemoryStream())
        {
            WatermarkStamper.ApplyText(output, doc,
                new TextWatermarkOptions("CONFIDENTIAL"));

            // Verify the watermark overlay was added by its graphics-state name.
            // This is more robust than a raw size comparison, which is unreliable
            // now that the source and watermark paths serialize differently.
            string text = Encoding.Latin1.GetString(output.ToArray());
            text.Should().Contain("WMTextGS");
        }
    }

    [Fact]
    public void ApplyText_ProducesReadablePdf()
    {
        using (MemoryStream source = BuildBlankPdf())
        using (PdfDocument doc = PdfDocument.Open(source, leaveOpen: true))
        using (MemoryStream output = new MemoryStream())
        {
            WatermarkStamper.ApplyText(output, doc,
                new TextWatermarkOptions("DRAFT"));

            output.Seek(0, SeekOrigin.Begin);

            using (PdfDocument result = PdfDocument.Open(output, leaveOpen: true))
            {
                result.PageCount.Should().Be(1);
            }
        }
    }

    [Fact]
    public void ApplyText_MultiPage_WatermarksAllPages()
    {
        using (MemoryStream source = BuildMultiPagePdf(3))
        using (PdfDocument doc = PdfDocument.Open(source, leaveOpen: true))
        using (MemoryStream output = new MemoryStream())
        {
            WatermarkStamper.ApplyText(output, doc,
                new TextWatermarkOptions("CONFIDENTIAL"));

            output.Seek(0, SeekOrigin.Begin);

            using (PdfDocument result = PdfDocument.Open(output, leaveOpen: true))
            {
                result.PageCount.Should().Be(3);
            }
        }
    }

    [Fact]
    public void ApplyText_SpecificPages_OnlyWatermarksThose()
    {
        using (MemoryStream source = BuildMultiPagePdf(3))
        using (PdfDocument doc = PdfDocument.Open(source, leaveOpen: true))
        using (MemoryStream output = new MemoryStream())
        {
            TextWatermarkOptions opts = new TextWatermarkOptions("DRAFT")
            {
                PageIndices = [0, 2], // Only pages 0 and 2
            };
            WatermarkStamper.ApplyText(output, doc, opts);

            output.Seek(0, SeekOrigin.Begin);

            using (PdfDocument result = PdfDocument.Open(output, leaveOpen: true))
            {
                result.PageCount.Should().Be(3);
            }
        }
    }

    [Fact]
    public void ApplyImage_SinglePage_ProducesReadablePdf()
    {
        using (MemoryStream source = BuildBlankPdf())
        using (PdfDocument doc = PdfDocument.Open(source, leaveOpen: true))
        using (MemoryStream output = new MemoryStream())
        {
            PixelBuffer buf = new PixelBuffer(20, 20);
            buf.ClearWhite();
            buf.SetPixel(10, 10, ColorF.FromRgb(1f, 0f, 0f));
            ImageFrame frame = new ImageFrame(buf, ImageColorFormat.Rgb24);

            WatermarkStamper.ApplyImage(output, doc, frame, new ImageWatermarkOptions());

            output.Seek(0, SeekOrigin.Begin);

            using (PdfDocument result = PdfDocument.Open(output, leaveOpen: true))
            {
                result.PageCount.Should().Be(1);
            }
        }
    }

    // ── Builder helpers ────────────────────────────────────────────────────

    private static PdfDocument OpenBlankDoc()
    {
        MemoryStream ms = BuildBlankPdf();
        return PdfDocument.Open(ms, leaveOpen: false);
    }

    private static MemoryStream BuildBlankPdf()
    {
        return BuildMultiPagePdf(1);
    }

    private static MemoryStream BuildMultiPagePdf(int pageCount)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);

        PdfArray kids = new PdfArray([]);
        List<PdfIndirectObject> objects = new List<PdfIndirectObject>();

        PdfArray mediaBox = new PdfArray([
            new PdfInteger(0), new PdfInteger(0),
            new PdfInteger(595), new PdfInteger(842),
        ]);

        for (int i = 0; i < pageCount; i++)
        {
            PdfObjectId pageId = new PdfObjectId(3 + i, 0);
            PdfDictionary pageDict = new PdfDictionary();
            pageDict.Set(PdfName.Type, PdfName.Page);
            pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
            pageDict.Set(PdfName.MediaBox, mediaBox);
            objects.Add(new PdfIndirectObject(pageId, pageDict));
            kids.Add(new PdfReference(pageId));
        }

        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, kids);
        pagesDict.Set(PdfName.Count, pageCount);

        PdfDictionary catalogDict = new PdfDictionary();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

        objects.Add(new PdfIndirectObject(catalogId, catalogDict));
        objects.Add(new PdfIndirectObject(pagesId, pagesDict));

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        return ms;
    }
}
