// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.6.2 (simple font /FirstChar /Widths /MissingWidth),
//        §9.7.4.3 (CIDFont /DW /W), §9.7.5.2 (Identity-H)
// PHASE: 3.26 — glyph widths for text extraction, so fragment positions and
//        extents come from the font instead of a per-character estimate.

using System.Collections.Generic;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Fonts;

/// <summary>Glyph widths of a font dictionary, in thousandths of text space.</summary>
internal sealed class FontWidths
{
    private readonly Dictionary<int, double> _widths;
    private readonly double _missing;
    private readonly bool _twoByte;

    private FontWidths(Dictionary<int, double> widths, double missing, bool twoByte)
    {
        _widths = widths;
        _missing = missing;
        _twoByte = twoByte;
    }

    /// <summary>
    /// Reads the widths of a simple font with /Widths, or of a Type0 font
    /// with the Identity-H encoding. Returns null when the widths are not known
    /// (standard 14 fonts without /Widths, Type3, other CMaps).
    /// </summary>
    internal static FontWidths? FromDictionary(PdfDictionary font, IPdfObjectResolver resolver)
    {
        string subtype = font.Subtype?.Value ?? string.Empty;
        if (subtype == "Type0")
        {
            return Composite(font, resolver);
        }

        if (subtype == "Type3")
        {
            return null;
        }

        if (R(resolver, Get(font, "Widths")) is not PdfArray widths)
        {
            return null;
        }

        int first = Number(R(resolver, Get(font, "FirstChar"))) is double f ? (int)f : 0;
        Dictionary<int, double> map = new Dictionary<int, double>();
        for (int i = 0; i < widths.Count; i++)
        {
            if (Number(resolver.Resolve(widths[i])) is double w)
            {
                map[first + i] = w;
            }
        }

        double missing = 0;
        if (R(resolver, Get(font, "FontDescriptor")) is PdfDictionary descriptor &&
            Number(R(resolver, Get(descriptor, "MissingWidth"))) is double m)
        {
            missing = m;
        }

        return new FontWidths(map, missing, twoByte: false);
    }

    /// <summary>
    /// Measures a shown string: total width (thousandths of text space),
    /// the number of character codes and the number of single-byte code 32s.
    /// </summary>
    internal void Measure(byte[] bytes, out double width, out int codes, out int spaces)
    {
        width = 0;
        codes = 0;
        spaces = 0;
        if (_twoByte)
        {
            for (int i = 0; i + 1 < bytes.Length; i += 2)
            {
                int code = (bytes[i] << 8) | bytes[i + 1];
                width += _widths.TryGetValue(code, out double w) ? w : _missing;
                codes++;
            }

            return;
        }

        foreach (byte b in bytes)
        {
            width += _widths.TryGetValue(b, out double w) ? w : _missing;
            codes++;
            if (b == 32)
            {
                spaces++;
            }
        }
    }

    private static FontWidths? Composite(PdfDictionary font, IPdfObjectResolver resolver)
    {
        // Horizontal writing only: vertical advances come from /W2 and /DW2.
        if (R(resolver, Get(font, "Encoding")) is not PdfName encoding || encoding.Value != "Identity-H")
        {
            return null;
        }

        if (R(resolver, Get(font, "DescendantFonts")) is not PdfArray descendants || descendants.Count == 0 ||
            resolver.Resolve(descendants[0]) is not PdfDictionary cidFont)
        {
            return null;
        }

        double dw = Number(R(resolver, Get(cidFont, "DW"))) ?? 1000;
        Dictionary<int, double> map = new Dictionary<int, double>();
        if (R(resolver, Get(cidFont, "W")) is PdfArray w)
        {
            int i = 0;
            while (i < w.Count)
            {
                if (Number(resolver.Resolve(w[i])) is not double first)
                {
                    break;
                }

                PdfPrimitive? next = i + 1 < w.Count ? resolver.Resolve(w[i + 1]) : null;
                if (next is PdfArray list)
                {
                    for (int k = 0; k < list.Count; k++)
                    {
                        if (Number(resolver.Resolve(list[k])) is double width)
                        {
                            map[(int)first + k] = width;
                        }
                    }

                    i += 2;
                }
                else if (Number(next) is double last && i + 2 < w.Count && Number(resolver.Resolve(w[i + 2])) is double width)
                {
                    for (int c = (int)first; c <= (int)last && c - (int)first < 65536; c++)
                    {
                        map[c] = width;
                    }

                    i += 3;
                }
                else
                {
                    break;
                }
            }
        }

        return new FontWidths(map, dw, twoByte: true);
    }

    private static PdfPrimitive? R(IPdfObjectResolver resolver, PdfPrimitive? p) => p is null ? null : resolver.Resolve(p);

    private static PdfPrimitive? Get(PdfDictionary d, string key) =>
        d.TryGetValue(PdfName.Intern(key), out PdfPrimitive? v) ? v : null;

    private static double? Number(PdfPrimitive? p) => p switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => null,
    };
}
