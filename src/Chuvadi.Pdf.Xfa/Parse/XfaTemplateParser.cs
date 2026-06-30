// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  XFA 3.3 — template grammar.
// PHASE: LA-23b Phase A — template parser.

using System;
using System.IO;
using System.Xml;
using Chuvadi.Pdf.Xfa.Model;

namespace Chuvadi.Pdf.Xfa.Parse;

/// <summary>
/// Parses the XFA <c>template</c> packet XML into a typed
/// <see cref="XfaNode"/> tree. Unknown elements are skipped (their children are
/// still visited), so the parser degrades gracefully on templates that use
/// features beyond the current model.
/// </summary>
public static class XfaTemplateParser
{
    /// <summary>
    /// Parses template XML bytes into the root subform of the model tree.
    /// </summary>
    /// <param name="templateXml">The raw template packet bytes (UTF-8).</param>
    /// <returns>The root <see cref="XfaSubform"/>, or null when no subform is found.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="templateXml"/> is null.</exception>
    public static XfaSubform? Parse(byte[] templateXml)
    {
        ArgumentNullException.ThrowIfNull(templateXml);

        XmlReaderSettings settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
        };

        using MemoryStream stream = new MemoryStream(templateXml);
        using XmlReader reader = XmlReader.Create(stream, settings);

        XmlDocument document = new XmlDocument { XmlResolver = null };
        document.Load(reader);

        XmlElement? documentElement = document.DocumentElement;

        // The template packet's document element is usually <template> itself,
        // but some producers wrap it (e.g. <xdp><template>...). Handle both.
        XmlElement? templateElement =
            documentElement is { LocalName: "template" }
                ? documentElement
                : FindFirstByLocalName(documentElement, "template");

        XmlElement? rootSubform = templateElement is null
            ? null
            : FindFirstByLocalName(templateElement, "subform");

        if (rootSubform is null)
        {
            return null;
        }

