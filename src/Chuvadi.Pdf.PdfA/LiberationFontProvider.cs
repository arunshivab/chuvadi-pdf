// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 3 — PDF/A font embedding
//
// Loads the bundled Liberation faces (SIL OFL 1.1, metric-compatible with the
// Standard-14 Latin fonts) used as embeddable substitutes.

using System.IO;
using System.Reflection;

namespace Chuvadi.Pdf.PdfA;

internal static class LiberationFontProvider
{
    /// <summary>
    /// Returns the TTF bytes for a Liberation face key (e.g.
    /// "LiberationSans-Regular"), or null when the face is not bundled.
    /// </summary>
    /// <param name="face">The Liberation face key.</param>
    /// <returns>The font bytes, or null.</returns>
    internal static byte[]? Get(string face)
    {
        string resource = "Chuvadi.Pdf.PdfA.Fonts." + face + ".ttf";
        Assembly assembly = typeof(LiberationFontProvider).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(resource);
        if (stream is null)
        {
            return null;
        }

        using MemoryStream buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
