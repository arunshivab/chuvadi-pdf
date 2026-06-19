// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.6.2 (Simple font widths), §9.7.4 (CID font widths)
// PHASE: Phase 2.1 — glyph-level text positioning

using System.Collections.Generic;
using Chuvadi.Pdf.Fonts.Rendering;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>
/// Resolves per-glyph advance widths from a PDF font dictionary's /Widths
/// (simple fonts) or /W (CID fonts) array. Widths are in font units; the
/// PDF spec convention is 1000ths of an em (so a width of 500 = 0.5em).
/// </summary>
internal sealed class FontWidths
{
    private readonly Dictionary<int, double> _widths = new();
    private readonly double _defaultWidth;
    private readonly bool _isComposite;

    private FontWidths(double defaultWidth, bool isComposite)
    {
        _defaultWidth = defaultWidth;
        _isComposite = isComposite;
    }

    /// <summary>Returns advance width for code in font units (typically 1000ths of em).</summary>
    internal double GetWidth(int code)
    {
        if (_widths.TryGetValue(code, out double w)) { return w; }
        // Standard 14 fallback: when /Widths isn't present, look up per-char widths.
        if (_standard14BaseFont is not null && code < 256)
        {
            int sw = Standard14GlyphWidths.Width(_standard14BaseFont, (char)code);
            if (sw > 0) { return sw; }
        }
        return _defaultWidth;
    }

    /// <summary>Tells this widths table to use Standard 14 fallback for unmapped codes.</summary>
    internal void EnableStandard14Fallback(string baseFont)
    {
        if (Standard14GlyphWidths.IsStandard14(baseFont))
        {
            _standard14BaseFont = baseFont;
        }
    }

    private string? _standard14BaseFont;

    /// <summary>True if this font uses 2-byte codes (Type 0 composite).</summary>
    internal bool IsComposite => _isComposite;

    /// <summary>Builds the widths table for a font dictionary.</summary>
    internal static FontWidths FromDictionary(PdfDictionary fontDict, IPdfObjectResolver resolver)
    {
        if (fontDict.TryGetValue(PdfName.Intern("Subtype"), out PdfPrimitive? sub)
            && sub is PdfName sn && sn.Value == "Type0")
        {
            return BuildCidWidths(fontDict, resolver);
        }
        return BuildSimpleWidths(fontDict);
    }

    private static FontWidths BuildSimpleWidths(PdfDictionary fontDict)
    {
        FontWidths fw = new(defaultWidth: 500, isComposite: false);
        if (!fontDict.TryGetValue(PdfName.Intern("Widths"), out PdfPrimitive? wv)) { return fw; }
        if (wv is not PdfArray arr) { return fw; }
        int firstChar = 0;
        if (fontDict.TryGetValue(PdfName.Intern("FirstChar"), out PdfPrimitive? fc) && fc is PdfInteger fci)
        {
            firstChar = fci.Value;
        }
        // Default width override if present
        if (fontDict.TryGetValue(PdfName.Intern("FontDescriptor"), out PdfPrimitive? fdv)
            && fdv is PdfDictionary fd
            && fd.TryGetValue(PdfName.Intern("MissingWidth"), out PdfPrimitive? mwv)
            && mwv is PdfReal mwr)
        {
            FontWidths fw2 = new(defaultWidth: mwr.Value, isComposite: false);
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] is PdfInteger pi) { fw2._widths[firstChar + i] = pi.Value; }
                else if (arr[i] is PdfReal pr) { fw2._widths[firstChar + i] = pr.Value; }
            }
            return fw2;
        }
        for (int i = 0; i < arr.Count; i++)
        {
            if (arr[i] is PdfInteger pi) { fw._widths[firstChar + i] = pi.Value; }
            else if (arr[i] is PdfReal pr) { fw._widths[firstChar + i] = pr.Value; }
        }
        return fw;
    }

    private static FontWidths BuildCidWidths(PdfDictionary type0Dict, IPdfObjectResolver resolver)
    {
        FontWidths fw = new(defaultWidth: 1000, isComposite: true);
        if (!type0Dict.TryGetValue(PdfName.Intern("DescendantFonts"), out PdfPrimitive? dv))
        {
            return fw;
        }
        if (resolver.Resolve(dv) is not PdfArray descArr || descArr.Count == 0) { return fw; }
        if (resolver.Resolve(descArr[0]) is not PdfDictionary descDict) { return fw; }

        // /DW gives default width
        if (descDict.TryGetValue(PdfName.Intern("DW"), out PdfPrimitive? dwv))
        {
            if (dwv is PdfInteger dwi) { fw = new FontWidths(dwi.Value, isComposite: true); }
            else if (dwv is PdfReal dwr) { fw = new FontWidths(dwr.Value, isComposite: true); }
        }

        // /W is an array of CID-to-width definitions:
        //   c_first [w1 w2 ...]   — widths for cids c_first, c_first+1, ...
        //   c_first c_last w      — uniform width w for cids in [c_first, c_last]
        if (descDict.TryGetValue(PdfName.Intern("W"), out PdfPrimitive? ww) && ww is PdfArray wArr)
        {
            int i = 0;
            while (i < wArr.Count)
            {
                if (wArr[i] is not PdfInteger first) { i++; continue; }
                if (i + 1 >= wArr.Count) { break; }
                if (wArr[i + 1] is PdfArray inner)
                {
                    int cid = first.Value;
                    for (int j = 0; j < inner.Count; j++)
                    {
                        if (inner[j] is PdfInteger pi) { fw._widths[cid + j] = pi.Value; }
                        else if (inner[j] is PdfReal pr) { fw._widths[cid + j] = pr.Value; }
                    }
                    i += 2;
                }
                else if (i + 2 < wArr.Count
                    && wArr[i + 1] is PdfInteger last
                    && (wArr[i + 2] is PdfInteger or PdfReal))
                {
                    double width = wArr[i + 2] is PdfInteger wi ? wi.Value :
                        ((PdfReal)wArr[i + 2]).Value;
                    for (int c = first.Value; c <= last.Value; c++) { fw._widths[c] = width; }
                    i += 3;
                }
                else { i++; }
            }
        }
        return fw;
    }
}
