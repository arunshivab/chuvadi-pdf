// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;

namespace Chuvadi.Pdf.Primitives;

/// <summary>
/// Represents a PDF name object (e.g. <c>/Type</c>, <c>/Page</c>).
/// Names compare by value: two names are equal when their decoded values are
/// equal (ordinal), whether or not they are the same instance.
/// PDF 32000-1:2008 §7.3.5 — Name objects.
/// </summary>
/// <remarks>
/// <para>
/// Since 3.31.2 (D-219) names are no longer kept for the life of the
/// process. The names Chuvadi itself uses, and the first 4,096 other names of
/// up to 64 characters, share one instance each, so
/// common lookups never allocate. Any other name is a new instance, freed
/// with the document that read it. Before 3.31.2 every name read from any
/// file was interned forever and compared by reference, so a server's memory
/// grew with every new name it saw.
/// </para>
/// <para>
/// Use <c>==</c>, <see cref="Equals(PdfName?)"/> or a dictionary to compare
/// names. <see cref="object.ReferenceEquals(object?, object?)"/> can be false
/// for two equal names that are not in the shared table.
/// </para>
/// </remarks>
public sealed class PdfName : PdfPrimitive, IEquatable<PdfName>
{
    // D-219: the most names, beyond the well-known ones, that are shared, and
    // the longest such name in characters.
    private const int MaxCachedNames = 4096;
    private const int MaxCachedNameLength = 64;

    // One table: the well-known names first, then up to MaxCachedNames others.
    // It never holds more than that, whatever documents are read.
    private static readonly ConcurrentDictionary<string, PdfName> s_table = CreateTable();
    private static int s_cachedCount;

    private readonly int _hash;

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
        _hash = value.GetHashCode(StringComparison.Ordinal);
    }

    /// <summary>
    /// Gets the decoded name value, without the leading solidus.
    /// For example, for the PDF token <c>/FlateDecode</c>, this is <c>"FlateDecode"</c>.
    /// </summary>
    public string Value { get; }

    /// <inheritdoc/>
    public override PdfPrimitiveType PrimitiveType => PdfPrimitiveType.Name;

    /// <summary>
    /// Returns the <see cref="PdfName"/> for the given decoded value: the
    /// shared instance when the name is well known or cached, otherwise a new
    /// instance that compares equal to every other instance with this value.
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

        if (s_table.TryGetValue(value, out PdfName? shared))
        {
            return shared;
        }

        if (value.Length <= MaxCachedNameLength && Volatile.Read(ref s_cachedCount) < MaxCachedNames)
        {
            PdfName created = new PdfName(value);
            if (s_table.TryAdd(value, created))
            {
                Interlocked.Increment(ref s_cachedCount);
                return created;
            }

            return s_table.TryGetValue(value, out shared) ? shared : created;
        }

        return new PdfName(value);
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

    // Equality is by value (3.31.2); the shared instances make the common
    // case a reference check. The hash is the ordinal hash of the value, as
    // before, computed once.

    /// <summary>Returns whether <paramref name="other"/> has the same decoded value.</summary>
    public bool Equals(PdfName? other) =>
        ReferenceEquals(this, other)
        || (other is not null && _hash == other._hash && string.Equals(Value, other.Value, StringComparison.Ordinal));

    /// <summary>Returns whether <paramref name="obj"/> is a name with the same decoded value.</summary>
    public override bool Equals(object? obj) => obj is PdfName other && Equals(other);

    /// <summary>Returns the ordinal hash of the decoded value.</summary>
    public override int GetHashCode() => _hash;

    /// <summary>Returns whether two names have the same decoded value.</summary>
    public static bool operator ==(PdfName? left, PdfName? right) => left is null ? right is null : left.Equals(right);

    /// <summary>Returns whether two names have different decoded values.</summary>
    public static bool operator !=(PdfName? left, PdfName? right) => !(left == right);

    private static ConcurrentDictionary<string, PdfName> CreateTable()
    {
        ConcurrentDictionary<string, PdfName> table = new ConcurrentDictionary<string, PdfName>(StringComparer.Ordinal);
        foreach (string name in WellKnownPdfNames.Names)
        {
            table.TryAdd(name, new PdfName(name));
        }

        return table;
    }

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
