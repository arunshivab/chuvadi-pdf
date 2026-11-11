// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.8.2 (content streams), §7.4.4 (FlateDecode)
// PHASE: 3.33.0 (ticket 39, D-325) — a page whose content stream is damaged
//        shows what decoded before the damage, with a StreamDamaged warning.

using System;

namespace Chuvadi.Pdf.Filters;

/// <summary>
/// What a damaged content stream still holds: the bytes a filter decoded
/// before it found the damage, cut back to the last whitespace so that no
/// half-read operand or operator is run.
/// </summary>
internal static class DamagedContent
{
    /// <summary>
    /// The usable part of a damaged stream: its decoded bytes up to and
    /// including the last whitespace, or empty when the filter could not tell
    /// what it decoded.
    /// </summary>
    /// <param name="damage">The filter's error.</param>
    /// <returns>The bytes to draw.</returns>
    internal static byte[] UsablePrefix(FilterException damage)
    {
        ArgumentNullException.ThrowIfNull(damage);
        byte[]? partial = damage.PartialOutput;
        if (partial is null)
        {
            return Array.Empty<byte>();
        }

        int end = partial.Length;
        while (end > 0 && !IsWhitespace(partial[end - 1]))
        {
            end--;
        }

        return partial.AsSpan(0, end).ToArray();
    }

    /// <summary>The warning text for a damaged page content stream.</summary>
    /// <param name="damage">The filter's error.</param>
    /// <returns>The reason, saying what is drawn.</returns>
    internal static string Reason(FilterException damage)
    {
        ArgumentNullException.ThrowIfNull(damage);
        return damage.PartialOutput is { Length: > 0 }
            ? damage.Message + " The page shows what decoded before the damage."
            : damage.Message + " The page content could not be read and is left out.";
    }

    // PDF white-space characters (PDF 32000-1:2008 Table 1).
    private static bool IsWhitespace(byte b) => b is 0x00 or 0x09 or 0x0A or 0x0C or 0x0D or 0x20;
}
