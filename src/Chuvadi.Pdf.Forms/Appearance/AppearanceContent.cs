// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.31 — appearance streams made from field and annotation entries
//        (D-108 item c, D-146): the content-stream writer they share.

using System.Globalization;
using System.Text;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Forms.Appearance;

/// <summary>Writes the operators of an appearance content stream.</summary>
internal sealed class AppearanceContent
{
    private readonly StringBuilder _sb = new StringBuilder();

    internal AppearanceContent Op(string op)
    {
        _sb.Append(op).Append('\n');
        return this;
    }

    internal AppearanceContent Op(string op, params double[] operands)
    {
        foreach (double v in operands)
        {
            _sb.Append(Num(v)).Append(' ');
        }

        _sb.Append(op).Append('\n');
        return this;
    }

    internal AppearanceContent Raw(string text)
    {
        _sb.Append(text);
        return this;
    }

    internal AppearanceContent Rect(double x, double y, double w, double h) => Op("re", x, y, w, h);

    internal AppearanceContent Fill(PdfColorSpec color)
    {
        _sb.Append(color.FillOperator).Append('\n');
        return this;
    }

    internal AppearanceContent Stroke(PdfColorSpec color)
    {
        _sb.Append(color.StrokeOperator).Append('\n');
        return this;
    }

    // A glyph or code string as a hex string operand.
    internal AppearanceContent ShowHex(byte[] codes)
    {
        _sb.Append('<');
        foreach (byte b in codes)
        {
            _sb.Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }

        _sb.Append("> Tj\n");
        return this;
    }

    internal byte[] ToBytes() => Encoding.Latin1.GetBytes(_sb.ToString());

    internal static string Num(double v)
    {
        double r = System.Math.Round(v, 4);
        if (r == 0)
        {
            return "0";
        }

        return r.ToString("0.####", CultureInfo.InvariantCulture);
    }

    internal static string Name(string name) => "/" + name;

    // A form XObject holding this content, with the given box and resources.
    internal PdfStream ToFormXObject(double width, double height, PdfDictionary resources, PdfArray? matrix = null)
    {
        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Intern("Type"), PdfName.Intern("XObject"));
        dict.Set(PdfName.Intern("Subtype"), PdfName.Intern("Form"));
        dict.Set(PdfName.Intern("BBox"), new PdfArray(new PdfPrimitive[] { new PdfReal(0), new PdfReal(0), new PdfReal(width), new PdfReal(height) }));
        if (matrix is not null)
        {
            dict.Set(PdfName.Intern("Matrix"), matrix);
        }

        dict.Set(PdfName.Intern("Resources"), resources);
        byte[] bytes = ToBytes();
        dict.Set(PdfName.Intern("Length"), bytes.Length);
        return new PdfStream(dict, bytes);
    }
}

/// <summary>A device colour for appearance content: gray, RGB or CMYK components.</summary>
internal readonly record struct PdfColorSpec(double[] Components)
{
    internal static readonly PdfColorSpec Black = new PdfColorSpec(new[] { 0.0 });

    internal bool IsEmpty => Components is null || Components.Length == 0;

    internal string FillOperator => Operator(false);

    internal string StrokeOperator => Operator(true);

    // /C, /IC, /BG, /BC arrays: 0 entries (transparent), 1 (gray), 3 (RGB), 4 (CMYK).
    internal static PdfColorSpec FromArray(PdfArray? array, Chuvadi.Pdf.Objects.PdfObjectStore objects)
    {
        if (array is null)
        {
            return new PdfColorSpec(System.Array.Empty<double>());
        }

        double[] c = new double[array.Count];
        for (int i = 0; i < c.Length; i++)
        {
            c[i] = objects.Resolve(array[i]) switch
            {
                PdfInteger n => n.Value,
                PdfReal r => r.Value,
                _ => 0,
            };
        }

        return c.Length is 1 or 3 or 4 ? new PdfColorSpec(c) : new PdfColorSpec(System.Array.Empty<double>());
    }

    private string Operator(bool stroke)
    {
        StringBuilder sb = new StringBuilder();
        double[] components = Components ?? System.Array.Empty<double>();
        foreach (double v in components)
        {
            sb.Append(AppearanceContent.Num(v)).Append(' ');
        }

        string op = components.Length switch
        {
            1 => stroke ? "G" : "g",
            3 => stroke ? "RG" : "rg",
            4 => stroke ? "K" : "k",
            _ => string.Empty,
        };
        return sb.Append(op).ToString();
    }
}
