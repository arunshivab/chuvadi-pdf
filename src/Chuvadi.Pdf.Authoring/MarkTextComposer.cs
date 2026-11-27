// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.4 (text showing), §9.7 (composite fonts)
// PHASE: 3.36.0 (D-412 / D-424) — the one path every mark (stamps, watermarks,
//        page numbers, headers and footers) draws its text through: segment by
//        script, shape with the LiPi Sans face for the script (or a chosen
//        font), fall back to the embedded monochrome Noto Emoji for code points
//        the primary face does not cover, and subset-embed every face used.
//        Replaces the old WinAnsi Helvetica standard-14 path, so marks render
//        Latin, Indic and emoji and are always embedded.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Chuvadi.Pdf.Fonts.Rendering;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Text.Shaping;
using Chuvadi.Fonts;

namespace Chuvadi.Pdf.Authoring;

/// <summary>
/// The composed form of a line of mark text: a content fragment that draws it,
/// the embedded-font objects to add to the document, the resource names those
/// fonts take, and the advance width in points.
/// </summary>
internal sealed class ComposedMarkText
{
    internal ComposedMarkText(
        string contentFragment,
        IReadOnlyList<PdfIndirectObject> fontObjects,
        IReadOnlyDictionary<string, PdfObjectId> fontResources,
        double widthPoints)
    {
        ContentFragment = contentFragment;
        FontObjects = fontObjects;
        FontResources = fontResources;
        WidthPoints = widthPoints;
    }

    /// <summary>Content-stream operators (colour + BT…ET), no surrounding q/Q.</summary>
    internal string ContentFragment { get; }

    /// <summary>Embedded-font objects to add to the document being written.</summary>
    internal IReadOnlyList<PdfIndirectObject> FontObjects { get; }

    /// <summary>Resource name (as used by the fragment's <c>Tf</c>) to Type0 font id.</summary>
    internal IReadOnlyDictionary<string, PdfObjectId> FontResources { get; }

    /// <summary>The line's advance width in points at the composed size.</summary>
    internal double WidthPoints { get; }
}

/// <summary>
/// Shapes and embeds mark text through the LiPi Sans faces (by script) with a
/// monochrome Noto Emoji fallback, and an optional caller-chosen primary font.
/// A code point is drawn by the first face that covers it in the order: chosen
/// font (if any), the LiPi face for the run's script, then emoji. Create one
/// per stamp operation; faces are loaded lazily and cached on the instance.
/// </summary>
internal sealed class MarkTextComposer
{
    private readonly FontFace? _chosen;
    private readonly Dictionary<LipiScript, FontFace> _lipi = new Dictionary<LipiScript, FontFace>();
    private FontFace? _emoji;

    /// <summary>
    /// Creates a composer. When <paramref name="chosenFontProgram"/> is given
    /// (raw sfnt bytes), it is the primary face for every code point it covers;
    /// otherwise the LiPi face for each run's script is primary.
    /// </summary>
    internal MarkTextComposer(byte[]? chosenFontProgram = null)
    {
        _chosen = chosenFontProgram is null ? null : FontFace.Load(chosenFontProgram);
    }

    /// <summary>The advance width of <paramref name="text"/> in points at <paramref name="fontSize"/>.</summary>
    internal double Measure(string text, double fontSize)
    {
        ArgumentNullException.ThrowIfNull(text);
        double width1000 = 0.0;
        foreach (Piece piece in Pieces(text))
        {
            foreach (ShapedGlyph glyph in TextShaper.Shape(piece.Face, piece.Text, piece.ShapingScript))
            {
                width1000 += glyph.XAdvance;
            }
        }

        return width1000 / 1000.0 * fontSize;
    }

    /// <summary>
    /// Composes <paramref name="text"/> in local text space — baseline on y = 0,
    /// starting at x = 0 and advancing in +x — at <paramref name="fontSize"/>
    /// and <paramref name="color"/>. The caller places and rotates it by wrapping
    /// the fragment in its own <c>q … cm … Q</c>. Font resource names are prefixed
    /// with <paramref name="resourcePrefix"/> so several marks on one page do not
    /// collide.
    /// </summary>
    internal ComposedMarkText Compose(
        string text,
        double fontSize,
        ColorF color,
        Func<PdfObjectId> allocateId,
        string resourcePrefix)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(allocateId);
        ArgumentNullException.ThrowIfNull(resourcePrefix);

        double scale = fontSize / 1000.0;
        ColorF rgb = color.ToRgb();

        StringBuilder body = new StringBuilder();
        double penX = 0.0;

        // One accumulator per face, in first-seen order, so each face is
        // embedded once and keeps a stable resource name.
        List<FaceUse> faces = new List<FaceUse>();
        Dictionary<FontFace, FaceUse> byFace = new Dictionary<FontFace, FaceUse>();

