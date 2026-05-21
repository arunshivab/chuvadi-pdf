// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §14.3.3 — Document information dictionary
//        PDF 32000-1:2008 §7.5.2 — File header (version)
// PHASE: v2.0.0 R3 — DocumentInfo aggregate

using System;

namespace Chuvadi.Pdf.Documents;

/// <summary>
/// Aggregate view of a document's metadata, structural properties, and
/// security state — the single object returned by
/// <see cref="PdfDocument.Info"/>.
/// </summary>
/// <remarks>
/// <para>
/// In v2.0.0 the breaking-change release, the raw
/// <see cref="Chuvadi.Pdf.Primitives.PdfDictionary"/> previously returned
/// by <see cref="PdfDocument.Info"/> moves to
/// <see cref="PdfDocument.InfoDictionary"/>, and
/// <see cref="PdfDocument.Info"/> now returns this aggregate. The scalar
/// helpers (<see cref="PdfDocument.Title"/>, <see cref="PdfDocument.Author"/>,
/// …) are unchanged and remain available as ergonomic shortcuts.
/// </para>
/// <para>
/// All metadata strings come from the
/// <c>/Info</c> dictionary as PDF document strings, decoded to .NET
/// strings using the encoding declared by their PDF leading byte order
/// mark.
/// </para>
/// </remarks>
public sealed class DocumentInfo
{
    /// <summary>Initialises a <see cref="DocumentInfo"/>.</summary>
    public DocumentInfo(
        string? title,
        string? author,
        string? subject,
        string? keywords,
        string? creator,
        string? producer,
        DateTimeOffset? creationDate,
        DateTimeOffset? modificationDate,
        string? pdfVersion,
        int pageCount,
        long? fileSize,
        EncryptionInfo encryption)
    {
        ArgumentNullException.ThrowIfNull(encryption);

        Title = title;
        Author = author;
        Subject = subject;
        Keywords = keywords;
        Creator = creator;
        Producer = producer;
        CreationDate = creationDate;
        ModificationDate = modificationDate;
        PdfVersion = pdfVersion;
        PageCount = pageCount;
        FileSize = fileSize;
        Encryption = encryption;
    }

    /// <summary>Gets the document title, or null when not set.</summary>
    public string? Title { get; }

    /// <summary>Gets the document author, or null when not set.</summary>
    public string? Author { get; }

    /// <summary>Gets the document subject, or null when not set.</summary>
    public string? Subject { get; }

    /// <summary>Gets the document keywords, or null when not set.</summary>
    public string? Keywords { get; }

    /// <summary>Gets the name of the application that created the document.</summary>
    public string? Creator { get; }

    /// <summary>Gets the name of the PDF producer application.</summary>
    public string? Producer { get; }

    /// <summary>Gets the document creation date, or null when not set or unparseable.</summary>
    public DateTimeOffset? CreationDate { get; }

    /// <summary>Gets the document last-modified date, or null when not set or unparseable.</summary>
    public DateTimeOffset? ModificationDate { get; }

    /// <summary>
    /// Gets the PDF version declared in the file header (e.g. "1.7", "2.0"),
    /// or null when the header could not be parsed.
    /// </summary>
    public string? PdfVersion { get; }

    /// <summary>Gets the total number of pages in the document.</summary>
    public int PageCount { get; }

    /// <summary>
    /// Gets the file size in bytes when the document was opened from a
    /// seekable stream, or null otherwise.
    /// </summary>
    /// <remarks>
    /// v2.0.0 always returns null; a future release will surface the
    /// underlying stream length here.
    /// </remarks>
    public long? FileSize { get; }

    /// <summary>Gets the document's security state.</summary>
    public EncryptionInfo Encryption { get; }
}
