// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1 §12.7.4.5 — Signature fields; §12.8 — Digital Signatures
// PHASE: Phase 1.1.4 — PDF signature field reading

using System;
using System.Collections.Generic;
using System.Globalization;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Signatures;

/// <summary>
/// Reads digital-signature fields out of a PDF document's AcroForm tree.
/// </summary>
public static class SignatureReader
{
    private static readonly PdfName NameAcroForm = PdfName.Intern("AcroForm");
    private static readonly PdfName NameFields = PdfName.Intern("Fields");
    private static readonly PdfName NameFT = PdfName.Intern("FT");
    private static readonly PdfName NameKids = PdfName.Intern("Kids");
    private static readonly PdfName NameT = PdfName.Intern("T");
    private static readonly PdfName NameV = PdfName.Intern("V");
    private static readonly PdfName NameFilter = PdfName.Intern("Filter");
    private static readonly PdfName NameSubFilter = PdfName.Intern("SubFilter");
    private static readonly PdfName NameByteRange = PdfName.Intern("ByteRange");
    private static readonly PdfName NameContents = PdfName.Intern("Contents");
    private static readonly PdfName NameM = PdfName.Intern("M");
    private static readonly PdfName NameNameAttr = PdfName.Intern("Name");
    private static readonly PdfName NameReason = PdfName.Intern("Reason");
    private static readonly PdfName NameLocation = PdfName.Intern("Location");
    private static readonly PdfName NameContactInfo = PdfName.Intern("ContactInfo");

    private static T? ResolveEntry<T>(IPdfObjectResolver resolver, PdfDictionary dict, PdfName key)
        where T : PdfPrimitive
    {
        if (!dict.TryGetValue(key, out PdfPrimitive? raw)) { return null; }
        PdfPrimitive resolved = resolver.Resolve(raw);
        return resolved as T;
    }

    /// <summary>
    /// Walks the AcroForm tree under <paramref name="catalog"/> and returns one
    /// <see cref="PdfSignature"/> per signature field that has a value.
    /// </summary>
    /// <param name="catalog">The document catalog dictionary.</param>
    /// <param name="resolver">Object resolver for indirect references.</param>
    /// <returns>The signatures in field order; empty when the document has none.</returns>
    public static IReadOnlyList<PdfSignature> Read(PdfDictionary catalog, IPdfObjectResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(resolver);

        List<PdfSignature> signatures = new();

        PdfDictionary? acroForm = ResolveEntry<PdfDictionary>(resolver, catalog, NameAcroForm);
        if (acroForm is null) { return signatures; }

        PdfArray? fields = ResolveEntry<PdfArray>(resolver, acroForm, NameFields);
        if (fields is null) { return signatures; }

        WalkFieldArray(fields, resolver, signatures, parentName: null);
        return signatures;
    }

    // Deepest nesting followed (3.35.0, DOC-01).
    private const int MaxFieldDepth = 1024;

    private static void WalkFieldArray(
        PdfArray fields,
        IPdfObjectResolver resolver,
        List<PdfSignature> output,
        string? parentName,
        int depth = 0)
    {
        // 3.35.0 (DOC-01): nesting deeper than MaxFieldDepth is not followed.
        if (depth > MaxFieldDepth)
        {
            return;
        }

        foreach (PdfPrimitive entry in fields)
        {
            PdfPrimitive resolvedEntry = resolver.Resolve(entry);
            if (resolvedEntry is not PdfDictionary fieldDict) { continue; }

            string? localName = ResolveText(resolver, fieldDict, NameT);
            string? qualifiedName = JoinFieldName(parentName, localName);

            // Recurse into /Kids to find nested fields.
            PdfArray? kids = ResolveEntry<PdfArray>(resolver, fieldDict, NameKids);
            if (kids is not null && kids.Count > 0 && LooksLikeFieldNode(resolver, kids))
            {
                WalkFieldArray(kids, resolver, output, qualifiedName, depth + 1);
            }

            // Lift if this is a signature field with a value.
            PdfName? ft = ResolveEntry<PdfName>(resolver, fieldDict, NameFT);
            if (ft is null || ft.Value != "Sig") { continue; }

            PdfDictionary? sigDict = ResolveEntry<PdfDictionary>(resolver, fieldDict, NameV);
            if (sigDict is null) { continue; }

            PdfSignature? signature = TryLiftSignature(qualifiedName ?? string.Empty, sigDict, resolver);
            if (signature is not null) { output.Add(signature); }
        }
    }

