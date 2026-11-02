// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.30 — one shared copy of the bundled fonts (D-87, D-100, D-105)
//
// The LiPi Sans faces ship as WOFF2 (SIL OFL 1.1) embedded in this assembly
// and are decoded on first use. Authoring embeds them in the PDFs it writes;
// the raster renderer draws Indic text in fonts a PDF does not embed with them.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Chuvadi.Pdf.Fonts.Woff2;

namespace Chuvadi.Pdf.Fonts.Bundled;

/// <summary>
/// The fonts bundled with Chuvadi: LiPi Sans, one face per script (Latin,
/// Tamil, Devanagari, Bengali, Gurmukhi, Gujarati, Odia, Telugu, Kannada and
/// Malayalam), licensed under the SIL Open Font License 1.1 (the licence text
/// is in this package).
/// </summary>
public static class BundledFonts
{
    private const string Prefix = "LiPi-Sans-";

    private static readonly string[] Scripts =
    {
        "Latin", "Tamil", "Devanagari", "Bengali", "Gurmukhi", "Gujarati", "Odia", "Telugu", "Kannada", "Malayalam",
    };

    private static readonly Dictionary<string, byte[]> Decoded = new Dictionary<string, byte[]>(StringComparer.Ordinal);

    /// <summary>Gets the face names, such as <c>LiPi-Sans-Tamil</c>.</summary>
    public static IReadOnlyList<string> FaceNames { get; } = Array.ConvertAll(Scripts, s => Prefix + s);

    /// <summary>
    /// Returns the face name for a script name (<c>"Tamil"</c> gives
    /// <c>LiPi-Sans-Tamil</c>), or null when no bundled face covers it.
    /// </summary>
    /// <param name="script">The script name, as in <see cref="FaceNames"/> without the prefix.</param>
    /// <returns>The face name, or null.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="script"/> is null.</exception>
    public static string? FaceForScript(string script)
    {
        ArgumentNullException.ThrowIfNull(script);
        return Array.IndexOf(Scripts, script) >= 0 ? Prefix + script : null;
    }

    /// <summary>
    /// Returns a face's TrueType program, decoded from its WOFF2 file on the
    /// first call and shared after that. The array is shared: do not change it.
    /// </summary>
    /// <param name="faceName">A name from <see cref="FaceNames"/>.</param>
    /// <returns>The decoded TrueType font program.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="faceName"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="faceName"/> is not a bundled face.</exception>
    public static byte[] GetFontProgram(string faceName)
    {
        ArgumentNullException.ThrowIfNull(faceName);
        lock (Decoded)
        {
            if (Decoded.TryGetValue(faceName, out byte[]? cached))
            {
                return cached;
            }

            int index = Array.IndexOf(Scripts, faceName.StartsWith(Prefix, StringComparison.Ordinal) ? faceName[Prefix.Length..] : string.Empty);
            if (index < 0)
            {
                throw new ArgumentException($"'{faceName}' is not a bundled face.", nameof(faceName));
            }

            string resource = "Chuvadi.Pdf.Fonts.Bundled." + faceName + ".woff2";
            Assembly assembly = typeof(BundledFonts).Assembly;
            using Stream stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"The bundled face '{resource}' is missing from the assembly.");
            using MemoryStream buffer = new MemoryStream();
            stream.CopyTo(buffer);

            byte[] sfnt = Woff2Unpacker.Unpack(buffer.ToArray());
            Decoded[faceName] = sfnt;
            return sfnt;
        }
    }
}
