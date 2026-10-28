// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Fonts.Rendering;
using Chuvadi.Pdf.Fonts.Woff2;

namespace Chuvadi.Pdf.Text.Shaping.Tests;

/// <summary>Loads the test fonts (the shipped LiPi Sans WOFF2 files and the fixtures).</summary>
internal static class TestFonts
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
    private static readonly Dictionary<string, FontFace> Faces = new Dictionary<string, FontFace>(StringComparer.Ordinal);

    internal static FontFace Lipi(LipiScript script) => Face("LiPi-Sans-" + script + ".woff2");

    internal static FontFace Face(string file)
    {
        lock (Faces)
        {
            if (!Faces.TryGetValue(file, out FontFace? face))
            {
                string path = Locate(file);
                byte[] bytes = file.EndsWith(".woff2", StringComparison.Ordinal)
                    ? Woff2Unpacker.Unpack(File.ReadAllBytes(path))
                    : File.ReadAllBytes(path);
                face = FontFace.Load(bytes);
                Faces[file] = face;
            }

            return face;
        }
    }

    // Fixtures, then the fonts under the repository's tools/fonts (for the full corpus).
    private static string Locate(string file)
    {
        foreach (string dir in new[] { Fixtures, Path.Combine(Fixtures, "LiPi"), Path.Combine(Fixtures, "Fonts") })
        {
            string candidate = Path.Combine(dir, file);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        for (DirectoryInfo? d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            string candidate = Path.Combine(d.FullName, "tools", "fonts", file);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("Test font not found.", file);
    }
}
