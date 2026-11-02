// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.3.4 — String objects, §7.3.5 — Name objects
// PHASE: Phase 2.8 — DisplayList consolidation (one walker, two sinks)
// Token-to-value decoding shared by the content-stream walker.

using System;
using System.Text;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Rendering.Walking;

/// <summary>
/// Decodes string and name tokens for the content-stream walker.
/// </summary>
/// <remarks>
/// Consolidation note: the two pre-2.8 builders carried separate copies of
/// this logic, and the raster copy did not decode octal escapes
/// (<c>\nnn</c>, §7.3.4.2) — literal strings written with octal-escaped
/// bytes (as Chuvadi's own authoring layer emits for WinAnsi characters)
/// rendered their escape characters verbatim through the raster path. This
/// unified decoder handles octal on both paths.
/// </remarks>
internal static class ContentStrings
{
    /// <summary>
    /// Extracts the bytes of a literal or hex string token, applying §7.3.4
    /// escape and hex decoding. Non-string tokens return their raw bytes.
    /// </summary>
    internal static byte[] ExtractStringBytes(PdfToken token)
    {
        if (token.Type == PdfTokenType.HexString)
        {
            return PdfString.DecodeHexToken(token.RawBytes);
        }
        if (token.Type == PdfTokenType.LiteralString)
        {
            return PdfString.DecodeLiteralToken(token.RawBytes);
        }
        return token.RawBytes;
    }

    /// <summary>Extracts a name token's text; empty for non-name tokens.</summary>
    internal static string ExtractName(PdfToken token)
    {
        if (token.Type != PdfTokenType.Name)
        {
            return string.Empty;
        }

        // "#xx" escapes are decoded (§7.3.5), as the object parser does for
        // resource dictionary keys, so /F#20A finds the key "F A" (3.30).
        if (token.RawBytes.Length == 0 || Array.IndexOf(token.RawBytes, (byte)'#') < 0)
        {
            return Encoding.Latin1.GetString(token.RawBytes);
        }

        try
        {
            return PdfName.FromRawBytes(token.RawBytes).Value;
        }
        catch (PdfParseException)
        {
            return Encoding.Latin1.GetString(token.RawBytes);
        }
    }

}
