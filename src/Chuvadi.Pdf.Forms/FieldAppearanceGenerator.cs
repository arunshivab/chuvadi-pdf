// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.31 — field appearances made from the field's value (D-108 item c,
//        D-146): for forms whose /NeedAppearances is true and widgets with no
//        /AP, as viewers do. In memory only; the file is never changed.
// SPEC:  PDF 32000-1:2008 §12.7.3.3 (variable text), §12.7.4 (field types),
//        §12.5.6.19 (widget annotations, /MK).

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Forms.Appearance;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Forms;

/// <summary>
/// Makes a widget's normal appearance stream from the field's value and its
/// <c>/DA</c>, <c>/Q</c>, <c>/MK</c> and flags: text fields (single line,
/// multiline, comb, password), check boxes and radio buttons, combo and list
/// boxes, and push-button captions. Signature fields are never generated.
/// </summary>
public static class FieldAppearanceGenerator
{
    private const int FlagMultiline = 1 << 12;
    private const int FlagPassword = 1 << 13;
    private const int FlagRadio = 1 << 15;
    private const int FlagPushButton = 1 << 16;
    private const int FlagCombo = 1 << 17;
    private const int FlagComb = 1 << 24;

    /// <summary>
    /// Returns a generated normal appearance for <paramref name="widget"/>, or
    /// null when the field is a signature, has no drawable size, or its type is
    /// unknown. Characters the <c>/DA</c> font cannot show are drawn with the
    /// LiPi Sans face of their script and reported as
    /// <see cref="PdfWarningKind.FontSubstituted"/>.
    /// </summary>
    /// <param name="widget">The widget annotation (merged with its field, or a child of it).</param>
    /// <param name="objects">The document's objects.</param>
    /// <param name="acroForm">The document's <c>/AcroForm</c> dictionary, for the default <c>/DA</c> and <c>/DR</c>; may be null.</param>
    /// <param name="onWarning">Receives warnings; may be null.</param>
    /// <param name="pageNumber">The 1-based page number for warnings, or null.</param>
    /// <returns>A form XObject the size of the widget's <c>/Rect</c>, or null.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="widget"/> or <paramref name="objects"/> is null.</exception>
    public static PdfStream? Generate(PdfDictionary widget, PdfObjectStore objects, PdfDictionary? acroForm, Action<PdfWarning>? onWarning, int? pageNumber)
    {
        ArgumentNullException.ThrowIfNull(widget);
        ArgumentNullException.ThrowIfNull(objects);

        Field field = new Field(widget, objects);
        string? type = field.Name("FT");
        if (type is null or "Sig")
        {
            return null;
        }

        if (!TryRect(widget, objects, out double width, out double height) || width <= 0 || height <= 0)
        {
            return null;
        }

        int rotation = 0;
        PdfDictionary? mk = objects.ResolveAs<PdfDictionary>(Get(widget, "MK"));
        if (mk is not null)
        {
            rotation = (((int)Num(objects, Get(mk, "R"), 0) % 360) + 360) % 360;
        }

        // A rotated widget draws in a box with swapped sides, turned by /Matrix.
        (double w, double h) = rotation is 90 or 270 ? (height, width) : (width, height);
        PdfArray? matrix = rotation switch
        {
            90 => Matrix(0, 1, -1, 0, height, 0),
            180 => Matrix(-1, 0, 0, -1, width, height),
            270 => Matrix(0, -1, 1, 0, 0, width),
            _ => null,
        };

        PdfDictionary resources = new PdfDictionary();
        AppearanceContent content = new AppearanceContent();
        double border = BorderWidth(widget, objects);
        PdfColorSpec background = mk is null ? default : PdfColorSpec.FromArray(objects.ResolveAs<PdfArray>(Get(mk, "BG")), objects);
        PdfColorSpec borderColor = mk is null ? default : PdfColorSpec.FromArray(objects.ResolveAs<PdfArray>(Get(mk, "BC")), objects);
        if (background.Components is { Length: > 0 })
        {
            content.Op("q").Fill(background).Rect(0, 0, w, h).Op("f").Op("Q");
        }

        if (borderColor.Components is { Length: > 0 } && border > 0)
        {
            content.Op("q").Stroke(borderColor).Op("w", border).Rect(border / 2, border / 2, w - border, h - border).Op("S").Op("Q");
        }

        bool drawsFrame = background.Components is { Length: > 0 } || (borderColor.Components is { Length: > 0 } && border > 0);
        int flags = (int)field.Number("Ff", 0);
        PdfDictionary? drFonts = DrFonts(widget, acroForm, objects);
        string da = field.String("DA") ?? StringOf(objects, Get(acroForm, "DA")) ?? "/Helv 0 Tf 0 g";
        (string fontName, double fontSize, PdfColorSpec color) = ParseDa(da);
        AppearanceText? text = null;

        if (type == "Tx")
        {
            string value = field.TextValue("V") ?? string.Empty;
            if (value.Length == 0 && !drawsFrame)
            {
                // Nothing to draw: no appearance rather than an empty one.
                return null;
            }

            if ((flags & FlagPassword) != 0)
            {
                value = new string('*', value.Length);
            }

            int maxLen = (int)field.Number("MaxLen", 0);
            int q = (int)field.Number("Q", 0);
            text = Text(objects, resources, drFonts, fontName, fontSize, value, w, h, border, (flags & FlagMultiline) != 0);
            content.Raw("/Tx BMC\nq\n").Rect(border, border, w - (2 * border), h - (2 * border)).Op("W").Op("n");
            if ((flags & FlagComb) != 0 && maxLen > 0)
            {
                Comb(content, text, color, value, maxLen, w, h);
            }
            else if ((flags & FlagMultiline) != 0)
            {
                Multiline(content, text, color, value, q, w, h, border);
            }
            else
            {
                SingleLine(content, text, color, value, q, w, h, border);
            }

            content.Raw("Q\nEMC\n");
        }
        else if (type == "Ch")
        {
            List<(string Export, string Display)> options = Options(field, objects);
            List<string> selected = field.Values("V");
            if (selected.Count == 0 && !drawsFrame && (flags & FlagCombo) != 0)
            {
                return null;
            }
            content.Raw("/Tx BMC\nq\n").Rect(border, border, w - (2 * border), h - (2 * border)).Op("W").Op("n");
            if ((flags & FlagCombo) != 0)
            {
                string shown = selected.Count > 0 ? Display(options, selected[0]) : string.Empty;
                text = Text(objects, resources, drFonts, fontName, fontSize, shown, w, h, border, false);
                SingleLine(content, text, color, shown, (int)field.Number("Q", 0), w, h, border);
            }
            else
            {
                double size = fontSize > 0 ? fontSize : 12;
                text = new AppearanceText(objects, resources, drFonts, fontName, size);
                ListBox(content, text, color, options, selected, (int)field.Number("TI", 0), w, h, border);
            }

            content.Raw("Q\nEMC\n");
        }
        else if (type == "Btn")
        {
            if ((flags & FlagPushButton) != 0)
            {
                string caption = mk is null ? string.Empty : TextOf(objects, Get(mk, "CA")) ?? string.Empty;
                text = Text(objects, resources, drFonts, fontName, fontSize, caption, w, h, border, false);
                SingleLine(content, text, color, caption, 1, w, h, border);
            }
            else
            {
                string? state = objects.ResolveAs<PdfName>(Get(widget, "AS"))?.Value ?? field.Name("V");
                if (state is not null && state != "Off")
                {
                    bool radio = (flags & FlagRadio) != 0;
                    string glyph = mk is null ? string.Empty : TextOf(objects, Get(mk, "CA")) ?? string.Empty;
                    if (glyph.Length == 0)
                    {
                        glyph = radio ? "l" : "4";
                    }

                    // ZapfDingbats codes are the glyph's own byte; size to fit the box.
                    double size = fontSize > 0 ? fontSize : Math.Min(w, h) * 0.8;
                    PdfDictionary fonts = new PdfDictionary();
                    PdfDictionary zadb = new PdfDictionary();
                    zadb.Set(PdfName.Intern("Type"), PdfName.Intern("Font"));
                    zadb.Set(PdfName.Intern("Subtype"), PdfName.Intern("Type1"));
                    zadb.Set(PdfName.Intern("BaseFont"), PdfName.Intern("ZapfDingbats"));
                    fonts.Set(PdfName.Intern("CvZaDb"), zadb);
                    resources.Set(PdfName.Intern("Font"), fonts);
                    byte code = (byte)glyph[0];
                    double gw = Chuvadi.Pdf.Fonts.Rendering.Standard14Widths.GetWidth("ZapfDingbats", code) / 1000.0 * size;
                    content.Op("q").Op("BT").Fill(color.IsEmpty ? PdfColorSpec.Black : color)
                        .Raw("/CvZaDb " + AppearanceContent.Num(size) + " Tf\n")
                        .Op("Tm", 1, 0, 0, 1, (w - gw) / 2, (h - (size * 0.705)) / 2)
                        .ShowHex(new[] { code }).Op("ET").Op("Q");
                }
            }
        }
        else
        {
            return null;
        }

        text?.Finish();
        if (text is not null && onWarning is not null)
        {
            string fieldName = field.FullName();
            foreach (string face in text.Substitutions)
            {
                onWarning(new PdfWarning(
                    PdfWarningKind.FontSubstituted,
                    pageNumber,
                    "field " + fieldName,
                    "the field's font /" + fontName + " cannot show some of its characters; they are drawn with " + face + " from Chuvadi.Fonts"));
            }

            if (text.HadMissing)
            {
                onWarning(new PdfWarning(
                    PdfWarningKind.FontSubstituted,
                    pageNumber,
                    "field " + fieldName,
                    "some characters of the field's value are in a script LiPi Sans does not cover; no substitute, they are drawn as .notdef"));
            }
        }

        return content.ToFormXObject(w, h, resources, matrix);
    }

