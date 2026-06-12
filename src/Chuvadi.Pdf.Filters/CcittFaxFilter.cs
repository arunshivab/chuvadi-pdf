// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.4.6 — CCITTFaxDecode; ITU-T T.4 (Group 3,
//        MH/MR coding, code tables 2/3) and T.6 (Group 4, MMR coding)
// PHASE: Phase 2.9 — Reader feature batch (scanned-document support)
// Decodes CCITT Group 3 / Group 4 fax-compressed image data.

using System;
using System.Collections.Generic;
using System.IO;

namespace Chuvadi.Pdf.Filters;

/// <summary>
/// Implements the <c>CCITTFaxDecode</c> filter: Group 3 one-dimensional
/// (Modified Huffman), Group 3 two-dimensional (Modified READ), and Group 4
/// (Modified Modified READ) decoding of bilevel image data, as used by
/// scanned-document PDFs.
/// </summary>
/// <remarks>
/// <para>
/// The output is packed one-bit-per-pixel rows, most significant bit first,
/// each row padded to a byte boundary. With the default
/// <c>BlackIs1 = false</c>, black pixels decode to 0 bits and white to 1
/// bits, per PDF 32000-1:2008 Table 11.
/// </para>
/// <para>
/// Encoding is not supported; <see cref="Encode"/> throws
/// <see cref="FilterException"/>. Chuvadi writes bilevel images with Flate,
/// which modern consumers handle universally.
/// </para>
/// </remarks>
public sealed class CcittFaxFilter : IStreamFilter
{
    // CCITTFaxDecode's /Columns default differs from the shared
    // FilterParameters default of 1 (PDF 32000-1:2008 Table 11).
    private const int DefaultColumns = 1728;

    /// <inheritdoc />
    public string FilterName => "CCITTFaxDecode";

    /// <inheritdoc />
    public void Decode(Stream input, Stream output, FilterParameters? decodeParms = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        int columns = decodeParms is null || !decodeParms.ColumnsSpecified
            ? DefaultColumns
            : Math.Max(1, decodeParms.Columns);
        int k = decodeParms?.CcittK ?? 0;
        int rows = decodeParms?.Rows ?? 0;
        bool blackIs1 = decodeParms?.BlackIs1 ?? false;
        bool byteAlign = decodeParms?.EncodedByteAlign ?? false;

        using MemoryStream buffer = new();
        input.CopyTo(buffer);
        byte[] data = buffer.ToArray();

        Decoder decoder = new(data, columns, k, rows, blackIs1, byteAlign);
        decoder.DecodeTo(output);
    }

    /// <inheritdoc />
    public void Encode(Stream input, Stream output, FilterParameters? encodeParms = null)
    {
        throw new FilterException(
            "CCITTFaxDecode encoding is not supported; write bilevel images with FlateDecode instead.");
    }

    // ── Decoder ───────────────────────────────────────────────────────────

    private sealed class Decoder
    {
        private readonly byte[] _data;
        private readonly int _columns;
        private readonly int _k;
        private readonly int _rows;
        private readonly bool _blackIs1;
        private readonly bool _byteAlign;

        private int _bitPosition;

        internal Decoder(byte[] data, int columns, int k, int rows, bool blackIs1, bool byteAlign)
        {
            _data = data;
            _columns = columns;
            _k = k;
            _rows = rows;
            _blackIs1 = blackIs1;
            _byteAlign = byteAlign;
        }

