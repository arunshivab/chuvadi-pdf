// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  Adobe Type 1 Font Format §5.3 (/FontMatrix); PDF 32000-1:2008 §9.6.2
// PHASE: 3.35.0 (N15) — the program's /FontMatrix is honoured, not 1/1000.

using System.Collections.Generic;
using System.Text;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Fonts.Rendering.Tests;

public sealed class Type1FontMatrix3350Tests
{
    // 0 500 hsbw, then a 100 x 100 square, closepath, endchar.
    private static readonly byte[] Square = { 139, 248, 136, 13, 239, 139, 5, 139, 239, 5, 39, 139, 5, 9, 14 };

    [Fact]
    public void A2048UnitMatrix_ScalesOutlineAndAdvance()
    {
        // govdocs1-0043 style: 1/2048 units per em. At 2048 pt the glyph's
        // 100-unit square is 100 pt, and its 500-unit advance is 500 pt.
        Type1FontRenderer font = Create("/FontMatrix [0.00048828125 0 0 0.00048828125 0 0] readonly def\n");

        Path path = font.GetGlyphPath(65, 2048, out double advance);

        advance.Should().BeApproximately(500, 0.01);
        RectangleF bounds = path.EndpointBounds();
        bounds.Width.Should().BeApproximately(100, 0.5);
        bounds.Height.Should().BeApproximately(100, 0.5);
    }

    [Fact]
    public void AMissingMatrix_FallsBackToOneThousandth()
    {
        Type1FontRenderer font = Create(string.Empty);

        Path path = font.GetGlyphPath(65, 1000, out double advance);

        advance.Should().BeApproximately(500, 0.01);
        path.EndpointBounds().Height.Should().BeApproximately(100, 0.5);
    }

    [Fact]
    public void ADegenerateMatrix_FallsBackToOneThousandth()
    {
        Type1FontRenderer font = Create("/FontMatrix [0 0 0 0 0 0] readonly def\n");

        Path path = font.GetGlyphPath(65, 1000, out double advance);

        advance.Should().BeApproximately(500, 0.01);
        path.EndpointBounds().Width.Should().BeApproximately(100, 0.5);
    }

    [Fact]
    public void ABraceDelimitedMatrix_IsRead()
    {
        Type1FontRenderer.ParseFontMatrix("/FontMatrix {0.002 0 0 0.002 0 0} readonly def")
            .Should().Equal(0.002, 0, 0, 0.002, 0, 0);
    }

    private static Type1FontRenderer Create(string matrixLine)
    {
        Type1FontRenderer? font = Type1FontRenderer.Create(Program(matrixLine), new PdfDictionary(), new PdfObjectStore(_ => null));
        font.Should().NotBeNull();
        return font!;
    }

    // A minimal Type 1 program: code 65 is /A, the square above.
    private static byte[] Program(string matrixLine)
    {
        string clear = "%!PS-AdobeFont-1.0: Test\n" + matrixLine + "/Encoding 256 array\n"
            + "0 1 255 {1 index exch /.notdef put} for\ndup 65 /A put\nreadonly def\ncurrentfile eexec\n";
        byte[] cs = Encrypt(Square, 4330);
        List<byte> priv = new List<byte>();
        priv.AddRange(Encoding.Latin1.GetBytes("dup /Private 8 dict dup begin /lenIV 4 def\n/CharStrings 1 dict dup begin\n/A "
            + cs.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + " RD "));
        priv.AddRange(cs);
        priv.AddRange(Encoding.Latin1.GetBytes(" ND\nend\nend\n"));
        List<byte> program = new List<byte>(Encoding.Latin1.GetBytes(clear));
        program.AddRange(Encrypt(priv.ToArray(), 55665));
        return program.ToArray();
    }

    // Type 1 encryption with four leading bytes (lenIV 4).
    private static byte[] Encrypt(byte[] plain, ushort r)
    {
        byte[] input = new byte[plain.Length + 4];
        input[0] = 0xF1;
        input[1] = 0xF2;
        input[2] = 0xF3;
        input[3] = 0xF4;
        plain.CopyTo(input, 4);
        byte[] output = new byte[input.Length];
        for (int i = 0; i < input.Length; i++)
        {
            byte c = (byte)(input[i] ^ (r >> 8));
            output[i] = c;
            r = (ushort)(((c + r) * 52845) + 22719);
        }

        return output;
    }
}
