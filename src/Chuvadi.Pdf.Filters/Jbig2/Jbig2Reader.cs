// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ITU-T T.88 (JBIG2) — all multi-byte fields are big-endian.
// PHASE: Phase 2 — item 22, JBIG2 decode.

using System;

namespace Chuvadi.Pdf.Filters.Jbig2;

/// <summary>
/// A forward big-endian reader over a JBIG2 byte buffer, used to parse segment
/// headers and segment data. All multi-byte integers in JBIG2 are big-endian.
/// </summary>
internal sealed class Jbig2Reader
{
    private readonly byte[] _data;

    /// <summary>Initialises a reader over <paramref name="data"/> from offset 0.</summary>
    /// <param name="data">The buffer to read.</param>
    internal Jbig2Reader(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        _data = data;
        Position = 0;
    }

    /// <summary>Current read offset.</summary>
    internal int Position { get; set; }

    /// <summary>Total length of the buffer.</summary>
    internal int Length => _data.Length;

    /// <summary>True when at least <paramref name="count"/> bytes remain.</summary>
    /// <param name="count">Number of bytes required.</param>
    /// <returns>Whether the bytes are available.</returns>
    internal bool HasBytes(int count) => Position + count <= _data.Length;

    /// <summary>Reads one unsigned byte and advances.</summary>
    /// <returns>The byte value, 0..255.</returns>
    internal int ReadByte()
    {
        return _data[Position++];
    }

    /// <summary>Reads one signed byte (two's complement) and advances.</summary>
    /// <returns>The signed value, -128..127.</returns>
    internal int ReadSByte()
    {
        return (sbyte)_data[Position++];
    }

    /// <summary>Reads a big-endian unsigned 16-bit integer and advances.</summary>
    /// <returns>The value, 0..65535.</returns>
    internal int ReadUInt16()
    {
        int value = (_data[Position] << 8) | _data[Position + 1];
        Position += 2;
        return value;
    }

    /// <summary>Reads a big-endian unsigned 32-bit integer and advances.</summary>
    /// <returns>The value as a <see cref="uint"/>.</returns>
    internal uint ReadUInt32()
    {
        uint value = ((uint)_data[Position] << 24)
            | ((uint)_data[Position + 1] << 16)
            | ((uint)_data[Position + 2] << 8)
            | _data[Position + 3];
        Position += 4;
        return value;
    }
}
