// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2 — Indic reordering
//
// Unicode category data sourced from:
//   https://unicode.org/Public/UCD/latest/ucd/IndicSyllabicCategory.txt
//   https://unicode.org/Public/UCD/latest/ucd/IndicPositionalCategory.txt
//
// LiPi font Left_And_Right vowel decompositions sourced from the LiPi Sans
// font family cmap tables (glyph ids are font-specific constants).

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Text.Shaping.Indic;

/// <summary>Unicode IndicSyllabicCategory values relevant to shaping.</summary>
internal enum IndicSyllabicCategory
{
    Other,
    Consonant,
    VowelIndependent,
    VowelDependent,
    Virama,
    Bindu,
    Visarga,
    Nukta,
    Avagraha,
    ConsonantDead,
    ConsonantPlaceholder,
    ConsonantMedial,
    ConsonantPrecedingRepha,
    ModifyingLetter,
    Number,
}

/// <summary>Unicode IndicPositionalCategory values relevant to reordering.</summary>
internal enum IndicPositionalCategory
{
    NA,
    Right,
    Left,
    Top,
    Bottom,
    LeftAndRight,
    TopAndRight,
    TopAndLeft,
    TopAndLeftAndRight,
    TopAndBottom,
}

/// <summary>
/// A Left_And_Right vowel decomposition for the LiPi Sans font family.
/// The composed vowel sign is synthesised as: left-part glyph, base consonant
/// glyph(s), right-part glyph. Glyph ids are font-specific constants derived
/// from the LiPi Sans cmap tables.
/// </summary>
internal readonly record struct VowelDecomposition(int ComposedCodepoint, int LeftGlyphId, int RightGlyphId);

/// <summary>
/// Static Unicode Indic category tables and LiPi font decomposition maps for all
/// nine supported scripts.
/// </summary>
internal static class IndicData
{
    // Ranges are sorted by Lo so binary search is used in the lookup methods.