        internal void DecodeTo(Stream output)
        {
            // Reference line for 2D rows: changing-element positions of the
            // row above. An all-white row has no changes.
            List<int> reference = new();
            List<int> current = new();
            int decodedRows = 0;

            while (_rows <= 0 || decodedRows < _rows)
            {
                if (_byteAlign)
                {
                    AlignToByte();
                }

                if (BitsRemaining() <= 0)
                {
                    break;
                }

                bool twoDimensional;
                if (_k < 0)
                {
                    // Group 4: every row is 2D; an EOL here is EOFB.
                    if (PeekEol())
                    {
                        break;
                    }
                    twoDimensional = true;
                }
                else if (_k == 0)
                {
                    // Group 3 1-D: rows may be separated by optional EOLs.
                    if (!SkipEolsAndFill())
                    {
                        break;
                    }
                    twoDimensional = false;
                }
                else
                {
                    // Group 3 2-D: each row is introduced by EOL + a tag bit
                    // (1 = the next row is 1-D, 0 = 2-D). The first row of a
                    // block may omit the EOL in practice; tolerate that by
                    // treating a missing EOL as a 1-D row only at row 0.
                    bool sawEol = SkipEolsRequired();
                    if (BitsRemaining() <= 0)
                    {
                        break;
                    }
                    if (sawEol)
                    {
                        twoDimensional = ReadBit() == 0;
                    }
                    else if (decodedRows == 0)
                    {
                        twoDimensional = false;
                    }
                    else
                    {
                        break;
                    }
                }

                current.Clear();
                bool ok = twoDimensional
                    ? DecodeRow2D(reference, current)
                    : DecodeRow1D(current);
                if (!ok)
                {
                    break;
                }

                WriteRow(output, current);
                decodedRows++;

                List<int> swap = reference;
                reference = current;
                current = swap;
            }

            if (decodedRows == 0)
            {
                throw new FilterException("CCITTFaxDecode: no rows could be decoded.");
            }
        }

        // ── Row decoding ──────────────────────────────────────────────────

        // 1-D Modified Huffman: alternating white/black run lengths.
        private bool DecodeRow1D(List<int> changes)
        {
            int position = 0;
            bool white = true;

            while (position < _columns)
            {
                int run = ReadRunLength(white);
                if (run < 0)
                {
                    return changes.Count > 0 || position > 0 ? Fail() : false;
                }

                position += run;
                if (position > _columns)
                {
                    position = _columns;
                }
                if (position < _columns || !white)
                {
                    changes.Add(Math.Min(position, _columns));
                }
                if (position >= _columns)
                {
                    break;
                }
                white = !white;
            }

            return true;
        }

        // 2-D Modified (Modified) READ: vertical, horizontal, and pass modes
        // against the reference line's changing elements.
        private bool DecodeRow2D(List<int> reference, List<int> changes)
        {
            int a0 = -1;
            bool white = true;

            while (a0 < _columns)
            {
                Mode mode = ReadMode();
                if (mode == Mode.EndOfData)
                {
                    return changes.Count > 0 ? Fail() : false;
                }
                if (mode == Mode.Invalid)
                {
                    return Fail();
                }

                (int b1, int b2) = FindReferenceElements(reference, a0, white);

                if (mode == Mode.Pass)
                {
                    a0 = b2;
                    continue;
                }

                if (mode == Mode.Horizontal)
                {
                    int start = a0 < 0 ? 0 : a0;
                    int run1 = ReadRunLength(white);
                    int run2 = run1 < 0 ? -1 : ReadRunLength(!white);
                    if (run1 < 0 || run2 < 0)
                    {
                        return Fail();
                    }

                    int a1 = start + run1;
                    int a2 = a1 + run2;
                    changes.Add(Math.Min(a1, _columns));
                    changes.Add(Math.Min(a2, _columns));
                    a0 = a2;
                    continue;
                }

                // Vertical modes: a1 = b1 + offset, colour flips.
                int offset = mode switch
                {
                    Mode.Vertical0 => 0,
                    Mode.VerticalR1 => 1,
                    Mode.VerticalR2 => 2,
                    Mode.VerticalR3 => 3,
                    Mode.VerticalL1 => -1,
                    Mode.VerticalL2 => -2,
                    _ => -3,
                };
                int vertical = b1 + offset;
                if (vertical < 0)
                {
                    vertical = 0;
                }
                changes.Add(Math.Min(vertical, _columns));
                a0 = vertical;
                white = !white;
            }

            return true;
        }

        private static bool Fail()
        {
            throw new FilterException("CCITTFaxDecode: corrupt or truncated coding data.");
        }

        // b1: first reference change > a0 whose new colour is opposite to
        // the current colour; b2: the next change after b1. Changing
        // elements alternate, starting with a white→black transition.
        private (int B1, int B2) FindReferenceElements(List<int> reference, int a0, bool white)
        {
            int wantParity = white ? 0 : 1;
            int index = 0;
            while (index < reference.Count &&
                   (reference[index] <= a0 || (index & 1) != wantParity))
            {
                index++;
            }

            int b1 = index < reference.Count ? reference[index] : _columns;
            int b2 = index + 1 < reference.Count ? reference[index + 1] : _columns;
            return (b1, b2);
        }

        // ── Output packing ────────────────────────────────────────────────

