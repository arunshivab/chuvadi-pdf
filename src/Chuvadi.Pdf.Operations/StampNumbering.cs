// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Document operations — Bates / styled numbering options for stamps.

using Chuvadi.Pdf.Authoring;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// Describes a running numbering sequence for the <c>{number}</c> stamp token:
/// a free prefix and suffix, a start value, an optional zero-pad width, a
/// <see cref="NumberingFormat"/> style, and first-page handling. Used with the
/// <see cref="TextStamper"/> numbering overload to produce Bates-style labels
/// such as <c>BATES-000123</c> in a single stamp pass.
/// </summary>
public sealed class StampNumbering
{
    /// <summary>Gets or initialises the text placed before the number. Default: empty.</summary>
    public string Prefix { get; init; } = string.Empty;

    /// <summary>Gets or initialises the text placed after the number. Default: empty.</summary>
    public string Suffix { get; init; } = string.Empty;

    /// <summary>Gets or initialises the value assigned to the first counted page. Default: 1.</summary>
    public int StartValue { get; init; } = 1;

    /// <summary>
    /// Gets or initialises the minimum width of the numeric core, left-filled
    /// with zeros. Applies to <see cref="NumberingFormat.Arabic"/> only; ignored
    /// for roman and letter styles. Zero (the default) means no padding.
    /// </summary>
    public int PadWidth { get; init; }

    /// <summary>Gets or initialises the numbering style. Default: <see cref="NumberingFormat.Arabic"/>.</summary>
    public NumberingFormat Numbering { get; init; } = NumberingFormat.Arabic;

    /// <summary>Gets or initialises first-page handling. Default: <see cref="StampFirstPageMode.Number"/>.</summary>
    public StampFirstPageMode FirstPage { get; init; } = StampFirstPageMode.Number;

    /// <summary>
    /// Returns the sequence value for the given zero-based document page index,
    /// honouring <see cref="FirstPage"/>, or null when the page is not counted
    /// (and therefore not stamped). The counter is anchored to the literal first
    /// page (index 0) regardless of which pages are selected for stamping.
    /// </summary>
    /// <param name="pageIndex">The zero-based document page index.</param>
    /// <returns>The sequence value, or null when the page is skipped.</returns>
    public int? ResolveValue(int pageIndex)
    {
        if (pageIndex == 0)
        {
            return FirstPage == StampFirstPageMode.Number ? StartValue : (int?)null;
        }

        if (FirstPage == StampFirstPageMode.SkipRenumber)
        {
            return StartValue + (pageIndex - 1);
        }

        return StartValue + pageIndex;
    }

    /// <summary>
    /// Formats a sequence value into its styled label: the prefix, the formatted
    /// (and, for <see cref="NumberingFormat.Arabic"/>, zero-padded) number, then
    /// the suffix.
    /// </summary>
    /// <param name="value">The sequence value to format.</param>
    /// <returns>The styled label.</returns>
    public string Format(int value)
    {
        string core = PageNumberFormatter.Format(value, Numbering);

        if (Numbering == NumberingFormat.Arabic && PadWidth > 0)
        {
            core = core.PadLeft(PadWidth, '0');
        }

        return Prefix + core + Suffix;
    }
}
