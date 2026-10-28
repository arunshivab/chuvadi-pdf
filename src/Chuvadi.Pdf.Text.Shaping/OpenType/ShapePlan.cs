// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// Port of HarfBuzz's OpenType shaping pipeline (hb-ot-shape.cc,
// hb-ot-shape-fallback.cc space fallback); see THIRD-PARTY-NOTICES.md.

using System;
using Chuvadi.Pdf.Fonts.Rendering;
using Chuvadi.Pdf.Text.Shaping.Indic;
using Chuvadi.Pdf.Text.Shaping.Unicode;

namespace Chuvadi.Pdf.Text.Shaping.OpenType;

/// <summary>Per-face OpenType tables, parsed once and shared by plans.</summary>
internal sealed class ShapingFace
{
    internal ShapingFace(FontFace face)
    {
        Face = face;
        byte[] d = face.FontProgram;
        Gsub = new OtLayoutTable(d, OtReader.FindTable(d, "GSUB"), isGsub: true);
        Gpos = new OtLayoutTable(d, OtReader.FindTable(d, "GPOS"), isGsub: false);
        Gdef = new GdefTable(d, OtReader.FindTable(d, "GDEF"));
    }

    internal FontFace Face { get; }

    internal OtLayoutTable Gsub { get; }

    internal OtLayoutTable Gpos { get; }

    internal GdefTable Gdef { get; }
}

/// <summary>A compiled shaping plan for one face, script and feature set.</summary>
internal sealed class ShapePlan
{
    private readonly ShapingFace _face;
    private readonly IndicShaper? _indic;

    internal ShapePlan(ShapingFace face, LipiScript script, ShapingFeatures features)
    {
        _face = face;
        Script = script;
        OtMapBuilder builder = new OtMapBuilder(face.Gsub, face.Gpos, ScriptTags(script));
        string? chosen = builder.ChosenGsubScript;
        bool indicScript = script != LipiScript.Latin;
        // HarfBuzz uses the default shaper only when the font chose DFLT or
        // latn; no matching script (or no GSUB) still gets the Indic shaper.
        if (indicScript && chosen is not "DFLT" and not "latn")
        {
            _indic = new IndicShaper(script);
        }

        CollectFeatures(builder, features);
        Map = builder.Compile();
        _indic?.CreateData(this);

        FracMask = Map.GetOneMask("frac");
        NumrMask = Map.GetOneMask("numr");
        DnomMask = Map.GetOneMask("dnom");
        HasFrac = FracMask != 0 || (NumrMask != 0 && DnomMask != 0);
        FallbackGlyphClasses = !face.Gdef.HasGlyphClasses;
        ApplyGpos = face.Gpos.Present;
        ZeroMarks = _indic is null;
        FallbackMarks = !ApplyGpos && _indic is null;
    }

    internal LipiScript Script { get; }

    internal OtMap Map { get; }

    internal OtLayoutTable Gsub => _face.Gsub;

    internal NormalizationMode NormalizationMode =>
        _indic is null ? NormalizationMode.ComposedDiacritics : NormalizationMode.ComposedDiacriticsNoShortCircuit;

    private uint FracMask { get; }

    private uint NumrMask { get; }

    private uint DnomMask { get; }

    private bool HasFrac { get; }

    private bool FallbackGlyphClasses { get; }

    private bool ApplyGpos { get; }

    private bool ZeroMarks { get; }

    // No GPOS: marks are placed from glyph extents (fallback_mark_positioning).
    private bool FallbackMarks { get; }

    internal static void SetUnicodeProps(ref GlyphInfo info, ShapeBuffer buffer)
    {
        int u = info.Codepoint;
        int genCat = UnicodeData.GeneralCategory(u);
        int props = genCat;
        if (u >= 0x80)
        {
            if (UnicodeData.IsDefaultIgnorable(u))
            {
                buffer.ScratchFlags |= ShapeBuffer.ScratchHasDefaultIgnorables;
                props |= UProps.Ignorable;
                if (u == 0x200C)
                {
                    props |= UProps.CfZwnj;
                }
                else if (u == 0x200D)
                {
                    props |= UProps.CfZwj;
                }
                else if (u is (>= 0x180B and <= 0x180D) or 0x180F or (>= 0xE0020 and <= 0xE007F))
                {
                    props |= UProps.Hidden;
                }
                else if (u == 0x034F)
                {
                    buffer.ScratchFlags |= ShapeBuffer.ScratchHasCgj;
                    props |= UProps.Hidden;
                }
            }

            if (UnicodeData.IsMarkCategory(genCat))
            {
                buffer.ScratchFlags |= ShapeBuffer.ScratchHasContinuations;
                props |= UProps.Continuation;
                props |= UnicodeData.ModifiedCombiningClass(u) << 8;
            }
        }

        info.UnicodeProps = props;
    }

