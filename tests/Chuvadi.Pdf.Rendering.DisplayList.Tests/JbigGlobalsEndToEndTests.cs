// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.4.7 — JBIG2Decode; ITU-T T.88 (JBIG2).
// PHASE: Phase 2 — item 22, /JBIG2Globals end-to-end.
//
// Loads a real PDF whose only image is JBIG2-compressed with a symbol dictionary
// carried in a /JBIG2Globals shared-segment stream, and verifies the image decodes
// through the full display-list pipeline. This exercises ContentStreamLoader
// resolving /JBIG2Globals from the document and Jbig2Filter decoding with it; were
// the globals not resolved, the decode would throw and no image op would be emitted.

using System.IO;
using System.Linq;
using Chuvadi.Pdf.Documents;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.DisplayList.Tests;

public sealed class JbigGlobalsEndToEndTests
{
    private static readonly string FixturePath = Path.Combine(
        System.AppContext.BaseDirectory, "Fixtures", "Jbig2", "testPDF_JBIG2.pdf");

    [Fact]
    public void RealJbig2Pdf_DecodesImageThroughPipeline()
    {
        byte[] pdf = File.ReadAllBytes(FixturePath);

        using PdfDocument document = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        PageDisplayList list = DisplayListBuilder.Build(document, 0);

        ImageOp image = list.OfType<ImageOp>().Should().ContainSingle().Subject;

        image.Width.Should().Be(352);
        image.Height.Should().Be(91);
        image.BitsPerComponent.Should().Be(1);
        image.Format.Should().Be(ImageFormat.Raw);
        image.ColorSpace.Should().Be(PdfColorSpace.DeviceGray);

        int rowBytes = (image.Width + 7) / 8;
        image.PixelData.Length.Should().Be(rowBytes * image.Height);

        // Real decoded content, not a blank fill: the packed samples vary.
        image.PixelData.Distinct().Should().HaveCountGreaterThan(1);
    }
}
