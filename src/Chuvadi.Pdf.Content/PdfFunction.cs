// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.10 — Functions (Type 0 sampled, Type 2 exponential,
//        Type 3 stitching, Type 4 PostScript calculator)
// PHASE: Phase 2 — rendering conformance (shadings, tint transforms, soft masks)
// A self-contained evaluator for the four PDF function types. Functions map an
// m-dimensional input to an n-dimensional output and underpin shadings, the
// tint transforms of Separation/DeviceN colour spaces, and soft-mask transfer.

using System;
using System.Collections.Generic;
using System.Globalization;
using Chuvadi.Pdf.Filters;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Content;

/// <summary>
/// A PDF function (PDF 32000-1:2008 §7.10): a mapping from an
/// <see cref="InputCount"/>-dimensional input to an <see cref="OutputCount"/>-dimensional
/// output. Use <see cref="Parse"/> to build one from a function dictionary,
/// stream, or array, then <see cref="Evaluate"/> to apply it.
/// </summary>
public abstract class PdfFunction
{
    private readonly double[] _domain;
    private readonly double[]? _range;

    /// <summary>Initialises the shared domain/range of a function.</summary>
    /// <param name="domain">Input clipping interval pairs (length 2·m).</param>
    /// <param name="range">Output clipping interval pairs (length 2·n), or null.</param>
    protected PdfFunction(double[] domain, double[]? range)
    {
        ArgumentNullException.ThrowIfNull(domain);
        _domain = domain;
        _range = range;
    }

    /// <summary>The number of input values the function consumes (m).</summary>
    public int InputCount => _domain.Length / 2;

    /// <summary>Returns a copy of this function's input domain as interval pairs (length 2·m).</summary>
    /// <returns>A fresh array of domain bounds.</returns>
    public double[] DomainCopy() => (double[])_domain.Clone();

    /// <summary>The number of output values the function produces (n).</summary>
    public abstract int OutputCount { get; }

    /// <summary>The input clipping domain as interval pairs (length 2·m).</summary>
    protected double[] Domain => _domain;

    /// <summary>The output clipping range as interval pairs (length 2·n), or null.</summary>
    protected double[]? Range => _range;

    /// <summary>
    /// Evaluates the function: clips <paramref name="input"/> to the domain,
    /// applies the mapping, and clips the result to the range when one is defined.
    /// </summary>
    /// <param name="input">The input values; length should equal <see cref="InputCount"/>.</param>
    /// <returns>The output values (length <see cref="OutputCount"/>).</returns>
    public double[] Evaluate(double[] input)
    {
        ArgumentNullException.ThrowIfNull(input);

        double[] clipped = new double[InputCount];
        for (int i = 0; i < InputCount; i++)
        {
            double value = i < input.Length ? input[i] : 0.0;
            clipped[i] = Clip(value, _domain[2 * i], _domain[(2 * i) + 1]);
        }

        double[] output = new double[OutputCount];
        EvaluateCore(clipped, output);

        if (_range is not null)
        {
            for (int j = 0; j < OutputCount; j++)
            {
                output[j] = Clip(output[j], _range[2 * j], _range[(2 * j) + 1]);
            }
        }

        return output;
    }

    /// <summary>Applies the type-specific mapping from clipped input into output.</summary>
    /// <param name="input">Domain-clipped input (length <see cref="InputCount"/>).</param>
    /// <param name="output">Buffer to fill (length <see cref="OutputCount"/>).</param>
    protected abstract void EvaluateCore(double[] input, double[] output);

