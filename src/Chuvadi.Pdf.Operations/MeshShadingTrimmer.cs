// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.7.4.5.5 (free-form triangle meshes, type 4),
//        §8.7.4.5.6 (lattice-form triangle meshes, type 5),
//        §8.7.4.5.7 (Coons patch meshes, type 6),
//        §8.7.4.5.8 (tensor-product patch meshes, type 7)
// PHASE: 3.22 — Scrub crop: mesh shadings keep only their in-box elements.

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Content;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// Trims a mesh shading (types 4 to 7) to the elements — triangles or
/// patches — that can paint inside the crop box. A mesh can carry a picture
/// in its vertex colours, so the elements wholly outside the box are removed
/// at the byte level, not merely clipped from view.
/// </summary>
/// <remarks>
/// <para>
/// Each kept element is re-encoded as a stand-alone record (flag 0), so
/// edge sharing with a removed neighbour is resolved. A lattice mesh (type 5)
/// is rewritten as a free-form triangle mesh (type 4) holding the kept cells'
/// triangles. Every field is written in whole bytes (flags 8 bits;
/// coordinates and colour components widened to 8, 16, 24 or 32 bits, with
/// the same <c>/Decode</c> ranges), so the result reads the same whether a
/// reader aligns records to byte boundaries or not.
/// </para>
/// <para>
/// An element is kept when the bounding box of its control points, mapped
/// to page space through any of the shading's uses, overlaps the crop box.
/// A mesh whose data cannot be parsed unambiguously is removed whole
/// (over-redaction in doubt, B15).
/// </para>
/// </remarks>
internal static class MeshShadingTrimmer
{
    /// <summary>What trimming a shading produced.</summary>
    internal enum Outcome
    {
        /// <summary>Not a mesh shading, or every element can paint in the box.</summary>
        Unchanged,

        /// <summary>Some elements were removed; <c>trimmed</c> holds the result.</summary>
        Trimmed,
    }

    // One element: control points (raw coordinate pairs) and corner colours
    // (raw component tuples), in the record order of its shading type.
    private sealed class Element
    {
        internal List<(ulong X, ulong Y)> Points = new List<(ulong X, ulong Y)>();
        internal List<ulong[]> Colors = new List<ulong[]>();
    }

    private sealed class Layout
    {
        internal int Type;
        internal int BitsPerCoordinate;
        internal int BitsPerComponent;
        internal int BitsPerFlag;
        internal int VerticesPerRow;
        internal int Components;
        internal double[] Decode = Array.Empty<double>();
    }

    /// <summary>
    /// Trims <paramref name="shading"/> to the elements that can paint inside
    /// [x0,x1]x[y0,y1] (page space) under any of <paramref name="toPage"/>
    /// (shading space to page space).
    /// </summary>
    internal static Outcome Trim(
        PdfPrimitive shading, IReadOnlyList<Matrix3x3> toPage,
        double x0, double y0, double x1, double y1,
        IPdfObjectResolver resolver, out PdfStream? trimmed)
    {
        trimmed = null;
        if (resolver.Resolve(shading) is not PdfStream stream)
        {
            return Outcome.Unchanged;
        }

        PdfDictionary dict = stream.Dictionary;
        int type = IntOf(resolver, dict, "ShadingType");
        if (type is < 4 or > 7)
        {
            return Outcome.Unchanged;
        }

        if (toPage.Count == 0)
        {
            trimmed = Rebuild(dict, null, new List<Element>());
            return Outcome.Trimmed;
        }

        Layout? layout = ReadLayout(dict, type, resolver);
        List<Element>? elements = null;
        if (layout is not null)
        {
            try
            {
                elements = Parse(ObjectImporter.DecodeStreamContent(stream), layout);
            }
            catch (Exception)
            {
                elements = null;
            }
        }

        if (layout is null || elements is null)
        {
            // Unreadable: the whole mesh is removed (B15).
            trimmed = Rebuild(dict, null, new List<Element>());
            return Outcome.Trimmed;
        }

        List<Element> kept = new List<Element>(elements.Count);
        foreach (Element element in elements)
        {
            if (CanPaintInBox(element, layout, toPage, x0, y0, x1, y1))
            {
                kept.Add(element);
            }
        }

        if (kept.Count == elements.Count)
        {
            return Outcome.Unchanged;
        }

        trimmed = Rebuild(dict, layout, kept);
        return Outcome.Trimmed;
    }

