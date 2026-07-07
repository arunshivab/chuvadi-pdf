// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  XFA 3.3 — "Positioned" layout (layout="position").
// PHASE: LA-23b Phase B — positioned layout.

using System.Collections.Generic;
using System.Text;
using Chuvadi.Pdf.Xfa.Model;

namespace Chuvadi.Pdf.Xfa.Layout;

/// <summary>
/// Resolves an XFA model subtree into a flat list of positioned
/// <see cref="XfaBox"/>es in device space. Phase B handles
/// <see cref="XfaLayout.Position"/> containers: each child is placed by its
/// explicit x/y offset relative to the accumulated parent origin.
/// </summary>
public static class XfaLayoutEngine
{
    /// <summary>
    /// Lays out a model subtree starting at the given device-space origin.
    /// </summary>
    /// <param name="root">The root node to lay out (typically the body subform).</param>
    /// <param name="originX">The device-space x origin in points.</param>
    /// <param name="originY">The device-space y origin in points.</param>
    /// <returns>The positioned boxes in document order.</returns>
    public static IReadOnlyList<XfaBox> Layout(XfaNode root, double originX, double originY)
    {
        System.ArgumentNullException.ThrowIfNull(root);
        List<XfaBox> boxes = new List<XfaBox>();
        LayoutNode(root, originX, originY, boxes);
        return boxes;
    }

    private static void LayoutNode(XfaNode node, double parentX, double parentY, List<XfaBox> boxes)
    {
        if (node.Presence is XfaPresence.Hidden or XfaPresence.Inactive)
        {
            return;
        }

        double x = parentX + node.X.Points;
        double y = parentY + node.Y.Points;

        switch (node)
        {
            case XfaDraw draw:
                EmitLeafBox(draw, x, y, draw.Value, draw.Font, draw.HAlign, draw.VAlign, null, boxes);
                break;
            case XfaField field:
                LayoutField(field, x, y, boxes);
                break;
            case XfaSubform or XfaExclGroup or XfaArea
                or XfaPageArea or XfaContentArea or XfaPageSet:
                // Containers contribute their own border/fill box when sized,
                // then lay out children according to the container's layout mode.
                if (node.Border is not null && (node.Width.HasValue || node.Height.HasValue))
                {
                    boxes.Add(new XfaBox(x, y, WidthOf(node), HeightOf(node)) { Border = node.Border });
                }

                LayoutChildren(node, x, y, boxes);
                break;
            default:
                break;
        }
    }

    private static void LayoutChildren(XfaNode node, double x, double y, List<XfaBox> boxes)
    {
        XfaLayout layout = LayoutOf(node);
        (double insetLeft, double insetTop) = ContentInset(node);
        double contentX = x + insetLeft;
        double contentY = y + insetTop;

        switch (layout)
        {
            case XfaLayout.TopToBottom or XfaLayout.Tb:
                LayoutTopToBottom(node, contentX, contentY, boxes);
                break;
            case XfaLayout.LeftRightTopToBottom:
                LayoutLeftRightTopToBottom(node, contentX, contentY, boxes);
                break;
            case XfaLayout.Table:
                LayoutTable(node, contentX, contentY, boxes);
                break;
            case XfaLayout.Row:
                LayoutRow(node, contentX, contentY, (node as XfaSubform)?.ColumnWidths, boxes);
                break;
            default:
                // Positioned (and table/row, handled in a later phase): each child
                // is placed by its own x/y relative to this origin.
                foreach (XfaNode child in node.Children)
                {
                    LayoutNode(child, x, y, boxes);
                }

                break;
        }
    }

    // Top-to-bottom flow: stack children vertically, advancing the pen by each
    // child's height (plus its top/bottom margins). The child's own x is honored
    // as a horizontal offset; only the y position is governed by the flow pen.
    private static void LayoutTopToBottom(XfaNode node, double x, double y, List<XfaBox> boxes)
    {
        double penY = y;
        foreach (XfaNode child in node.Children)
        {
            if (child.Presence is XfaPresence.Hidden or XfaPresence.Inactive
                || IsNonFlowing(child))
            {
                continue;
            }

            (double marginTop, double marginBottom, _, _) = Margins(child);
            penY += marginTop;

            // x flows from the container origin + child x (LayoutNode adds child.X);
            // y comes from the flow pen, so cancel child.Y which LayoutNode re-adds.
            LayoutNode(child, x, penY - child.Y.Points, boxes);
            penY += HeightOf(child) + marginBottom;
        }
    }

