// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.7.4.5.2 (function-based shadings, type 1),
//        §8.7.4.5.3 (axial shadings, type 2), §7.10.2 (sampled functions)
//        §8.7.4.5.4 (radial shadings, type 3), §7.10.4 (stitching functions)
// PHASE: 3.23 — Scrub crop: sampled shading functions keep only the samples
//        the crop box can show.

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Content;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// Trims the sampled (type 0) functions of function-based (type 1), axial
/// (type 2) and radial (type 3) shadings to the samples that can colour the
/// crop box. A sampled
/// function is a grid of colour values over its input domain — for a
/// function-based shading, effectively an image — so the samples whose input
/// never falls inside the box are removed at the byte level.
/// </summary>
/// <remarks>
/// The input range the box can reach is computed exactly for types 1 and 2
/// (the box mapped into the shading's domain; the box corners projected onto
/// the axis), widened by two samples for interpolation. The kept
/// samples are copied bit for bit and the function's <c>/Domain</c> and
/// <c>/Encode</c> are narrowed so every kept input maps to the same sample
/// as before: colours inside the box are unchanged. Functions of other types
/// (exponential, PostScript) carry no positioned sample data and are kept;
/// sampled functions inside a stitching function are trimmed. Radial shadings
/// (type 3) are trimmed by sampling the box densely and widening the range.
/// </remarks>
internal static class FunctionShadingTrimmer
{
    /// <summary>
    /// Trims <paramref name="shading"/> for the box under the given uses
    /// (shading space to page space). Returns false when nothing was trimmed.
    /// </summary>
    internal static bool Trim(
        PdfPrimitive shading, IReadOnlyList<Matrix3x3> toPage,
        double x0, double y0, double x1, double y1,
        IPdfObjectResolver resolver, Func<PdfStream, PdfReference> allocate,
        out PdfDictionary? trimmed)
    {
        trimmed = null;
        PdfPrimitive resolved = resolver.Resolve(shading);
        PdfDictionary? dict = resolved as PdfDictionary ?? (resolved as PdfStream)?.Dictionary;
        if (dict is null || resolved is PdfStream || toPage.Count == 0)
        {
            return false;
        }

        int type = (int)Num(resolver, dict.GetAs<PdfPrimitive>(PdfName.Intern("ShadingType")));
        List<(double Min, double Max)>? inputs = type switch
        {
            1 => FunctionBasedInputs(dict, toPage, x0, y0, x1, y1, resolver),
            2 => AxialInputs(dict, toPage, x0, y0, x1, y1, resolver),
            3 => RadialInputs(dict, toPage, x0, y0, x1, y1, resolver),
            _ => null,
        };
        if (inputs is null || !dict.TryGetValue(PdfName.Intern("Function"), out PdfPrimitive? function))
        {
            return false;
        }

        PdfPrimitive? newFunction = null;
        PdfPrimitive fn = resolver.Resolve(function);
        if (fn is PdfArray array)
        {
            PdfArray copy = new PdfArray([]);
            bool any = false;
            for (int i = 0; i < array.Count; i++)
            {
                PdfPrimitive? part = TrimFunction(array[i], inputs, resolver, allocate, 0);
                any |= part is not null;
                copy.Add(part ?? array[i]);
            }

            newFunction = any ? copy : null;
        }
        else
        {
            newFunction = TrimFunction(function, inputs, resolver, allocate, 0);
        }

        if (newFunction is null)
        {
            return false;
        }

        trimmed = new PdfDictionary();
        foreach (PdfName key in dict.Keys)
        {
            trimmed.Set(key, dict[key]);
        }

        trimmed.Set(PdfName.Intern("Function"), newFunction);
        return true;
    }

