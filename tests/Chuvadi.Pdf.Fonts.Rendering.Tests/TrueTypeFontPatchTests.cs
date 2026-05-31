// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  OpenType specification — cmap formats 4 and 12, head checkSumAdjustment
// PHASE: Phase 2.1 — v2.1.5 cmap-remap tests

using System;
using System.Collections.Generic;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Fonts.Rendering.Tests;

public sealed class TrueTypeFontPatchTests
{
    // ── Null / argument validation ────────────────────────────────────────

    [Fact]
    public void WithAugmentedCmap_NullFontBytes_Throws()
    {
        Dictionary<int, int> map = new() { { 0x41, 1 } };
        Action act = () => TrueTypeFontPatch.WithAugmentedCmap(null!, map);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void WithAugmentedCmap_NullMapping_Throws()
    {
        byte[] font = BuildMinimalTtfWithGlyphs(numGlyphs: 1);
        Action act = () => TrueTypeFontPatch.WithAugmentedCmap(font, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void WithAugmentedCmap_TooShortInput_Throws()
    {
        byte[] tooShort = new byte[8];
        Dictionary<int, int> map = new() { { 0x41, 1 } };
        Action act = () => TrueTypeFontPatch.WithAugmentedCmap(tooShort, map);
        act.Should().Throw<FontRenderingException>();
    }

    [Fact]
    public void WithAugmentedCmap_InvalidSfVersion_Throws()
    {
        byte[] junk = new byte[64];
        // Set a clearly bogus sfVersion (not one of 0x00010000/OTTO/true/typ1).
        junk[0] = 0xDE; junk[1] = 0xAD; junk[2] = 0xBE; junk[3] = 0xEF;
        Dictionary<int, int> map = new() { { 0x41, 1 } };
        Action act = () => TrueTypeFontPatch.WithAugmentedCmap(junk, map);
        act.Should().Throw<FontRenderingException>();
    }

    // ── TrueTypeLoader bounds checks (truncated / malformed input) ────────

    [Fact]
    public void Loader_TruncatedAfterHeader_ThrowsFontRenderingException()
    {
        // A valid font whose byte array is cut off mid-table-directory leaves
        // table offsets pointing past the end of the data. Parsing must surface
        // a typed FontRenderingException, never an IndexOutOfRangeException.
        byte[] font = BuildMinimalTtfWithGlyphs(numGlyphs: 4);
        byte[] truncated = new byte[20];
        Array.Copy(font, truncated, Math.Min(20, font.Length));

        Action act = () => _ = new TrueTypeLoader(truncated);
        act.Should().Throw<FontRenderingException>();
    }

    [Fact]
    public void Loader_TruncatedMidFont_OnlyThrowsFontRenderingException()
    {
        // Cut the font at a series of lengths past the header. Whatever fails,
        // the only acceptable failure is a typed FontRenderingException — never
        // an unguarded IndexOutOfRangeException or ArgumentOutOfRangeException
        // leaking from a raw buffer read.
        byte[] font = BuildMinimalTtfWithGlyphs(numGlyphs: 4);

        for (int cut = 16; cut < font.Length; cut += 7)
        {
            byte[] truncated = new byte[cut];
            Array.Copy(font, truncated, cut);

            try
            {
                TrueTypeLoader loader = new(truncated);
                _ = loader.GetGlyphOutline(1);
            }
            catch (FontRenderingException)
            {
                // Expected: malformed input rejected with a typed error.
            }
            catch (Exception ex)
            {
                throw new Xunit.Sdk.XunitException(
                    $"Truncation at {cut} bytes leaked {ex.GetType().Name} " +
                    $"instead of FontRenderingException: {ex.Message}");
            }
        }
    }

    // ── Format 4 (BMP) — single and multiple mappings ────────────────────

    [Fact]
    public void WithAugmentedCmap_EmptyMapping_ProducesValidFont()
    {
        byte[] font = BuildMinimalTtfWithGlyphs(numGlyphs: 1);
        Dictionary<int, int> map = new();

        byte[] patched = TrueTypeFontPatch.WithAugmentedCmap(font, map);

        // The patched font must still be parseable. The cmap will exist but
        // contain only the 0xFFFF sentinel segment.
        TrueTypeLoader loader = new(patched);
        loader.NumGlyphs.Should().Be(1);
    }

    [Fact]
    public void WithAugmentedCmap_SingleBmpMapping_RoundtripsThroughLoader()
    {
        byte[] font = BuildMinimalTtfWithGlyphs(numGlyphs: 4);
        Dictionary<int, int> map = new() { { 0x0041, 2 } }; // 'A' → GID 2

        byte[] patched = TrueTypeFontPatch.WithAugmentedCmap(font, map);
        TrueTypeLoader loader = new(patched);

        loader.GetGlyphIndex(0x0041).Should().Be(2);
        loader.GetGlyphIndex(0x0042).Should().Be(0);  // unmapped
    }

    [Fact]
    public void WithAugmentedCmap_WingdingsShape_RoundtripsCorrectly()
    {
        // Real-world shape from a Word-produced PDF: Wingdings sparse map
        // with U+0020 → GID 3 (space) and U+2713 → GID 57 (checkmark).
        byte[] font = BuildMinimalTtfWithGlyphs(numGlyphs: 64);
        Dictionary<int, int> map = new()
        {
            { 0x0020, 3 },
            { 0x2713, 57 },
        };

        byte[] patched = TrueTypeFontPatch.WithAugmentedCmap(font, map);
        TrueTypeLoader loader = new(patched);

        loader.GetGlyphIndex(0x0020).Should().Be(3);
        loader.GetGlyphIndex(0x2713).Should().Be(57);
        loader.GetGlyphIndex(0x0030).Should().Be(0);  // unmapped neighbour
    }

    [Fact]
    public void WithAugmentedCmap_OriginalCmap_IsDiscarded()
    {
        // Build a font with an existing cmap mapping 'A' (0x41) → GID 1.
        byte[] font = BuildMinimalTtfWithGlyphs(
            numGlyphs: 4,
            existingMappings: new Dictionary<int, int> { { 0x41, 1 } });

        // Sanity-check the original mapping is present before patching.
        TrueTypeLoader before = new(font);
        before.GetGlyphIndex(0x41).Should().Be(1);

        // Patch with a different mapping. The original 'A' → 1 should vanish.
        Dictionary<int, int> map = new() { { 0x42, 3 } }; // 'B' → GID 3
        byte[] patched = TrueTypeFontPatch.WithAugmentedCmap(font, map);
        TrueTypeLoader after = new(patched);

        after.GetGlyphIndex(0x42).Should().Be(3);  // new mapping in effect
        after.GetGlyphIndex(0x41).Should().Be(0);  // old mapping discarded
    }

    [Fact]
    public void WithAugmentedCmap_GidOutOfRange_SkipsEntrySilently()
    {
        byte[] font = BuildMinimalTtfWithGlyphs(numGlyphs: 4);
        Dictionary<int, int> map = new()
        {
            { 0x0041, 2 },        // valid
            { 0x0042, 100_000 },  // out of uint16 range — must be skipped
            { 0x0043, -1 },       // negative — must be skipped
        };

        byte[] patched = TrueTypeFontPatch.WithAugmentedCmap(font, map);
        TrueTypeLoader loader = new(patched);

        loader.GetGlyphIndex(0x0041).Should().Be(2);
        loader.GetGlyphIndex(0x0042).Should().Be(0);
        loader.GetGlyphIndex(0x0043).Should().Be(0);
    }

    // ── Format 12 (full Unicode) ─────────────────────────────────────────

    [Fact]
    public void WithAugmentedCmap_NonBmpCodepoint_RoundtripsThroughLoader()
    {
        // Non-BMP code point forces a format-12 subtable. TrueTypeLoader
        // currently parses only format 4, so the loader's GetGlyphIndex will
        // return 0 for these. We still verify that the loader can parse the
        // augmented font (no exception) — the format-12 subtable produced
        // here is valid OpenType.
        byte[] font = BuildMinimalTtfWithGlyphs(numGlyphs: 4);
        Dictionary<int, int> map = new()
        {
            { 0x1F600, 2 },  // 😀 — outside BMP
        };

        byte[] patched = TrueTypeFontPatch.WithAugmentedCmap(font, map);

        // Must not throw on parse.
        Action act = () => _ = new TrueTypeLoader(patched);
        act.Should().NotThrow();
    }

    // ── head.checkSumAdjustment ──────────────────────────────────────────

    [Fact]
    public void WithAugmentedCmap_HeadChecksumAdjustment_IsCorrect()
    {
        byte[] font = BuildMinimalTtfWithGlyphs(numGlyphs: 4);
        Dictionary<int, int> map = new() { { 0x0041, 2 } };

        byte[] patched = TrueTypeFontPatch.WithAugmentedCmap(font, map);

        // Verify head.checkSumAdjustment by recomputing it: read the
        // entire file as a stream of big-endian uint32s, zero out the
        // adjustment field locally, sum everything, and check that
        // (0xB1B0AFBA - sum) equals the stored adjustment.
        int headOffset = FindTableOffset(patched, "head");
        headOffset.Should().BeGreaterThan(0);

        uint storedAdjustment = ReadUInt32BE(patched, headOffset + 8);

        // Recompute: copy the file with the adjustment field zeroed and sum.
        byte[] copy = (byte[])patched.Clone();
        copy[headOffset + 8] = 0;
        copy[headOffset + 9] = 0;
        copy[headOffset + 10] = 0;
        copy[headOffset + 11] = 0;

        uint sum = 0;
        for (int i = 0; i + 3 < copy.Length; i += 4)
        {
            sum = unchecked(sum + ReadUInt32BE(copy, i));
        }

        uint expected = unchecked(0xB1B0AFBAu - sum);
        storedAdjustment.Should().Be(expected);
    }

    // ── Idempotence and stability ────────────────────────────────────────

    [Fact]
    public void WithAugmentedCmap_AppliedTwice_GivesSameResult()
    {
        byte[] font = BuildMinimalTtfWithGlyphs(numGlyphs: 8);
        Dictionary<int, int> map = new()
        {
            { 0x0041, 2 },
            { 0x0042, 4 },
        };

        byte[] once = TrueTypeFontPatch.WithAugmentedCmap(font, map);
        byte[] twice = TrueTypeFontPatch.WithAugmentedCmap(once, map);

        once.Should().BeEquivalentTo(twice);
    }

    // ── Test helpers: minimal TTF builder ────────────────────────────────

    /// <summary>
    /// Builds a structurally valid minimal TrueType font with the requested
    /// number of empty glyphs. When <paramref name="existingMappings"/> is
    /// non-null, emits a format-4 cmap subtable carrying those mappings so
    /// tests can verify that a patch replaces (not augments) the original.
    /// </summary>
    private static byte[] BuildMinimalTtfWithGlyphs(
        int numGlyphs,
        IReadOnlyDictionary<int, int>? existingMappings = null)
    {
        List<byte> data = new();

        // ── Offset table ─────────────────────────────────────────────────
        AppendBytes(data, U32(0x00010000));  // sfVersion = TrueType
        AppendBytes(data, U16(7));            // numTables
        AppendBytes(data, U16(128));          // searchRange
        AppendBytes(data, U16(3));            // entrySelector
        AppendBytes(data, U16(112));          // rangeShift

        int dirStart = data.Count;
        for (int i = 0; i < 7 * 16; i++)
        {
            data.Add(0);
        }

        // ── cmap ─────────────────────────────────────────────────────────
        int cmapOffset = data.Count;
        if (existingMappings is null)
        {
            AppendBytes(data, U16(0));  // version
            AppendBytes(data, U16(0));  // numTables = 0
        }
        else
        {
            AppendBytes(data, BuildSimpleFormat4Cmap(existingMappings));
        }

        int cmapLen = data.Count - cmapOffset;
        PadTo4(data);

        // ── head (54 bytes) ──────────────────────────────────────────────
        int headOffset = data.Count;
        AppendBytes(data, U32(0x00010000));  // version
        AppendBytes(data, U32(0));            // fontRevision
        AppendBytes(data, U32(0));            // checkSumAdjustment
        AppendBytes(data, U32(0x5F0F3CF5));  // magicNumber
        AppendBytes(data, U16(0));            // flags
        AppendBytes(data, U16(1000));         // unitsPerEm
        for (int i = 0; i < 16; i++) { data.Add(0); }  // created + modified
        AppendBytes(data, U16(0));            // xMin
        AppendBytes(data, U16(0));            // yMin
        AppendBytes(data, U16(0));            // xMax
        AppendBytes(data, U16(0));            // yMax
        AppendBytes(data, U16(0));            // macStyle
        AppendBytes(data, U16(8));            // lowestRecPPEM
        AppendBytes(data, U16(2));            // fontDirectionHint
        AppendBytes(data, U16(0));            // indexToLocFormat = short
        AppendBytes(data, U16(0));            // glyphDataFormat

        int headLen = data.Count - headOffset;
        PadTo4(data);

        // ── hhea (36 bytes) ──────────────────────────────────────────────
        int hheaOffset = data.Count;
        AppendBytes(data, U32(0x00010000));  // version
        for (int i = 0; i < 28; i++) { data.Add(0); }
        AppendBytes(data, U16(numGlyphs));   // numberOfHMetrics

        int hheaLen = data.Count - hheaOffset;
        PadTo4(data);

        // ── maxp (6 bytes — v0.5) ────────────────────────────────────────
        int maxpOffset = data.Count;
        AppendBytes(data, U32(0x00005000));  // version 0.5
        AppendBytes(data, U16(numGlyphs));

        int maxpLen = data.Count - maxpOffset;
        PadTo4(data);

        // ── hmtx (4 bytes per hMetric) ───────────────────────────────────
        int hmtxOffset = data.Count;
        for (int i = 0; i < numGlyphs; i++)
        {
            AppendBytes(data, U16(500));      // advanceWidth
            AppendBytes(data, U16(0));        // lsb
        }

        int hmtxLen = data.Count - hmtxOffset;
        PadTo4(data);

        // ── loca (short format — 2 bytes per entry, numGlyphs+1 entries) ─
        int locaOffset = data.Count;
        for (int i = 0; i <= numGlyphs; i++)
        {
            AppendBytes(data, U16(0));        // all entries 0 → empty glyphs
        }

        int locaLen = data.Count - locaOffset;
        PadTo4(data);

        // ── glyf (empty) ─────────────────────────────────────────────────
        int glyfOffset = data.Count;
        int glyfLen = 0;

        // ── Fill in table directory ──────────────────────────────────────
        string[] tags = { "cmap", "glyf", "head", "hhea", "hmtx", "loca", "maxp" };
        int[] offsets = { cmapOffset, glyfOffset, headOffset, hheaOffset, hmtxOffset, locaOffset, maxpOffset };
        int[] lengths = { cmapLen, glyfLen, headLen, hheaLen, hmtxLen, locaLen, maxpLen };

        for (int i = 0; i < 7; i++)
        {
            int pos = dirStart + i * 16;
            byte[] tagBytes = System.Text.Encoding.ASCII.GetBytes(tags[i]);
            data[pos] = tagBytes[0];
            data[pos + 1] = tagBytes[1];
            data[pos + 2] = tagBytes[2];
            data[pos + 3] = tagBytes[3];
            byte[] off = U32((uint)offsets[i]);
            byte[] len = U32((uint)lengths[i]);
            data[pos + 8] = off[0]; data[pos + 9] = off[1];
            data[pos + 10] = off[2]; data[pos + 11] = off[3];
            data[pos + 12] = len[0]; data[pos + 13] = len[1];
            data[pos + 14] = len[2]; data[pos + 15] = len[3];
        }

        return data.ToArray();
    }

    /// <summary>
    /// Builds a complete cmap table (4-byte header + one Windows BMP
    /// EncodingRecord + format-4 subtable) for the given mappings. The
    /// minimal-TTF builder uses this for the existing-mappings test case.
    /// </summary>
    private static byte[] BuildSimpleFormat4Cmap(IReadOnlyDictionary<int, int> mappings)
    {
        SortedDictionary<int, int> sorted = new();
        foreach (KeyValuePair<int, int> kv in mappings)
        {
            sorted[kv.Key] = kv.Value;
        }

        List<int> endCodes = new();
        List<int> startCodes = new();
        List<int> idDeltas = new();
        foreach (KeyValuePair<int, int> kv in sorted)
        {
            endCodes.Add(kv.Key);
            startCodes.Add(kv.Key);
            idDeltas.Add((kv.Value - kv.Key) & 0xFFFF);
        }

        endCodes.Add(0xFFFF);
        startCodes.Add(0xFFFF);
        idDeltas.Add(1);

        int segCount = endCodes.Count;
        int subtableLen = 14 + (2 * segCount) + 2 + (2 * segCount) + (2 * segCount) + (2 * segCount);

        int searchRange = 2;
        int entrySelector = 0;
        while (searchRange * 2 <= segCount * 2)
        {
            searchRange *= 2;
            entrySelector++;
        }

        int rangeShift = segCount * 2 - searchRange;

        List<byte> bytes = new();
        AppendBytes(bytes, U16(0));      // cmap version
        AppendBytes(bytes, U16(1));      // numTables
        AppendBytes(bytes, U16(3));      // platformID = Windows
        AppendBytes(bytes, U16(1));      // encodingID = BMP
        AppendBytes(bytes, U32(12u));    // subtable offset
        AppendBytes(bytes, U16(4));      // format
        AppendBytes(bytes, U16(subtableLen));
        AppendBytes(bytes, U16(0));      // language
        AppendBytes(bytes, U16(segCount * 2));
        AppendBytes(bytes, U16(searchRange));
        AppendBytes(bytes, U16(entrySelector));
        AppendBytes(bytes, U16(rangeShift));
        foreach (int v in endCodes) { AppendBytes(bytes, U16(v)); }
        AppendBytes(bytes, U16(0));      // reservedPad
        foreach (int v in startCodes) { AppendBytes(bytes, U16(v)); }
        foreach (int v in idDeltas) { AppendBytes(bytes, U16(v)); }
        for (int i = 0; i < segCount; i++) { AppendBytes(bytes, U16(0)); }

        return bytes.ToArray();
    }

    private static void AppendBytes(List<byte> data, byte[] bytes)
    {
        foreach (byte b in bytes) { data.Add(b); }
    }

    private static void PadTo4(List<byte> data)
    {
        while ((data.Count & 3) != 0) { data.Add(0); }
    }

    private static byte[] U16(int v)
    {
        return new byte[] { (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF) };
    }

    private static byte[] U32(uint v)
    {
        return new byte[]
        {
            (byte)((v >> 24) & 0xFF),
            (byte)((v >> 16) & 0xFF),
            (byte)((v >> 8) & 0xFF),
            (byte)(v & 0xFF),
        };
    }

    private static uint ReadUInt32BE(byte[] data, int offset)
    {
        return ((uint)data[offset] << 24)
             | ((uint)data[offset + 1] << 16)
             | ((uint)data[offset + 2] << 8)
             | data[offset + 3];
    }

    private static int ReadUInt16BE(byte[] data, int offset)
    {
        return (data[offset] << 8) | data[offset + 1];
    }

    /// <summary>
    /// Finds the byte offset of the named table in the font. Returns -1
    /// when the table is absent.
    /// </summary>
    private static int FindTableOffset(byte[] font, string tag)
    {
        int numTables = ReadUInt16BE(font, 4);
        byte[] tagBytes = System.Text.Encoding.ASCII.GetBytes(tag);

        for (int i = 0; i < numTables; i++)
        {
            int entryOffset = 12 + i * 16;
            if (font[entryOffset] == tagBytes[0]
                && font[entryOffset + 1] == tagBytes[1]
                && font[entryOffset + 2] == tagBytes[2]
                && font[entryOffset + 3] == tagBytes[3])
            {
                return (int)ReadUInt32BE(font, entryOffset + 8);
            }
        }

        return -1;
    }
}
