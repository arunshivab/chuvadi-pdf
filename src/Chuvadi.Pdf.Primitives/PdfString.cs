// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Text;

namespace Chuvadi.Pdf.Primitives;

/// <summary>
/// Represents a PDF string object.
/// A PDF string is a sequence of bytes, not necessarily valid Unicode.
/// Serialised as literal <c>(Hello)</c> or hex <c>&lt;48656C6C6F&gt;</c> form.
/// PDF 32000-1:2008 §7.3.4 — String objects.
/// </summary>
public sealed class PdfString : PdfPrimitive, IEquatable<PdfString>
{
    /// <summary>The empty PDF string.</summary>
    public static readonly PdfString Empty = new([], false);

    /// <summary>
    /// Initialises a new <see cref="PdfString"/> with the given raw bytes.
    /// </summary>
    /// <param name="bytes">The raw byte content. A copy is taken.</param>
    /// <param name="preferHexForm">True to serialise in hex form; false for literal form.</param>
    public PdfString(ReadOnlySpan<byte> bytes, bool preferHexForm = false)
    {
        Bytes = bytes.ToArray();
        PreferHexForm = preferHexForm;
    }

    /// <summary>
    /// Initialises a new <see cref="PdfString"/> from a .NET string,
    /// encoded as Latin-1 (PDFDocEncoding for ASCII range).
    /// </summary>
    public PdfString(string value, bool preferHexForm = false)
        : this(Encoding.Latin1.GetBytes(value), preferHexForm)
    {
    }

    /// <summary>Gets the raw byte content of this string.</summary>
    public byte[] Bytes { get; }

    /// <summary>True if this string prefers hex serialisation form.</summary>
    public bool PreferHexForm { get; }

    /// <summary>Gets the length of the string in bytes.</summary>
    public int Length => Bytes.Length;

    /// <inheritdoc/>
    public override PdfPrimitiveType PrimitiveType => PdfPrimitiveType.String;

    /// <summary>
    /// Decodes this PDF string as a text string.
    /// Uses UTF-16BE if the bytes begin with BOM 0xFE 0xFF,
    /// UTF-16LE if they begin with 0xFF 0xFE,
    /// UTF-8 if they begin with 0xEF 0xBB 0xBF,
    /// or PDFDocEncoding (Latin-1) otherwise.
    /// </summary>
    public string ToTextString()
    {
        if (Bytes.Length >= 3 && Bytes[0] == 0xEF && Bytes[1] == 0xBB && Bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(Bytes, 3, Bytes.Length - 3);
        }

        if (Bytes.Length >= 2)
        {
            if (Bytes[0] == 0xFE && Bytes[1] == 0xFF)
            {
                return Encoding.BigEndianUnicode.GetString(Bytes, 2, Bytes.Length - 2);
            }

            if (Bytes[0] == 0xFF && Bytes[1] == 0xFE)
            {
                return Encoding.Unicode.GetString(Bytes, 2, Bytes.Length - 2);
            }
        }

        return Encoding.Latin1.GetString(Bytes);
    }

