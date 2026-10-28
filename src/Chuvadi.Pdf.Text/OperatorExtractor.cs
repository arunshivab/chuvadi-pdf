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
/// For complex layouts (multi-column, tables) use <see cref="LayoutExtractor"/>.
/// PDF 32000-1:2008 §9.10 — Extraction of text content.
/// </remarks>
public sealed class OperatorExtractor
{
    /// <summary>
    /// Converts a list of text fragments to a plain text string
    /// using stream-order heuristics.
    /// </summary>
    /// <param name="fragments">Fragments from <see cref="ContentStreamParser"/>.</param>
    /// <returns>The extracted text with word spaces and line breaks inserted.</returns>
    public string Extract(List<TextFragment> fragments)
    {
        if (fragments is null)
        {
            throw new ArgumentNullException(nameof(fragments));
        }

        if (fragments.Count == 0)
        {
            return string.Empty;
        }

        StringBuilder sb = new StringBuilder();
        TextFragment prev = fragments[0];
        sb.Append(prev.Text);

        for (int i = 1; i < fragments.Count; i++)
        {
            if ((i & 4095) == 0)
            {
                PdfWorkScope.ThrowIfCancellationRequested();
            }

            TextFragment current = fragments[i];

            double xGap = current.X - (prev.X + EstimateWidth(prev));
            double yDrop = Math.Abs(current.Y - prev.Y);
            double lineThreshold = prev.FontSize * 0.5;

            if (yDrop > lineThreshold)
            {
                // Significant vertical movement — treat as new line.
                sb.AppendLine();
            }
            else if (xGap > prev.FontSize * 0.3)
            {
                // Horizontal gap — treat as word space.
                sb.Append(' ');
            }

            sb.Append(current.Text);
            prev = current;
        }

        return sb.ToString().Trim();
    }

    private static double EstimateWidth(TextFragment fragment)
    {
        // The font's advance when the parser measured it; otherwise
        // approximately 0.6 of the font size per character.
        return double.IsNaN(fragment.Width) ? fragment.Text.Length * fragment.FontSize * 0.6 : fragment.Width;
    }
}