    internal int NominalGlyph(int codepoint) => _face.Face.GetGlyphIndex(codepoint);

    internal bool Decompose(int ab, out int a, out int b) =>
        _indic is null ? UnicodeData.Decompose(ab, out a, out b) : IndicShaper.Decompose(ab, out a, out b);

    internal bool Compose(int a, int b, out int ab) =>
        _indic is null ? UnicodeData.Compose(a, b, out ab) : IndicShaper.Compose(a, b, out ab);

    /// <summary>Shapes the text; positions are in font units.</summary>
    internal ShapeBuffer Shape(string text)
    {
        ShapeBuffer buffer = new ShapeBuffer(text.Length + 8) { Script = Script };
        for (int i = 0; i < text.Length;)
        {
            // An unpaired surrogate becomes U+FFFD, as in HarfBuzz.
            bool pair = char.IsSurrogatePair(text, i);
            int cp = pair ? char.ConvertToUtf32(text, i) : char.IsSurrogate(text[i]) ? 0xFFFD : text[i];
            buffer.Add(cp, i);
            i += pair ? 2 : 1;
        }

        // HarfBuzz's operation budget: bounds the work a hostile font can cause.
        buffer.MaxOps = Math.Max(buffer.Len * 64, 16384);

        SetUnicodePropsAll(buffer, Map.GlobalMask);
        FormClusters(buffer);
        _indic?.PreprocessText(buffer);

        // substitute_pre: normalize, set masks, map glyphs.
        Normalizer.Normalize(this, buffer);
        SetupMasksFraction(buffer);
        if (_indic is not null)
        {
            IndicShaper.SetupMasks(buffer);
        }

        if (FallbackMarks)
        {
            FallbackMarkPositioning.RecategorizeMarks(buffer);
        }

        for (int i = 0; i < buffer.Len; i++)
        {
            buffer.Info[i].Codepoint = buffer.Info[i].NormalizerGlyph;
        }

        // substitute_plan.
        for (int i = 0; i < buffer.Len; i++)
        {
            buffer.Info[i].GlyphProps = _face.Gdef.GetGlyphProps(buffer.Info[i].Codepoint);
            buffer.Info[i].LigProps = 0;
            buffer.Info[i].Syllable = 0;
        }

        if (FallbackGlyphClasses)
        {
            for (int i = 0; i < buffer.Len; i++)
            {
                ref GlyphInfo g = ref buffer.Info[i];
                g.GlyphProps = (g.UnicodeProps & UProps.GenCatMask) != UnicodeData.NonSpacingMark || OtApplyContext.IsDefaultIgnorable(g)
                    ? GlyphProps.BaseGlyph
                    : GlyphProps.Mark;
            }
        }

        OtApplyContext gsub = new OtApplyContext(0, _face.Gsub, _face.Gdef, buffer);
        Map.Apply(0, this, gsub);

        Position(buffer);
        HideDefaultIgnorables(buffer);
        return buffer;
    }

    private static string[] ScriptTags(LipiScript script) => script switch
    {
        LipiScript.Tamil => new[] { "tml3", "tml2", "taml" },
        LipiScript.Devanagari => new[] { "dev3", "dev2", "deva" },
        LipiScript.Bengali => new[] { "bng3", "bng2", "beng" },
        LipiScript.Gurmukhi => new[] { "gur3", "gur2", "guru" },
        LipiScript.Gujarati => new[] { "gjr3", "gjr2", "gujr" },
        LipiScript.Odia => new[] { "ory3", "ory2", "orya" },
        LipiScript.Telugu => new[] { "tel3", "tel2", "telu" },
        LipiScript.Kannada => new[] { "knd3", "knd2", "knda" },
        LipiScript.Malayalam => new[] { "mlm3", "mlm2", "mlym" },
        _ => new[] { "latn" },
    };

