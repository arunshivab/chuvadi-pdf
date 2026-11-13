// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ITU-T T.88 (JBIG2) — bitmaps are the fundamental decoded unit.
// PHASE: Phase 2 — item 22, JBIG2 decode.

using System;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Filters.Jbig2;

/// <summary>
/// A bilevel bitmap: the fundamental decoded unit in JBIG2. Pixels are stored one
/// byte each (0 or 1) for clear, branch-free template addressing; packing to 1 bpp
/// happens only at the filter's output boundary. Reads outside the bounds return 0,
/// matching the JBIG2 convention that off-bitmap context pixels are zero.
/// </summary>
internal sealed class Jbig2Bitmap
{
    // Bitmaps from this size (one byte per pixel) are charged against the
    // memory limit; symbol bitmaps are smaller and are not.
    private const long LargeBitmap = 1 << 16;

    /// <summary>Initialises a zero-filled bitmap of the given dimensions.</summary>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    internal Jbig2Bitmap(int width, int height)
    {
        if (width < 0 || height < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Bitmap dimensions must be non-negative.");
        }

        // 3.34.0 (#20): a region bitmap is charged against the memory limit
        // (and checks cancellation) before it is allocated, so a page-sized
        // region stops early instead of after its whole decode.
        long size = (long)width * height;
        if (size > Array.MaxLength)
        {
            throw new FilterException("JBIG2Decode", $"A {width} x {height} bitmap is too large.");
        }

        if (size >= LargeBitmap)
        {
            PdfWorkScope.ChargeMemory(size);
        }

        Width = width;
        Height = height;
        Data = new byte[size];
    }

    /// <summary>Width in pixels.</summary>
    internal int Width { get; }

    /// <summary>Height in pixels.</summary>
    internal int Height { get; }

    /// <summary>Row-major pixel data, one byte per pixel (0 or 1).</summary>
    internal byte[] Data { get; }

    /// <summary>Returns the pixel at (x, y), or 0 if the coordinate is off-bitmap.</summary>
    /// <param name="x">X coordinate.</param>
    /// <param name="y">Y coordinate.</param>
    /// <returns>The pixel value, 0 or 1.</returns>
    internal int Get(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
        {
            return 0;
        }

        return Data[(y * Width) + x];
    }

    /// <summary>Sets the pixel at (x, y).</summary>
    /// <param name="x">X coordinate.</param>
    /// <param name="y">Y coordinate.</param>
    /// <param name="value">The pixel value, 0 or 1.</param>
    internal void Set(int x, int y, int value)
    {
        Data[(y * Width) + x] = (byte)value;
    }
}
