// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.4.6 — LZWDecode filter
// PHASE: Phase 1 — Chuvadi.Pdf.Filters
// Lempel-Ziv-Welch compression for legacy PDF compatibility.

using System;
using System.Collections.Generic;
using System.IO;

namespace Chuvadi.Pdf.Filters;

/// <summary>
/// Implements the PDF LZWDecode filter.
/// Variable-width codes (9-12 bits), MSB-first. EarlyChange=1 is PDF default.
/// Code 256 = ClearTable. Code 257 = EOD.
/// PDF 32000-1:2008 §7.4.6.
/// </summary>
public sealed class LzwFilter : IStreamFilter
{
    private const int ClearCode = 256;
    private const int EodCode = 257;
    private const int FirstCode = 258;
    private const int MaxCodeBits = 12;
    private const int MaxTableSize = 1 << MaxCodeBits;

    /// <inheritdoc/>
    public string FilterName => "LZWDecode";

    /// <inheritdoc/>
    public void Decode(Stream input, Stream output, FilterParameters? decodeParms = null)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        if (output is null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        int earlyChange = decodeParms?.EarlyChange ?? 1;
        LzwBitReader reader = new LzwBitReader(input);
        List<byte[]> table = new List<byte[]>(MaxTableSize);
        InitTable(table);

        int codeBits = 9;
        int nextCode = FirstCode;

        int code = reader.ReadBits(codeBits);

        if (code == EodCode)
        {
            return;
        }

        if (code == ClearCode)
        {
            InitTable(table);
            codeBits = 9;
            nextCode = FirstCode;
            code = reader.ReadBits(codeBits);
        }

        if (code >= table.Count)
        {
            throw new FilterException(FilterName,
                $"Invalid first LZW code {code} after clear.");
        }

        byte[] entry = table[code];
        output.Write(entry, 0, entry.Length);
        byte[] prevEntry = entry;

        while (true)
        {
            code = reader.ReadBits(codeBits);

            if (code == EodCode || code == -1)
            {
                break;
            }

            if (code == ClearCode)
            {
                InitTable(table);
                codeBits = 9;
                nextCode = FirstCode;

                code = reader.ReadBits(codeBits);

                if (code == EodCode || code == -1)
                {
                    break;
                }

                if (code >= table.Count)
                {
                    throw new FilterException(FilterName,
                        $"Invalid LZW code {code} after clear.");
                }

                entry = table[code];
                output.Write(entry, 0, entry.Length);
                prevEntry = entry;
                continue;
            }

            if (code < table.Count)
            {
                entry = table[code];
            }
            else if (code == nextCode)
            {
                entry = new byte[prevEntry.Length + 1];
                Array.Copy(prevEntry, entry, prevEntry.Length);
                entry[prevEntry.Length] = prevEntry[0];
            }
            else
            {
                throw new FilterException(FilterName,
                    $"Invalid LZW code {code}: expected <= {nextCode}.");
            }

            output.Write(entry, 0, entry.Length);

            if (nextCode < MaxTableSize)
            {
                byte[] newEntry = new byte[prevEntry.Length + 1];
                Array.Copy(prevEntry, newEntry, prevEntry.Length);
                newEntry[prevEntry.Length] = entry[0];
                table.Add(newEntry);
                nextCode++;

                // 3.34.0: the code width grows once the next code (plus
                // EarlyChange) reaches the width's limit (PDF 32000-1:2008
                // §7.4.4.2), as every other reader does; before, it grew one
                // code too early, so valid streams failed once codes passed
                // 9 bits.
                if (nextCode + earlyChange >= (1 << codeBits) && codeBits < MaxCodeBits)
                {
                    codeBits++;
                }
            }

            prevEntry = entry;
        }
    }

