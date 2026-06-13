// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using Chuvadi.Pdf.Graphics;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Images.Tests;

/// <summary>
/// Tests for progressive (SOF2) JPEG decoding, including chroma subsampling,
/// grayscale, and CMYK. Fixtures are small progressive JPEGs encoded with a
/// reference encoder; expected colours are the reference decoder's output.
/// </summary>
public sealed class JpegProgressiveTests
{
    // 32x16 progressive JPEG, 4:2:0, left half red (200,30,40), right blue (30,40,200).
    private const string ProgRgb =
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAIBAQEBAQIBAQECAgICAgQDAgICAgUEBAMEBgUGBgYFBgYGBwkIBgcJBwYGCAsICQoKCgoKBggLDAsKDAkKCgr/2wBDAQICAgICAgUDAwUKBwYHCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgr/wgARCAAQACADASIAAhEBAxEB/8QAFgABAQEAAAAAAAAAAAAAAAAAAAYH/8QAFQEBAQAAAAAAAAAAAAAAAAAACAb/2gAMAwEAAhADEAAAAcjEeqIkNQZ//8QAFBABAAAAAAAAAAAAAAAAAAAAMP/aAAgBAQABBQIP/8QAFBEBAAAAAAAAAAAAAAAAAAAAEP/aAAgBAwEBPwE//8QAFBEBAAAAAAAAAAAAAAAAAAAAEP/aAAgBAgEBPwE//8QAFBABAAAAAAAAAAAAAAAAAAAAMP/aAAgBAQAGPwIP/8QAFBABAAAAAAAAAAAAAAAAAAAAMP/aAAgBAQABPyEP/9oADAMBAAIAAwAAABAAD//EABQRAQAAAAAAAAAAAAAAAAAAABD/2gAIAQMBAT8QP//EABQRAQAAAAAAAAAAAAAAAAAAABD/2gAIAQIBAT8QP//EABQQAQAAAAAAAAAAAAAAAAAAADD/2gAIAQEAAT8QD//Z";

    // 16x16 progressive grayscale, solid 128.
    private const string ProgGray =
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAIBAQEBAQIBAQECAgICAgQDAgICAgUEBAMEBgUGBgYFBgYGBwkIBgcJBwYGCAsICQoKCgoKBggLDAsKDAkKCgr/wgALCAAQABABAREA/8QAFAABAAAAAAAAAAAAAAAAAAAAAP/aAAgBAQAAAAEP/8QAFBABAAAAAAAAAAAAAAAAAAAAIP/aAAgBAQABBQIf/8QAFBABAAAAAAAAAAAAAAAAAAAAIP/aAAgBAQAGPwIf/8QAFBABAAAAAAAAAAAAAAAAAAAAIP/aAAgBAQABPyEf/9oACAEBAAAAEA//xAAUEAEAAAAAAAAAAAAAAAAAAAAg/9oACAEBAAE/EB//2Q==";