    private static Layout? ReadLayout(PdfDictionary dict, int type, IPdfObjectResolver resolver)
    {
        Layout layout = new Layout
        {
            Type = type,
            BitsPerCoordinate = IntOf(resolver, dict, "BitsPerCoordinate"),
            BitsPerComponent = IntOf(resolver, dict, "BitsPerComponent"),
            BitsPerFlag = IntOf(resolver, dict, "BitsPerFlag"),
            VerticesPerRow = IntOf(resolver, dict, "VerticesPerRow"),
        };

        if (layout.BitsPerCoordinate is not (1 or 2 or 4 or 8 or 12 or 16 or 24 or 32)
            || layout.BitsPerComponent is not (1 or 2 or 4 or 8 or 12 or 16))
        {
            return null;
        }

        if (type == 5)
        {
            if (layout.VerticesPerRow < 2)
            {
                return null;
            }
        }
        else if (layout.BitsPerFlag is not (2 or 4 or 8))
        {
            return null;
        }

        layout.Components = dict.ContainsKey(PdfName.Intern("Function"))
            ? 1
            : ImageSampleCropper.ComponentsOf(
                resolver.Resolve(dict.GetAs<PdfPrimitive>(PdfName.Intern("ColorSpace")) ?? PdfNull.Value), resolver);
        if (layout.Components <= 0)
        {
            return null;
        }

        if (resolver.Resolve(dict.GetAs<PdfPrimitive>(PdfName.Intern("Decode")) ?? PdfNull.Value) is not PdfArray decode
            || decode.Count < 4 + (2 * layout.Components))
        {
            return null;
        }

        layout.Decode = new double[decode.Count];
        for (int i = 0; i < decode.Count; i++)
        {
            layout.Decode[i] = resolver.Resolve(decode[i]) switch
            {
                PdfInteger n => n.Value,
                PdfReal r => r.Value,
                _ => 0.0,
            };
        }

        return layout;
    }

    // Parses under both record conventions (byte-aligned records, and packed
    // records); the data must parse completely under at least one, and the
    // two must agree when both do.
    private static List<Element>? Parse(byte[] data, Layout layout)
    {
        List<Element>? aligned = ParseWith(data, layout, align: true);
        List<Element>? packed = ParseWith(data, layout, align: false);
        if (aligned is null)
        {
            return packed;
        }

        if (packed is null)
        {
            return aligned;
        }

        return SameElements(aligned, packed) ? aligned : null;
    }

    private static List<Element>? ParseWith(byte[] data, Layout layout, bool align)
    {
        BitReader reader = new BitReader(data);
        return layout.Type switch
        {
            4 => ParseFreeForm(reader, layout, align),
            5 => ParseLattice(reader, layout, align),
            _ => ParsePatches(reader, layout, align),
        };
    }

    private static List<Element>? ParseFreeForm(BitReader reader, Layout layout, bool align)
    {
        List<Element> triangles = new List<Element>();
        List<((ulong X, ulong Y) P, ulong[] C)> pending = new List<((ulong X, ulong Y) P, ulong[] C)>();
        ((ulong X, ulong Y) P, ulong[] C) va = default, vb = default, vc = default;
        bool haveTriangle = false;
        int vertexBits = layout.BitsPerFlag + (2 * layout.BitsPerCoordinate) + (layout.Components * layout.BitsPerComponent);
        while (reader.Remaining(vertexBits, align))
        {
            ulong flag = reader.Read(layout.BitsPerFlag);
            ((ulong X, ulong Y) P, ulong[] C) vertex = ReadVertex(reader, layout);
            if (align)
            {
                reader.Align();
            }

            if (pending.Count == 0)
            {
                if (flag == 0)
                {
                    pending.Add(vertex);
                    continue;
                }

                if (flag > 2 || !haveTriangle)
                {
                    return null;
                }

                ((ulong X, ulong Y) P, ulong[] C) first = flag == 1 ? vb : va;
                va = first;
                vb = vc;
                vc = vertex;
                triangles.Add(Triangle(va, vb, vc));
                continue;
            }

            pending.Add(vertex);
            if (pending.Count == 3)
            {
                va = pending[0];
                vb = pending[1];
                vc = pending[2];
                haveTriangle = true;
                pending.Clear();
                triangles.Add(Triangle(va, vb, vc));
            }
        }

        return pending.Count == 0 && reader.AtEnd(align) ? triangles : null;
    }