    private static AppearanceText Text(
        PdfObjectStore objects, PdfDictionary resources, PdfDictionary? drFonts, string fontName, double size,
        string value, double w, double h, double border, bool multiline)
    {
        if (size > 0)
        {
            return new AppearanceText(objects, resources, drFonts, fontName, size);
        }

        // Auto size (§12.7.3.3): multiline uses 12; a single line fits the
        // height, then shrinks until the value fits the width.
        if (multiline)
        {
            return new AppearanceText(objects, resources, drFonts, fontName, 12);
        }

        double available = h - (2 * border) - 2;
        double fit = Math.Max(1, Math.Min(available / 1.15, 1000));
        AppearanceText t = new AppearanceText(objects, new PdfDictionary(), drFonts, fontName, fit);
        double textWidth = t.Measure(value);
        double room = w - (2 * border) - 4;
        if (textWidth > room && textWidth > 0)
        {
            fit = Math.Max(1, fit * room / textWidth);
        }

        return new AppearanceText(objects, resources, drFonts, fontName, fit);
    }

    private static void SingleLine(AppearanceContent content, AppearanceText text, PdfColorSpec color, string value, int q, double w, double h, double border)
    {
        double size = text.Size;
        double baseline = ((h - ((text.Ascent - text.Descent) * size)) / 2) - (text.Descent * size);
        double textWidth = text.Measure(value);
        double pad = border + 2;
        double x = q switch
        {
            1 => (w - textWidth) / 2,
            2 => w - pad - textWidth,
            _ => pad,
        };
        text.Show(content, color.IsEmpty ? PdfColorSpec.Black : color, value, x, baseline);
    }

