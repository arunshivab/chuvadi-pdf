// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.10 — Extraction of text content
// PHASE: Phase 1 — Chuvadi.Pdf.Text
// Fast stream-order text extraction with word and line break heuristics.

using System;
using System.Collections.Generic;
using System.Text;
using Chuvadi.Pdf.Content;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Text;

/// <summary>
/// Extracts text from a list of <see cref="TextFragment"/> objects
/// in content stream order with simple heuristics for word and line breaks.
/// </summary>
/// <remarks>
/// This is the fastest extraction strategy. It preserves the order in which
/// text operators appear in the content stream, which matches reading order
/// for most well-structured born-digital PDFs.
///
/// Heuristics applied:
/// <list type="bullet">
///   <item>
///     A gap between two fragments whose X distance exceeds half the font size
///     is treated as a word space.
///   </item>
///   <item>
///     A vertical drop of more than half the font size between two fragments
///     is treated as a line break.
///   </item>
/// </list>
///
/// Raised and lowered text is written as <see cref="Superscripts"/> says
/// (since 3.29).
///
/// For complex layouts (multi-column, tables) use <see cref="LayoutExtractor"/>.
/// PDF 32000-1:2008 §9.10 — Extraction of text content.
/// </remarks>
public sealed class OperatorExtractor
{
    /// <summary>
    /// Gets or initialises how raised and lowered text is written (since
    /// 3.29). Default: <see cref="SuperscriptMode.Unicode"/>.
    /// </summary>
    public SuperscriptMode Superscripts { get; init; } = SuperscriptMode.Unicode;

    /// <summary>
    /// Converts a list of text fragments to a plain text string
    /// using stream-order heuristics.
    /// </summary>
    /// <param name="fragments">Fragments from <see cref="ContentStreamParser"/>.</param>
    /// <returns>The extracted text with word spaces and line breaks inserted.</returns>
    public string Extract(List<TextFragment> fragments)
    {
        ArgumentNullException.ThrowIfNull(fragments);
        return fragments.Count == 0 ? string.Empty : Write(ScriptText.Classify(fragments), Superscripts);
    }

    // Writes fragments in the given order: a new line on a move across the
    // baseline or a change of direction, a space on a gap along it. With
    // layoutSpacing (tagged order in Layout mode, 3.29) no space is added
    // where a fragment already ends or begins with one, as Layout mode does.
    internal static string Write(List<TextFragment> fragments, SuperscriptMode mode, bool layoutSpacing = false)
    {
        if (fragments.Count == 0)
        {
            return string.Empty;
        }

        // Split into lines first, so raised and lowered runs are planned
        // within the line they belong to (3.29).
        List<List<TextFragment>> lines = new List<List<TextFragment>>();
        List<TextFragment> line = new List<TextFragment> { fragments[0] };
        for (int i = 1; i < fragments.Count; i++)
        {
            if ((i & 4095) == 0)
            {
                PdfWorkScope.ThrowIfCancellationRequested();
            }

            TextFragment prev = fragments[i - 1];
            TextFragment current = fragments[i];

            // Measured across the previous fragment's baseline, so rotated
            // text is read like horizontal text (3.28). Half the larger size:
            // a superscript (small, raised) followed by text back on the
            // baseline is still the same line (3.28).
            double angle = prev.Angle;
            double yDrop = Math.Abs(TextGeometry.Across(current, angle) - TextGeometry.Across(prev, angle));
            double lineThreshold = Math.Max(prev.FontSize, current.FontSize) * 0.5;

            if (yDrop > lineThreshold || !TextGeometry.SameDirection(prev, current))
            {
                lines.Add(line);
                line = new List<TextFragment>();
            }

            line.Add(current);
        }

        lines.Add(line);

        StringBuilder sb = new StringBuilder();
        for (int l = 0; l < lines.Count; l++)
        {
            List<TextFragment> current = lines[l];
            if (l > 0)
            {
                sb.AppendLine();
            }

            (string[] texts, bool[] joinBefore) = ScriptText.Plan(current, mode);
            sb.Append(texts[0]);
            for (int i = 1; i < current.Count; i++)
            {
                TextFragment prev = current[i - 1];
                TextFragment fragment = current[i];
                double angle = prev.Angle;
                double xGap = TextGeometry.Along(fragment, angle) - (TextGeometry.Along(prev, angle) + ScriptText.Width(prev));

                // Gap along the baseline — treat as word space. A dot leader
                // touching a word (3.27) is kept apart from it too; raised or
                // lowered text joins its neighbour or is kept apart as the
                // superscript mode decides (3.28, 3.29).
                bool alreadySeparated = layoutSpacing
                    && ((texts[i - 1].Length > 0 && texts[i - 1][^1] == ' ')
                        || (texts[i].Length > 0 && texts[i][0] == ' '));
                if ((xGap > prev.FontSize * 0.3
                        || LeaderSpacing.NeedsSpace(prev, fragment)
                        || (!joinBefore[i] && TextGeometry.IsScriptBoundary(prev, fragment)))
                    && !alreadySeparated)
                {
                    sb.Append(' ');
                }

                sb.Append(texts[i]);
            }
        }

        return sb.ToString().Trim();
    }
}