    // Type 1: the box mapped through the inverse of Matrix x use into the
    // shading's /Domain [x0 x1 y0 y1], clipped to it.
    private static List<(double Min, double Max)>? FunctionBasedInputs(
        PdfDictionary dict, IReadOnlyList<Matrix3x3> toPage, double x0, double y0, double x1, double y1,
        IPdfObjectResolver resolver)
    {
        double[] domain = Numbers(resolver, dict.GetAs<PdfPrimitive>(PdfName.Intern("Domain"))) ?? new double[] { 0, 1, 0, 1 };
        if (domain.Length < 4)
        {
            return null;
        }

        Matrix3x3 matrix = Matrix(resolver, dict.GetAs<PdfPrimitive>(PdfName.Intern("Matrix")));
        double ux0 = double.PositiveInfinity, uy0 = double.PositiveInfinity;
        double ux1 = double.NegativeInfinity, uy1 = double.NegativeInfinity;
        foreach (Matrix3x3 use in toPage)
        {
            if (!ScrubGeometry.TryInvert(matrix.Multiply(use), out Matrix3x3 inverse))
            {
                return null;
            }

            foreach ((double x, double y) in ScrubGeometry.TransformRectCorners(inverse, x0, y0, x1, y1))
            {
                ux0 = Math.Min(ux0, x);
                uy0 = Math.Min(uy0, y);
                ux1 = Math.Max(ux1, x);
                uy1 = Math.Max(uy1, y);
            }
        }

        return new List<(double Min, double Max)>
        {
            (Math.Max(ux0, Math.Min(domain[0], domain[1])), Math.Min(ux1, Math.Max(domain[0], domain[1]))),
            (Math.Max(uy0, Math.Min(domain[2], domain[3])), Math.Min(uy1, Math.Max(domain[2], domain[3]))),
        };
    }

    // Type 2: the box corners projected onto the axis (s in [0,1], clamped
    // as /Extend does), mapped to t over the shading's /Domain [t0 t1].
    private static List<(double Min, double Max)>? AxialInputs(
        PdfDictionary dict, IReadOnlyList<Matrix3x3> toPage, double x0, double y0, double x1, double y1,
        IPdfObjectResolver resolver)
    {
        double[]? coords = Numbers(resolver, dict.GetAs<PdfPrimitive>(PdfName.Intern("Coords")));
        double[] domain = Numbers(resolver, dict.GetAs<PdfPrimitive>(PdfName.Intern("Domain"))) ?? new double[] { 0, 1 };
        if (coords is null || coords.Length < 4 || domain.Length < 2)
        {
            return null;
        }

        double dx = coords[2] - coords[0];
        double dy = coords[3] - coords[1];
        double len2 = (dx * dx) + (dy * dy);
        if (len2 < 1e-18)
        {
            return null;
        }

        double sMin = double.PositiveInfinity, sMax = double.NegativeInfinity;
        foreach (Matrix3x3 use in toPage)
        {
            if (!ScrubGeometry.TryInvert(use, out Matrix3x3 inverse))
            {
                return null;
            }

            foreach ((double x, double y) in ScrubGeometry.TransformRectCorners(inverse, x0, y0, x1, y1))
            {
                double s = (((x - coords[0]) * dx) + ((y - coords[1]) * dy)) / len2;
                sMin = Math.Min(sMin, s);
                sMax = Math.Max(sMax, s);
            }
        }

        sMin = Math.Clamp(sMin, 0, 1);
        sMax = Math.Clamp(sMax, 0, 1);
        double t0 = domain[0] + (sMin * (domain[1] - domain[0]));
        double t1 = domain[0] + (sMax * (domain[1] - domain[0]));
        return new List<(double Min, double Max)> { (Math.Min(t0, t1), Math.Max(t0, t1)) };
    }

