// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Document operations — stamp anchor positions.

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// The twelve positions a text stamp can be anchored to on a page: the three
/// top and three bottom positions (horizontal text), plus three on each
/// vertical edge (text rotated 90° to read up the left edge or down the right
/// edge). Margins are measured inward from the page edge to the anchor.
/// </summary>
public enum StampAnchor
{
    /// <summary>Top-left, horizontal.</summary>
    TopLeft,

    /// <summary>Top-centre, horizontal.</summary>
    TopCenter,

    /// <summary>Top-right, horizontal.</summary>
    TopRight,

    /// <summary>Bottom-left, horizontal.</summary>
    BottomLeft,

    /// <summary>Bottom-centre, horizontal.</summary>
    BottomCenter,

    /// <summary>Bottom-right, horizontal.</summary>
    BottomRight,

    /// <summary>Left edge, top, text reading upward (rotated 90° CCW).</summary>
    LeftEdgeTop,

    /// <summary>Left edge, middle, text reading upward (rotated 90° CCW).</summary>
    LeftEdgeMiddle,

    /// <summary>Left edge, bottom, text reading upward (rotated 90° CCW).</summary>
    LeftEdgeBottom,

    /// <summary>Right edge, top, text reading downward (rotated 90° CW).</summary>
    RightEdgeTop,

    /// <summary>Right edge, middle, text reading downward (rotated 90° CW).</summary>
    RightEdgeMiddle,

    /// <summary>Right edge, bottom, text reading downward (rotated 90° CW).</summary>
    RightEdgeBottom,
}