        private void WriteRow(Stream output, List<int> changes)
        {
            int stride = (_columns + 7) / 8;
            byte[] row = new byte[stride];

            // Start white; the polarity of the packed bits follows BlackIs1.
            byte whiteFill = _blackIs1 ? (byte)0x00 : (byte)0xFF;
            for (int i = 0; i < stride; i++)
            {
                row[i] = whiteFill;
            }

            // Paint black runs: changes alternate white→black, black→white.
            for (int i = 0; i + 1 <= changes.Count; i += 2)
            {
                int blackStart = changes[i];
                int blackEnd = i + 1 < changes.Count ? changes[i + 1] : _columns;
                for (int x = blackStart; x < blackEnd && x < _columns; x++)
                {
                    int byteIndex = x >> 3;
                    int bit = 0x80 >> (x & 7);
                    if (_blackIs1)
                    {
                        row[byteIndex] |= (byte)bit;
                    }
                    else
                    {
                        row[byteIndex] &= (byte)~bit;
                    }
                }
            }

            // Mask the padding bits in the last byte to the white fill.
            int tail = _columns & 7;
            if (tail != 0)
            {
                int mask = 0xFF >> tail;
                if (_blackIs1)
                {
                    row[stride - 1] &= (byte)~mask;
                }
                else
                {
                    row[stride - 1] |= (byte)mask;
                }
            }

            output.Write(row, 0, stride);
        }

        // ── Bit reading ───────────────────────────────────────────────────

        private int BitsRemaining() => (_data.Length * 8) - _bitPosition;

        private int ReadBit()
        {
            int byteIndex = _bitPosition >> 3;
            int bit = (_data[byteIndex] >> (7 - (_bitPosition & 7))) & 1;
            _bitPosition++;
            return bit;
        }

        private void AlignToByte()
        {
            _bitPosition = (_bitPosition + 7) & ~7;
        }

        // True when the next 12 bits are the EOL pattern 000000000001
        // (without consuming them).
        private bool PeekEol()
        {
            if (BitsRemaining() < 12)
            {
                return false;
            }
            int saved = _bitPosition;
            bool isEol = true;
            for (int i = 0; i < 11; i++)
            {
                if (ReadBit() != 0)
                {
                    isEol = false;
                    break;
                }
            }
            if (isEol && ReadBit() != 1)
            {
                isEol = false;
            }
            _bitPosition = saved;
            return isEol;
        }

        // Skips any number of EOLs and fill bits before a 1-D row.
        // Returns false when only padding remains.
        private bool SkipEolsAndFill()
        {
            while (PeekEol())
            {
                ConsumeEol();
            }
            return BitsRemaining() > 0 && !OnlyZerosRemain();
        }

        // Skips fill bits and at least one EOL when present; returns whether
        // an EOL was consumed.
        private bool SkipEolsRequired()
        {
            bool saw = false;
            while (PeekEol())
            {
                ConsumeEol();
                saw = true;
                // Consecutive EOLs form RTC; the row loop's data check ends
                // decoding naturally when only EOLs remain.
            }
            return saw;
        }

        private void ConsumeEol()
        {
            while (ReadBit() == 0)
            {
            }
        }

        private bool OnlyZerosRemain()
        {
            int saved = _bitPosition;
            bool allZero = true;
            while (BitsRemaining() > 0)
            {
                if (ReadBit() != 0)
                {
                    allZero = false;
                    break;
                }
            }
            _bitPosition = saved;
            return allZero;
        }

        // ── Run-length decoding (T.4 code tables 2 and 3) ────────────────

        // Reads one complete run length: zero or more make-up codes followed
        // by a terminating code. Returns −1 on end of data or invalid code.
        private int ReadRunLength(bool white)
        {
            int total = 0;
            while (true)
            {
                int run = ReadRunCode(white);
                if (run < 0)
                {
                    return -1;
                }
                total += run;
                if (run < 64)
                {
                    // Terminating code — the run is complete. Make-up codes
                    // (multiples of 64) accumulate and loop for the next code.
                    return total;
                }
            }
        }

        private int ReadRunCode(bool white)
        {
            Dictionary<int, int> table = white ? WhiteCodes : BlackCodes;
            int code = 0;
            for (int length = 1; length <= 14; length++)
            {
                if (BitsRemaining() <= 0)
                {
                    return -1;
                }
                code = (code << 1) | ReadBit();
                if (table.TryGetValue((length << 16) | code, out int run))
                {
                    return run;
                }
            }
            return -1;
        }