    private static readonly (int Lo, int Hi, IndicSyllabicCategory Cat)[] ISCRanges =
    {
        (0x0900, 0x0902, IndicSyllabicCategory.Bindu),
        (0x0903, 0x0903, IndicSyllabicCategory.Visarga),
        (0x0904, 0x0914, IndicSyllabicCategory.VowelIndependent),
        (0x0915, 0x0939, IndicSyllabicCategory.Consonant),
        (0x093A, 0x093B, IndicSyllabicCategory.VowelDependent),
        (0x093C, 0x093C, IndicSyllabicCategory.Nukta),
        (0x093D, 0x093D, IndicSyllabicCategory.Avagraha),
        (0x093E, 0x0940, IndicSyllabicCategory.VowelDependent),
        (0x0941, 0x0948, IndicSyllabicCategory.VowelDependent),
        (0x0949, 0x094C, IndicSyllabicCategory.VowelDependent),
        (0x094D, 0x094D, IndicSyllabicCategory.Virama),
        (0x094E, 0x094F, IndicSyllabicCategory.VowelDependent),
        (0x0955, 0x0957, IndicSyllabicCategory.VowelDependent),
        (0x0958, 0x095F, IndicSyllabicCategory.Consonant),
        (0x0960, 0x0961, IndicSyllabicCategory.VowelIndependent),
        (0x0962, 0x0963, IndicSyllabicCategory.VowelDependent),
        (0x0966, 0x096F, IndicSyllabicCategory.Number),
        (0x0972, 0x0977, IndicSyllabicCategory.VowelIndependent),
        (0x0978, 0x097F, IndicSyllabicCategory.Consonant),
        (0x0980, 0x0980, IndicSyllabicCategory.ConsonantPlaceholder),
        (0x0981, 0x0982, IndicSyllabicCategory.Bindu),
        (0x0983, 0x0983, IndicSyllabicCategory.Visarga),
        (0x0985, 0x098C, IndicSyllabicCategory.VowelIndependent),
        (0x098F, 0x0990, IndicSyllabicCategory.VowelIndependent),
        (0x0993, 0x0994, IndicSyllabicCategory.VowelIndependent),
        (0x0995, 0x09A8, IndicSyllabicCategory.Consonant),
        (0x09AA, 0x09B0, IndicSyllabicCategory.Consonant),
        (0x09B2, 0x09B2, IndicSyllabicCategory.Consonant),
        (0x09B6, 0x09B9, IndicSyllabicCategory.Consonant),
        (0x09BC, 0x09BC, IndicSyllabicCategory.Nukta),
        (0x09BD, 0x09BD, IndicSyllabicCategory.Avagraha),
        (0x09BE, 0x09C0, IndicSyllabicCategory.VowelDependent),
        (0x09C1, 0x09C4, IndicSyllabicCategory.VowelDependent),
        (0x09C7, 0x09C8, IndicSyllabicCategory.VowelDependent),
        (0x09CB, 0x09CC, IndicSyllabicCategory.VowelDependent),
        (0x09CD, 0x09CD, IndicSyllabicCategory.Virama),
        (0x09CE, 0x09CE, IndicSyllabicCategory.ConsonantDead),
        (0x09D7, 0x09D7, IndicSyllabicCategory.VowelDependent),
        (0x09DC, 0x09DD, IndicSyllabicCategory.Consonant),
        (0x09DF, 0x09DF, IndicSyllabicCategory.Consonant),
        (0x09E0, 0x09E1, IndicSyllabicCategory.VowelIndependent),
        (0x09E2, 0x09E3, IndicSyllabicCategory.VowelDependent),
        (0x09E6, 0x09EF, IndicSyllabicCategory.Number),
        (0x09F0, 0x09F1, IndicSyllabicCategory.Consonant),
        (0x09FC, 0x09FC, IndicSyllabicCategory.Bindu),
        (0x0A01, 0x0A02, IndicSyllabicCategory.Bindu),
        (0x0A03, 0x0A03, IndicSyllabicCategory.Visarga),
        (0x0A05, 0x0A0A, IndicSyllabicCategory.VowelIndependent),
        (0x0A0F, 0x0A10, IndicSyllabicCategory.VowelIndependent),
        (0x0A13, 0x0A14, IndicSyllabicCategory.VowelIndependent),
        (0x0A15, 0x0A28, IndicSyllabicCategory.Consonant),
        (0x0A2A, 0x0A30, IndicSyllabicCategory.Consonant),
        (0x0A32, 0x0A33, IndicSyllabicCategory.Consonant),
        (0x0A35, 0x0A36, IndicSyllabicCategory.Consonant),
        (0x0A38, 0x0A39, IndicSyllabicCategory.Consonant),
        (0x0A3C, 0x0A3C, IndicSyllabicCategory.Nukta),
        (0x0A3E, 0x0A40, IndicSyllabicCategory.VowelDependent),
        (0x0A41, 0x0A42, IndicSyllabicCategory.VowelDependent),
        (0x0A47, 0x0A48, IndicSyllabicCategory.VowelDependent),
        (0x0A4B, 0x0A4C, IndicSyllabicCategory.VowelDependent),
        (0x0A4D, 0x0A4D, IndicSyllabicCategory.Virama),
        (0x0A59, 0x0A5C, IndicSyllabicCategory.Consonant),
        (0x0A5E, 0x0A5E, IndicSyllabicCategory.Consonant),
        (0x0A66, 0x0A6F, IndicSyllabicCategory.Number),
        (0x0A70, 0x0A70, IndicSyllabicCategory.Bindu),
        (0x0A72, 0x0A73, IndicSyllabicCategory.ConsonantPlaceholder),
        (0x0A75, 0x0A75, IndicSyllabicCategory.ConsonantMedial),
        (0x0A81, 0x0A82, IndicSyllabicCategory.Bindu),
        (0x0A83, 0x0A83, IndicSyllabicCategory.Visarga),
        (0x0A85, 0x0A8D, IndicSyllabicCategory.VowelIndependent),
        (0x0A8F, 0x0A91, IndicSyllabicCategory.VowelIndependent),
        (0x0A93, 0x0A94, IndicSyllabicCategory.VowelIndependent),
        (0x0A95, 0x0AA8, IndicSyllabicCategory.Consonant),
        (0x0AAA, 0x0AB0, IndicSyllabicCategory.Consonant),
        (0x0AB2, 0x0AB3, IndicSyllabicCategory.Consonant),
        (0x0AB5, 0x0AB9, IndicSyllabicCategory.Consonant),
        (0x0ABC, 0x0ABC, IndicSyllabicCategory.Nukta),
        (0x0ABD, 0x0ABD, IndicSyllabicCategory.Avagraha),
        (0x0ABE, 0x0AC0, IndicSyllabicCategory.VowelDependent),
        (0x0AC1, 0x0AC5, IndicSyllabicCategory.VowelDependent),
        (0x0AC7, 0x0AC9, IndicSyllabicCategory.VowelDependent),
        (0x0ACB, 0x0ACC, IndicSyllabicCategory.VowelDependent),
        (0x0ACD, 0x0ACD, IndicSyllabicCategory.Virama),
        (0x0AE0, 0x0AE1, IndicSyllabicCategory.VowelIndependent),
        (0x0AE2, 0x0AE3, IndicSyllabicCategory.VowelDependent),
        (0x0AE6, 0x0AEF, IndicSyllabicCategory.Number),
        (0x0AF9, 0x0AF9, IndicSyllabicCategory.Consonant),
        (0x0B01, 0x0B02, IndicSyllabicCategory.Bindu),
        (0x0B03, 0x0B03, IndicSyllabicCategory.Visarga),
        (0x0B05, 0x0B0C, IndicSyllabicCategory.VowelIndependent),
        (0x0B0F, 0x0B10, IndicSyllabicCategory.VowelIndependent),
        (0x0B13, 0x0B14, IndicSyllabicCategory.VowelIndependent),
        (0x0B15, 0x0B28, IndicSyllabicCategory.Consonant),
        (0x0B2A, 0x0B30, IndicSyllabicCategory.Consonant),
        (0x0B32, 0x0B33, IndicSyllabicCategory.Consonant),
        (0x0B35, 0x0B39, IndicSyllabicCategory.Consonant),
        (0x0B3C, 0x0B3C, IndicSyllabicCategory.Nukta),
        (0x0B3D, 0x0B3D, IndicSyllabicCategory.Avagraha),
        (0x0B3E, 0x0B3E, IndicSyllabicCategory.VowelDependent),
        (0x0B3F, 0x0B40, IndicSyllabicCategory.VowelDependent),
        (0x0B41, 0x0B44, IndicSyllabicCategory.VowelDependent),
        (0x0B47, 0x0B48, IndicSyllabicCategory.VowelDependent),
        (0x0B4B, 0x0B4C, IndicSyllabicCategory.VowelDependent),
        (0x0B4D, 0x0B4D, IndicSyllabicCategory.Virama),
        (0x0B55, 0x0B57, IndicSyllabicCategory.VowelDependent),
        (0x0B5C, 0x0B5D, IndicSyllabicCategory.Consonant),
        (0x0B5F, 0x0B5F, IndicSyllabicCategory.Consonant),
        (0x0B60, 0x0B61, IndicSyllabicCategory.VowelIndependent),
        (0x0B62, 0x0B63, IndicSyllabicCategory.VowelDependent),
        (0x0B66, 0x0B6F, IndicSyllabicCategory.Number),
        (0x0B71, 0x0B71, IndicSyllabicCategory.Consonant),
        (0x0B82, 0x0B82, IndicSyllabicCategory.Bindu),
        (0x0B83, 0x0B83, IndicSyllabicCategory.ModifyingLetter),
        (0x0B85, 0x0B8A, IndicSyllabicCategory.VowelIndependent),
        (0x0B8E, 0x0B90, IndicSyllabicCategory.VowelIndependent),
        (0x0B92, 0x0B94, IndicSyllabicCategory.VowelIndependent),
        (0x0B95, 0x0B95, IndicSyllabicCategory.Consonant),
        (0x0B99, 0x0B9A, IndicSyllabicCategory.Consonant),
        (0x0B9C, 0x0B9C, IndicSyllabicCategory.Consonant),
        (0x0B9E, 0x0B9F, IndicSyllabicCategory.Consonant),
        (0x0BA3, 0x0BA4, IndicSyllabicCategory.Consonant),
        (0x0BA8, 0x0BAA, IndicSyllabicCategory.Consonant),
        (0x0BAE, 0x0BB9, IndicSyllabicCategory.Consonant),
        (0x0BBE, 0x0BBF, IndicSyllabicCategory.VowelDependent),
        (0x0BC0, 0x0BC2, IndicSyllabicCategory.VowelDependent),
        (0x0BC6, 0x0BC8, IndicSyllabicCategory.VowelDependent),
        (0x0BCA, 0x0BCC, IndicSyllabicCategory.VowelDependent),
        (0x0BCD, 0x0BCD, IndicSyllabicCategory.Virama),
        (0x0BD7, 0x0BD7, IndicSyllabicCategory.VowelDependent),
        (0x0BE6, 0x0BEF, IndicSyllabicCategory.Number),
        (0x0C00, 0x0C02, IndicSyllabicCategory.Bindu),
        (0x0C03, 0x0C03, IndicSyllabicCategory.Visarga),
        (0x0C04, 0x0C04, IndicSyllabicCategory.Bindu),
        (0x0C05, 0x0C0C, IndicSyllabicCategory.VowelIndependent),
        (0x0C0E, 0x0C10, IndicSyllabicCategory.VowelIndependent),
        (0x0C12, 0x0C14, IndicSyllabicCategory.VowelIndependent),
        (0x0C15, 0x0C28, IndicSyllabicCategory.Consonant),
        (0x0C2A, 0x0C39, IndicSyllabicCategory.Consonant),
        (0x0C3C, 0x0C3C, IndicSyllabicCategory.Nukta),
        (0x0C3D, 0x0C3D, IndicSyllabicCategory.Avagraha),
        (0x0C3E, 0x0C40, IndicSyllabicCategory.VowelDependent),
        (0x0C41, 0x0C44, IndicSyllabicCategory.VowelDependent),
        (0x0C46, 0x0C48, IndicSyllabicCategory.VowelDependent),
        (0x0C4A, 0x0C4C, IndicSyllabicCategory.VowelDependent),
        (0x0C4D, 0x0C4D, IndicSyllabicCategory.Virama),
        (0x0C55, 0x0C56, IndicSyllabicCategory.VowelDependent),
        (0x0C58, 0x0C5A, IndicSyllabicCategory.Consonant),
        (0x0C5D, 0x0C5D, IndicSyllabicCategory.ConsonantDead),
        (0x0C60, 0x0C61, IndicSyllabicCategory.VowelIndependent),
        (0x0C62, 0x0C63, IndicSyllabicCategory.VowelDependent),
        (0x0C66, 0x0C6F, IndicSyllabicCategory.Number),
        (0x0C80, 0x0C82, IndicSyllabicCategory.Bindu),
        (0x0C83, 0x0C83, IndicSyllabicCategory.Visarga),
        (0x0C85, 0x0C8C, IndicSyllabicCategory.VowelIndependent),
        (0x0C8E, 0x0C90, IndicSyllabicCategory.VowelIndependent),
        (0x0C92, 0x0C94, IndicSyllabicCategory.VowelIndependent),
        (0x0C95, 0x0CA8, IndicSyllabicCategory.Consonant),
        (0x0CAA, 0x0CB3, IndicSyllabicCategory.Consonant),
        (0x0CB5, 0x0CB9, IndicSyllabicCategory.Consonant),
        (0x0CBC, 0x0CBC, IndicSyllabicCategory.Nukta),
        (0x0CBD, 0x0CBD, IndicSyllabicCategory.Avagraha),
        (0x0CBE, 0x0CBF, IndicSyllabicCategory.VowelDependent),
        (0x0CC0, 0x0CC4, IndicSyllabicCategory.VowelDependent),
        (0x0CC6, 0x0CC8, IndicSyllabicCategory.VowelDependent),
        (0x0CCA, 0x0CCC, IndicSyllabicCategory.VowelDependent),
        (0x0CCD, 0x0CCD, IndicSyllabicCategory.Virama),
        (0x0CDD, 0x0CDD, IndicSyllabicCategory.ConsonantDead),
        (0x0CDE, 0x0CDE, IndicSyllabicCategory.Consonant),
        (0x0CE0, 0x0CE1, IndicSyllabicCategory.VowelIndependent),
        (0x0CE2, 0x0CE3, IndicSyllabicCategory.VowelDependent),
        (0x0CE6, 0x0CEF, IndicSyllabicCategory.Number),
        (0x0CF3, 0x0CF3, IndicSyllabicCategory.Bindu),
        (0x0D00, 0x0D02, IndicSyllabicCategory.Bindu),
        (0x0D03, 0x0D03, IndicSyllabicCategory.Visarga),
        (0x0D04, 0x0D04, IndicSyllabicCategory.Bindu),
        (0x0D05, 0x0D0C, IndicSyllabicCategory.VowelIndependent),
        (0x0D0E, 0x0D10, IndicSyllabicCategory.VowelIndependent),
        (0x0D12, 0x0D14, IndicSyllabicCategory.VowelIndependent),
        (0x0D15, 0x0D3A, IndicSyllabicCategory.Consonant),
        (0x0D3D, 0x0D3D, IndicSyllabicCategory.Avagraha),
        (0x0D3E, 0x0D40, IndicSyllabicCategory.VowelDependent),
        (0x0D41, 0x0D44, IndicSyllabicCategory.VowelDependent),
        (0x0D46, 0x0D48, IndicSyllabicCategory.VowelDependent),
        (0x0D4A, 0x0D4C, IndicSyllabicCategory.VowelDependent),
        (0x0D4D, 0x0D4D, IndicSyllabicCategory.Virama),
        (0x0D4E, 0x0D4E, IndicSyllabicCategory.ConsonantPrecedingRepha),
        (0x0D54, 0x0D56, IndicSyllabicCategory.ConsonantDead),
        (0x0D57, 0x0D57, IndicSyllabicCategory.VowelDependent),
        (0x0D5F, 0x0D61, IndicSyllabicCategory.VowelIndependent),
        (0x0D62, 0x0D63, IndicSyllabicCategory.VowelDependent),
        (0x0D66, 0x0D6F, IndicSyllabicCategory.Number),
        (0x0D7A, 0x0D7F, IndicSyllabicCategory.ConsonantDead),
    };