    private static void Multiline(AppearanceContent content, AppearanceText text, PdfColorSpec color, string value, int q, double w, double h, double border)
    {
        double size = text.Size;
        double pad = border + 2;
        double room = w - (2 * pad);
        double lineHeight = size * 1.15;
        double y = h - pad - (text.Ascent * size);
        foreach (string line in Wrap(text, value, room))
        {
            double lw = text.Measure(line);
            double x = q switch
            {
                1 => (w - lw) / 2,
                2 => w - pad - lw,
                _ => pad,
            };
            text.Show(content, color.IsEmpty ? PdfColorSpec.Black : color, line, x, y);
            y -= lineHeight;
            if (y < -size)
            {
                break;
            }
        }
    }

    private static List<string> Wrap(AppearanceText text, string value, double room)
    {
        List<string> lines = new List<string>();
        foreach (string paragraph in value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            string current = string.Empty;
            foreach (string word in paragraph.Split(' '))
            {
                string candidate = current.Length == 0 ? word : current + " " + word;
                if (current.Length > 0 && text.Measure(candidate) > room)
                {
                    lines.Add(current);
                    current = word;
                }
                else
                {
                    current = candidate;
                }
            }

            lines.Add(current);
        }

        return lines;
    }

    private static void Comb(AppearanceContent content, AppearanceText text, PdfColorSpec color, string value, int maxLen, double w, double h)
    {
        double cell = w / maxLen;
        double size = text.Size;
        double baseline = ((h - ((text.Ascent - text.Descent) * size)) / 2) - (text.Descent * size);
        int n = Math.Min(maxLen, value.Length);
        for (int i = 0; i < n; i++)
        {
            string ch = value[i].ToString();
            double cw = text.Measure(ch);
            text.Show(content, color.IsEmpty ? PdfColorSpec.Black : color, ch, (i * cell) + ((cell - cw) / 2), baseline);
        }
    }

