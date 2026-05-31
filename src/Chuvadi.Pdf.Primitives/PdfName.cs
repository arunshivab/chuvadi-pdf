// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Concurrent;
using System.Text;

namespace Chuvadi.Pdf.Primitives;

/// <summary>
/// Represents a PDF name object (e.g. <c>/Type</c>, <c>/Page</c>).
/// All instances are interned — the same name string always returns the
/// same <see cref="PdfName"/> instance, making equality checks allocation-free.
/// PDF 32000-1:2008 §7.3.5 — Name objects.
/// </summary>
public sealed class PdfName : PdfPrimitive, IEquatable<PdfName>
{
    private static readonly ConcurrentDictionary<string, PdfName> s_intern =
        new(StringComparer.Ordinal);

    // Common names cached as static fields for zero-allocation access.
    public static readonly PdfName Type = Intern("Type");
    public static readonly PdfName Subtype = Intern("Subtype");
    public static readonly PdfName Page = Intern("Page");
    public static readonly PdfName Pages = Intern("Pages");
    public static readonly PdfName Catalog = Intern("Catalog");
    public static readonly PdfName Kids = Intern("Kids");
    public static readonly PdfName Parent = Intern("Parent");
    public static readonly PdfName Count = Intern("Count");
    public static readonly PdfName Contents = Intern("Contents");
    public static readonly PdfName Resources = Intern("Resources");
    public static readonly PdfName MediaBox = Intern("MediaBox");
    public static readonly PdfName CropBox = Intern("CropBox");
    public static readonly PdfName Rotate = Intern("Rotate");
    public static readonly PdfName Filter = Intern("Filter");
    public static readonly PdfName Length = Intern("Length");
    public static readonly PdfName FlateDecode = Intern("FlateDecode");
    public static readonly PdfName Font = Intern("Font");
    public static readonly PdfName XObject = Intern("XObject");
    public static readonly PdfName Outlines = Intern("Outlines");
    public static readonly PdfName Info = Intern("Info");
    public static readonly PdfName Root = Intern("Root");
    public static readonly PdfName Size = Intern("Size");
    public static readonly PdfName Prev = Intern("Prev");

    private PdfName(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Gets the decoded name value, without the leading solidus.
    /// For example, for the PDF token <c>/FlateDecode</c>, this is <c>"FlateDecode"</c>.
    /// </summary>
    public string Value { get; }

    /// <inheritdoc/>
    public override PdfPrimitiveType PrimitiveType => PdfPrimitiveType.Name;

    /// <summary>
    /// Returns the interned <see cref="PdfName"/> for the given decoded value.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="value"/> is null or empty.
    /// </exception>
    public static PdfName Intern(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException("PDF name must not be null or empty.", nameof(value));
        }

        return s_intern.GetOrAdd(value, static v => new PdfName(v));
    }

    /// <summary>
    /// Parses and interns a <see cref="PdfName"/> from raw PDF bytes,
    /// decoding <c>#XX</c> escape sequences.
    /// </summary>
    /// <param name="rawBytes">
    /// The raw name bytes from the content stream or object parser, excluding
    /// the leading solidus.
    /// </param>
    /// <returns>The interned <see cref="PdfName"/> for the decoded value.</returns>
    /// <exception cref="PdfParseException">
    /// Thrown when <paramref name="rawBytes"/> decodes to an empty name. An
    /// empty name is not valid PDF syntax; because this method is the parser
    /// entry point for untrusted document bytes, the failure is surfaced as a
    /// catchable parse error rather than the argument-validation exception
    /// thrown by <see cref="Intern(string)"/>.
    /// </exception>
    public static PdfName FromRawBytes(ReadOnlySpan<byte> rawBytes)
    {
        bool needsDecode = false;

        foreach (byte b in rawBytes)
        {
            if (b == (byte)'#')
            {
                needsDecode = true;
                break;
            }
        }

        string decoded = needsDecode
            ? DecodeNameBytes(rawBytes)
            : Encoding.Latin1.GetString(rawBytes);

        if (string.IsNullOrEmpty(decoded))
        {
            throw new PdfParseException("PDF name token decoded to an empty name, which is not valid PDF syntax.");
        }

        return Intern(decoded);
    }

    // Equality: reference equality is correct because all names are interned.
    public bool Equals(PdfName? other) => ReferenceEquals(this, other);
    public override bool Equals(object? obj) => ReferenceEquals(this, obj);
    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);
    public static bool operator ==(PdfName? left, PdfName? right) => ReferenceEquals(left, right);
    public static bool operator !=(PdfName? left, PdfName? right) => !ReferenceEquals(left, right);

    /// <summary>
    /// Returns the PDF syntax representation including the leading solidus,
    /// e.g. <c>/FlateDecode</c>. Characters requiring encoding are written as <c>#XX</c>.
    /// </summary>
    public override string ToString()
    {
        StringBuilder sb = new StringBuilder(Value.Length + 1);
        sb.Append('/');

        foreach (char c in Value)
        {
            if (c == '#' || c < 0x21 || c > 0x7E)
            {
                sb.Append('#');
                sb.Append(((int)c).ToString("X2"));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    /// <summary>Implicit conversion from string — interns the name.</summary>
    public static implicit operator PdfName(string value) => Intern(value);

    /// <summary>Implicit conversion to string — returns <see cref="Value"/>.</summary>
    public static implicit operator string(PdfName name)
    {
        if (name is null)
        {
            throw new ArgumentNullException(nameof(name));
        }

        return name.Value;
    }

    private static string DecodeNameBytes(ReadOnlySpan<byte> raw)
    {
        StringBuilder sb = new StringBuilder(raw.Length);
        int i = 0;

        while (i < raw.Length)
        {
            byte b = raw[i];

            if (b == (byte)'#' && i + 2 < raw.Length)
            {
                byte high = raw[i + 1];
                byte low = raw[i + 2];

                if (IsHexDigit(high) && IsHexDigit(low))
                {
                    sb.Append((char)((HexValue(high) << 4) | HexValue(low)));
                    i += 3;
                    continue;
                }
            }

            sb.Append((char)b);
            i++;
        }

        return sb.ToString();
    }

    private static bool IsHexDigit(byte b) =>
        (b >= '0' && b <= '9') ||
        (b >= 'A' && b <= 'F') ||
        (b >= 'a' && b <= 'f');

    private static int HexValue(byte b) => b switch
    {
        >= (byte)'0' and <= (byte)'9' => b - '0',
        >= (byte)'A' and <= (byte)'F' => b - 'A' + 10,
        _ => b - 'a' + 10
    };
}