        // ── 2-D mode codes (T.4 Table 4 / T.6) ───────────────────────────

        private enum Mode
        {
            Pass,
            Horizontal,
            Vertical0,
            VerticalR1,
            VerticalR2,
            VerticalR3,
            VerticalL1,
            VerticalL2,
            VerticalL3,
            EndOfData,
            Invalid,
        }

        private Mode ReadMode()
        {
            if (BitsRemaining() <= 0)
            {
                return Mode.EndOfData;
            }
            if (ReadBit() == 1)
            {
                return Mode.Vertical0;                // 1
            }
            if (BitsRemaining() <= 0)
            {
                return Mode.EndOfData;
            }
            if (ReadBit() == 1)
            {
                // 01x
                return ReadBit() == 1 ? Mode.VerticalR1 : Mode.VerticalL1;
            }
            if (BitsRemaining() <= 0)
            {
                return Mode.EndOfData;
            }
            if (ReadBit() == 1)
            {
                return Mode.Horizontal;               // 001
            }
            if (BitsRemaining() <= 0)
            {
                return Mode.EndOfData;
            }
            if (ReadBit() == 1)
            {
                return Mode.Pass;                     // 0001
            }
            if (BitsRemaining() <= 1)
            {
                return Mode.EndOfData;
            }
            if (ReadBit() == 1)
            {
                // 00001x
                return ReadBit() == 1 ? Mode.VerticalR2 : Mode.VerticalL2;
            }
            if (BitsRemaining() <= 1)
            {
                return Mode.EndOfData;
            }
            if (ReadBit() == 1)
            {
                // 000001x
                return ReadBit() == 1 ? Mode.VerticalR3 : Mode.VerticalL3;
            }

            // 0000000... — EOL / EOFB territory.
            return Mode.EndOfData;
        }

        // ── T.4 code tables, keyed (bitLength << 16) | code ──────────────

        private static readonly Dictionary<int, int> WhiteCodes = BuildWhiteCodes();
        private static readonly Dictionary<int, int> BlackCodes = BuildBlackCodes();

        private static void Add(Dictionary<int, int> table, int length, int code, int run)
        {
            table[(length << 16) | code] = run;
        }

