// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.33.0 (M1, D-302, D-311) — the shared standard-14 embedding engine
//
// The Liberation faces (SIL OFL 1.1, metric-compatible with the standard 14
// Latin fonts) that the engine embeds as substitutes, read from the
// Chuvadi.Fonts.Liberation package. This is the one place that knows where
// the font files come from.

using Chuvadi.Fonts.Liberation;

namespace Chuvadi.Pdf.Authoring;

internal static class LiberationFaces
{
    /// <summary>
    /// Returns the TTF bytes for a Liberation face key (for example
    /// "LiberationSans-Regular"), or null when the face is not available.
    /// </summary>
    /// <param name="face">The Liberation face key.</param>
    /// <returns>The font bytes, or null.</returns>
    internal static byte[]? Get(string face)
    {
        string fileName = face + ".ttf";
        return LiberationPack.Find(fileName) is null ? null : LiberationPack.ReadAllBytes(fileName);
    }
}
