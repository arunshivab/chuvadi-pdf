// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.27 — leader spacing in extracted text (D-72)

using Chuvadi.Pdf.Content;

namespace Chuvadi.Pdf.Text;

/// <summary>
/// Keeps a dot leader apart from the words on either side of it. A table of
/// contents line such as "ROOMS ........ 3" is often drawn with the leader
/// starting exactly where the label ends (the tab before it is not drawn),
/// so there is no gap to read a space from. A fragment made only of four or
/// more leader characters ('.', '…', '·', '_', '-') is separated from text
/// that touches it.
/// </summary>
internal static class LeaderSpacing
{
    private const int MinLeaderLength = 4;

    /// <summary>
    /// True when a space belongs between <paramref name="prev"/> and
    /// <paramref name="current"/> on one line because one of them is a leader
    /// and the other is text that touches it.
    /// </summary>
    internal static bool NeedsSpace(TextFragment prev, TextFragment current)
    {
        string left = prev.Text;
        string right = current.Text;
        if (left.Length == 0 || right.Length == 0)
        {
            return false;
        }

        char last = left[^1];
        char first = right[0];
        if (char.IsWhiteSpace(last) || char.IsWhiteSpace(first))
        {
            return false;
        }

        return (IsLeader(right) && !IsLeaderChar(last)) || (IsLeader(left) && !IsLeaderChar(first));
    }

    private static bool IsLeader(string text)
    {
        if (text.Length < MinLeaderLength)
        {
            return false;
        }

        foreach (char c in text)
        {
            if (!IsLeaderChar(c))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsLeaderChar(char c) => c is '.' or '…' or '·' or '_' or '-';
}