    // Trims a sampled function, or the sampled functions inside a stitching
    // function (each given the part of the input range that falls in its
    // interval, mapped through its /Encode); null when nothing changes.
    private static PdfPrimitive? TrimFunction(
        PdfPrimitive function, List<(double Min, double Max)> inputs, IPdfObjectResolver resolver,
        Func<PdfStream, PdfReference> allocate, int depth)
    {
        PdfPrimitive resolved = resolver.Resolve(function);
        PdfDictionary? d = resolved as PdfDictionary ?? (resolved as PdfStream)?.Dictionary;
        if (d is null || depth > 8)
        {
            return null;
        }

        int type = (int)Num(resolver, d.GetAs<PdfPrimitive>(PdfName.Intern("FunctionType")));
        if (type == 0)
        {
            return TrimSampled(function, inputs, resolver, allocate);
        }

        if (type != 3 || inputs.Count != 1 || resolved is not PdfDictionary stitching)
        {
            return null;
        }

        double[]? domain = Numbers(resolver, d.GetAs<PdfPrimitive>(PdfName.Intern("Domain")));
        double[] bounds = Numbers(resolver, d.GetAs<PdfPrimitive>(PdfName.Intern("Bounds"))) ?? System.Array.Empty<double>();
        double[]? encode = Numbers(resolver, d.GetAs<PdfPrimitive>(PdfName.Intern("Encode")));
        if (domain is null || domain.Length < 2
            || resolver.Resolve(d.GetAs<PdfPrimitive>(PdfName.Intern("Functions")) ?? PdfNull.Value) is not PdfArray functions
            || bounds.Length != functions.Count - 1 || encode is null || encode.Length != 2 * functions.Count)
        {
            return null;
        }

        (double inMin, double inMax) = inputs[0];
        inMin = Math.Clamp(inMin, domain[0], domain[1]);
        inMax = Math.Clamp(inMax, domain[0], domain[1]);
        PdfArray newFunctions = new PdfArray([]);
        bool any = false;
        for (int k = 0; k < functions.Count; k++)
        {
            double lo = k == 0 ? domain[0] : bounds[k - 1];
            double hi = k == functions.Count - 1 ? domain[1] : bounds[k];
            double a = Math.Max(inMin, lo);
            double b = Math.Min(inMax, hi);
            if (b < a)
            {
                // Never reached from the box: keep only the samples at the
                // end of its interval nearest the box.
                a = b = inMax < lo ? lo : hi;
            }

            double e0 = encode[2 * k];
            double e1 = encode[(2 * k) + 1];
            double Map(double t) => hi - lo < 1e-18 ? e0 : e0 + ((t - lo) * (e1 - e0) / (hi - lo));
            double ma = Map(a);
            double mb = Map(b);
            PdfPrimitive? part = TrimFunction(
                functions[k], new List<(double Min, double Max)> { (Math.Min(ma, mb), Math.Max(ma, mb)) },
                resolver, allocate, depth + 1);
            any |= part is not null;
            newFunctions.Add(part ?? functions[k]);
        }

        if (!any)
        {
            return null;
        }

        PdfDictionary copy = new PdfDictionary();
        foreach (PdfName key in stitching.Keys)
        {
            copy.Set(key, stitching[key]);
        }

        copy.Set(PdfName.Intern("Functions"), newFunctions);
        return copy;
    }

