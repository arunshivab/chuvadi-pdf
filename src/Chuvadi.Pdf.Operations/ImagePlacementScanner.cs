// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.3.4 (CTM), §8.9.5.2 (image placement via Do),
//        §8.10 (form XObjects: /Matrix, /Resources inheritance)
// PHASE: 3.18 — image downsampling support for the compression path.
//        3.20 — form XObjects are walked, so images inside forms are measured.
//
// Downsampling an image to a target DPI needs to know how large the image is
// painted on the page: a 3000-pixel-wide photo placed 2 inches wide is
// 1500 DPI, a candidate for downsampling; the same photo placed full-page is
// not. This scanner walks each page's content stream tracking q/Q/cm and, for
// every image Do, records the LARGEST placement extent per image object, so
// downsampling never undershoots any visible use. Form XObjects invoked by Do
// are walked recursively under their /Matrix and the invoking CTM, with
// resource inheritance. Any image whose every use cannot be measured — one
// reachable from a pattern, a soft mask, a Type 3 font, an annotation
// appearance, a form nested too deep or recursive, or content that cannot be
// decoded — is excluded (never downsampled).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Chuvadi.Pdf.Content;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Filters;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// Scans page content streams (and the form XObjects they invoke) and
/// reports, per image XObject object number, the largest extent (in points)
/// at which the image is painted on any page.
/// </summary>
internal static class ImagePlacementScanner
{
    private const int MaxFormDepth = 8;

    /// <summary>
    /// Returns the largest placement extent, in points, per image XObject
    /// object number. An image never seen painted has no entry, and neither
    /// has any image with a use the scanner cannot measure (caller treats both
    /// as not-downsampleable).
    /// </summary>
    internal static Dictionary<int, (double WidthPts, double HeightPts)> Scan(PdfDocument document)
    {
        Dictionary<int, (double WidthPts, double HeightPts)> placements =
            new Dictionary<int, (double WidthPts, double HeightPts)>();
        ScanState state = new ScanState(document.Objects, placements);

        for (int i = 0; i < document.PageCount; i++)
        {
            PdfPage page = document.Pages[i];
            PdfDictionary? resources = page.Resources;

            // Images reachable through annotation appearances are painted at
            // extents this scanner does not measure.
            if (page.Dictionary.TryGetValue(PdfName.Intern("Annots"), out PdfPrimitive? annotsValue)
                && state.Store.Resolve(annotsValue) is PdfArray annots)
            {
                foreach (PdfPrimitive item in annots)
                {
                    if (state.Store.Resolve(item) is PdfDictionary annot
                        && annot.TryGetValue(PdfName.Intern("AP"), out PdfPrimitive? ap))
                    {
                        state.ExcludeReachable(ap);
                    }
                }
            }

            byte[] content;
            try
            {
                content = DecodePageContent(page, state.Store);
            }
            catch (FilterException)
            {
                if (resources is not null)
                {
                    state.ExcludeReachable(resources);
                }

                continue;
            }

            ScanContent(content, resources, Matrix3x3.Identity, depth: 0, state);
        }

        foreach (int objNumber in state.Excluded)
        {
            placements.Remove(objNumber);
        }

        return placements;
    }

