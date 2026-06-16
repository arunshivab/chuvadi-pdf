// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §14.3.3 — Document information dictionary
// PHASE: Document operations — set Info metadata on an existing document.

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// Sets document-information metadata (Title, Author, Subject, Keywords) on an
/// existing document and writes the result. The rest of the document — pages,
/// outlines, resources — is preserved unchanged. A null argument leaves the
/// corresponding entry untouched; passing an empty string clears it.
/// PDF 32000-1:2008 §14.3.3 — Document information dictionary.
/// </summary>
public static class DocumentInfo
{
    /// <summary>
    /// Writes <paramref name="document"/> to <paramref name="output"/> with the
    /// given information-dictionary entries applied. Any parameter left null is
    /// preserved from the source document; a non-null value (including the empty
    /// string) replaces it.
    /// </summary>
    /// <param name="output">The stream to write the updated PDF to.</param>
    /// <param name="document">The source document.</param>
    /// <param name="title">New /Title, or null to leave unchanged.</param>
    /// <param name="author">New /Author, or null to leave unchanged.</param>
    /// <param name="subject">New /Subject, or null to leave unchanged.</param>
    /// <param name="keywords">New /Keywords, or null to leave unchanged.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="output"/> or <paramref name="document"/> is null.
    /// </exception>
    public static void Apply(
        Stream output,
        PdfDocument document,
        string? title = null,
        string? author = null,
        string? subject = null,
        string? keywords = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(document);

        Dictionary<string, string?> entries = new Dictionary<string, string?>(StringComparer.Ordinal);
        AddIfSet(entries, "Title", title);
        AddIfSet(entries, "Author", author);
        AddIfSet(entries, "Subject", subject);
        AddIfSet(entries, "Keywords", keywords);

        DocumentRewriter rewriter = new DocumentRewriter(document);

        // Start from a copy of the existing Info dictionary (if any) so entries
        // not being changed (Creator, Producer, dates) survive.
        PdfDictionary info = document.Info is not null
            ? ObjectImporter.CopyDictionary(document.Info)
            : new PdfDictionary();

        foreach (KeyValuePair<string, string?> entry in entries)
        {
            PdfName key = PdfName.Intern(entry.Key);
            if (entry.Value is null)
            {
                continue;
            }

            info.Set(key, new PdfString(entry.Value));
        }

        PdfObjectId infoId = rewriter.AllocateId();
        rewriter.AddObject(infoId, info);
        rewriter.SetTrailerEntry(PdfName.Info, new PdfReference(infoId));

        rewriter.Write(output);
    }

    private static void AddIfSet(Dictionary<string, string?> map, string key, string? value)
    {
        if (value is not null)
        {
            map[key] = value;
        }
    }
}
