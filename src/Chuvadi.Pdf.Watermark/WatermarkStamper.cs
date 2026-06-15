// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §11.6.4 — Transparency groups and opacity
//        PDF 32000-1:2008 §8.4.5 — Graphics state parameter dictionaries
//        PDF 32000-1:2008 §7.8.2 — Content streams
// PHASE: Phase 2 — Chuvadi.Pdf.Watermark
// Applies text and image watermarks to PDF pages.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Images;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Watermark;

/// <summary>
/// Stamps text or image watermarks onto PDF pages by appending new content
/// streams, preserving the original page content.
/// </summary>
/// <remarks>
/// Watermarks are applied as additional content streams appended to each
/// targeted page. The original content is untouched. Opacity is implemented
/// via PDF ExtGState (/ca fill opacity, PDF 32000-1:2008 §11.6.4.4).
///
/// Standard PDF font names (no embedding required):
/// Helvetica, Helvetica-Bold, Helvetica-Oblique, Helvetica-BoldOblique,
/// Times-Roman, Times-Bold, Times-Italic, Times-BoldItalic,
/// Courier, Courier-Bold, Courier-Oblique, Courier-BoldOblique,
/// Symbol, ZapfDingbats.
/// </remarks>
public static class WatermarkStamper
{
    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Applies a text watermark to all (or specified) pages of a document
    /// and writes the result to <paramref name="output"/>.
    /// </summary>
    /// <param name="output">The stream to write the watermarked PDF to.</param>
    /// <param name="document">The source document.</param>
    /// <param name="options">Watermark configuration.</param>
    public static void ApplyText(
        Stream output,
        PdfDocument document,
        TextWatermarkOptions options)
    {
        if (output is null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.Text))
        {
            throw new WatermarkException("Watermark text cannot be empty.");
        }

        HashSet<int> pageSet = BuildPageSet(options.PageIndices, document.PageCount);
        WatermarkDocument doc = new WatermarkDocument(document);

        for (int i = 0; i < document.PageCount; i++)
        {
            if (!pageSet.Contains(i))
            {
                continue;
            }

            PdfPage page = document.Pages[i];
            byte[] wmStream = BuildTextStream(page.Width, page.Height, options);
            doc.AppendContentStream(i, wmStream, options.Opacity, "WMTextGS");
        }

