// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.29 — raised and lowered text: classification and plain-text form (D-104)

using System;
using System.Collections.Generic;
using System.Text;
using Chuvadi.Pdf.Content;

namespace Chuvadi.Pdf.Text;

/// <summary>
/// Finds raised and lowered small text (superscripts, subscripts) on each
/// line, records it on the fragments (<see cref="TextFragment.BaselineShift"/>),
/// and decides how plain text writes it (<see cref="SuperscriptMode"/>).
/// </summary>
internal static class ScriptText
{
    // A fragment is raised or lowered when it is at most this fraction of the
    // size of its line's main text and its baseline is off the main baseline
    // by more than this fraction of that size (subscripts are often lowered
    // by little more than a tenth of an em).
    private const double SizeRatio = 0.85;
    private const double ShiftRatio = 0.1;

    // Sizes within this fraction of each other count as the same size.
    private const double SameSize = 0.02;

    // A raised or lowered run must touch the main text: within this fraction
    // of the main size of a neighbour on the line.
    private const double TouchRatio = 0.5;

    // At most this many letters in a run written with Unicode characters;
    // longer raised words stay words.
    private const int MaxMappedLetters = 3;

    // The Unicode superscript and subscript forms (each NFKC-normalises back
    // to the plain character; hyphen-minus and minus both become the
    // superscript or subscript minus).
    private const string SuperFrom = "0123456789+-−=()abcdefghijklmnoprstuvwxyzABDEGHIJKLMNOPRTUVW";
    private const string SuperTo = "⁰¹²³⁴⁵⁶⁷⁸⁹⁺⁻⁻⁼⁽⁾ᵃᵇᶜᵈᵉᶠᵍʰⁱʲᵏˡᵐⁿᵒᵖʳˢᵗᵘᵛʷˣʸᶻᴬᴮᴰᴱᴳᴴᴵᴶᴷᴸᴹᴺᴼᴾᴿᵀᵁⱽᵂ";
    private const string SubFrom = "0123456789+-−=()aehijklmnoprstuvxə";
    private const string SubTo = "₀₁₂₃₄₅₆₇₈₉₊₋₋₌₍₎ₐₑₕᵢⱼₖₗₘₙₒₚᵣₛₜᵤᵥₓₔ";

    // Kept as they are inside a run ("²,³", "ᶠ,*").
    private const string PassThrough = " ,.;*†‡§′″'’";

    private static readonly Dictionary<char, char> Super = Table(SuperFrom, SuperTo);
    private static readonly Dictionary<char, char> Sub = Table(SubFrom, SubTo);
    private static readonly HashSet<char> SuperForms = new HashSet<char>(SuperTo);
    private static readonly HashSet<char> SubForms = new HashSet<char>(SubTo);

    /// <summary>
    /// Returns the fragments in the same order, each raised or lowered one
    /// replaced by a copy carrying its <see cref="BaselineShift"/> and shift.
    /// </summary>
    internal static List<TextFragment> Classify(List<TextFragment> fragments)
    {
        List<TextFragment> result = new List<TextFragment>(fragments.Count);
        if (fragments.Count < 2)
        {
            result.AddRange(fragments);
            return result;
        }

        Dictionary<TextFragment, TextFragment> shifted =
            new Dictionary<TextFragment, TextFragment>(ReferenceEqualityComparer.Instance);
        foreach (List<TextFragment> line in LayoutExtractor.GroupIntoLines(fragments))
        {
            if (line.Count >= 2)
            {
                ClassifyLine(line, shifted);
            }
        }

        foreach (TextFragment f in fragments)
        {
            result.Add(shifted.TryGetValue(f, out TextFragment? copy) ? copy : f);
        }

        return result;
    }

