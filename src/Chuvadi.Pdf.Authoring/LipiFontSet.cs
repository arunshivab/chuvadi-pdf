// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 4 (Phase 0) — LiPi automatic font embedding
//
// Maps each script to its LiPi Sans face. The faces themselves come from the
// Chuvadi.Fonts package (3.31, D-113): the Regular file of each script. The
// face name is used as the builder's font key.

using System.Collections.Concurrent;
using System.Collections.Generic;
using Chuvadi.Fonts;
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

    private static readonly ConcurrentDictionary<LipiScript, byte[]> Programs = new ConcurrentDictionary<LipiScript, byte[]>();

    /// <summary>Returns the Chuvadi.Fonts file name of a script's LiPi face.</summary>
    /// <param name="script">The script.</param>
    /// <returns>The file name, e.g. "LiPi-Sans-Tamil-Regular.ttf".</returns>
    internal static string FileName(LipiScript script) => FaceName(script) + "-Regular.ttf";

    /// <summary>Returns the sfnt program for a script's LiPi face (read once, shared).</summary>
    /// <param name="script">The script.</param>
    /// <returns>The TrueType sfnt bytes. The array is shared: do not change it.</returns>
    internal byte[] GetFontProgram(LipiScript script) => Programs.GetOrAdd(script, s => FontPack.ReadAllBytes(FileName(s)));
}