        return (XfaSubform)ParseNode(rootSubform);
    }

    private static XfaNode ParseNode(XmlElement element)
    {
        XfaNode node = CreateNode(element.LocalName);
        ApplyCommonAttributes(node, element);
        ApplyTypeSpecific(node, element);

        foreach (XmlNode childNode in element.ChildNodes)
        {
            if (childNode is not XmlElement childElement)
            {
                continue;
            }

            AttachChild(node, childElement);
        }

        return node;
    }

    private static void AttachChild(XfaNode parent, XmlElement childElement)
    {
        switch (childElement.LocalName)
        {
            case "margin":
                parent.Margin = ParseMargin(childElement);
                return;
            case "border":
                parent.Border = ParseBorder(childElement);
                return;
            case "caption" when parent is XfaField field:
                field.Caption = ParseCaption(childElement);
                return;
            case "value" when parent is XfaField valueField:
                valueField.Value = ParseValue(childElement);
                return;
            case "value" when parent is XfaDraw draw:
                draw.Value = ParseValue(childElement);
                return;
            case "ui" when parent is XfaField uiField:
                uiField.Ui = ParseUi(childElement);
                return;
            case "font" when parent is XfaField fontField:
                fontField.Font = ParseFont(childElement);
                return;
            case "font" when parent is XfaDraw fontDraw:
                fontDraw.Font = ParseFont(childElement);
                return;
            case "medium" when parent is XfaPageArea pageArea:
                ApplyMedium(pageArea, childElement);
                return;
            default:
                break;
        }

        // Structural children (subform/field/draw/exclGroup/area/pageSet/...)
        if (IsStructural(childElement.LocalName))
        {
            parent.AddChild(ParseNode(childElement));
        }
    }

    private static bool IsStructural(string localName) => localName switch
    {
        "subform" => true,
        "field" => true,
        "draw" => true,
        "exclGroup" => true,
        "area" => true,
        "pageSet" => true,
        "pageArea" => true,
        "contentArea" => true,
        _ => false,
    };

    private static XfaNode CreateNode(string localName) => localName switch
    {
        "subform" => new XfaSubform(),
        "field" => new XfaField(),
        "draw" => new XfaDraw(),
        "exclGroup" => new XfaExclGroup(),
        "area" => new XfaArea(),
        "pageSet" => new XfaPageSet(),
        "pageArea" => new XfaPageArea(),
        "contentArea" => new XfaContentArea(),
        _ => new XfaArea(),
    };

    private static void ApplyCommonAttributes(XfaNode node, XmlElement element)
    {
        node.Name = element.GetAttribute("name") is { Length: > 0 } name ? name : null;
        node.X = XfaMeasurement.Parse(NullIfEmpty(element.GetAttribute("x")));
        node.Y = XfaMeasurement.Parse(NullIfEmpty(element.GetAttribute("y")));

        string w = element.GetAttribute("w");
        if (w.Length > 0)
        {
            node.Width = XfaMeasurement.Parse(w);
        }

        string h = element.GetAttribute("h");
        if (h.Length > 0)
        {
            node.Height = XfaMeasurement.Parse(h);
        }

        string presence = element.GetAttribute("presence");
        node.Presence = presence switch
        {
            "invisible" => XfaPresence.Invisible,
            "hidden" => XfaPresence.Hidden,
            "inactive" => XfaPresence.Inactive,
            _ => XfaPresence.Visible,
        };
    }

    private static void ApplyTypeSpecific(XfaNode node, XmlElement element)
    {
        switch (node)
        {
            case XfaSubform subform:
                subform.Layout = ParseLayout(element.GetAttribute("layout"));
                break;
            case XfaExclGroup exclGroup:
                exclGroup.Layout = ParseLayout(element.GetAttribute("layout"));
                break;
            case XfaField field:
                field.HAlign = ParseHAlign(element.GetAttribute("hAlign"));
                field.VAlign = ParseVAlign(element.GetAttribute("vAlign"));
                break;
            case XfaDraw draw:
                draw.HAlign = ParseHAlign(element.GetAttribute("hAlign"));
                draw.VAlign = ParseVAlign(element.GetAttribute("vAlign"));
                break;
            default:
                break;
        }
    }

    private static XfaLayout ParseLayout(string value) => value switch
    {
        "tb" => XfaLayout.TopToBottom,
        "lr-tb" => XfaLayout.LeftRightTopToBottom,
        "position" => XfaLayout.Position,
        "table" => XfaLayout.Table,
        "row" => XfaLayout.Row,
        _ => XfaLayout.Position,
    };

    private static XfaHAlign ParseHAlign(string value) => value switch
    {
        "center" => XfaHAlign.Center,
        "right" => XfaHAlign.Right,
        "justify" => XfaHAlign.Justify,
        "justifyAll" => XfaHAlign.JustifyAll,
        "radix" => XfaHAlign.Radix,
        _ => XfaHAlign.Left,
    };

    private static XfaVAlign ParseVAlign(string value) => value switch
    {
        "middle" => XfaVAlign.Middle,
        "bottom" => XfaVAlign.Bottom,
        _ => XfaVAlign.Top,
    };

    private static void ApplyMedium(XfaPageArea pageArea, XmlElement element)
    {
        string longEdge = element.GetAttribute("long");
        string shortEdge = element.GetAttribute("short");
        if (longEdge.Length > 0)
        {
            pageArea.MediumLong = XfaMeasurement.Parse(longEdge);
        }

        if (shortEdge.Length > 0)
        {
            pageArea.MediumShort = XfaMeasurement.Parse(shortEdge);
        }

        pageArea.Landscape = element.GetAttribute("orientation") == "landscape";
    }

    private static XfaMargin ParseMargin(XmlElement element)
    {
        return new XfaMargin
        {
            Left = XfaMeasurement.Parse(NullIfEmpty(element.GetAttribute("leftInset"))),
            Right = XfaMeasurement.Parse(NullIfEmpty(element.GetAttribute("rightInset"))),
            Top = XfaMeasurement.Parse(NullIfEmpty(element.GetAttribute("topInset"))),
            Bottom = XfaMeasurement.Parse(NullIfEmpty(element.GetAttribute("bottomInset"))),
        };
    }

    private static XfaBorder ParseBorder(XmlElement element)
    {
        XfaBorder border = new XfaBorder();
        XmlElement? edge = FindFirstByLocalName(element, "edge");
        if (edge is not null)
        {
            border.HasEdge = edge.GetAttribute("presence") != "hidden";
            border.EdgeThickness = XfaMeasurement.Parse(NullIfEmpty(edge.GetAttribute("thickness")));
            XmlElement? edgeColor = FindFirstByLocalName(edge, "color");
            if (edgeColor is not null)
            {
                border.EdgeColor = NullIfEmpty(edgeColor.GetAttribute("value"));
            }
        }

        XmlElement? fill = FindFirstByLocalName(element, "fill");
        if (fill is not null)
        {
            XmlElement? fillColor = FindFirstByLocalName(fill, "color");
            if (fillColor is not null)
            {
                border.FillColor = NullIfEmpty(fillColor.GetAttribute("value"));
            }
        }

        return border;
    }

    private static XfaCaption ParseCaption(XmlElement element)
    {
        XfaCaption caption = new XfaCaption
        {
            Placement = element.GetAttribute("placement") switch
            {
                "right" => XfaCaptionPlacement.Right,
                "top" => XfaCaptionPlacement.Top,
                "bottom" => XfaCaptionPlacement.Bottom,
                "inline" => XfaCaptionPlacement.Inline,
                _ => XfaCaptionPlacement.Left,
            },
        };

        string reserve = element.GetAttribute("reserve");
        if (reserve.Length > 0)
        {
            caption.Reserve = XfaMeasurement.Parse(reserve);
        }

        XmlElement? value = FindFirstByLocalName(element, "value");
        XmlElement? text = value is null ? null : FindFirstByLocalName(value, "text");
        caption.Text = text?.InnerText;

        XmlElement? font = FindFirstByLocalName(element, "font");
        if (font is not null)
        {
            caption.Font = ParseFont(font);
        }

        return caption;
    }

    private static XfaValue ParseValue(XmlElement element)
    {
        XfaValue value = new XfaValue();
        XmlElement? text = FindFirstByLocalName(element, "text");
        if (text is not null)
        {
            value.Text = text.InnerText;
            return value;
        }

        XmlElement? exData = FindFirstByLocalName(element, "exData");
        if (exData is not null)
        {
            value.RichText = exData.InnerXml;
        }

        return value;
    }

    private static XfaUi ParseUi(XmlElement element)
    {
        XfaUi ui = new XfaUi();
        foreach (XmlNode child in element.ChildNodes)
        {
            if (child is not XmlElement childElement)
            {
                continue;
            }

            ui.Kind = childElement.LocalName switch
            {
                "textEdit" => XfaUiKind.TextEdit,
                "checkButton" => XfaUiKind.CheckButton,
                "choiceList" => XfaUiKind.ChoiceList,
                "dateTimeEdit" => XfaUiKind.DateTimeEdit,
                "numericEdit" => XfaUiKind.NumericEdit,
                "passwordEdit" => XfaUiKind.PasswordEdit,
                "imageEdit" => XfaUiKind.ImageEdit,
                "signature" => XfaUiKind.Signature,
                "barcode" => XfaUiKind.Barcode,
                _ => XfaUiKind.Default,
            };

            if (ui.Kind != XfaUiKind.Default)
            {
                break;
            }
        }

        return ui;
    }

    private static XfaFont ParseFont(XmlElement element)
    {
        XfaFont font = new XfaFont
        {
            Typeface = NullIfEmpty(element.GetAttribute("typeface")),
            Bold = element.GetAttribute("weight") == "bold",
            Italic = element.GetAttribute("posture") == "italic",
        };

        string size = element.GetAttribute("size");
        if (size.Length > 0)
        {
            font.Size = XfaMeasurement.Parse(size).Points;
        }

        XmlElement? fill = FindFirstByLocalName(element, "fill");
        XmlElement? color = fill is null ? null : FindFirstByLocalName(fill, "color");
        if (color is not null)
        {
            font.Color = NullIfEmpty(color.GetAttribute("value"));
        }

        return font;
    }

    private static XmlElement? FindFirstByLocalName(XmlNode? parent, string localName)
    {
        if (parent is null)
        {
            return null;
        }

        foreach (XmlNode child in parent.ChildNodes)
        {
            if (child is XmlElement element && element.LocalName == localName)
            {
                return element;
            }
        }

        return null;
    }

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}
