// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 4 (Phase 0) — LiPi automatic font embedding
//
// Provides the LiPi Sans faces as decoded sfnt programs. The faces ship as WOFF2
// (SIL OFL 1.1) and are decoded on first use via Woff2Unpacker and cached. Each
// script maps to one face; the face name is used as the builder's font key.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Chuvadi.Pdf.Fonts.Woff2;

namespace Chuvadi.Pdf.Authoring;

internal sealed class LipiFontSet
{
    /// <summary>The logical font name that triggers automatic LiPi selection.</summary>
    internal const string LogicalName = "Lipi";

    private static readonly IReadOnlyDictionary<LipiScript, string> ResourceByScript =
        new Dictionary<LipiScript, string>
        {
            [LipiScript.Latin] = "Latin",
            [LipiScript.Tamil] = "Tamil",
            [LipiScript.Devanagari] = "Devanagari",
            [LipiScript.Bengali] = "Bengali",
            [LipiScript.Gurmukhi] = "Gurmukhi",
            [LipiScript.Gujarati] = "Gujarati",
            [LipiScript.Odia] = "Odia",
            [LipiScript.Telugu] = "Telugu",
            [LipiScript.Kannada] = "Kannada",
            [LipiScript.Malayalam] = "Malayalam",
        };

    private readonly Dictionary<LipiScript, byte[]> _decoded = new Dictionary<LipiScript, byte[]>();

    /// <summary>Returns the registry font key for a script's LiPi face.</summary>
    /// <param name="script">The script.</param>
    /// <returns>The face name, e.g. "LiPi-Sans-Tamil".</returns>
    internal static string FaceName(LipiScript script) => "LiPi-Sans-" + ResourceByScript[script];

    /// <summary>Decodes (once) and returns the sfnt program for a script's LiPi face.</summary>
    /// <param name="script">The script.</param>
    /// <returns>The decoded TrueType sfnt bytes.</returns>
    /// <exception cref="InvalidOperationException">The bundled face is missing.</exception>
    internal byte[] GetFontProgram(LipiScript script)
    {
        if (_decoded.TryGetValue(script, out byte[]? cached))
        {
            return cached;
        }

        string resource = "Chuvadi.Pdf.Authoring.Lipi.LiPi-Sans-" + ResourceByScript[script] + ".woff2";
        Assembly assembly = typeof(LipiFontSet).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The bundled LiPi face '{resource}' is missing.");
        using MemoryStream buffer = new MemoryStream();
        stream.CopyTo(buffer);

        byte[] sfnt = Woff2Unpacker.Unpack(buffer.ToArray());
        _decoded[script] = sfnt;
        return sfnt;
    }
}