    // Type 3: s (0 at the first circle, 1 at the last) at points across the
    // box, each point given the largest s whose circle passes through it with
    // a non-negative radius (PDF 8.7.4.5.4), s clamped as /Extend does and
    // points outside an unextended end skipped. s has no extreme inside the
    // box except at the cone's apex (where the radius reaches 0), so a dense
    // grid finds the range once the apex and the circle centres (and their
    // nearest points on the box) are sampled too; the range is then widened
    // by the largest step between neighbouring grid points, for what lies
    // between them, and by 2% of the domain. Where the painted area ends
    // inside the box (beyond an unextended end, or at the edge of the cone
    // two separate circles sweep out) s jumps, and the grid cannot bound
    // it, so then nothing is trimmed.
    private static List<(double Min, double Max)>? RadialInputs(
        PdfDictionary dict, IReadOnlyList<Matrix3x3> toPage, double x0, double y0, double x1, double y1,
        IPdfObjectResolver resolver)
    {
        double[]? c = Numbers(resolver, dict.GetAs<PdfPrimitive>(PdfName.Intern("Coords")));
        double[] domain = Numbers(resolver, dict.GetAs<PdfPrimitive>(PdfName.Intern("Domain"))) ?? new double[] { 0, 1 };
        if (c is null || c.Length < 6 || domain.Length < 2)
        {
            return null;
        }

        bool extend0 = false, extend1 = false;
        if (resolver.Resolve(dict.GetAs<PdfPrimitive>(PdfName.Intern("Extend")) ?? PdfNull.Value) is PdfArray ext && ext.Count >= 2)
        {
            extend0 = resolver.Resolve(ext[0]) is PdfBoolean { Value: true };
            extend1 = resolver.Resolve(ext[1]) is PdfBoolean { Value: true };
        }

        RadialFamily family = new RadialFamily(c, extend0, extend1);
        List<(double X, double Y)> special = new List<(double X, double Y)> { (c[0], c[1]), (c[3], c[4]) };
        if (Math.Abs(family.Dr) > 1e-12)
        {
            special.Add((c[0] - (c[2] / family.Dr * family.Dcx), c[1] - (c[2] / family.Dr * family.Dcy)));
        }

        double lox = Math.Min(x0, x1), hix = Math.Max(x0, x1), loy = Math.Min(y0, y1), hiy = Math.Max(y0, y1);
        double sMin = double.PositiveInfinity, sMax = double.NegativeInfinity, step = 0;
        const int Grid = 64;
        foreach (Matrix3x3 use in toPage)
        {
            if (!ScrubGeometry.TryInvert(use, out Matrix3x3 inverse))
            {
                return null;
            }

            double?[] previousRow = new double?[Grid + 1];
            for (int gy = 0; gy <= Grid; gy++)
            {
                double? left = null;
                for (int gx = 0; gx <= Grid; gx++)
                {
                    (double px, double py) = ScrubGeometry.Transform(
                        inverse, x0 + ((x1 - x0) * gx / Grid), y0 + ((y1 - y0) * gy / Grid));
                    double? s = family.At(px, py);
                    if (s is null)
                    {
                        return null;
                    }

                    if (s is double sv)
                    {
                        sMin = Math.Min(sMin, sv);
                        sMax = Math.Max(sMax, sv);
                        if (left is double lv)
                        {
                            step = Math.Max(step, Math.Abs(sv - lv));
                        }

                        if (gy > 0 && previousRow[gx] is double uv)
                        {
                            step = Math.Max(step, Math.Abs(sv - uv));
                        }
                    }

                    left = s;
                    previousRow[gx] = s;
                }
            }

            // The special points where they lie in the box, and their nearest
            // points on its edges.
            foreach ((double sx, double sy) in special)
            {
                (double qx, double qy) = ScrubGeometry.Transform(use, sx, sy);
                double cx = Math.Clamp(qx, lox, hix), cy = Math.Clamp(qy, loy, hiy);
                foreach ((double ex, double ey) in new[] { (cx, cy), (lox, cy), (hix, cy), (cx, loy), (cx, hiy) })
                {
                    (double px, double py) = ScrubGeometry.Transform(inverse, ex, ey);
                    if (family.At(px, py) is not double sv)
                    {
                        return null;
                    }

                    sMin = Math.Min(sMin, sv);
                    sMax = Math.Max(sMax, sv);
                }
            }
        }

        if (sMin > sMax)
        {
            sMin = sMax = 0;
        }

        double widen = step + 0.02;
        sMin = Math.Max(0, sMin - widen);
        sMax = Math.Min(1, sMax + widen);
        double t0 = domain[0] + (sMin * (domain[1] - domain[0]));
        double t1 = domain[0] + (sMax * (domain[1] - domain[0]));
        return new List<(double Min, double Max)> { (Math.Min(t0, t1), Math.Max(t0, t1)) };
    }

    // The circles of a radial shading: At gives the clamped s that paints a
    // shading-space point, or null when no circle reaches it.
    private sealed class RadialFamily
    {
        private readonly double[] _c;
        private readonly bool _extend0;
        private readonly bool _extend1;
        private readonly double _qa;

        internal RadialFamily(double[] c, bool extend0, bool extend1)
        {
            _c = c;
            _extend0 = extend0;
            _extend1 = extend1;
            Dcx = c[3] - c[0];
            Dcy = c[4] - c[1];
            Dr = c[5] - c[2];
            _qa = (Dcx * Dcx) + (Dcy * Dcy) - (Dr * Dr);
        }

        internal double Dcx { get; }

        internal double Dcy { get; }

        internal double Dr { get; }

        internal double? At(double px, double py)
        {
            double qx = px - _c[0], qy = py - _c[1];
            double qb = -2 * ((qx * Dcx) + (qy * Dcy) + (_c[2] * Dr));
            double qc = (qx * qx) + (qy * qy) - (_c[2] * _c[2]);
            double? best = null;
            foreach (double root in Roots(_qa, qb, qc))
            {
                if (_c[2] + (root * Dr) < -1e-9)
                {
                    continue;
                }

                if ((root < 0 && !_extend0) || (root > 1 && !_extend1))
                {
                    continue;
                }

                best = best is null ? root : Math.Max(best.Value, root);
            }

            return best is double b ? Math.Clamp(b, 0, 1) : null;
        }
    }

    private static IEnumerable<double> Roots(double a, double b, double c)
    {
        if (Math.Abs(a) < 1e-12)
        {
            if (Math.Abs(b) > 1e-12)
            {
                yield return -c / b;
            }

            yield break;
        }

        double disc = (b * b) - (4 * a * c);
        if (disc < 0)
        {
            yield break;
        }

        double sq = Math.Sqrt(disc);
        yield return (-b + sq) / (2 * a);
        yield return (-b - sq) / (2 * a);
    }