    /// <summary>True when the kids array's first element looks like a form-field dict (has /T or /FT or /Kids).</summary>
    private static bool LooksLikeFieldNode(IPdfObjectResolver resolver, PdfArray kids)
    {
        PdfPrimitive first = resolver.Resolve(kids[0]);
        if (first is not PdfDictionary d) { return false; }
        return d.TryGetValue(NameT, out _)
            || d.TryGetValue(NameFT, out _)
            || d.TryGetValue(NameKids, out _);
    }

    private static PdfSignature? TryLiftSignature(
        string fieldName,
        PdfDictionary sigDict,
        IPdfObjectResolver resolver)
    {
        // /ByteRange is mandatory for any usable signature.
        PdfArray? byteRangeArr = ResolveEntry<PdfArray>(resolver, sigDict, NameByteRange);
        if (byteRangeArr is null || byteRangeArr.Count != 4) { return null; }

        long a = ReadLong(resolver, byteRangeArr[0]);
        long b = ReadLong(resolver, byteRangeArr[1]);
        long c = ReadLong(resolver, byteRangeArr[2]);
        long d = ReadLong(resolver, byteRangeArr[3]);

        ByteRange byteRange;
        try
        {
            byteRange = new ByteRange(a, b, c, d);
        }
        catch (ArgumentException)
        {
            return null;
        }

        // /Contents — mandatory; the raw cryptographic envelope bytes.
        PdfPrimitive contentsObj = resolver.Resolve(sigDict.GetAs<PdfPrimitive>(NameContents) ?? PdfNull.Value);
        if (contentsObj is not PdfString contentsStr) { return null; }

        string? filter = ResolveName(resolver, sigDict, NameFilter);
        string? subFilter = ResolveName(resolver, sigDict, NameSubFilter);

        string? name = ResolveText(resolver, sigDict, NameNameAttr);
        string? reason = ResolveText(resolver, sigDict, NameReason);
        string? location = ResolveText(resolver, sigDict, NameLocation);
        string? contactInfo = ResolveText(resolver, sigDict, NameContactInfo);

        DateTimeOffset? mDate = TryParseDate(ResolveText(resolver, sigDict, NameM));

        PdfName? type = ResolveEntry<PdfName>(resolver, sigDict, PdfName.Type);
        bool isDts = type is not null && type.Value == "DocTimeStamp";

        return new PdfSignature(
            fieldName,
            filter,
            subFilter,
            byteRange,
            contentsStr.Bytes,
            name,
            reason,
            location,
            contactInfo,
            mDate,
            isDts);
    }

    private static long ReadLong(IPdfObjectResolver resolver, PdfPrimitive primitive)
    {
        PdfPrimitive r = resolver.Resolve(primitive);
        if (r is PdfInteger i) { return i.Value; }
        if (r is PdfReal real) { return (long)real.Value; }
        throw new InvalidOperationException(
            $"ByteRange element is not a numeric value: {r.PrimitiveType}");
    }

    private static string? ResolveName(IPdfObjectResolver resolver, PdfDictionary dict, PdfName key)
    {
        PdfName? n = ResolveEntry<PdfName>(resolver, dict, key);
        return n?.Value;
    }

    private static string? ResolveText(IPdfObjectResolver resolver, PdfDictionary dict, PdfName key)
    {
        if (!dict.TryGetValue(key, out PdfPrimitive? raw)) { return null; }
        PdfPrimitive r = resolver.Resolve(raw);
        if (r is PdfString s) { return s.ToTextString(); }
        return null;
    }

    private static string? JoinFieldName(string? parent, string? local)
    {
        if (string.IsNullOrEmpty(local)) { return parent; }
        if (string.IsNullOrEmpty(parent)) { return local; }
        return $"{parent}.{local}";
    }

    /// <summary>
    /// Parses a PDF date string (D:YYYYMMDDHHmmSSOHH'mm') into a DateTimeOffset.
    /// PDF 32000-1 §7.9.4. Returns null on malformed input.
    /// </summary>
    private static DateTimeOffset? TryParseDate(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) { return null; }
        string s = raw;
        if (s.StartsWith("D:", StringComparison.Ordinal)) { s = s.Substring(2); }
        if (s.Length < 4) { return null; }