    // 16x16 progressive CMYK (Adobe), solid C180 M60 Y20 K30.
    private const string ProgCmyk =
        "/9j/7gAOQWRvYmUAZAAAAAAA/9sAQwACAQEBAQECAQEBAgICAgIEAwICAgIFBAQDBAYFBgYGBQYGBgcJCAYHCQcGBggLCAkKCgoKCgYICwwLCgwJCgoK/8IAFAgAEAAQBEMRAE0RAFkRAEsRAP/EABYAAQEBAAAAAAAAAAAAAAAAAAAIB//aAA4EQwBNAFkASwAAAAHFaGtawgAP/8QAFBABAAAAAAAAAAAAAAAAAAAAIP/aAAgBQwABBQIf/8QAFBABAAAAAAAAAAAAAAAAAAAAIP/aAAgBTQABBQIf/8QAFBABAAAAAAAAAAAAAAAAAAAAIP/aAAgBWQABBQIf/8QAFBABAAAAAAAAAAAAAAAAAAAAIP/aAAgBSwABBQIf/8QAFBABAAAAAAAAAAAAAAAAAAAAIP/aAAgBQwAGPwIf/8QAFBABAAAAAAAAAAAAAAAAAAAAIP/aAAgBTQAGPwIf/8QAFBABAAAAAAAAAAAAAAAAAAAAIP/aAAgBWQAGPwIf/8QAFBABAAAAAAAAAAAAAAAAAAAAIP/aAAgBSwAGPwIf/8QAFBABAAAAAAAAAAAAAAAAAAAAIP/aAAgBQwABPyEf/8QAFBABAAAAAAAAAAAAAAAAAAAAIP/aAAgBTQABPyEf/8QAFBABAAAAAAAAAAAAAAAAAAAAIP/aAAgBWQABPyEf/8QAFBABAAAAAAAAAAAAAAAAAAAAIP/aAAgBSwABPyEf/9oADgRDAE0AWQBLAAAAEAAA/8QAFBABAAAAAAAAAAAAAAAAAAAAIP/aAAgBQwABPxAf/8QAFBABAAAAAAAAAAAAAAAAAAAAIP/aAAgBTQABPxAf/8QAFBABAAAAAAAAAAAAAAAAAAAAIP/aAAgBWQABPxAf/8QAFBABAAAAAAAAAAAAAAAAAAAAIP/aAAgBSwABPxAf/9k=";

    private static void AssertNear(int actual, int expected, int tolerance)
    {
        Math.Abs(actual - expected).Should().BeLessThanOrEqualTo(
            tolerance, $"channel {actual} should be within {tolerance} of {expected}");
    }

    [Fact]
    public void Decode_ProgressiveRgb_HasCorrectDimensions()
    {
        ImageFrame frame = JpegDecoder.Decode(Convert.FromBase64String(ProgRgb));
        frame.Pixels.Width.Should().Be(32);
        frame.Pixels.Height.Should().Be(16);
        frame.OriginalFormat.Should().Be(ImageColorFormat.Rgb24);
    }

    [Fact]
    public void Decode_ProgressiveRgb_RecoversBothHalves()
    {
        ImageFrame frame = JpegDecoder.Decode(Convert.FromBase64String(ProgRgb));
        PixelBuffer p = frame.Pixels;

        (byte b1, byte g1, byte r1, _) = p.GetPixelBgra(8, 8);
        AssertNear(r1, 200, 12);
        AssertNear(g1, 30, 12);
        AssertNear(b1, 40, 12);

        (byte b2, byte g2, byte r2, _) = p.GetPixelBgra(24, 8);
        AssertNear(r2, 30, 12);
        AssertNear(g2, 40, 12);
        AssertNear(b2, 200, 12);
    }

    [Fact]
    public void Decode_ProgressiveGrayscale_IsUniformGray()
    {
        ImageFrame frame = JpegDecoder.Decode(Convert.FromBase64String(ProgGray));
        frame.OriginalFormat.Should().Be(ImageColorFormat.Gray8);

        (byte b, byte g, byte r, _) = frame.Pixels.GetPixelBgra(8, 8);
        AssertNear(r, 128, 6);
        r.Should().Be(g);
        g.Should().Be(b);
    }

    [Fact]
    public void Decode_ProgressiveCmyk_ConvertsToRgb()
    {
        ImageFrame frame = JpegDecoder.Decode(Convert.FromBase64String(ProgCmyk));
        frame.Pixels.Width.Should().Be(16);
        frame.OriginalFormat.Should().Be(ImageColorFormat.Rgb24);

        (byte b, byte g, byte r, _) = frame.Pixels.GetPixelBgra(8, 8);
        // Reference decoder yields approximately (66, 172, 207).
        AssertNear(r, 66, 16);
        AssertNear(g, 172, 16);
        AssertNear(b, 207, 16);
    }
}