    private static List<Element>? ParseLattice(BitReader reader, Layout layout, bool align)
    {
        int vertexBits = (2 * layout.BitsPerCoordinate) + (layout.Components * layout.BitsPerComponent);
        List<((ulong X, ulong Y) P, ulong[] C)> vertices = new List<((ulong X, ulong Y) P, ulong[] C)>();
        while (reader.Remaining(vertexBits, align))
        {
            vertices.Add(ReadVertex(reader, layout));
            if (align)
            {
                reader.Align();
            }
        }

        int perRow = layout.VerticesPerRow;
        if (!reader.AtEnd(align) || vertices.Count % perRow != 0 || vertices.Count < 2 * perRow)
        {
            return null;
        }

        int rows = vertices.Count / perRow;
        List<Element> triangles = new List<Element>();
        for (int r = 0; r + 1 < rows; r++)
        {
            for (int c = 0; c + 1 < perRow; c++)
            {
                ((ulong X, ulong Y) P, ulong[] C) a = vertices[(r * perRow) + c];
                ((ulong X, ulong Y) P, ulong[] C) b = vertices[(r * perRow) + c + 1];
                ((ulong X, ulong Y) P, ulong[] C) d = vertices[((r + 1) * perRow) + c];
                ((ulong X, ulong Y) P, ulong[] C) e = vertices[((r + 1) * perRow) + c + 1];
                triangles.Add(Triangle(a, b, d));
                triangles.Add(Triangle(b, e, d));
            }
        }

        return triangles;
    }

    // Coons (12 control points) and tensor-product (16) patches. A patch
    // with flag 1, 2 or 3 shares an edge (four points, two colours) with the
    // previous patch, in boundary record order (§8.7.4.5.7, Table 86).
    private static List<Element>? ParsePatches(BitReader reader, Layout layout, bool align)
    {
        int pointCount = layout.Type == 6 ? 12 : 16;
        List<Element> patches = new List<Element>();
        Element? previous = null;
        int minBits = layout.BitsPerFlag + (8 * 2 * layout.BitsPerCoordinate) + (2 * layout.Components * layout.BitsPerComponent);
        while (reader.Remaining(minBits, align))
        {
            ulong flag = reader.Read(layout.BitsPerFlag);
            if (flag > 3 || (flag != 0 && previous is null))
            {
                return null;
            }

            Element patch = new Element();
            int explicitPoints = flag == 0 ? pointCount : pointCount - 4;
            int explicitColors = flag == 0 ? 4 : 2;
            if (flag != 0)
            {
                int[] shared = flag switch
                {
                    1 => new[] { 3, 4, 5, 6 },
                    2 => new[] { 6, 7, 8, 9 },
                    _ => new[] { 9, 10, 11, 0 },
                };
                int[] sharedColors = flag switch
                {
                    1 => new[] { 1, 2 },
                    2 => new[] { 2, 3 },
                    _ => new[] { 3, 0 },
                };
                foreach (int i in shared)
                {
                    patch.Points.Add(previous!.Points[i]);
                }

                foreach (int i in sharedColors)
                {
                    patch.Colors.Add(previous!.Colors[i]);
                }
            }

            int needed = (explicitPoints * 2 * layout.BitsPerCoordinate) + (explicitColors * layout.Components * layout.BitsPerComponent);
            if (!reader.Has(needed))
            {
                return null;
            }

            for (int i = 0; i < explicitPoints; i++)
            {
                ulong x = reader.Read(layout.BitsPerCoordinate);
                ulong y = reader.Read(layout.BitsPerCoordinate);
                patch.Points.Add((x, y));
            }

            for (int i = 0; i < explicitColors; i++)
            {
                patch.Colors.Add(ReadColor(reader, layout));
            }

            if (align)
            {
                reader.Align();
            }

            patches.Add(patch);
            previous = patch;
        }

        return reader.AtEnd(align) ? patches : null;
    }