        try
        {
            int year = int.Parse(s.AsSpan(0, 4), CultureInfo.InvariantCulture);
            int month = s.Length >= 6 ? int.Parse(s.AsSpan(4, 2), CultureInfo.InvariantCulture) : 1;
            int day = s.Length >= 8 ? int.Parse(s.AsSpan(6, 2), CultureInfo.InvariantCulture) : 1;
            int hour = s.Length >= 10 ? int.Parse(s.AsSpan(8, 2), CultureInfo.InvariantCulture) : 0;
            int minute = s.Length >= 12 ? int.Parse(s.AsSpan(10, 2), CultureInfo.InvariantCulture) : 0;
            int second = s.Length >= 14 ? int.Parse(s.AsSpan(12, 2), CultureInfo.InvariantCulture) : 0;

            TimeSpan offset = TimeSpan.Zero;
            int pos = 14;
            if (pos < s.Length)
            {
                char sign = s[pos];
                if (sign == 'Z')
                {
                    offset = TimeSpan.Zero;
                }
                else if ((sign == '+' || sign == '-') && pos + 3 <= s.Length)
                {
                    int sgn = sign == '+' ? 1 : -1;
                    int oh = int.Parse(s.AsSpan(pos + 1, 2), CultureInfo.InvariantCulture);
                    int om = 0;
                    // After HH there may be ' before MM, then trailing '.
                    // Tolerate both "HH'mm'" and "HHmm".
                    int afterHh = pos + 3;
                    if (afterHh < s.Length && s[afterHh] == '\'') { afterHh++; }
                    if (afterHh + 2 <= s.Length && char.IsDigit(s[afterHh]))
                    {
                        om = int.Parse(s.AsSpan(afterHh, 2), CultureInfo.InvariantCulture);
                    }
                    offset = new TimeSpan(sgn * oh, sgn * om, 0);
                }
            }

            return new DateTimeOffset(year, month, day, hour, minute, second, offset);
        }
        catch (FormatException) { return null; }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    /// <summary>
    /// Builds the contiguous byte sequence covered by <paramref name="byteRange"/>
    /// from <paramref name="fileBytes"/>.
    /// </summary>
    public static byte[] ExtractSignedBytes(byte[] fileBytes, ByteRange byteRange)
    {
        ArgumentNullException.ThrowIfNull(fileBytes);
        ArgumentNullException.ThrowIfNull(byteRange);

        long totalLong = byteRange.TotalLength;
        if (totalLong > int.MaxValue)
        {
            throw new InvalidOperationException(
                "Signed byte range exceeds Int32.MaxValue; use the stream-based overload.");
        }

        int total = (int)totalLong;
        byte[] result = new byte[total];

        if (byteRange.FirstOffset + byteRange.FirstLength > fileBytes.Length)
        {
            throw new ArgumentException("ByteRange first region extends past file bytes.", nameof(fileBytes));
        }
        if (byteRange.SecondOffset + byteRange.SecondLength > fileBytes.Length)
        {
            throw new ArgumentException("ByteRange second region extends past file bytes.", nameof(fileBytes));
        }

        Buffer.BlockCopy(fileBytes, (int)byteRange.FirstOffset, result, 0, (int)byteRange.FirstLength);
        Buffer.BlockCopy(fileBytes, (int)byteRange.SecondOffset, result,
            (int)byteRange.FirstLength, (int)byteRange.SecondLength);
        return result;
    }

    /// <summary>
    /// Streams the bytes covered by <paramref name="byteRange"/> from
    /// <paramref name="source"/> into <paramref name="destination"/>.
    /// </summary>
    /// <remarks>
    /// Use this overload for files larger than 2 GiB or when the caller wants
    /// to feed a hash function incrementally rather than materialising the
    /// signed bytes as a single array.
    /// </remarks>
    public static void WriteSignedBytes(System.IO.Stream source, ByteRange byteRange, System.IO.Stream destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(byteRange);
        ArgumentNullException.ThrowIfNull(destination);

        CopyRange(source, destination, byteRange.FirstOffset, byteRange.FirstLength);
        CopyRange(source, destination, byteRange.SecondOffset, byteRange.SecondLength);
    }

    private static void CopyRange(System.IO.Stream source, System.IO.Stream destination, long offset, long length)
    {
        source.Position = offset;
        byte[] buffer = new byte[8192];
        long remaining = length;
        while (remaining > 0)
        {
            int want = (int)Math.Min(remaining, buffer.Length);
            int read = source.Read(buffer, 0, want);
            if (read <= 0)
            {
                throw new System.IO.EndOfStreamException(
                    "Unexpected end of stream while copying signed byte range.");
            }
            destination.Write(buffer, 0, read);
            remaining -= read;
        }
    }
}
