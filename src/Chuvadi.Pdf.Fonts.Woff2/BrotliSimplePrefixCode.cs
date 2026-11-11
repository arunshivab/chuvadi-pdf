// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 7932 §3.4 — Simple Prefix Codes
// PHASE: Phase 2.2 stage 2

using System;
using System.Collections.Generic;
using System.Linq;

namespace Chuvadi.Pdf.Fonts.Woff2;

/// <summary>
/// Emitter for RFC 7932 §3.4 simple prefix codes, which support alphabets of
/// 1..4 distinct symbols.
/// </summary>
/// <remarks>
/// <para>
/// Layout per §3.4: <c>2 bits "01"</c> (value 1 = simple code, LSB-first means emit
/// bit 1 then bit 0), then <c>2 bits</c> NSYM-1, then NSYM × ALPHABET_BITS symbol
/// values, then a 1-bit tree-select if NSYM=4.
/// </para>
/// <para>
/// Code lengths per NSYM:
/// <list type="bullet">
///   <item>NSYM=1: length 0 (symbol emits zero bits when used)</item>
///   <item>NSYM=2: both length 1 (codes 0 and 1)</item>
///   <item>NSYM=3: lengths 1, 2, 2 in decode order</item>
///   <item>NSYM=4 tree-select=0: lengths 2, 2, 2, 2</item>
///   <item>NSYM=4 tree-select=1: lengths 1, 2, 3, 3</item>
/// </list>
/// </para>
/// <para>
/// Crucial: "Prefix codes of the same bit length must be assigned to the symbols
/// in sorted order" (§3.4). So among symbols of equal length, the smaller symbol
/// value gets the smaller code value.
/// </para>
/// </remarks>
internal sealed class BrotliSimplePrefixCode
{
    private readonly Dictionary<int, (int CodeValue, int BitLength)> _table = new();

    /// <summary>The symbol values that appear in this code, in the emit order (sorted ascending).</summary>
    internal int[] Symbols { get; }

    /// <summary>Number of distinct symbols (1..4).</summary>
    internal int NSym => Symbols.Length;

    internal BrotliSimplePrefixCode(IEnumerable<int> distinctSymbols)
    {
        ArgumentNullException.ThrowIfNull(distinctSymbols);
        int[] symbols = distinctSymbols.Distinct().OrderBy(s => s).ToArray();
        if (symbols.Length is < 1 or > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(distinctSymbols), "Simple prefix code supports 1..4 symbols.");
        }
        Symbols = symbols;
        BuildTable();
    }

    /// <summary>Look up the (code value, bit length) for emitting <paramref name="symbol"/>.</summary>
    internal (int CodeValue, int BitLength) GetCode(int symbol) => _table[symbol];

    /// <summary>Emit the prefix code declaration into the bitstream (RFC §3.4 format).</summary>
    internal void EmitDeclaration(BrotliBitWriter bw, int alphabetBits)
    {
        ArgumentNullException.ThrowIfNull(bw);
        // 2 bits: value 1 = simple prefix code marker.
        bw.WriteBits(1, 2);
        // 2 bits: NSYM - 1.
        bw.WriteBits((ulong)(NSym - 1), 2);
        // NSYM × ALPHABET_BITS: the symbol values in sorted order.
        foreach (int s in Symbols)
        {
            bw.WriteBits((ulong)s, alphabetBits);
        }
        // For NSYM=4 we emit tree-select=0 (lengths {2,2,2,2}).
        if (NSym == 4)
        {
            bw.WriteBits(0, 1);
        }
    }

    private void BuildTable()
    {
        // Code lengths per §3.4, in symbol-emission order (which is sorted-ascending).
        int[] lengths = NSym switch
        {
            1 => new[] { 0 },
            2 => new[] { 1, 1 },
            3 => new[] { 1, 2, 2 },
            _ => new[] { 2, 2, 2, 2 },        // NSYM=4 tree-select=0
        };

        // Canonical code assignment: among symbols of equal bit length, the smaller
        // symbol value gets the smaller code value (§3.4). Since Symbols is sorted
        // ascending, we just walk lengths in order and assign codes by canonical rule.
        //
        // Canonical algorithm: sort (length, symbol). For each length L (ascending),
        // assign successive code values 0, 1, 2, ... For each length transition L→L+1,
        // left-shift the next code by 1 bit.
        //
        // For our table this works out as follows:
        //   NSYM=1: symbol gets code (0, 0 bits) — zero bits, never emitted.
        //   NSYM=2: symbol 0 → code 0 (1 bit), symbol 1 → code 1 (1 bit).
        //   NSYM=3: symbol with len 1 → code 0 (1 bit); symbols with len 2 →
        //           codes 10, 11 binary = values 2, 3 (canonical, 2-bit).
        //   NSYM=4 (2,2,2,2): codes 00, 01, 10, 11 = values 0, 1, 2, 3.
        (int sym, int len)[] symbolsByLengthThenValue = Symbols
            .Select((sym, idx) => (sym, len: lengths[idx]))
            .OrderBy(t => t.len)
            .ThenBy(t => t.sym)
            .ToArray();

        int code = 0;
        int prevLen = symbolsByLengthThenValue[0].len;
        foreach (var (sym, len) in symbolsByLengthThenValue)
        {
            if (len > prevLen)
            {
                code <<= (len - prevLen);
                prevLen = len;
            }
            // Brotli reads bits LSB-first. Canonical Huffman code "01" means the decoder
            // reads 0 then 1 — but our bit writer emits the LSB first. So we must bit-reverse
            // the canonical code before storing it, so WriteBits emits the bits in the order
            // the decoder expects.
            _table[sym] = (BitReverse(code, len), len);
            code++;
        }
    }

    private static int BitReverse(int value, int bits)
    {
        int reversed = 0;
        for (int i = 0; i < bits; i++)
        {
            reversed = (reversed << 1) | (value & 1);
            value >>= 1;
        }
        return reversed;
    }
}