    /// <summary>
    /// Parses a function object: a Type 2/3 dictionary, a Type 0/4 stream, or an
    /// array of n single-output functions (treated as one n-output function).
    /// </summary>
    /// <param name="function">The function object or reference.</param>
    /// <param name="objects">The object store used to resolve references and stream data.</param>
    /// <returns>The parsed function.</returns>
    public static PdfFunction Parse(PdfPrimitive function, PdfObjectStore objects)
    {
        ArgumentNullException.ThrowIfNull(function);
        ArgumentNullException.ThrowIfNull(objects);

        PdfPrimitive resolved = objects.Resolve(function);

        if (resolved is PdfArray array)
        {
            List<PdfFunction> parts = new(array.Count);
            for (int i = 0; i < array.Count; i++)
            {
                parts.Add(Parse(array[i], objects));
            }
            return new ArrayFunction(parts.ToArray());
        }

        PdfStream? stream = resolved as PdfStream;
        PdfDictionary dictionary = stream?.Dictionary
            ?? resolved as PdfDictionary
            ?? throw new ContentException("Function object is not a dictionary, stream, or array.");

        int functionType = (int)dictionary.GetNumber(PdfName.Intern("FunctionType"), -1);
        double[] domain = ReadNumbers(dictionary, objects, "Domain")
            ?? throw new ContentException("Function is missing the required /Domain.");

        switch (functionType)
        {
            case 0:
                return SampledFunction.Create(
                    stream ?? throw new ContentException("Type 0 function requires a stream."),
                    domain, objects);
            case 2:
                return ExponentialFunction.Create(dictionary, objects, domain);
            case 3:
                return StitchingFunction.Create(dictionary, objects, domain);
            case 4:
                return PostScriptFunction.Create(
                    stream ?? throw new ContentException("Type 4 function requires a stream."),
                    domain, objects);
            default:
                throw new ContentException(
                    "Unsupported /FunctionType " + functionType.ToString(CultureInfo.InvariantCulture) + ".");
        }
    }

    /// <summary>Clips <paramref name="value"/> to the inclusive interval [lo, hi].</summary>
    /// <param name="value">The value to clip.</param>
    /// <param name="lo">Lower bound.</param>
    /// <param name="hi">Upper bound.</param>
    /// <returns>The clipped value.</returns>
    protected static double Clip(double value, double lo, double hi)
    {
        if (lo > hi)
        {
            (lo, hi) = (hi, lo);
        }
        if (value < lo)
        {
            return lo;
        }
        if (value > hi)
        {
            return hi;
        }
        return value;
    }

    /// <summary>
    /// Linearly maps <paramref name="x"/> from the interval [xMin, xMax] onto
    /// [yMin, yMax]. A degenerate input interval maps to <paramref name="yMin"/>.
    /// </summary>
    /// <param name="x">The value to map.</param>
    /// <param name="xMin">Source interval lower bound.</param>
    /// <param name="xMax">Source interval upper bound.</param>
    /// <param name="yMin">Target interval lower bound.</param>
    /// <param name="yMax">Target interval upper bound.</param>
    /// <returns>The interpolated value.</returns>
    protected static double Interpolate(double x, double xMin, double xMax, double yMin, double yMax)
    {
        if (xMax == xMin)
        {
            return yMin;
        }
        return yMin + ((x - xMin) * (yMax - yMin) / (xMax - xMin));
    }

    /// <summary>Reads a numeric array entry, resolving indirect references.</summary>
    /// <param name="dictionary">The owning dictionary.</param>
    /// <param name="objects">The object store.</param>
    /// <param name="key">The dictionary key.</param>
    /// <returns>The numbers, or null when the key is absent or not an array.</returns>
    private protected static double[]? ReadNumbers(PdfDictionary dictionary, PdfObjectStore objects, string key)
    {
        if (!dictionary.TryGetValue(PdfName.Intern(key), out PdfPrimitive? value))
        {
            return null;
        }

        if (objects.Resolve(value) is not PdfArray array)
        {
            return null;
        }

        double[] numbers = new double[array.Count];
        for (int i = 0; i < array.Count; i++)
        {
            numbers[i] = PdfReal.ToDouble(objects.Resolve(array[i]));
        }
        return numbers;
    }

    /// <summary>
    /// Decodes a function stream's bytes through its filter chain (Type 0 sample
    /// data and Type 4 program text).
    /// </summary>
    /// <param name="stream">The function stream.</param>
    /// <returns>The decoded bytes.</returns>
    private protected static byte[] DecodeStream(PdfStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.IsFiltered)
        {
            return stream.RawBytes;
        }

        FilterPipeline pipeline = FilterRegistry.CreateDefaultPipeline();
        byte[] data = stream.RawBytes;
        PdfPrimitive? filter = stream.Filter;

        if (filter is PdfName name)
        {
            return pipeline.Decode(FilterRegistry.ResolveAlias(name.Value), data, null);
        }

