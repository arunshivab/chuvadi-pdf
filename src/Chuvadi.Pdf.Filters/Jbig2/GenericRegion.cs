// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ITU-T T.88 (JBIG2) §6.2 — generic region decoding procedure
//        (§6.2.5.3 templates, §6.2.5.7 TPGDON typical prediction).
// PHASE: Phase 2 — item 22 (decode) and 23 (encode).
//
// Context bits are assembled by visiting the template pixels (the fixed set plus
// the adaptive AT pixels) in (y, x) raster order, most-significant bit first — the
// ordering that yields the standard context labels. Encode and decode share that
// ordering, so the encoder is the exact inverse of the decoder and round-trips any
// bitmap; bit-exact conformance against an independent JBIG2 stream is gated on a
// reference fixture.

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Filters.Jbig2;

/// <summary>A relative pixel offset within a generic-region coding template.</summary>
/// <param name="Dx">X offset from the current pixel.</param>
/// <param name="Dy">Y offset from the current pixel.</param>
internal readonly record struct TemplatePixel(int Dx, int Dy);

/// <summary>
/// Generic-region coding (ITU-T T.88 §6.2): decodes — and, for round-trip and
/// encode support, re-encodes — an arithmetically coded bilevel bitmap using one
/// of the four GB templates, the adaptive AT pixels, and optional TPGDON typical
/// prediction.
/// </summary>
internal static class GenericRegion
{
    // Fixed (non-AT) template pixels for GBTEMPLATE 0..3 (T.88 §6.2.5.3).
    private static readonly TemplatePixel[][] FixedTemplates =
    {
        new[]
        {
            new TemplatePixel(-1, -2), new TemplatePixel(0, -2), new TemplatePixel(1, -2),
            new TemplatePixel(-2, -1), new TemplatePixel(-1, -1), new TemplatePixel(0, -1),
            new TemplatePixel(1, -1), new TemplatePixel(2, -1),
            new TemplatePixel(-4, 0), new TemplatePixel(-3, 0), new TemplatePixel(-2, 0),
            new TemplatePixel(-1, 0),
        },
        new[]
        {
            new TemplatePixel(-1, -2), new TemplatePixel(0, -2), new TemplatePixel(1, -2),
            new TemplatePixel(2, -2),
            new TemplatePixel(-2, -1), new TemplatePixel(-1, -1), new TemplatePixel(0, -1),
            new TemplatePixel(1, -1), new TemplatePixel(2, -1),
            new TemplatePixel(-3, 0), new TemplatePixel(-2, 0), new TemplatePixel(-1, 0),
        },
        new[]
        {
            new TemplatePixel(-1, -2), new TemplatePixel(0, -2), new TemplatePixel(1, -2),
            new TemplatePixel(-2, -1), new TemplatePixel(-1, -1), new TemplatePixel(0, -1),
            new TemplatePixel(1, -1),
            new TemplatePixel(-2, 0), new TemplatePixel(-1, 0),
        },
        new[]
        {
            new TemplatePixel(-3, -1), new TemplatePixel(-2, -1), new TemplatePixel(-1, -1),
            new TemplatePixel(0, -1), new TemplatePixel(1, -1),
            new TemplatePixel(-4, 0), new TemplatePixel(-3, 0), new TemplatePixel(-2, 0),
            new TemplatePixel(-1, 0),
        },
    };

    // SLTP pseudo-pixel context per template for the TPGDON line bit (T.88 §6.2.5.7).
    private static readonly int[] SltpContext = { 0x9B25, 0x0795, 0x00E5, 0x0195 };

    /// <summary>Nominal AT-pixel positions for the given template.</summary>
    /// <param name="template">GBTEMPLATE index, 0..3.</param>
    /// <returns>The default adaptive-template pixel offsets.</returns>
    internal static TemplatePixel[] DefaultAt(int template)
    {
        return template == 0
            ? new[]
            {
                new TemplatePixel(3, -1), new TemplatePixel(-3, -1),
                new TemplatePixel(2, -2), new TemplatePixel(-2, -2),
            }
            : template == 1
                ? new[] { new TemplatePixel(3, -1) }
                : new[] { new TemplatePixel(2, -1) };
    }

    /// <summary>Number of context-state entries required for a template + AT set.</summary>
    /// <param name="template">GBTEMPLATE index, 0..3.</param>
    /// <param name="at">The adaptive-template pixels in use.</param>
    /// <returns>The size the caller's context array must have.</returns>
    internal static int ContextSize(int template, TemplatePixel[] at)
    {
        ArgumentNullException.ThrowIfNull(at);
        return 1 << (FixedTemplates[template].Length + at.Length);
    }

