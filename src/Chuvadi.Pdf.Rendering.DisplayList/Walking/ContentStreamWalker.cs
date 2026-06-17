// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.2 — Content streams and operators (Annex A
//        operator summary)
// PHASE: Phase 2.8 — DisplayList consolidation (one walker, two sinks)
// Tokenises a content stream and dispatches typed operator events to a sink.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Rendering.Walking;

/// <summary>
/// The shared content-stream interpreter front-end: reads tokens, gathers
/// operands (including inline arrays for TJ and d), parses operand values,
/// and dispatches one typed event per operator to an
/// <see cref="IContentOperatorSink"/>.
/// </summary>
/// <remarks>
/// <para>
/// The walker holds no interpretation state. Graphics state, paths, text
/// matrices, fonts, clipping, and emission all live in the sink, so the two
/// display-list builders keep their exact pre-consolidation numerics. Form
/// XObject recursion is likewise a sink concern: the sink resolves the
/// XObject and calls <see cref="Walk"/> again on the form's bytes.
/// </para>
/// <para>
/// Parsing is tolerant: malformed numbers read as 0, and operators with too
/// few operands are skipped — matching both pre-consolidation builders'
/// permissive handling of real-world streams.
/// </para>
/// </remarks>
internal static class ContentStreamWalker
{
    /// <summary>
    /// Walks decoded content-stream bytes, dispatching each operator to the
    /// sink. Empty content dispatches nothing.
    /// </summary>
    internal static void Walk(byte[] content, IContentOperatorSink sink)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(sink);

        if (content.Length == 0)
        {
            return;
        }

        using MemoryStream ms = new(content);
        using PdfTokenizer tokenizer = new(ms);
        List<PdfToken> operands = new();

