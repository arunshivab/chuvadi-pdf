// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  Adobe Technical Note #5177 — Type 2 Charstring Format
// PHASE: Phase 2.1 — CFF parser

using System.Collections.Generic;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Fonts.Rendering;

/// <summary>
/// Interprets Type 2 charstrings — the operator language used inside CFF
/// font programs — to produce glyph outlines.
/// </summary>
/// <remarks>
/// <para>
/// Operators are encoded as single bytes (0–31) or two-byte sequences
/// (12 + extension). Operands are integers (one to five bytes) or fixed-point
/// reals. The interpreter maintains an operand stack of up to 48 entries,
/// a current point, and a transient hint state.
/// </para>
/// <para>
/// Subroutine calls use a biased index: subrs 0–31 are biased by -107,
/// subrs 32–1023 by -1131, and so on (per the spec). The bias depends on
/// the size of the subr INDEX.
/// </para>
/// </remarks>
internal sealed class Type2Interpreter
{
    private readonly List<byte[]> _gsubrs;
    private readonly List<byte[]> _lsubrs;
    private readonly int _defaultWidthX;
    private readonly int _nominalWidthX;
    private readonly List<double> _stack = new();
    private double _x;
    private double _y;
    private bool _widthRead;
    private int _hintCount;
    private bool _contourOpen;
    private Path _path = new();

    internal Type2Interpreter(List<byte[]> globalSubrs, List<byte[]> localSubrs,
        int defaultWidthX, int nominalWidthX)
    {
        _gsubrs = globalSubrs;
        _lsubrs = localSubrs;
        _defaultWidthX = defaultWidthX;
        _nominalWidthX = nominalWidthX;
        AdvanceWidth = defaultWidthX;
    }

    internal int AdvanceWidth { get; private set; }

    internal Path Run(byte[] charstring)
    {
        _path = new Path();
        Execute(charstring);
        CloseOpenContour();
        return _path;
    }

    // Type 2 charstring contours are implicitly closed. A new moveto closes the
    // contour in progress, and the glyph's final contour is closed at endchar
    // (or here, if the charstring ends without one). Emitting the ClosePath so
    // each contour carries its closing edge matches the TrueType outline path
    // and lets the fill rasterizer treat each contour as a separate sub-path.
    private void CloseOpenContour()
    {
        if (_contourOpen)
        {
            _path.ClosePath();
            _contourOpen = false;
        }
    }

    private bool Execute(byte[] cs)
    {
        // Returns true when endchar reached (signal to terminate the entire glyph).
        int i = 0;
        while (i < cs.Length)
        {
            byte b = cs[i];
            if (b <= 31)
            {
                int op = b == 12 ? (12 << 8) | cs[i + 1] : b;
                i += b == 12 ? 2 : 1;
                if (HandleOp(op)) { return true; }
                // hintmask/cntrmask consume hintCount/8 extra bytes
                if (op == 19 || op == 20)
                {
                    // hint counts may include implicit stem operators
                    int implicitStems = _stack.Count / 2;
                    _hintCount += implicitStems;
                    _stack.Clear();
                    int maskBytes = (_hintCount + 7) / 8;
                    i += maskBytes;
                }
            }
            else if (b == 28)
            {
                short v = (short)((cs[i + 1] << 8) | cs[i + 2]);
                _stack.Add(v); i += 3;
            }
            else if (b >= 32 && b <= 246)
            {
                _stack.Add(b - 139); i++;
            }
            else if (b >= 247 && b <= 250)
            {
                _stack.Add((b - 247) * 256 + cs[i + 1] + 108); i += 2;
            }
            else if (b >= 251 && b <= 254)
            {
                _stack.Add(-((b - 251) * 256) - cs[i + 1] - 108); i += 2;
            }
            else if (b == 255)
            {
                int intPart = (cs[i + 1] << 24) | (cs[i + 2] << 16) | (cs[i + 3] << 8) | cs[i + 4];
                double v = intPart / 65536.0;
                _stack.Add(v); i += 5;
            }
            else { i++; }
        }
        return false;
    }