        private static Dictionary<int, int> BuildWhiteCodes()
        {
            Dictionary<int, int> t = new();

            // Terminating codes 0–63 (T.4 Table 2).
            Add(t, 8, 0b00110101, 0);
            Add(t, 6, 0b000111, 1);
            Add(t, 4, 0b0111, 2);
            Add(t, 4, 0b1000, 3);
            Add(t, 4, 0b1011, 4);
            Add(t, 4, 0b1100, 5);
            Add(t, 4, 0b1110, 6);
            Add(t, 4, 0b1111, 7);
            Add(t, 5, 0b10011, 8);
            Add(t, 5, 0b10100, 9);
            Add(t, 5, 0b00111, 10);
            Add(t, 5, 0b01000, 11);
            Add(t, 6, 0b001000, 12);
            Add(t, 6, 0b000011, 13);
            Add(t, 6, 0b110100, 14);
            Add(t, 6, 0b110101, 15);
            Add(t, 6, 0b101010, 16);
            Add(t, 6, 0b101011, 17);
            Add(t, 7, 0b0100111, 18);
            Add(t, 7, 0b0001100, 19);
            Add(t, 7, 0b0001000, 20);
            Add(t, 7, 0b0010111, 21);
            Add(t, 7, 0b0000011, 22);
            Add(t, 7, 0b0000100, 23);
            Add(t, 7, 0b0101000, 24);
            Add(t, 7, 0b0101011, 25);
            Add(t, 7, 0b0010011, 26);
            Add(t, 7, 0b0100100, 27);
            Add(t, 7, 0b0011000, 28);
            Add(t, 8, 0b00000010, 29);
            Add(t, 8, 0b00000011, 30);
            Add(t, 8, 0b00011010, 31);
            Add(t, 8, 0b00011011, 32);
            Add(t, 8, 0b00010010, 33);
            Add(t, 8, 0b00010011, 34);
            Add(t, 8, 0b00010100, 35);
            Add(t, 8, 0b00010101, 36);
            Add(t, 8, 0b00010110, 37);
            Add(t, 8, 0b00010111, 38);
            Add(t, 8, 0b00101000, 39);
            Add(t, 8, 0b00101001, 40);
            Add(t, 8, 0b00101010, 41);
            Add(t, 8, 0b00101011, 42);
            Add(t, 8, 0b00101100, 43);
            Add(t, 8, 0b00101101, 44);
            Add(t, 8, 0b00000100, 45);
            Add(t, 8, 0b00000101, 46);
            Add(t, 8, 0b00001010, 47);
            Add(t, 8, 0b00001011, 48);
            Add(t, 8, 0b01010010, 49);
            Add(t, 8, 0b01010011, 50);
            Add(t, 8, 0b01010100, 51);
            Add(t, 8, 0b01010101, 52);
            Add(t, 8, 0b00100100, 53);
            Add(t, 8, 0b00100101, 54);
            Add(t, 8, 0b01011000, 55);
            Add(t, 8, 0b01011001, 56);
            Add(t, 8, 0b01011010, 57);
            Add(t, 8, 0b01011011, 58);
            Add(t, 8, 0b01001010, 59);
            Add(t, 8, 0b01001011, 60);
            Add(t, 8, 0b00110010, 61);
            Add(t, 8, 0b00110011, 62);
            Add(t, 8, 0b00110100, 63);

            // Make-up codes (T.4 Table 2).
            Add(t, 5, 0b11011, 64);
            Add(t, 5, 0b10010, 128);
            Add(t, 6, 0b010111, 192);
            Add(t, 7, 0b0110111, 256);
            Add(t, 8, 0b00110110, 320);
            Add(t, 8, 0b00110111, 384);
            Add(t, 8, 0b01100100, 448);
            Add(t, 8, 0b01100101, 512);
            Add(t, 8, 0b01101000, 576);
            Add(t, 8, 0b01100111, 640);
            Add(t, 9, 0b011001100, 704);
            Add(t, 9, 0b011001101, 768);
            Add(t, 9, 0b011010010, 832);
            Add(t, 9, 0b011010011, 896);
            Add(t, 9, 0b011010100, 960);
            Add(t, 9, 0b011010101, 1024);
            Add(t, 9, 0b011010110, 1088);
            Add(t, 9, 0b011010111, 1152);
            Add(t, 9, 0b011011000, 1216);
            Add(t, 9, 0b011011001, 1280);
            Add(t, 9, 0b011011010, 1344);
            Add(t, 9, 0b011011011, 1408);
            Add(t, 9, 0b010011000, 1472);
            Add(t, 9, 0b010011001, 1536);
            Add(t, 9, 0b010011010, 1600);
            Add(t, 6, 0b011000, 1664);
            Add(t, 9, 0b010011011, 1728);

            AddExtendedMakeups(t);
            return t;
        }