    private static readonly (int Lo, int Hi, IndicPositionalCategory Cat)[] IPCRanges =
    {
        (0x0900, 0x0902, IndicPositionalCategory.Top),
        (0x0903, 0x0903, IndicPositionalCategory.Right),
        (0x093A, 0x093A, IndicPositionalCategory.Top),
        (0x093B, 0x093B, IndicPositionalCategory.Right),
        (0x093C, 0x093C, IndicPositionalCategory.Bottom),
        (0x093E, 0x093E, IndicPositionalCategory.Right),
        (0x093F, 0x093F, IndicPositionalCategory.Left),
        (0x0940, 0x0940, IndicPositionalCategory.Right),
        (0x0941, 0x0944, IndicPositionalCategory.Bottom),
        (0x0945, 0x0948, IndicPositionalCategory.Top),
        (0x0949, 0x094C, IndicPositionalCategory.Right),
        (0x094D, 0x094D, IndicPositionalCategory.Bottom),
        (0x094E, 0x094E, IndicPositionalCategory.Left),
        (0x094F, 0x094F, IndicPositionalCategory.Right),
        (0x0955, 0x0955, IndicPositionalCategory.Top),
        (0x0956, 0x0957, IndicPositionalCategory.Bottom),
        (0x0962, 0x0963, IndicPositionalCategory.Bottom),
        (0x0981, 0x0981, IndicPositionalCategory.Top),
        (0x0982, 0x0983, IndicPositionalCategory.Right),
        (0x09BC, 0x09BC, IndicPositionalCategory.Bottom),
        (0x09BE, 0x09BE, IndicPositionalCategory.Right),
        (0x09BF, 0x09BF, IndicPositionalCategory.Left),
        (0x09C0, 0x09C0, IndicPositionalCategory.Right),
        (0x09C1, 0x09C4, IndicPositionalCategory.Bottom),
        (0x09C7, 0x09C8, IndicPositionalCategory.Left),
        (0x09CB, 0x09CC, IndicPositionalCategory.LeftAndRight),
        (0x09CD, 0x09CD, IndicPositionalCategory.Bottom),
        (0x09D7, 0x09D7, IndicPositionalCategory.Right),
        (0x09E2, 0x09E3, IndicPositionalCategory.Bottom),
        (0x0A01, 0x0A02, IndicPositionalCategory.Top),
        (0x0A03, 0x0A03, IndicPositionalCategory.Right),
        (0x0A3C, 0x0A3C, IndicPositionalCategory.Bottom),
        (0x0A3E, 0x0A3E, IndicPositionalCategory.Right),
        (0x0A3F, 0x0A3F, IndicPositionalCategory.Left),
        (0x0A40, 0x0A40, IndicPositionalCategory.Right),
        (0x0A41, 0x0A42, IndicPositionalCategory.Bottom),
        (0x0A47, 0x0A48, IndicPositionalCategory.Top),
        (0x0A4B, 0x0A4C, IndicPositionalCategory.Top),
        (0x0A4D, 0x0A4D, IndicPositionalCategory.Bottom),
        (0x0A51, 0x0A51, IndicPositionalCategory.Bottom),
        (0x0A70, 0x0A71, IndicPositionalCategory.Top),
        (0x0A75, 0x0A75, IndicPositionalCategory.Bottom),
        (0x0A81, 0x0A82, IndicPositionalCategory.Top),
        (0x0A83, 0x0A83, IndicPositionalCategory.Right),
        (0x0ABC, 0x0ABC, IndicPositionalCategory.Bottom),
        (0x0ABE, 0x0ABE, IndicPositionalCategory.Right),
        (0x0ABF, 0x0ABF, IndicPositionalCategory.Left),
        (0x0AC0, 0x0AC0, IndicPositionalCategory.Right),
        (0x0AC1, 0x0AC4, IndicPositionalCategory.Bottom),
        (0x0AC5, 0x0AC5, IndicPositionalCategory.Top),
        (0x0AC7, 0x0AC8, IndicPositionalCategory.Top),
        (0x0AC9, 0x0AC9, IndicPositionalCategory.TopAndRight),
        (0x0ACB, 0x0ACC, IndicPositionalCategory.Right),
        (0x0ACD, 0x0ACD, IndicPositionalCategory.Bottom),
        (0x0AE2, 0x0AE3, IndicPositionalCategory.Bottom),
        (0x0B01, 0x0B01, IndicPositionalCategory.Top),
        (0x0B02, 0x0B03, IndicPositionalCategory.Right),
        (0x0B3C, 0x0B3C, IndicPositionalCategory.Bottom),
        (0x0B3E, 0x0B3E, IndicPositionalCategory.Right),
        (0x0B3F, 0x0B3F, IndicPositionalCategory.Top),
        (0x0B40, 0x0B40, IndicPositionalCategory.Right),
        (0x0B41, 0x0B44, IndicPositionalCategory.Bottom),
        (0x0B47, 0x0B47, IndicPositionalCategory.Left),
        (0x0B48, 0x0B48, IndicPositionalCategory.TopAndLeft),
        (0x0B4B, 0x0B4B, IndicPositionalCategory.LeftAndRight),
        (0x0B4C, 0x0B4C, IndicPositionalCategory.TopAndLeftAndRight),
        (0x0B4D, 0x0B4D, IndicPositionalCategory.Bottom),
        (0x0B55, 0x0B56, IndicPositionalCategory.Top),
        (0x0B57, 0x0B57, IndicPositionalCategory.TopAndRight),
        (0x0B62, 0x0B63, IndicPositionalCategory.Bottom),
        (0x0B82, 0x0B82, IndicPositionalCategory.Top),
        (0x0BBE, 0x0BBF, IndicPositionalCategory.Right),
        (0x0BC0, 0x0BC0, IndicPositionalCategory.Top),
        (0x0BC1, 0x0BC2, IndicPositionalCategory.Right),
        (0x0BC6, 0x0BC8, IndicPositionalCategory.Left),
        (0x0BCA, 0x0BCC, IndicPositionalCategory.LeftAndRight),
        (0x0BCD, 0x0BCD, IndicPositionalCategory.Top),
        (0x0BD7, 0x0BD7, IndicPositionalCategory.Right),
        (0x0C00, 0x0C00, IndicPositionalCategory.Top),
        (0x0C01, 0x0C03, IndicPositionalCategory.Right),
        (0x0C04, 0x0C04, IndicPositionalCategory.Top),
        (0x0C3C, 0x0C3C, IndicPositionalCategory.Bottom),
        (0x0C3E, 0x0C40, IndicPositionalCategory.Top),
        (0x0C41, 0x0C44, IndicPositionalCategory.Right),
        (0x0C46, 0x0C47, IndicPositionalCategory.Top),
        (0x0C48, 0x0C48, IndicPositionalCategory.TopAndBottom),
        (0x0C4A, 0x0C4D, IndicPositionalCategory.Top),
        (0x0C55, 0x0C55, IndicPositionalCategory.Top),
        (0x0C56, 0x0C56, IndicPositionalCategory.Bottom),
        (0x0C62, 0x0C63, IndicPositionalCategory.Bottom),
        (0x0C81, 0x0C81, IndicPositionalCategory.Top),
        (0x0C82, 0x0C83, IndicPositionalCategory.Right),
        (0x0CBC, 0x0CBC, IndicPositionalCategory.Bottom),
        (0x0CBE, 0x0CBE, IndicPositionalCategory.Right),
        (0x0CBF, 0x0CBF, IndicPositionalCategory.Top),
        (0x0CC0, 0x0CC0, IndicPositionalCategory.TopAndRight),
        (0x0CC1, 0x0CC4, IndicPositionalCategory.Right),
        (0x0CC6, 0x0CC6, IndicPositionalCategory.Top),
        (0x0CC7, 0x0CC8, IndicPositionalCategory.TopAndRight),
        (0x0CCA, 0x0CCB, IndicPositionalCategory.TopAndRight),
        (0x0CCC, 0x0CCD, IndicPositionalCategory.Top),
        (0x0CD5, 0x0CD6, IndicPositionalCategory.Right),
        (0x0CE2, 0x0CE3, IndicPositionalCategory.Bottom),
        (0x0CF3, 0x0CF3, IndicPositionalCategory.Right),
        (0x0D00, 0x0D01, IndicPositionalCategory.Top),
        (0x0D02, 0x0D03, IndicPositionalCategory.Right),
        (0x0D3B, 0x0D3C, IndicPositionalCategory.Top),
        (0x0D3E, 0x0D40, IndicPositionalCategory.Right),
        (0x0D41, 0x0D44, IndicPositionalCategory.Bottom),
        (0x0D46, 0x0D48, IndicPositionalCategory.Left),
        (0x0D4A, 0x0D4C, IndicPositionalCategory.LeftAndRight),
        (0x0D4D, 0x0D4E, IndicPositionalCategory.Top),
        (0x0D57, 0x0D57, IndicPositionalCategory.Right),
        (0x0D62, 0x0D63, IndicPositionalCategory.Bottom),
    };

