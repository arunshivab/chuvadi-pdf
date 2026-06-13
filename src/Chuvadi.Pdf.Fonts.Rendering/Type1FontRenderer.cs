// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Embedded Type1 (FontFile) glyph rendering

using System;
using System.Collections.Generic;
using System.Text;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using GraphicsPath = Chuvadi.Pdf.Graphics.Path;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Fonts.Rendering;

/// <summary>
/// Renders glyphs from an embedded Type1 (PostScript) font program — the
/// <c>FontFile</c> stream of a simple Type1 font. Implements eexec decryption,
/// charstring decryption, and the Type1 charstring interpreter (PDF 32000-1:2008
/// §9.6.2; Adobe Type 1 Font Format).
/// </summary>
/// <remarks>
/// Outlines are produced in a 1000-unit em and scaled by the caller's point
/// size. Glyph selection is by character code through the font dictionary's
/// <c>/Encoding</c> (with <c>/Differences</c>) when present, otherwise the
/// font program's built-in encoding, otherwise StandardEncoding.
/// </remarks>
public sealed class Type1FontRenderer
{
    private const ushort EexecR = 55665;
    private const ushort CharstringR = 4330;

    private readonly Dictionary<string, byte[]> _charstrings;
    private readonly List<byte[]> _subrs;
    private readonly string[] _encoding; // code -> glyph name (256)
    private readonly Dictionary<int, GraphicsPath> _pathCache = new();
    private readonly Dictionary<int, double> _widthCache = new();

    private Type1FontRenderer(
        Dictionary<string, byte[]> charstrings,
        List<byte[]> subrs,
        string[] encoding)
    {
        _charstrings = charstrings;
        _subrs = subrs;
        _encoding = encoding;
    }

    /// <summary>
    /// Parses an embedded Type1 program and builds a renderer. The
    /// <paramref name="fontDict"/> supplies a PDF <c>/Encoding</c> override
    /// (Differences) when present.
    /// </summary>
    /// <returns>A renderer, or <c>null</c> when the program cannot be parsed.</returns>
    /// <exception cref="ArgumentNullException">When a required argument is null.</exception>
    public static Type1FontRenderer? Create(byte[] fontFile, PdfDictionary fontDict, IPdfObjectResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(fontFile);
        ArgumentNullException.ThrowIfNull(fontDict);
        ArgumentNullException.ThrowIfNull(resolver);

        byte[] program = NormalizePfb(fontFile);

        int eexecPos = IndexOf(program, "eexec", 0);
        if (eexecPos < 0)
        {
            return null;
        }

        string clear = Encoding.Latin1.GetString(program, 0, eexecPos);

        int binStart = eexecPos + 5;
        while (binStart < program.Length && IsWhitespace(program[binStart]))
        {
            binStart++;
        }

        byte[] encryptedSection = ExtractEexecBinary(program, binStart);
        byte[] privatePart = Decrypt(encryptedSection, EexecR, 4);

        int lenIV = ParseLenIV(privatePart);
        List<byte[]> subrs = ParseSubrs(privatePart, lenIV);
        Dictionary<string, byte[]> charstrings = ParseCharStrings(privatePart, lenIV);
        if (charstrings.Count == 0)
        {
            return null;
        }

        string[] encoding = BuildEncoding(clear, fontDict, resolver);
        return new Type1FontRenderer(charstrings, subrs, encoding);
    }

    /// <summary>
    /// Returns the outline path for <paramref name="code"/>, scaled to
    /// <paramref name="pointSize"/>, and reports the glyph advance in points.
    /// </summary>
    public GraphicsPath GetGlyphPath(int code, double pointSize, out double advance)
    {
        if (pointSize <= 0)
        {
            advance = 0;
            return new GraphicsPath();
        }

        GraphicsPath unscaled = GetUnscaledPath(code, out double width1000);
        advance = width1000 / 1000.0 * pointSize;

        if (unscaled.IsEmpty)
        {
            return unscaled;
        }

        double scale = pointSize / 1000.0;
        return ScalePath(unscaled, scale);
    }

