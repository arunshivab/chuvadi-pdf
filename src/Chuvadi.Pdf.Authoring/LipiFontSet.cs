// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 4 (Phase 0) — LiPi automatic font embedding
//
// Maps each script to its LiPi Sans face. The faces themselves live in the
// shared Chuvadi.Pdf.Fonts.Bundled package (3.30); the face name is used as
// the builder's font key.

using System.Collections.Generic;
using Chuvadi.Pdf.Fonts.Bundled;
using Chuvadi.Pdf.Text.Shaping;

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

    /// <summary>Returns the registry font key for a script's LiPi face.</summary>
    /// <param name="script">The script.</param>
    /// <returns>The face name, e.g. "LiPi-Sans-Tamil".</returns>
    internal static string FaceName(LipiScript script) => "LiPi-Sans-" + ResourceByScript[script];

    /// <summary>Returns the sfnt program for a script's LiPi face (decoded once, shared).</summary>
    /// <param name="script">The script.</param>
    /// <returns>The decoded TrueType sfnt bytes.</returns>
    internal byte[] GetFontProgram(LipiScript script) => BundledFonts.GetFontProgram(FaceName(script));
}