    // Left-right then top-to-bottom flow: place children in a row until the
    // container width is exceeded, then wrap to the next row.
    private static void LayoutLeftRightTopToBottom(XfaNode node, double x, double y, List<XfaBox> boxes)
    {
        double maxWidth = WidthOf(node);
        double penX = x;
        double penY = y;
        double rowHeight = 0.0;

        foreach (XfaNode child in node.Children)
        {
            if (child.Presence is XfaPresence.Hidden or XfaPresence.Inactive
                || IsNonFlowing(child))
            {
                continue;
            }

            double childWidth = WidthOf(child);
            double childHeight = HeightOf(child);

            if (maxWidth > 0 && penX > x && (penX + childWidth) > (x + maxWidth))
            {
                penX = x;
                penY += rowHeight;
                rowHeight = 0.0;
            }

            LayoutNode(child, penX - child.X.Points, penY - child.Y.Points, boxes);
            penX += childWidth;
            rowHeight = System.Math.Max(rowHeight, childHeight);
        }
    }

    // Table layout: rows stack vertically; each row's cells are placed
    // left-to-right using the table's columnWidths (falling back to each
    // cell's own declared width when the table declares none).
    private static void LayoutTable(XfaNode node, double x, double y, List<XfaBox> boxes)
    {
        IReadOnlyList<XfaMeasurement>? widths = (node as XfaSubform)?.ColumnWidths;
        double penY = y;

        foreach (XfaNode row in node.Children)
        {
            if (row.Presence is XfaPresence.Hidden or XfaPresence.Inactive
                || IsNonFlowing(row))
            {
                continue;
            }

            (double marginTop, double marginBottom, _, _) = Margins(row);
            penY += marginTop;
            LayoutRow(row, x, penY, widths, boxes);
            penY += RowHeight(row) + marginBottom;
        }
    }

    // A single table row: cells advance left-to-right by column width.
    private static void LayoutRow(
        XfaNode row, double x, double y,
        IReadOnlyList<XfaMeasurement>? columnWidths, List<XfaBox> boxes)
    {
        double penX = x;
        int column = 0;

        foreach (XfaNode cell in row.Children)
        {
            if (cell.Presence is XfaPresence.Hidden or XfaPresence.Inactive
                || IsNonFlowing(cell))
            {
                continue;
            }

            double width = columnWidths is not null && column < columnWidths.Count
                ? columnWidths[column].Points
                : WidthOf(cell);

            LayoutNode(cell, penX - cell.X.Points, y - cell.Y.Points, boxes);
            penX += width;
            column++;
        }
    }

    private static double RowHeight(XfaNode row)
    {
        if (row.Height is { } declared)
        {
            return declared.Points;
        }

        double max = 0.0;
        foreach (XfaNode cell in row.Children)
        {
            max = System.Math.Max(max, HeightOf(cell));
        }

        return max;
    }

    private static XfaLayout LayoutOf(XfaNode node) => node switch
    {
        XfaSubform s => s.Layout,
        XfaExclGroup e => e.Layout,
        _ => XfaLayout.Position,
    };

    // Page-geometry nodes do not participate in their parent's content flow.
    private static bool IsNonFlowing(XfaNode node) =>
        node is XfaPageSet or XfaPageArea or XfaContentArea;

    private static (double Top, double Bottom, double Left, double Right) Margins(XfaNode node)
    {
        if (node.Margin is null)
        {
            return (0, 0, 0, 0);
        }

        return (node.Margin.Top.Points, node.Margin.Bottom.Points,
            node.Margin.Left.Points, node.Margin.Right.Points);
    }

    private static (double Left, double Top) ContentInset(XfaNode node)
    {
        if (node.Margin is null)
        {
            return (0, 0);
        }

        return (node.Margin.Left.Points, node.Margin.Top.Points);
    }