    private GraphicsPath GetUnscaledPath(int code, out double width1000)
    {
        if (_pathCache.TryGetValue(code, out GraphicsPath? cached))
        {
            width1000 = _widthCache[code];
            return cached;
        }

        string? name = code >= 0 && code < 256 ? _encoding[code] : null;
        GraphicsPath path = new();
        double width = 500;

        if (name is not null && _charstrings.TryGetValue(name, out byte[]? charstring))
        {
            Interpreter interp = new(this);
            interp.Run(charstring);
            path = interp.Path;
            width = interp.Width;
        }

        _pathCache[code] = path;
        _widthCache[code] = width;
        width1000 = width;
        return path;
    }

    // ── Type1 charstring interpreter ──────────────────────────────────────

    private sealed class Interpreter
    {
        private readonly Type1FontRenderer _font;
        private readonly List<double> _stack = new();
        private readonly List<double> _psStack = new();
        private double _x;
        private double _y;
        private double _sbx;
        private bool _open;
        private int _flexPointCount = -1;
        private readonly List<double> _flexPoints = new();

        public Interpreter(Type1FontRenderer font)
        {
            _font = font;
        }

        public GraphicsPath Path { get; } = new();
        public double Width { get; private set; } = 500;

        public void Run(byte[] cs)
        {
            Execute(cs, 0);
            if (_open)
            {
                Path.ClosePath();
            }
        }

