// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  XFA 3.3 — template grammar.
// PHASE: LA-23b Phase A — template parser.

using System;
using System.Collections.Generic;
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
            case "bind" when parent is XfaField bindField:
                bindField.DataRef = NullIfEmpty(childElement.GetAttribute("ref"));
                return;
            case "occur" when parent is XfaPageArea occurArea:
                ApplyOccur(occurArea, childElement);
                return;
            case "breakBefore":
                parent.BreakBefore = ParseBreakTarget(childElement.GetAttribute("targetType"));
                return;
            case "breakAfter":
                parent.BreakAfter = ParseBreakTarget(childElement.GetAttribute("targetType"));
                return;
            case "break":
                ApplyLegacyBreak(parent, childElement);
                return;
            case "keep":
                ApplyKeep(parent, childElement);
                return;
            case "event":
                ParseEvent(parent, childElement);
                return;
            default:
                break;
        }

        // Structural children (subform/field/draw/exclGroup/area/pageSet/...)
        if (IsStructural(childElement.LocalName))
        {
            XfaNode child = ParseNode(childElement);
            if (parent is XfaExclGroup && child is XfaField radioMember)
            {
                radioMember.IsExclGroupMember = true;
            }

            parent.AddChild(child);
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
                subform.ColumnWidths = ParseColumnWidths(element.GetAttribute("columnWidths"));
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
            case XfaPageArea pageAreaNode:
                pageAreaNode.OddOrEven = element.GetAttribute("oddOrEven") switch
                {
                    "odd" => XfaOddOrEven.Odd,
                    "even" => XfaOddOrEven.Even,
                    _ => XfaOddOrEven.Any,
                };
                break;
            case XfaPageSet pageSet:
                pageSet.Relation = element.GetAttribute("relation") switch
                {
                    "duplexPaginated" => XfaPageSetRelation.DuplexPaginated,
                    "simplexPaginated" => XfaPageSetRelation.SimplexPaginated,
                    _ => XfaPageSetRelation.OrderedOccurrence,
                };
                break;
            default:
                break;
        }
    }

    // Parses <event activity="..."><script contentType="...">source</script>.
    // The script is attached to the owning node so the runner can fire it.
    private static void ParseEvent(XfaNode parent, XmlElement element)
    {
        XfaScriptEvent activity = ParseActivity(element.GetAttribute("activity"));

        foreach (XmlNode child in element.ChildNodes)
        {
            if (child is not XmlElement scriptElement
                || scriptElement.LocalName != "script")
            {
                continue;
            }

            XfaScriptLanguage language =
                ParseScriptLanguage(scriptElement.GetAttribute("contentType"));
            string source = scriptElement.InnerText ?? string.Empty;
            parent.AddScript(new XfaScript(language, activity, source));
        }
    }

    private static XfaScriptEvent ParseActivity(string activity) => activity switch
    {
        "initialize" => XfaScriptEvent.Initialize,
        "calculate" => XfaScriptEvent.Calculate,
        "validate" => XfaScriptEvent.Validate,
        "preSign" => XfaScriptEvent.PreSign,
        "postSign" => XfaScriptEvent.PostSign,
        _ => XfaScriptEvent.Interactive,
    };

    private static XfaScriptLanguage ParseScriptLanguage(string contentType)
    {
        // FormCalc is the XFA default when no contentType is specified.
        if (contentType.Length == 0)
        {
            return XfaScriptLanguage.FormCalc;
        }

        return contentType.Contains("javascript", StringComparison.OrdinalIgnoreCase)
            ? XfaScriptLanguage.JavaScript
            : XfaScriptLanguage.FormCalc;
    }

    private static void ApplyKeep(XfaNode parent, XmlElement element)
    {
        parent.KeepIntact = ParseKeepScope(element.GetAttribute("intact"));
        parent.KeepPrevious = ParseKeepScope(element.GetAttribute("previous"));
        parent.KeepNext = ParseKeepScope(element.GetAttribute("next"));
    }

    private static XfaKeepScope ParseKeepScope(string value) => value switch
    {
        "contentArea" => XfaKeepScope.ContentArea,
        "pageArea" => XfaKeepScope.PageArea,
        _ => XfaKeepScope.None,
    };

    private static List<XfaMeasurement>? ParseColumnWidths(string value)
    {
        if (value.Length == 0)
        {
            return null;
        }

        List<XfaMeasurement> widths = new List<XfaMeasurement>();
        foreach (string token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            widths.Add(XfaMeasurement.Parse(token));
        }

        return widths.Count == 0 ? null : widths;
    }

    private static void ApplyOccur(XfaPageArea pageArea, XmlElement element)
    {
        string min = element.GetAttribute("min");
        string max = element.GetAttribute("max");
        if (min.Length > 0 && int.TryParse(min, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out int minValue))
        {
            pageArea.MinOccur = minValue;
        }

        if (max.Length > 0 && int.TryParse(max, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out int maxValue))
        {
            pageArea.MaxOccur = maxValue;
        }
    }

    private static XfaBreakTarget ParseBreakTarget(string targetType) => targetType switch
    {
        "pageArea" => XfaBreakTarget.PageArea,
        "contentArea" => XfaBreakTarget.ContentArea,
        _ => XfaBreakTarget.Auto,
    };

    // The legacy <break> element combines before/after on one element:
    // <break before="pageArea" after="contentArea"/>.
    private static void ApplyLegacyBreak(XfaNode parent, XmlElement element)
    {
        string before = element.GetAttribute("before");
        if (before.Length > 0 && before != "auto")
        {
            parent.BreakBefore = ParseBreakTarget(before);
        }

        string after = element.GetAttribute("after");
        if (after.Length > 0 && after != "auto")
        {
            parent.BreakAfter = ParseBreakTarget(after);
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
            return value;
        }

        XmlElement? image = FindFirstByLocalName(element, "image");
        if (image is not null)
        {
            value.ImageBase64 = image.InnerText.Trim();
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
