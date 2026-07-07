// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  XFA 3.3 — template element hierarchy.
// PHASE: LA-23b Phase A — template model.

using System.Collections.Generic;

namespace Chuvadi.Pdf.Xfa.Model;

/// <summary>
/// Base type for every parsed XFA template node. Carries the element name, the
/// optional template <c>name</c> attribute, the child nodes, and the geometry
/// and presence properties common to layout containers and leaves.
/// </summary>
public abstract class XfaNode
{
    private readonly List<XfaNode> _children = new List<XfaNode>();
    private readonly List<XfaScript> _scripts = new List<XfaScript>();

    /// <summary>Gets the XFA element name (for example "subform", "field").</summary>
    public abstract string ElementName { get; }

    /// <summary>Gets or sets the template <c>name</c> attribute, if present.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the explicit x offset within the parent container.</summary>
    public XfaMeasurement X { get; set; }

    /// <summary>Gets or sets the explicit y offset within the parent container.</summary>
    public XfaMeasurement Y { get; set; }

    /// <summary>Gets or sets the declared width, when specified.</summary>
    public XfaMeasurement? Width { get; set; }

    /// <summary>Gets or sets the declared height, when specified.</summary>
    public XfaMeasurement? Height { get; set; }

    /// <summary>Gets or sets the presence (visibility / layout participation).</summary>
    public XfaPresence Presence { get; set; } = XfaPresence.Visible;

    /// <summary>Gets or sets the margin box, when specified.</summary>
    public XfaMargin? Margin { get; set; }

    /// <summary>Gets or sets the border, when specified.</summary>
    public XfaBorder? Border { get; set; }

    /// <summary>
    /// Gets or sets a forced layout transition before this node lays out
    /// (from <c>&lt;breakBefore&gt;</c> or the legacy <c>&lt;break before&gt;</c>).
    /// Null when no break is requested.
    /// </summary>
    public XfaBreakTarget? BreakBefore { get; set; }

    /// <summary>
    /// Gets or sets a forced layout transition after this node lays out
    /// (from <c>&lt;breakAfter&gt;</c> or the legacy <c>&lt;break after&gt;</c>).
    /// Null when no break is requested.
    /// </summary>
    public XfaBreakTarget? BreakAfter { get; set; }

    /// <summary>Gets or sets the keep-intact constraint (the node must not split).</summary>
    public XfaKeepScope KeepIntact { get; set; }

    /// <summary>Gets or sets the keep-with-previous constraint scope.</summary>
    public XfaKeepScope KeepPrevious { get; set; }

    /// <summary>Gets or sets the keep-with-next constraint scope.</summary>
    public XfaKeepScope KeepNext { get; set; }

    /// <summary>Gets the scripts attached to this node via its events.</summary>
    public IReadOnlyList<XfaScript> Scripts => _scripts;

    /// <summary>Appends a script to this node.</summary>
    /// <param name="script">The script to append.</param>
    public void AddScript(XfaScript script) => _scripts.Add(script);

    /// <summary>Gets the child nodes in document order.</summary>
    public IReadOnlyList<XfaNode> Children => _children;

    /// <summary>Appends a child node.</summary>
    /// <param name="child">The child to append.</param>
    public void AddChild(XfaNode child) => _children.Add(child);
}

/// <summary>A container of fields and nested subforms; the primary layout unit.</summary>
public sealed class XfaSubform : XfaNode
{
    /// <inheritdoc />
    public override string ElementName => "subform";

    /// <summary>Gets or sets the layout strategy applied to children.</summary>
    public XfaLayout Layout { get; set; } = XfaLayout.Position;

    /// <summary>
    /// Gets or sets the table column widths (from <c>columnWidths</c>), used
    /// when <see cref="Layout"/> is <see cref="XfaLayout.Table"/>. Null when
    /// the subform declares no column widths.
    /// </summary>
    public IReadOnlyList<XfaMeasurement>? ColumnWidths { get; set; }
}

/// <summary>A mutually-exclusive group of fields (for example radio buttons).</summary>
public sealed class XfaExclGroup : XfaNode
{
    /// <inheritdoc />
    public override string ElementName => "exclGroup";

    /// <summary>Gets or sets the layout strategy applied to children.</summary>
    public XfaLayout Layout { get; set; } = XfaLayout.Position;
}