    private static void ListBox(
        AppearanceContent content, AppearanceText text, PdfColorSpec color, List<(string Export, string Display)> options,
        List<string> selected, int topIndex, double w, double h, double border)
    {
        double size = text.Size;
        double lineHeight = size * 1.15;
        double pad = border + 2;
        double y = h - border;
        PdfColorSpec highlight = new PdfColorSpec(new[] { 0.6, 0.75, 0.85 });
        for (int i = Math.Max(0, topIndex); i < options.Count && y > 0; i++)
        {
            bool isSelected = selected.Contains(options[i].Export) || selected.Contains(options[i].Display);
            if (isSelected)
            {
                content.Op("q").Fill(highlight).Rect(border, y - lineHeight, w - (2 * border), lineHeight).Op("f").Op("Q");
            }

            double baseline = y - lineHeight + ((lineHeight - ((text.Ascent - text.Descent) * size)) / 2) - (text.Descent * size);
            text.Show(content, color.IsEmpty ? PdfColorSpec.Black : color, options[i].Display, pad, baseline);
            y -= lineHeight;
        }
    }

    private static string Display(List<(string Export, string Display)> options, string value)
    {
        foreach ((string export, string display) in options)
        {
            if (export == value)
            {
                return display;
            }
        }

        return value;
    }

    private static List<(string, string)> Options(Field field, PdfObjectStore objects)
    {
        List<(string, string)> list = new List<(string, string)>();
        if (objects.ResolveAs<PdfArray>(field.Inherited("Opt")) is not PdfArray opt)
        {
            return list;
        }

        foreach (PdfPrimitive item in opt)
        {
            PdfPrimitive resolved = objects.Resolve(item);
            if (resolved is PdfArray pair && pair.Count >= 2)
            {
                list.Add((TextOf(objects, pair[0]) ?? string.Empty, TextOf(objects, pair[1]) ?? string.Empty));
            }
            else
            {
                string s = TextOf(objects, resolved) ?? string.Empty;
                list.Add((s, s));
            }
        }

        return list;
    }

    // "/Helv 12 Tf 0 g" -> (Helv, 12, gray 0). Unknown colour -> black.
    private static (string Font, double Size, PdfColorSpec Color) ParseDa(string da)
    {
        string font = "Helv";
        double size = 0;
        PdfColorSpec color = PdfColorSpec.Black;
        List<string> operands = new List<string>();
        foreach (string token in da.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            switch (token)
            {
                case "Tf" when operands.Count >= 2:
                    font = operands[^2].TrimStart('/');
                    size = Parse(operands[^1]);
                    break;
                case "g" when operands.Count >= 1:
                    color = new PdfColorSpec(new[] { Parse(operands[^1]) });
                    break;
                case "rg" when operands.Count >= 3:
                    color = new PdfColorSpec(new[] { Parse(operands[^3]), Parse(operands[^2]), Parse(operands[^1]) });
                    break;
                case "k" when operands.Count >= 4:
                    color = new PdfColorSpec(new[] { Parse(operands[^4]), Parse(operands[^3]), Parse(operands[^2]), Parse(operands[^1]) });
                    break;
                default:
                    operands.Add(token);
                    continue;
            }

            operands.Clear();
        }

        return (font, size, color);
    }

    private static double Parse(string s) =>
        double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0;

