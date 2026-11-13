// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.9.7 — Inline images (BI ... ID ... EI),
//        Tables 92 and 93 (abbreviations)
// PHASE: 3.34.0 (Phase B N4) — inline images are read and drawn. Before, the
//        walker had no BI/ID/EI handling: the image's binary data was read as
//        content-stream tokens, so the image never drew and a stray "<" or
//        "(" in the data ended the rest of the page.

using System;
using System.Globalization;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Rendering.Walking;

/// <summary>
/// Reads one inline image, from just after its <c>BI</c> keyword to just
/// after its <c>EI</c> keyword. The image dictionary comes back with its
/// abbreviated keys and values written out in full (<c>/W</c> becomes
/// <c>/Width</c>, <c>/Fl</c> becomes <c>/FlateDecode</c>, <c>/G</c> becomes
/// <c>/DeviceGray</c>), so the image can be drawn like an image XObject.
/// </summary>
internal static class InlineImageReader
{
    private const int MaxDictionaryTokens = 4096;
    private const int MaxDepth = 32;

    // How many bytes after a candidate EI must look like content-stream
    // text (not binary image data) for it to count as the real end.
    private const int TextCheckLength = 32;

    /// <summary>
    /// Reads the image dictionary, finds where the data ends and leaves the
    /// tokenizer just after <c>EI</c>. False, with the reason in
    /// <paramref name="problem"/>, when the end of the image cannot be found.
    /// </summary>
    internal static bool TryRead(
        PdfTokenizer tokenizer, byte[] content, out PdfDictionary dictionary, out byte[] data, out string problem)
    {
        ArgumentNullException.ThrowIfNull(tokenizer);
        ArgumentNullException.ThrowIfNull(content);

        dictionary = new PdfDictionary();
        data = Array.Empty<byte>();
        problem = string.Empty;

        try
        {
            if (!ReadDictionary(tokenizer, dictionary))
            {
                problem = "An inline image (BI) has no ID keyword; the rest of the content is not drawn.";
                return false;
            }
        }
        catch (PdfParseException ex)
        {
            problem = "An inline image's dictionary is damaged (" + ex.Message + "); the rest of the content is not drawn.";
            return false;
        }

        // The data starts after the single white-space byte that follows ID.
        long dataStart = tokenizer.Position;
        if (dataStart < content.Length && IsWhiteSpace(content[dataStart]))
        {
            dataStart++;
        }

        long knownLength = UnfilteredLength(dictionary);
        if (knownLength >= 0)
        {
            long end = EndAfterData(content, dataStart, knownLength);
            if (end < 0 && dataStart < content.Length && content[dataStart - 1] == (byte)'\r' && content[dataStart] == (byte)'\n')
            {
                // ID followed by CR LF: the LF is not image data.
                dataStart++;
                end = EndAfterData(content, dataStart, knownLength);
            }

            if (end >= 0)
            {
                data = Slice(content, dataStart, knownLength);
                tokenizer.Seek(end);
                return true;
            }
        }

        long ei = FindEndKeyword(content, dataStart);
        if (ei < 0)
        {
            problem = "An inline image has no EI keyword after its data; the rest of the content is not drawn.";
            return false;
        }

        // The white-space byte before EI separates it from the data.
        long dataEnd = ei > dataStart && IsWhiteSpace(content[ei - 1]) ? ei - 1 : ei;
        data = Slice(content, dataStart, Math.Max(0, dataEnd - dataStart));
        tokenizer.Seek(ei + 2);
        return true;
    }

    // Reads key/value pairs up to ID. False when the content ends first.
    private static bool ReadDictionary(PdfTokenizer tokenizer, PdfDictionary dictionary)
    {
        int tokens = 0;
        while (true)
        {
            PdfToken key = tokenizer.Read();
            if (key.IsEndOfStream || ++tokens > MaxDictionaryTokens)
            {
                return false;
            }

            if (IsIdKeyword(key))
            {
                return true;
            }

            if (key.Type != PdfTokenType.Name)
            {
                // Not a key: skipped, as viewers do.
                continue;
            }

            PdfToken valueToken = tokenizer.Read();
            if (valueToken.IsEndOfStream)
            {
                return false;
            }

            if (IsIdKeyword(valueToken))
            {
                return true;
            }

            string name = FullKey(ContentStrings.ExtractName(key));
            PdfPrimitive? value = ReadValue(tokenizer, valueToken, 0);
            if (value is not null && name.Length > 0)
            {
                dictionary.Set(PdfName.Intern(name), FullValue(name, value));
            }
        }
    }