        if (filter is PdfArray array)
        {
            for (int i = 0; i < array.Count; i++)
            {
                PdfName? element = array.GetAs<PdfName>(i)
                    ?? throw new ContentException("Function filter chain contains a non-name entry.");
                data = pipeline.Decode(FilterRegistry.ResolveAlias(element.Value), data, null);
            }
            return data;
        }

        return data;
    }
}

/// <summary>
/// Type 0 (sampled) function: a table of samples interpolated multilinearly
/// (PDF 32000-1:2008 §7.10.2).
/// </summary>
internal sealed class SampledFunction : PdfFunction
{
    private readonly int[] _size;
    private readonly int _bitsPerSample;
    private readonly double[] _encode;
    private readonly double[] _decode;
    private readonly int _outputCount;
    private readonly byte[] _samples;
    private readonly long _maxSampleValue;

    private SampledFunction(
        double[] domain,
        double[] range,
        int[] size,
        int bitsPerSample,
        double[] encode,
        double[] decode,
        byte[] samples)
        : base(domain, range)
    {
        _size = size;
        _bitsPerSample = bitsPerSample;
        _encode = encode;
        _decode = decode;
        _outputCount = range.Length / 2;
        _samples = samples;
        _maxSampleValue = (1L << bitsPerSample) - 1L;
    }

    /// <inheritdoc/>
    public override int OutputCount => _outputCount;

    internal static SampledFunction Create(PdfStream stream, double[] domain, PdfObjectStore objects)
    {
        PdfDictionary dict = stream.Dictionary;
        double[] range = ReadNumbers(dict, objects, "Range")
            ?? throw new ContentException("Type 0 function is missing the required /Range.");

        double[]? sizeNumbers = ReadNumbers(dict, objects, "Size")
            ?? throw new ContentException("Type 0 function is missing the required /Size.");
        int[] size = new int[sizeNumbers.Length];
        for (int i = 0; i < size.Length; i++)
        {
            size[i] = (int)sizeNumbers[i];
        }

        int bitsPerSample = (int)dict.GetNumber(PdfName.Intern("BitsPerSample"), 8);

        int inputCount = domain.Length / 2;
        double[] encode = ReadNumbers(dict, objects, "Encode") ?? DefaultEncode(size);
        if (encode.Length < 2 * inputCount)
        {
            encode = DefaultEncode(size);
        }

        double[] decode = ReadNumbers(dict, objects, "Decode") ?? (double[])range.Clone();

        byte[] samples = DecodeStream(stream);
        return new SampledFunction(domain, range, size, bitsPerSample, encode, decode, samples);
    }

    private static double[] DefaultEncode(int[] size)
    {
        double[] encode = new double[size.Length * 2];
        for (int i = 0; i < size.Length; i++)
        {
            encode[2 * i] = 0.0;
            encode[(2 * i) + 1] = size[i] - 1;
        }
        return encode;
    }

    /// <inheritdoc/>
    protected override void EvaluateCore(double[] input, double[] output)
    {
        int m = _size.Length;

        // Encode each input coordinate into sample space and split into the
        // lower integer corner plus a fractional weight.
        double[] e = new double[m];
        int[] lo = new int[m];
        double[] frac = new double[m];
        for (int i = 0; i < m; i++)
        {
            double encoded = Interpolate(
                input[i], Domain[2 * i], Domain[(2 * i) + 1], _encode[2 * i], _encode[(2 * i) + 1]);
            encoded = Clip(encoded, 0, _size[i] - 1);
            e[i] = encoded;
            int floor = (int)Math.Floor(encoded);
            if (floor >= _size[i] - 1)
            {
                floor = Math.Max(0, _size[i] - 1);
            }
            lo[i] = floor;
            frac[i] = encoded - floor;
        }

        // Multilinear interpolation across the 2^m surrounding corners.
        int corners = 1 << m;
        for (int j = 0; j < _outputCount; j++)
        {
            double acc = 0.0;
            for (int c = 0; c < corners; c++)
            {
                double weight = 1.0;
                int[] coord = new int[m];
                for (int i = 0; i < m; i++)
                {
                    bool upper = (c & (1 << i)) != 0;
                    int ci = lo[i] + (upper ? 1 : 0);
                    if (ci > _size[i] - 1)
                    {
                        ci = _size[i] - 1;
                    }
                    coord[i] = ci;
                    weight *= upper ? frac[i] : (1.0 - frac[i]);
                }
                if (weight == 0.0)
                {
                    continue;
                }
                long raw = ReadSample(coord, j);
                acc += weight * raw;
            }

            double decoded = Interpolate(
                acc, 0, _maxSampleValue, _decode[2 * j], _decode[(2 * j) + 1]);
            output[j] = decoded;
        }
    }

