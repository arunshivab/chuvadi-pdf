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
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Filters;
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
    private const int TokensPerCheck = 4096;
    private const int MaxOperands = 65536;

    private readonly IPdfObjectResolver _resolver;
    private readonly PdfDictionary? _resources;
    private readonly FilterPipeline _pipeline = FilterRegistry.CreateDefaultPipeline();

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

    /// <summary>
    /// Gets or initialises which optional-content layers are extracted. Text
    /// inside a hidden <c>BDC /OC</c> sequence, or in a form XObject whose
    /// <c>/OC</c> is hidden, is left out. Default:
    /// <see cref="OptionalContentVisibility.AllVisible"/> (every layer).
    /// </summary>
    public OptionalContentVisibility OptionalContent { get; init; } = OptionalContentVisibility.AllVisible;

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

        return ParseStreams(streams, _resources, new HashSet<int>(), 0);
    }

    // Maximum form-XObject nesting depth, guarding against pathological or
    // cyclic /Do reference chains.
    private const int MaxFormDepth = 12;

    private List<TextFragment> ParseStreams(
        byte[] streams,
        PdfDictionary? resources,
        HashSet<int> visited,
        int depth)
    {
        List<TextFragment> fragments = new List<TextFragment>();
        Stack<GraphicsState> stateStack = new Stack<GraphicsState>();
        GraphicsState state = new GraphicsState();
        bool inTextObject = false;

        List<PdfPrimitive> operands = new List<PdfPrimitive>();

        // Marked-content nesting: true where a BDC /OC sequence is hidden.
        // Text shown while any open sequence is hidden is dropped; the text
        // state still advances as normal.
        List<bool> hiddenStack = new List<bool>();
        int hiddenDepth = 0;

        // Parallel to hiddenStack: the /ActualText of each open sequence and
        // the fragment count when it began (null when it has none).
        List<(string Text, int FirstFragment)?> actualTextStack = new List<(string, int)?>();
        int actualTextDepth = 0;

        int tokensSinceCheck = 0;
        using (MemoryStream ms = new MemoryStream(streams))
        using (PdfTokenizer tokenizer = new PdfTokenizer(ms, leaveOpen: false))
        {
            while (true)
            {
                PdfToken token = tokenizer.Read();

                if (token.IsEndOfStream)
                {
                    PdfWorkScope.ThrowIfCancellationRequested();
                    break;
                }

                // Operands with no operator are malformed: every 4096 tokens
                // count as one operator against the page-complexity limit
                // (which also checks cancellation), and a run longer than any
                // real operator takes is dropped rather than held in memory.
                if (++tokensSinceCheck >= TokensPerCheck)
                {
                    tokensSinceCheck = 0;
                    PdfWorkScope.CountOperators();
                    if (operands.Count > MaxOperands)
                    {
                        operands.Clear();
                    }
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

                        if (++tokensSinceCheck >= TokensPerCheck)
                        {
                            tokensSinceCheck = 0;
                            PdfWorkScope.CountOperators();
                        }

                        // An operator inside an array means the "]" is
                        // missing: end the array there and run the operator.
                        if (inner.Type == PdfTokenType.Keyword)
                        {
                            tokenizer.Unread(inner);
                            break;
                        }

                        // An inline array longer than this is not real
                        // content; the rest is skipped.
                        if (arrayItems.Count < MaxOperands)
                        {
                            arrayItems.Add(TokenToPrimitive(inner));
                        }
                    }

                    operands.Add(new PdfArray(arrayItems));
                    continue;
                }

                // Inline dictionaries (BDC property lists) are assembled so
                // marked content can read /ActualText.
                if (token.Type == PdfTokenType.DictionaryStart)
                {
                    operands.Add(ReadInlineDictionary(tokenizer, 0));
                    continue;
                }

                if (token.Type != PdfTokenType.Keyword)
                {
                    operands.Add(TokenToPrimitive(token));
                    continue;
                }

                PdfWorkScope.CountOperators();
                string op = token.RawText;
                int fragmentsBefore = fragments.Count;

                switch (op)
                {
                    // ── Marked content (optional content) ─────────────────

                    // ── Inline images: skip the binary data ─────────────

                    case "ID":
                        SkipInlineImageData(streams, token, tokenizer);
                        break;

                    case "BMC":
                        hiddenStack.Add(false);
                        actualTextStack.Add(null);
                        break;

                    case "BDC":
                        {
                            bool hidden = operands.Count >= 2
                                && operands[0] is PdfName mcTag && mcTag.Value == "OC"
                                && operands[1] is PdfName mcProp
                                && !OptionalContent.IsVisible(ResolveProperty(resources, mcProp));
                            hiddenStack.Add(hidden);
                            if (hidden)
                            {
                                hiddenDepth++;
                            }

                            // /ActualText replaces the text of the glyphs it
                            // spans (§14.9.4); an outer span wins over inner ones.
                            string? actual = actualTextDepth == 0 ? ActualTextOf(operands, resources) : null;
                            actualTextStack.Add(actual is null ? null : (actual, fragments.Count));
                            if (actual is not null)
                            {
                                actualTextDepth++;
                            }
                        }

                        break;

                    case "EMC":
                        if (hiddenStack.Count > 0)
                        {
                            if (hiddenStack[^1])
                            {
                                hiddenDepth--;
                            }

                            hiddenStack.RemoveAt(hiddenStack.Count - 1);
                        }

                        if (actualTextStack.Count > 0)
                        {
                            if (actualTextStack[^1] is (string actualText, int firstFragment))
                            {
                                actualTextDepth--;
                                ReplaceWithActualText(fragments, firstFragment, actualText);
                            }

                            actualTextStack.RemoveAt(actualTextStack.Count - 1);
                        }

                        break;

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
                            state.Font = ResolveFont(fontName, resources);
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
                            ShowString(textBytes, state, fragments);
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
                                ShowString(textBytes, state, fragments);
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
                            ShowString(textBytes, state, fragments);
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

                    case "Do":
                        if (operands.Count >= 1 && depth < MaxFormDepth && hiddenDepth == 0)
                        {
                            AppendFormText(
                                GetString(operands, 0), resources, visited, depth, fragments);
                        }
                        break;

                    default:
                        // Unknown operator — discard operands and continue.
                        break;
                }

                if (hiddenDepth > 0 && fragments.Count > fragmentsBefore)
                {
                    fragments.RemoveRange(fragmentsBefore, fragments.Count - fragmentsBefore);
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
        bool widthKnown = true;

        for (int i = 0; i < array.Count; i++)
        {
            PdfPrimitive item = array[i];

            if (item is PdfString str)
            {
                string decoded = state.Font.Decode(str.Bytes);
                sb.Append(decoded);
                state.TextMatrix = AdvanceTextMatrix(state.TextMatrix, str.Bytes, decoded, state, out bool measured);
                widthKnown &= measured;
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
            double width = widthKnown ? state.TextMatrix.TranslationX - startX : double.NaN;
            fragments.Add(new TextFragment(sb.ToString(), startX, startY, state.FontSize, width));
        }
    }

    // Shows one string (Tj, ', "): adds its fragment with its measured width
    // and advances the text matrix.
    private static void ShowString(byte[] textBytes, GraphicsState state, List<TextFragment> fragments)
    {
        string text = state.Font.Decode(textBytes);
        double x = state.TextMatrix.TranslationX;
        double y = state.TextMatrix.TranslationY;
        state.TextMatrix = AdvanceTextMatrix(state.TextMatrix, textBytes, text, state, out bool measured);
        double width = measured ? state.TextMatrix.TranslationX - x : double.NaN;
        fragments.Add(new TextFragment(text, x, y, state.FontSize, width));
    }

    // ── Text matrix advance ───────────────────────────────────────────────

    private static Matrix3x3 AdvanceTextMatrix(
        Matrix3x3 matrix,
        byte[] bytes,
        string text,
        GraphicsState state,
        out bool measured)
    {
        // With the font's widths (§9.4.4): tx = ((w0 × Tfs) + Tc + Tw) × Th,
        // Tw applying to single-byte code 32 only.
        if (state.Font.TryMeasure(bytes, out double width, out int codes, out int spaces))
        {
            measured = true;
            double tx = ((width / 1000.0 * state.FontSize) + (state.CharacterSpacing * codes) + (state.WordSpacing * spaces))
                * (state.HorizontalScaling / 100.0);
            return matrix.Translate(tx, 0);
        }

        // Approximate advance: FontSize * HorizontalScaling/100 per character,
        // for fonts whose widths are not known (standard 14 without /Widths).
        measured = false;
        double advance = text.Length * state.FontSize * (state.HorizontalScaling / 100.0) * 0.6;
        return matrix.Translate(advance + (state.CharacterSpacing * text.Length), 0);
    }

    // ── Font resolution ───────────────────────────────────────────────────

    private PdfFont ResolveFont(string fontName, PdfDictionary? resources)
    {
        if (resources is null)
        {
            return PdfFont.Default();
        }

        if (string.IsNullOrEmpty(fontName))
        {
            return PdfFont.Default();
        }

        PdfDictionary? fontDict = ResolveDictionary(resources, PdfName.Font);

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

    // Resolves a named XObject in the given resources and, when it is a form,
    // recurses into its content so text drawn via /Do (e.g. an imported or
    // stamped page) is extracted too.
    private void AppendFormText(
        string name,
        PdfDictionary? resources,
        HashSet<int> visited,
        int depth,
        List<TextFragment> fragments)
    {
        if (resources is null || string.IsNullOrEmpty(name))
        {
            return;
        }

        PdfDictionary? xobjects = ResolveDictionary(resources, PdfName.XObject);
        if (xobjects is null
            || !xobjects.TryGetValue(PdfName.Intern(name), out PdfPrimitive? xref))
        {
            return;
        }

        // Guard against cyclic /Do references by object number.
        if (xref is PdfReference reference && !visited.Add(reference.ObjectId.ObjectNumber))
        {
            return;
        }

        if (_resolver.Resolve(xref) is not PdfStream stream || !IsFormXObject(stream))
        {
            return;
        }

        if (stream.Dictionary.TryGetValue(PdfName.Intern("OC"), out PdfPrimitive? oc)
            && !OptionalContent.IsVisible(oc))
        {
            return;
        }

        byte[] content = DecodeStream(stream);
        PdfDictionary? formResources =
            ResolveDictionary(stream.Dictionary, PdfName.Resources) ?? resources;

        fragments.AddRange(ParseStreams(content, formResources, visited, depth + 1));
    }

    // The /ActualText of a BDC property list (inline or a named /Properties
    // entry), or null.
    private string? ActualTextOf(List<PdfPrimitive> operands, PdfDictionary? resources)
    {
        if (operands.Count < 2)
        {
            return null;
        }

        PdfDictionary? props = operands[1] switch
        {
            PdfDictionary inline => inline,
            PdfName name => ResolveProperty(resources, name) is PdfPrimitive entry ? _resolver.Resolve(entry) as PdfDictionary : null,
            _ => null,
        };
        return props is not null
            && props.TryGetValue(PdfName.Intern("ActualText"), out PdfPrimitive? value)
            && _resolver.Resolve(value) is PdfString text
            ? text.ToTextString()
            : null;
    }

    // Replaces the fragments shown since firstFragment with one fragment
    // holding the actual text, placed where the first of them was. A span
    // that showed nothing adds nothing.
    private static void ReplaceWithActualText(List<TextFragment> fragments, int firstFragment, string actualText)
    {
        if (firstFragment >= fragments.Count)
        {
            return;
        }

        TextFragment first = fragments[firstFragment];

        // The span covers its glyphs from the first one's left edge to the
        // right edge of the one that ends furthest right, so the text after
        // it is judged against the real extent and gets no false word gap.
        double right = double.NegativeInfinity;
        for (int i = firstFragment; i < fragments.Count; i++)
        {
            TextFragment f = fragments[i];
            right = double.IsNaN(f.Width) || double.IsNaN(right) ? double.NaN : Math.Max(right, f.X + f.Width);
        }

        fragments.RemoveRange(firstFragment, fragments.Count - firstFragment);
        if (actualText.Length > 0)
        {
            fragments.Add(new TextFragment(actualText, first.X, first.Y, first.FontSize, right - first.X));
        }
    }

    // Skips an inline image's data after its ID operator (§8.9.7): the data
    // starts after the single white-space byte that follows ID and ends at
    // the first "EI" with white space before it and white space (or the end)
    // after it. Without this, bytes of the image were read as operators.
    private static void SkipInlineImageData(byte[] streams, PdfToken id, PdfTokenizer tokenizer)
    {
        long start = id.ByteOffset + 3;
        for (long i = Math.Max(start, 1); i + 1 < streams.Length; i++)
        {
            if (streams[i] == (byte)'E' && streams[i + 1] == (byte)'I'
                && IsWhiteSpace(streams[i - 1])
                && (i + 2 == streams.Length || IsWhiteSpace(streams[i + 2])))
            {
                tokenizer.Seek(i + 2);
                return;
            }
        }

        tokenizer.Seek(streams.Length);
    }

    private static bool IsWhiteSpace(byte b) => b is 0 or 9 or 10 or 12 or 13 or 32;

    // Reads an inline dictionary after its "<<" (nested dictionaries and
    // arrays included). Malformed input ends it at the stream's end.
    private static PdfDictionary ReadInlineDictionary(PdfTokenizer tokenizer, int depth)
    {
        PdfDictionary dict = new PdfDictionary();
        PdfName? key = null;
        int items = 0;
        while (true)
        {
            PdfToken token = tokenizer.Read();
            if (token.IsEndOfStream || token.Type == PdfTokenType.DictionaryEnd)
            {
                return dict;
            }

            // An operator means the ">>" is missing: end here and let the
            // caller run it.
            if (token.Type == PdfTokenType.Keyword)
            {
                tokenizer.Unread(token);
                return dict;
            }

            if (++items % TokensPerCheck == 0)
            {
                PdfWorkScope.CountOperators();
            }

            PdfPrimitive value = token.Type switch
            {
                PdfTokenType.DictionaryStart when depth < 32 => ReadInlineDictionary(tokenizer, depth + 1),
                PdfTokenType.ArrayStart when depth < 32 => ReadInlineArray(tokenizer, depth + 1),
                _ => TokenToPrimitive(token),
            };

            if (key is null)
            {
                key = value as PdfName;
            }
            else
            {
                if (dict.Count < MaxOperands)
                {
                    dict.Set(key, value);
                }

                key = null;
            }
        }
    }

    private static PdfArray ReadInlineArray(PdfTokenizer tokenizer, int depth)
    {
        List<PdfPrimitive> items = new List<PdfPrimitive>();
        int read = 0;
        while (true)
        {
            PdfToken token = tokenizer.Read();
            if (token.IsEndOfStream || token.Type == PdfTokenType.ArrayEnd)
            {
                return new PdfArray(items);
            }

            if (token.Type == PdfTokenType.Keyword)
            {
                tokenizer.Unread(token);
                return new PdfArray(items);
            }

            if (++read % TokensPerCheck == 0)
            {
                PdfWorkScope.CountOperators();
            }

            PdfPrimitive value = token.Type switch
            {
                PdfTokenType.DictionaryStart when depth < 32 => ReadInlineDictionary(tokenizer, depth + 1),
                PdfTokenType.ArrayStart when depth < 32 => ReadInlineArray(tokenizer, depth + 1),
                _ => TokenToPrimitive(token),
            };

            if (items.Count < MaxOperands)
            {
                items.Add(value);
            }
        }
    }

    // The raw /Properties entry for a BDC property name (reference kept so
    // the group's identity survives), or null.
    private PdfPrimitive? ResolveProperty(PdfDictionary? resources, PdfName name)
    {
        if (resources is null)
        {
            return null;
        }

        PdfDictionary? properties = ResolveDictionary(resources, PdfName.Intern("Properties"));
        return properties is not null && properties.TryGetValue(name, out PdfPrimitive? entry) ? entry : null;
    }

    private static bool IsFormXObject(PdfStream stream)
    {
        return stream.Dictionary.TryGetValue(PdfName.Subtype, out PdfPrimitive? subtype)
            && subtype is PdfName name && name.Value == "Form";
    }

    private PdfDictionary? ResolveDictionary(PdfDictionary parent, PdfName key)
    {
        if (!parent.TryGetValue(key, out PdfPrimitive? value))
        {
            return null;
        }

        return _resolver.Resolve(value) as PdfDictionary;
    }

    private byte[] DecodeStream(PdfStream stream)
    {
        PdfDictionary dict = stream.Dictionary;

        if (!dict.TryGetValue(PdfName.Filter, out PdfPrimitive? filterPrim))
        {
            return stream.RawBytes;
        }

        dict.TryGetValue(PdfName.Intern("DecodeParms"), out PdfPrimitive? parms);
        byte[] current = stream.RawBytes;

        if (filterPrim is PdfName single)
        {
            return _pipeline.Decode(
                FilterRegistry.ResolveAlias(single.Value), current,
                FilterParameters.FromDictionary(parms, 0));
        }

        if (filterPrim is PdfArray array)
        {
            for (int i = 0; i < array.Count; i++)
            {
                if (array[i] is PdfName filterName)
                {
                    current = _pipeline.Decode(
                        FilterRegistry.ResolveAlias(filterName.Value), current,
                        FilterParameters.FromDictionary(parms, i));
                }
            }
        }

        return current;
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
        // Single authoritative escape decoder (§7.3.4.2) — see
        // PdfString.DecodeLiteralToken. Previously this path passed the raw
        // bytes through unescaped, so octal escapes such as \050 surfaced
        // verbatim in extracted text.
        return new PdfString(PdfString.DecodeLiteralToken(raw));
    }

    private static PdfString ParseHexString(byte[] raw)
    {
        return new PdfString(PdfString.DecodeHexToken(raw));
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
