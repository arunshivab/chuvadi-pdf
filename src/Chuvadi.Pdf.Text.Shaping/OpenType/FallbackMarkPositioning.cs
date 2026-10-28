// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// Port of HarfBuzz's fallback mark positioning (hb-ot-shape-fallback.cc), used
// when a font has no GPOS table; see THIRD-PARTY-NOTICES.md.

using System;
using Chuvadi.Pdf.Fonts.Rendering;
using Chuvadi.Pdf.Text.Shaping.Unicode;

namespace Chuvadi.Pdf.Text.Shaping.OpenType;

/// <summary>Places marks over or under their base from glyph extents and combining classes.</summary>
internal static class FallbackMarkPositioning
{
    private const int AttachedBelowLeft = 200;
    private const int AttachedBelow = 202;
    private const int AttachedAbove = 214;
    private const int AttachedAboveRight = 216;
    private const int BelowLeft = 218;
    private const int Below = 220;
    private const int BelowRight = 222;
    private const int AboveLeft = 228;
    private const int Above = 230;
    private const int AboveRight = 232;
    private const int DoubleBelow = 233;
    private const int DoubleAbove = 234;

    /// <summary>Re-categorizes non-spacing marks' combining classes for positioning.</summary>
    internal static void RecategorizeMarks(ShapeBuffer buffer)
    {
        for (int i = 0; i < buffer.Len; i++)
        {
            ref GlyphInfo info = ref buffer.Info[i];
            if ((info.UnicodeProps & UProps.GenCatMask) != UnicodeData.NonSpacingMark)
            {
                continue;
            }

            int klass = Recategorize(info.Codepoint, info.UnicodeProps >> 8);
            info.UnicodeProps = (klass << 8) | (info.UnicodeProps & 0xFF);
        }
    }

    internal static void Position(FontFace face, ShapeBuffer buffer)
    {
        int start = 0;
        GlyphInfo[] info = buffer.Info;
        for (int i = 1; i < buffer.Len; i++)
        {
            if (!Normalizer.IsUnicodeMark(info[i]) &&
                (info[i].UnicodeProps & UProps.Hidden) == 0 &&
                !OtApplyContext.IsDefaultIgnorable(info[i]))
            {
                PositionCluster(face, buffer, start, i);
                start = i;
            }
        }

        PositionCluster(face, buffer, start, buffer.Len);
    }

    private static int Recategorize(int u, int klass)
    {
        if (klass >= 200)
        {
            return klass;
        }

        // Thai / Lao need some per-character work.
        if ((u & ~0xFF) == 0x0E00)
        {
            if (klass == 0)
            {
                switch (u)
                {
                    case 0x0E31:
                    case 0x0E34:
                    case 0x0E35:
                    case 0x0E36:
                    case 0x0E37:
                    case 0x0E47:
                    case 0x0E4C:
                    case 0x0E4D:
                    case 0x0E4E:
                        klass = AboveRight;
                        break;
                    case 0x0EB1:
                    case 0x0EB4:
                    case 0x0EB5:
                    case 0x0EB6:
                    case 0x0EB7:
                    case 0x0EBB:
                    case 0x0ECC:
                    case 0x0ECD:
                        klass = Above;
                        break;
                    case 0x0EBC:
                        klass = Below;
                        break;
                }
            }
            else if (u == 0x0E3A)
            {
                // Thai virama is below-right.
                klass = BelowRight;
            }
        }

        return klass switch
        {
            // Hebrew (modified classes).
            22 or 15 or 16 or 17 or 23 or 18 or 19 or 20 or 21 or 24 or 25 => Below,
            13 => AttachedAbove,
            10 => AboveRight,
            11 or 14 => AboveLeft,
            26 => Above,

            // Arabic and Syriac.
            28 or 29 or 31 or 32 or 27 or 34 or 35 or 36 => Above,
            30 or 33 => Below,

            // Thai, Lao, Tibetan.
            3 => BelowRight,
            107 => AboveRight,
            118 => Below,
            122 => Above,
            129 => Below,
            132 => Above,
            131 => Below,
            _ => klass,
        };
    }

    private static void PositionCluster(FontFace face, ShapeBuffer buffer, int start, int end)
    {
        if (end - start < 2)
        {
            return;
        }

        GlyphInfo[] info = buffer.Info;
        for (int i = start; i < end; i++)
        {
            if (!Normalizer.IsUnicodeMark(info[i]))
            {
                int j;
                for (j = i + 1; j < end; j++)
                {
                    if ((info[j].UnicodeProps & UProps.Hidden) != 0 || OtApplyContext.IsDefaultIgnorable(info[j]))
                    {
                        continue;
                    }

                    if (!Normalizer.IsUnicodeMark(info[j]))
                    {
                        break;
                    }
                }

                PositionAroundBase(face, buffer, i, j);
                i = j - 1;
            }
        }
    }

    private static Extents GetExtents(FontFace face, int glyph)
    {
        FontBox box = face.GetGlyphBounds(glyph);
        int lsb = face.IsCff ? Math.Min(box.XMin, box.XMax) : face.GetLeftSideBearing(glyph);
        return new Extents
        {
            XBearing = lsb,
            YBearing = Math.Max(box.YMin, box.YMax),
            Width = Math.Abs(box.XMax - box.XMin),
            Height = Math.Min(box.YMin, box.YMax) - Math.Max(box.YMin, box.YMax),
        };
    }