    // Reads output component j of the sample at the given m-dimensional coordinate.
    private long ReadSample(int[] coord, int component)
    {
        // First input dimension varies fastest (PDF 32000-1:2008 §7.10.2).
        long flat = 0;
        long stride = 1;
        for (int i = 0; i < _size.Length; i++)
        {
            flat += coord[i] * stride;
            stride *= _size[i];
        }

        long bitIndex = ((flat * _outputCount) + component) * _bitsPerSample;
        return ReadBits(bitIndex, _bitsPerSample);
    }

    // Reads a big-endian (high-order-bit-first) value from the continuous sample
    // bitstream — function samples are packed with no per-sample byte alignment.
    private long ReadBits(long bitIndex, int bitCount)
    {
        long value = 0;
        for (int k = 0; k < bitCount; k++)
        {
            long pos = bitIndex + k;
            int byteIndex = (int)(pos >> 3);
            int bitInByte = 7 - (int)(pos & 7);
            int bit = byteIndex < _samples.Length ? (_samples[byteIndex] >> bitInByte) & 1 : 0;
            value = (value << 1) | (uint)bit;
        }
        return value;
    }
}

/// <summary>
/// Type 2 (exponential interpolation) function over a single input
/// (PDF 32000-1:2008 §7.10.3).
/// </summary>
internal sealed class ExponentialFunction : PdfFunction
{
    private readonly double[] _c0;
    private readonly double[] _c1;
    private readonly double _n;

    private ExponentialFunction(double[] domain, double[]? range, double[] c0, double[] c1, double n)
        : base(domain, range)
    {
        _c0 = c0;
        _c1 = c1;
        _n = n;
    }

    /// <inheritdoc/>
    public override int OutputCount => _c0.Length;

    internal static ExponentialFunction Create(PdfDictionary dict, PdfObjectStore objects, double[] domain)
    {
        double[] c0 = ReadNumbers(dict, objects, "C0") ?? new double[] { 0.0 };
        double[] c1 = ReadNumbers(dict, objects, "C1") ?? new double[] { 1.0 };
        double n = dict.GetNumber(PdfName.Intern("N"), 1.0);
        double[]? range = ReadNumbers(dict, objects, "Range");
        return new ExponentialFunction(domain, range, c0, c1, n);
    }

    /// <inheritdoc/>
    protected override void EvaluateCore(double[] input, double[] output)
    {
        double x = input[0];
        double xn = _n == 1.0 ? x : Math.Pow(x, _n);
        for (int j = 0; j < output.Length; j++)
        {
            output[j] = _c0[j] + (xn * (_c1[j] - _c0[j]));
        }
    }
}

/// <summary>
/// Type 3 (stitching) function: selects a subfunction by input interval and
/// re-encodes the input for it (PDF 32000-1:2008 §7.10.4).
/// </summary>
internal sealed class StitchingFunction : PdfFunction
{
    private readonly PdfFunction[] _functions;
    private readonly double[] _bounds;
    private readonly double[] _encode;

    private StitchingFunction(
        double[] domain, double[]? range, PdfFunction[] functions, double[] bounds, double[] encode)
        : base(domain, range)
    {
        _functions = functions;
        _bounds = bounds;
        _encode = encode;
    }

    /// <inheritdoc/>
    public override int OutputCount => _functions.Length > 0 ? _functions[0].OutputCount : 0;

    internal static StitchingFunction Create(PdfDictionary dict, PdfObjectStore objects, double[] domain)
    {
        if (objects.Resolve(dict[PdfName.Intern("Functions")]) is not PdfArray functionsArray)
        {
            throw new ContentException("Type 3 function is missing the required /Functions array.");
        }

        PdfFunction[] functions = new PdfFunction[functionsArray.Count];
        for (int i = 0; i < functionsArray.Count; i++)
        {
            functions[i] = Parse(functionsArray[i], objects);
        }

        double[] bounds = ReadNumbers(dict, objects, "Bounds") ?? Array.Empty<double>();
        double[] encode = ReadNumbers(dict, objects, "Encode") ?? Array.Empty<double>();
        double[]? range = ReadNumbers(dict, objects, "Range");
        return new StitchingFunction(domain, range, functions, bounds, encode);
    }

