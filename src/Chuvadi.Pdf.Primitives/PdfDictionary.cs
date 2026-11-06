// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Chuvadi.Pdf.Primitives;

/// <summary>
/// Represents a PDF dictionary object — a map from <see cref="PdfName"/> keys
/// to <see cref="PdfPrimitive"/> values.
/// PDF 32000-1:2008 §7.3.7 — Dictionary objects.
/// </summary>
public sealed class PdfDictionary : PdfPrimitive, IReadOnlyDictionary<PdfName, PdfPrimitive>
{
    private readonly Dictionary<PdfName, PdfPrimitive> _entries;

    /// <summary>Creates an empty <see cref="PdfDictionary"/>.</summary>
    public PdfDictionary()
    {
        _entries = new Dictionary<PdfName, PdfPrimitive>();
    }

    /// <summary>Creates a <see cref="PdfDictionary"/> with the given initial capacity.</summary>
    public PdfDictionary(int capacity)
    {
        _entries = new Dictionary<PdfName, PdfPrimitive>(capacity);
    }

    /// <inheritdoc/>
    public int Count => _entries.Count;

    /// <inheritdoc/>
    public PdfPrimitive this[PdfName key] => _entries[key];

    /// <inheritdoc/>
    public IEnumerable<PdfName> Keys => _entries.Keys;

    /// <inheritdoc/>
    public IEnumerable<PdfPrimitive> Values => _entries.Values;

    /// <inheritdoc/>
    public bool ContainsKey(PdfName key) => _entries.ContainsKey(key);

    /// <inheritdoc/>
    public bool TryGetValue(
        PdfName key,
        [MaybeNullWhen(false)] out PdfPrimitive value) =>
        _entries.TryGetValue(key, out value);

    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<PdfName, PdfPrimitive>> GetEnumerator() =>
        _entries.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _entries.GetEnumerator();

    /// <summary>Sets or replaces the value for <paramref name="key"/>.</summary>
    public void Set(PdfName key, PdfPrimitive value) => _entries[key] = value;

    /// <summary>Sets or replaces an integer value.</summary>
    public void Set(PdfName key, int value) => _entries[key] = new PdfInteger(value);

    /// <summary>Sets or replaces a boolean value.</summary>
    public void Set(PdfName key, bool value) => _entries[key] = PdfBoolean.FromBool(value);

    /// <summary>Removes the entry with the given key. Returns true if removed.</summary>
    public bool Remove(PdfName key) => _entries.Remove(key);

    /// <summary>Gets the value cast to <typeparamref name="T"/>, or null if absent or wrong type.</summary>
    public T? GetAs<T>(PdfName key) where T : PdfPrimitive =>
        _entries.TryGetValue(key, out PdfPrimitive? value) ? value as T : null;

    /// <summary>Gets the value as a <see cref="PdfName"/>, or null if absent.</summary>
    public PdfName? GetName(PdfName key) => GetAs<PdfName>(key);

    /// <summary>Gets the value as an integer, or <paramref name="defaultValue"/> if absent.</summary>
    public int GetInteger(PdfName key, int defaultValue = 0) =>
        GetAs<PdfInteger>(key)?.Value ?? defaultValue;

    /// <summary>
    /// Gets the value as a double. Accepts both <see cref="PdfInteger"/>
    /// and <see cref="PdfReal"/> values.
    /// </summary>
    public double GetNumber(PdfName key, double defaultValue = 0.0)
    {
        if (!_entries.TryGetValue(key, out PdfPrimitive? value))
        {
            return defaultValue;
        }

        return value switch
        {
            PdfReal r => r.Value,
            PdfInteger i => i.Value,
            _ => defaultValue
        };
    }

    /// <summary>Gets the value as a boolean, or <paramref name="defaultValue"/> if absent.</summary>
    public bool GetBoolean(PdfName key, bool defaultValue = false) =>
        GetAs<PdfBoolean>(key)?.Value ?? defaultValue;

    /// <summary>Gets the value as a <see cref="PdfDictionary"/>, or null if absent.</summary>
    public PdfDictionary? GetDictionary(PdfName key) => GetAs<PdfDictionary>(key);

    /// <summary>Gets the value as a <see cref="PdfArray"/>, or null if absent.</summary>
    public PdfArray? GetArray(PdfName key) => GetAs<PdfArray>(key);

    /// <summary>Gets the <c>/Type</c> entry, or null if absent.</summary>
    public PdfName? Type => GetName(PdfName.Type);

    /// <summary>Gets the <c>/Subtype</c> entry, or null if absent.</summary>
    public PdfName? Subtype => GetName(PdfName.Subtype);

    /// <inheritdoc/>
    public override PdfPrimitiveType PrimitiveType => PdfPrimitiveType.Dictionary;

    /// <summary>Returns the PDF syntax representation.</summary>
    public override string ToString()
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("<< ");

        foreach (KeyValuePair<PdfName, PdfPrimitive> entry in _entries)
        {
            sb.Append(entry.Key);
            sb.Append(' ');
            sb.Append(entry.Value);
            sb.Append(' ');
        }

        sb.Append(">>");
        return sb.ToString();
    }
}
