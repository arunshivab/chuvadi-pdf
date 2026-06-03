// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  Phase 2 - gamma-correct anti-aliased blending
// PHASE: Phase 2 - Chuvadi.Pdf.Graphics
// sRGB <-> linear-light conversions for gamma-correct compositing.
using System;

namespace Chuvadi.Pdf.Graphics;

/// <summary>
/// sRGB transfer-function conversions used for gamma-correct alpha blending.
/// </summary>
/// <remarks>
/// <para>
/// Anti-aliased coverage is a measure of how much of a pixel a shape covers,
/// which is a quantity in linear light. Blending two sRGB-encoded colours
/// directly by a coverage fraction (the naive approach) mixes them in a
/// non-linear space and makes anti-aliased edges read as too light or too
/// thin. The correct sequence is: decode both colours from sRGB to linear,
/// mix in linear by the coverage, then re-encode the result to sRGB.
/// </para>
/// <para>
/// The exact piecewise sRGB transfer function (IEC 61966-2-1) is used rather
/// than a gamma-2.2 approximation so the result matches what colour-managed
/// renderers produce. The sRGB-to-linear direction is the hot path (one
/// lookup per destination channel per blended pixel) and is served from a
/// 256-entry table; the linear-to-sRGB direction operates on continuous
/// values and uses the closed-form function.
/// </para>
/// </remarks>
internal static class Srgb
{
    private static readonly float[] ToLinearLut = BuildToLinearLut();

    private static float[] BuildToLinearLut()
    {
        float[] lut = new float[256];
        for (int i = 0; i < 256; i++)
        {
            double c = i / 255.0;
            lut[i] = (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4));
        }

        return lut;
    }

    /// <summary>Converts an sRGB-encoded byte (0-255) to linear light (0-1).</summary>
    public static float ByteToLinear(byte value) => ToLinearLut[value];

    /// <summary>Converts an sRGB-encoded float channel (0-1) to linear light (0-1).</summary>
    public static float ToLinear(float c)
    {
        if (c <= 0.04045f)
        {
            return c / 12.92f;
        }

        return (float)Math.Pow((c + 0.055f) / 1.055f, 2.4);
    }

    /// <summary>Converts a linear-light value (0-1) to an sRGB-encoded float (0-1).</summary>
    public static float ToSrgb(float linear)
    {
        if (linear <= 0.0031308f)
        {
            return linear * 12.92f;
        }

        return (float)((1.055 * Math.Pow(linear, 1.0 / 2.4)) - 0.055);
    }
}
