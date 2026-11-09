// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.8.1 (font descriptor), §9.8.2 Table 123 (flags);
//        OpenType OS/2 (usWeightClass, sFamilyClass), post (isFixedPitch)
// PHASE: 3.32.0 (D-244, D-245) — the one place Chuvadi decides /StemV and
//        /Flags for the fonts it embeds.

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Authoring;

/// <summary>
/// The font-descriptor rules (D-244, D-245): <c>/StemV</c> estimated from the
/// weight class, and <c>/Flags</c> from the font's own tables. The shaper's
/// reader only exposes the inputs; the policy lives here.
/// </summary>
internal static class FontDescriptorPolicy
{
    internal const int FixedPitch = 1;
    internal const int Serif = 2;
    internal const int Symbolic = 4;
    internal const int Nonsymbolic = 32;
    internal const int Italic = 64;

    /// <summary>
    /// The stem estimate: 50 + (weight / 65)², rounded (a common heuristic; the
    /// specification does not define it). Regular (400) gives 88, Bold (700)
    /// gives 166. A missing or out-of-range weight class counts as 400.
    /// </summary>
    internal static int StemV(int weightClass)
    {
        int weight = weightClass is >= 1 and <= 1000 ? weightClass : 400;
        double ratio = weight / 65.0;
        return (int)Math.Round(50 + (ratio * ratio), MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The flags: Symbolic or Nonsymbolic as the caller's encoding requires,
    /// plus Italic (macStyle bit 1 or a non-zero italic angle), FixedPitch
    /// (post isFixedPitch) and Serif (sFamilyClass class 1-5 or 7). ForceBold
    /// is never set (D-245).
    /// </summary>
    internal static int Flags(bool symbolic, bool italic, bool fixedPitch, int familyClass)
    {
        int flags = symbolic ? Symbolic : Nonsymbolic;
        if (italic)
        {
            flags |= Italic;
        }

        if (fixedPitch)
        {
            flags |= FixedPitch;
        }

        if (IsSerifClass(familyClass))
        {
            flags |= Serif;
        }

        return flags;
    }

    /// <summary>True for OS/2 sFamilyClass classes (the high byte) 1-5 and 7: the serif families.</summary>
    internal static bool IsSerifClass(int familyClass)
    {
        int cls = (familyClass >> 8) & 0xFF;
        return cls is >= 1 and <= 5 or 7;
    }

    /// <summary>The inputs read from an sfnt (TrueType or OpenType) font, with safe defaults.</summary>
    internal readonly record struct Inputs(int WeightClass, int FamilyClass, bool FixedPitch, bool MacStyleItalic, double ItalicAngle);

    /// <summary>
    /// Reads the policy inputs. Missing or short tables give the defaults:
    /// weight 400, family class 0, not fixed pitch, not italic. A bare CFF font
    /// (no sfnt tables) gets all defaults.
    /// </summary>
    internal static Inputs Read(byte[] font)
    {
        ArgumentNullException.ThrowIfNull(font);
        Dictionary<string, (int Offset, int Length)> tables = Tables(font);

        int weight = 400;
        int familyClass = 0;
        if (tables.TryGetValue("OS/2", out (int Offset, int Length) os2) && os2.Length >= 32 && Fits(font, os2.Offset, 32))
        {
            weight = U16(font, os2.Offset + 4);
            familyClass = U16(font, os2.Offset + 30);
        }

        bool fixedPitch = false;
        double italicAngle = 0;
        if (tables.TryGetValue("post", out (int Offset, int Length) post) && post.Length >= 16 && Fits(font, post.Offset, 16))
        {
            italicAngle = (int)U32(font, post.Offset + 4) / 65536.0;
            fixedPitch = U32(font, post.Offset + 12) != 0;
        }

        bool macItalic = false;
        if (tables.TryGetValue("head", out (int Offset, int Length) head) && head.Length >= 46 && Fits(font, head.Offset, 46))
        {
            macItalic = (U16(font, head.Offset + 44) & 0x2) != 0;
        }

        return new Inputs(weight, familyClass, fixedPitch, macItalic, italicAngle);
    }

    private static Dictionary<string, (int Offset, int Length)> Tables(byte[] font)
    {
        Dictionary<string, (int Offset, int Length)> tables = new Dictionary<string, (int Offset, int Length)>(StringComparer.Ordinal);
        if (font.Length < 12)
        {
            return tables;
        }

        uint tag = U32(font, 0);
        if (tag != 0x00010000 && tag != 0x4F54544F && tag != 0x74727565)
        {
            return tables;
        }

        int count = U16(font, 4);
        for (int i = 0; i < count; i++)
        {
            int record = 12 + (i * 16);
            if (!Fits(font, record, 16))
            {
                break;
            }

            string name = System.Text.Encoding.ASCII.GetString(font, record, 4);
            tables[name] = ((int)U32(font, record + 8), (int)U32(font, record + 12));
        }

        return tables;
    }

    private static bool Fits(byte[] b, int offset, int length) => offset >= 0 && length >= 0 && offset <= b.Length - length;

    private static int U16(byte[] b, int o) => (b[o] << 8) | b[o + 1];

    private static uint U32(byte[] b, int o) =>
        ((uint)b[o] << 24) | ((uint)b[o + 1] << 16) | ((uint)b[o + 2] << 8) | b[o + 3];
}