    private void CollectFeatures(OtMapBuilder map, ShapingFeatures features)
    {
        map.EnableFeature("rvrn");
        map.AddGsubPause(null);
        map.EnableFeature("ltra");
        map.EnableFeature("ltrm");
        map.AddFeature("frac");
        map.AddFeature("numr");
        map.AddFeature("dnom");
        map.EnableFeature("rand", FeatureFlags.Randomize, 255);
        map.EnableFeature("Harf");
        map.EnableFeature("HARF");
        _indic?.CollectFeatures(map);
        map.EnableFeature("Buzz");
        map.EnableFeature("BUZZ");

        map.AddFeature("abvm", FeatureFlags.Global);
        map.AddFeature("blwm", FeatureFlags.Global);
        map.AddFeature("ccmp", FeatureFlags.Global);
        map.AddFeature("locl", FeatureFlags.Global);
        map.AddFeature("mark", FeatureFlags.GlobalManualJoiners);
        map.AddFeature("mkmk", FeatureFlags.GlobalManualJoiners);
        map.AddFeature("rlig", FeatureFlags.Global);

        map.AddFeature("calt", FeatureFlags.Global);
        map.AddFeature("clig", FeatureFlags.Global);
        map.AddFeature("curs", FeatureFlags.Global);
        map.AddFeature("dist", FeatureFlags.Global);
        map.AddFeature("kern", FeatureFlags.GlobalHasFallback);
        map.AddFeature("liga", FeatureFlags.Global);
        map.AddFeature("rclt", FeatureFlags.Global);

        // User features: the ShapingFeatures toggles that differ from these defaults.
        foreach ((string tag, bool on) in features.Toggles())
        {
            map.AddFeature(tag, FeatureFlags.Global, on ? 1 : 0);
        }

        if (_indic is not null)
        {
            IndicShaper.OverrideFeatures(map);
        }
    }

    private static void SetUnicodePropsAll(ShapeBuffer buffer, uint globalMask)
    {
        GlyphInfo[] info = buffer.Info;
        for (int i = 0; i < buffer.Len; i++)
        {
            info[i].Mask = globalMask;
            SetUnicodeProps(ref info[i], buffer);
            int u = info[i].Codepoint;
            if (u < 0x80)
            {
                continue;
            }

            int gc = info[i].UnicodeProps & UProps.GenCatMask;
            if (UnicodeData.IsLetterCategory(gc) && gc != UnicodeData.ModifierLetter)
            {
                continue;
            }

            if (gc == UnicodeData.SpaceSeparator)
            {
                continue;
            }

            if (gc == UnicodeData.ModifierSymbol && u is >= 0x1F3FB and <= 0x1F3FF)
            {
                info[i].UnicodeProps |= UProps.Continuation;
                buffer.ScratchFlags |= ShapeBuffer.ScratchHasContinuations;
            }
            else if (i > 0 && UnicodeData.IsRegionalIndicator(u))
            {
                if (UnicodeData.IsRegionalIndicator(info[i - 1].Codepoint) && (info[i - 1].UnicodeProps & UProps.Continuation) == 0)
                {
                    info[i].UnicodeProps |= UProps.Continuation;
                    buffer.ScratchFlags |= ShapeBuffer.ScratchHasContinuations;
                }
            }
            else if (OtApplyContext.IsZwj(info[i]))
            {
                info[i].UnicodeProps |= UProps.Continuation;
                buffer.ScratchFlags |= ShapeBuffer.ScratchHasContinuations;
            }
            else if (u is (>= 0xFF9E and <= 0xFF9F) or (>= 0xE0020 and <= 0xE007F))
            {
                info[i].UnicodeProps |= UProps.Continuation;
                buffer.ScratchFlags |= ShapeBuffer.ScratchHasContinuations;
            }
        }
    }

    private static void FormClusters(ShapeBuffer buffer)
    {
        if ((buffer.ScratchFlags & ShapeBuffer.ScratchHasContinuations) == 0 || buffer.Len == 0)
        {
            return;
        }

        int start = 0;
        while (start < buffer.Len)
        {
            int end = start + 1;
            while (end < buffer.Len && (buffer.Info[end].UnicodeProps & UProps.Continuation) != 0)
            {
                end++;
            }

            buffer.MergeClusters(start, end);
            start = end;
        }
    }

    private void SetupMasksFraction(ShapeBuffer buffer)
    {
        if (!HasFrac)
        {
            return;
        }

        uint preMask = NumrMask | FracMask;
        uint postMask = FracMask | DnomMask;
        GlyphInfo[] info = buffer.Info;
        int count = buffer.Len;
        for (int i = 0; i < count; i++)
        {
            if (info[i].Codepoint != 0x2044)
            {
                continue;
            }

            int start = i;
            int end = i + 1;
            while (start > 0 && (info[start - 1].UnicodeProps & UProps.GenCatMask) == UnicodeData.DecimalNumber)
            {
                start--;
            }

            while (end < count && (info[end].UnicodeProps & UProps.GenCatMask) == UnicodeData.DecimalNumber)
            {
                end++;
            }

            if (start == i || end == i + 1)
            {
                continue;
            }

            for (int j = start; j < i; j++)
            {
                info[j].Mask |= preMask;
            }

            info[i].Mask |= FracMask;
            for (int j = i + 1; j < end; j++)
            {
                info[j].Mask |= postMask;
            }

            i = end - 1;
        }
    }