        doc.Write(output);
    }

    /// <summary>
    /// Applies an image watermark to all (or specified) pages of a document
    /// and writes the result to <paramref name="output"/>.
    /// </summary>
    /// <param name="output">The stream to write the watermarked PDF to.</param>
    /// <param name="document">The source document.</param>
    /// <param name="frame">The image to use as the watermark.</param>
    /// <param name="options">Watermark configuration.</param>
    public static void ApplyImage(
        Stream output,
        PdfDocument document,
        ImageFrame frame,
        ImageWatermarkOptions options)
    {
        if (output is null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (frame is null)
        {
            throw new ArgumentNullException(nameof(frame));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        HashSet<int> pageSet = BuildPageSet(options.PageIndices, document.PageCount);
        WatermarkDocument doc = new WatermarkDocument(document);

        // Encode image as PNG for embedding
        byte[] pngBytes;

        using (MemoryStream pngMs = new MemoryStream())
        {
            PngEncoder.Encode(frame, pngMs, includeAlpha: true);
            pngBytes = pngMs.ToArray();
        }

        for (int i = 0; i < document.PageCount; i++)
        {
            if (!pageSet.Contains(i))
            {
                continue;
            }

            PdfPage page = document.Pages[i];
            byte[] wmStream = BuildImageStream(
                page.Width, page.Height, frame.Width, frame.Height, options);
            doc.AppendImageStream(i, wmStream, pngBytes,
                frame.Width, frame.Height, options.Opacity);
        }

        doc.Write(output);
    }

    // ── Text stream builder ───────────────────────────────────────────────

    private static byte[] BuildTextStream(
        double pageW, double pageH, TextWatermarkOptions options)
    {
        // Estimate text width: Helvetica metrics average ~0.5 × fontSize per char
        double textWidth = options.Text.Length * options.FontSize * 0.5;
        double textHeight = options.FontSize;

        // Centre of page
        double cx = pageW / 2.0;
        double cy = pageH / 2.0;

        // Rotation angle in radians (PDF: counter-clockwise)
        double rad = options.RotationDegrees * Math.PI / 180.0;
        double cosA = Math.Cos(rad);
        double sinA = Math.Sin(rad);

        // Translation to centre text: place text origin at (-width/2, -height/4)
        // in rotated space, then transform to page space
        double tx = cx - (textWidth / 2.0) * cosA + (textHeight / 4.0) * sinA;
        double ty = cy - (textWidth / 2.0) * sinA - (textHeight / 4.0) * cosA;

        ColorF rgb = options.Color.ToRgb();
        string r = Fmt(rgb.R);
        string g = Fmt(rgb.G);
        string bv = Fmt(rgb.B);

        string fontKey = SanitizeName(options.FontName);
        string escaped = EscapePdfString(options.Text);

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("q");
        sb.AppendLine("/WMTextGS gs");
        sb.AppendLine($"{r} {g} {bv} rg");
        sb.AppendLine($"{Fmt(cosA)} {Fmt(sinA)} {Fmt(-sinA)} {Fmt(cosA)} {Fmt(tx)} {Fmt(ty)} cm");
        sb.AppendLine("BT");
        sb.AppendLine($"/{fontKey} {Fmt(options.FontSize)} Tf");
        sb.AppendLine($"({escaped}) Tj");
        sb.AppendLine("ET");
        sb.AppendLine("Q");

        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    // ── Image stream builder ──────────────────────────────────────────────

    private static byte[] BuildImageStream(
        double pageW, double pageH,
        int imgW, int imgH,
        ImageWatermarkOptions options)
    {
        // Scale image to fraction of page width, maintain aspect ratio
        double destW = pageW * options.ScaleFraction;
        double destH = imgH > 0 ? destW * imgH / imgW : destW;

        // Centre position
        double cx = (pageW - destW) / 2.0;
        double cy = (pageH - destH) / 2.0;

        double rad = options.RotationDegrees * Math.PI / 180.0;
        double cosA = Math.Cos(rad);
        double sinA = Math.Sin(rad);

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("q");
        sb.AppendLine("/WMImageGS gs");
        // Scale matrix for image: [w 0 0 h x y]
        sb.AppendLine($"{Fmt(destW * cosA)} {Fmt(destW * sinA)} {Fmt(-destH * sinA)} {Fmt(destH * cosA)} {Fmt(cx)} {Fmt(cy)} cm");
        sb.AppendLine("/WMImage Do");
        sb.AppendLine("Q");

        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static HashSet<int> BuildPageSet(int[]? pageIndices, int pageCount)
    {
        if (pageIndices is null)
        {
            HashSet<int> all = new HashSet<int>();

            for (int i = 0; i < pageCount; i++)
            {
                all.Add(i);
            }

            return all;
        }

        return new HashSet<int>(pageIndices);
    }

    private static string Fmt(double v)
    {
        return v.ToString("F6", CultureInfo.InvariantCulture);
    }

    private static string SanitizeName(string name)
    {
        return name.Replace(" ", "").Replace("-", "_");
    }

    private static string EscapePdfString(string text)
    {
        StringBuilder sb = new StringBuilder(text.Length);

        foreach (char c in text)
        {
            if (c == '(') { sb.Append(@"\("); }
            else if (c == ')') { sb.Append(@"\)"); }
            else if (c == '\\') { sb.Append(@"\\"); }
            else { sb.Append(c); }
        }

        return sb.ToString();
    }
}

// ── WatermarkDocument — internal helper for building the output PDF ────────

internal sealed class WatermarkDocument
{
    private readonly PdfDocument _source;
    private readonly List<PdfIndirectObject> _extraObjects;

    // Tracks per-page watermark stream IDs and resource additions
    private readonly Dictionary<int, List<WatermarkEntry>> _pageWatermarks;
    private int _nextObjectNumber;

    internal WatermarkDocument(PdfDocument source)
    {
        _source = source;
        _extraObjects = new List<PdfIndirectObject>();
        _pageWatermarks = new Dictionary<int, List<WatermarkEntry>>();

        // The object store loads lazily, so force the full graph reachable from
        // the trailer into memory before numbering. Without this a freshly
        // opened document exposes only a partial object set, which breaks the
        // rewrite two ways: the next object number is computed too low (new
        // watermark streams collide with existing pages/fonts), and objects that
        // were never loaded — outlines, /Names, attachments, metadata, the
        // structure tree — are silently dropped when the originals are copied in
        // Write.
        ForceLoadAll(source);

        // Start numbering above existing objects.
        _nextObjectNumber = 1;
        foreach (PdfIndirectObject obj in source.Objects.Objects)
        {
            if (obj.Id.ObjectNumber >= _nextObjectNumber)
            {
                _nextObjectNumber = obj.Id.ObjectNumber + 1;
            }
        }

        // Floor at the trailer /Size (highest number + 1) as a safety net.
        if (source.Trailer.TryGetValue(PdfName.Size, out PdfPrimitive? sizePrim)
            && sizePrim is PdfInteger size && size.Value >= _nextObjectNumber)
        {
            _nextObjectNumber = (int)size.Value;
        }
    }

    /// <summary>
    /// Resolves every object reachable from the document trailer, forcing the
    /// lazily-loaded store to materialise the complete object graph. This makes
    /// the store's loaded set complete for both object-number allocation and the
    /// original-object copy performed in <c>Write</c>.
    /// </summary>
    private static void ForceLoadAll(PdfDocument source)
    {
        HashSet<int> visited = new HashSet<int>();
        Queue<PdfPrimitive> pending = new Queue<PdfPrimitive>();
        pending.Enqueue(source.Trailer);

        while (pending.Count > 0)
        {
            PdfPrimitive current = pending.Dequeue();
            switch (current)
            {
                case PdfReference reference:
                    if (visited.Add(reference.ObjectId.ObjectNumber))
                    {
                        pending.Enqueue(source.Objects.ResolveById(reference.ObjectId));
                    }
                    break;
                case PdfStream stream:
                    pending.Enqueue(stream.Dictionary);
                    break;
                case PdfDictionary dictionary:
                    foreach (KeyValuePair<PdfName, PdfPrimitive> entry in dictionary)
                    {
                        pending.Enqueue(entry.Value);
                    }
                    break;
                case PdfArray array:
                    foreach (PdfPrimitive item in array)
                    {
                        pending.Enqueue(item);
                    }
                    break;
            }
        }
    }

    internal void AppendContentStream(
        int pageIndex, byte[] content, float opacity, string gsName)
    {
        PdfObjectId streamId = NextId();
        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Length, content.Length);
        _extraObjects.Add(new PdfIndirectObject(streamId, new PdfStream(dict, content)));

        if (!_pageWatermarks.ContainsKey(pageIndex))
        {
            _pageWatermarks[pageIndex] = new List<WatermarkEntry>();
        }

        _pageWatermarks[pageIndex].Add(new WatermarkEntry(
            streamId, opacity, gsName,
            null, null));
    }

    internal void AppendImageStream(
        int pageIndex, byte[] content, byte[] imageBytes,
        int imgW, int imgH, float opacity)
    {
        // Image XObject stream
        PdfObjectId imageId = NextId();
        PdfDictionary imageDict = new PdfDictionary();
        imageDict.Set(PdfName.Type, PdfName.XObject);
        imageDict.Set(PdfName.Intern("Subtype"), PdfName.Intern("Image"));
        imageDict.Set(PdfName.Intern("Width"), imgW);
        imageDict.Set(PdfName.Intern("Height"), imgH);
        imageDict.Set(PdfName.Intern("ColorSpace"), PdfName.Intern("DeviceRGB"));
        imageDict.Set(PdfName.Intern("BitsPerComponent"), 8);
        imageDict.Set(PdfName.Length, imageBytes.Length);
        _extraObjects.Add(new PdfIndirectObject(imageId, new PdfStream(imageDict, imageBytes)));

        // Content stream
        PdfObjectId streamId = NextId();
        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Length, content.Length);
        _extraObjects.Add(new PdfIndirectObject(streamId, new PdfStream(dict, content)));

        if (!_pageWatermarks.ContainsKey(pageIndex))
        {
            _pageWatermarks[pageIndex] = new List<WatermarkEntry>();
        }

        _pageWatermarks[pageIndex].Add(new WatermarkEntry(
            streamId, opacity, "WMImageGS",
            imageId, "WMImage"));
    }

    internal void Write(System.IO.Stream output)
    {
        // Collect all objects: original + modified pages + extras
        List<PdfIndirectObject> allObjects = new List<PdfIndirectObject>();
        HashSet<int> modifiedPageIds = new HashSet<int>();

        // Map page index → original page object ID
        Dictionary<int, PdfObjectId> pageIds = BuildPageIdMap();

        // Build modified page objects
        foreach (KeyValuePair<int, List<WatermarkEntry>> kvp in _pageWatermarks)
        {
            int pageIndex = kvp.Key;
            List<WatermarkEntry> entries = kvp.Value;

            if (!pageIds.TryGetValue(pageIndex, out PdfObjectId pageId))
            {
                continue;
            }

            PdfPage page = _source.Pages[pageIndex];
            PdfDictionary modifiedPage = BuildModifiedPage(page, entries);
            allObjects.Add(new PdfIndirectObject(pageId, modifiedPage));
            modifiedPageIds.Add(pageId.ObjectNumber);
        }

        // Add all original objects (excluding modified pages)
        foreach (PdfIndirectObject obj in _source.Objects.Objects)
        {
            if (!modifiedPageIds.Contains(obj.Id.ObjectNumber))
            {
                allObjects.Add(obj);
            }
        }

        // Add extra objects (watermark streams, image XObjects)
        allObjects.AddRange(_extraObjects);

        // Build trailer from source
        PdfDictionary trailer = BuildTrailer();
        PdfWriter.Write(output, allObjects, trailer);
    }

    private PdfDictionary BuildModifiedPage(
        PdfPage page,
        List<WatermarkEntry> entries)
    {
        PdfDictionary pageDict = CopyDictionary(page.Dictionary);

        // Build /Contents array: original stream(s) + watermark streams
        PdfArray contentsArray = new PdfArray([]);

        PdfPrimitive? existing = page.Contents;

        if (existing is not null && existing is not PdfNull)
        {
            PdfPrimitive resolved = _source.Objects.Resolve(existing);

            if (resolved is PdfStream)
            {
                // Wrap single stream in array (need its reference)
                if (existing is PdfReference existRef)
                {
                    contentsArray.Add(existRef);
                }
            }
            else if (resolved is PdfArray existArray)
            {
                for (int i = 0; i < existArray.Count; i++)
                {
                    contentsArray.Add(existArray[i]);
                }
            }
        }

        foreach (WatermarkEntry entry in entries)
        {
            contentsArray.Add(new PdfReference(entry.StreamId));
        }

        pageDict.Set(PdfName.Contents, contentsArray);

        // Build /Resources with font and ExtGState additions
        PdfDictionary resources = BuildModifiedResources(page, entries);
        pageDict.Set(PdfName.Resources, resources);

        return pageDict;
    }

    private PdfDictionary BuildModifiedResources(
        PdfPage page, List<WatermarkEntry> entries)
    {
        PdfDictionary resources = page.Resources is not null
            ? CopyDictionary(page.Resources)
            : new PdfDictionary();

        // Add ExtGState entries for opacity
        PdfDictionary extGState = GetOrCreateSubdict(resources, "ExtGState");

        foreach (WatermarkEntry entry in entries)
        {
            PdfDictionary gs = new PdfDictionary();
            gs.Set(PdfName.Type, PdfName.Intern("ExtGState"));
            gs.Set(PdfName.Intern("ca"), new PdfReal(entry.Opacity)); // fill opacity
            gs.Set(PdfName.Intern("CA"), new PdfReal(entry.Opacity)); // stroke opacity
            extGState.Set(PdfName.Intern(entry.GsName), gs);
        }

        resources.Set(PdfName.Intern("ExtGState"), extGState);

        // Add Font entry for text watermarks (standard font — no embedding needed)
        bool hasTextEntry = false;

        foreach (WatermarkEntry entry in entries)
        {
            if (entry.ImageId is null)
            {
                hasTextEntry = true;
            }
        }

        if (hasTextEntry)
        {
            PdfDictionary fonts = GetOrCreateSubdict(resources, "Font");
            PdfDictionary helvetica = new PdfDictionary();
            helvetica.Set(PdfName.Type, PdfName.Intern("Font"));
            helvetica.Set(PdfName.Intern("Subtype"), PdfName.Intern("Type1"));
            helvetica.Set(PdfName.Intern("BaseFont"), PdfName.Intern("Helvetica"));
            helvetica.Set(PdfName.Intern("Encoding"), PdfName.Intern("WinAnsiEncoding"));
            fonts.Set(PdfName.Intern("Helvetica"), helvetica);
            resources.Set(PdfName.Intern("Font"), fonts);
        }

        // Add XObject entries for image watermarks
        bool hasImageEntry = false;

        foreach (WatermarkEntry entry in entries)
        {
            if (entry.ImageId is not null)
            {
                hasImageEntry = true;
                break;
            }
        }

        if (hasImageEntry)
        {
            PdfDictionary xObjects = GetOrCreateSubdict(resources, "XObject");

            foreach (WatermarkEntry entry in entries)
            {
                if (entry.ImageId is not null && entry.XObjectName is not null)
                {
                    xObjects.Set(PdfName.Intern(entry.XObjectName),
                        new PdfReference(entry.ImageId.Value));
                }
            }

            resources.Set(PdfName.Intern("XObject"), xObjects);
        }

        return resources;
    }

    private PdfDictionary GetOrCreateSubdict(PdfDictionary parent, string key)
    {
        if (parent.TryGetValue(PdfName.Intern(key), out PdfPrimitive? existing))
        {
            PdfDictionary? resolved =
                _source.Objects.ResolveAs<PdfDictionary>(existing ?? PdfNull.Value);

            if (resolved is not null)
            {
                return CopyDictionary(resolved);
            }
        }

        return new PdfDictionary();
    }

    private Dictionary<int, PdfObjectId> BuildPageIdMap()
    {
        Dictionary<int, PdfObjectId> map = new Dictionary<int, PdfObjectId>();

        // Walk the page tree to collect page IDs in order
        // Simple approach: find pages that match page content
        int idx = 0;

        foreach (PdfIndirectObject obj in _source.Objects.Objects)
        {
            if (obj.Value is not PdfDictionary dict)
            {
                continue;
            }

            if (!dict.TryGetValue(PdfName.Type, out PdfPrimitive? typePrim))
            {
                continue;
            }

            if (typePrim is PdfName typeName && typeName.Value == "Page")
            {
                map[idx++] = obj.Id;
            }
        }

        return map;
    }

    private PdfDictionary BuildTrailer()
    {
        PdfDictionary trailer = new PdfDictionary();

        // Find catalog reference
        foreach (PdfIndirectObject obj in _source.Objects.Objects)
        {
            if (obj.Value is PdfDictionary dict &&
                dict.TryGetValue(PdfName.Type, out PdfPrimitive? t) &&
                t is PdfName tn && tn.Value == "Catalog")
            {
                trailer.Set(PdfName.Root, new PdfReference(obj.Id));
                break;
            }
        }

        // Carry the document information dictionary forward. Without this the
        // writer synthesises a generic /Info and the original Title, Author,
        // Subject, Keywords and dates are lost. The referenced object is kept
        // alive by the full-graph force-load in the constructor.
        if (_source.Trailer.TryGetValue(PdfName.Intern("Info"), out PdfPrimitive? info))
        {
            trailer.Set(PdfName.Intern("Info"), info);
        }

        return trailer;
    }

    private static PdfDictionary CopyDictionary(PdfDictionary source)
    {
        PdfDictionary copy = new PdfDictionary();

        foreach (KeyValuePair<PdfName, PdfPrimitive> entry in source)
        {
            copy.Set(entry.Key, entry.Value);
        }

        return copy;
    }

    private PdfObjectId NextId()
    {
        return new PdfObjectId(_nextObjectNumber++, 0);
    }

    // ── Entry record ──────────────────────────────────────────────────────

    private sealed class WatermarkEntry
    {
        internal WatermarkEntry(
            PdfObjectId streamId, float opacity, string gsName,
            PdfObjectId? imageId, string? xObjectName)
        {
            StreamId = streamId;
            Opacity = opacity;
            GsName = gsName;
            ImageId = imageId;
            XObjectName = xObjectName;
        }

        internal PdfObjectId StreamId { get; }
        internal float Opacity { get; }
        internal string GsName { get; }
        internal PdfObjectId? ImageId { get; }
        internal string? XObjectName { get; }
    }
}