/// <summary>An interactive field with an optional caption, value, and UI widget.</summary>
public sealed class XfaField : XfaNode
{
    /// <inheritdoc />
    public override string ElementName => "field";

    /// <summary>Gets or sets the field caption, when present.</summary>
    public XfaCaption? Caption { get; set; }

    /// <summary>Gets or sets the field value, when present.</summary>
    public XfaValue? Value { get; set; }

    /// <summary>Gets or sets the field UI widget descriptor, when present.</summary>
    public XfaUi? Ui { get; set; }

    /// <summary>Gets or sets the font applied to the field value text.</summary>
    public XfaFont? Font { get; set; }

    /// <summary>Gets or sets the horizontal alignment of value content.</summary>
    public XfaHAlign HAlign { get; set; } = XfaHAlign.Left;

    /// <summary>Gets or sets the vertical alignment of value content.</summary>
    public XfaVAlign VAlign { get; set; } = XfaVAlign.Top;

    /// <summary>
    /// Gets or sets the datasets bind reference (the SOM expression from
    /// <c>&lt;bind ref="..."&gt;</c>), used to merge a value from the datasets
    /// packet. Null when the field has no data binding.
    /// </summary>
    public string? DataRef { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this field is a direct member of
    /// an <c>exclGroup</c> (its check button renders as a radio button).
    /// </summary>
    public bool IsExclGroupMember { get; set; }
}

/// <summary>Static, non-interactive content such as boilerplate text or lines.</summary>
public sealed class XfaDraw : XfaNode
{
    /// <inheritdoc />
    public override string ElementName => "draw";

    /// <summary>Gets or sets the static value (text content), when present.</summary>
    public XfaValue? Value { get; set; }

    /// <summary>Gets or sets the font applied to the drawn text.</summary>
    public XfaFont? Font { get; set; }

    /// <summary>Gets or sets the horizontal alignment of content.</summary>
    public XfaHAlign HAlign { get; set; } = XfaHAlign.Left;

    /// <summary>Gets or sets the vertical alignment of content.</summary>
    public XfaVAlign VAlign { get; set; } = XfaVAlign.Top;
}

/// <summary>A page-geometry container holding one or more page areas.</summary>
public sealed class XfaPageSet : XfaNode
{
    /// <inheritdoc />
    public override string ElementName => "pageSet";

    /// <summary>Gets or sets how this page set generates pages from its page areas.</summary>
    public XfaPageSetRelation Relation { get; set; } = XfaPageSetRelation.OrderedOccurrence;
}

/// <summary>A single page area, defining its size and content region.</summary>
public sealed class XfaPageArea : XfaNode
{
    /// <inheritdoc />
    public override string ElementName => "pageArea";

    /// <summary>Gets or sets the long edge of the page medium, when specified.</summary>
    public XfaMeasurement? MediumLong { get; set; }

    /// <summary>Gets or sets the short edge of the page medium, when specified.</summary>
    public XfaMeasurement? MediumShort { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the medium is oriented landscape
    /// (long edge horizontal).
    /// </summary>
    public bool Landscape { get; set; }

    /// <summary>Gets or sets the minimum occurrence count (from <c>&lt;occur min&gt;</c>).</summary>
    public int MinOccur { get; set; } = 1;

    /// <summary>
    /// Gets or sets the maximum occurrence count (from <c>&lt;occur max&gt;</c>).
    /// -1 means unbounded: the page area repeats for as long as overflow demands.
    /// </summary>
    public int MaxOccur { get; set; } = 1;

    /// <summary>Gets or sets which page parity this area may serve (duplex pagination).</summary>
    public XfaOddOrEven OddOrEven { get; set; } = XfaOddOrEven.Any;
}

/// <summary>The drawable region within a page area where content flows.</summary>
public sealed class XfaContentArea : XfaNode
{
    /// <inheritdoc />
    public override string ElementName => "contentArea";
}

/// <summary>A generic container element used for grouped positioning.</summary>
public sealed class XfaArea : XfaNode
{
    /// <inheritdoc />
    public override string ElementName => "area";
}

/// <summary>The value of a field or draw, carrying its resolved text content.</summary>
public sealed class XfaValue : XfaNode
{
    /// <inheritdoc />
    public override string ElementName => "value";

    /// <summary>Gets or sets the plain-text content of the value, when present.</summary>
    public string? Text { get; set; }

