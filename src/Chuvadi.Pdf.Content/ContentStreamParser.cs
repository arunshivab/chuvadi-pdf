// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.8.2  — Content streams
//        PDF 32000-1:2008 §9.4    — Text objects and operators
//        PDF 32000-1:2008 §9.4.4  — Text positioning operators
//        PDF 32000-1:2008 §9.4.5  — Text showing operators
// PHASE: Phase 1 — Chuvadi.Pdf.Content
// Parses PDF content streams and drives text extraction.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Fonts;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Content;

/// <summary>
/// Parses a PDF content stream and extracts text fragments with their
/// approximate positions.
/// </summary>
/// <remarks>
/// A PDF content stream is a sequence of operands followed by an operator
/// keyword. This parser processes the operators relevant to text extraction:
///
/// Text object operators: BT (begin text), ET (end text).
/// Text state operators: Tf (set font/size), Tc, Tw, Tz, TL, Tr, Ts.
/// Text positioning: Td, TD, Tm, T*.
/// Text showing: Tj, TJ, ' (apostrophe), " (quote).
/// Graphics state: q (save), Q (restore), cm (concat matrix).
///
/// All other operators are parsed for their operands (so the operand stack
/// stays clean) but their effects are ignored.
///
/// PDF 32000-1:2008 §9.4 — Text objects and operators.
/// </remarks>
public sealed class ContentStreamParser
{
    private readonly IPdfObjectResolver _resolver;
    private readonly PdfDictionary? _resources;