    /// <inheritdoc/>
    protected override void EvaluateCore(double[] input, double[] output)
    {
        double x = input[0];
        int k = _functions.Length;

        int index = 0;
        while (index < _bounds.Length && x >= _bounds[index])
        {
            index++;
        }
        if (index >= k)
        {
            index = Math.Max(0, k - 1);
        }

        double lo = index == 0 ? Domain[0] : _bounds[index - 1];
        double hi = index == _bounds.Length ? Domain[1] : _bounds[index];

        double encLo = (2 * index) < _encode.Length ? _encode[2 * index] : 0.0;
        double encHi = ((2 * index) + 1) < _encode.Length ? _encode[(2 * index) + 1] : 1.0;

        double encoded = Interpolate(x, lo, hi, encLo, encHi);
        double[] sub = _functions[index].Evaluate(new double[] { encoded });
        for (int j = 0; j < output.Length && j < sub.Length; j++)
        {
            output[j] = sub[j];
        }
    }
}

/// <summary>
/// An array of single-output functions presented as one multi-output function;
/// shading and tint-transform dictionaries permit this form.
/// </summary>
internal sealed class ArrayFunction : PdfFunction
{
    private readonly PdfFunction[] _functions;

    internal ArrayFunction(PdfFunction[] functions)
        : base(functions.Length > 0 ? functions[0].DomainCopy() : new double[] { 0.0, 1.0 }, null)
    {
        _functions = functions;
    }

    /// <inheritdoc/>
    public override int OutputCount => _functions.Length;

    /// <inheritdoc/>
    protected override void EvaluateCore(double[] input, double[] output)
    {
        for (int j = 0; j < _functions.Length; j++)
        {
            double[] sub = _functions[j].Evaluate(input);
            output[j] = sub.Length > 0 ? sub[0] : 0.0;
        }
    }
}

/// <summary>
/// Type 4 (PostScript calculator) function: a small stack language evaluated
/// over the input values to produce the outputs (PDF 32000-1:2008 §7.10.5).
/// </summary>
internal sealed class PostScriptFunction : PdfFunction
{
    private readonly Block _program;
    private readonly int _outputCount;

    private PostScriptFunction(double[] domain, double[] range, Block program)
        : base(domain, range)
    {
        _program = program;
        _outputCount = range.Length / 2;
    }

    /// <inheritdoc/>
    public override int OutputCount => _outputCount;

    internal static PostScriptFunction Create(PdfStream stream, double[] domain, PdfObjectStore objects)
    {
        double[] range = ReadNumbers(stream.Dictionary, objects, "Range")
            ?? throw new ContentException("Type 4 function is missing the required /Range.");

        string text = System.Text.Encoding.ASCII.GetString(DecodeStream(stream));
        List<string> tokens = Tokenize(text);
        int pos = 0;
        Block program = ParseBlock(tokens, ref pos, requireOpenBrace: true);
        return new PostScriptFunction(domain, range, program);
    }

    /// <inheritdoc/>
    protected override void EvaluateCore(double[] input, double[] output)
    {
        Stack<double> values = new();
        Stack<Block> procedures = new();
        for (int i = 0; i < input.Length; i++)
        {
            values.Push(input[i]);
        }

        Execute(_program, values, procedures);

        // The topmost n stack entries are the outputs, in order.
        for (int j = _outputCount - 1; j >= 0; j--)
        {
            output[j] = values.Count > 0 ? values.Pop() : 0.0;
        }
    }

    private static void Execute(Block block, Stack<double> values, Stack<Block> procedures)
    {
        foreach (object item in block.Items)
        {
            if (item is double number)
            {
                values.Push(number);
            }
            else if (item is Block nested)
            {
                procedures.Push(nested);
            }
            else if (item is string op)
            {
                ApplyOperator(op, values, procedures);
            }
        }
    }

