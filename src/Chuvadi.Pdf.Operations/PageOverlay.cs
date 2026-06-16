// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.10.1 (form XObjects), §11.6.4.4 (/ca opacity)
// PHASE: Document operations — recolour/fade existing pages.

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// Recolours existing pages by drawing a solid background fill behind the page
/// content and/or rendering the existing content at reduced opacity. The page's
/// content is wrapped in a form XObject and painted under an ExtGState
/// constant-alpha (<c>/ca</c>), so 0 opacity yields a blank (optionally
/// coloured) page and 1 leaves content fully opaque.
/// PDF 32000-1:2008 §8.10.1 (form XObjects), §11.6.4.4 (constant alpha).
/// </summary>
public static class PageOverlay
{
    /// <summary>
    /// Writes <paramref name="document"/> to <paramref name="output"/> with the
    /// requested pages recoloured.
    /// </summary>
    /// <param name="output">The stream to write the updated PDF to.</param>
    /// <param name="document">The source document.</param>
    /// <param name="pageIndices">
    /// Zero-based indices of pages to recolour. Null recolours every page.
    /// </param>
    /// <param name="background">
    /// Fill colour drawn behind the page content, or null for no fill.
    /// </param>
    /// <param name="contentOpacity">
    /// Opacity of the existing content, 0 (fully transparent) to 1 (unchanged).
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="output"/> or <paramref name="document"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="contentOpacity"/> is outside [0, 1].
    /// </exception>
    public static void Apply(
        Stream output,
        PdfDocument document,
        IEnumerable<int>? pageIndices,
        ColorF? background,
        float contentOpacity)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(document);

        if (contentOpacity < 0f || contentOpacity > 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(contentOpacity), contentOpacity, "Opacity must be in [0, 1].");
        }

        HashSet<int> targets = ResolveTargets(pageIndices, document.PageCount);

        PageContentEditor editor = new PageContentEditor(document);

        foreach (int pageIndex in targets)
        {
            if (pageIndex < 0 || pageIndex >= document.PageCount)
            {
                continue;
            }

            editor.Recolor(pageIndex, background, contentOpacity);
        }

        editor.Write(output);
    }

    private static HashSet<int> ResolveTargets(IEnumerable<int>? pageIndices, int pageCount)
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
}