    /// <summary>
    /// Initialises a <see cref="ContentStreamParser"/> for a page.
    /// </summary>
    /// <param name="resolver">Resolves indirect object references.</param>
    /// <param name="resources">The page Resources dictionary, or null.</param>
    public ContentStreamParser(IPdfObjectResolver resolver, PdfDictionary? resources)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _resources = resources;
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Parses one or more content streams and returns extracted text fragments.
    /// </summary>
    /// <param name="streams">
    /// The decoded content stream bytes. For pages with multiple content streams,
    /// concatenate them with a single space separator before calling.
    /// </param>
    /// <returns>
    /// A list of <see cref="TextFragment"/> objects in stream order.
    /// </returns>
    public List<TextFragment> Parse(byte[] streams)
    {
        if (streams is null)
        {
            throw new ArgumentNullException(nameof(streams));
        }

        List<TextFragment> fragments = new List<TextFragment>();
        Stack<GraphicsState> stateStack = new Stack<GraphicsState>();
        GraphicsState state = new GraphicsState();
        bool inTextObject = false;

        List<PdfPrimitive> operands = new List<PdfPrimitive>();

        using (MemoryStream ms = new MemoryStream(streams))
        using (PdfTokenizer tokenizer = new PdfTokenizer(ms, leaveOpen: false))
        {
            while (true)
            {
                PdfToken token = tokenizer.Read();

                if (token.IsEndOfStream)
                {
                    break;
                }

                // Operands accumulate until we hit a keyword/operator.
                // Arrays are assembled inline when we see ArrayStart.
                if (token.Type == PdfTokenType.ArrayStart)
                {
                    List<PdfPrimitive> arrayItems = new List<PdfPrimitive>();

                    while (true)
                    {
                        PdfToken inner = tokenizer.Read();

                        if (inner.IsEndOfStream || inner.Type == PdfTokenType.ArrayEnd)
                        {
                            break;
                        }

                        if (inner.Type != PdfTokenType.Keyword)
                        {
                            arrayItems.Add(TokenToPrimitive(inner));
                        }
                    }

                    operands.Add(new PdfArray(arrayItems));
                    continue;
                }

                if (token.Type != PdfTokenType.Keyword)
                {
                    operands.Add(TokenToPrimitive(token));
                    continue;
                }

                string op = token.RawText;

                switch (op)
                {
                    // ── Graphics state ────────────────────────────────────

                    case "q":
                        stateStack.Push(state.Clone());
                        break;

                    case "Q":
                        if (stateStack.Count > 0)
                        {
                            state = stateStack.Pop();
                        }
                        break;

                    case "cm":
                        if (operands.Count >= 6)
                        {
                            Matrix3x3 m = OperandsToMatrix(operands);
                            state.CurrentTransformationMatrix =
                                m.Multiply(state.CurrentTransformationMatrix);
                        }
                        break;

                    // ── Text object ───────────────────────────────────────

                    case "BT":
                        inTextObject = true;
                        state.TextMatrix = Matrix3x3.Identity;
                        state.TextLineMatrix = Matrix3x3.Identity;
                        break;

                    case "ET":
                        inTextObject = false;
                        break;

                    // ── Text state ────────────────────────────────────────

                    case "Tf":
                        if (operands.Count >= 2)
                        {
                            string fontName = GetString(operands, operands.Count - 2);
                            double fontSize = GetDouble(operands, operands.Count - 1);
                            state.Font = ResolveFont(fontName);
                            state.FontSize = fontSize;
                        }
                        break;

                    case "Tc":
                        if (operands.Count >= 1)
                        {
                            state.CharacterSpacing = GetDouble(operands, 0);
                        }
                        break;

                    case "Tw":
                        if (operands.Count >= 1)
                        {
                            state.WordSpacing = GetDouble(operands, 0);
                        }
                        break;

                    case "Tz":
                        if (operands.Count >= 1)
                        {
                            state.HorizontalScaling = GetDouble(operands, 0);
                        }
                        break;

                    case "Ts":
                        if (operands.Count >= 1)
                        {
                            state.TextRise = GetDouble(operands, 0);
                        }
                        break;

                    // ── Text positioning ──────────────────────────────────

                    case "Td":
                        if (inTextObject && operands.Count >= 2)
                        {
                            double tx = GetDouble(operands, 0);
                            double ty = GetDouble(operands, 1);
                            state.TextLineMatrix = state.TextLineMatrix.Translate(tx, ty);
                            state.TextMatrix = state.TextLineMatrix;
                        }
                        break;

                    case "TD":
                        if (inTextObject && operands.Count >= 2)
                        {
                            double tx = GetDouble(operands, 0);
                            double ty = GetDouble(operands, 1);
                            state.TextLeading = -ty;
                            state.TextLineMatrix = state.TextLineMatrix.Translate(tx, ty);
                            state.TextMatrix = state.TextLineMatrix;
                        }
                        break;

                    case "Tm":
                        if (inTextObject && operands.Count >= 6)
                        {
                            state.TextMatrix = OperandsToMatrix(operands);
                            state.TextLineMatrix = state.TextMatrix;
                        }
                        break;

                    case "T*":
                        if (inTextObject)
                        {
                            state.TextLineMatrix =
                                state.TextLineMatrix.Translate(0, -state.TextLeading);
                            state.TextMatrix = state.TextLineMatrix;
                        }
                        break;

                    // ── Text showing ──────────────────────────────────────

                    case "Tj":
                        if (inTextObject && operands.Count >= 1)
                        {
                            byte[] textBytes = GetBytes(operands, 0);
                            string text = state.Font.Decode(textBytes);
                            double x = state.TextMatrix.TranslationX;
                            double y = state.TextMatrix.TranslationY;
                            fragments.Add(new TextFragment(text, x, y, state.FontSize));
                            state.TextMatrix = AdvanceTextMatrix(
                                state.TextMatrix, text, state);
                        }
                        break;

                    case "'":
                        if (inTextObject)
                        {
                            // Move to next line then show text.
                            state.TextLineMatrix =
                                state.TextLineMatrix.Translate(0, -state.TextLeading);
                            state.TextMatrix = state.TextLineMatrix;

                            if (operands.Count >= 1)
                            {
                                byte[] textBytes = GetBytes(operands, 0);
                                string text = state.Font.Decode(textBytes);
                                double x = state.TextMatrix.TranslationX;
                                double y = state.TextMatrix.TranslationY;
                                fragments.Add(new TextFragment(text, x, y, state.FontSize));
                                state.TextMatrix = AdvanceTextMatrix(
                                    state.TextMatrix, text, state);
                            }
                        }
                        break;

                    case "\"":
                        if (inTextObject && operands.Count >= 3)
                        {
                            state.WordSpacing = GetDouble(operands, 0);
                            state.CharacterSpacing = GetDouble(operands, 1);
                            state.TextLineMatrix =
                                state.TextLineMatrix.Translate(0, -state.TextLeading);
                            state.TextMatrix = state.TextLineMatrix;
                            byte[] textBytes = GetBytes(operands, 2);
                            string text = state.Font.Decode(textBytes);
                            double x = state.TextMatrix.TranslationX;
                            double y = state.TextMatrix.TranslationY;
                            fragments.Add(new TextFragment(text, x, y, state.FontSize));
                            state.TextMatrix = AdvanceTextMatrix(
                                state.TextMatrix, text, state);
                        }
                        break;

                    case "TJ":
                        if (inTextObject && operands.Count >= 1)
                        {
                            PdfPrimitive last = operands[operands.Count - 1];

                            if (last is PdfArray array)
                            {
                                ProcessTJ(array, state, fragments);
                            }
                        }
                        break;

                    default:
                        // Unknown operator — discard operands and continue.
                        break;
                }

                operands.Clear();
            }
        }

        return fragments;
    }

