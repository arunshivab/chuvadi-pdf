// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using Chuvadi.Pdf.Fonts.Rendering;

namespace Chuvadi.Pdf.Authoring;

/// <summary>
/// Tracks custom fonts (TrueType, CFF OpenType, collection faces) registered on a document builder, along with
/// the Unicode code points actually drawn with each, so only those glyphs need
/// width and ToUnicode entries when the font is embedded.
/// </summary>
internal sealed class CustomFontRegistry
{
    private readonly Dictionary<string, CustomFont> _fonts =
        new Dictionary<string, CustomFont>();

    /// <summary>Gets every registered custom font keyed by name.</summary>
    public IReadOnlyDictionary<string, CustomFont> Fonts => _fonts;

    /// <summary>
    /// Registers a font program (TrueType, CFF-based OpenType, or one face of
    /// a collection) under <paramref name="name"/>.
    /// </summary>
    /// <exception cref="FontRenderingException">The font data is invalid or unsupported.</exception>
    public void Register(string name, byte[] fontData, int faceIndex = 0)
    {
        _fonts[name] = new CustomFont(FontFace.Load(fontData, faceIndex));
    }

    /// <summary>Looks up a registered font by name.</summary>
    public bool TryGet(string name, out CustomFont font) => _fonts.TryGetValue(name, out font!);
}

/// <summary>A registered custom font and the glyphs used from it.</summary>
internal sealed class CustomFont
{
    // Glyph id -> how many code points the cmap maps to it.
    private Dictionary<int, int>? _cmapGlyphs;

    /// <summary>Initialises a new <see cref="CustomFont"/>.</summary>
    public CustomFont(FontFace face)
    {
        Face = face;
        FontData = face.FontProgram;
        Loader = new TrueTypeLoader(FontData);
        UsedCodepoints = new SortedSet<int>();
        UsedGlyphs = new SortedSet<int>();
        GlyphText = new Dictionary<int, string>();
    }

    /// <summary>Gets the loaded face.</summary>
    public FontFace Face { get; }

    /// <summary>Gets the complete font program (a collection face as a font of its own).</summary>
    public byte[] FontData { get; }

    /// <summary>Gets a loader over the font, for cmap lookups and widths.</summary>
    public TrueTypeLoader Loader { get; }

    /// <summary>Gets the set of Unicode code points drawn with this font.</summary>
    public SortedSet<int> UsedCodepoints { get; }

    /// <summary>Gets raw glyph ids drawn via pre-shaped runs (may not be cmap-reachable).</summary>
    public SortedSet<int> UsedGlyphs { get; }

    /// <summary>
    /// Gets the text claimed by glyphs that no code point reaches through the
    /// cmap (ligatures, conjuncts), for their ToUnicode entries.
    /// </summary>
    public Dictionary<int, string> GlyphText { get; }

    /// <summary>Returns how many code points map to <paramref name="glyphId"/> through the cmap.</summary>
    public int CmapCodePointCount(int glyphId)
    {
        if (_cmapGlyphs is null)
        {
            Dictionary<int, int> glyphs = new Dictionary<int, int>();
            foreach (KeyValuePair<int, int> entry in Face.CharacterMap)
            {
                glyphs[entry.Value] = glyphs.GetValueOrDefault(entry.Value) + 1;
            }

            _cmapGlyphs = glyphs;
        }

        return _cmapGlyphs.GetValueOrDefault(glyphId);
    }
}