    private static PdfPrimitive? ReadValue(PdfTokenizer tokenizer, PdfToken token, int depth)
    {
        switch (token.Type)
        {
            case PdfTokenType.Integer:
                return int.TryParse(token.RawText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)
                    ? new PdfInteger(i)
                    : new PdfReal(Number(token));
            case PdfTokenType.Real:
                return new PdfReal(Number(token));
            case PdfTokenType.Name:
                string name = ContentStrings.ExtractName(token);
                return name.Length > 0 ? PdfName.Intern(name) : null;
            case PdfTokenType.LiteralString:
            case PdfTokenType.HexString:
                return new PdfString(ContentStrings.ExtractStringBytes(token));
            case PdfTokenType.True:
                return PdfBoolean.True;
            case PdfTokenType.False:
                return PdfBoolean.False;
            case PdfTokenType.Null:
                return PdfNull.Value;
            case PdfTokenType.ArrayStart:
                return depth < MaxDepth ? ReadArray(tokenizer, depth + 1) : null;
            case PdfTokenType.DictionaryStart:
                return depth < MaxDepth ? ReadInnerDictionary(tokenizer, depth + 1) : null;
            default:
                return null;
        }
    }

    private static PdfArray ReadArray(PdfTokenizer tokenizer, int depth)
    {
        PdfArray array = new PdfArray();
        for (int n = 0; n < MaxDictionaryTokens; n++)
        {
            PdfToken t = tokenizer.Read();
            if (t.IsEndOfStream || t.Type == PdfTokenType.ArrayEnd || t.Type == PdfTokenType.Keyword)
            {
                break;
            }

            PdfPrimitive? item = ReadValue(tokenizer, t, depth);
            if (item is not null)
            {
                array.Add(item);
            }
        }

        return array;
    }

    private static PdfDictionary ReadInnerDictionary(PdfTokenizer tokenizer, int depth)
    {
        PdfDictionary dict = new PdfDictionary();
        for (int n = 0; n < MaxDictionaryTokens; n++)
        {
            PdfToken key = tokenizer.Read();
            if (key.IsEndOfStream || key.Type == PdfTokenType.DictionaryEnd || key.Type == PdfTokenType.Keyword)
            {
                break;
            }

            if (key.Type != PdfTokenType.Name)
            {
                continue;
            }

            PdfToken valueToken = tokenizer.Read();
            if (valueToken.IsEndOfStream || valueToken.Type == PdfTokenType.DictionaryEnd)
            {
                break;
            }

            PdfPrimitive? value = ReadValue(tokenizer, valueToken, depth);
            string keyName = ContentStrings.ExtractName(key);
            if (value is not null && keyName.Length > 0)
            {
                dict.Set(PdfName.Intern(keyName), value);
            }
        }

        return dict;
    }

    // Table 93: the abbreviated keys of an inline image dictionary.
    private static string FullKey(string key) => key switch
    {
        "BPC" => "BitsPerComponent",
        "CS" => "ColorSpace",
        "D" => "Decode",
        "DP" => "DecodeParms",
        "F" => "Filter",
        "H" => "Height",
        "IM" => "ImageMask",
        "I" => "Interpolate",
        "W" => "Width",
        "L" => "Length",
        _ => key,
    };

    // Table 94: abbreviated filter and colour-space names.
    private static PdfPrimitive FullValue(string key, PdfPrimitive value)
    {
        if (key == "Filter")
        {
            return MapNames(value, FullFilter, all: true);
        }

        if (key == "ColorSpace")
        {
            return MapNames(value, FullColorSpace, all: false);
        }

        return value;
    }

    // Maps a name, or the names in an array (for a colour space only the
    // family and the Indexed base, the first two entries).
    private static PdfPrimitive MapNames(PdfPrimitive value, Func<string, string> map, bool all)
    {
        if (value is PdfName name)
        {
            return PdfName.Intern(map(name.Value));
        }

        if (value is not PdfArray array)
        {
            return value;
        }

        PdfArray mapped = new PdfArray(array.Count);
        for (int i = 0; i < array.Count; i++)
        {
            PdfPrimitive item = array[i];
            mapped.Add((all || i < 2) && item is PdfName n ? PdfName.Intern(map(n.Value)) : item);
        }

        return mapped;
    }

    private static string FullFilter(string name) => name switch
    {
        "AHx" => "ASCIIHexDecode",
        "A85" => "ASCII85Decode",
        "LZW" => "LZWDecode",
        "Fl" => "FlateDecode",
        "RL" => "RunLengthDecode",
        "CCF" => "CCITTFaxDecode",
        "DCT" => "DCTDecode",
        _ => name,
    };

    private static string FullColorSpace(string name) => name switch
    {
        "G" => "DeviceGray",
        "RGB" => "DeviceRGB",
        "CMYK" => "DeviceCMYK",
        "I" => "Indexed",
        _ => name,
    };

