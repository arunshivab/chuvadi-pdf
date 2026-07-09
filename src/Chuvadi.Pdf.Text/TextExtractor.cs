// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.10 — Extraction of text content
// PHASE: Phase 1 — Chuvadi.Pdf.Text
// Public API: wires PdfPage -> content stream -> parser -> extractor -> string.

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Content;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Filters;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Text;

/// <summary>
/// Specifies the text extraction strategy.
/// </summary>
public enum ExtractionStrategy
{
    /// <summary>
    /// Stream-order extraction. Fastest. Correct for most born-digital PDFs.
    /// </summary>
    Operator,

    /// <summary>
    /// Layout-aware extraction. Groups by line, sorts by X position.
    /// Better for multi-column and table-heavy PDFs.
    /// </summary>
    Layout,
}

/// <summary>
/// Extracts plain text from a PDF page.
/// </summary>
/// <remarks>
/// <see cref="TextExtractor"/> is the top-level public API for Phase 1 text extraction.
/// It wires together all layers:
/// <list type="number">
///   <item>Resolves the page's /Contents entry to one or more content streams.</item>
///   <item>Decodes each stream through its filter chain (FlateDecode etc.).</item>
///   <item>Concatenates streams and passes them to <see cref="ContentStreamParser"/>.</item>
///   <item>Applies the chosen <see cref="ExtractionStrategy"/> to the resulting fragments.</item>
///   <item>Returns the extracted text as a plain Unicode string.</item>
/// </list>
///
/// Phase 1 scope: born-digital text only. Image-embedded text requires OCR (Phase 3).
/// PDF 32000-1:2008 §9.10 — Extraction of text content.
/// </remarks>
public sealed class TextExtractor
{
    private readonly PdfObjectStore _objects;
    private readonly ExtractionStrategy _strategy;
    private readonly FilterPipeline _pipeline;

