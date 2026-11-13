// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  Adobe Type 1 Font Format §6 (charstring commands), §7 (encryption)
// PHASE: 3.34.0 (#52b, D-353) — a line with no current point starts a subpath.

using System.Collections.Generic;
using System.Text;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Fonts.Rendering.Tests;

public sealed class Type1NoCurrentPoint3340Tests
{
    // 0 500 hsbw, then 100 0 rlineto, 0 100 rlineto, -100 0 rlineto,
    // closepath, endchar: lines with no rmoveto before them.
    private static readonly byte[] Square = { 139, 248, 136, 13, 239, 139, 5, 139, 239, 5, 39, 139, 5, 9, 14 };

    [Fact]
    public void LinesWithNoMoveTo_StartAtTheSidebearingPoint()
    {
        Type1FontRenderer font = Type1FontRenderer.Create(Program(Square), new PdfDictionary(), new PdfObjectStore(_ => null))!;
        font.Should().NotBeNull();

        Path path = font.GetGlyphPath(65, 1000, out double advance);

        advance.Should().BeApproximately(500, 0.01);
        path.Segments[0].Kind.Should().Be(PathSegmentKind.MoveTo);
        path.Segments.Should().Contain(s => s.Kind == PathSegmentKind.LineTo);
        RectangleF bounds = path.EndpointBounds();
        bounds.Width.Should().BeApproximately(100, 0.5);
        bounds.Height.Should().BeApproximately(100, 0.5);
    }

    // A minimal Type 1 program: code 65 is /A, whose charstring is given.
    private static byte[] Program(byte[] charstring)
    {
        string clear = "%!PS-AdobeFont-1.0: Test\n/FontMatrix [0.001 0 0 0.001 0 0] readonly def\n/Encoding 256 array\n"
            + "0 1 255 {1 index exch /.notdef put} for\ndup 65 /A put\nreadonly def\ncurrentfile eexec\n";
        byte[] cs = Encrypt(charstring, 4330);
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