    /// <summary>
    /// Two strings are equal when their byte contents are identical.
    /// Serialisation form is not considered.
    /// </summary>
    public bool Equals(PdfString? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return Bytes.AsSpan().SequenceEqual(other.Bytes);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as PdfString);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        HashCode hash = new HashCode();
        hash.AddBytes(Bytes);
        return hash.ToHashCode();
    }

    /// <inheritdoc/>
    public override string ToString() => PreferHexForm ? ToHexForm() : ToLiteralForm();

    private string ToHexForm()
    {
        StringBuilder sb = new StringBuilder(Bytes.Length * 2 + 2);
        sb.Append('<');

        foreach (byte b in Bytes)
        {
            sb.Append(b.ToString("X2"));
        }

        sb.Append('>');
        return sb.ToString();
    }

    private string ToLiteralForm()
    {
        // PDF 32000-1:2008 §7.3.4.2 — escape sequences for literal strings.
        StringBuilder sb = new StringBuilder(Bytes.Length + 2);
        sb.Append('(');

        foreach (byte b in Bytes)
        {
            switch (b)
            {
                case 0x0A: sb.Append("\\n"); break;
                case 0x0D: sb.Append("\\r"); break;
                case 0x09: sb.Append("\\t"); break;
                case 0x08: sb.Append("\\b"); break;
                case 0x0C: sb.Append("\\f"); break;
                case (byte)'(': sb.Append("\\("); break;
                case (byte)')': sb.Append("\\)"); break;
                case (byte)'\\': sb.Append("\\\\"); break;
                default: sb.Append((char)b); break;
            }
        }

        sb.Append(')');
        return sb.ToString();
    }

    /// <summary>
    /// Creates a <see cref="PdfString"/> from a .NET string encoded as
    /// UTF-16BE with BOM for correct round-trip of non-Latin characters.
    /// </summary>
    public static PdfString FromUnicode(string value)
    {
        byte[] utf16 = Encoding.BigEndianUnicode.GetBytes(value);
        byte[] withBom = new byte[utf16.Length + 2];
        withBom[0] = 0xFE;
        withBom[1] = 0xFF;
        utf16.CopyTo(withBom, 2);
        return new PdfString(withBom, preferHexForm: true);
    }

    /// <summary>
    /// Decodes a literal string token's raw bytes into string content bytes,
    /// resolving every escape defined by PDF 32000-1:2008 §7.3.4.2.
    /// </summary>
    /// <remarks>
    /// Accepts the raw token with or without the wrapping parentheses.
    /// Handles: <c>\n \r \t \b \f \( \) \\</c>; one-to-three-digit octal
    /// escapes (high-order overflow ignored per spec); a reverse solidus at
    /// end-of-line as a line continuation (the solidus and the end-of-line
    /// marker are both dropped, CR LF counting as one marker); a reverse
    /// solidus before any other character is dropped, keeping the character;
    /// and an unescaped end-of-line marker inside the string is normalized to
    /// a single LF (0x0A) byte.
    /// This is the single authoritative decoder — the object parser, the
    /// content-stream parser, and the content-stream walker all delegate here
    /// so a string decodes identically on every path.
    /// </remarks>
    /// <param name="raw">The token bytes, optionally including the parentheses.</param>
    /// <returns>The decoded string content bytes.</returns>
    public static byte[] DecodeLiteralToken(ReadOnlySpan<byte> raw)
    {
        int start = (raw.Length > 0 && raw[0] == (byte)'(') ? 1 : 0;
        int end = (raw.Length > start && raw[raw.Length - 1] == (byte)')')
            ? raw.Length - 1
            : raw.Length;

        System.Collections.Generic.List<byte> decoded =
            new System.Collections.Generic.List<byte>(end - start);
        int i = start;

        while (i < end)
        {
            byte b = raw[i];

            if (b == (byte)'\\' && i + 1 < end)
            {
                i++;
                byte escaped = raw[i];

                switch (escaped)
                {
                    case (byte)'n': decoded.Add(0x0A); i++; break;
                    case (byte)'r': decoded.Add(0x0D); i++; break;
                    case (byte)'t': decoded.Add(0x09); i++; break;
                    case (byte)'b': decoded.Add(0x08); i++; break;
                    case (byte)'f': decoded.Add(0x0C); i++; break;
                    case (byte)'(': decoded.Add((byte)'('); i++; break;
                    case (byte)')': decoded.Add((byte)')'); i++; break;
                    case (byte)'\\': decoded.Add((byte)'\\'); i++; break;
                    case 0x0D:
                        // Line continuation: \CR or \CRLF — drop both.
                        i++;
                        if (i < end && raw[i] == 0x0A)
                        {
                            i++;
                        }

                        break;
                    case 0x0A:
                        // Line continuation: \LF — drop both.
                        i++;
                        break;
                    default:
                        if (escaped >= (byte)'0' && escaped <= (byte)'7')
                        {
                            // Octal escape: one to three digits (§7.3.4.2).
                            int value = 0;
                            int digits = 0;

                            while (digits < 3 && i < end
                                && raw[i] >= (byte)'0' && raw[i] <= (byte)'7')
                            {
                                value = (value << 3) | (raw[i] - (byte)'0');
                                i++;
                                digits++;
                            }

                            decoded.Add((byte)(value & 0xFF));
                        }
                        else
                        {
                            // Unknown escape: the reverse solidus is ignored.
                            decoded.Add(escaped);
                            i++;
                        }

                        break;
                }
            }
            else if (b == 0x0D)
            {
                // Unescaped end-of-line inside a string: CR or CRLF reads as
                // a single LF byte (§7.3.4.2).
                decoded.Add(0x0A);
                i++;

                if (i < end && raw[i] == 0x0A)
                {
                    i++;
                }
            }
            else
            {
                decoded.Add(b);
                i++;
            }
        }

        return decoded.ToArray();
    }

    /// <summary>
    /// Decodes a hexadecimal string token's raw bytes into string content
    /// bytes per PDF 32000-1:2008 §7.3.4.3. Accepts the raw token with or
    /// without the wrapping angle brackets; whitespace between digits is
    /// ignored and a trailing odd digit is padded with zero.
    /// </summary>
    /// <param name="raw">The token bytes, optionally including the brackets.</param>
    /// <returns>The decoded string content bytes.</returns>
    public static byte[] DecodeHexToken(ReadOnlySpan<byte> raw)
    {
        int start = (raw.Length > 0 && raw[0] == (byte)'<') ? 1 : 0;
        int end = (raw.Length > start && raw[raw.Length - 1] == (byte)'>')
            ? raw.Length - 1
            : raw.Length;

        System.Collections.Generic.List<byte> decoded =
            new System.Collections.Generic.List<byte>((end - start + 1) / 2);
        int highNibble = -1;

        for (int i = start; i < end; i++)
        {
            int nibble = HexNibble(raw[i]);

            if (nibble < 0)
            {
                continue;
            }

            if (highNibble < 0)
            {
                highNibble = nibble;
            }
            else
            {
                decoded.Add((byte)((highNibble << 4) | nibble));
                highNibble = -1;
            }
        }

        if (highNibble >= 0)
        {
            decoded.Add((byte)(highNibble << 4));
        }

        return decoded.ToArray();
    }

    private static int HexNibble(byte b)
    {
        if (b >= (byte)'0' && b <= (byte)'9')
        {
            return b - (byte)'0';
        }

        if (b >= (byte)'A' && b <= (byte)'F')
        {
            return b - (byte)'A' + 10;
        }

        if (b >= (byte)'a' && b <= (byte)'f')
        {
            return b - (byte)'a' + 10;
        }

        return -1;
    }

    /// <summary>Implicit conversion from a .NET string using Latin-1 encoding.</summary>
    public static implicit operator PdfString(string value) => new(value);
}