    // ── TJ processing ─────────────────────────────────────────────────────

    private static void ProcessTJ(
        PdfArray array,
        GraphicsState state,
        List<TextFragment> fragments)
    {
        // TJ: array of strings and numbers.
        // Strings are shown; numbers adjust the text position (in thousandths
        // of a text space unit, applied as a negative horizontal displacement).
        // PDF 32000-1:2008 §9.4.5 — TJ operator.
        StringBuilder sb = new StringBuilder();
        double startX = state.TextMatrix.TranslationX;
        double startY = state.TextMatrix.TranslationY;

        for (int i = 0; i < array.Count; i++)
        {
            PdfPrimitive item = array[i];

            if (item is PdfString str)
            {
                string decoded = state.Font.Decode(str.Bytes);
                sb.Append(decoded);
                state.TextMatrix = AdvanceTextMatrix(state.TextMatrix, decoded, state);
            }
            else if (item is PdfInteger intItem)
            {
                // Negative displacement = move right (toward next glyph).
                // Threshold: if displacement > 200 thousandths, insert a space.
                double displacement = intItem.Value / 1000.0 * state.FontSize;

                if (intItem.Value < -200)
                {
                    sb.Append(' ');
                }

                // Adjust text matrix horizontally.
                state.TextMatrix = state.TextMatrix.Translate(-displacement, 0);
            }
            else if (item is PdfReal realItem)
            {
                double displacement = realItem.Value / 1000.0 * state.FontSize;

                if (realItem.Value < -200)
                {
                    sb.Append(' ');
                }

                state.TextMatrix = state.TextMatrix.Translate(-displacement, 0);
            }
        }

        if (sb.Length > 0)
        {
            fragments.Add(new TextFragment(sb.ToString(), startX, startY, state.FontSize));
        }
    }

    // ── Text matrix advance ───────────────────────────────────────────────

    private static Matrix3x3 AdvanceTextMatrix(
        Matrix3x3 matrix,
        string text,
        GraphicsState state)
    {
        // Approximate advance: FontSize * HorizontalScaling/100 per character.
        // A proper implementation would look up glyph widths from the font.
        // For Phase 1, average advance gives good results for most fonts.
        double advance = text.Length * state.FontSize * (state.HorizontalScaling / 100.0) * 0.6;
        return matrix.Translate(advance + state.CharacterSpacing * text.Length, 0);
    }

    // ── Font resolution ───────────────────────────────────────────────────

    private PdfFont ResolveFont(string fontName)
    {
        if (_resources is null)
        {
            return PdfFont.Default();
        }

        if (string.IsNullOrEmpty(fontName))
        {
            return PdfFont.Default();
        }

        PdfDictionary? fontDict = _resources.GetDictionary(PdfName.Font);

        if (fontDict is null)
        {
            return PdfFont.Default();
        }

        PdfName fontKey = PdfName.Intern(fontName);

        if (!fontDict.TryGetValue(fontKey, out PdfPrimitive? fontRef))
        {
            return PdfFont.Default();
        }

        PdfPrimitive resolved = _resolver.Resolve(fontRef);

        if (resolved is not PdfDictionary specificFontDict)
        {
            return PdfFont.Default();
        }

        return PdfFont.FromDictionary(specificFontDict, _resolver);
    }

