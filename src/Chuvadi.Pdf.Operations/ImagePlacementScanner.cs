// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.3.4 (CTM), §8.9.5.2 (image placement via Do)
// PHASE: 3.18 — image downsampling support for the compression path.
//
// Downsampling an image to a target DPI needs to know how large the image is
// painted on the page: a 3000-pixel-wide photo placed 2 inches wide is
// 1500 DPI, a candidate for downsampling; the same photo placed full-page is
// not. This scanner walks each page's own content stream tracking q/Q/cm and,
// for every image Do, records the LARGEST placement extent per image object,
// so downsampling never undershoots any visible use. Nested form XObjects are
// not walked (their images are left un-downsampled — conservative).

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
/// Scans page content streams and reports, per image XObject object number,
/// the largest extent (in points) at which the image is painted on any page.
/// </summary>
internal static class ImagePlacementScanner
{
    /// <summary>
    /// Returns the largest placement extent, in points, per image XObject
    /// object number. An image never seen in any page's content stream has no
    /// entry (caller treats it as not-downsampleable).
    /// </summary>
    internal static Dictionary<int, (double WidthPts, double HeightPts)> Scan(PdfDocument document)
    {
        Dictionary<int, (double WidthPts, double HeightPts)> placements =
            new Dictionary<int, (double WidthPts, double HeightPts)>();
        FilterPipeline pipeline = FilterRegistry.CreateDefaultPipeline();

        // Images whose placement cannot be fully known are EXCLUDED from the
        // result (never downsampled): anything referenced from a form
        // XObject's resources (forms are not walked, so a large placement
        // inside one would be invisible here), and every image on a page
        // whose content stream could not be decoded.
        HashSet<int> excluded = new HashSet<int>();

        for (int i = 0; i < document.PageCount; i++)
        {
            PdfPage page = document.Pages[i];
            Dictionary<string, int> imageNames = CollectImageNames(page, document.Objects);
            CollectFormReferencedImages(page.Resources, document.Objects, excluded, depth: 0);

            if (imageNames.Count == 0)
            {
                continue;
            }

            byte[] content;
            try
            {
                content = DecodePageContent(page, document.Objects, pipeline);
            }
            catch (FilterException)
            {
                foreach (int objNumber in imageNames.Values)
                {
                    excluded.Add(objNumber);
                }

                continue;
            }

            ScanContent(content, imageNames, placements);
        }

        foreach (int objNumber in excluded)
        {
            placements.Remove(objNumber);
        }

        return placements;
    }

    // Collects the object numbers of every image reachable from a form
    // XObject's resources (recursively, depth-capped): their true painted
    // extents live inside form content this scanner does not walk.
    private static void CollectFormReferencedImages(
        PdfDictionary? resources, PdfObjectStore store, HashSet<int> excluded, int depth)
    {
        if (resources is null || depth > 8
            || !resources.TryGetValue(PdfName.Intern("XObject"), out PdfPrimitive? xv)
            || store.Resolve(xv) is not PdfDictionary xobjects)
        {
            return;
        }

        foreach (KeyValuePair<PdfName, PdfPrimitive> entry in xobjects)
        {
            if (store.Resolve(entry.Value) is not PdfStream stream
                || stream.Dictionary.Subtype is not PdfName subtype)
            {
                continue;
            }

            if (subtype.Value != "Form")
            {
                continue;
            }

            PdfDictionary? formResources = null;
            if (stream.Dictionary.TryGetValue(PdfName.Intern("Resources"), out PdfPrimitive? rp))
            {
                formResources = store.Resolve(rp) as PdfDictionary;
            }

            if (formResources is null
                || !formResources.TryGetValue(PdfName.Intern("XObject"), out PdfPrimitive? fxv)
                || store.Resolve(fxv) is not PdfDictionary formXObjects)
            {
                continue;
            }

            foreach (KeyValuePair<PdfName, PdfPrimitive> formEntry in formXObjects)
            {
                if (formEntry.Value is PdfReference reference
                    && store.Resolve(reference) is PdfStream inner
                    && inner.Dictionary.Subtype is PdfName innerSubtype
                    && innerSubtype.Value == "Image")
                {
                    excluded.Add(reference.ObjectId.ObjectNumber);
                }
            }

            CollectFormReferencedImages(formResources, store, excluded, depth + 1);
        }
    }