        private static Dictionary<int, int> BuildBlackCodes()
        {
            Dictionary<int, int> t = new();

            // Terminating codes 0–63 (T.4 Table 3).
            Add(t, 10, 0b0000110111, 0);
            Add(t, 3, 0b010, 1);
            Add(t, 2, 0b11, 2);
            Add(t, 2, 0b10, 3);
            Add(t, 3, 0b011, 4);
            Add(t, 4, 0b0011, 5);
            Add(t, 4, 0b0010, 6);
            Add(t, 5, 0b00011, 7);
            Add(t, 6, 0b000101, 8);
            Add(t, 6, 0b000100, 9);
            Add(t, 7, 0b0000100, 10);
            Add(t, 7, 0b0000101, 11);
            Add(t, 7, 0b0000111, 12);
            Add(t, 8, 0b00000100, 13);
            Add(t, 8, 0b00000111, 14);
            Add(t, 9, 0b000011000, 15);
            Add(t, 10, 0b0000010111, 16);
            Add(t, 10, 0b0000011000, 17);
            Add(t, 10, 0b0000001000, 18);
            Add(t, 11, 0b00001100111, 19);
            Add(t, 11, 0b00001101000, 20);
            Add(t, 11, 0b00001101100, 21);
            Add(t, 11, 0b00000110111, 22);
            Add(t, 11, 0b00000101000, 23);
            Add(t, 11, 0b00000010111, 24);
            Add(t, 11, 0b00000011000, 25);
            Add(t, 12, 0b000011001010, 26);
            Add(t, 12, 0b000011001011, 27);
            Add(t, 12, 0b000011001100, 28);
            Add(t, 12, 0b000011001101, 29);
            Add(t, 12, 0b000001101000, 30);
            Add(t, 12, 0b000001101001, 31);
            Add(t, 12, 0b000001101010, 32);
            Add(t, 12, 0b000001101011, 33);
            Add(t, 12, 0b000011010010, 34);
            Add(t, 12, 0b000011010011, 35);
            Add(t, 12, 0b000011010100, 36);
            Add(t, 12, 0b000011010101, 37);
            Add(t, 12, 0b000011010110, 38);
            Add(t, 12, 0b000011010111, 39);
            Add(t, 12, 0b000001101100, 40);
            Add(t, 12, 0b000001101101, 41);
            Add(t, 12, 0b000011011010, 42);
            Add(t, 12, 0b000011011011, 43);
            Add(t, 12, 0b000001010100, 44);
            Add(t, 12, 0b000001010101, 45);
            Add(t, 12, 0b000001010110, 46);
            Add(t, 12, 0b000001010111, 47);
            Add(t, 12, 0b000001100100, 48);
            Add(t, 12, 0b000001100101, 49);
            Add(t, 12, 0b000001010010, 50);
            Add(t, 12, 0b000001010011, 51);
            Add(t, 12, 0b000000100100, 52);
            Add(t, 12, 0b000000110111, 53);
            Add(t, 12, 0b000000111000, 54);
            Add(t, 12, 0b000000100111, 55);
            Add(t, 12, 0b000000101000, 56);
            Add(t, 12, 0b000001011000, 57);
            Add(t, 12, 0b000001011001, 58);
            Add(t, 12, 0b000000101011, 59);
            Add(t, 12, 0b000000101100, 60);
            Add(t, 12, 0b000001011010, 61);
            Add(t, 12, 0b000001100110, 62);
            Add(t, 12, 0b000001100111, 63);

            // Make-up codes (T.4 Table 3).
            Add(t, 10, 0b0000001111, 64);
            Add(t, 12, 0b000011001000, 128);
            Add(t, 12, 0b000011001001, 192);
            Add(t, 12, 0b000001011011, 256);
            Add(t, 12, 0b000000110011, 320);
            Add(t, 12, 0b000000110100, 384);
            Add(t, 12, 0b000000110101, 448);
            Add(t, 13, 0b0000001101100, 512);
            Add(t, 13, 0b0000001101101, 576);
            Add(t, 13, 0b0000001001010, 640);
            Add(t, 13, 0b0000001001011, 704);
            Add(t, 13, 0b0000001001100, 768);
            Add(t, 13, 0b0000001001101, 832);
            Add(t, 13, 0b0000001110010, 896);
            Add(t, 13, 0b0000001110011, 960);
            Add(t, 13, 0b0000001110100, 1024);
            Add(t, 13, 0b0000001110101, 1088);
            Add(t, 13, 0b0000001110110, 1152);
            Add(t, 13, 0b0000001110111, 1216);
            Add(t, 13, 0b0000001010010, 1280);
            Add(t, 13, 0b0000001010011, 1344);
            Add(t, 13, 0b0000001010100, 1408);
            Add(t, 13, 0b0000001010101, 1472);
            Add(t, 13, 0b0000001011010, 1536);
            Add(t, 13, 0b0000001011011, 1600);
            Add(t, 13, 0b0000001100100, 1664);
            Add(t, 13, 0b0000001100101, 1728);

            AddExtendedMakeups(t);
            return t;
        }

        // Extended make-up codes 1792–2560, shared between colours (T.4).
        private static void AddExtendedMakeups(Dictionary<int, int> t)
        {
            Add(t, 11, 0b00000001000, 1792);
            Add(t, 11, 0b00000001100, 1856);
            Add(t, 11, 0b00000001101, 1920);
            Add(t, 12, 0b000000010010, 1984);
            Add(t, 12, 0b000000010011, 2048);
            Add(t, 12, 0b000000010100, 2112);
            Add(t, 12, 0b000000010101, 2176);
            Add(t, 12, 0b000000010110, 2240);
            Add(t, 12, 0b000000010111, 2304);
            Add(t, 12, 0b000000011100, 2368);
            Add(t, 12, 0b000000011101, 2432);
            Add(t, 12, 0b000000011110, 2496);
            Add(t, 12, 0b000000011111, 2560);
        }
    }
}
