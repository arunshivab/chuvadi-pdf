// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.30 — Indic text in fonts a PDF does not embed (D-100, D-105)

using System;
using System.Collections.Concurrent;
using Chuvadi.Pdf.Fonts.Bundled;
using Chuvadi.Pdf.Fonts.Rendering;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Text.Shaping;

namespace Chuvadi.Pdf.Rendering.Raster;

/// <summary>
/// A bundled LiPi Sans face loaded once per process for drawing: the face
/// the shaper uses and the renderer that draws its glyph outlines.
/// </summary>
internal sealed class BundledFace
{
    private static readonly ConcurrentDictionary<LipiScript, BundledFace?> Loaded = new ConcurrentDictionary<LipiScript, BundledFace?>();

    private BundledFace(FontFace face, FontRenderer renderer)
    {
        Face = face;
        Renderer = renderer;
    }

    internal FontFace Face { get; }

    internal FontRenderer Renderer { get; }

    /// <summary>The face for a script, or null when none is bundled or it cannot be read.</summary>
    internal static BundledFace? For(LipiScript script) => Loaded.GetOrAdd(script, Load);

    private static BundledFace? Load(LipiScript script)
    {
        string? name = BundledFonts.FaceForScript(script.ToString());
        if (name is null)
        {
            return null;
        }

        try
        {
            byte[] program = BundledFonts.GetFontProgram(name);
            return new BundledFace(FontFace.Load(program), new FontRenderer(program));
        }
        catch (Exception ex) when (!PdfWorkScope.IsAbort(ex))
        {
            return null;
        }
    }
}