    private static void ScanContent(
        byte[] content, PdfDictionary? resources, Matrix3x3 baseCtm, int depth, ScanState state)
    {
        PdfDictionary? xobjects = null;
        if (resources is not null)
        {
            state.ExcludeNonXObjectResources(resources);
            if (resources.TryGetValue(PdfName.Intern("XObject"), out PdfPrimitive? xv))
            {
                xobjects = state.Store.Resolve(xv) as PdfDictionary;
            }
        }

        Matrix3x3 ctm = baseCtm;
        Stack<Matrix3x3> stack = new Stack<Matrix3x3>();
        List<double> nums = new List<double>();
        string? lastName = null;

        using MemoryStream ms = new MemoryStream(content);
        using PdfTokenizer tok = new PdfTokenizer(ms, leaveOpen: false);

        while (true)
        {
            PdfToken token = tok.Read();
            if (token.IsEndOfStream)
            {
                break;
            }

            if (token.Type != PdfTokenType.Keyword)
            {
                if (token.IsNumeric && double.TryParse(
                    token.RawText, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                {
                    nums.Add(v);
                }
                else if (token.Type == PdfTokenType.Name)
                {
                    lastName = token.RawText.TrimStart('/');
                }

                continue;
            }

            switch (token.RawText)
            {
                // Inline image: the binary after ID is not tokenizable, so the
                // whole BI…EI run is skipped as a unit to keep the scan aligned.
                case "BI":
                    SkipInlineImage(tok, content);
                    break;

                case "q":
                    stack.Push(ctm);
                    break;

                case "Q":
                    if (stack.Count > 0)
                    {
                        ctm = stack.Pop();
                    }

                    break;

                case "cm":
                    if (nums.Count >= 6)
                    {
                        int n = nums.Count;
                        Matrix3x3 m = new Matrix3x3(
                            nums[n - 6], nums[n - 5], nums[n - 4],
                            nums[n - 3], nums[n - 2], nums[n - 1]);
                        ctm = m.Multiply(ctm);
                    }

                    break;

                case "Do":
                    if (lastName is not null && xobjects is not null
                        && xobjects.TryGetValue(PdfName.Intern(lastName), out PdfPrimitive? target))
                    {
                        HandleDo(target, ctm, resources, depth, state);
                    }

                    break;

                default:
                    break;
            }

            nums.Clear();
            lastName = null;
        }
    }

    private static void HandleDo(
        PdfPrimitive target, Matrix3x3 ctm, PdfDictionary? resources, int depth, ScanState state)
    {
        if (state.Store.Resolve(target) is not PdfStream stream
            || stream.Dictionary.Subtype is not PdfName subtype)
        {
            return;
        }

        if (subtype.Value == "Image")
        {
            if (target is PdfReference reference)
            {
                // The image paints the unit square under the CTM; its extent
                // is the length of the transformed basis vectors.
                double widthPts = Math.Sqrt((ctm.A * ctm.A) + (ctm.B * ctm.B));
                double heightPts = Math.Sqrt((ctm.C * ctm.C) + (ctm.D * ctm.D));
                state.Record(reference.ObjectId.ObjectNumber, widthPts, heightPts);
            }

            return;
        }

        if (subtype.Value != "Form")
        {
            return;
        }

        PdfDictionary? formResources = stream.Dictionary.TryGetValue(PdfName.Resources, out PdfPrimitive? rv)
            ? state.Store.Resolve(rv) as PdfDictionary
            : null;

        // A form too deep or already being walked (a cycle) cannot be
        // measured: everything it can reach is excluded.
        if (depth >= MaxFormDepth || !state.Active.Add(stream))
        {
            state.ExcludeReachable(stream);
            return;
        }

        try
        {
            byte[] formContent;
            try
            {
                formContent = ObjectImporter.DecodeStreamContent(stream);
            }
            catch (FilterException)
            {
                state.ExcludeReachable(stream);
                if (formResources is null && resources is not null)
                {
                    state.ExcludeReachable(resources);
                }

                return;
            }

            Matrix3x3 formMatrix = ReadMatrix(stream.Dictionary, state.Store);
            ScanContent(formContent, formResources ?? resources, formMatrix.Multiply(ctm), depth + 1, state);
        }
        finally
        {
            state.Active.Remove(stream);
        }
    }

    private static Matrix3x3 ReadMatrix(PdfDictionary dict, PdfObjectStore store)
    {
        if (!dict.TryGetValue(PdfName.Intern("Matrix"), out PdfPrimitive? mv)
            || store.Resolve(mv) is not PdfArray array
            || array.Count != 6)
        {
            return Matrix3x3.Identity;
        }

        double[] v = new double[6];
        for (int i = 0; i < 6; i++)
        {
            switch (store.Resolve(array[i]))
            {
                case PdfInteger integer:
                    v[i] = integer.Value;
                    break;
                case PdfReal real:
                    v[i] = real.Value;
                    break;
                default:
                    return Matrix3x3.Identity;
            }
        }

        return new Matrix3x3(v[0], v[1], v[2], v[3], v[4], v[5]);
    }

    // Consumes an inline image's dictionary tokens, binary data and EI,
    // using the exact-extent helper so a false "EI" inside the binary data
    // cannot misalign the scan.
    private static void SkipInlineImage(PdfTokenizer tok, byte[] content)
    {
        int end = InlineImageExtent.FindEnd(tok, content);
        tok.Seek(end < 0 ? content.Length : end);
    }

    private static byte[] DecodePageContent(PdfPage page, PdfObjectStore store)
    {
        using MemoryStream merged = new MemoryStream();

        if (page.Dictionary.TryGetValue(PdfName.Intern("Contents"), out PdfPrimitive? contentsPrim))
        {
            PdfPrimitive resolved = store.Resolve(contentsPrim);
            if (resolved is PdfArray array)
            {
                foreach (PdfPrimitive item in array)
                {
                    AppendDecoded(store.Resolve(item) as PdfStream, merged);
                }
            }
            else
            {
                AppendDecoded(resolved as PdfStream, merged);
            }
        }

        return merged.ToArray();
    }

    // Decodes through the stream's whole /Filter chain with its /DecodeParms.
    private static void AppendDecoded(PdfStream? stream, MemoryStream merged)
    {
        if (stream is null)
        {
            return;
        }

        byte[] bytes = ObjectImporter.DecodeStreamContent(stream);
        merged.Write(bytes, 0, bytes.Length);
        merged.WriteByte((byte)'\n');
    }

    private sealed class ScanState
    {
        private readonly Dictionary<int, (double WidthPts, double HeightPts)> _placements;
        private readonly HashSet<PdfPrimitive> _excludeVisited =
            new HashSet<PdfPrimitive>(ReferenceEqualityComparer.Instance);

        internal ScanState(PdfObjectStore store, Dictionary<int, (double WidthPts, double HeightPts)> placements)
        {
            Store = store;
            _placements = placements;
        }

        internal PdfObjectStore Store { get; }

        internal HashSet<int> Excluded { get; } = new HashSet<int>();

        internal HashSet<PdfStream> Active { get; } = new HashSet<PdfStream>(ReferenceEqualityComparer.Instance);

        internal void Record(int objNumber, double widthPts, double heightPts)
        {
            if (_placements.TryGetValue(objNumber, out (double WidthPts, double HeightPts) prior))
            {
                _placements[objNumber] = (Math.Max(prior.WidthPts, widthPts), Math.Max(prior.HeightPts, heightPts));
            }
            else
            {
                _placements[objNumber] = (widthPts, heightPts);
            }
        }

        // Images reachable from a resource dictionary through anything other
        // than its /XObject entry (patterns, soft-mask groups, Type 3 fonts)
        // are painted at extents the walk does not measure.
        internal void ExcludeNonXObjectResources(PdfDictionary resources)
        {
            foreach (KeyValuePair<PdfName, PdfPrimitive> entry in resources)
            {
                if (entry.Key.Value != "XObject")
                {
                    ExcludeReachable(entry.Value);
                }
            }
        }

        // Excludes every image stream reachable from a value (never through
        // /Parent, which leads back up the page tree).
        internal void ExcludeReachable(PdfPrimitive root)
        {
            Stack<PdfPrimitive> work = new Stack<PdfPrimitive>();
            work.Push(root);
            while (work.Count > 0)
            {
                PdfPrimitive current = work.Pop();
                if (current is PdfReference reference)
                {
                    PdfPrimitive resolved = Store.Resolve(reference);
                    if (resolved is PdfStream image && image.Dictionary.Subtype is PdfName st && st.Value == "Image")
                    {
                        Excluded.Add(reference.ObjectId.ObjectNumber);
                    }

                    current = resolved;
                }

                if (!_excludeVisited.Add(current))
                {
                    continue;
                }

                PdfDictionary? dict = current switch
                {
                    PdfStream s => s.Dictionary,
                    PdfDictionary d => d,
                    _ => null,
                };

                if (dict is not null)
                {
                    foreach (KeyValuePair<PdfName, PdfPrimitive> entry in dict)
                    {
                        if (entry.Key.Value != "Parent")
                        {
                            work.Push(entry.Value);
                        }
                    }
                }
                else if (current is PdfArray array)
                {
                    foreach (PdfPrimitive item in array)
                    {
                        work.Push(item);
                    }
                }
            }
        }
    }
}
