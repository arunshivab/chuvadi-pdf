// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Authoring;

/// <summary>
/// The result of embedding a TrueType font: the top-level Type0 font object id
/// to reference from a page's <c>/Font</c> resource, and every object that must
/// be added to the document.
/// </summary>
public sealed class EmbeddedFontObjects
{
    /// <summary>Initialises a new <see cref="EmbeddedFontObjects"/>.</summary>
    /// <param name="type0FontId">The top-level Type0 font object id.</param>
    /// <param name="objects">All objects to add to the document.</param>
    public EmbeddedFontObjects(PdfObjectId type0FontId, IReadOnlyList<PdfIndirectObject> objects)
    {
        Type0FontId = type0FontId;
        Objects = objects;
    }

    /// <summary>Gets the Type0 font object id to reference from page resources.</summary>
    public PdfObjectId Type0FontId { get; }

    /// <summary>Gets every object to add to the document.</summary>
    public IReadOnlyList<PdfIndirectObject> Objects { get; }
}
