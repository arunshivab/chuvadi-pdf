// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.9.5.2 (image colour spaces), §7.4.4 (FlateDecode)
// PHASE: 3.18 — raw DeviceCMYK sample embedding.
//
// A decoded CMYK frame (e.g. a separated TIFF, whose C,M,Y,K channels live in
// the frame's B,G,R,A pixel slots) previously fell through the RGB embedding
// path: the K channel was misread as alpha and split into a soft mask. It now
// embeds as raw four-channel DeviceCMYK samples under FlateDecode.

using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Images;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Authoring.Tests;

public sealed class CmykRawEmbeddingTests
{
    [Fact]
    public void DrawImage_CmykFrame_EmbedsAsRawDeviceCmykUnderFlate()
    {
        // 4×4 frame: C,M,Y,K in the B,G,R,A slots (the TiffDecoder layout).
        ImageFrame frame = ImageFrame.Create(4, 4, ImageColorFormat.Cmyk32);
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                frame.Pixels.SetPixelBgra(x, y, 255, 0, 0, 64);
            }
        }

        PdfDocumentBuilder doc = PdfDocumentBuilder.Create();
        doc.AddPage(PageSize.A4).DrawImage(frame, 50, 50, 100, 100);
        byte[] bytes = doc.ToByteArray();

        string asText = Encoding.Latin1.GetString(bytes);
        asText.Should().Contain("/DeviceCMYK",
            "the ink separation is preserved rather than converted to RGB");
        asText.Should().Contain("/FlateDecode",
            "raw samples embed Flate-compressed");
        asText.Should().NotContain("/SMask",
            "the K channel must not be mistaken for alpha");

        using PdfDocument read = PdfDocument.Open(new MemoryStream(bytes), leaveOpen: false);
        read.PageCount.Should().Be(1);
    }
}