    private void Position(ShapeBuffer buffer)
    {
        buffer.ClearPositions();
        GlyphInfo[] info = buffer.Info;
        GlyphPos[] pos = buffer.Pos;
        FontFace face = _face.Face;
        for (int i = 0; i < buffer.Len; i++)
        {
            pos[i].XAdvance = face.GetAdvanceWidth(info[i].Codepoint);
        }

        if ((buffer.ScratchFlags & ShapeBuffer.ScratchHasSpaceFallback) != 0)
        {
            FallbackSpaces(buffer);
        }

        bool hasAttachment = false;
        if (ApplyGpos)
        {
            OtApplyContext gpos = new OtApplyContext(1, _face.Gpos, _face.Gdef, buffer);
            Map.Apply(1, this, gpos);
            hasAttachment = gpos.HasAttachment;
        }

        if (ZeroMarks)
        {
            // HB_OT_SHAPE_ZERO_WIDTH_MARKS_BY_GDEF_LATE; without GPOS the mark is
            // shifted back so it hangs over the previous glyph.
            for (int i = 0; i < buffer.Len; i++)
            {
                if ((info[i].GlyphProps & GlyphProps.Mark) != 0)
                {
                    if (!ApplyGpos)
                    {
                        pos[i].XOffset -= pos[i].XAdvance;
                    }

                    pos[i].XAdvance = 0;
                    pos[i].YAdvance = 0;
                }
            }
        }

        // Zero-width default ignorables.
        if ((buffer.ScratchFlags & ShapeBuffer.ScratchHasDefaultIgnorables) != 0)
        {
            for (int i = 0; i < buffer.Len; i++)
            {
                if (OtApplyContext.IsDefaultIgnorable(info[i]))
                {
                    pos[i].XAdvance = 0;
                    pos[i].YAdvance = 0;
                    pos[i].XOffset = 0;
                }
            }
        }

        if (hasAttachment)
        {
            OtApplyContext.PropagateAttachmentOffsets(buffer);
        }

        if (FallbackMarks)
        {
            FallbackMarkPositioning.Position(face, buffer);
        }
    }

    private void FallbackSpaces(ShapeBuffer buffer)
    {
        GlyphInfo[] info = buffer.Info;
        GlyphPos[] pos = buffer.Pos;
        int upem = _face.Face.UnitsPerEm;
        for (int i = 0; i < buffer.Len; i++)
        {
            if ((info[i].UnicodeProps & UProps.GenCatMask) != UnicodeData.SpaceSeparator ||
                (info[i].GlyphProps & GlyphProps.Ligated) != 0)
            {
                continue;
            }

            int spaceType = info[i].UnicodeProps >> 8;
            switch (spaceType)
            {
                case UnicodeData.SpaceEm:
                case UnicodeData.SpaceEm2:
                case UnicodeData.SpaceEm3:
                case UnicodeData.SpaceEm4:
                case UnicodeData.SpaceEm5:
                case UnicodeData.SpaceEm6:
                case UnicodeData.SpaceEm16:
                    pos[i].XAdvance = (upem + (spaceType / 2)) / spaceType;
                    break;
                case UnicodeData.Space4Em18:
                    pos[i].XAdvance = upem * 4 / 18;
                    break;
                case UnicodeData.SpaceFigure:
                    for (int u = '0'; u <= '9'; u++)
                    {
                        int g = NominalGlyph(u);
                        if (g != 0)
                        {
                            pos[i].XAdvance = _face.Face.GetAdvanceWidth(g);
                            break;
                        }
                    }

                    break;
                case UnicodeData.SpacePunctuation:
                    {
                        int g = NominalGlyph('.');
                        if (g == 0)
                        {
                            g = NominalGlyph(',');
                        }

                        if (g != 0)
                        {
                            pos[i].XAdvance = _face.Face.GetAdvanceWidth(g);
                        }

                        break;
                    }

                case UnicodeData.SpaceNarrow:
                    pos[i].XAdvance /= 2;
                    break;
            }
        }
    }

    private void HideDefaultIgnorables(ShapeBuffer buffer)
    {
        if ((buffer.ScratchFlags & ShapeBuffer.ScratchHasDefaultIgnorables) == 0)
        {
            return;
        }

        int invisible = NominalGlyph(' ');
        if (invisible != 0)
        {
            for (int i = 0; i < buffer.Len; i++)
            {
                if (OtApplyContext.IsDefaultIgnorable(buffer.Info[i]))
                {
                    buffer.Info[i].Codepoint = invisible;
                }
            }
        }
        else
        {
            buffer.DeleteGlyphsInplace(OtApplyContext.IsDefaultIgnorableValue);
        }
    }
}