    // ── Operand helpers ───────────────────────────────────────────────────

    private static PdfPrimitive TokenToPrimitive(PdfToken token)
    {
        switch (token.Type)
        {
            case PdfTokenType.Integer:
                if (int.TryParse(token.RawText, NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out int iv))
                {
                    return new PdfInteger(iv);
                }
                return new PdfInteger(0);

            case PdfTokenType.Real:
                if (double.TryParse(token.RawText, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double rv))
                {
                    return new PdfReal(rv);
                }
                return new PdfReal(0);

            case PdfTokenType.Name:
                return PdfName.FromRawBytes(token.RawBytes);

            case PdfTokenType.LiteralString:
                return ParseLiteralString(token.RawBytes);

            case PdfTokenType.HexString:
                return ParseHexString(token.RawBytes);

            case PdfTokenType.True:
                return PdfBoolean.True;

            case PdfTokenType.False:
                return PdfBoolean.False;

            case PdfTokenType.ArrayStart:
            case PdfTokenType.ArrayEnd:
            case PdfTokenType.DictionaryStart:
            case PdfTokenType.DictionaryEnd:
                return PdfNull.Value;

            default:
                return PdfNull.Value;
        }
    }

    private static PdfString ParseLiteralString(byte[] raw)
    {
        int start = (raw.Length > 0 && raw[0] == 40) ? 1 : 0;
        int end = (raw.Length > start && raw[raw.Length - 1] == 41)
            ? raw.Length - 1 : raw.Length;
        return new PdfString(raw.AsSpan()[start..end]);
    }

    private static PdfString ParseHexString(byte[] raw)
    {
        int start = (raw.Length > 0 && raw[0] == 60) ? 1 : 0;
        int end = (raw.Length > start && raw[raw.Length - 1] == 62)
            ? raw.Length - 1 : raw.Length;
        List<byte> decoded = new List<byte>();
        int highNibble = -1;

        for (int i = start; i < end; i++)
        {
            byte b = raw[i];
            if (b == 32 || b == 9 || b == 10 || b == 13) { continue; }
            int n = HexNibble(b);
            if (n < 0) { continue; }
            if (highNibble < 0) { highNibble = n; }
            else { decoded.Add((byte)((highNibble << 4) | n)); highNibble = -1; }
        }

        if (highNibble >= 0) { decoded.Add((byte)(highNibble << 4)); }
        return new PdfString([.. decoded]);
    }

    private static int HexNibble(byte b)
    {
        if (b >= 48 && b <= 57) { return b - 48; }
        if (b >= 65 && b <= 70) { return b - 55; }
        if (b >= 97 && b <= 102) { return b - 87; }
        return -1;
    }

    private static double GetDouble(List<PdfPrimitive> ops, int index)
    {
        if (index < 0 || index >= ops.Count) { return 0; }

        if (ops[index] is PdfInteger i) { return i.Value; }
        if (ops[index] is PdfReal r) { return r.Value; }
        return 0;
    }

    private static string GetString(List<PdfPrimitive> ops, int index)
    {
        if (index < 0 || index >= ops.Count) { return string.Empty; }
        if (ops[index] is PdfName n) { return n.Value; }
        if (ops[index] is PdfString s) { return s.ToTextString(); }
        return string.Empty;
    }

    private static byte[] GetBytes(List<PdfPrimitive> ops, int index)
    {
        if (index < 0 || index >= ops.Count) { return []; }
        if (ops[index] is PdfString s) { return s.Bytes; }
        return [];
    }

    private static Matrix3x3 OperandsToMatrix(List<PdfPrimitive> ops)
    {
        int n = ops.Count;
        return new Matrix3x3(
            GetDouble(ops, n - 6),
            GetDouble(ops, n - 5),
            GetDouble(ops, n - 4),
            GetDouble(ops, n - 3),
            GetDouble(ops, n - 2),
            GetDouble(ops, n - 1));
    }
}