    private static void ApplyOperator(string op, Stack<double> values, Stack<Block> procedures)
    {
        switch (op)
        {
            case "add": Binary(values, static (a, b) => a + b); break;
            case "sub": Binary(values, static (a, b) => a - b); break;
            case "mul": Binary(values, static (a, b) => a * b); break;
            case "div": Binary(values, static (a, b) => b == 0 ? 0 : a / b); break;
            case "idiv": Binary(values, static (a, b) => b == 0 ? 0 : (long)a / (long)b); break;
            case "mod": Binary(values, static (a, b) => b == 0 ? 0 : (long)a % (long)b); break;
            case "neg": Unary(values, static a => -a); break;
            case "abs": Unary(values, static a => Math.Abs(a)); break;
            case "sqrt": Unary(values, static a => Math.Sqrt(Math.Max(0, a))); break;
            case "sin": Unary(values, static a => Math.Sin(a * Math.PI / 180.0)); break;
            case "cos": Unary(values, static a => Math.Cos(a * Math.PI / 180.0)); break;
            case "atan": Binary(values, static (a, b) => Atan2Degrees(a, b)); break;
            case "exp": Binary(values, static (a, b) => Math.Pow(a, b)); break;
            case "ln": Unary(values, static a => a <= 0 ? 0 : Math.Log(a)); break;
            case "log": Unary(values, static a => a <= 0 ? 0 : Math.Log10(a)); break;
            case "ceiling": Unary(values, static a => Math.Ceiling(a)); break;
            case "floor": Unary(values, static a => Math.Floor(a)); break;
            case "round": Unary(values, static a => Math.Round(a, MidpointRounding.AwayFromZero)); break;
            case "truncate": Unary(values, static a => Math.Truncate(a)); break;
            case "cvi": Unary(values, static a => Math.Truncate(a)); break;
            case "cvr": break;
            case "eq": Binary(values, static (a, b) => a == b ? 1.0 : 0.0); break;
            case "ne": Binary(values, static (a, b) => a != b ? 1.0 : 0.0); break;
            case "gt": Binary(values, static (a, b) => a > b ? 1.0 : 0.0); break;
            case "ge": Binary(values, static (a, b) => a >= b ? 1.0 : 0.0); break;
            case "lt": Binary(values, static (a, b) => a < b ? 1.0 : 0.0); break;
            case "le": Binary(values, static (a, b) => a <= b ? 1.0 : 0.0); break;
            case "and": Binary(values, static (a, b) => (long)a & (long)b); break;
            case "or": Binary(values, static (a, b) => (long)a | (long)b); break;
            case "xor": Binary(values, static (a, b) => (long)a ^ (long)b); break;
            case "bitshift": Binary(values, static (a, b) => b >= 0 ? (long)a << (int)b : (long)a >> (int)-b); break;
            case "not": NotOp(values); break;
            case "true": values.Push(1.0); break;
            case "false": values.Push(0.0); break;
            case "pop": if (values.Count > 0) { values.Pop(); } break;
            case "exch": ExchOp(values); break;
            case "dup": if (values.Count > 0) { values.Push(values.Peek()); } break;
            case "copy": CopyOp(values); break;
            case "index": IndexOp(values); break;
            case "roll": RollOp(values); break;
            case "if": IfOp(values, procedures); break;
            case "ifelse": IfElseOp(values, procedures); break;
            default: break;
        }
    }

    private static double Atan2Degrees(double num, double den)
    {
        double angle = Math.Atan2(num, den) * 180.0 / Math.PI;
        if (angle < 0)
        {
            angle += 360.0;
        }
        return angle;
    }

    private static void Unary(Stack<double> values, Func<double, double> fn)
    {
        if (values.Count < 1)
        {
            return;
        }
        values.Push(fn(values.Pop()));
    }

    private static void Binary(Stack<double> values, Func<double, double, double> fn)
    {
        if (values.Count < 2)
        {
            return;
        }
        double b = values.Pop();
        double a = values.Pop();
        values.Push(fn(a, b));
    }

    private static void NotOp(Stack<double> values)
    {
        if (values.Count < 1)
        {
            return;
        }
        double x = values.Pop();
        if (x == 0.0)
        {
            values.Push(1.0);
        }
        else if (x == 1.0)
        {
            values.Push(0.0);
        }
        else
        {
            values.Push(~(long)x);
        }
    }

