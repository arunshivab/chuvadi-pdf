// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.9.2.2 — Text string type
// Verifies PdfString.ToTextString decodes per the byte-order-mark rules.

using System.Text;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Primitives.Tests;

public sealed class PdfStringTextStringTests
{
    [Fact]
    public void Utf16Be_BomDecodes()
    {
        byte[] bytes = { 0xFE, 0xFF, 0x00, (byte)'H', 0x00, (byte)'i' };
        new PdfString(bytes).ToTextString().Should().Be("Hi");
    }

    [Fact]
    public void Utf16Le_BomDecodes()
    {
        byte[] bytes = { 0xFF, 0xFE, (byte)'H', 0x00, (byte)'i', 0x00 };
        new PdfString(bytes).ToTextString().Should().Be("Hi");
    }

    [Fact]
    public void Utf8_BomDecodes()
    {
        byte[] bytes = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(Encoding.UTF8.GetBytes("Café"));
        new PdfString(bytes).ToTextString().Should().Be("Café");
    }

    [Fact]
    public void NoBom_FallsBackToPdfDocEncoding()
    {
        new PdfString(Encoding.Latin1.GetBytes("Plain")).ToTextString().Should().Be("Plain");
    }
}

file static class ByteArrayExtensions
{
    public static byte[] Concat(this byte[] head, byte[] tail)
    {
        byte[] result = new byte[head.Length + tail.Length];
        head.CopyTo(result, 0);
        tail.CopyTo(result, head.Length);
        return result;
    }
}
