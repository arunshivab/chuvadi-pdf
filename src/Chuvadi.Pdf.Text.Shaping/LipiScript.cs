// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 4 (Phase 0) — LiPi automatic script selection
//
// Classifies code points into the scripts covered by the LiPi Sans family and
// splits a string into maximal same-script runs, so each run can be drawn with
// the matching LiPi face. This is selection only; it performs no shaping.

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Text.Shaping;

/// <summary>A script covered by the LiPi Sans font family.</summary>
public enum LipiScript
{
    /// <summary>Latin and any code point outside the supported Indic blocks.</summary>
    Latin,

    /// <summary>Tamil (U+0B80–U+0BFF).</summary>
    Tamil,

    /// <summary>Devanagari (U+0900–U+097F).</summary>
    Devanagari,

    /// <summary>Bengali (U+0980–U+09FF).</summary>
    Bengali,

    /// <summary>Gurmukhi (U+0A00–U+0A7F).</summary>
    Gurmukhi,

    /// <summary>Gujarati (U+0A80–U+0AFF).</summary>
    Gujarati,

    /// <summary>Odia (U+0B00–U+0B7F).</summary>
    Odia,

    /// <summary>Telugu (U+0C00–U+0C7F).</summary>
    Telugu,

    /// <summary>Kannada (U+0C80–U+0CFF).</summary>
    Kannada,

    /// <summary>Malayalam (U+0D00–U+0D7F).</summary>
    Malayalam,
}

/// <summary>A maximal run of text in a single script.</summary>
/// <param name="Script">The run's script.</param>
/// <param name="Text">The run's text.</param>
public readonly record struct ScriptRun(LipiScript Script, string Text);

/// <summary>Classifies code points by script and splits text into script runs.</summary>
public static class ScriptClassifier
{
    /// <summary>Returns the LiPi script for a Unicode code point.</summary>
    /// <param name="codepoint">The Unicode scalar value.</param>
    /// <returns>The matching script, or <see cref="LipiScript.Latin"/> when outside the Indic blocks.</returns>
    public static LipiScript Classify(int codepoint)
    {
        if (codepoint >= 0x0B80 && codepoint <= 0x0BFF) { return LipiScript.Tamil; }
        if (codepoint >= 0x0900 && codepoint <= 0x097F) { return LipiScript.Devanagari; }
        if (codepoint >= 0x0980 && codepoint <= 0x09FF) { return LipiScript.Bengali; }
        if (codepoint >= 0x0A00 && codepoint <= 0x0A7F) { return LipiScript.Gurmukhi; }
        if (codepoint >= 0x0A80 && codepoint <= 0x0AFF) { return LipiScript.Gujarati; }
        if (codepoint >= 0x0B00 && codepoint <= 0x0B7F) { return LipiScript.Odia; }
        if (codepoint >= 0x0C00 && codepoint <= 0x0C7F) { return LipiScript.Telugu; }
        if (codepoint >= 0x0C80 && codepoint <= 0x0CFF) { return LipiScript.Kannada; }
        if (codepoint >= 0x0D00 && codepoint <= 0x0D7F) { return LipiScript.Malayalam; }
        return LipiScript.Latin;
    }

    /// <summary>
    /// Splits text into maximal same-script runs. Whitespace attaches to the
    /// current run so that interleaved spaces do not fragment a passage.
    /// </summary>
    /// <param name="text">The text to split.</param>
    /// <returns>The ordered script runs.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    public static IReadOnlyList<ScriptRun> Split(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        List<ScriptRun> runs = new List<ScriptRun>();
        if (text.Length == 0)
        {
            return runs;
        }

        System.Text.StringBuilder current = new System.Text.StringBuilder();
        LipiScript currentScript = LipiScript.Latin;
        bool have = false;

        int i = 0;
        while (i < text.Length)
        {
            int codepoint = char.ConvertToUtf32(text, i);
            int width = char.IsSurrogatePair(text, i) ? 2 : 1;
            string piece = text.Substring(i, width);
            i += width;

            bool neutral = codepoint == ' ' || codepoint == '\u00A0' || codepoint == '\t';
            LipiScript script = neutral && have ? currentScript : Classify(codepoint);

            if (!have)
            {
                currentScript = script;
                have = true;
            }
            else if (script != currentScript)
            {
                runs.Add(new ScriptRun(currentScript, current.ToString()));
                current.Clear();
                currentScript = script;
            }

            current.Append(piece);
        }

        if (current.Length > 0)
        {
            runs.Add(new ScriptRun(currentScript, current.ToString()));
        }

        return runs;
    }
}
