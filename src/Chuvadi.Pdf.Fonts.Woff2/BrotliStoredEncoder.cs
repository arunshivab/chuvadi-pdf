// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 7932 — Brotli Compressed Data Format
// PHASE: Phase 2.1 — Brotli stored-block encoder (delegating shim since Phase 2.2)


namespace Chuvadi.Pdf.Fonts.Woff2;

/// <summary>
/// Emits Brotli-compatible bitstreams for WOFF2 packaging.
/// </summary>
/// <remarks>
/// <para>
/// Since Phase 2.2 this is a thin shim over <see cref="BrotliEncoder"/>, the
/// pure-C# clean-room Brotli implementation. The shim is preserved so that
/// callers from Phase 2.1 continue to work without source changes.
/// </para>
/// <para>
/// Behaviour: emits valid Brotli output using uncompressed (stored) meta-blocks.
/// Output bytes are byte-identical to <see cref="BrotliEncoder.Encode"/>.
/// </para>
/// </remarks>
[System.Obsolete("Deprecated: no longer used by Chuvadi; for the LiPi Sans faces use Chuvadi.Fonts (FontPack). Removed at the Pdf move.")]
public static class BrotliStoredEncoder
{
    /// <summary>Encodes <paramref name="data"/> as a valid Brotli stream.</summary>
    public static byte[] Encode(byte[] data) => BrotliEncoder.Encode(data);
}
