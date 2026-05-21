// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: v2.0.0 R2 — SVG renderer

using System.Globalization;
using System.Text;

namespace Chuvadi.Pdf.Svg;

/// <summary>
/// Internal helper that accumulates SVG text deterministically. Provides
/// invariant-culture float formatting with fixed precision, XML attribute
/// escaping, and monotonic id allocation.
/// </summary>
internal sealed class SvgWriter
{
    private readonly StringBuilder _sb;
    private readonly string _floatFormat;
    private readonly bool _indent;
    private int _depth;
    private int _idCounter;

    internal SvgWriter(int decimalPrecision, bool indent)
    {
        _sb = new StringBuilder(4096);
        _floatFormat = "0." + new string('#', decimalPrecision);
        _indent = indent;
        _depth = 0;
        _idCounter = 0;
    }

    internal int Length => _sb.Length;

    internal string ToSvgString()
    {
        return _sb.ToString();
    }

    /// <summary>Allocates the next stable id. Format: "c0", "c1", "c2", ...</summary>
    internal string NextClipId()
    {
        string id = "c" + _idCounter.ToString(CultureInfo.InvariantCulture);
        _idCounter++;
        return id;
    }

    /// <summary>Allocates the next stable glyph-defs id. Format: "g0", "g1", ...</summary>
    internal string NextGlyphId()
    {
        string id = "g" + _idCounter.ToString(CultureInfo.InvariantCulture);
        _idCounter++;
        return id;
    }

    internal void WriteRaw(string s)
    {
        _sb.Append(s);
    }

    internal void WriteLine()
    {
        if (_indent)
        {
            _sb.Append('\n');
        }
    }

    internal void WriteIndent()
    {
        if (_indent)
        {
            for (int i = 0; i < _depth; i++)
            {
                _sb.Append("  ");
            }
        }
    }

    internal void OpenTag(string name)
    {
        WriteIndent();
        _sb.Append('<');
        _sb.Append(name);
    }

    internal void CloseStartTag()
    {
        _sb.Append('>');
        WriteLine();
        _depth++;
    }

    internal void SelfCloseTag()
    {
        _sb.Append("/>");
        WriteLine();
    }

    internal void CloseTag(string name)
    {
        _depth--;
        WriteIndent();
        _sb.Append("</");
        _sb.Append(name);
        _sb.Append('>');
        WriteLine();
    }

    /// <summary>Writes a string attribute with XML escaping.</summary>
    internal void Attr(string name, string value)
    {
        _sb.Append(' ');
        _sb.Append(name);
        _sb.Append("=\"");
        AppendEscaped(value);
        _sb.Append('"');
    }

    /// <summary>Writes an integer attribute.</summary>
    internal void Attr(string name, int value)
    {
        _sb.Append(' ');
        _sb.Append(name);
        _sb.Append("=\"");
        _sb.Append(value.ToString(CultureInfo.InvariantCulture));
        _sb.Append('"');
    }

    /// <summary>Writes a double attribute using the configured precision.</summary>
    internal void AttrDouble(string name, double value)
    {
        _sb.Append(' ');
        _sb.Append(name);
        _sb.Append("=\"");
        AppendDouble(value);
        _sb.Append('"');
    }

    /// <summary>Writes a literal attribute value with no escaping (caller-validated).</summary>
    internal void AttrLiteral(string name, string literalValue)
    {
        _sb.Append(' ');
        _sb.Append(name);
        _sb.Append("=\"");
        _sb.Append(literalValue);
        _sb.Append('"');
    }

    internal void AppendDouble(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            // Non-finite values would corrupt the SVG; clamp to 0 silently.
            _sb.Append('0');
            return;
        }

        // ToString with the format pattern strips trailing zeros via the '#'
        // placeholders, while InvariantCulture keeps '.' as the decimal
        // separator across locales.
        string text = value.ToString(_floatFormat, CultureInfo.InvariantCulture);

        // Format leaves "0." (or "-0." etc.) when fraction rounds to zero;
        // strip the trailing dot so we emit "0" rather than "0.".
        if (text.Length > 0 && text[text.Length - 1] == '.')
        {
            text = text.Substring(0, text.Length - 1);
        }

        // Avoid the negative-zero string "-0".
        if (text == "-0")
        {
            text = "0";
        }

        _sb.Append(text);
    }

    /// <summary>
    /// Appends a string to the buffer with the five XML attribute escapes
    /// applied: &amp; &lt; &gt; &quot; &apos;.
    /// </summary>
    internal void AppendEscaped(string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];

            switch (c)
            {
                case '&': _sb.Append("&amp;"); break;
                case '<': _sb.Append("&lt;"); break;
                case '>': _sb.Append("&gt;"); break;
                case '"': _sb.Append("&quot;"); break;
                case '\'': _sb.Append("&apos;"); break;
                default: _sb.Append(c); break;
            }
        }
    }

    /// <summary>Appends a space-separated number to a path "d" buffer.</summary>
    internal void AppendPathNumber(StringBuilder buffer, double value, bool needsLeadingSpace)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            if (needsLeadingSpace)
            {
                buffer.Append(' ');
            }

            buffer.Append('0');
            return;
        }

        string text = value.ToString(_floatFormat, CultureInfo.InvariantCulture);

        if (text.Length > 0 && text[text.Length - 1] == '.')
        {
            text = text.Substring(0, text.Length - 1);
        }

        if (text == "-0")
        {
            text = "0";
        }

        // SVG path data allows omitting the separator before a negative
        // number; emit a space only when the previous character would
        // otherwise concatenate digits.
        if (needsLeadingSpace && text.Length > 0 && text[0] != '-')
        {
            buffer.Append(' ');
        }

        buffer.Append(text);
    }
}
