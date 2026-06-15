// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.5 (objects), §8.10.1 (form XObjects)
// PHASE: Page composition — shared cross-document import primitives.

using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Filters;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// Shared machinery for importing objects from one PDF document into another:
/// reference collection, deep copy with object-number remapping, and content
/// decoding. Used by page-tree operations (merge/extract/…) and page
/// composition (place/stamp). Centralised here so the cross-document
/// object-number remap — keyed by (document, number) — has a single
/// implementation.
/// </summary>
internal static class ObjectImporter
{
    // The empty remap detaches a dictionary from its source document without
    // renumbering (object IDs aren't known until the final write).
    private static readonly Dictionary<(int Doc, int Num), int> EmptyRemap =
        new Dictionary<(int Doc, int Num), int>();

    /// <summary>
    /// Recursively gathers every indirect object reachable from
    /// <paramref name="primitive"/>, resolving references via
    /// <paramref name="resolver"/>. Never follows <c>/Parent</c> (it forms a
    /// cycle up the page tree).
    /// </summary>
    internal static void CollectReferences(
        PdfPrimitive primitive,
        IPdfObjectResolver resolver,
        List<PdfIndirectObject> collected,
        HashSet<int> visited)
    {
        if (primitive is PdfReference reference)
        {
            int num = reference.ObjectId.ObjectNumber;

            if (visited.Contains(num))
            {
                return;
            }

            visited.Add(num);
            PdfPrimitive resolved = resolver.Resolve(reference);
            collected.Add(new PdfIndirectObject(reference.ObjectId, resolved));
            CollectReferences(resolved, resolver, collected, visited);
        }
        else if (primitive is PdfDictionary dict)
        {
            foreach (KeyValuePair<PdfName, PdfPrimitive> entry in dict)
            {
                if (entry.Key == PdfName.Parent)
                {
                    continue; // Never follow /Parent — it forms a cycle
                }

                CollectReferences(entry.Value, resolver, collected, visited);
            }
        }
        else if (primitive is PdfArray array)
        {
            for (int i = 0; i < array.Count; i++)
            {
                CollectReferences(array[i], resolver, collected, visited);
            }
        }
        else if (primitive is PdfStream stream)
        {
            foreach (KeyValuePair<PdfName, PdfPrimitive> entry in stream.Dictionary)
            {
                CollectReferences(entry.Value, resolver, collected, visited);
            }
        }
    }

    /// <summary>
    /// Deep-copies a primitive, rewriting indirect-reference object numbers
    /// through <paramref name="idRemap"/>. Always returns new dictionary,
    /// array, and stream instances so neither the source document nor any
    /// other copy is ever mutated. Scalars and references are immutable and
    /// returned as-is, with a reference's number rewritten when present in the
    /// remap.
    /// </summary>
    internal static PdfPrimitive DeepCopyPrimitive(
        PdfPrimitive primitive,
        int docIndex,
        Dictionary<(int Doc, int Num), int> idRemap)
    {
        switch (primitive)
        {
            case PdfReference reference:
                return idRemap.TryGetValue(
                    (docIndex, reference.ObjectId.ObjectNumber), out int newNum)
                    ? new PdfReference(new PdfObjectId(newNum, 0))
                    : reference;

            case PdfStream stream:
                return new PdfStream(
                    DeepCopyDictionary(stream.Dictionary, docIndex, idRemap),
                    stream.RawBytes);

            case PdfDictionary dict:
                return DeepCopyDictionary(dict, docIndex, idRemap);

            case PdfArray array:
                PdfArray arrayCopy = new PdfArray([]);
                for (int i = 0; i < array.Count; i++)
                {
                    arrayCopy.Add(DeepCopyPrimitive(array[i], docIndex, idRemap));
                }
                return arrayCopy;

            default:
                // PdfName, PdfInteger, PdfReal, PdfString, PdfBoolean,
                // PdfNull — immutable, safe to share.
                return primitive;
        }
    }

    /// <summary>Deep-copies a dictionary with reference remapping.</summary>
    internal static PdfDictionary DeepCopyDictionary(
        PdfDictionary source,
        int docIndex,
        Dictionary<(int Doc, int Num), int> idRemap)
    {
        PdfDictionary copy = new PdfDictionary();

        foreach (KeyValuePair<PdfName, PdfPrimitive> entry in source)
        {
            copy.Set(entry.Key, DeepCopyPrimitive(entry.Value, docIndex, idRemap));
        }

        return copy;
    }

    /// <summary>
    /// Detaches a dictionary from its source document (deep copy, no
    /// renumbering) for use before object IDs are assigned.
    /// </summary>
    internal static PdfDictionary CopyDictionary(PdfDictionary source)
    {
        return DeepCopyDictionary(source, 0, EmptyRemap);
    }

    /// <summary>
    /// Decodes a stream's bytes through its <c>/Filter</c> chain (resolving a
    /// single name or an array of names, with matching <c>/DecodeParms</c>).
    /// Returns the raw bytes unchanged when the stream is unfiltered.
    /// </summary>
    internal static byte[] DecodeStreamContent(PdfStream stream)
    {
        PdfDictionary dict = stream.Dictionary;

        if (!dict.TryGetValue(PdfName.Filter, out PdfPrimitive? filterPrim))
        {
            return stream.RawBytes;
        }

        dict.TryGetValue(PdfName.Intern("DecodeParms"), out PdfPrimitive? decodeParms);

        FilterPipeline pipeline = FilterRegistry.CreateDefaultPipeline();
        byte[] current = stream.RawBytes;

        if (filterPrim is PdfName singleName)
        {
            string resolved = FilterRegistry.ResolveAlias(singleName.Value);
            return pipeline.Decode(resolved, current, FilterParameters.FromDictionary(decodeParms, 0));
        }

        if (filterPrim is PdfArray filterArray)
        {
            for (int i = 0; i < filterArray.Count; i++)
            {
                if (filterArray[i] is PdfName filterName)
                {
                    string resolved = FilterRegistry.ResolveAlias(filterName.Value);
                    current = pipeline.Decode(resolved, current, FilterParameters.FromDictionary(decodeParms, i));
                }
            }
        }

        return current;
    }

    /// <summary>
    /// Resolves a page's <c>/Contents</c> (a single stream or an array of
    /// streams) and returns the fully decoded content-stream bytes,
    /// concatenated with newline separators per ISO 32000-1 §7.8.2.
    /// </summary>
    internal static byte[] ConcatenatePageContent(PdfPage page, IPdfObjectResolver resolver)
    {
        PdfPrimitive? contents = page.Contents;

        if (contents is null)
        {
            return [];
        }

        PdfPrimitive resolved = resolver.Resolve(contents);

        if (resolved is PdfStream single)
        {
            return DecodeStreamContent(single);
        }

        if (resolved is PdfArray array)
        {
            using MemoryStream buffer = new MemoryStream();

            for (int i = 0; i < array.Count; i++)
            {
                if (resolver.Resolve(array[i]) is PdfStream part)
                {
                    byte[] decoded = DecodeStreamContent(part);
                    buffer.Write(decoded, 0, decoded.Length);
                    buffer.WriteByte((byte)'\n');
                }
            }

            return buffer.ToArray();
        }

        return [];
    }
}
