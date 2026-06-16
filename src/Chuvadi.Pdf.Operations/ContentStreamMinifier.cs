// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC: PDF 32000-1:2008 §7.2 (lexical conventions), §8.9.7 (inline images)

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// Minifies decoded content-stream bytes by collapsing redundant whitespace and
/// dropping comments, while preserving every token verbatim. The result is
/// self-verified: the original and minified byte sequences are re-tokenised and
/// compared, and minification is abandoned (returning <c>null</c>) if the token
/// streams differ, if an inline image is present, or if the input is malformed —
/// so a content stream can never be corrupted, only shrunk or left unchanged.
/// </summary>
internal static class ContentStreamMinifier
{
    /// <summary>
    /// Returns minified content bytes, or null when minification is unsafe
    /// (inline image, malformed input, or token mismatch) or yields no gain.
    /// </summary>
    /// <param name="content">Decoded content-stream bytes.</param>
    internal static byte[]? Minify(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);

        byte[]? minified = MinifyCore(content);
        if (minified is null)
        {
            return null;
        }

        if (minified.Length >= content.Length)
        {
            return null;
        }

        if (!TokensEqual(content, minified))
        {
            return null;
        }

        return minified;
    }

    private static byte[]? MinifyCore(byte[] d)
    {
        List<byte> output = new List<byte>(d.Length);
        int i = 0;
        int last = -1;
        bool pending = false;

        while (i < d.Length)
        {
            byte b = d[i];

            if (IsWhitespace(b))
            {
                i++;
                pending = true;
                continue;
            }

            if (b == (byte)'%')
            {
                i++;
                while (i < d.Length && d[i] != (byte)'\n' && d[i] != (byte)'\r')
                {
                    i++;
                }
                pending = true;
                continue;
            }

            if (b == (byte)'(')
            {
                EmitSeparator(output, last, b, ref pending);
                int depth = 0;
                while (i < d.Length)
                {
                    byte c = d[i];
                    output.Add(c);
                    i++;
                    if (c == (byte)'\\')
                    {
                        if (i < d.Length)
                        {
                            output.Add(d[i]);
                            i++;
                        }
                        continue;
                    }
                    if (c == (byte)'(')
                    {
                        depth++;
                    }
                    else if (c == (byte)')')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            break;
                        }
                    }
                }
                if (depth != 0)
                {
                    return null;
                }
                last = (byte)')';
                continue;
            }

            if (b == (byte)'<')
            {
                if (i + 1 < d.Length && d[i + 1] == (byte)'<')
                {
                    EmitSeparator(output, last, b, ref pending);
                    output.Add(b);
                    output.Add(b);
                    i += 2;
                    last = (byte)'<';
                    continue;
                }

                EmitSeparator(output, last, b, ref pending);
                output.Add(b);
                i++;
                while (i < d.Length && d[i] != (byte)'>')
                {
                    output.Add(d[i]);
                    i++;
                }
                if (i >= d.Length)
                {
                    return null;
                }
                output.Add((byte)'>');
                i++;
                last = (byte)'>';
                continue;
            }

            if (b == (byte)'>')
            {
                if (i + 1 < d.Length && d[i + 1] == (byte)'>')
                {
                    output.Add(b);
                    output.Add(b);
                    i += 2;
                    last = (byte)'>';
                    pending = false;
                    continue;
                }
                return null;
            }

            if (b == (byte)'/')
            {
                EmitSeparator(output, last, b, ref pending);
                output.Add(b);
                i++;
                while (i < d.Length && !IsWhitespace(d[i]) && !IsDelimiter(d[i]))
                {
                    output.Add(d[i]);
                    i++;
                }
                last = output[output.Count - 1];
                continue;
            }

            if (b == (byte)'[' || b == (byte)']' || b == (byte)'{' || b == (byte)'}')
            {
                output.Add(b);
                i++;
                last = b;
                pending = false;
                continue;
            }

            if (b == (byte)')')
            {
                return null;
            }

            // Regular token: number, operator, or keyword.
            int start = i;
            while (i < d.Length && !IsWhitespace(d[i]) && !IsDelimiter(d[i]))
            {
                i++;
            }

            // Inline images (BI ... ID <binary> EI) carry arbitrary bytes that
            // are not safely tokenisable; leave such streams untouched.
            if (i - start == 2 && d[start] == (byte)'B' && d[start + 1] == (byte)'I')
            {
                return null;
            }

            EmitSeparator(output, last, d[start], ref pending);
            for (int k = start; k < i; k++)
            {
                output.Add(d[k]);
            }
            last = d[i - 1];
        }

        return output.ToArray();
    }

    // Emits a single space only when dropping the pending whitespace would merge
    // two regular tokens. No separator is needed next to a delimiter, which is
    // self-terminating.
    private static void EmitSeparator(List<byte> output, int last, byte nextFirst, ref bool pending)
    {
        if (pending && last >= 0 && IsRegular((byte)last) && IsRegular(nextFirst))
        {
            output.Add((byte)' ');
        }
        pending = false;
    }

    private static bool TokensEqual(byte[] a, byte[] b)
    {
        List<(PdfTokenType Type, string Text)>? ta = TryTokenize(a);
        List<(PdfTokenType Type, string Text)>? tb = TryTokenize(b);
        if (ta is null || tb is null || ta.Count != tb.Count)
        {
            return false;
        }

        for (int i = 0; i < ta.Count; i++)
        {
            if (ta[i].Type != tb[i].Type)
            {
                return false;
            }
            if (!string.Equals(ta[i].Text, tb[i].Text, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static List<(PdfTokenType Type, string Text)>? TryTokenize(byte[] data)
    {
        try
        {
            List<(PdfTokenType Type, string Text)> tokens = new List<(PdfTokenType Type, string Text)>();
            using MemoryStream ms = new MemoryStream(data);
            using PdfTokenizer tokenizer = new PdfTokenizer(ms, leaveOpen: false);
            while (true)
            {
                PdfToken token = tokenizer.Read();
                if (token.Type == PdfTokenType.EndOfStream || token.Type == PdfTokenType.EndOfFile)
                {
                    break;
                }
                tokens.Add((token.Type, token.RawText));
            }
            return tokens;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsWhitespace(byte b)
    {
        return b == 0x00 || b == 0x09 || b == 0x0A || b == 0x0C || b == 0x0D || b == 0x20;
    }

    private static bool IsDelimiter(byte b)
    {
        return b == (byte)'(' || b == (byte)')' || b == (byte)'<' || b == (byte)'>'
            || b == (byte)'[' || b == (byte)']' || b == (byte)'{' || b == (byte)'}'
            || b == (byte)'/' || b == (byte)'%';
    }

    private static bool IsRegular(byte b)
    {
        return !IsWhitespace(b) && !IsDelimiter(b);
    }
}