    /// <summary>
    /// Decodes a generic region bitmap (T.88 §6.2.5).
    /// </summary>
    /// <param name="mq">The arithmetic decoder positioned at the region data.</param>
    /// <param name="cx">Context-state array sized by <see cref="ContextSize"/>.</param>
    /// <param name="width">Region width in pixels.</param>
    /// <param name="height">Region height in pixels.</param>
    /// <param name="template">GBTEMPLATE index, 0..3.</param>
    /// <param name="at">Adaptive-template pixels (use <see cref="DefaultAt"/> for nominal).</param>
    /// <param name="tpgdon">Whether typical-prediction (TPGDON) is enabled.</param>
    /// <returns>The decoded bitmap.</returns>
    internal static Jbig2Bitmap Decode(
        MQDecoder mq, byte[] cx, int width, int height, int template, TemplatePixel[] at, bool tpgdon)
    {
        ArgumentNullException.ThrowIfNull(mq);
        ArgumentNullException.ThrowIfNull(cx);
        ArgumentNullException.ThrowIfNull(at);

        Jbig2Bitmap bitmap = new Jbig2Bitmap(width, height);
        IReadOnlyList<TemplatePixel> pixels = OrderedTemplate(template, at);
        int sltp = SltpContext[template];
        bool ltp = false;

        for (int y = 0; y < height; y++)
        {
            if (tpgdon)
            {
                ltp ^= mq.Decode(cx, sltp) == 1;
                if (ltp)
                {
                    for (int x = 0; x < width; x++)
                    {
                        bitmap.Set(x, y, bitmap.Get(x, y - 1));
                    }

                    continue;
                }
            }

            for (int x = 0; x < width; x++)
            {
                int context = 0;
                for (int i = 0; i < pixels.Count; i++)
                {
                    context = (context << 1) | bitmap.Get(x + pixels[i].Dx, y + pixels[i].Dy);
                }

                bitmap.Set(x, y, mq.Decode(cx, context));
            }
        }

        return bitmap;
    }

    /// <summary>
    /// Encodes a generic region bitmap — the exact inverse of <see cref="Decode"/>.
    /// </summary>
    /// <param name="bitmap">The bitmap to encode.</param>
    /// <param name="template">GBTEMPLATE index, 0..3.</param>
    /// <param name="at">Adaptive-template pixels.</param>
    /// <param name="tpgdon">Whether typical-prediction (TPGDON) is enabled.</param>
    /// <returns>The arithmetic-coded bytes.</returns>
    internal static byte[] Encode(Jbig2Bitmap bitmap, int template, TemplatePixel[] at, bool tpgdon)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(at);

        MQEncoder mq = new MQEncoder();
        byte[] cx = new byte[ContextSize(template, at)];
        IReadOnlyList<TemplatePixel> pixels = OrderedTemplate(template, at);
        int sltp = SltpContext[template];
        bool ltp = false;

        for (int y = 0; y < bitmap.Height; y++)
        {
            if (tpgdon)
            {
                bool typical = RowMatchesAbove(bitmap, y);
                mq.Encode(cx, sltp, typical != ltp ? 1 : 0);
                ltp = typical;
                if (ltp)
                {
                    continue;
                }
            }

            for (int x = 0; x < bitmap.Width; x++)
            {
                int context = 0;
                for (int i = 0; i < pixels.Count; i++)
                {
                    context = (context << 1) | bitmap.Get(x + pixels[i].Dx, y + pixels[i].Dy);
                }

                mq.Encode(cx, context, bitmap.Get(x, y));
            }
        }

        return mq.Flush();
    }

    // Combines fixed + AT pixels and orders them (y, then x) for MSB-first context bits.
    private static IReadOnlyList<TemplatePixel> OrderedTemplate(int template, TemplatePixel[] at)
    {
        List<TemplatePixel> pixels = new List<TemplatePixel>(FixedTemplates[template]);
        pixels.AddRange(at);
        pixels.Sort((a, b) => a.Dy != b.Dy ? a.Dy - b.Dy : a.Dx - b.Dx);
        return pixels;
    }

    private static bool RowMatchesAbove(Jbig2Bitmap bitmap, int y)
    {
        for (int x = 0; x < bitmap.Width; x++)
        {
            if (bitmap.Get(x, y) != bitmap.Get(x, y - 1))
            {
                return false;
            }
        }

        return true;
    }
}
