// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.3.10 — Indirect objects
// PHASE: Phase 1 — Chuvadi.Pdf.Objects
// In-memory object graph with lazy indirect reference resolution.

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Objects;

/// <summary>
/// In-memory store for PDF indirect objects, with lazy indirect
/// reference resolution.
/// </summary>
/// <remarks>
/// <see cref="PdfObjectStore"/> is the central object graph used during
/// PDF reading, writing, and modification. It maps <see cref="PdfObjectId"/>
/// values to <see cref="PdfIndirectObject"/> instances and resolves
/// <see cref="PdfReference"/> primitives to their target values.
///
/// Lazy loading: the store does not pre-populate itself. Objects are
/// added on demand as the reader encounters them or as the document
/// model requests them. An optional <see cref="Func{T, TResult}"/>
/// loader delegate can be provided to load objects on demand from the
/// underlying PDF stream.
///
/// Thread safety: not thread-safe. Synchronise externally if needed.
/// </remarks>
public sealed class PdfObjectStore : IPdfObjectResolver
{
    private readonly Dictionary<PdfObjectId, PdfIndirectObject> _objects;

    // Optional delegate that loads an object from the underlying source
    // (e.g. the PdfReader in Chuvadi.Pdf.IO) when a requested object
    // is not yet in the store.
    private readonly Func<PdfObjectId, PdfIndirectObject?>? _loader;

    /// <summary>
    /// Creates an empty <see cref="PdfObjectStore"/> with no loader.
    /// Objects must be added explicitly via <see cref="Add(PdfIndirectObject)"/>.
    /// </summary>
    public PdfObjectStore()
    {
        _objects = new Dictionary<PdfObjectId, PdfIndirectObject>();
        _loader = null;
    }

    /// <summary>
    /// Creates a <see cref="PdfObjectStore"/> with a loader delegate.
    /// When an object is not in the store, the loader is called and the
    /// result is cached.
    /// </summary>
    /// <param name="loader">
    /// A function that loads an object given its <see cref="PdfObjectId"/>.
    /// Return null when the object does not exist.
    /// </param>
    public PdfObjectStore(Func<PdfObjectId, PdfIndirectObject?> loader)
    {
        _objects = new Dictionary<PdfObjectId, PdfIndirectObject>();
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
    }

    /// <summary>
    /// Gets or sets the resource limits for work on this document's objects
    /// (set from <c>PdfOpenOptions.Limits</c> when the document is opened).
    /// <c>PageRasterizer</c> and <c>TextExtractor</c> apply them to every call.
    /// Null: no limits.
    /// </summary>
    public PdfLimits? Limits { get; set; }

    // ── Object management ─────────────────────────────────────────────────

    /// <summary>Gets the number of objects currently loaded in the store.</summary>
    public int Count => _objects.Count;

    /// <summary>
    /// Adds or replaces an indirect object in the store.
    /// </summary>
    public void Add(PdfIndirectObject obj)
    {
        if (obj is null)
        {
            throw new ArgumentNullException(nameof(obj));
        }

        _objects[obj.Id] = obj;
    }

    /// <summary>
    /// Adds a primitive with the given identity as an indirect object.
    /// </summary>
    public void Add(PdfObjectId id, PdfPrimitive value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        _objects[id] = new PdfIndirectObject(id, value);
    }

    /// <summary>
    /// Removes the object with the given identity from the store.
    /// Returns true if it was present.
    /// </summary>
    public bool Remove(PdfObjectId id)
    {
        return _objects.Remove(id);
    }

    /// <summary>
    /// Attempts to get the indirect object with the given identity.
    /// If not in the store and a loader was provided, the loader is called.
    /// </summary>
    public bool TryGet(PdfObjectId id, out PdfIndirectObject? obj)
    {
        if (_objects.TryGetValue(id, out obj))
        {
            return true;
        }

        if (_loader is not null)
        {
            obj = _loader(id);

            if (obj is not null)
            {
                _objects[id] = obj;
                return true;
            }
        }

        obj = null;
        return false;
    }

    // ── IPdfObjectResolver ────────────────────────────────────────────────

    /// <inheritdoc/>
    public PdfPrimitive Resolve(PdfPrimitive primitive)
    {
        if (primitive is null)
        {
            throw new ArgumentNullException(nameof(primitive));
        }

        if (primitive is not PdfReference reference)
        {
            return primitive;
        }

        return ResolveById(reference.ObjectId);
    }

    /// <inheritdoc/>
    public PdfPrimitive ResolveById(PdfObjectId id)
    {
        if (TryGet(id, out PdfIndirectObject? obj) && obj is not null)
        {
            return obj.Value;
        }

        // Missing or free object resolves to null per PDF spec.
        // PDF 32000-1:2008 §7.3.9: null can appear as the value of
        // an indirect object.
        return PdfNull.Value;
    }

    /// <inheritdoc/>
    public bool Contains(PdfObjectId id)
    {
        if (_objects.ContainsKey(id))
        {
            return true;
        }

        if (_loader is not null)
        {
            PdfIndirectObject? obj = _loader(id);

            if (obj is not null)
            {
                _objects[id] = obj;
                return true;
            }
        }

        return false;
    }

    // ── Convenience resolver extension ────────────────────────────────────

    /// <summary>
    /// Resolves a primitive and attempts to cast the result to
    /// <typeparamref name="T"/>. Returns null when the object is missing
    /// or the value is not of the expected type.
    /// </summary>
    public T? ResolveAs<T>(PdfPrimitive primitive) where T : PdfPrimitive
    {
        return Resolve(primitive) as T;
    }

    /// <summary>
    /// Resolves a <see cref="PdfDictionary"/> entry that may be a direct
    /// or indirect value. Returns null when the key is absent or the value
    /// is of the wrong type.
    /// </summary>
    public T? ResolveDictionaryEntry<T>(PdfDictionary dictionary, PdfName key)
        where T : PdfPrimitive
    {
        if (dictionary is null)
        {
            throw new ArgumentNullException(nameof(dictionary));
        }

        if (key is null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        if (!dictionary.TryGetValue(key, out PdfPrimitive? value))
        {
            return null;
        }

        return Resolve(value) as T;
    }

    /// <summary>Gets all indirect objects currently in the store.</summary>
    public IEnumerable<PdfIndirectObject> Objects => _objects.Values;
}