    private static void PositionAroundBase(FontFace face, ShapeBuffer buffer, int baseIdx, int end)
    {
        GlyphInfo[] info = buffer.Info;
        GlyphPos[] pos = buffer.Pos;
        Extents baseExtents = GetExtents(face, info[baseIdx].Codepoint);
        baseExtents.YBearing += pos[baseIdx].YOffset;

        // Use horizontal advance for horizontal positioning.
        baseExtents.XBearing = 0;
        baseExtents.Width = face.GetAdvanceWidth(info[baseIdx].Codepoint);

        int ligId = OtApplyContext.GetLigId(info[baseIdx]);
        int numLigComponents = OtApplyContext.GetLigNumComps(info[baseIdx]);
        int xOffset = -pos[baseIdx].XAdvance;
        int yOffset = -pos[baseIdx].YAdvance;

        Extents componentExtents = baseExtents;
        int lastLigComponent = -1;
        int lastCombiningClass = 255;
        Extents clusterExtents = baseExtents;
        for (int i = baseIdx + 1; i < end; i++)
        {
            if (Normalizer.Cc(info[i]) != 0)
            {
                if (numLigComponents > 1)
                {
                    int thisLigId = OtApplyContext.GetLigId(info[i]);
                    int thisLigComponent = OtApplyContext.GetLigComp(info[i]) - 1;

                    // Conditions for attaching to the last component.
                    if (ligId == 0 || ligId != thisLigId || thisLigComponent >= numLigComponents)
                    {
                        thisLigComponent = numLigComponents - 1;
                    }

                    if (lastLigComponent != thisLigComponent)
                    {
                        lastLigComponent = thisLigComponent;
                        lastCombiningClass = 255;
                        componentExtents = baseExtents;
                        componentExtents.XBearing += (int)((long)thisLigComponent * componentExtents.Width / numLigComponents);
                        componentExtents.Width /= numLigComponents;
                    }
                }

                int thisCombiningClass = Normalizer.Cc(info[i]);
                if (lastCombiningClass != thisCombiningClass)
                {
                    lastCombiningClass = thisCombiningClass;
                    clusterExtents = componentExtents;
                }

                PositionMark(face, buffer, ref clusterExtents, i, thisCombiningClass);
                pos[i].XAdvance = 0;
                pos[i].YAdvance = 0;
                pos[i].XOffset += xOffset;
                pos[i].YOffset += yOffset;
            }
            else
            {
                xOffset -= pos[i].XAdvance;
                yOffset -= pos[i].YAdvance;
            }
        }
    }

    private static void PositionMark(FontFace face, ShapeBuffer buffer, ref Extents baseExtents, int i, int combiningClass)
    {
        Extents mark = GetExtents(face, buffer.Info[i].Codepoint);
        int yGap = face.UnitsPerEm / 16;
        ref GlyphPos pos = ref buffer.Pos[i];
        pos.XOffset = 0;
        pos.YOffset = 0;

        // X positioning.
        switch (combiningClass)
        {
            case DoubleBelow:
            case DoubleAbove:
                pos.XOffset = baseExtents.XBearing + baseExtents.Width - (mark.Width / 2) - mark.XBearing;
                break;
            case AttachedBelowLeft:
            case BelowLeft:
            case AboveLeft:
                pos.XOffset = baseExtents.XBearing - mark.XBearing;
                break;
            case AttachedAboveRight:
            case BelowRight:
            case AboveRight:
                pos.XOffset = baseExtents.XBearing + baseExtents.Width - mark.Width - mark.XBearing;
                break;
            default:
                // Center align.
                pos.XOffset = (int)(baseExtents.XBearing + (((long)baseExtents.Width - mark.Width) / 2) - mark.XBearing);
                break;
        }

        // Y positioning.
        switch (combiningClass)
        {
            case DoubleBelow:
            case BelowLeft:
            case Below:
            case BelowRight:
            case AttachedBelowLeft:
            case AttachedBelow:
                if (combiningClass is DoubleBelow or BelowLeft or Below or BelowRight)
                {
                    baseExtents.Height -= yGap;
                }

                pos.YOffset = baseExtents.YBearing + baseExtents.Height - mark.YBearing;

                // Never shift up "below" marks.
                if ((yGap > 0) == (pos.YOffset > 0))
                {
                    baseExtents.Height -= pos.YOffset;
                    pos.YOffset = 0;
                }

                baseExtents.Height += mark.Height;
                break;

            case DoubleAbove:
            case AboveLeft:
            case Above:
            case AboveRight:
            case AttachedAbove:
            case AttachedAboveRight:
                if (combiningClass is DoubleAbove or AboveLeft or Above or AboveRight)
                {
                    baseExtents.YBearing += yGap;
                    baseExtents.Height -= yGap;
                }

                pos.YOffset = baseExtents.YBearing - mark.YBearing - mark.Height;

                // Don't shift down "above" marks too much.
                if ((yGap > 0) != (pos.YOffset > 0))
                {
                    int correction = -(pos.YOffset / 2);
                    baseExtents.YBearing += correction;
                    baseExtents.Height -= correction;
                    pos.YOffset += correction;
                }

                baseExtents.YBearing -= mark.Height;
                baseExtents.Height += mark.Height;
                break;
        }
    }

    private struct Extents
    {
        internal int XBearing;
        internal int YBearing;
        internal int Width;
        internal int Height;
    }
}
