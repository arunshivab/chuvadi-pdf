// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.31 — Indic text in fonts a PDF does not embed (D-100, D-105),
//        drawn with the LiPi Sans faces of Chuvadi.Fonts (D-113, D-148) and
//        their real Bold files for bold text (D-152).

using System;
using System.Collections.Concurrent;
using Chuvadi.Fonts;
using Chuvadi.Pdf.Fonts.Rendering;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Text.Shaping;

namespace Chuvadi.Pdf.Rendering.Raster;

/// <summary>
/// A LiPi Sans face from Chuvadi.Fonts, loaded once per process for drawing:
/// the face the shaper uses and the renderer that draws its glyph outlines.
/// </summary>
internal sealed class LipiFace
{
    private static readonly ConcurrentDictionary<(LipiScript Script, bool Bold), LipiFace?> Loaded =
        new ConcurrentDictionary<(LipiScript, bool), LipiFace?>();

    private LipiFace(FontFace face, FontRenderer renderer)
    {
        Face = face;
        Renderer = renderer;
    }

    internal FontFace Face { get; }

    internal FontRenderer Renderer { get; }

    /// <summary>
    /// The face for an Indic script, Bold or Regular, or null when Chuvadi.Fonts
    /// has none or it cannot be read. Chuvadi.Fonts has no Indic italic, so
    /// italic text uses the upright face of the same weight.
    /// </summary>
    internal static LipiFace? For(LipiScript script, bool bold) => Loaded.GetOrAdd((script, bold), Load);

    /// <summary>The Chuvadi.Fonts file for an Indic script and weight.</summary>
    internal static string FileName(LipiScript script, bool bold) =>
        "LiPi-Sans-" + script.ToString() + (bold ? "-Bold.ttf" : "-Regular.ttf");

    private static LipiFace? Load((LipiScript Script, bool Bold) key)
    {
        if (key.Script == LipiScript.Latin)
        {
            return null;
        }

        try
        {
            FontPackFile? file = FontPack.Find(FileName(key.Script, key.Bold));
            if (file is null)
            {
                return null;
            }

            byte[] program = FontPack.ReadAllBytes(file.FileName);
            return new LipiFace(FontFace.Load(program), new FontRenderer(program));
        }
        catch (Exception ex) when (!PdfWorkScope.IsAbort(ex))
        {
            return null;
        }
    }
}