    private static void LayoutField(XfaField field, double x, double y, List<XfaBox> boxes)
    {
        double width = WidthOf(field);
        double height = HeightOf(field);

        // Field background / border box.
        XfaBox box = new XfaBox(x, y, width, height)
        {
            Border = field.Border,
            Widget = field.Ui?.Kind ?? XfaUiKind.Default,
        };

        if (field.Ui?.Kind == XfaUiKind.CheckButton)
        {
            box.WidgetChecked = IsCheckedValue(field.Value?.Text);
            box.WidgetRound = field.IsExclGroupMember;
        }
        else if (field.Ui?.Kind == XfaUiKind.PasswordEdit)
        {
            string? secret = ResolveText(field.Value);
            box.Text = secret is null ? null : new string('*', secret.Length);
            box.Font = field.Font;
            box.HAlign = field.HAlign;
            box.VAlign = field.VAlign;
        }
        else if (field.Ui?.Kind == XfaUiKind.ImageEdit)
        {
            // Assign via if/else: in a conditional expression the null branch
            // would convert through ReadOnlyMemory's implicit byte[] operator,
            // silently producing an empty (non-null) memory.
            byte[]? payload = DecodeImage(field.Value?.ImageBase64);
            if (payload is not null)
            {
                box.ImageBytes = new System.ReadOnlyMemory<byte>(payload);
            }
        }
        else
        {
            box.Text = ResolveText(field.Value);
            box.Font = field.Font;
            box.HAlign = field.HAlign;
            box.VAlign = field.VAlign;
        }

        boxes.Add(box);

        // Caption, placed in its reserved area beside or above the value.
        if (field.Caption is { } caption && caption.Text is { Length: > 0 })
        {
            (double cx, double cy, double cw, double ch) = CaptionRect(field, x, y, width, height);
            boxes.Add(new XfaBox(cx, cy, cw, ch)
            {
                Text = caption.Text,
                Font = caption.Font ?? field.Font,
                HAlign = XfaHAlign.Left,
                VAlign = XfaVAlign.Middle,
            });
        }
    }

    private static (double X, double Y, double W, double H) CaptionRect(
        XfaField field, double x, double y, double width, double height)
    {
        double reserve = field.Caption?.Reserve?.Points ?? 0.0;
        return field.Caption?.Placement switch
        {
            XfaCaptionPlacement.Top => (x, y, width, reserve > 0 ? reserve : height / 2.0),
            XfaCaptionPlacement.Bottom => (x, y + height - (reserve > 0 ? reserve : height / 2.0),
                width, reserve > 0 ? reserve : height / 2.0),
            XfaCaptionPlacement.Right => (x + width - (reserve > 0 ? reserve : width / 3.0), y,
                reserve > 0 ? reserve : width / 3.0, height),
            _ => (x, y, reserve > 0 ? reserve : width / 3.0, height),
        };
    }

    private static void EmitLeafBox(
        XfaNode node, double x, double y, XfaValue? value, XfaFont? font,
        XfaHAlign hAlign, XfaVAlign vAlign, XfaBorder? border, List<XfaBox> boxes)
    {
        boxes.Add(new XfaBox(x, y, WidthOf(node), HeightOf(node))
        {
            Text = ResolveText(value),
            Font = font,
            HAlign = hAlign,
            VAlign = vAlign,
            Border = border ?? node.Border,
        });
    }

    private static double WidthOf(XfaNode node) => node.Width?.Points ?? 0.0;

    private static double HeightOf(XfaNode node) => node.Height?.Points ?? 0.0;

    // Resolves the text to render for a value: plain text when present, else the
    // rich-text (XHTML) flattened to plain text.
    private static string? ResolveText(XfaValue? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value.Text is { Length: > 0 })
        {
            return value.Text;
        }

        if (value.RichText is { Length: > 0 } rich)
        {
            return FlattenHtml(rich);
        }

        return value.Text;
    }

    // Flattens an XHTML fragment to plain text by stripping tags and collapsing
    // whitespace. Block-level tags become single spaces so words do not run
    // together. This is a Phase B approximation; full rich-text layout is later.
    private static string FlattenHtml(string html)
    {
        StringBuilder builder = new StringBuilder(html.Length);
        bool inTag = false;
        bool lastWasSpace = false;

        foreach (char c in html)
        {
            if (c == '<')
            {
                inTag = true;
                if (!lastWasSpace)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }

                continue;
            }

            if (c == '>')
            {
                inTag = false;
                continue;
            }

            if (inTag)
            {
                continue;
            }

            if (c is ' ' or '\t' or '\r' or '\n')
            {
                if (!lastWasSpace)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }

                continue;
            }

            builder.Append(c);
            lastWasSpace = false;
        }

        return builder.ToString().Trim();
    }

    // Decodes a base64 image payload, returning null when the payload is
    // absent or malformed rather than failing the whole layout.
    private static byte[]? DecodeImage(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return null;
        }

        try
        {
            return System.Convert.FromBase64String(base64);
        }
        catch (System.FormatException)
        {
            return null;
        }
    }

    private static bool IsCheckedValue(string? text) =>
        text is "1" or "on" or "true" or "On" or "True" or "Yes" or "yes";
}