    private static double BorderWidth(PdfDictionary widget, PdfObjectStore objects)
    {
        PdfDictionary? bs = objects.ResolveAs<PdfDictionary>(Get(widget, "BS"));
        return bs is null ? 1 : Num(objects, Get(bs, "W"), 1);
    }

    private static PdfDictionary? DrFonts(PdfDictionary widget, PdfDictionary? acroForm, PdfObjectStore objects)
    {
        PdfDictionary? dr = objects.ResolveAs<PdfDictionary>(Get(widget, "DR")) ?? objects.ResolveAs<PdfDictionary>(Get(acroForm, "DR"));
        return dr is null ? null : objects.ResolveAs<PdfDictionary>(Get(dr, "Font"));
    }

    private static PdfArray Matrix(double a, double b, double c, double d, double e, double f) =>
        new PdfArray(new PdfPrimitive[] { new PdfReal(a), new PdfReal(b), new PdfReal(c), new PdfReal(d), new PdfReal(e), new PdfReal(f) });

    private static bool TryRect(PdfDictionary widget, PdfObjectStore objects, out double width, out double height)
    {
        width = 0;
        height = 0;
        if (objects.ResolveAs<PdfArray>(Get(widget, "Rect")) is not PdfArray r || r.Count < 4)
        {
            return false;
        }

        width = Math.Abs(Num(objects, r[2], 0) - Num(objects, r[0], 0));
        height = Math.Abs(Num(objects, r[3], 0) - Num(objects, r[1], 0));
        return true;
    }

    internal static PdfPrimitive Get(PdfDictionary? dict, string key) =>
        dict is not null && dict.TryGetValue(PdfName.Intern(key), out PdfPrimitive? v) && v is not null ? v : PdfNull.Value;

    internal static double Num(PdfObjectStore objects, PdfPrimitive value, double fallback) => objects.Resolve(value) switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => fallback,
    };

    internal static string? TextOf(PdfObjectStore objects, PdfPrimitive value) => objects.Resolve(value) switch
    {
        PdfString s => s.ToTextString(),
        PdfName n => n.Value,
        _ => null,
    };

    private static string? StringOf(PdfObjectStore objects, PdfPrimitive value) => objects.Resolve(value) is PdfString s ? s.ToTextString() : null;

    // A widget and its field ancestors, for inherited entries (§12.7.3.1).
    private sealed class Field
    {
        private readonly PdfDictionary _widget;
        private readonly PdfObjectStore _objects;

        internal Field(PdfDictionary widget, PdfObjectStore objects)
        {
            _widget = widget;
            _objects = objects;
        }

        internal PdfPrimitive Inherited(string key)
        {
            PdfDictionary? d = _widget;
            for (int depth = 0; d is not null && depth < 32; depth++)
            {
                if (d.TryGetValue(PdfName.Intern(key), out PdfPrimitive? v) && v is not null)
                {
                    return v;
                }

                d = _objects.ResolveAs<PdfDictionary>(Get(d, "Parent"));
            }

            return PdfNull.Value;
        }

        internal string? Name(string key) => _objects.ResolveAs<PdfName>(Inherited(key))?.Value;

        internal string? String(string key) => StringOf(_objects, Inherited(key));

        internal string? TextValue(string key) => TextOf(_objects, Inherited(key));

        internal double Number(string key, double fallback) => Num(_objects, Inherited(key), fallback);

        internal List<string> Values(string key)
        {
            List<string> list = new List<string>();
            PdfPrimitive v = _objects.Resolve(Inherited(key));
            if (v is PdfArray a)
            {
                foreach (PdfPrimitive item in a)
                {
                    string? s = TextOf(_objects, item);
                    if (s is not null)
                    {
                        list.Add(s);
                    }
                }
            }
            else if (TextOf(_objects, v) is string s)
            {
                list.Add(s);
            }

            return list;
        }

        internal string FullName()
        {
            List<string> parts = new List<string>();
            PdfDictionary? d = _widget;
            for (int depth = 0; d is not null && depth < 32; depth++)
            {
                if (StringOf(_objects, Get(d, "T")) is string t)
                {
                    parts.Insert(0, t);
                }

                d = _objects.ResolveAs<PdfDictionary>(Get(d, "Parent"));
            }

            return parts.Count == 0 ? "(unnamed)" : string.Join(".", parts);
        }
    }
}