    private bool HandleOp(int op)
    {
        switch (op)
        {
            case 1:   // hstem
            case 3:   // vstem
            case 18:  // hstemhm
            case 23:  // vstemhm
                {
                    ConsumeWidth();
                    _hintCount += _stack.Count / 2;
                    _stack.Clear();
                    return false;
                }
            case 21: // rmoveto
                {
                    ConsumeWidth();
                    if (_stack.Count >= 2)
                    {
                        CloseOpenContour();
                        _x += _stack[^2]; _y += _stack[^1];
                        _path.MoveTo(_x, _y);
                        _contourOpen = true;
                    }
                    _stack.Clear();
                    return false;
                }
            case 22: // hmoveto
                {
                    ConsumeWidth();
                    if (_stack.Count >= 1)
                    {
                        CloseOpenContour();
                        _x += _stack[^1];
                        _path.MoveTo(_x, _y);
                        _contourOpen = true;
                    }
                    _stack.Clear();
                    return false;
                }
            case 4:  // vmoveto
                {
                    ConsumeWidth();
                    if (_stack.Count >= 1)
                    {
                        CloseOpenContour();
                        _y += _stack[^1];
                        _path.MoveTo(_x, _y);
                        _contourOpen = true;
                    }
                    _stack.Clear();
                    return false;
                }
            case 5:  // rlineto
                {
                    for (int k = 0; k + 1 < _stack.Count; k += 2)
                    {
                        _x += _stack[k]; _y += _stack[k + 1];
                        _path.LineTo(_x, _y);
                    }
                    _stack.Clear();
                    return false;
                }
            case 6:  // hlineto
                {
                    bool horiz = true;
                    for (int k = 0; k < _stack.Count; k++)
                    {
                        if (horiz) { _x += _stack[k]; } else { _y += _stack[k]; }
                        _path.LineTo(_x, _y);
                        horiz = !horiz;
                    }
                    _stack.Clear();
                    return false;
                }
            case 7:  // vlineto
                {
                    bool vert = true;
                    for (int k = 0; k < _stack.Count; k++)
                    {
                        if (vert) { _y += _stack[k]; } else { _x += _stack[k]; }
                        _path.LineTo(_x, _y);
                        vert = !vert;
                    }
                    _stack.Clear();
                    return false;
                }
            case 8:  // rrcurveto
                {
                    for (int k = 0; k + 5 < _stack.Count; k += 6)
                    {
                        double x1 = _x + _stack[k];
                        double y1 = _y + _stack[k + 1];
                        double x2 = x1 + _stack[k + 2];
                        double y2 = y1 + _stack[k + 3];
                        double x3 = x2 + _stack[k + 4];
                        double y3 = y2 + _stack[k + 5];
                        _path.CubicBezierTo(new PointF((float)x1, (float)y1),
                            new PointF((float)x2, (float)y2),
                            new PointF((float)x3, (float)y3));
                        _x = x3; _y = y3;
                    }
                    _stack.Clear();
                    return false;
                }
            case 24: // rcurveline
                {
                    int curves = (_stack.Count - 2) / 6;
                    int idx = 0;
                    for (int c = 0; c < curves; c++)
                    {
                        double x1 = _x + _stack[idx];
                        double y1 = _y + _stack[idx + 1];
                        double x2 = x1 + _stack[idx + 2];
                        double y2 = y1 + _stack[idx + 3];
                        double x3 = x2 + _stack[idx + 4];
                        double y3 = y2 + _stack[idx + 5];
                        _path.CubicBezierTo(new PointF((float)x1, (float)y1),
                            new PointF((float)x2, (float)y2),
                            new PointF((float)x3, (float)y3));
                        _x = x3; _y = y3;
                        idx += 6;
                    }
                    if (idx + 1 < _stack.Count)
                    {
                        _x += _stack[idx]; _y += _stack[idx + 1];
                        _path.LineTo(_x, _y);
                    }
                    _stack.Clear();
                    return false;
                }
            case 25: // rlinecurve
                {
                    int lines = (_stack.Count - 6) / 2;
                    int idx = 0;
                    for (int l = 0; l < lines; l++)
                    {
                        _x += _stack[idx]; _y += _stack[idx + 1];
                        _path.LineTo(_x, _y);
                        idx += 2;
                    }
                    if (idx + 5 < _stack.Count)
                    {
                        double x1 = _x + _stack[idx];
                        double y1 = _y + _stack[idx + 1];
                        double x2 = x1 + _stack[idx + 2];
                        double y2 = y1 + _stack[idx + 3];
                        double x3 = x2 + _stack[idx + 4];
                        double y3 = y2 + _stack[idx + 5];
                        _path.CubicBezierTo(new PointF((float)x1, (float)y1),
                            new PointF((float)x2, (float)y2),
                            new PointF((float)x3, (float)y3));
                        _x = x3; _y = y3;
                    }
                    _stack.Clear();
                    return false;
                }
            case 27: // hhcurveto
            case 26: // vvcurveto
                {
                    bool horiz = op == 27;
                    int idx = 0;
                    double initialOff = 0;
                    if (_stack.Count % 4 != 0)
                    {
                        initialOff = _stack[0]; idx = 1;
                    }
                    while (idx + 3 < _stack.Count)
                    {
                        double x1, y1, x2, y2, x3, y3;
                        if (horiz)
                        {
                            x1 = _x + _stack[idx];
                            y1 = _y + initialOff;
                            x2 = x1 + _stack[idx + 1];
                            y2 = y1 + _stack[idx + 2];
                            x3 = x2 + _stack[idx + 3];
                            y3 = y2;
                        }
                        else
                        {
                            x1 = _x + initialOff;
                            y1 = _y + _stack[idx];
                            x2 = x1 + _stack[idx + 1];
                            y2 = y1 + _stack[idx + 2];
                            x3 = x2;
                            y3 = y2 + _stack[idx + 3];
                        }
                        _path.CubicBezierTo(new PointF((float)x1, (float)y1),
                            new PointF((float)x2, (float)y2),
                            new PointF((float)x3, (float)y3));
                        _x = x3; _y = y3;
                        initialOff = 0;
                        idx += 4;
                    }
                    _stack.Clear();
                    return false;
                }
            case 30: // vhcurveto
            case 31: // hvcurveto
                {
                    bool startVert = op == 30;
                    int idx = 0;
                    while (idx + 3 < _stack.Count)
                    {
                        double x1, y1, x2, y2, x3, y3;
                        double finalOff = 0;
                        bool hasFinal = idx + 4 < _stack.Count && (_stack.Count - idx) % 4 != 0
                            && idx + 4 == _stack.Count - 1;
                        if (hasFinal) { finalOff = _stack[idx + 4]; }
                        if (startVert)
                        {
                            x1 = _x;
                            y1 = _y + _stack[idx];
                            x2 = x1 + _stack[idx + 1];
                            y2 = y1 + _stack[idx + 2];
                            x3 = x2 + _stack[idx + 3];
                            y3 = y2 + (hasFinal ? finalOff : 0);
                        }
                        else
                        {
                            x1 = _x + _stack[idx];
                            y1 = _y;
                            x2 = x1 + _stack[idx + 1];
                            y2 = y1 + _stack[idx + 2];
                            x3 = x2 + (hasFinal ? finalOff : 0);
                            y3 = y2 + _stack[idx + 3];
                        }
                        _path.CubicBezierTo(new PointF((float)x1, (float)y1),
                            new PointF((float)x2, (float)y2),
                            new PointF((float)x3, (float)y3));
                        _x = x3; _y = y3;
                        idx += 4 + (hasFinal ? 1 : 0);
                        startVert = !startVert;
                    }
                    _stack.Clear();
                    return false;
                }
            case 10: // callsubr
                {
                    if (_stack.Count > 0)
                    {
                        int subrIdx = (int)_stack[^1];
                        _stack.RemoveAt(_stack.Count - 1);
                        int bias = ComputeBias(_lsubrs.Count);
                        int idx = subrIdx + bias;
                        if (idx >= 0 && idx < _lsubrs.Count)
                        {
                            if (Execute(_lsubrs[idx])) { return true; }
                        }
                    }
                    return false;
                }
            case 29: // callgsubr
                {
                    if (_stack.Count > 0)
                    {
                        int subrIdx = (int)_stack[^1];
                        _stack.RemoveAt(_stack.Count - 1);
                        int bias = ComputeBias(_gsubrs.Count);
                        int idx = subrIdx + bias;
                        if (idx >= 0 && idx < _gsubrs.Count)
                        {
                            if (Execute(_gsubrs[idx])) { return true; }
                        }
                    }
                    return false;
                }
            case 11: // return
                return false;
            case 14: // endchar
                {
                    ConsumeWidth();
                    CloseOpenContour();
                    _stack.Clear();
                    return true;
                }
            case 19: // hintmask
            case 20: // cntrmask
                return false;   // mask bytes handled in caller
            case (12 << 8) | 34: // hflex
                {
                    if (_stack.Count >= 7)
                    {
                        double startY = _y;
                        double x1 = _x + _stack[0];
                        double y1 = _y;
                        double x2 = x1 + _stack[1];
                        double y2 = y1 + _stack[2];
                        double x3 = x2 + _stack[3];
                        double y3 = y2;
                        double x4 = x3 + _stack[4];
                        double y4 = y2;
                        double x5 = x4 + _stack[5];
                        double y5 = startY;
                        double x6 = x5 + _stack[6];
                        double y6 = startY;
                        EmitFlex(x1, y1, x2, y2, x3, y3, x4, y4, x5, y5, x6, y6);
                    }
                    _stack.Clear();
                    return false;
                }
            case (12 << 8) | 35: // flex
                {
                    if (_stack.Count >= 12)
                    {
                        double x1 = _x + _stack[0];
                        double y1 = _y + _stack[1];
                        double x2 = x1 + _stack[2];
                        double y2 = y1 + _stack[3];
                        double x3 = x2 + _stack[4];
                        double y3 = y2 + _stack[5];
                        double x4 = x3 + _stack[6];
                        double y4 = y3 + _stack[7];
                        double x5 = x4 + _stack[8];
                        double y5 = y4 + _stack[9];
                        double x6 = x5 + _stack[10];
                        double y6 = y5 + _stack[11];
                        EmitFlex(x1, y1, x2, y2, x3, y3, x4, y4, x5, y5, x6, y6);
                    }
                    _stack.Clear();
                    return false;
                }
            case (12 << 8) | 36: // hflex1
                {
                    if (_stack.Count >= 9)
                    {
                        double startY = _y;
                        double x1 = _x + _stack[0];
                        double y1 = _y + _stack[1];
                        double x2 = x1 + _stack[2];
                        double y2 = y1 + _stack[3];
                        double x3 = x2 + _stack[4];
                        double y3 = y2;
                        double x4 = x3 + _stack[5];
                        double y4 = y2;
                        double x5 = x4 + _stack[6];
                        double y5 = y4 + _stack[7];
                        double x6 = x5 + _stack[8];
                        double y6 = startY;
                        EmitFlex(x1, y1, x2, y2, x3, y3, x4, y4, x5, y5, x6, y6);
                    }
                    _stack.Clear();
                    return false;
                }
            case (12 << 8) | 37: // flex1
                {
                    if (_stack.Count >= 11)
                    {
                        double startX = _x;
                        double startY = _y;
                        double dxSum = _stack[0] + _stack[2] + _stack[4] + _stack[6] + _stack[8];
                        double dySum = _stack[1] + _stack[3] + _stack[5] + _stack[7] + _stack[9];
                        double x1 = _x + _stack[0];
                        double y1 = _y + _stack[1];
                        double x2 = x1 + _stack[2];
                        double y2 = y1 + _stack[3];
                        double x3 = x2 + _stack[4];
                        double y3 = y2 + _stack[5];
                        double x4 = x3 + _stack[6];
                        double y4 = y3 + _stack[7];
                        double x5 = x4 + _stack[8];
                        double y5 = y4 + _stack[9];
                        double d6 = _stack[10];
                        double absDx = dxSum < 0 ? -dxSum : dxSum;
                        double absDy = dySum < 0 ? -dySum : dySum;
                        double x6;
                        double y6;
                        if (absDx > absDy)
                        {
                            x6 = x5 + d6;
                            y6 = startY;
                        }
                        else
                        {
                            x6 = startX;
                            y6 = y5 + d6;
                        }
                        EmitFlex(x1, y1, x2, y2, x3, y3, x4, y4, x5, y5, x6, y6);
                    }
                    _stack.Clear();
                    return false;
                }
            default:
                _stack.Clear();
                return false;
        }
    }

    private void EmitFlex(
        double x1, double y1, double x2, double y2, double x3, double y3,
        double x4, double y4, double x5, double y5, double x6, double y6)
    {
        _path.CubicBezierTo(
            new PointF((float)x1, (float)y1),
            new PointF((float)x2, (float)y2),
            new PointF((float)x3, (float)y3));
        _path.CubicBezierTo(
            new PointF((float)x4, (float)y4),
            new PointF((float)x5, (float)y5),
            new PointF((float)x6, (float)y6));
        _x = x6;
        _y = y6;
    }

    private void ConsumeWidth()
    {
        if (_widthRead) { return; }
        _widthRead = true;
        // If the operand stack has an odd count for a movement op (or any count
        // for hstem etc with a width), the bottom entry is the width offset.
        if (_stack.Count > 0 && _stack.Count % 2 != 0)
        {
            AdvanceWidth = _nominalWidthX + (int)_stack[0];
            _stack.RemoveAt(0);
        }
        else
        {
            AdvanceWidth = _defaultWidthX;
        }
    }

    private static int ComputeBias(int subrCount)
    {
        if (subrCount < 1240) { return 107; }
        if (subrCount < 33900) { return 1131; }
        return 32768;
    }
}
