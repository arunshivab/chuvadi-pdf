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

    /// <summary>Implicit conversion from a .NET string using Latin-1 encoding.</summary>
    public static implicit operator PdfString(string value) => new(value);
}