        while (true)
        {
            PdfToken token = tokenizer.Read();
            if (token.IsEndOfStream)
            {
                break;
            }

            if (token.Type == PdfTokenType.ArrayStart)
            {
                // Capture the array inline (TJ text arrays, d dash arrays),
                // bracketed by start/end marker tokens.
                operands.Add(new PdfToken(PdfTokenType.ArrayStart, Array.Empty<byte>(), 0));
                while (true)
                {
                    PdfToken inner = tokenizer.Read();
                    if (inner.IsEndOfStream || inner.Type == PdfTokenType.ArrayEnd)
                    {
                        break;
                    }
                    operands.Add(inner);
                }
                operands.Add(new PdfToken(PdfTokenType.ArrayEnd, Array.Empty<byte>(), 0));
                continue;
            }

            if (token.Type != PdfTokenType.Keyword)
            {
                operands.Add(token);
                continue;
            }

            Dispatch(token.RawText, operands, sink);
            operands.Clear();
        }
    }

    private static void Dispatch(string op, List<PdfToken> operands, IContentOperatorSink sink)
    {
        switch (op)
        {
            // ── Graphics state ────────────────────────────────────────────
            case "q":
                sink.SaveState();
                break;
            case "Q":
                sink.RestoreState();
                break;
            case "cm":
                if (operands.Count >= 6)
                {
                    sink.ConcatMatrix(
                        Number(operands[0]), Number(operands[1]), Number(operands[2]),
                        Number(operands[3]), Number(operands[4]), Number(operands[5]));
                }
                break;
            case "w":
                if (operands.Count > 0)
                {
                    sink.SetLineWidth(Number(operands[0]));
                }
                break;
            case "J":
                if (operands.Count > 0)
                {
                    sink.SetLineCap((int)Integer(operands[0]));
                }
                break;
            case "j":
                if (operands.Count > 0)
                {
                    sink.SetLineJoin((int)Integer(operands[0]));
                }
                break;
            case "M":
                if (operands.Count > 0)
                {
                    sink.SetMiterLimit(Number(operands[0]));
                }
                break;
            case "d":
                DispatchDash(operands, sink);
                break;

            // ── Colour ────────────────────────────────────────────────────
            case "g":
                if (operands.Count > 0)
                {
                    sink.SetFillGray(Number(operands[0]));
                }
                break;
            case "G":
                if (operands.Count > 0)
                {
                    sink.SetStrokeGray(Number(operands[0]));
                }
                break;
            case "rg":
                if (operands.Count >= 3)
                {
                    sink.SetFillRgb(Number(operands[0]), Number(operands[1]), Number(operands[2]));
                }
                break;
            case "RG":
                if (operands.Count >= 3)
                {
                    sink.SetStrokeRgb(Number(operands[0]), Number(operands[1]), Number(operands[2]));
                }
                break;
            case "k":
                if (operands.Count >= 4)
                {
                    sink.SetFillCmyk(
                        Number(operands[0]), Number(operands[1]),
                        Number(operands[2]), Number(operands[3]));
                }
                break;
            case "K":
                if (operands.Count >= 4)
                {
                    sink.SetStrokeCmyk(
                        Number(operands[0]), Number(operands[1]),
                        Number(operands[2]), Number(operands[3]));
                }
                break;
            case "cs":
                if (operands.Count > 0)
                {
                    sink.SetColorSpace(ContentStrings.ExtractName(operands[0]), stroke: false);
                }
                break;
            case "CS":
                if (operands.Count > 0)
                {
                    sink.SetColorSpace(ContentStrings.ExtractName(operands[0]), stroke: true);
                }
                break;
            case "sc":
            case "scn":
                DispatchColorN(operands, sink, stroke: false);
                break;
            case "SC":
            case "SCN":
                DispatchColorN(operands, sink, stroke: true);
                break;

            // ── Path construction ─────────────────────────────────────────
            case "m":
                if (operands.Count >= 2)
                {
                    sink.MoveTo(Number(operands[0]), Number(operands[1]));
                }
                break;
            case "l":
                if (operands.Count >= 2)
                {
                    sink.LineTo(Number(operands[0]), Number(operands[1]));
                }
                break;
            case "c":
                if (operands.Count >= 6)
                {
                    sink.CurveTo(
                        Number(operands[0]), Number(operands[1]),
                        Number(operands[2]), Number(operands[3]),
                        Number(operands[4]), Number(operands[5]));
                }
                break;
            case "v":
                if (operands.Count >= 4)
                {
                    sink.CurveToV(
                        Number(operands[0]), Number(operands[1]),
                        Number(operands[2]), Number(operands[3]));
                }
                break;
            case "y":
                if (operands.Count >= 4)
                {
                    sink.CurveToY(
                        Number(operands[0]), Number(operands[1]),
                        Number(operands[2]), Number(operands[3]));
                }
                break;
            case "h":
                sink.ClosePath();
                break;
            case "re":
                if (operands.Count >= 4)
                {
                    sink.AppendRectangle(
                        Number(operands[0]), Number(operands[1]),
                        Number(operands[2]), Number(operands[3]));
                }
                break;

            // ── Path painting ─────────────────────────────────────────────
            case "f":
            case "F":
                sink.FillPath(evenOdd: false);
                break;
            case "f*":
                sink.FillPath(evenOdd: true);
                break;
            case "S":
                sink.StrokePath(closeFirst: false);
                break;
            case "s":
                sink.StrokePath(closeFirst: true);
                break;
            case "B":
                sink.FillAndStrokePath(evenOdd: false, closeFirst: false);
                break;
            case "B*":
                sink.FillAndStrokePath(evenOdd: true, closeFirst: false);
                break;
            case "b":
                sink.FillAndStrokePath(evenOdd: false, closeFirst: true);
                break;
            case "b*":
                sink.FillAndStrokePath(evenOdd: true, closeFirst: true);
                break;
            case "n":
                sink.EndPath();
                break;
            case "W":
                sink.SetClip(evenOdd: false);
                break;
            case "W*":
                sink.SetClip(evenOdd: true);
                break;

            // ── Text objects, state, and positioning ──────────────────────
            case "BT":
                sink.BeginText();
                break;
            case "ET":
                sink.EndText();
                break;
            case "Tf":
                if (operands.Count >= 2)
                {
                    sink.SetFont(ContentStrings.ExtractName(operands[0]), Number(operands[1]));
                }
                break;
            case "Td":
                if (operands.Count >= 2)
                {
                    sink.TextMove(Number(operands[0]), Number(operands[1]));
                }
                break;
            case "TD":
                if (operands.Count >= 2)
                {
                    sink.TextMoveWithLeading(Number(operands[0]), Number(operands[1]));
                }
                break;
            case "Tm":
                if (operands.Count >= 6)
                {
                    sink.SetTextMatrix(
                        Number(operands[0]), Number(operands[1]), Number(operands[2]),
                        Number(operands[3]), Number(operands[4]), Number(operands[5]));
                }
                break;
            case "T*":
                sink.TextNextLine();
                break;
            case "Tc":
                if (operands.Count > 0)
                {
                    sink.SetCharSpacing(Number(operands[0]));
                }
                break;
            case "Tw":
                if (operands.Count > 0)
                {
                    sink.SetWordSpacing(Number(operands[0]));
                }
                break;
            case "Tz":
                if (operands.Count > 0)
                {
                    sink.SetHorizontalScaling(Number(operands[0]));
                }
                break;
            case "TL":
                if (operands.Count > 0)
                {
                    sink.SetLeading(Number(operands[0]));
                }
                break;
            case "Tr":
                if (operands.Count > 0)
                {
                    sink.SetTextRenderingMode((int)Integer(operands[0]));
                }
                break;
            case "Ts":
                if (operands.Count > 0)
                {
                    sink.SetTextRise(Number(operands[0]));
                }
                break;

            // ── Text showing ──────────────────────────────────────────────
            case "Tj":
                if (operands.Count > 0)
                {
                    sink.ShowText(ContentStrings.ExtractStringBytes(operands[0]));
                }
                break;
            case "'":
                if (operands.Count > 0)
                {
                    sink.MoveNextLineShowText(ContentStrings.ExtractStringBytes(operands[0]));
                }
                break;
            case "\"":
                if (operands.Count >= 3)
                {
                    sink.SetSpacingMoveNextLineShowText(
                        Number(operands[0]),
                        Number(operands[1]),
                        ContentStrings.ExtractStringBytes(operands[2]));
                }
                break;
            case "TJ":
                sink.ShowTextArray(ParseTextArray(operands));
                break;

            // ── XObjects ──────────────────────────────────────────────────
            case "Do":
                if (operands.Count > 0)
                {
                    string name = ContentStrings.ExtractName(operands[0]);
                    if (name.Length > 0)
                    {
                        sink.InvokeXObject(name);
                    }
                }
                break;

            // ── ExtGState ─────────────────────────────────────────────────
            case "gs":
                if (operands.Count > 0)
                {
                    string gsName = ContentStrings.ExtractName(operands[0]);
                    if (gsName.Length > 0)
                    {
                        sink.ApplyExtGState(gsName);
                    }
                }
                break;

            // ── Recognised no-ops (both builders ignore these) ────────────
            case "i":      // flatness — visual hint
            case "ri":     // rendering intent
            case "sh":     // shading paint
            case "BMC":    // marked content
            case "BDC":
            case "EMC":
                break;

            default:
                sink.UnknownOperator(op);
                break;
        }
    }

    private static void DispatchDash(List<PdfToken> operands, IContentOperatorSink sink)
    {
        // d [dashArray] phase — the phase is the last operand; the array
        // body sits between the captured start/end markers.
        if (operands.Count < 1)
        {
            return;
        }

        int phaseIndex = operands.Count - 1;
        double phase = Number(operands[phaseIndex]);

        int start = 0;
        int end = phaseIndex;
        while (start < end && operands[start].Type == PdfTokenType.ArrayStart)
        {
            start++;
        }
        while (end > start && operands[end - 1].Type == PdfTokenType.ArrayEnd)
        {
            end--;
        }

        double[] dashes = new double[end - start];
        for (int i = 0; i < dashes.Length; i++)
        {
            dashes[i] = Number(operands[start + i]);
        }

        sink.SetDashPattern(dashes, phase);
    }

    private static void DispatchColorN(
        List<PdfToken> operands, IContentOperatorSink sink, bool stroke)
    {
        List<double> components = new(operands.Count);
        bool hasName = false;

        foreach (PdfToken t in operands)
        {
            if (t.Type == PdfTokenType.Integer || t.Type == PdfTokenType.Real)
            {
                components.Add(Number(t));
            }
            else if (t.Type == PdfTokenType.Name)
            {
                hasName = true;
            }
        }

        sink.SetColorN(components.ToArray(), hasName, stroke);
    }

    private static IReadOnlyList<TextArrayElement> ParseTextArray(List<PdfToken> operands)
    {
        List<TextArrayElement> elements = new(operands.Count);
        foreach (PdfToken t in operands)
        {
            if (t.Type == PdfTokenType.LiteralString || t.Type == PdfTokenType.HexString)
            {
                elements.Add(TextArrayElement.ForText(ContentStrings.ExtractStringBytes(t)));
            }
            else if (t.Type == PdfTokenType.Integer || t.Type == PdfTokenType.Real)
            {
                elements.Add(TextArrayElement.ForAdjustment(Number(t)));
            }
        }
        return elements;
    }

    // Tolerant numeric parsing: malformed values read as 0, matching the
    // permissive behaviour both pre-consolidation builders converged on.
    private static double Number(PdfToken token)
    {
        return double.TryParse(
            token.RawText, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
            ? v
            : 0.0;
    }

    private static long Integer(PdfToken token)
    {
        if (long.TryParse(
            token.RawText, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v))
        {
            return v;
        }

        // PDFs occasionally write integer-valued operators as reals (1.0).
        return (long)Number(token);
    }
}