    private static void ClassifyLine(List<TextFragment> line, Dictionary<TextFragment, TextFragment> shifted)
    {
        double angle = line[0].Angle;
        double main = MainSize(line);
        if (main <= 0)
        {
            return;
        }

        double baseline = MainBaseline(line, main, angle);
        List<TextFragment> sorted = new List<TextFragment>(line);
        sorted.Sort((a, b) => TextGeometry.Along(a, angle).CompareTo(TextGeometry.Along(b, angle)));

        // -1 lowered, +1 raised, 0 on the baseline.
        int[] side = new int[sorted.Count];
        for (int i = 0; i < sorted.Count; i++)
        {
            TextFragment f = sorted[i];
            double shift = TextGeometry.Across(f, angle) - baseline;
            if (f.Text.Trim().Length > 0 && f.FontSize <= main * SizeRatio && Math.Abs(shift) > main * ShiftRatio)
            {
                side[i] = shift > 0 ? 1 : -1;
            }
        }

        int start = 0;
        while (start < sorted.Count)
        {
            if (side[start] == 0)
            {
                start++;
                continue;
            }

            int end = start;
            while (end + 1 < sorted.Count && side[end + 1] == side[start])
            {
                end++;
            }

            if (Touches(sorted, start, end, main, angle))
            {
                BaselineShift kind = side[start] > 0 ? BaselineShift.Raised : BaselineShift.Lowered;
                for (int i = start; i <= end; i++)
                {
                    TextFragment f = sorted[i];
                    shifted[f] = f.WithBaselineShift(kind, TextGeometry.Across(f, angle) - baseline);
                }
            }

            start = end + 1;
        }
    }

    // True when the run sorted[start..end] sits next to the text before or
    // after it on the line: it starts after the text before it begins and at
    // most a little past its end, or the text after it begins at most a
    // little past the run's end. (When the font's widths are not known the
    // ends are estimates, so an apparent overlap still counts as touching.)
    private static bool Touches(List<TextFragment> sorted, int start, int end, double main, double angle)
    {
        double limit = main * TouchRatio;
        if (start > 0)
        {
            TextFragment left = sorted[start - 1];
            double at = TextGeometry.Along(sorted[start], angle);
            double leftAt = TextGeometry.Along(left, angle);
            if (at > leftAt && at - (leftAt + Width(left)) <= limit)
            {
                return true;
            }
        }

        if (end + 1 < sorted.Count)
        {
            TextFragment last = sorted[end];
            double lastAt = TextGeometry.Along(last, angle);
            double rightAt = TextGeometry.Along(sorted[end + 1], angle);
            if (rightAt > lastAt && rightAt - (lastAt + Width(last)) <= limit)
            {
                return true;
            }
        }

        return false;
    }

    // The size carrying the most characters on the line.
    private static double MainSize(List<TextFragment> line)
    {
        double best = 0;
        int bestWeight = -1;
        foreach (TextFragment candidate in line)
        {
            int weight = 0;
            foreach (TextFragment f in line)
            {
                if (Math.Abs(f.FontSize - candidate.FontSize) <= candidate.FontSize * SameSize)
                {
                    weight += Math.Max(1, f.Text.Trim().Length);
                }
            }

            if (weight > bestWeight || (weight == bestWeight && candidate.FontSize > best))
            {
                best = candidate.FontSize;
                bestWeight = weight;
            }
        }

        return best;
    }

    // The median baseline of the line's main-size text.
    private static double MainBaseline(List<TextFragment> line, double main, double angle)
    {
        List<double> across = new List<double>();
        foreach (TextFragment f in line)
        {
            if (Math.Abs(f.FontSize - main) <= main * SameSize)
            {
                across.Add(TextGeometry.Across(f, angle));
            }
        }

        across.Sort();
        return across[across.Count / 2];
    }