    // LiPi Sans Left_And_Right vowel decompositions.
    // Glyph ids sourced from LiPi Sans cmap tables (font-specific constants).
    private static readonly Dictionary<LipiScript, IReadOnlyList<VowelDecomposition>> DecompositionMap =
        new Dictionary<LipiScript, IReadOnlyList<VowelDecomposition>>
        {
            [LipiScript.Tamil] = new VowelDecomposition[]
            {
                new VowelDecomposition(0x0BCA, 46, 41),   // o-short: left=e-sign(46)  right=aa-sign(41)
                new VowelDecomposition(0x0BCB, 47, 41),   // o:       left=ee-sign(47) right=aa-sign(41)
                new VowelDecomposition(0x0BCC, 46, 54),   // au:      left=e-sign(46)  right=au-length-mark(54)
            },
            [LipiScript.Bengali] = new VowelDecomposition[]
            {
                new VowelDecomposition(0x09CB, 66, 59),   // o:  left=e-sign(66)  right=aa-sign(59)
                new VowelDecomposition(0x09CC, 66, 72),   // au: left=e-sign(66)  right=au-length-mark(72)
            },
            [LipiScript.Odia] = new VowelDecomposition[]
            {
                new VowelDecomposition(0x0B4B, 69, 59),   // o:  left=e-sign(69)  right=aa-sign(59)
                new VowelDecomposition(0x0B4C, 70, 76),   // au: left=ai-sign(70) right=au-length-mark(76)
            },
            [LipiScript.Malayalam] = new VowelDecomposition[]
            {
                new VowelDecomposition(0x0D4A, 71, 64),   // o:  left=e-sign(71)  right=aa-sign(64)
                new VowelDecomposition(0x0D4B, 72, 64),   // oo: left=ee-sign(72) right=aa-sign(64)
                new VowelDecomposition(0x0D4C, 71, 83),   // au: left=e-sign(71)  right=au-length-mark(83)
            },
        };