    /// <inheritdoc/>
    public void Encode(Stream input, Stream output, FilterParameters? encodeParms = null)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        if (output is null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        int earlyChange = encodeParms?.EarlyChange ?? 1;
        byte[] data = ReadAllBytes(input);
        LzwBitWriter writer = new LzwBitWriter(output);
        Dictionary<ByteSequenceKey, int> encodeTable = new Dictionary<ByteSequenceKey, int>();
        InitEncodeTable(encodeTable);

        int codeBits = 9;
        int nextCode = FirstCode;

        writer.WriteBits(ClearCode, codeBits);

        if (data.Length == 0)
        {
            writer.WriteBits(EodCode, codeBits);
            writer.Flush();
            return;
        }

        List<byte> current = new List<byte> { data[0] };

        for (int i = 1; i < data.Length; i++)
        {
            current.Add(data[i]);
            ByteSequenceKey key = new ByteSequenceKey(current.ToArray());

            if (!encodeTable.ContainsKey(key))
            {
                current.RemoveAt(current.Count - 1);
                ByteSequenceKey prevKey = new ByteSequenceKey(current.ToArray());
                writer.WriteBits(encodeTable[prevKey], codeBits);

                if (nextCode < MaxTableSize)
                {
                    current.Add(data[i]);
                    encodeTable[new ByteSequenceKey(current.ToArray())] = nextCode++;

                    // 3.34.0: the encoder is one entry ahead of a decoder,
                    // so it widens one code later (see Decode).
                    if (nextCode + earlyChange > (1 << codeBits) && codeBits < MaxCodeBits)
                    {
                        codeBits++;
                    }
                }
                else
                {
                    writer.WriteBits(ClearCode, codeBits);
                    InitEncodeTable(encodeTable);
                    codeBits = 9;
                    nextCode = FirstCode;
                }

                current.Clear();
                current.Add(data[i]);
            }
        }

        if (current.Count > 0)
        {
            ByteSequenceKey lastKey = new ByteSequenceKey(current.ToArray());
            writer.WriteBits(encodeTable[lastKey], codeBits);
        }

        writer.WriteBits(EodCode, codeBits);
        writer.Flush();
    }

    private static void InitTable(List<byte[]> table)
    {
        table.Clear();

        for (int i = 0; i < 256; i++)
        {
            table.Add([(byte)i]);
        }

        table.Add([]);
        table.Add([]);
    }

    private static void InitEncodeTable(Dictionary<ByteSequenceKey, int> table)
    {
        table.Clear();

        for (int i = 0; i < 256; i++)
        {
            table[new ByteSequenceKey([(byte)i])] = i;
        }
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        using (MemoryStream ms = new MemoryStream())
        {
            stream.CopyTo(ms);
            return ms.ToArray();
        }
    }
}

internal sealed class LzwBitReader
{
    private readonly Stream _stream;
    private int _bitBuf;
    private int _bitsInBuf;

    internal LzwBitReader(Stream stream)
    {
        _stream = stream;
        _bitBuf = 0;
        _bitsInBuf = 0;
    }

    internal int ReadBits(int count)
    {
        while (_bitsInBuf < count)
        {
            int b = _stream.ReadByte();

            if (b == -1)
            {
                return -1;
            }

            _bitBuf = (_bitBuf << 8) | b;
            _bitsInBuf += 8;
        }

        int value = (_bitBuf >> (_bitsInBuf - count)) & ((1 << count) - 1);
        _bitsInBuf -= count;
        return value;
    }
}

internal sealed class LzwBitWriter
{
    private readonly Stream _stream;
    private int _bitBuf;
    private int _bitsInBuf;

    internal LzwBitWriter(Stream stream)
    {
        _stream = stream;
        _bitBuf = 0;
        _bitsInBuf = 0;
    }

    internal void WriteBits(int value, int count)
    {
        _bitBuf = (_bitBuf << count) | (value & ((1 << count) - 1));
        _bitsInBuf += count;

        while (_bitsInBuf >= 8)
        {
            _bitsInBuf -= 8;
            _stream.WriteByte((byte)((_bitBuf >> _bitsInBuf) & 0xFF));
        }
    }

    internal void Flush()
    {
        if (_bitsInBuf > 0)
        {
            _stream.WriteByte((byte)(_bitBuf << (8 - _bitsInBuf)));
            _bitBuf = 0;
            _bitsInBuf = 0;
        }
    }
}

internal readonly struct ByteSequenceKey : IEquatable<ByteSequenceKey>
{
    private readonly byte[] _data;

    internal ByteSequenceKey(byte[] data)
    {
        _data = data;
    }

    public bool Equals(ByteSequenceKey other)
    {
        return _data.AsSpan().SequenceEqual(other._data);
    }

    public override bool Equals(object? obj)
    {
        return obj is ByteSequenceKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        HashCode hash = new HashCode();
        hash.AddBytes(_data);
        return hash.ToHashCode();
    }
}
