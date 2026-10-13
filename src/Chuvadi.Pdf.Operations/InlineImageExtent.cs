// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.9.7 — Inline images
// PHASE: 3.18 — exact inline-image extents.
//
// The binary data after ID is not tokenizable, and scanning for a
// whitespace-delimited "EI" can stop EARLY on a byte pair inside the data —
// the tail of the image then re-tokenises as content (a scrub leak, and
// stream corruption). For an unfiltered image the data length is computable
// exactly from the dictionary (/W /H /BPC /CS, /ImageMask), so the EI is
// verified at the computed offset; the scan remains the fallback for filtered
// or underspecified images.

using System;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// Locates the end of an inline image (<c>BI … ID &lt;binary&gt; EI</c>) in a
/// content stream, preferring the exact data length computed from the image
/// dictionary over the heuristic <c>EI</c> scan.
/// </summary>
internal static class InlineImageExtent
{
    /// <summary>
    /// Consumes the inline image's dictionary tokens from
    /// <paramref name="tok"/> (positioned just after <c>BI</c>) through
    /// <c>ID</c>, then returns the offset just past the terminating
    /// <c>EI</c>, or <c>-1</c> when no terminating <c>EI</c> exists — the
    /// caller must then treat the rest of the stream as unknowable and keep
    /// it verbatim.
    /// </summary>
    internal static int FindEnd(PdfTokenizer tok, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(tok);
        ArgumentNullException.ThrowIfNull(content);

        int width = 0;
        int height = 0;
        int bpc = 8;
        int components = -1;
        bool filtered = false;
        bool imageMask = false;
        string? pendingKey = null;

        while (true)
        {
            PdfToken t = tok.Read();
            if (t.IsEndOfStream)
            {
                return -1;
            }

            if (t.Type == PdfTokenType.Keyword && t.RawText == "ID")
            {
                break;
            }

            if (t.Type == PdfTokenType.ArrayStart)
            {
                // An array value (e.g. /Decode [0 1] or /Filter [/AHx]): any
                // /Filter form means the data length is not computable here.
                if (pendingKey is "F" or "Filter")
                {
                    filtered = true;
                }

                while (true)
                {
                    PdfToken inner = tok.Read();
                    if (inner.IsEndOfStream)
                    {
                        return -1;
                    }

                    if (inner.Type == PdfTokenType.ArrayEnd)
                    {
                        break;
                    }
                }

                pendingKey = null;
                continue;
            }

            if (t.Type == PdfTokenType.Name)
            {
                string name = t.RawText.TrimStart('/');
                if (pendingKey is null)
                {
                    pendingKey = name;
                    continue;
                }

                switch (pendingKey)
                {
                    case "CS":
                    case "ColorSpace":
                        components = name switch
                        {
                            "G" or "DeviceGray" or "CalGray" => 1,
                            "RGB" or "DeviceRGB" or "CalRGB" => 3,
                            "CMYK" or "DeviceCMYK" => 4,
                            "I" or "Indexed" => 1,
                            _ => -1,
                        };
                        break;
                    case "F":
                    case "Filter":
                        filtered = true;
                        break;
                    default:
                        break;
                }

                pendingKey = null;
                continue;
            }

            if (pendingKey is not null)
            {
                if (t.IsNumeric && double.TryParse(
                    t.RawText, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double value))
                {
                    switch (pendingKey)
                    {
                        case "W" or "Width":
                            width = (int)value;
                            break;
                        case "H" or "Height":
                            height = (int)value;
                            break;
                        case "BPC" or "BitsPerComponent":
                            bpc = (int)value;
                            break;
                        default:
                            break;
                    }
                }
                else if (t.Type == PdfTokenType.Keyword && t.RawText == "true"
                    && pendingKey is "IM" or "ImageMask")
                {
                    imageMask = true;
                }

                pendingKey = null;
            }
        }

        // Data begins after the single whitespace byte following ID (§8.9.7).
        int dataStart = (int)tok.Position;
        if (dataStart < content.Length && IsWhitespace(content[dataStart]))
        {
            dataStart++;
        }

        if (imageMask)
        {
            components = 1;
            bpc = 1;
        }

        // Exact-length path for unfiltered images with a computable geometry.
        if (!filtered && width > 0 && height > 0 && components > 0 && bpc > 0)
        {
            long rowBytes = (((long)width * components * bpc) + 7) / 8;
            long afterData = dataStart + (rowBytes * height);
            if (afterData <= content.Length)
            {
                long k = afterData;
                while (k < content.Length && IsWhitespace(content[k]))
                {
                    k++;
                }

                if (k + 1 < content.Length
                    && content[k] == (byte)'E' && content[k + 1] == (byte)'I')
                {
                    return (int)k + 2;
                }
            }
        }

        // Fallback: whitespace-delimited EI scan.
        for (int i = dataStart; i + 1 < content.Length; i++)
        {
            if (content[i] == (byte)'E' && content[i + 1] == (byte)'I'
                && (i == 0 || IsWhitespace(content[i - 1]))
                && (i + 2 >= content.Length || IsWhitespace(content[i + 2])))
            {
                return i + 2;
            }
        }

        return -1;
    }

    private static bool IsWhitespace(byte b) =>
        b == 0x00 || b == 0x09 || b == 0x0A || b == 0x0C || b == 0x0D || b == 0x20;
}