    /// <summary>Returns the IndicSyllabicCategory for a Unicode code point.</summary>
    internal static IndicSyllabicCategory GetSyllabicCategory(int codepoint)
    {
        int lo = 0;
        int hi = ISCRanges.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            (int rangeLo, int rangeHi, IndicSyllabicCategory cat) = ISCRanges[mid];
            if (codepoint < rangeLo) { hi = mid - 1; }
            else if (codepoint > rangeHi) { lo = mid + 1; }
            else { return cat; }
        }

        return IndicSyllabicCategory.Other;
    }

    /// <summary>Returns the IndicPositionalCategory for a Unicode code point.</summary>
    internal static IndicPositionalCategory GetPositionalCategory(int codepoint)
    {
        int lo = 0;
        int hi = IPCRanges.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            (int rangeLo, int rangeHi, IndicPositionalCategory cat) = IPCRanges[mid];
            if (codepoint < rangeLo) { hi = mid - 1; }
            else if (codepoint > rangeHi) { lo = mid + 1; }
            else { return cat; }
        }

        return IndicPositionalCategory.NA;
    }

    /// <summary>
    /// Returns the Left_And_Right vowel decompositions for a script, or an empty
    /// list when the script has none.
    /// </summary>
    internal static IReadOnlyList<VowelDecomposition> GetDecompositions(LipiScript script)
        => DecompositionMap.TryGetValue(script, out IReadOnlyList<VowelDecomposition>? list)
            ? list
            : Array.Empty<VowelDecomposition>();
}