    /// <summary>
    /// Initialises a <see cref="TextExtractor"/> for a document's object store.
    /// </summary>
    /// <param name="objects">The document's object store, used to resolve references.</param>
    /// <param name="strategy">
    /// The extraction strategy to use. Defaults to <see cref="ExtractionStrategy.Operator"/>.
    /// </param>
    public TextExtractor(
        PdfObjectStore objects,
        ExtractionStrategy strategy = ExtractionStrategy.Operator)
    {
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        _strategy = strategy;
        _pipeline = FilterRegistry.CreateDefaultPipeline();
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts all text from the given page as a plain Unicode string.
    /// </summary>
    /// <param name="page">The page to extract text from.</param>
    /// <returns>The extracted text, or an empty string when the page has no text.</returns>
    public string ExtractText(PdfPage page)
    {
        if (page is null)
        {
            throw new ArgumentNullException(nameof(page));
        }

        byte[] contentBytes = ReadContentBytes(page);

        ContentStreamParser parser = new ContentStreamParser(_objects, page.Resources);
        List<TextFragment> fragments = contentBytes.Length > 0
            ? parser.Parse(contentBytes)
            : new List<TextFragment>();

        AppendAnnotationFragments(page, fragments);

        if (fragments.Count == 0)
        {
            return string.Empty;
        }

        return _strategy == ExtractionStrategy.Layout
            ? new LayoutExtractor().Extract(fragments)
            : new OperatorExtractor().Extract(fragments);
    }

    /// <summary>
    /// Extracts positioned text fragments from the given page.
    /// </summary>
    /// <remarks>
    /// Each fragment is a piece of text shown by a single Tj or TJ entry with
    /// the X, Y position (PDF user space) and font size at the time of rendering.
    /// Returned in operator order, not reading order — callers wanting reading
    /// order should apply layout reconstruction.
    /// </remarks>
    /// <param name="page">The page to extract fragments from.</param>
    /// <returns>A list of fragments, or an empty list when the page has no text.</returns>
    public List<TextFragment> ExtractFragments(PdfPage page)
    {
        if (page is null)
        {
            throw new ArgumentNullException(nameof(page));
        }

        byte[] contentBytes = ReadContentBytes(page);

        ContentStreamParser parser = new ContentStreamParser(_objects, page.Resources);
        List<TextFragment> fragments = contentBytes.Length > 0
            ? parser.Parse(contentBytes)
            : new List<TextFragment>();

        AppendAnnotationFragments(page, fragments);
        return fragments;
    }

    // ── Private: annotation appearance text ───────────────────────────────

    /// <summary>
    /// Appends text fragments from the page's annotation normal appearance
    /// streams (<c>/AP /N</c>), positioned into page space by the §12.5.5
    /// placement so layout reconstruction interleaves them correctly with the
    /// page content. Hybrid XFA/AcroForm documents carry their field values in
    /// widget appearance streams, so without this pass extraction returns the
    /// static template labels but not the filled values.
    /// PDF 32000-1:2008 §12.5.5 — Appearance streams.
    /// </summary>
    private void AppendAnnotationFragments(PdfPage page, List<TextFragment> fragments)
    {
        IReadOnlyList<AnnotationAppearance> appearances =
            PageAnnotationAppearances.Collect(page, _objects);

        for (int i = 0; i < appearances.Count; i++)
        {
            AnnotationAppearance ap = appearances[i];
            byte[] apBytes;

            try
            {
                apBytes = DecodeStream(ap.Appearance);
            }
            catch (PdfException)
            {
                continue;
            }

            if (apBytes.Length == 0)
            {
                continue;
            }

            ContentStreamParser apParser = new ContentStreamParser(_objects, ap.Resources);
            List<TextFragment> apFragments = apParser.Parse(apBytes);

            for (int j = 0; j < apFragments.Count; j++)
            {
                TextFragment fragment = apFragments[j];

                // Form matrix first, then the §12.5.5 placement, mapping the
                // fragment's position from appearance space into page space.
                double formX = (ap.MatrixA * fragment.X) + (ap.MatrixC * fragment.Y) + ap.MatrixE;
                double formY = (ap.MatrixB * fragment.X) + (ap.MatrixD * fragment.Y) + ap.MatrixF;
                double pageX = (ap.ScaleX * formX) + ap.OffsetX;
                double pageY = (ap.ScaleY * formY) + ap.OffsetY;
                double matrixYScale = Math.Sqrt(
                    (ap.MatrixB * ap.MatrixB) + (ap.MatrixD * ap.MatrixD));
                double fontSize = fragment.FontSize * Math.Abs(ap.ScaleY) * matrixYScale;

                fragments.Add(new TextFragment(fragment.Text, pageX, pageY, fontSize));
            }
        }
    }

    // ── Private: content stream loading ──────────────────────────────────

    /// <summary>
    /// Reads, decodes, and concatenates all content streams for the page.
    /// Handles /Contents as a single reference, an array of references,
    /// or an inline stream dictionary.
    /// PDF 32000-1:2008 §7.8.2 — Content streams.
    /// </summary>
    private byte[] ReadContentBytes(PdfPage page)
    {
        PdfPrimitive? contents = page.Contents;

        if (contents is null || contents is PdfNull)
        {
            return [];
        }

        // Resolve indirect reference if needed.
        PdfPrimitive resolved = _objects.Resolve(contents);

        // Single stream.
        if (resolved is PdfStream singleStream)
        {
            return DecodeStream(singleStream);
        }

        // Array of stream references.
        if (resolved is PdfArray array)
        {
            return ConcatenateStreams(array);
        }

        return [];
    }

    private byte[] ConcatenateStreams(PdfArray array)
    {
        using (MemoryStream ms = new MemoryStream())
        {
            for (int i = 0; i < array.Count; i++)
            {
                PdfPrimitive item = _objects.Resolve(array[i]);

                if (item is PdfStream stream)
                {
                    byte[] decoded = DecodeStream(stream);
                    ms.Write(decoded, 0, decoded.Length);

                    // Separate streams with a space to prevent token merging.
                    if (i < array.Count - 1)
                    {
                        ms.WriteByte(32); // space
                    }
                }
            }

            return ms.ToArray();
        }
    }

    private byte[] DecodeStream(PdfStream stream)
    {
        if (!stream.IsFiltered)
        {
            return stream.RawBytes;
        }

        PdfPrimitive? filter = stream.Filter;
        stream.Dictionary.TryGetValue(PdfName.Intern("DecodeParms"), out PdfPrimitive? decodeParms);

        // Single filter name.
        if (filter is PdfName filterName)
        {
            string resolved = FilterRegistry.ResolveAlias(filterName.Value);

            return _pipeline.Decode(
                resolved,
                stream.RawBytes,
                FilterParameters.FromDictionary(decodeParms, 0));
        }

        // Array of filter names applied in sequence.
        if (filter is PdfArray filterArray)
        {
            byte[] data = stream.RawBytes;

            for (int i = 0; i < filterArray.Count; i++)
            {
                PdfName? fn = filterArray.GetAs<PdfName>(i);

                if (fn is null)
                {
                    continue;
                }

                string resolved = FilterRegistry.ResolveAlias(fn.Value);
                data = _pipeline.Decode(resolved, data, FilterParameters.FromDictionary(decodeParms, i));
            }

            return data;
        }

        return stream.RawBytes;
    }
}
