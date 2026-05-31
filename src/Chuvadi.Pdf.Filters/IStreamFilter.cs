// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.4 — Filters
// PHASE: Phase 1 — Chuvadi.Pdf.Filters
// Contract implemented by every PDF stream filter in Chuvadi.

using System.IO;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Filters;

/// <summary>
/// Defines the contract for a PDF stream filter.
/// </summary>
/// <remarks>
/// PDF streams may be compressed or encoded using one or more filters
/// specified in the stream dictionary's <c>/Filter</c> entry.
/// Each filter implementation handles one filter name.
///
/// Filters are applied in sequence when decoding (Decode removes the filter)
/// and in reverse sequence when encoding (Encode applies the filter).
///
/// PDF 32000-1:2008 §7.4 — Filters.
/// </remarks>
public interface IStreamFilter
{
    /// <summary>
    /// Gets the PDF filter name this implementation handles,
    /// e.g. <c>FlateDecode</c>, <c>ASCII85Decode</c>.
    /// This matches the value of the <c>/Filter</c> entry without the leading slash.
    /// </summary>
    string FilterName { get; }

    /// <summary>
    /// Decodes (decompresses/decodes) data from <paramref name="input"/>
    /// and writes the result to <paramref name="output"/>.
    /// </summary>
    /// <param name="input">
    /// The encoded/compressed source stream. Read from current position.
    /// </param>
    /// <param name="output">
    /// The stream to write decoded bytes to.
    /// </param>
    /// <param name="decodeParms">
    /// Optional filter parameters from the stream dictionary's
    /// <c>/DecodeParms</c> entry. May be null when no parameters are present.
    /// </param>
    /// <exception cref="FilterException">
    /// Thrown when the encoded data is malformed or truncated.
    /// </exception>
    void Decode(Stream input, Stream output, FilterParameters? decodeParms = null);

    /// <summary>
    /// Encodes (compresses/encodes) data from <paramref name="input"/>
    /// and writes the result to <paramref name="output"/>.
    /// </summary>
    /// <param name="input">
    /// The raw source stream. Read from current position.
    /// </param>
    /// <param name="output">
    /// The stream to write encoded bytes to.
    /// </param>
    /// <param name="encodeParms">
    /// Optional encoding parameters. May be null to use filter defaults.
    /// </param>
    void Encode(Stream input, Stream output, FilterParameters? encodeParms = null);
}

/// <summary>
/// Parameters passed to a filter's Decode or Encode operation,
/// derived from the <c>/DecodeParms</c> or <c>/EncodeParms</c>
/// dictionary in the stream dictionary.
/// </summary>
/// <remarks>
/// Different filters use different parameters. This record carries
/// the subset of parameters Chuvadi supports in Phase 1.
/// PDF 32000-1:2008 §7.4.4.3 — FlateDecode parameters (Predictor etc.)
/// </remarks>
public sealed record FilterParameters
{
    /// <summary>
    /// For FlateDecode: the predictor algorithm applied before compression.
    /// 1 = no predictor (default), 2 = TIFF predictor,
    /// 10-15 = PNG predictors (most common in modern PDFs).
    /// PDF 32000-1:2008 Table 8.
    /// </summary>
    public int Predictor { get; init; } = 1;

    /// <summary>
    /// For PNG predictors (Predictor 10-15): number of color components per pixel.
    /// Default is 1.
    /// </summary>
    public int Colors { get; init; } = 1;

    /// <summary>
    /// For PNG predictors: number of bits per color component.
    /// Default is 8.
    /// </summary>
    public int BitsPerComponent { get; init; } = 8;

    /// <summary>
    /// For PNG predictors: number of pixels (columns) per row.
    /// Must be set when a PNG predictor is used.
    /// </summary>
    public int Columns { get; init; } = 1;

    /// <summary>
    /// For LZW: early change flag.
    /// 0 = compatible with original LZW; 1 = early change (PDF default).
    /// </summary>
    public int EarlyChange { get; init; } = 1;

    /// <summary>
    /// Builds <see cref="FilterParameters"/> from a <c>/DecodeParms</c> (or
    /// <c>/DecodeParams</c>) value taken from a stream dictionary.
    /// </summary>
    /// <param name="decodeParms">
    /// The raw <c>/DecodeParms</c> primitive. May be a single
    /// <see cref="PdfDictionary"/> (applies to the sole or
    /// <paramref name="filterIndex"/>-th filter), a <see cref="PdfArray"/> of
    /// per-filter dictionaries (possibly containing nulls), or null when the
    /// stream has no <c>/DecodeParms</c> entry.
    /// </param>
    /// <param name="filterIndex">
    /// Zero-based index of the filter these parameters apply to, used to
    /// select the entry when <paramref name="decodeParms"/> is an array.
    /// Defaults to 0 for the single-filter case.
    /// </param>
    /// <returns>
    /// A populated <see cref="FilterParameters"/>, or null when there are no
    /// parameters for the given filter (no <c>/DecodeParms</c>, a null array
    /// entry, or a non-dictionary value).
    /// </returns>
    public static FilterParameters? FromDictionary(PdfPrimitive? decodeParms, int filterIndex = 0)
    {
        PdfDictionary? parmsDict = decodeParms switch
        {
            PdfDictionary single => single,
            PdfArray array => array.GetAs<PdfDictionary>(filterIndex),
            _ => null,
        };

        if (parmsDict is null)
        {
            return null;
        }

        int predictor = ReadInt(parmsDict, "Predictor", 1);
        int columns = ReadInt(parmsDict, "Columns", 1);
        int colors = ReadInt(parmsDict, "Colors", 1);
        int bitsPerComponent = ReadInt(parmsDict, "BitsPerComponent", 8);
        int earlyChange = ReadInt(parmsDict, "EarlyChange", 1);

        return new FilterParameters
        {
            Predictor = predictor,
            Columns = columns,
            Colors = colors,
            BitsPerComponent = bitsPerComponent,
            EarlyChange = earlyChange,
        };
    }

    private static int ReadInt(PdfDictionary dict, string key, int fallback)
    {
        return dict.TryGetValue(PdfName.Intern(key), out PdfPrimitive? value)
            && value is PdfInteger integer
            ? integer.Value
            : fallback;
    }
}