    // Subsets one sampled function to the inputs; null when it is not a
    // sampled function of the right arity, or nothing would be removed.
    private static PdfPrimitive? TrimSampled(
        PdfPrimitive function, List<(double Min, double Max)> inputs, IPdfObjectResolver resolver,
        Func<PdfStream, PdfReference> allocate)
    {
        if (resolver.Resolve(function) is not PdfStream stream)
        {
            return null;
        }

        PdfDictionary d = stream.Dictionary;
        if (Num(resolver, d.GetAs<PdfPrimitive>(PdfName.Intern("FunctionType"))) != 0)
        {
            return null;
        }

        int m = inputs.Count;
        double[]? domain = Numbers(resolver, d.GetAs<PdfPrimitive>(PdfName.Intern("Domain")));
        double[]? range = Numbers(resolver, d.GetAs<PdfPrimitive>(PdfName.Intern("Range")));
        double[]? sizeValues = Numbers(resolver, d.GetAs<PdfPrimitive>(PdfName.Intern("Size")));
        int bps = (int)Num(resolver, d.GetAs<PdfPrimitive>(PdfName.Intern("BitsPerSample")));
        if (domain is null || domain.Length != 2 * m || range is null || range.Length < 2
            || sizeValues is null || sizeValues.Length != m
            || bps is not (1 or 2 or 4 or 8 or 12 or 16 or 24 or 32))
        {
            return null;
        }

        int n = range.Length / 2;
        int[] size = new int[m];
        for (int i = 0; i < m; i++)
        {
            size[i] = (int)sizeValues[i];
            if (size[i] < 1)
            {
                return null;
            }
        }

        double[] encode = Numbers(resolver, d.GetAs<PdfPrimitive>(PdfName.Intern("Encode"))) ?? DefaultEncode(size);
        if (encode.Length != 2 * m)
        {
            return null;
        }

        // Sample index range per input: e(x) at the input bounds, widened
        // by two samples for interpolation (cubic order included).
        int[] lo = new int[m];
        int[] hi = new int[m];
        double[] newDomain = new double[2 * m];
        double[] newEncode = new double[2 * m];
        bool shrinks = false;
        for (int i = 0; i < m; i++)
        {
            double a = domain[2 * i];
            double b = domain[(2 * i) + 1];
            double e0 = encode[2 * i];
            double e1 = encode[(2 * i) + 1];
            if (b - a < 1e-18 || Math.Abs(e1 - e0) < 1e-18)
            {
                return null;
            }

            double Ex(double x) => e0 + ((Math.Clamp(x, Math.Min(a, b), Math.Max(a, b)) - a) * (e1 - e0) / (b - a));
            double X(double e) => a + ((e - e0) * (b - a) / (e1 - e0));
            (double inMin, double inMax) = inputs[i];
            if (inMax < inMin)
            {
                inMin = inMax = (a + b) / 2;
            }

            double ea = Ex(inMin);
            double eb = Ex(inMax);
            lo[i] = Math.Clamp((int)Math.Floor(Math.Min(ea, eb)) - 2, 0, size[i] - 1);
            hi[i] = Math.Clamp((int)Math.Ceiling(Math.Max(ea, eb)) + 2, 0, size[i] - 1);
            if (lo[i] > 0 || hi[i] < size[i] - 1)
            {
                shrinks = true;
            }

            // The new domain spans exactly the kept samples (and stays
            // inside the old one); the encode keeps each input on the same
            // sample as before, shifted by the dropped ones.
            double encLo = Math.Max(lo[i], Math.Min(e0, e1));
            double encHi = Math.Min(hi[i], Math.Max(e0, e1));
            if (encHi <= encLo)
            {
                return null;
            }

            bool increasing = e1 > e0;
            double ds = X(increasing ? encLo : encHi);
            double de = X(increasing ? encHi : encLo);
            newDomain[2 * i] = ds;
            newDomain[(2 * i) + 1] = de;
            newEncode[2 * i] = (increasing ? encLo : encHi) - lo[i];
            newEncode[(2 * i) + 1] = (increasing ? encHi : encLo) - lo[i];
        }

        if (!shrinks)
        {
            return null;
        }

        byte[] data;
        try
        {
            data = ObjectImporter.DecodeStreamContent(stream);
        }
        catch (Exception)
        {
            return null;
        }

        long total = n;
        foreach (int s in size)
        {
            total *= s;
        }

        if (((total * bps) + 7) / 8 > data.LongLength)
        {
            return null;
        }

        int[] newSize = new int[m];
        long newTotal = n;
        for (int i = 0; i < m; i++)
        {
            newSize[i] = hi[i] - lo[i] + 1;
            newTotal *= newSize[i];
        }

        byte[] output = new byte[((newTotal * bps) + 7) / 8];
        long outIndex = 0;
        int[] idx = new int[m];
        Array.Copy(lo, idx, m);
        while (true)
        {
            // Sample offset: first input varies fastest.
            long sample = 0;
            long stride = 1;
            for (int i = 0; i < m; i++)
            {
                sample += idx[i] * stride;
                stride *= size[i];
            }

            for (int c = 0; c < n; c++)
            {
                CopyBits(data, ((sample * n) + c) * bps, output, outIndex * bps, bps);
                outIndex++;
            }

            int k = 0;
            while (k < m)
            {
                idx[k]++;
                if (idx[k] <= hi[k])
                {
                    break;
                }

                idx[k] = lo[k];
                k++;
            }

            if (k == m)
            {
                break;
            }
        }

        PdfDictionary newDict = new PdfDictionary();
        foreach (PdfName key in d.Keys)
        {
            if (key.Value is not ("Filter" or "DecodeParms" or "Length" or "Domain" or "Encode" or "Size"))
            {
                newDict.Set(key, d[key]);
            }
        }

        newDict.Set(PdfName.Intern("Domain"), ToArray(newDomain));
        newDict.Set(PdfName.Intern("Encode"), ToArray(newEncode));
        PdfArray sizeArray = new PdfArray([]);
        foreach (int s in newSize)
        {
            sizeArray.Add(new PdfInteger(s));
        }

        newDict.Set(PdfName.Intern("Size"), sizeArray);
        byte[] encoded = Flate(output);
        newDict.Set(PdfName.Filter, PdfName.Intern("FlateDecode"));
        newDict.Set(PdfName.Length, encoded.Length);
        return allocate(new PdfStream(newDict, encoded));
    }

