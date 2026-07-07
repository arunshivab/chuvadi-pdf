// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  XFA 3.3 — layout, presence, and alignment property values.
// PHASE: LA-23b Phase A — template model.

namespace Chuvadi.Pdf.Xfa.Model;

/// <summary>The layout strategy a container applies to its children.</summary>
public enum XfaLayout
{
    /// <summary>Children are positioned by their explicit x/y coordinates.</summary>
    Position,

    /// <summary>Children flow top to bottom.</summary>
    TopToBottom,

    /// <summary>Children flow left to right, wrapping to new rows top to bottom.</summary>
    LeftRightTopToBottom,

    /// <summary>Children are laid out as table rows.</summary>
    Table,

    /// <summary>The container is a single table row of cells.</summary>
    Row,

    /// <summary>Children flow into a single tabbed line.</summary>
    Tb,
}

/// <summary>Whether and how a node participates in layout and rendering.</summary>
public enum XfaPresence
{
    /// <summary>The node is laid out and rendered normally.</summary>
    Visible,

    /// <summary>The node is not rendered but still occupies layout space.</summary>
    Invisible,

    /// <summary>The node is neither rendered nor allotted layout space.</summary>
    Hidden,

    /// <summary>The node is excluded from layout but kept in the form model.</summary>
    Inactive,
}

/// <summary>Horizontal alignment of content within a box.</summary>
public enum XfaHAlign
{
    /// <summary>Align to the left edge.</summary>
    Left,

    /// <summary>Center horizontally.</summary>
    Center,

    /// <summary>Align to the right edge.</summary>
    Right,

    /// <summary>Justify to both edges.</summary>
    Justify,

    /// <summary>Justify all lines including the last.</summary>
    JustifyAll,

    /// <summary>Align numbers on the radix point.</summary>
    Radix,
}

/// <summary>Vertical alignment of content within a box.</summary>
public enum XfaVAlign
{
    /// <summary>Align to the top edge.</summary>
    Top,

    /// <summary>Center vertically.</summary>
    Middle,

    /// <summary>Align to the bottom edge.</summary>
    Bottom,
}

/// <summary>Placement of a field caption relative to its value area.</summary>
public enum XfaCaptionPlacement
{
    /// <summary>Caption to the left of the value.</summary>
    Left,

    /// <summary>Caption to the right of the value.</summary>
    Right,

    /// <summary>Caption above the value.</summary>
    Top,

    /// <summary>Caption below the value.</summary>
    Bottom,

    /// <summary>Caption occupies the whole content area (inline).</summary>
    Inline,
}

/// <summary>The target of a forced layout break (breakBefore / breakAfter).</summary>
public enum XfaBreakTarget
{
    /// <summary>Let the layout engine choose the transition.</summary>
    Auto,

    /// <summary>Force a transition to the next content area.</summary>
    ContentArea,

    /// <summary>Force a transition to a new page.</summary>
    PageArea,
}

/// <summary>How a page set generates pages from its child page areas.</summary>
public enum XfaPageSetRelation
{
    /// <summary>
    /// Walk the child page areas in document order, honoring each one's
    /// occurrence counts; unbounded page areas repeat for overflow.
    /// </summary>
    OrderedOccurrence,

    /// <summary>Generate front/back page pairs for double-sided output.</summary>
    DuplexPaginated,

    /// <summary>Generate single-sided pages.</summary>
    SimplexPaginated,
}

/// <summary>The scope of a keep-together / keep-with constraint.</summary>
public enum XfaKeepScope
{
    /// <summary>No keep constraint.</summary>
    None,

    /// <summary>The constrained nodes must share one content area.</summary>
    ContentArea,

    /// <summary>The constrained nodes must share one page.</summary>
    PageArea,
}

/// <summary>Which page parity a page area may be used for (duplex pagination).</summary>
public enum XfaOddOrEven
{
    /// <summary>Usable for any page.</summary>
    Any,

    /// <summary>Usable only for odd (front / recto) pages.</summary>
    Odd,

    /// <summary>Usable only for even (back / verso) pages.</summary>
    Even,
}