    /// <summary>
    /// Plans one line in reading order: the text to write for each fragment,
    /// and for each boundary whether raised or lowered text joins its
    /// neighbour (true) or follows the 3.28 rule (a space between a letter
    /// or digit and raised or lowered text).
    /// </summary>
    internal static (string[] Texts, bool[] JoinBefore) Plan(IReadOnlyList<TextFragment> line, SuperscriptMode mode)
    {
        int n = line.Count;
        string[] texts = new string[n];
        bool[] join = new bool[n];
        for (int i = 0; i < n; i++)
        {
            texts[i] = line[i].Text;
        }

        if (mode == SuperscriptMode.Separate)
        {
            return (texts, join);
        }

        int start = 0;
        while (start < n)
        {
            BaselineShift kind = line[start].BaselineShift;
            if (kind == BaselineShift.None)
            {
                start++;
                continue;
            }

            int end = start;
            while (end + 1 < n && line[end + 1].BaselineShift == kind)
            {
                end++;
            }

            bool joins = mode == SuperscriptMode.Join || JoinsAsUnicode(line, texts, start, end, kind);
            if (joins)
            {
                join[start] = start > 0;
                if (end + 1 < n)
                {
                    join[end + 1] = true;
                }
            }

            start = end + 1;
        }

        return (texts, join);
    }

    // Unicode mode: an ordinal ending after a digit joins as it is; a run
    // with exact Unicode forms is rewritten and joins; anything else (no exact
    // form, or /ActualText, whose source text wins) stays as in 3.28.
    private static bool JoinsAsUnicode(IReadOnlyList<TextFragment> line, string[] texts, int start, int end, BaselineShift kind)
    {
        StringBuilder run = new StringBuilder();
        for (int i = start; i <= end; i++)
        {
            if (line[i].IsActualText)
            {
                return false;
            }

            run.Append(texts[i]);
        }

        if (kind == BaselineShift.Raised && start > 0 && IsOrdinalEnding(run.ToString().Trim()))
        {
            string before = texts[start - 1];
            if (before.Length > 0 && char.IsDigit(before[^1]))
            {
                return true;
            }
        }

        Dictionary<char, char> table = kind == BaselineShift.Raised ? Super : Sub;
        HashSet<char> forms = kind == BaselineShift.Raised ? SuperForms : SubForms;
        string[] mapped = new string[end - start + 1];
        int written = 0;
        int letters = 0;
        for (int i = start; i <= end; i++)
        {
            StringBuilder sb = new StringBuilder(texts[i].Length);
            foreach (char c in texts[i])
            {
                if (forms.Contains(c))
                {
                    written++;
                    sb.Append(c);
                }
                else if (table.TryGetValue(c, out char form))
                {
                    written++;
                    letters += char.IsLetter(c) ? 1 : 0;
                    sb.Append(form);
                }
                else if (PassThrough.Contains(c, StringComparison.Ordinal))
                {
                    sb.Append(c);
                }
                else
                {
                    return false;
                }
            }

            mapped[i - start] = sb.ToString();
        }

        if (written == 0 || letters > MaxMappedLetters)
        {
            return false;
        }

        for (int i = start; i <= end; i++)
        {
            texts[i] = mapped[i - start];
        }

        return true;
    }

    private static bool IsOrdinalEnding(string s) =>
        s.Equals("st", StringComparison.OrdinalIgnoreCase)
        || s.Equals("nd", StringComparison.OrdinalIgnoreCase)
        || s.Equals("rd", StringComparison.OrdinalIgnoreCase)
        || s.Equals("th", StringComparison.OrdinalIgnoreCase);

    /// <summary>The fragment's advance, measured or estimated.</summary>
    internal static double Width(TextFragment fragment) =>
        double.IsNaN(fragment.Width) ? fragment.Text.Length * fragment.FontSize * 0.6 : fragment.Width;

    private static Dictionary<char, char> Table(string from, string to)
    {
        Dictionary<char, char> table = new Dictionary<char, char>(from.Length);
        for (int i = 0; i < from.Length; i++)
        {
            table[from[i]] = to[i];
        }

        return table;
    }
}