    private static double[] DefaultEncode(int[] size)
    {
        double[] e = new double[2 * size.Length];
        for (int i = 0; i < size.Length; i++)
        {
            e[(2 * i) + 1] = size[i] - 1;
        }

        return e;
    }

    private static void CopyBits(byte[] src, long srcBit, byte[] dst, long dstBit, int count)
    {
        for (int b = 0; b < count; b++)
        {
            long s = srcBit + b;
            if (((src[s >> 3] >> (7 - (int)(s & 7))) & 1) != 0)
            {
                long t = dstBit + b;
                dst[t >> 3] |= (byte)(0x80 >> (int)(t & 7));
            }
        }
    }

    private static PdfArray ToArray(double[] values)
    {
        PdfArray a = new PdfArray([]);
        foreach (double v in values)
        {
            a.Add(new PdfReal(v));
        }

        return a;
    }

    private static byte[] Flate(byte[] data)
    {
        using MemoryStream ms = new MemoryStream();
        using (System.IO.Compression.ZLibStream zs =
            new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            zs.Write(data, 0, data.Length);
        }

        return ms.ToArray();
    }

    private static Matrix3x3 Matrix(IPdfObjectResolver resolver, PdfPrimitive? value)
    {
        double[]? m = Numbers(resolver, value);
        return m is { Length: >= 6 } ? new Matrix3x3(m[0], m[1], m[2], m[3], m[4], m[5]) : Matrix3x3.Identity;
    }

    private static double[]? Numbers(IPdfObjectResolver resolver, PdfPrimitive? value)
    {
        if (value is null || resolver.Resolve(value) is not PdfArray array)
        {
            return null;
        }

        double[] result = new double[array.Count];
        for (int i = 0; i < array.Count; i++)
        {
            result[i] = Num(resolver, array[i]);
        }

        return result;
    }

    private static double Num(IPdfObjectResolver resolver, PdfPrimitive? value) =>
        value is null ? 0.0 : resolver.Resolve(value) switch
        {
            PdfInteger i => i.Value,
            PdfReal r => r.Value,
            _ => 0.0,
        };
}
