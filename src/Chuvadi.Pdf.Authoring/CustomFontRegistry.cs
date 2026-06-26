// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using Chuvadi.Pdf.Fonts.Rendering;

namespace Chuvadi.Pdf.Authoring;

/// <summary>
/// Tracks custom TrueType fonts registered on a document builder, along with
/// the Unicode code points actually drawn with each, so only those glyphs need
/// width and ToUnicode entries when the font is embedded.
/// </summary>
internal sealed class CustomFontRegistry
{
    private readonly Dictionary<string, CustomFont> _fonts =
        new Dictionary<string, CustomFont>();

    /// <summary>Gets every registered custom font keyed by name.</summary>
    public IReadOnlyDictionary<string, CustomFont> Fonts => _fonts;

    /// <summary>Registers a font program under <paramref name="name"/>.</summary>
    public void Register(string name, byte[] fontData)
    {
        _fonts[name] = new CustomFont(fontData, new TrueTypeLoader(fontData));
    }

    /// <summary>Looks up a registered font by name.</summary>
    public bool TryGet(string name, out CustomFont font) => _fonts.TryGetValue(name, out font!);
}

/// <summary>A registered custom font and the glyphs used from it.</summary>
internal sealed class CustomFont
{
    /// <summary>Initialises a new <see cref="CustomFont"/>.</summary>
    public CustomFont(byte[] fontData, TrueTypeLoader loader)
    {
        FontData = fontData;
        Loader = loader;
        UsedCodepoints = new SortedSet<int>();
        UsedGlyphs = new SortedSet<int>();
    }

    /// <summary>Gets the complete TrueType font program.</summary>
    public byte[] FontData { get; }

    /// <summary>Gets a loader over the font, for cmap lookups and widths.</summary>
    public TrueTypeLoader Loader { get; }

    /// <summary>Gets the set of Unicode code points drawn with this font.</summary>
    public SortedSet<int> UsedCodepoints { get; }

    /// <summary>Gets raw glyph ids drawn via pre-shaped runs (may not be cmap-reachable).</summary>
    public SortedSet<int> UsedGlyphs { get; }
}