        foreach (Piece piece in Pieces(text))
        {
            if (!byFace.TryGetValue(piece.Face, out FaceUse? use))
            {
                use = new FaceUse(piece.Face, resourcePrefix + "F" + faces.Count.ToString(CultureInfo.InvariantCulture));
                byFace[piece.Face] = use;
                faces.Add(use);
            }

            body.Append('/').Append(use.ResourceName).Append(' ').Append(Fmt(fontSize)).Append(" Tf\n");
            foreach (int cp in Codepoints(piece.Text))
            {
                use.Codepoints.Add(cp);
            }

            foreach (ShapedGlyph glyph in TextShaper.Shape(piece.Face, piece.Text, piece.ShapingScript))
            {
                use.Glyphs.Add(glyph.GlyphId);
                double gx = penX + (glyph.XOffset * scale);
                double gy = glyph.YOffset * scale;
                body.Append("1 0 0 1 ").Append(Fmt(gx)).Append(' ').Append(Fmt(gy)).Append(" Tm\n");
                body.Append('<').Append(Hex(glyph.GlyphId)).Append("> Tj\n");
                penX += glyph.XAdvance * scale;
            }
        }

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>();
        Dictionary<string, PdfObjectId> resources = new Dictionary<string, PdfObjectId>();
        foreach (FaceUse use in faces)
        {
            EmbeddedFontObjects embedded = FontEmbedder.Build(
                use.Face, use.Codepoints, use.BaseFont, allocateId, use.Glyphs);
            objects.AddRange(embedded.Objects);
            resources[use.ResourceName] = embedded.Type0FontId;
        }

        StringBuilder fragment = new StringBuilder();
        fragment.Append(Fmt(rgb.R)).Append(' ').Append(Fmt(rgb.G)).Append(' ')
            .Append(Fmt(rgb.B)).Append(" rg\n");
        fragment.Append("BT\n").Append(body).Append("ET\n");

        return new ComposedMarkText(fragment.ToString(), objects, resources, penX);
    }

    // ── Face selection + segmentation ────────────────────────────────────

    // A run of text that one face draws, with the script to shape it as.
    private readonly record struct Piece(FontFace Face, string Text, LipiScript ShapingScript);

    private sealed class FaceUse
    {
        internal FaceUse(FontFace face, string resourceName)
        {
            Face = face;
            ResourceName = resourceName;
            BaseFont = "CvMark+F" + resourceName;
        }

        internal FontFace Face { get; }

        internal string ResourceName { get; }

        internal string BaseFont { get; }

        internal HashSet<int> Codepoints { get; } = new HashSet<int>();

        internal List<int> Glyphs { get; } = new List<int>();
    }

    // Splits text into script runs, then each run into maximal pieces drawn by
    // one face (chosen → LiPi(script) → emoji; the primary carries notdef).
    private List<Piece> Pieces(string text)
    {
        List<Piece> pieces = new List<Piece>();
        foreach (ScriptRun run in ScriptClassifier.Split(text))
        {
            FontFace lipi = LipiFace(run.Script);
            FontFace emoji = EmojiFace();
            StringBuilder current = new StringBuilder();
            FontFace? currentFace = null;
            bool currentIsEmoji = false;

            foreach (int cp in Codepoints(run.Text))
            {
                bool useEmoji = _chosen is null
                    ? lipi.GetGlyphIndex(cp) <= 0 && emoji.GetGlyphIndex(cp) > 0
                    : _chosen.GetGlyphIndex(cp) <= 0 && lipi.GetGlyphIndex(cp) <= 0 && emoji.GetGlyphIndex(cp) > 0;
                FontFace face = useEmoji ? emoji : Primary(cp, lipi);

                if (currentFace is not null && (!ReferenceEquals(face, currentFace) || useEmoji != currentIsEmoji))
                {
                    pieces.Add(new Piece(currentFace, current.ToString(), currentIsEmoji ? LipiScript.Latin : run.Script));
                    current.Clear();
                }

                current.Append(char.ConvertFromUtf32(cp));
                currentFace = face;
                currentIsEmoji = useEmoji;
            }

            if (currentFace is not null && current.Length > 0)
            {
                pieces.Add(new Piece(currentFace, current.ToString(), currentIsEmoji ? LipiScript.Latin : run.Script));
            }
        }

        return pieces;
    }

    // The primary (non-emoji) face for a code point: the chosen font when it
    // covers the code point, otherwise the LiPi face for the run's script. The
    // LiPi face also carries code points nothing covers (drawn as .notdef).
    private FontFace Primary(int codepoint, FontFace lipi)
    {
        if (_chosen is not null && _chosen.GetGlyphIndex(codepoint) > 0)
        {
            return _chosen;
        }

        return lipi;
    }

    private FontFace LipiFace(LipiScript script)
    {
        if (!_lipi.TryGetValue(script, out FontFace? face))
        {
            face = FontFace.Load(FontPack.ReadAllBytes(LipiFontSet.FileName(script)));
            _lipi[script] = face;
        }

        return face;
    }

    private FontFace EmojiFace() => _emoji ??= FontFace.Load(FontPack.ReadAllBytes("NotoEmoji-Regular.ttf"));

    private static IEnumerable<int> Codepoints(string text)
    {
        for (int i = 0; i < text.Length; i += char.IsHighSurrogate(text[i]) && i + 1 < text.Length ? 2 : 1)
        {
            yield return char.ConvertToUtf32(text, i);
        }
    }

    private static string Hex(int glyphId) => (glyphId & 0xFFFF).ToString("X4", CultureInfo.InvariantCulture);

    private static string Fmt(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}
