// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.3.4 — String objects, §7.3.5 — Name objects
// PHASE: Phase 2.8 — DisplayList consolidation (one walker, two sinks)
// Token-to-value decoding shared by the content-stream walker.

using System.Collections.Generic;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Rendering.Walking;

/// <summary>
/// Decodes string and name tokens for the content-stream walker.
/// </summary>
/// <remarks>
/// Consolidation note: the two pre-2.8 builders carried separate copies of
/// this logic, and the raster copy did not decode octal escapes
/// (<c>\nnn</c>, §7.3.4.2) — literal strings written with octal-escaped
/// bytes (as Chuvadi's own authoring layer emits for WinAnsi characters)
/// rendered their escape characters verbatim through the raster path. This
/// unified decoder handles octal on both paths.
/// </remarks>
internal static class ContentStrings
{
    /// <summary>
    /// Extracts the bytes of a literal or hex string token, applying §7.3.4
    /// escape and hex decoding. Non-string tokens return their raw bytes.
    /// </summary>
    internal static byte[] ExtractStringBytes(PdfToken token)
    {
        if (token.Type == PdfTokenType.HexString)
        {
            return DecodeHexString(token.RawBytes);
        }
        if (token.Type == PdfTokenType.LiteralString)
        {
            return DecodeLiteralString(token.RawBytes);
        }
        return token.RawBytes;
    }

    /// <summary>Extracts a name token's text; empty for non-name tokens.</summary>
    internal static string ExtractName(PdfToken token)
    {
        if (token.Type != PdfTokenType.Name)
        {
            return string.Empty;
        }

        byte[] bytes = token.RawBytes;
        char[] chars = new char[bytes.Length];
        for (int i = 0; i < bytes.Length; i++)
        {
            chars[i] = (char)bytes[i];
        }
        return new string(chars);
    }

    // Decodes a literal string's bytes: strips the wrapping parentheses when
    // present and resolves backslash escapes, including octal (§7.3.4.2).
    private static byte[] DecodeLiteralString(byte[] raw)
    {
        int start = 0;
        int end = raw.Length;
        if (end > 0 && raw[0] == (byte)'(')
        {
            start = 1;
        }
        if (end > start && raw[end - 1] == (byte)')')
        {
            end--;
        }

        List<byte> result = new(end - start);
        for (int i = start; i < end; i++)
        {
            byte b = raw[i];
            if (b != (byte)'\\' || i + 1 >= end)
            {
                result.Add(b);
                continue;
            }

            byte next = raw[++i];
            switch (next)
            {
                case (byte)'n':
                    result.Add((byte)'\n');
                    break;
                case (byte)'r':
                    result.Add((byte)'\r');
                    break;
                case (byte)'t':
                    result.Add((byte)'\t');
                    break;
                case (byte)'b':
                    result.Add(0x08);
                    break;
                case (byte)'f':
                    result.Add(0x0C);
                    break;
                case (byte)'(':
                    result.Add((byte)'(');
                    break;
                case (byte)')':
                    result.Add((byte)')');
                    break;
                case (byte)'\\':
                    result.Add((byte)'\\');
                    break;
                default:
                    if (next >= (byte)'0' && next <= (byte)'7')
                    {
                        int v = next - (byte)'0';
                        int digits = 1;
                        while (digits < 3 && i + 1 < end &&
                               raw[i + 1] >= (byte)'0' && raw[i + 1] <= (byte)'7')
                        {
                            v = (v * 8) + (raw[++i] - (byte)'0');
                            digits++;
                        }
                        result.Add((byte)v);
                    }
                    else
                    {
                        result.Add(next);
                    }
                    break;
            }
        }
        return result.ToArray();
    }

    // Decodes a hex string token: angle brackets and interior non-hex bytes
    // are ignored; an odd final digit is padded with 0 (§7.3.4.3).
    private static byte[] DecodeHexString(byte[] raw)
    {
        List<byte> result = new(raw.Length / 2);
        int pending = -1;
        foreach (byte b in raw)
        {
            int v = HexValue(b);
            if (v < 0)
            {
                continue;
            }
            if (pending < 0)
            {
                pending = v;
            }
            else
            {
                result.Add((byte)((pending << 4) | v));
                pending = -1;
            }
        }
        if (pending >= 0)
        {
            result.Add((byte)(pending << 4));
        }
        return result.ToArray();
    }

    private static int HexValue(byte b)
    {
        if (b >= (byte)'0' && b <= (byte)'9')
        {
            return b - (byte)'0';
        }
        if (b >= (byte)'A' && b <= (byte)'F')
        {
            return 10 + b - (byte)'A';
        }
        if (b >= (byte)'a' && b <= (byte)'f')
        {
            return 10 + b - (byte)'a';
        }
        return -1;
    }
}
