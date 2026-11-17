// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.34.0 (D-335) — Redactor.ApplyWithReport: what was removed, per page.

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Redaction;

/// <summary>
/// What <see cref="Redactor.ApplyWithReport"/> removed, page by page (3.34.0).
/// </summary>
public sealed class RedactionReport
{
    internal RedactionReport(IReadOnlyList<PageRedactionReport> pages)
    {
        Pages = pages;
    }

    /// <summary>
    /// Gets one entry per page that was rewritten (a page with a redaction
    /// rectangle or an applicable text pattern), in page order. A page whose
    /// counts are all zero had nothing under its rectangles.
    /// </summary>
    public IReadOnlyList<PageRedactionReport> Pages { get; }

    /// <summary>
    /// Gets the number of matches of the document-wide patterns (those with no
    /// page list) removed outside the pages: from bookmark titles and from the
    /// XMP metadata (3.34.0), and from URI actions and alternate texts
    /// (<c>/ActualText</c>, <c>/Alt</c>, <c>/E</c>) in the document's objects
    /// (3.35.0). The redacted file carries no document information
    /// dictionary (<c>/Info</c>) at all. Scripts are counted in
    /// <see cref="ScriptsRemoved"/> and attachments in
    /// <see cref="AttachmentsRemoved"/>.
    /// </summary>
    public int DocumentMatchesRemoved { get; internal init; }

    /// <summary>
    /// Gets the document catalog entries left out of the redacted file because
    /// the objects they refer to were not written (3.35.0): the name trees
    /// other than named destinations, attachments and JavaScript (for
    /// example <c>Names/Templates</c>, page templates whose content is
    /// outside the page tree), and inside the form <c>AcroForm/XFA</c> and
    /// <c>AcroForm/Fields n 0 R</c> for fields with no widget on a page.
    /// They are dropped rather than carried through unscrubbed. Named
    /// destinations, attachments, JavaScript, <c>OpenAction</c> and
    /// <c>AA</c> are kept (see <see cref="AttachmentsRemoved"/> and
    /// <see cref="ScriptsRemoved"/>). Empty when nothing was dropped.
    /// </summary>
    public IReadOnlyList<string> CatalogEntriesRemoved { get; internal init; } = Array.Empty<string>();

    /// <summary>
    /// Gets the names of the attachments (embedded files) removed (3.35.0):
    /// those that matched a document-wide pattern, in their content or their
    /// name, and, in a pattern redaction unless
    /// <see cref="RedactionOptions.KeepAttachments"/> is set, those that could
    /// not be checked (not text, or only page-limited patterns). An
    /// attachment whose name matched is listed as <c>(name redacted)</c>.
    /// The attachment's file data is not in the redacted file. Empty when
    /// none was removed.
    /// </summary>
    public IReadOnlyList<string> AttachmentsRemoved { get; internal init; } = Array.Empty<string>();

    /// <summary>
    /// Gets the names of the attachments kept without being checked against
    /// the patterns (3.35.0): with rectangles only (no patterns), every
    /// attachment; in a pattern redaction with
    /// <see cref="RedactionOptions.KeepAttachments"/> set, those that could
    /// not be checked. Their content is carried to the redacted file as it
    /// was.
    /// </summary>
    public IReadOnlyList<string> AttachmentsNotScanned { get; internal init; } = Array.Empty<string>();

    /// <summary>
    /// Gets the number of JavaScript actions whose script matched a
    /// document-wide pattern (or could not be read) and was removed
    /// (3.35.0). The action itself is kept with an empty script; document
    /// JavaScript that does not match is kept as it was.
    /// </summary>
    public int ScriptsRemoved { get; internal init; }
}

/// <summary>
/// The redactions made on one page. Content drawn through a form XObject
/// counts on the first page that draws it.
/// </summary>
public sealed class PageRedactionReport
{
    internal PageRedactionReport(int pageIndex, RedactCounts counts)
    {
        PageIndex = pageIndex;
        GlyphsRemoved = counts.GlyphsRemoved;
        ImagesBlanked = counts.ImagesBlanked;
        ImagesRemoved = counts.ImagesRemoved;
        PathsCut = counts.PathsCut;
        PathsRemoved = counts.PathsRemoved;
        AnnotationsRemoved = counts.AnnotationsRemoved;
        FieldValuesCleared = counts.FieldValuesCleared;
        AlternateTextsRemoved = counts.AlternateTextsRemoved;
    }

    /// <summary>Gets the zero-based page index.</summary>
    public int PageIndex { get; }

    /// <summary>Gets the number of glyphs removed from text (each replaced by a gap of the same width).</summary>
    public int GlyphsRemoved { get; }

    /// <summary>Gets the number of image placements whose covered pixels were blanked (the rest of the image kept).</summary>
    public int ImagesBlanked { get; }

    /// <summary>Gets the number of image placements removed whole.</summary>
    public int ImagesRemoved { get; }

    /// <summary>
    /// Gets the number of painted paths that had the redacted area cut out of
    /// them, and (3.35.0) of shadings painted with <c>sh</c> that were clipped
    /// out of the redacted area.
    /// </summary>
    public int PathsCut { get; }

    /// <summary>
    /// Gets the number of painted paths removed whole, and (3.35.0) of mesh
    /// shadings painted with <c>sh</c> under a redaction.
    /// </summary>
    public int PathsRemoved { get; }

    /// <summary>Gets the number of annotations (other than form widgets) removed.</summary>
    public int AnnotationsRemoved { get; }

    /// <summary>Gets the number of form-field widgets whose field value was cleared.</summary>
    public int FieldValuesCleared { get; }

    /// <summary>
    /// Gets the number of alternate texts (<c>/ActualText</c>, <c>/Alt</c>,
    /// <c>/E</c>) removed because their content was redacted, in marked
    /// content and in the structure tree.
    /// </summary>
    public int AlternateTextsRemoved { get; }
}

/// <summary>Mutable counters for one page while it is redacted.</summary>
internal sealed class RedactCounts
{
    internal int GlyphsRemoved { get; set; }

    internal int ImagesBlanked { get; set; }

    internal int ImagesRemoved { get; set; }

    internal int PathsCut { get; set; }

    internal int PathsRemoved { get; set; }

    internal int AnnotationsRemoved { get; set; }

    internal int FieldValuesCleared { get; set; }

    internal int AlternateTextsRemoved { get; set; }

    internal void Add(RedactCounts other)
    {
        ArgumentNullException.ThrowIfNull(other);
        GlyphsRemoved += other.GlyphsRemoved;
        ImagesBlanked += other.ImagesBlanked;
        ImagesRemoved += other.ImagesRemoved;
        PathsCut += other.PathsCut;
        PathsRemoved += other.PathsRemoved;
        AnnotationsRemoved += other.AnnotationsRemoved;
        FieldValuesCleared += other.FieldValuesCleared;
        AlternateTextsRemoved += other.AlternateTextsRemoved;
    }
}
