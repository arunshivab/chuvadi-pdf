// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.6.5 — Type 3 fonts
// PHASE: Phase 2 — item 26, Type 3 fonts (rendering)
//
// A Type 3 font defines each glyph as a content stream (a CharProc) drawn with
// ordinary graphics operators in glyph space, mapped to text space by the
// font's FontMatrix. This model parses the pieces a renderer needs — the
// FontMatrix, the per-code glyph content streams (resolved via the Encoding's
// Differences and the CharProcs dictionary), the font's own Resources, and the
// glyph-space widths — leaving the matrix composition to each rendering sink.

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Rendering.Walking;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>
/// A parsed Type 3 font: its FontMatrix, per-code glyph content streams, the
/// font's own /Resources, and glyph-space widths. Returned by
/// <see cref="FromDictionary"/>; rendering sinks execute each glyph's content
/// stream under the FontMatrix-composed text rendering matrix.
/// </summary>
public sealed class Type3Font
{
    private readonly Dictionary<int, Type3Glyph> _glyphs;

    private Type3Font(double[] fontMatrix, PdfDictionary? resources, Dictionary<int, Type3Glyph> glyphs)
    {
        FontMatrix = fontMatrix;
        Resources = resources;
        _glyphs = glyphs;
    }

    /// <summary>The six FontMatrix entries mapping glyph space to text space.</summary>
    public double[] FontMatrix { get; }

    /// <summary>The font's own /Resources, used when executing a glyph's content stream.</summary>
    public PdfDictionary? Resources { get; }

    /// <summary>Gets the glyph for a character code, if defined.</summary>
    public bool TryGetGlyph(int code, out Type3Glyph glyph) => _glyphs.TryGetValue(code, out glyph);

    /// <summary>
    /// Parses a Type 3 font dictionary. Returns null if the dictionary is not a
    /// Type 3 font or lacks a CharProcs dictionary.
    /// </summary>
    /// <param name="fontDict">The font dictionary (<c>/Subtype /Type3</c>).</param>
    /// <param name="resolver">Resolver for indirect references.</param>
    public static Type3Font? FromDictionary(PdfDictionary fontDict, IPdfObjectResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(fontDict);
        ArgumentNullException.ThrowIfNull(resolver);

        if (fontDict.Subtype?.Value != "Type3")
        {
            return null;
        }

        double[] fontMatrix = new double[] { 0.001, 0, 0, 0.001, 0, 0 };
        if (Resolved(fontDict, "FontMatrix", resolver) is PdfArray fm && fm.Count >= 6)
        {
            for (int i = 0; i < 6; i++)
            {
                fontMatrix[i] = NumberOf(resolver.Resolve(fm[i]));
            }
        }

        if (Resolved(fontDict, "CharProcs", resolver) is not PdfDictionary charProcs)
        {
            return null;
        }

        PdfDictionary? resources = Resolved(fontDict, "Resources", resolver) as PdfDictionary;
        Dictionary<int, string> codeToName = ReadDifferences(fontDict, resolver);

        int firstChar = fontDict.GetInteger(PdfName.Intern("FirstChar"), 0);
        PdfArray? widths = Resolved(fontDict, "Widths", resolver) as PdfArray;

        Dictionary<int, Type3Glyph> glyphs = new Dictionary<int, Type3Glyph>();
        foreach (KeyValuePair<int, string> entry in codeToName)
        {
            if (!charProcs.TryGetValue(PdfName.Intern(entry.Value), out PdfPrimitive? procRef)
                || resolver.Resolve(procRef) is not PdfStream proc)
            {
                continue;
            }

            byte[] content;
            try
            {
                content = ContentStreamLoader.Decode(proc);
            }
            catch (Exception)
            {
                continue;
            }

            double width = 0.0;
            int code = entry.Key;
            if (widths is not null && code >= firstChar && code - firstChar < widths.Count)
            {
                width = NumberOf(resolver.Resolve(widths[code - firstChar]));
            }

            glyphs[code] = new Type3Glyph(content, width);
        }

        return new Type3Font(fontMatrix, resources, glyphs);
    }

    private static Dictionary<int, string> ReadDifferences(
        PdfDictionary fontDict, IPdfObjectResolver resolver)
    {
        Dictionary<int, string> map = new Dictionary<int, string>();
        if (Resolved(fontDict, "Encoding", resolver) is not PdfDictionary encoding
            || encoding.GetArray(PdfName.Intern("Differences")) is not PdfArray differences)
        {
            return map;
        }

        int current = 0;
        foreach (PdfPrimitive item in differences)
        {
            PdfPrimitive resolved = resolver.Resolve(item);
            if (resolved is PdfInteger code)
            {
                current = code.Value;
            }
            else if (resolved is PdfName name)
            {
                map[current] = name.Value;
                current++;
            }
        }

        return map;
    }

    private static PdfPrimitive? Resolved(PdfDictionary dict, string key, IPdfObjectResolver resolver)
        => dict.TryGetValue(PdfName.Intern(key), out PdfPrimitive? value) ? resolver.Resolve(value) : null;

    private static double NumberOf(PdfPrimitive primitive) => primitive switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0.0,
    };
}

/// <summary>A single Type 3 glyph: its content stream and glyph-space width.</summary>
/// <param name="Content">The decoded CharProc content stream.</param>
/// <param name="Width">The glyph width in glyph space (from /Widths).</param>
public readonly record struct Type3Glyph(byte[] Content, double Width);