    /// <summary>
    /// Gets or sets the rich-text (XHTML) content, when the value uses
    /// <c>exData</c> with an HTML content type. Null for plain values.
    /// </summary>
    public string? RichText { get; set; }

    /// <summary>
    /// Gets or sets the base64-encoded image payload (from an <c>&lt;image&gt;</c>
    /// value), used by image fields. Null when the value carries no image.
    /// </summary>
    public string? ImageBase64 { get; set; }
}

/// <summary>A field caption: its text and placement relative to the value.</summary>
public sealed class XfaCaption : XfaNode
{
    /// <inheritdoc />
    public override string ElementName => "caption";

    /// <summary>Gets or sets the caption text.</summary>
    public string? Text { get; set; }

    /// <summary>Gets or sets the caption placement relative to the value area.</summary>
    public XfaCaptionPlacement Placement { get; set; } = XfaCaptionPlacement.Left;

    /// <summary>Gets or sets the reserved size of the caption area, when specified.</summary>
    public XfaMeasurement? Reserve { get; set; }

    /// <summary>Gets or sets the font applied to the caption text.</summary>
    public XfaFont? Font { get; set; }
}

/// <summary>The kind of widget a field uses to present its value.</summary>
public enum XfaUiKind
{
    /// <summary>A free-text edit box.</summary>
    TextEdit,

    /// <summary>A check box.</summary>
    CheckButton,

    /// <summary>A drop-down or list selection.</summary>
    ChoiceList,

    /// <summary>A date / time edit.</summary>
    DateTimeEdit,

    /// <summary>A numeric edit.</summary>
    NumericEdit,

    /// <summary>A masked password edit.</summary>
    PasswordEdit,

    /// <summary>An image field.</summary>
    ImageEdit,

    /// <summary>A signature field.</summary>
    Signature,

    /// <summary>A barcode field.</summary>
    Barcode,

    /// <summary>An unrecognized or default UI; rendered as plain text.</summary>
    Default,
}

/// <summary>Describes the UI widget a field uses to present its value.</summary>
public sealed class XfaUi : XfaNode
{
    /// <inheritdoc />
    public override string ElementName => "ui";

    /// <summary>Gets or sets the widget kind.</summary>
    public XfaUiKind Kind { get; set; } = XfaUiKind.Default;
}

/// <summary>A font descriptor applied to caption, value, or draw text.</summary>
public sealed class XfaFont : XfaNode
{
    /// <inheritdoc />
    public override string ElementName => "font";

    /// <summary>Gets or sets the typeface name.</summary>
    public string? Typeface { get; set; }

    /// <summary>Gets or sets the font size in points.</summary>
    public double Size { get; set; } = 10.0;

    /// <summary>Gets or sets a value indicating whether the font is bold.</summary>
    public bool Bold { get; set; }

    /// <summary>Gets or sets a value indicating whether the font is italic.</summary>
    public bool Italic { get; set; }

    /// <summary>Gets or sets the text colour as an "r,g,b" triple (0-255), when specified.</summary>
    public string? Color { get; set; }
}

/// <summary>The four-sided margin (inset) box of a node.</summary>
public sealed class XfaMargin : XfaNode
{
    /// <inheritdoc />
    public override string ElementName => "margin";

    /// <summary>Gets or sets the left inset.</summary>
    public XfaMeasurement Left { get; set; }

    /// <summary>Gets or sets the right inset.</summary>
    public XfaMeasurement Right { get; set; }

    /// <summary>Gets or sets the top inset.</summary>
    public XfaMeasurement Top { get; set; }

    /// <summary>Gets or sets the bottom inset.</summary>
    public XfaMeasurement Bottom { get; set; }
}

/// <summary>A node border: edge stroke and optional fill.</summary>
public sealed class XfaBorder : XfaNode
{
    /// <inheritdoc />
    public override string ElementName => "border";

    /// <summary>Gets or sets the stroke width of the border edges.</summary>
    public XfaMeasurement EdgeThickness { get; set; }

    /// <summary>Gets or sets the edge colour as an "r,g,b" triple (0-255), when specified.</summary>
    public string? EdgeColor { get; set; }

    /// <summary>Gets or sets the fill colour as an "r,g,b" triple (0-255), when specified.</summary>
    public string? FillColor { get; set; }

    /// <summary>Gets or sets a value indicating whether the border edges are visible.</summary>
    public bool HasEdge { get; set; }
}
