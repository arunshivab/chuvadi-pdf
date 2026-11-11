// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.10 — Extraction of text content
// PHASE: Phase 1 — Chuvadi.Pdf.Text
// Public API: wires PdfPage -> content stream -> parser -> extractor -> string.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using Chuvadi.Pdf.Annotations;
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
    /// Better for multi-column and table-heavy PDFs. Since 3.29 it follows a
    /// Tagged PDF's structure tree when the tree orders the page's text, and
    /// otherwise reads columns of running text one after another
    /// (<see cref="TextExtractor.AnalyzeLayout(PdfPage)"/>).
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

    /// <summary>
    /// Gets or initialises which optional-content layers are extracted.
    /// Default (null): the page's <see cref="PdfPage.OptionalContent"/> — text
    /// in layers hidden by the document's default configuration is left out,
    /// as Acrobat does. Pass <see cref="OptionalContentVisibility.AllVisible"/>
    /// to extract every layer.
    /// </summary>
    public OptionalContentVisibility? OptionalContent { get; init; }

    /// <summary>
    /// Gets or initialises how raised and lowered text (superscripts and
    /// subscripts) is written in the extracted text (since 3.29). Default:
    /// <see cref="SuperscriptMode.Unicode"/>. Extracted text is a derived
    /// representation: the PDF is never changed, and
    /// <see cref="ExtractFragments(PdfPage)"/> returns each fragment's own
    /// text with its <see cref="TextFragment.BaselineShift"/> whatever the mode.
    /// </summary>
    public SuperscriptMode Superscripts { get; init; } = SuperscriptMode.Unicode;

    /// <summary>
    /// Gets or initialises whether layout-aware extraction follows a Tagged
    /// PDF's structure tree (since 3.29). Default true: when the tree orders
    /// at least 80% of a page's text, that order is used; otherwise, or when
    /// false, columns and rows are found from the page's geometry.
    /// </summary>
    public bool UseTaggedOrder { get; init; } = true;

    /// <summary>
    /// Gets or initialises which annotations' text is extracted (3.31): View
    /// (the default), Print, FormFieldsOnly or None. Form fields without
    /// up-to-date appearances are read from their values in every mode.
    /// </summary>
    public AnnotationMode AnnotationMode { get; init; } = AnnotationMode.View;

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts all text from the given page as a plain Unicode string.
    /// </summary>
    /// <param name="page">The page to extract text from.</param>
    /// <returns>The extracted text, or an empty string when the page has no text.</returns>
    public string ExtractText(PdfPage page) => ExtractText(page, CancellationToken.None);

    /// <summary>
    /// Extracts all text from the given page as a plain Unicode string,
    /// stopping when <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    /// <remarks>
    /// The work runs in a <see cref="PdfWorkScope"/> with the limits the
    /// document was opened with (<see cref="PdfObjectStore.Limits"/>);
    /// cancellation is noticed well within a second.
    /// </remarks>
    /// <param name="page">The page to extract text from.</param>
    /// <param name="cancellationToken">A token that cancels the extraction.</param>
    /// <returns>The extracted text, or an empty string when the page has no text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="page"/> is null.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    /// <exception cref="PdfLimitExceededException">A limit was exceeded.</exception>
    /// <exception cref="PdfException">
    /// The page could not be read. Failures the code did not anticipate are
    /// reported as <see cref="PdfCorruptionException"/> with the original as
    /// the inner exception.
    /// </exception>
    public string ExtractText(PdfPage page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        return Guard(() => ExtractTextCore(page), cancellationToken);
    }

    // Runs work in a scope with the document's limits and the caller's token,
    // reporting failures outside the documented set as PdfCorruptionException.
    private T Guard<T>(Func<T> work, CancellationToken cancellationToken)
    {
        using PdfWorkScope scope = PdfWorkScope.Enter(_objects.Limits, cancellationToken);
        try
        {
            return work();
        }
        catch (Exception ex) when (ex is not (PdfException or OperationCanceledException or OutOfMemoryException or ObjectDisposedException)
            && (ex is not IOException || ex is EndOfStreamException))
        {
            throw new PdfCorruptionException("The page text could not be extracted: " + ex.Message, ex);
        }
    }

    /// <summary>
    /// Extracts the page's text with layout-aware reading, in the given
    /// reading order whatever the extractor's strategy (since 3.29).
    /// </summary>
    /// <remarks>
    /// Pass a layout from <see cref="AnalyzeLayout(PdfPage)"/>, changed as the
    /// application needs, to override the bands and columns found;
    /// <see cref="PageLayout.Rows"/> reads plain rows (the 3.28 order), and
    /// <see cref="PageLayout.Tagged"/> uses the structure tree when it orders
    /// the page's text, and otherwise finds columns as
    /// <see cref="ExtractionStrategy.Layout"/> does.
    /// </remarks>
    /// <param name="page">The page to extract text from.</param>
    /// <param name="layout">The reading order to use.</param>
    /// <returns>The extracted text, or an empty string when the page has no text.</returns>
    public string ExtractText(PdfPage page, PageLayout layout) => ExtractText(page, layout, CancellationToken.None);

    /// <summary>
    /// Extracts the page's text in the given reading order, stopping when
    /// <paramref name="cancellationToken"/> is cancelled. See
    /// <see cref="ExtractText(PdfPage, PageLayout)"/>, and
    /// <see cref="ExtractText(PdfPage, CancellationToken)"/> for limits and exceptions.
    /// </summary>
    /// <param name="page">The page to extract text from.</param>
    /// <param name="layout">The reading order to use.</param>
    /// <param name="cancellationToken">A token that cancels the extraction.</param>
    /// <returns>The extracted text, or an empty string when the page has no text.</returns>
    public string ExtractText(PdfPage page, PageLayout layout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(layout);
        return Guard(() => LayoutText(page, ScriptText.Classify(ReadFragments(page)), layout), cancellationToken);
    }

    /// <summary>
    /// Returns the reading order layout-aware extraction uses for the page
    /// (since 3.29): <see cref="PageLayout.Tagged"/> when the document's
    /// structure tree orders the page's text (and <see cref="UseTaggedOrder"/>
    /// is true), otherwise the bands and columns
    /// found (<see cref="ReadingOrderSource.Columns"/>), or
    /// <see cref="PageLayout.Rows"/> when no band is read in columns.
    /// </summary>
    /// <param name="page">The page to analyse.</param>
    /// <returns>The page's reading order.</returns>
    public PageLayout AnalyzeLayout(PdfPage page) => AnalyzeLayout(page, CancellationToken.None);

    /// <summary>
    /// Returns the reading order layout-aware extraction uses for the page,
    /// stopping when <paramref name="cancellationToken"/> is cancelled. See
    /// <see cref="AnalyzeLayout(PdfPage)"/>, and
    /// <see cref="ExtractText(PdfPage, CancellationToken)"/> for limits and exceptions.
    /// </summary>
    /// <param name="page">The page to analyse.</param>
    /// <param name="cancellationToken">A token that cancels the analysis.</param>
    /// <returns>The page's reading order.</returns>
    public PageLayout AnalyzeLayout(PdfPage page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        return Guard(
            () =>
            {
                List<TextFragment> fragments = ReadFragments(page);
                if (UseTaggedOrder && TaggedOrder.Ranks(page, _objects, fragments) is not null)
                {
                    return PageLayout.Tagged;
                }

                return fragments.Count == 0
                    ? PageLayout.Rows
                    : ColumnLayout.Detect(LayoutExtractor.GroupIntoLines(fragments));
            },
            cancellationToken);
    }

    private string ExtractTextCore(PdfPage page)
    {
        List<TextFragment> fragments = ScriptText.Classify(ReadFragments(page));

        if (fragments.Count == 0)
        {
            return string.Empty;
        }

        return _strategy == ExtractionStrategy.Layout
            ? LayoutText(page, fragments, PageLayout.Tagged)
            : OperatorExtractor.Write(fragments, Superscripts);
    }

    // Layout-aware text in the requested order: the structure tree first when
    // asked for, then columns, then rows (3.29).
    private string LayoutText(PdfPage page, List<TextFragment> classified, PageLayout layout)
    {
        if (classified.Count == 0)
        {
            return string.Empty;
        }

        LayoutExtractor extractor = new LayoutExtractor { Superscripts = Superscripts };
        if (layout.Source == ReadingOrderSource.Tagged)
        {
            Dictionary<int, int>? ranks = UseTaggedOrder ? TaggedOrder.Ranks(page, _objects, classified) : null;
            if (ranks is not null)
            {
                return extractor.ExtractTagged(classified, ranks);
            }

            return extractor.Extract(classified);
        }

        return extractor.Extract(classified, layout);
    }

    /// <summary>
    /// Extracts positioned text fragments from the given page.
    /// </summary>
    /// <remarks>
    /// Each fragment is a piece of text shown by a single Tj or TJ entry with
    /// the X, Y position (PDF user space) and font size at the time of rendering.
    /// Returned in operator order, not reading order — callers wanting reading
    /// order should apply layout reconstruction. Since 3.29 each fragment
    /// that is raised or lowered off its line's baseline carries
    /// <see cref="TextFragment.BaselineShift"/> and its shift; the text of
    /// every fragment is the PDF's own.
    /// </remarks>
    /// <param name="page">The page to extract fragments from.</param>
    /// <returns>A list of fragments, or an empty list when the page has no text.</returns>
    public List<TextFragment> ExtractFragments(PdfPage page) => ExtractFragments(page, CancellationToken.None);

    /// <summary>
    /// Extracts positioned text fragments from the given page, stopping when
    /// <paramref name="cancellationToken"/> is cancelled. See
    /// <see cref="ExtractText(PdfPage, CancellationToken)"/> for limits and exceptions.
    /// </summary>
    /// <param name="page">The page to extract fragments from.</param>
    /// <param name="cancellationToken">A token that cancels the extraction.</param>
    /// <returns>A list of fragments, or an empty list when the page has no text.</returns>
    public List<TextFragment> ExtractFragments(PdfPage page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        return Guard(() => ExtractFragmentsCore(page), cancellationToken);
    }

    // Fragments in operator order, raised and lowered ones marked (3.29).
    private List<TextFragment> ExtractFragmentsCore(PdfPage page) => ScriptText.Classify(ReadFragments(page));

    private List<TextFragment> ReadFragments(PdfPage page)
    {
        // FormFieldsOnly (3.31): the form fields' text only, not the page's.
        byte[] contentBytes = AnnotationMode == AnnotationMode.FormFieldsOnly ? Array.Empty<byte>() : ReadContentBytes(page);

        ContentStreamParser parser = new ContentStreamParser(_objects, page.Resources)
        {
            OptionalContent = VisibilityFor(page),
        };
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
        IReadOnlyList<AnnotationAppearance> appearances = AnnotationMode == AnnotationMode.None
            ? Array.Empty<AnnotationAppearance>()
            : PageAnnotationAppearances.Collect(page, _objects, AnnotationMode, GeneratedAppearances.Instance);

        for (int i = 0; i < appearances.Count; i++)
        {
            AnnotationAppearance ap = appearances[i];
            OptionalContentVisibility visibility = VisibilityFor(page);
            if (HiddenByEntry(ap.Annotation, visibility) || HiddenByEntry(ap.Appearance.Dictionary, visibility))
            {
                continue;
            }

            byte[] apBytes;

            try
            {
                apBytes = DecodeStream(ap.Appearance);
            }
            catch (PdfException abortCheck) when (!Chuvadi.Pdf.Primitives.PdfWorkScope.IsAbort(abortCheck))
            {
                continue;
            }

            if (apBytes.Length == 0)
            {
                continue;
            }

            ContentStreamParser apParser = new ContentStreamParser(_objects, ap.Resources)
            {
                OptionalContent = visibility,
            };
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

                // 3.33.0 (ticket 45): the measured width comes along, scaled
                // into page space, when the baseline stays horizontal there
                // (the page fragment keeps angle 0, as before). Without it a
                // shaped cluster wider than its character count suggests
                // (a Tamil vowel sign drawn as two glyphs) was followed by a
                // false word space.
                double width = double.NaN;
                if (!double.IsNaN(fragment.Width) && fragment.Angle == 0 && ap.MatrixB == 0 && ap.MatrixA * ap.ScaleX > 0)
                {
                    width = fragment.Width * ap.MatrixA * ap.ScaleX;
                }

                fragments.Add(new TextFragment(fragment.Text, pageX, pageY, fontSize, width)
                {
                    IsActualText = fragment.IsActualText,
                });
            }
        }
    }

    private OptionalContentVisibility VisibilityFor(PdfPage page) => OptionalContent ?? page.OptionalContent;

    private static bool HiddenByEntry(PdfDictionary dict, OptionalContentVisibility visibility) =>
        dict.TryGetValue(PdfName.Intern("OC"), out PdfPrimitive? oc) && !visibility.IsVisible(oc);

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
            return DecodePageStream(singleStream, contents, page.PageNumber);
        }

        // Array of stream references.
        if (resolved is PdfArray array)
        {
            return ConcatenateStreams(array, page.PageNumber);
        }

        return [];
    }

    private byte[] ConcatenateStreams(PdfArray array, int pageNumber)
    {
        using (MemoryStream ms = new MemoryStream())
        {
            for (int i = 0; i < array.Count; i++)
            {
                PdfPrimitive item = _objects.Resolve(array[i]);

                if (item is PdfStream stream)
                {
                    byte[] decoded = DecodePageStream(stream, array[i], pageNumber);
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

    // 3.33.0 (ticket 39, D-325): a page content stream a filter finds
    // damaged gives what decoded before the damage (or nothing) and a
    // StreamDamaged warning, as drawing does; before, extraction threw.
    private byte[] DecodePageStream(PdfStream stream, PdfPrimitive? reference, int pageNumber)
    {
        try
        {
            return DecodeStream(stream);
        }
        catch (FilterException damage)
        {
            string subject = reference is PdfReference r
                ? "page content " + r.ObjectNumber.ToString(CultureInfo.InvariantCulture) + " " + r.Generation.ToString(CultureInfo.InvariantCulture) + " R"
                : "page content (direct)";
            _objects.OnWarning?.Invoke(new PdfWarning(PdfWarningKind.StreamDamaged, pageNumber, subject, DamagedContent.Reason(damage)));
            return DamagedContent.UsablePrefix(damage);
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
