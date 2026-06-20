// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.9.5.2 (image colour spaces), §7.4.8 (DCTDecode),
//        Adobe APP14 marker (DeviceCMYK / inverted-channel convention)
// PHASE: Phase 2 — item 38, CMYK JPEG image support
//
// A 4-component (CMYK / YCCK) JPEG previously went through the full decoder and
// was re-embedded as Flate-compressed DeviceRGB samples: lossy, larger, and a
// colour-space downgrade. It now embeds directly under DCTDecode as DeviceCMYK,
// preserving the original DCT bytes. An Adobe APP14 marker additionally signals
// the inverted-channel convention, corrected with a /Decode [1 0 ...] array.

using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Authoring.Tests;

public sealed class CmykJpegEmbeddingTests
{
    private const string InversionDecode = "1 0 1 0 1 0 1 0";

    [Fact]
    public void DrawImage_AdobeCmykJpeg_EmbedsAsDeviceCmykWithInversion()
    {
        byte[] jpeg = BuildSyntheticCmykJpeg(adobe: true);

        var doc = PdfDocumentBuilder.Create();
        doc.AddPage(PageSize.A4).DrawImage(jpeg, 50, 50, 100, 100);
        byte[] bytes = doc.ToByteArray();

        string asText = Encoding.Latin1.GetString(bytes);
        asText.Should().Contain("/DeviceCMYK", "a 4-component JPEG is a CMYK image");
        asText.Should().Contain("/DCTDecode", "the original DCT stream is embedded directly");
        asText.Should().NotContain("/FlateDecode", "the image must not be decoded and re-deflated");
        asText.Should().Contain(
            InversionDecode, "an Adobe marker requires the inverted-channel Decode array");
        ContainsSubsequence(bytes, jpeg).Should().BeTrue(
            "the original JPEG bytes are passed through unchanged");

        using PdfDocument read = PdfDocument.Open(new MemoryStream(bytes), leaveOpen: false);
        read.PageCount.Should().Be(1);
    }

    [Fact]
    public void DrawImage_NonAdobeCmykJpeg_EmbedsAsDeviceCmykWithoutInversion()
    {
        byte[] jpeg = BuildSyntheticCmykJpeg(adobe: false);

        var doc = PdfDocumentBuilder.Create();
        doc.AddPage(PageSize.A4).DrawImage(jpeg, 50, 50, 100, 100);
        byte[] bytes = doc.ToByteArray();

        string asText = Encoding.Latin1.GetString(bytes);
        asText.Should().Contain("/DeviceCMYK");
        asText.Should().Contain("/DCTDecode");
        asText.Should().NotContain(
            InversionDecode, "without an Adobe marker the channels are not inverted");
    }

    // Builds a minimal 4-component baseline JPEG header sufficient for the
    // embedder, which passes 4-component data through without decoding. The scan
    // data is a stub: header parsing stops at the SOS marker.
    private static byte[] BuildSyntheticCmykJpeg(bool adobe)
    {
        List<byte> b = new List<byte>();

        void U16(int v)
        {
            b.Add((byte)((v >> 8) & 0xFF));
            b.Add((byte)(v & 0xFF));
        }

        // SOI
        b.Add(0xFF);
        b.Add(0xD8);

        if (adobe)
        {
            // APP14 "Adobe": version, flags0, flags1, transform (2 = YCCK).
            b.Add(0xFF);
            b.Add(0xEE);
            U16(14);
            foreach (char c in "Adobe")
            {
                b.Add((byte)c);
            }

            b.Add(0x00);
            b.Add(0x64);
            b.Add(0x00);
            b.Add(0x00);
            b.Add(0x00);
            b.Add(0x00);
            b.Add(0x02);
        }

        // SOF0: 8-bit, 2x2, 4 components.
        b.Add(0xFF);
        b.Add(0xC0);
        U16(20);
        b.Add(0x08);
        U16(2);
        U16(2);
        b.Add(0x04);
        for (int id = 1; id <= 4; id++)
        {
            b.Add((byte)id);
            b.Add(0x11);
            b.Add(0x00);
        }

        // SOS + stub scan data + EOI.
        b.Add(0xFF);
        b.Add(0xDA);
        U16(8);
        b.Add(0x01);
        b.Add(0x01);
        b.Add(0x00);
        b.Add(0x00);
        b.Add(0x3F);
        b.Add(0x00);
        b.Add(0x00);
        b.Add(0x00);
        b.Add(0xFF);
        b.Add(0xD9);

        return b.ToArray();
    }

    private static bool ContainsSubsequence(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return true;
            }
        }

        return false;
    }
}