    // Maps each /XObject resource name on the page to its image object number.
    private static Dictionary<string, int> CollectImageNames(PdfPage page, PdfObjectStore store)
    {
        Dictionary<string, int> names = new Dictionary<string, int>(StringComparer.Ordinal);

        PdfDictionary? resources = page.Resources;
        if (resources is null
            || !resources.TryGetValue(PdfName.Intern("XObject"), out PdfPrimitive? xv)
            || store.Resolve(xv) is not PdfDictionary xobjects)
        {
            return names;
        }

        foreach (KeyValuePair<PdfName, PdfPrimitive> entry in xobjects)
        {
            if (entry.Value is not PdfReference reference)
            {
                continue;
            }

            if (store.Resolve(reference) is PdfStream stream
                && stream.Dictionary.Subtype is PdfName subtype
                && subtype.Value == "Image")
            {
                names[entry.Key.Value] = reference.ObjectId.ObjectNumber;
            }
        }

        return names;
    }

    private static void ScanContent(
        byte[] content,
        Dictionary<string, int> imageNames,
        Dictionary<int, (double WidthPts, double HeightPts)> placements)
    {
        Matrix3x3 ctm = Matrix3x3.Identity;
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
                    if (lastName is not null && imageNames.TryGetValue(lastName, out int objNumber))
                    {
                        // The image paints the unit square under the CTM; its
                        // extent is the length of the transformed basis vectors.
                        double widthPts = Math.Sqrt((ctm.A * ctm.A) + (ctm.B * ctm.B));
                        double heightPts = Math.Sqrt((ctm.C * ctm.C) + (ctm.D * ctm.D));

                        if (placements.TryGetValue(objNumber, out (double WidthPts, double HeightPts) prior))
                        {
                            placements[objNumber] = (
                                Math.Max(prior.WidthPts, widthPts),
                                Math.Max(prior.HeightPts, heightPts));
                        }
                        else
                        {
                            placements[objNumber] = (widthPts, heightPts);
                        }
                    }

                    break;

                default:
                    break;
            }

            nums.Clear();
            lastName = null;
        }
    }

    // Consumes an inline image's dictionary tokens, binary data and EI,
    // using the exact-extent helper so a false "EI" inside the binary data
    // cannot misalign the scan.
    private static void SkipInlineImage(PdfTokenizer tok, byte[] content)
    {
        int end = InlineImageExtent.FindEnd(tok, content);
        tok.Seek(end < 0 ? content.Length : end);
    }

    private static byte[] DecodePageContent(
        PdfPage page, PdfObjectStore store, FilterPipeline pipeline)
    {
        using MemoryStream merged = new MemoryStream();

        if (page.Dictionary.TryGetValue(PdfName.Intern("Contents"), out PdfPrimitive? contentsPrim))
        {
            PdfPrimitive resolved = store.Resolve(contentsPrim);
            if (resolved is PdfArray array)
            {
                foreach (PdfPrimitive item in array)
                {
                    AppendDecoded(store.Resolve(item) as PdfStream, pipeline, merged);
                }
            }
            else
            {
                AppendDecoded(resolved as PdfStream, pipeline, merged);
            }
        }

        return merged.ToArray();
    }

    private static void AppendDecoded(PdfStream? stream, FilterPipeline pipeline, MemoryStream merged)
    {
        if (stream is null)
        {
            return;
        }

        byte[] bytes;
        if (stream.Filter is PdfName filter)
        {
            bytes = pipeline.Decode(FilterRegistry.ResolveAlias(filter.Value), stream.RawBytes, null);
        }
        else
        {
            bytes = stream.RawBytes;
        }

        merged.Write(bytes, 0, bytes.Length);
        merged.WriteByte((byte)'\n');
    }
}