    private static void ExchOp(Stack<double> values)
    {
        if (values.Count < 2)
        {
            return;
        }
        double a = values.Pop();
        double b = values.Pop();
        values.Push(a);
        values.Push(b);
    }

    private static void CopyOp(Stack<double> values)
    {
        if (values.Count < 1)
        {
            return;
        }
        int n = (int)values.Pop();
        if (n <= 0 || n > values.Count)
        {
            return;
        }
        double[] top = values.ToArray();
        for (int i = n - 1; i >= 0; i--)
        {
            values.Push(top[i]);
        }
    }

    private static void IndexOp(Stack<double> values)
    {
        if (values.Count < 1)
        {
            return;
        }
        int n = (int)values.Pop();
        if (n < 0 || n >= values.Count)
        {
            values.Push(0.0);
            return;
        }
        double[] arr = values.ToArray();
        values.Push(arr[n]);
    }

    private static void RollOp(Stack<double> values)
    {
        if (values.Count < 2)
        {
            return;
        }
        int j = (int)values.Pop();
        int n = (int)values.Pop();
        if (n <= 0 || n > values.Count)
        {
            return;
        }

        double[] window = new double[n];
        for (int i = n - 1; i >= 0; i--)
        {
            window[i] = values.Pop();
        }
        int shift = ((j % n) + n) % n;
        double[] rolled = new double[n];
        for (int i = 0; i < n; i++)
        {
            rolled[(i + shift) % n] = window[i];
        }
        for (int i = 0; i < n; i++)
        {
            values.Push(rolled[i]);
        }
    }

    private static void IfOp(Stack<double> values, Stack<Block> procedures)
    {
        if (procedures.Count < 1 || values.Count < 1)
        {
            return;
        }
        Block proc = procedures.Pop();
        bool cond = values.Pop() != 0.0;
        if (cond)
        {
            Execute(proc, values, procedures);
        }
    }

    private static void IfElseOp(Stack<double> values, Stack<Block> procedures)
    {
        if (procedures.Count < 2 || values.Count < 1)
        {
            return;
        }
        Block proc2 = procedures.Pop();
        Block proc1 = procedures.Pop();
        bool cond = values.Pop() != 0.0;
        Execute(cond ? proc1 : proc2, values, procedures);
    }

    private static List<string> Tokenize(string text)
    {
        List<string> tokens = new();
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '{' || c == '}')
            {
                tokens.Add(c.ToString());
                i++;
            }
            else if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c == '%')
            {
                while (i < text.Length && text[i] != '\n' && text[i] != '\r')
                {
                    i++;
                }
            }
            else
            {
                int start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '{' && text[i] != '}')
                {
                    i++;
                }
                tokens.Add(text.Substring(start, i - start));
            }
        }
        return tokens;
    }

    private static Block ParseBlock(List<string> tokens, ref int pos, bool requireOpenBrace)
    {
        if (requireOpenBrace)
        {
            while (pos < tokens.Count && tokens[pos] != "{")
            {
                pos++;
            }
            if (pos < tokens.Count)
            {
                pos++; // consume '{'
            }
        }

        Block block = new();
        while (pos < tokens.Count)
        {
            string token = tokens[pos];
            if (token == "}")
            {
                pos++;
                break;
            }
            if (token == "{")
            {
                pos++;
                block.Items.Add(ParseInner(tokens, ref pos));
            }
            else if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            {
                block.Items.Add(number);
                pos++;
            }
            else
            {
                block.Items.Add(token);
                pos++;
            }
        }
        return block;
    }

    private static Block ParseInner(List<string> tokens, ref int pos)
    {
        Block block = new();
        while (pos < tokens.Count)
        {
            string token = tokens[pos];
            if (token == "}")
            {
                pos++;
                break;
            }
            if (token == "{")
            {
                pos++;
                block.Items.Add(ParseInner(tokens, ref pos));
            }
            else if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            {
                block.Items.Add(number);
                pos++;
            }
            else
            {
                block.Items.Add(token);
                pos++;
            }
        }
        return block;
    }

    private sealed class Block
    {
        public List<object> Items { get; } = new();
    }
}