        private bool Execute(byte[] cs, int depth)
        {
            if (depth > 60)
            {
                return true; // runaway recursion guard
            }

            int i = 0;
            while (i < cs.Length)
            {
                int b = cs[i++];
                if (b >= 32)
                {
                    double val;
                    if (b <= 246)
                    {
                        val = b - 139;
                    }
                    else if (b <= 250)
                    {
                        val = (b - 247) * 256 + cs[i++] + 108;
                    }
                    else if (b <= 254)
                    {
                        val = -((b - 251) * 256) - cs[i++] - 108;
                    }
                    else
                    {
                        val = (cs[i] << 24) | (cs[i + 1] << 16) | (cs[i + 2] << 8) | cs[i + 3];
                        i += 4;
                    }
                    _stack.Add(val);
                    continue;
                }

                if (b == 12)
                {
                    int b2 = cs[i++];
                    if (HandleEscape(b2))
                    {
                        return true;
                    }
                    continue;
                }

                if (HandleOperator(b, depth, out bool stop))
                {
                    if (stop)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private bool HandleOperator(int op, int depth, out bool stop)
        {
            stop = false;
            switch (op)
            {
                case 13: // hsbw: sbx wx
                    if (_stack.Count >= 2)
                    {
                        _sbx = _stack[0];
                        Width = _stack[1];
                        _x = _sbx;
                        _y = 0;
                    }
                    _stack.Clear();
                    return true;
                case 9: // closepath
                    if (_open)
                    {
                        Path.ClosePath();
                        _open = false;
                    }
                    _stack.Clear();
                    return true;
                case 21: // rmoveto
                    if (_stack.Count >= 2) { MoveBy(_stack[^2], _stack[^1]); }
                    _stack.Clear();
                    return true;
                case 22: // hmoveto
                    if (_stack.Count >= 1) { MoveBy(_stack[^1], 0); }
                    _stack.Clear();
                    return true;
                case 4: // vmoveto
                    if (_stack.Count >= 1) { MoveBy(0, _stack[^1]); }
                    _stack.Clear();
                    return true;
                case 5: // rlineto
                    if (_stack.Count >= 2) { LineBy(_stack[^2], _stack[^1]); }
                    _stack.Clear();
                    return true;
                case 6: // hlineto
                    if (_stack.Count >= 1) { LineBy(_stack[^1], 0); }
                    _stack.Clear();
                    return true;
                case 7: // vlineto
                    if (_stack.Count >= 1) { LineBy(0, _stack[^1]); }
                    _stack.Clear();
                    return true;
                case 8: // rrcurveto
                    if (_stack.Count >= 6) { CurveBy(_stack[^6], _stack[^5], _stack[^4], _stack[^3], _stack[^2], _stack[^1]); }
                    _stack.Clear();
                    return true;
                case 30: // vhcurveto
                    if (_stack.Count >= 4) { CurveBy(0, _stack[^4], _stack[^3], _stack[^2], _stack[^1], 0); }
                    _stack.Clear();
                    return true;
                case 31: // hvcurveto
                    if (_stack.Count >= 4) { CurveBy(_stack[^4], 0, _stack[^3], _stack[^2], 0, _stack[^1]); }
                    _stack.Clear();
                    return true;
                case 1: // hstem
                case 3: // vstem
                    _stack.Clear();
                    return true;
                case 10: // callsubr
                    if (_stack.Count >= 1)
                    {
                        int idx = (int)_stack[^1];
                        _stack.RemoveAt(_stack.Count - 1);
                        if (idx >= 0 && idx < _font._subrs.Count)
                        {
                            if (Execute(_font._subrs[idx], depth + 1))
                            {
                                stop = true;
                                return true;
                            }
                        }
                    }
                    return true;
                case 11: // return
                    return true;
                case 14: // endchar
                    stop = true;
                    return true;
                default:
                    _stack.Clear();
                    return true;
            }
        }

        private bool HandleEscape(int op)
        {
            switch (op)
            {
                case 12: // div
                    if (_stack.Count >= 2)
                    {
                        double bd = _stack[^1];
                        double ad = _stack[^2];
                        _stack.RemoveAt(_stack.Count - 1);
                        _stack[^1] = bd != 0 ? ad / bd : 0;
                    }
                    return false;
                case 6: // seac: asb adx ady bchar achar
                    if (_stack.Count >= 5)
                    {
                        Seac(_stack[0], _stack[1], _stack[2], (int)_stack[3], (int)_stack[4]);
                    }
                    _stack.Clear();
                    return true;
                case 7: // sbw
                    if (_stack.Count >= 4)
                    {
                        _sbx = _stack[0];
                        Width = _stack[2];
                        _x = _sbx;
                        _y = _stack[1];
                    }
                    _stack.Clear();
                    return true;
                case 16: // callothersubr
                    CallOtherSubr();
                    return false;
                case 17: // pop
                    _stack.Add(_psStack.Count > 0 ? PopPs() : 0);
                    return false;
                case 33: // setcurrentpoint
                    if (_stack.Count >= 2)
                    {
                        _x = _stack[^2];
                        _y = _stack[^1];
                    }
                    _stack.Clear();
                    return false;
                case 0: // dotsection
                case 1: // vstem3
                case 2: // hstem3
                    _stack.Clear();
                    return false;
                default:
                    _stack.Clear();
                    return false;
            }
        }

        // OtherSubrs: 0 = flex, 1 = flex begin, 2 = flex add point, 3 = hint replacement.
        private void CallOtherSubr()
        {
            if (_stack.Count < 2)
            {
                _stack.Clear();
                return;
            }

            int othersubr = (int)_stack[^1];
            int n = (int)_stack[^2];
            _stack.RemoveAt(_stack.Count - 1);
            _stack.RemoveAt(_stack.Count - 1);

            double[] args = new double[n];
            for (int k = n - 1; k >= 0; k--)
            {
                args[k] = _stack.Count > 0 ? PopStack() : 0;
            }

            switch (othersubr)
            {
                case 1: // begin flex
                    _flexPointCount = 0;
                    _flexPoints.Clear();
                    break;
                case 2: // collect a flex reference point (current point)
                    break;
                case 0: // end flex: build two curves from the 7 collected points
                    EndFlex();
                    break;
                case 3: // hint replacement: push subr# back for the following pop+callsubr
                    _psStack.Add(args.Length > 0 ? args[0] : 3);
                    break;
                default:
                    // Unknown: echo args back (PostScript convention) for pop.
                    for (int k = 0; k < args.Length; k++)
                    {
                        _psStack.Add(args[k]);
                    }
                    break;
            }
        }

        private void EndFlex()
        {
            // Flex is implemented by the BuildCharArray rmoveto calls that
            // preceded; the reference/control points were applied as moves.
            // Replace the 7 moveto points with two curves. We approximated the
            // moves as rmovetos updating _x,_y; the collected absolute points:
            if (_flexPoints.Count >= 14)
            {
                double x1 = _flexPoints[2], y1 = _flexPoints[3];
                double x2 = _flexPoints[4], y2 = _flexPoints[5];
                double x3 = _flexPoints[6], y3 = _flexPoints[7];
                double x4 = _flexPoints[8], y4 = _flexPoints[9];
                double x5 = _flexPoints[10], y5 = _flexPoints[11];
                double x6 = _flexPoints[12], y6 = _flexPoints[13];
                Path.CubicBezierTo(new PointF((float)x1, (float)y1), new PointF((float)x2, (float)y2), new PointF((float)x3, (float)y3));
                Path.CubicBezierTo(new PointF((float)x4, (float)y4), new PointF((float)x5, (float)y5), new PointF((float)x6, (float)y6));
                _x = x6;
                _y = y6;
            }
            _flexPointCount = -1;
            // Leave the final x,y on the PS stack for the following two pops.
            _psStack.Add(_y);
            _psStack.Add(_x);
        }

        private double PopStack()
        {
            double v = _stack[^1];
            _stack.RemoveAt(_stack.Count - 1);
            return v;
        }

        private double PopPs()
        {
            double v = _psStack[^1];
            _psStack.RemoveAt(_psStack.Count - 1);
            return v;
        }

        private void MoveBy(double dx, double dy)
        {
            _x += dx;
            _y += dy;
            if (_flexPointCount >= 0)
            {
                _flexPoints.Add(_x);
                _flexPoints.Add(_y);
                _flexPointCount++;
                return;
            }
            if (_open)
            {
                Path.ClosePath();
            }
            Path.MoveTo(_x, _y);
            _open = true;
        }

        private void LineBy(double dx, double dy)
        {
            _x += dx;
            _y += dy;
            Path.LineTo(_x, _y);
        }

        private void CurveBy(double dx1, double dy1, double dx2, double dy2, double dx3, double dy3)
        {
            double c1x = _x + dx1;
            double c1y = _y + dy1;
            double c2x = c1x + dx2;
            double c2y = c1y + dy2;
            double ex = c2x + dx3;
            double ey = c2y + dy3;
            Path.CubicBezierTo(
                new PointF((float)c1x, (float)c1y),
                new PointF((float)c2x, (float)c2y),
                new PointF((float)ex, (float)ey));
            _x = ex;
            _y = ey;
        }

        private void Seac(double asb, double adx, double ady, int bchar, int achar)
        {
            string? baseName = StandardEncoding.GetName(bchar);
            string? accentName = StandardEncoding.GetName(achar);

            if (baseName is not null && _font._charstrings.TryGetValue(baseName, out byte[]? baseCs))
            {
                Interpreter b = new(_font);
                b.Run(baseCs);
                AppendPath(b.Path, 0, 0);
            }

            if (accentName is not null && _font._charstrings.TryGetValue(accentName, out byte[]? accentCs))
            {
                Interpreter a = new(_font);
                a.Run(accentCs);
                AppendPath(a.Path, _sbx - asb + adx, ady);
            }
        }

        private void AppendPath(GraphicsPath src, double dx, double dy)
        {
            foreach (PathSegment seg in src.Segments)
            {
                switch (seg.Kind)
                {
                    case PathSegmentKind.MoveTo:
                        Path.MoveTo(seg.P0.X + dx, seg.P0.Y + dy);
                        break;
                    case PathSegmentKind.LineTo:
                        Path.LineTo(seg.P0.X + dx, seg.P0.Y + dy);
                        break;
                    case PathSegmentKind.CubicBezierTo:
                        Path.CubicBezierTo(
                            new PointF((float)(seg.P0.X + dx), (float)(seg.P0.Y + dy)),
                            new PointF((float)(seg.P1.X + dx), (float)(seg.P1.Y + dy)),
                            new PointF((float)(seg.P2.X + dx), (float)(seg.P2.Y + dy)));
                        break;
                    case PathSegmentKind.ClosePath:
                        Path.ClosePath();
                        break;
                    default:
                        break;
                }
            }
        }
    }

    // ── Type1 program parsing ─────────────────────────────────────────────

    private static byte[] NormalizePfb(byte[] data)
    {
        if (data.Length < 6 || data[0] != 0x80)
        {
            return data; // raw PostScript, not PFB-segmented
        }

        List<byte> output = new(data.Length);
        int pos = 0;
        while (pos < data.Length && data[pos] == 0x80)
        {
            int type = data[pos + 1];
            if (type == 3)
            {
                break; // EOF segment
            }
            int len = data[pos + 2] | (data[pos + 3] << 8) | (data[pos + 4] << 16) | (data[pos + 5] << 24);
            pos += 6;
            for (int k = 0; k < len && pos < data.Length; k++)
            {
                output.Add(data[pos++]);
            }
        }
        return output.ToArray();
    }

    private static byte[] ExtractEexecBinary(byte[] program, int start)
    {
        // The eexec section may be binary or ASCII-hex. Detect hex by checking
        // the first bytes are hex digits / whitespace.
        bool looksHex = true;
        int chec01 = 0;
        for (int k = start; k < program.Length && chec01 < 4; k++)
        {
            byte c = program[k];
            if (IsWhitespace(c))
            {
                continue;
            }
            if (!IsHexDigit(c))
            {
                looksHex = false;
                break;
            }
            chec01++;
        }

        if (!looksHex)
        {
            int len = program.Length - start;
            byte[] bin = new byte[len];
            Array.Copy(program, start, bin, 0, len);
            return bin;
        }

        List<byte> hex = new((program.Length - start) / 2);
        int hi = -1;
        for (int k = start; k < program.Length; k++)
        {
            byte c = program[k];
            if (IsWhitespace(c))
            {
                continue;
            }
            int v = HexVal(c);
            if (v < 0)
            {
                break;
            }
            if (hi < 0)
            {
                hi = v;
            }
            else
            {
                hex.Add((byte)((hi << 4) | v));
                hi = -1;
            }
        }
        return hex.ToArray();
    }

    private static byte[] Decrypt(byte[] cipher, ushort r, int skip)
    {
        const ushort C1 = 52845;
        const ushort C2 = 22719;
        ushort key = r;
        byte[] plain = new byte[cipher.Length];
        for (int k = 0; k < cipher.Length; k++)
        {
            byte c = cipher[k];
            plain[k] = (byte)(c ^ (key >> 8));
            key = (ushort)((c + key) * C1 + C2);
        }
        if (skip <= 0)
        {
            return plain;
        }
        if (skip >= plain.Length)
        {
            return Array.Empty<byte>();
        }
        byte[] result = new byte[plain.Length - skip];
        Array.Copy(plain, skip, result, 0, result.Length);
        return result;
    }

    private static int ParseLenIV(byte[] data)
    {
        int pos = IndexOf(data, "/lenIV", 0);
        if (pos < 0)
        {
            return 4;
        }
        pos += 6;
        return ReadIntAt(data, ref pos, 4);
    }

    private static List<byte[]> ParseSubrs(byte[] data, int lenIV)
    {
        List<byte[]> subrs = new();
        int pos = IndexOf(data, "/Subrs", 0);
        if (pos < 0)
        {
            return subrs;
        }

        // Entries: dup <index> <length> RD <binary> NP
        int scan = pos;
        while (true)
        {
            int dup = IndexOf(data, "dup ", scan);
            if (dup < 0)
            {
                break;
            }
            // Stop if we've run into CharStrings.
            int csMark = IndexOf(data, "/CharStrings", pos);
            if (csMark >= 0 && dup > csMark)
            {
                break;
            }
            int p = dup + 4;
            int index = ReadIntAt(data, ref p, -1);
            SkipSpaces(data, ref p);
            int len = ReadIntAt(data, ref p, -1);
            int rd = FindRdToken(data, p);
            if (index < 0 || len < 0 || rd < 0)
            {
                scan = dup + 4;
                continue;
            }
            int binStart = rd + 1; // single space after RD token
            if (binStart + len > data.Length)
            {
                break;
            }
            byte[] enc = new byte[len];
            Array.Copy(data, binStart, enc, 0, len);
            byte[] dec = Decrypt(enc, CharstringR, lenIV);
            while (subrs.Count <= index)
            {
                subrs.Add(Array.Empty<byte>());
            }
            subrs[index] = dec;
            scan = binStart + len;
        }
        return subrs;
    }

    private static Dictionary<string, byte[]> ParseCharStrings(byte[] data, int lenIV)
    {
        Dictionary<string, byte[]> map = new(StringComparer.Ordinal);
        int pos = IndexOf(data, "/CharStrings", 0);
        if (pos < 0)
        {
            return map;
        }

        int scan = pos + 12;
        while (true)
        {
            // Entries: /<name> <length> RD <binary> ND
            int slash = IndexOf(data, "/", scan);
            if (slash < 0)
            {
                break;
            }
            int p = slash + 1;
            StringBuilder name = new();
            while (p < data.Length && !IsWhitespace(data[p]) && data[p] != '{' && data[p] != '(')
            {
                name.Append((char)data[p]);
                p++;
            }
            SkipSpaces(data, ref p);
            int len = ReadIntAt(data, ref p, -1);
            if (len < 0)
            {
                scan = slash + 1;
                continue;
            }
            int rd = FindRdToken(data, p);
            if (rd < 0)
            {
                scan = slash + 1;
                continue;
            }
            int binStart = rd + 1;
            if (binStart + len > data.Length)
            {
                break;
            }
            byte[] enc = new byte[len];
            Array.Copy(data, binStart, enc, 0, len);
            map[name.ToString()] = Decrypt(enc, CharstringR, lenIV);
            scan = binStart + len;

            if (name.ToString() == "end" || map.Count > 5000)
            {
                break;
            }
        }
        return map;
    }

    private static string[] BuildEncoding(string clear, PdfDictionary fontDict, IPdfObjectResolver resolver)
    {
        string[] enc = new string[256];

        // Built-in encoding from the cleartext portion.
        if (clear.Contains("StandardEncoding", StringComparison.Ordinal))
        {
            for (int c = 0; c < 256; c++)
            {
                enc[c] = StandardEncoding.GetName(c) ?? ".notdef";
            }
        }
        else
        {
            for (int c = 0; c < 256; c++)
            {
                enc[c] = ".notdef";
            }
            // Parse "dup <code> /<name> put" entries.
            int scan = clear.IndexOf("/Encoding", StringComparison.Ordinal);
            if (scan >= 0)
            {
                int idx = scan;
                while (true)
                {
                    int dup = clear.IndexOf("dup ", idx, StringComparison.Ordinal);
                    if (dup < 0)
                    {
                        break;
                    }
                    int readonlyEnd = clear.IndexOf("readonly", scan, StringComparison.Ordinal);
                    int defEnd = clear.IndexOf(" def", scan, StringComparison.Ordinal);
                    int limit = readonlyEnd >= 0 ? readonlyEnd : (defEnd >= 0 ? defEnd : clear.Length);
                    if (dup > limit)
                    {
                        break;
                    }
                    int q = dup + 4;
                    int code = ReadDecimal(clear, ref q);
                    while (q < clear.Length && clear[q] != '/')
                    {
                        q++;
                    }
                    if (q < clear.Length && clear[q] == '/')
                    {
                        q++;
                        StringBuilder nm = new();
                        while (q < clear.Length && !char.IsWhiteSpace(clear[q]))
                        {
                            nm.Append(clear[q]);
                            q++;
                        }
                        if (code >= 0 && code < 256)
                        {
                            enc[code] = nm.ToString();
                        }
                    }
                    idx = dup + 4;
                }
            }
        }

        // PDF /Encoding with /Differences overrides the built-in encoding.
        if (fontDict.TryGetValue(PdfName.Intern("Encoding"), out PdfPrimitive? encPrim))
        {
            PdfPrimitive? resolved = encPrim is PdfReference ? resolver.Resolve(encPrim) : encPrim;
            if (resolved is PdfName baseEnc)
            {
                if (baseEnc.Value is "WinAnsiEncoding" or "MacRomanEncoding" or "StandardEncoding")
                {
                    for (int c = 0; c < 256; c++)
                    {
                        string? n = StandardEncoding.GetName(c);
                        if (n is not null)
                        {
                            enc[c] = n;
                        }
                    }
                }
            }
            else if (resolved is PdfDictionary encDict
                && encDict.TryGetValue(PdfName.Intern("Differences"), out PdfPrimitive? diffsPrim)
                && (diffsPrim is PdfReference ? resolver.Resolve(diffsPrim) : diffsPrim) is PdfArray diffs)
            {
                int current = 0;
                foreach (PdfPrimitive item in diffs)
                {
                    if (item is PdfInteger n)
                    {
                        current = (int)n.Value;
                    }
                    else if (item is PdfName nm && current >= 0 && current < 256)
                    {
                        enc[current] = nm.Value;
                        current++;
                    }
                }
            }
        }

        return enc;
    }

    // ── byte/text scanning helpers ────────────────────────────────────────

    private static int FindRdToken(byte[] data, int from)
    {
        // Accept "RD " or "-| " (the binary-data operators). Returns the index
        // of the space immediately preceding the binary stream.
        SkipSpaces(data, ref from);
        if (from + 2 <= data.Length && data[from] == 'R' && data[from + 1] == 'D' && from + 2 < data.Length && data[from + 2] == ' ')
        {
            return from + 2;
        }
        if (from + 2 <= data.Length && data[from] == '-' && data[from + 1] == '|' && from + 2 < data.Length && data[from + 2] == ' ')
        {
            return from + 2;
        }
        return -1;
    }

    private static int ReadIntAt(byte[] data, ref int pos, int fallback)
    {
        SkipSpaces(data, ref pos);
        int start = pos;
        bool neg = false;
        if (pos < data.Length && (data[pos] == '-' || data[pos] == '+'))
        {
            neg = data[pos] == '-';
            pos++;
        }
        int val = 0;
        bool any = false;
        while (pos < data.Length && data[pos] >= '0' && data[pos] <= '9')
        {
            val = val * 10 + (data[pos] - '0');
            pos++;
            any = true;
        }
        if (!any)
        {
            pos = start;
            return fallback;
        }
        return neg ? -val : val;
    }

    private static int ReadDecimal(string s, ref int pos)
    {
        while (pos < s.Length && char.IsWhiteSpace(s[pos]))
        {
            pos++;
        }
        int start = pos;
        while (pos < s.Length && (char.IsDigit(s[pos]) || s[pos] == '-'))
        {
            pos++;
        }
        return int.TryParse(s.AsSpan(start, pos - start), out int v) ? v : -1;
    }

    private static void SkipSpaces(byte[] data, ref int pos)
    {
        while (pos < data.Length && IsWhitespace(data[pos]))
        {
            pos++;
        }
    }

    private static int IndexOf(byte[] haystack, string needle, int start)
    {
        int n = needle.Length;
        for (int i = Math.Max(0, start); i <= haystack.Length - n; i++)
        {
            bool match = true;
            for (int j = 0; j < n; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }
            if (match)
            {
                return i;
            }
        }
        return -1;
    }

    private static bool IsWhitespace(byte b) => b is 0x20 or 0x09 or 0x0A or 0x0D or 0x0C or 0x00;

    private static bool IsHexDigit(byte b) => (b >= '0' && b <= '9') || (b >= 'a' && b <= 'f') || (b >= 'A' && b <= 'F');

    private static int HexVal(byte b)
    {
        if (b >= '0' && b <= '9') { return b - '0'; }
        if (b >= 'a' && b <= 'f') { return b - 'a' + 10; }
        if (b >= 'A' && b <= 'F') { return b - 'A' + 10; }
        return -1;
    }

    private static GraphicsPath ScalePath(GraphicsPath source, double scale)
    {
        GraphicsPath scaled = new();
        foreach (PathSegment seg in source.Segments)
        {
            switch (seg.Kind)
            {
                case PathSegmentKind.MoveTo:
                    scaled.MoveTo(seg.P0.X * scale, seg.P0.Y * scale);
                    break;
                case PathSegmentKind.LineTo:
                    scaled.LineTo(seg.P0.X * scale, seg.P0.Y * scale);
                    break;
                case PathSegmentKind.CubicBezierTo:
                    scaled.CubicBezierTo(
                        new PointF((float)(seg.P0.X * scale), (float)(seg.P0.Y * scale)),
                        new PointF((float)(seg.P1.X * scale), (float)(seg.P1.Y * scale)),
                        new PointF((float)(seg.P2.X * scale), (float)(seg.P2.Y * scale)));
                    break;
                case PathSegmentKind.ClosePath:
                    scaled.ClosePath();
                    break;
                default:
                    break;
            }
        }
        return scaled;
    }
}