    // The data length of an unfiltered image whose size and colour space
    // are known from the dictionary alone, or -1.
    private static long UnfilteredLength(PdfDictionary dict)
    {
        if (dict.TryGetValue(PdfName.Intern("Filter"), out PdfPrimitive? filter)
            && filter is not PdfNull && !(filter is PdfArray { Count: 0 }))
        {
            return -1;
        }

        long width = IntOf(dict, "Width");
        long height = IntOf(dict, "Height");
        long bpc = IntOf(dict, "BitsPerComponent");
        long components;
        if (dict.TryGetValue(PdfName.Intern("ImageMask"), out PdfPrimitive? mask) && mask is PdfBoolean { Value: true })
        {
            components = 1;
            bpc = 1;
        }
        else
        {
            components = ComponentsOf(dict);
        }

        if (width <= 0 || height <= 0 || bpc <= 0 || components <= 0)
        {
            return -1;
        }

        long rowBytes = ((width * components * bpc) + 7) / 8;
        return rowBytes * height;
    }

    private static long ComponentsOf(PdfDictionary dict)
    {
        if (!dict.TryGetValue(PdfName.Intern("ColorSpace"), out PdfPrimitive? cs) || cs is null)
        {
            return -1;
        }

        string family = cs switch
        {
            PdfName n => n.Value,
            PdfArray { Count: > 0 } a when a[0] is PdfName first => first.Value,
            _ => string.Empty,
        };

        return family switch
        {
            "DeviceGray" or "CalGray" or "Indexed" => 1,
            "DeviceRGB" or "CalRGB" or "Lab" => 3,
            "DeviceCMYK" => 4,
            _ => -1,
        };
    }

    private static long IntOf(PdfDictionary dict, string key)
    {
        if (!dict.TryGetValue(PdfName.Intern(key), out PdfPrimitive? value))
        {
            return -1;
        }

        return value switch
        {
            PdfInteger i => i.Value,
            PdfReal r => (long)r.Value,
            _ => -1,
        };
    }

    // Where the content continues when EI follows the data (after optional
    // white space) and is itself followed by a delimiter, or -1.
    private static long EndAfterData(byte[] content, long dataStart, long length)
    {
        long k = dataStart + length;
        if (k > content.Length)
        {
            return -1;
        }

        while (k < content.Length && IsWhiteSpace(content[k]))
        {
            k++;
        }

        return IsEndKeywordAt(content, k) ? k + 2 : -1;
    }

    // The first EI that stands as a keyword (white space before it, a
    // delimiter or the end after it) and is followed by text rather than
    // binary data, or -1.
    private static long FindEndKeyword(byte[] content, long from)
    {
        for (long i = from; i + 1 < content.Length; i++)
        {
            if (content[i] != (byte)'E' || content[i + 1] != (byte)'I')
            {
                continue;
            }

            if (i > from && !IsWhiteSpace(content[i - 1]))
            {
                continue;
            }

            if (IsEndKeywordAt(content, i) && LooksLikeText(content, i + 2))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsEndKeywordAt(byte[] content, long i)
    {
        if (i + 1 >= content.Length || content[i] != (byte)'E' || content[i + 1] != (byte)'I')
        {
            return false;
        }

        long after = i + 2;
        return after >= content.Length || IsWhiteSpace(content[after]) || IsDelimiter(content[after]);
    }

    private static bool LooksLikeText(byte[] content, long from)
    {
        long end = Math.Min(content.Length, from + TextCheckLength);
        for (long i = from; i < end; i++)
        {
            byte b = content[i];
            if (!IsWhiteSpace(b) && (b < 0x20 || b > 0x7E))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsIdKeyword(PdfToken token) =>
        token.Type == PdfTokenType.Keyword && token.RawText == "ID";

    private static byte[] Slice(byte[] content, long start, long length)
    {
        PdfWorkScope.ChargeMemory(length);
        byte[] bytes = new byte[length];
        Array.Copy(content, start, bytes, 0, length);
        return bytes;
    }

    private static double Number(PdfToken token) =>
        double.TryParse(token.RawText, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0.0;

    private static bool IsWhiteSpace(byte b) =>
        b == 0x00 || b == 0x09 || b == 0x0A || b == 0x0C || b == 0x0D || b == 0x20;

    private static bool IsDelimiter(byte b) =>
        b == (byte)'(' || b == (byte)')' || b == (byte)'<' || b == (byte)'>' || b == (byte)'['
        || b == (byte)']' || b == (byte)'{' || b == (byte)'}' || b == (byte)'/' || b == (byte)'%';
}