    private static ((ulong X, ulong Y) P, ulong[] C) ReadVertex(BitReader reader, Layout layout)
    {
        ulong x = reader.Read(layout.BitsPerCoordinate);
        ulong y = reader.Read(layout.BitsPerCoordinate);
        return ((x, y), ReadColor(reader, layout));
    }

    private static ulong[] ReadColor(BitReader reader, Layout layout)
    {
        ulong[] color = new ulong[layout.Components];
        for (int i = 0; i < color.Length; i++)
        {
            color[i] = reader.Read(layout.BitsPerComponent);
        }

        return color;
    }

    private static Element Triangle(
        ((ulong X, ulong Y) P, ulong[] C) a, ((ulong X, ulong Y) P, ulong[] C) b, ((ulong X, ulong Y) P, ulong[] C) c)
    {
        Element e = new Element();
        e.Points.Add(a.P);
        e.Points.Add(b.P);
        e.Points.Add(c.P);
        e.Colors.Add(a.C);
        e.Colors.Add(b.C);
        e.Colors.Add(c.C);
        return e;
    }

    private static bool SameElements(List<Element> a, List<Element> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].Points.Count != b[i].Points.Count || a[i].Colors.Count != b[i].Colors.Count)
            {
                return false;
            }

            for (int p = 0; p < a[i].Points.Count; p++)
            {
                if (a[i].Points[p] != b[i].Points[p])
                {
                    return false;
                }
            }

            for (int c = 0; c < a[i].Colors.Count; c++)
            {
                if (!a[i].Colors[c].AsSpan().SequenceEqual(b[i].Colors[c]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool CanPaintInBox(
        Element element, Layout layout, IReadOnlyList<Matrix3x3> toPage,
        double x0, double y0, double x1, double y1)
    {
        double max = Math.Pow(2, layout.BitsPerCoordinate) - 1;
        foreach (Matrix3x3 m in toPage)
        {
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
            foreach ((ulong rx, ulong ry) in element.Points)
            {
                double sx = layout.Decode[0] + (rx * (layout.Decode[1] - layout.Decode[0]) / max);
                double sy = layout.Decode[2] + (ry * (layout.Decode[3] - layout.Decode[2]) / max);
                (double px, double py) = ScrubGeometry.Transform(m, sx, sy);
                minX = Math.Min(minX, px);
                minY = Math.Min(minY, py);
                maxX = Math.Max(maxX, px);
                maxY = Math.Max(maxY, py);
            }

            if (maxX > x0 && minX < x1 && maxY > y0 && minY < y1)
            {
                return true;
            }
        }

        return false;
    }

    // Writes the kept elements as stand-alone records (flag 0) in whole-byte
    // fields; a lattice becomes a free-form triangle mesh. A null layout
    // (unreadable data) or no kept element gives an empty mesh.
    private static PdfStream Rebuild(PdfDictionary dict, Layout? layout, List<Element> kept)
    {
        PdfDictionary copy = new PdfDictionary();
        foreach (PdfName key in dict.Keys)
        {
            if (key.Value is "Filter" or "DecodeParms" or "Length" or "DL")
            {
                continue;
            }

            copy.Set(key, dict[key]);
        }

        byte[] data = Array.Empty<byte>();
        if (layout is not null && kept.Count > 0)
        {
            int coordBits = WholeBytes(layout.BitsPerCoordinate);
            int compBits = WholeBytes(layout.BitsPerComponent);
            BitWriter writer = new BitWriter();
            foreach (Element element in kept)
            {
                if (layout.Type is 4 or 5)
                {
                    for (int v = 0; v < 3; v++)
                    {
                        writer.Write(0, 8);
                        WritePoint(writer, element.Points[v], layout.BitsPerCoordinate, coordBits);
                        WriteColor(writer, element.Colors[v], layout.BitsPerComponent, compBits);
                    }
                }
                else
                {
                    writer.Write(0, 8);
                    foreach ((ulong X, ulong Y) point in element.Points)
                    {
                        WritePoint(writer, point, layout.BitsPerCoordinate, coordBits);
                    }

                    foreach (ulong[] color in element.Colors)
                    {
                        WriteColor(writer, color, layout.BitsPerComponent, compBits);
                    }
                }
            }

            data = writer.ToArray();
            copy.Set(PdfName.Intern("BitsPerCoordinate"), coordBits);
            copy.Set(PdfName.Intern("BitsPerComponent"), compBits);
            copy.Set(PdfName.Intern("BitsPerFlag"), 8);
            if (layout.Type == 5)
            {
                copy.Set(PdfName.Intern("ShadingType"), 4);
                copy.Remove(PdfName.Intern("VerticesPerRow"));
            }
        }

        byte[] encoded = FlateEncode(data);
        copy.Set(PdfName.Filter, PdfName.Intern("FlateDecode"));
        copy.Set(PdfName.Length, encoded.Length);
        return new PdfStream(copy, encoded);
    }

    private static int WholeBytes(int bits) => bits <= 8 ? 8 : bits <= 16 ? 16 : bits <= 24 ? 24 : 32;

    private static void WritePoint(BitWriter writer, (ulong X, ulong Y) point, int fromBits, int toBits)
    {
        writer.Write(Widen(point.X, fromBits, toBits), toBits);
        writer.Write(Widen(point.Y, fromBits, toBits), toBits);
    }

    private static void WriteColor(BitWriter writer, ulong[] color, int fromBits, int toBits)
    {
        foreach (ulong c in color)
        {
            writer.Write(Widen(c, fromBits, toBits), toBits);
        }
    }

    // Rescales a raw value to a wider field with the same decoded value
    // (exact when the old maximum divides the new, as for 1, 2 and 4 bits
    // widened to 8; nearest otherwise).
    private static ulong Widen(ulong value, int fromBits, int toBits)
    {
        if (fromBits == toBits)
        {
            return value;
        }

        ulong fromMax = (1UL << fromBits) - 1;
        ulong toMax = (1UL << toBits) - 1;
        return (ulong)Math.Round((double)value * toMax / fromMax);
    }

    private static byte[] FlateEncode(byte[] data)
    {
        using MemoryStream ms = new MemoryStream();
        using (System.IO.Compression.ZLibStream zs =
            new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            zs.Write(data, 0, data.Length);
        }

        return ms.ToArray();
    }

    private static int IntOf(IPdfObjectResolver resolver, PdfDictionary d, string key)
    {
        if (d.TryGetValue(PdfName.Intern(key), out PdfPrimitive? v))
        {
            PdfPrimitive r = resolver.Resolve(v);
            if (r is PdfInteger i)
            {
                return (int)i.Value;
            }
        }

        return 0;
    }

    private sealed class BitReader
    {
        private readonly byte[] _data;
        private long _bit;

        internal BitReader(byte[] data)
        {
            _data = data;
        }

        private long TotalBits => (long)_data.Length * 8;

        // True when a further record of at least recordBits can start: there
        // are that many bits left (and, packed, more than padding remains).
        internal bool Remaining(int recordBits, bool align)
        {
            long left = TotalBits - _bit;
            if (align)
            {
                return left >= recordBits;
            }

            return left >= recordBits && left >= 8;
        }

        internal bool Has(int bits) => TotalBits - _bit >= bits;

        // The data is fully consumed: nothing left (aligned) or only the
        // zero padding of the last byte (packed).
        internal bool AtEnd(bool align)
        {
            long left = TotalBits - _bit;
            return align ? left == 0 : left < 8;
        }

        internal ulong Read(int bits)
        {
            if (!Has(bits))
            {
                throw new InvalidDataException("Mesh data ends inside a record.");
            }

            ulong value = 0;
            for (int i = 0; i < bits; i++)
            {
                int bit = (_data[_bit >> 3] >> (7 - (int)(_bit & 7))) & 1;
                value = (value << 1) | (uint)bit;
                _bit++;
            }

            return value;
        }

        internal void Align()
        {
            _bit = (_bit + 7) & ~7L;
        }
    }

    private sealed class BitWriter
    {
        private readonly MemoryStream _out = new MemoryStream();

        // Every field written is a whole number of bytes.
        internal void Write(ulong value, int bits)
        {
            for (int shift = bits - 8; shift >= 0; shift -= 8)
            {
                _out.WriteByte((byte)(value >> shift));
            }
        }

        internal byte[] ToArray() => _out.ToArray();
    }
}
