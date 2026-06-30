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
                // then lay out children relative to this origin.
                if (node.Border is not null && (node.Width.HasValue || node.Height.HasValue))
                {
                    boxes.Add(new XfaBox(x, y, WidthOf(node), HeightOf(node)) { Border = node.Border });
                }

                foreach (XfaNode child in node.Children)
                {
                    LayoutNode(child, x, y, boxes);
                }

                break;
            default:
                break;
        }
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

    private static bool IsCheckedValue(string? text) =>
        text is "1" or "on" or "true" or "On" or "True" or "Yes" or "yes";
}
