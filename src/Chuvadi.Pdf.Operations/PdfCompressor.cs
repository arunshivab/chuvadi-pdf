// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.5 — File structure (cross-reference rebuild),
//        §7.4.4 — FlateDecode, §8.9 — Images
// PHASE: Phase 2.9 — Reader feature batch (PDF compression)
// Rewrites a document smaller: garbage-collects unreachable objects,
// Flate-compresses raw streams, and optionally re-encodes images as JPEG.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Filters;
using Chuvadi.Pdf.Images;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// Options controlling <see cref="PdfCompressor.Compress"/>.
/// </summary>
public sealed record CompressionOptions
{
    /// <summary>
    /// When true, eligible raster images (8-bit RGB or grayscale, stored raw
    /// or Flate-compressed, without transparency) are re-encoded as JPEG at
    /// <see cref="JpegQuality"/>. This is lossy and off by default.
    /// </summary>
    public bool RecompressImages { get; init; }

    /// <summary>
    /// JPEG quality (1–100, IJG convention) used when
    /// <see cref="RecompressImages"/> is enabled. Default 75.
    /// </summary>
    public int JpegQuality { get; init; } = 75;

    /// <summary>
    /// Minimum raw stream length, in bytes, worth Flate-compressing.
    /// Streams shorter than this are left untouched. Default 64.
    /// </summary>
    public int MinStreamLengthToCompress { get; init; } = 64;

    /// <summary>
    /// Minimum pixel count (width × height) for an image to be considered
    /// for JPEG recompression. Default 4096 (e.g. 64×64).
    /// </summary>
    public int MinImagePixelsToRecompress { get; init; } = 4096;

    /// <summary>Drop the catalog /Metadata (XMP) stream. Off by default.</summary>
    public bool RemoveMetadata { get; init; }

    /// <summary>Drop the document information dictionary. Off by default.</summary>
    public bool RemoveDocumentInfo { get; init; }

    /// <summary>
    /// Drop document-level JavaScript: the catalog /Names /JavaScript name tree,
    /// an /OpenAction that runs a script, and the document /AA additional-actions.
    /// Off by default.
    /// </summary>
    public bool RemoveJavaScript { get; init; }

    /// <summary>
    /// Drop embedded file attachments: the catalog /Names /EmbeddedFiles name
    /// tree, the catalog /AF associated files, and file-attachment annotations.
    /// Off by default.
    /// </summary>
    public bool RemoveAttachments { get; init; }

    /// <summary>Drop page thumbnail images (the page /Thumb entry). Off by default.</summary>
    public bool RemoveThumbnails { get; init; }

    /// <summary>
    /// Drop application-private data (the /PieceInfo dictionary) on the catalog
    /// and pages. Off by default.
    /// </summary>
    public bool RemovePieceInfo { get; init; }

    /// <summary>
    /// Drop the logical structure tree (the catalog /StructTreeRoot and
    /// /MarkInfo). This removes accessibility tagging and is therefore lossy.
    /// Off by default.
    /// </summary>
    public bool RemoveStructTree { get; init; }

    /// <summary>
    /// Drop page annotations (the page /Annots array): links, comments, and form
    /// field widgets. This is lossy. Off by default.
    /// </summary>
    public bool RemoveAnnotations { get; init; }

    /// <summary>
    /// When false (the default), a digitally signed document is not rewritten:
    /// <see cref="PdfCompressor.Compress"/> returns a result whose
    /// <see cref="CompressionResult.SkipReason"/> is
    /// <see cref="CompressionSkipReason.Signed"/> and writes nothing, because a
    /// full rewrite invalidates the signature byte ranges. Set to true to
    /// rewrite anyway, accepting that existing signatures will break.
    /// </summary>
    public bool AllowSignedRewrite { get; init; }

    /// <summary>
    /// When false (the default), an encrypted document is not rewritten:
    /// <see cref="PdfCompressor.Compress"/> returns a result whose
    /// <see cref="CompressionResult.SkipReason"/> is
    /// <see cref="CompressionSkipReason.Encrypted"/> and writes nothing, because
    /// the reader exposes decrypted content and the rewrite would emit the
    /// document without encryption. Set to true to rewrite the decrypted
    /// content anyway.
    /// </summary>
    public bool AllowEncryptedRewrite { get; init; }

    /// <summary>
    /// When true, streams are re-deflated at maximum effort
    /// (<see cref="Chuvadi.Pdf.Filters.DeflateEffort.Maximum"/>): the encoder also
    /// tries the runtime deflater and an iterated optimal ("zopfli-style") parse
    /// and keeps the smallest result. This yields the best lossless ratio at the
    /// cost of compression speed. Default false (fast greedy parse).
    /// </summary>
    public bool MaxDeflateStreams { get; init; }
}

/// <summary>
/// Why <see cref="PdfCompressor.Compress"/> declined to rewrite a document.
/// </summary>
public enum CompressionSkipReason
{
    /// <summary>The document was rewritten; no skip occurred.</summary>
    None = 0,

    /// <summary>
    /// The document is digitally signed and
    /// <see cref="CompressionOptions.AllowSignedRewrite"/> was not set.
    /// </summary>
    Signed = 1,

    /// <summary>
    /// The document is encrypted and
    /// <see cref="CompressionOptions.AllowEncryptedRewrite"/> was not set.
    /// </summary>
    Encrypted = 2,
}

/// <summary>
/// Statistics describing what <see cref="PdfCompressor.Compress"/> did.
/// </summary>
public sealed record CompressionResult
{
    /// <summary>Indirect objects dropped as unreachable from the trailer.</summary>
    public int ObjectsRemoved { get; init; }

    /// <summary>Previously-uncompressed streams that were Flate-compressed.</summary>
    public int StreamsCompressed { get; init; }

    /// <summary>Images re-encoded as JPEG.</summary>
    public int ImagesRecompressed { get; init; }

    /// <summary>Byte-identical indirect objects merged into one.</summary>
    public int DuplicatesRemoved { get; init; }

    /// <summary>Content streams shrunk by whitespace/comment minification.</summary>
    public int ContentStreamsMinified { get; init; }

    /// <summary>
    /// Why the rewrite was skipped, or <see cref="CompressionSkipReason.None"/>
    /// when the document was rewritten normally.
    /// </summary>
    public CompressionSkipReason SkipReason { get; init; }

    /// <summary>
    /// True when the document was left untouched and nothing was written to the
    /// output stream because a safety guard fired (see <see cref="SkipReason"/>).
    /// </summary>
    public bool Skipped => SkipReason != CompressionSkipReason.None;
}

/// <summary>
/// Rewrites a PDF document to a smaller equivalent.
/// </summary>
/// <remarks>
/// <para>
/// Three independent reductions are applied. First, a reachability pass from
/// the trailer drops every object the document no longer references —
/// orphans left behind by incremental updates, deleted pages, or earlier
/// edits — and renumbers the survivors densely. Second, streams stored
/// without any filter are Flate-compressed when that makes them smaller.
/// Third, optionally, photographic images are re-encoded as JPEG.
/// </para>
/// <para>
/// The catalog graph (outlines, forms, named destinations, metadata) is
/// preserved; this is a rewrite, not a page extraction. Because a full rewrite
/// invalidates digital signatures and emits decrypted content, signed and
/// encrypted documents are skipped by default — nothing is written and the
/// returned <see cref="CompressionResult.SkipReason"/> says why (see
/// <see cref="CompressionOptions.AllowSignedRewrite"/> and
/// <see cref="CompressionOptions.AllowEncryptedRewrite"/> to override).
/// The result is written with an object stream and a compressed cross-reference
/// stream (PDF 1.5+), the most compact lossless structure. Opt-in flags on
/// <see cref="CompressionOptions"/> can additionally drop metadata, JavaScript,
/// attachments, thumbnails, piece-info, the structure tree, and annotations.
/// </para>
/// </remarks>
public static class PdfCompressor
{
    /// <summary>
    /// Compresses <paramref name="document"/> and writes the result to
    /// <paramref name="output"/>.
    /// </summary>
    /// <param name="document">The source document.</param>
    /// <param name="output">The writable stream receiving the new PDF.</param>
    /// <param name="options">Compression options; null uses defaults.</param>
    /// <returns>Statistics about the rewrite.</returns>
    public static CompressionResult Compress(
        PdfDocument document, Stream output, CompressionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(output);
        options ??= new CompressionOptions();

        // ── 0. Safety guard ───────────────────────────────────────────────
        // A full rewrite invalidates digital signatures and emits the document
        // without its encryption. Such documents are skipped by default — no
        // bytes are written to the output — so a batch run over a mixed corpus
        // carries on instead of throwing; callers opt in per hazard to rewrite
        // anyway.
        CompressionSkipReason skip = EvaluateGuard(document, options);
        if (skip != CompressionSkipReason.None)
        {
            return new CompressionResult { SkipReason = skip };
        }

        PdfName[] effectiveRootKeys = options.RemoveDocumentInfo
            ? new[] { PdfName.Root }
            : RootKeys;

        // ── 1. Reachability from the trailer ─────────────────────────────
        Dictionary<int, PdfPrimitive> reachable = new();
        foreach (PdfName key in effectiveRootKeys)
        {
            if (document.Trailer.TryGetValue(key, out PdfPrimitive? value))
            {
                Collect(value, document.Objects, reachable);
            }
        }

        // The object store loads lazily, so its cache cannot be used to
        // count the source document's objects. The trailer's /Size (highest
        // object number + 1, PDF 32000-1:2008 Table 15) gives the number of
        // object slots; the difference against the reachable set counts the
        // slots not carried over (orphans plus free entries).
        int totalObjects = 0;
        if (document.Trailer.TryGetValue(PdfName.Intern("Size"), out PdfPrimitive? sizeValue) &&
            sizeValue is PdfInteger size)
        {
            totalObjects = size.Value - 1;
        }

        // ── 2. Dense renumbering ──────────────────────────────────────────
        Dictionary<int, int> remap = new(reachable.Count);
        int next = 1;
        foreach (int oldNumber in reachable.Keys)
        {
            remap[oldNumber] = next++;
        }

        // ── 3. Copy with remapped references and stream reduction ────────
        int streamsCompressed = 0;
        int imagesRecompressed = 0;
        int contentStreamsMinified = 0;
        List<PdfIndirectObject> objects = new(reachable.Count);
        HashSet<int> contentStreamNumbers = CollectContentStreamNumbers(document);

        foreach (KeyValuePair<int, PdfPrimitive> entry in reachable)
        {
            PdfPrimitive copy = DeepCopy(entry.Value, remap);

            if (copy is PdfStream stream)
            {
                bool isContent = contentStreamNumbers.Contains(entry.Key) || IsFormXObject(stream.Dictionary);
                PdfStream? minified = isContent ? TryMinifyContentStream(stream, options) : null;
                if (minified is not null)
                {
                    copy = minified;
                    contentStreamsMinified++;
                }
                else
                {
                    PdfStream? recompressed = options.RecompressImages
                        ? TryRecompressImage(stream, document.Objects, options)
                        : null;
                    if (recompressed is not null)
                    {
                        copy = recompressed;
                        imagesRecompressed++;
                    }
                    else
                    {
                        PdfStream? flated = TryFlateRawStream(stream, options);
                        if (flated is not null)
                        {
                            copy = flated;
                            streamsCompressed++;
                        }
                    }
                }
            }

            objects.Add(new PdfIndirectObject(new PdfObjectId(remap[entry.Key], 0), copy));
        }

        // ── 4. New trailer ────────────────────────────────────────────────
        PdfDictionary trailer = new();
        foreach (PdfName key in effectiveRootKeys)
        {
            if (document.Trailer.TryGetValue(key, out PdfPrimitive? value))
            {
                trailer.Set(key, RemapPrimitive(value, remap));
            }
        }
        if (document.Trailer.TryGetValue(PdfName.Intern("ID"), out PdfPrimitive? fileId))
        {
            trailer.Set(PdfName.Intern("ID"), DeepCopy(fileId, remap));
        }

        // ── 5. Optional stripping, then drop now-orphaned objects ─────────
        // Each opt-in flag removes a catalog or page entry; the streams and
        // dictionaries thereby made unreachable are swept by the garbage
        // collector that follows. RemoveDocumentInfo needs no strip pass here —
        // the Info dictionary is simply not collected as a root above.
        if (StripRequested(options))
        {
            StripCategories(objects, trailer, options);
            GarbageCollect(objects, trailer);
        }

        // Orphans unreachable from the trailer plus anything stripped above.
        // Captured before deduplication so byte-identical merges are reported
        // only in DuplicatesRemoved rather than being double-counted here.
        int objectsRemoved = Math.Max(0, totalObjects - objects.Count);

        // ── 6. Merge byte-identical objects, then densify numbering ──────
        PdfDictionary deduped = Deduplicate(objects, trailer, out int duplicatesRemoved);
        trailer = Densify(objects, deduped);

        SynthesizedMetadata synthesized = SynthesizedMetadata.All;
        if (options.RemoveDocumentInfo)
        {
            synthesized &= ~SynthesizedMetadata.Info;
        }
        if (options.RemoveMetadata)
        {
            synthesized &= ~SynthesizedMetadata.Metadata;
        }

        // Object streams + a compressed cross-reference stream (PDF 1.5+) give
        // the largest lossless structural win, so the compressor enables them by
        // default. Plain document saves via PdfWriter.Write remain classic xref.
        PdfWriter.Write(output, objects, trailer, null, synthesized, XrefStyle.Stream);

        return new CompressionResult
        {
            ObjectsRemoved = objectsRemoved,
            StreamsCompressed = streamsCompressed,
            ImagesRecompressed = imagesRecompressed,
            DuplicatesRemoved = duplicatesRemoved,
            ContentStreamsMinified = contentStreamsMinified,
        };
    }

    private static readonly PdfName[] RootKeys =
    [
        PdfName.Root,
        PdfName.Intern("Info"),
    ];

    // ── Safety guard ──────────────────────────────────────────────────────

    private static CompressionSkipReason EvaluateGuard(
        PdfDocument document, CompressionOptions options)
    {
        if (!options.AllowSignedRewrite && IsSigned(document))
        {
            return CompressionSkipReason.Signed;
        }

        if (!options.AllowEncryptedRewrite && document.Encryption is not null)
        {
            return CompressionSkipReason.Encrypted;
        }

        return CompressionSkipReason.None;
    }

    /// <summary>
    /// Detects whether the document carries digital signatures by inspecting the
    /// AcroForm /SigFlags SignaturesExist bit (PDF 32000-1:2008 §12.7.2, Table
    /// 219). The low-level catalog walk avoids a dependency on the signatures
    /// module.
    /// </summary>
    private static bool IsSigned(PdfDocument document)
    {
        if (!document.Trailer.TryGetValue(PdfName.Root, out PdfPrimitive? rootValue) ||
            document.Objects.Resolve(rootValue) is not PdfDictionary catalog)
        {
            return false;
        }

        if (!catalog.TryGetValue(PdfName.Intern("AcroForm"), out PdfPrimitive? acroValue) ||
            document.Objects.Resolve(acroValue) is not PdfDictionary acroForm)
        {
            return false;
        }

        if (!acroForm.TryGetValue(PdfName.Intern("SigFlags"), out PdfPrimitive? flagsValue) ||
            document.Objects.Resolve(flagsValue) is not PdfInteger flags)
        {
            return false;
        }

        return (flags.Value & 1) != 0;
    }

    // ── Reachability ──────────────────────────────────────────────────────

    private static void Collect(
        PdfPrimitive primitive,
        PdfObjectStore resolver,
        Dictionary<int, PdfPrimitive> reachable)
    {
        switch (primitive)
        {
            case PdfReference reference:
                {
                    int number = reference.ObjectId.ObjectNumber;
                    if (reachable.ContainsKey(number))
                    {
                        return;
                    }

                    PdfPrimitive resolved = resolver.Resolve(reference);
                    reachable[number] = resolved;
                    Collect(resolved, resolver, reachable);
                    break;
                }

            case PdfDictionary dict:
                {
                    foreach (KeyValuePair<PdfName, PdfPrimitive> entry in dict)
                    {
                        Collect(entry.Value, resolver, reachable);
                    }
                    break;
                }

            case PdfArray array:
                {
                    for (int i = 0; i < array.Count; i++)
                    {
                        Collect(array[i], resolver, reachable);
                    }
                    break;
                }

            case PdfStream stream:
                {
                    foreach (KeyValuePair<PdfName, PdfPrimitive> entry in stream.Dictionary)
                    {
                        Collect(entry.Value, resolver, reachable);
                    }
                    break;
                }

            default:
                break;
        }
    }

    // ── Copying with reference remapping ──────────────────────────────────

    private static PdfPrimitive DeepCopy(PdfPrimitive primitive, Dictionary<int, int> remap)
    {
        switch (primitive)
        {
            case PdfReference reference:
                return RemapPrimitive(reference, remap);

            case PdfDictionary dict:
                {
                    PdfDictionary copy = new();
                    foreach (KeyValuePair<PdfName, PdfPrimitive> entry in dict)
                    {
                        copy.Set(entry.Key, DeepCopy(entry.Value, remap));
                    }
                    return copy;
                }

            case PdfArray array:
                {
                    PdfArray copy = new();
                    for (int i = 0; i < array.Count; i++)
                    {
                        copy.Add(DeepCopy(array[i], remap));
                    }
                    return copy;
                }

            case PdfStream stream:
                {
                    PdfDictionary dictCopy = (PdfDictionary)DeepCopy(stream.Dictionary, remap);
                    return new PdfStream(dictCopy, stream.RawBytes);
                }

            default:
                return primitive;
        }
    }

    private static PdfPrimitive RemapPrimitive(PdfPrimitive primitive, Dictionary<int, int> remap)
    {
        if (primitive is PdfReference reference)
        {
            return remap.TryGetValue(reference.ObjectId.ObjectNumber, out int mapped)
                ? new PdfReference(new PdfObjectId(mapped, 0))
                : PdfNull.Value;
        }
        return DeepCopy(primitive, remap);
    }

    // ── Object deduplication ──────────────────────────────────────────────

    // Merges byte-identical indirect objects. Objects are grouped by a canonical
    // content signature (dictionary key order is normalised, so order-only
    // differences still merge); each duplicate is repointed at the lowest-
    // numbered survivor and dropped. Repeated to a fixpoint so objects made
    // identical by an earlier merge also collapse. Returns the rebuilt trailer;
    // the object list is updated in place.
    private static PdfDictionary Deduplicate(
        List<PdfIndirectObject> objects, PdfDictionary trailer, out int removed)
    {
        removed = 0;
        bool changed = true;
        while (changed)
        {
            changed = false;

            Dictionary<string, int> survivorBySignature = new(StringComparer.Ordinal);
            Dictionary<int, int> duplicateToSurvivor = new();
            foreach (PdfIndirectObject obj in objects)
            {
                string signature = Signature(obj.Value);
                if (survivorBySignature.TryGetValue(signature, out int survivor))
                {
                    duplicateToSurvivor[obj.Id.ObjectNumber] = survivor;
                }
                else
                {
                    survivorBySignature[signature] = obj.Id.ObjectNumber;
                }
            }

            if (duplicateToSurvivor.Count == 0)
            {
                break;
            }

            changed = true;
            removed += duplicateToSurvivor.Count;

            // RemapPrimitive nulls references absent from the map, so every
            // surviving number must map to itself in addition to the merges.
            Dictionary<int, int> remap = new(objects.Count);
            foreach (PdfIndirectObject obj in objects)
            {
                int number = obj.Id.ObjectNumber;
                remap[number] = duplicateToSurvivor.TryGetValue(number, out int s) ? s : number;
            }

            List<PdfIndirectObject> survivors = new(objects.Count - duplicateToSurvivor.Count);
            foreach (PdfIndirectObject obj in objects)
            {
                if (duplicateToSurvivor.ContainsKey(obj.Id.ObjectNumber))
                {
                    continue;
                }
                survivors.Add(new PdfIndirectObject(obj.Id, RemapPrimitive(obj.Value, remap)));
            }

            objects.Clear();
            objects.AddRange(survivors);
            trailer = (PdfDictionary)RemapPrimitive(trailer, remap);
        }

        return trailer;
    }

    // Renumbers the surviving objects 1..n densely (closing gaps left by merged
    // duplicates) and rewrites all references and the trailer to match.
    private static PdfDictionary Densify(List<PdfIndirectObject> objects, PdfDictionary trailer)
    {
        Dictionary<int, int> remap = new(objects.Count);
        int next = 1;
        foreach (PdfIndirectObject obj in objects)
        {
            remap[obj.Id.ObjectNumber] = next;
            next++;
        }

        List<PdfIndirectObject> renumbered = new(objects.Count);
        foreach (PdfIndirectObject obj in objects)
        {
            PdfObjectId id = new(remap[obj.Id.ObjectNumber], 0);
            renumbered.Add(new PdfIndirectObject(id, RemapPrimitive(obj.Value, remap)));
        }

        objects.Clear();
        objects.AddRange(renumbered);
        return (PdfDictionary)RemapPrimitive(trailer, remap);
    }

    // Canonical content signature used to detect byte-identical objects. The
    // serialization is unambiguous (type tags plus length-prefixed payloads)
    // and order-normalised for dictionaries, then reduced to a SHA-256 digest.
    private static string Signature(PdfPrimitive primitive)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendCanonical(primitive, hash);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void AppendCanonical(PdfPrimitive primitive, IncrementalHash hash)
    {
        switch (primitive)
        {
            case PdfBoolean boolean:
                AppendTag(hash, boolean.Value ? (byte)'t' : (byte)'f');
                break;

            case PdfInteger integer:
                AppendTag(hash, (byte)'i');
                hash.AppendData(BitConverter.GetBytes((long)integer.Value));
                break;

            case PdfReal real:
                AppendTag(hash, (byte)'r');
                hash.AppendData(BitConverter.GetBytes(real.Value));
                break;

            case PdfName name:
                AppendTag(hash, (byte)'/');
                AppendBytes(hash, Encoding.UTF8.GetBytes(name.Value));
                break;

            case PdfString text:
                AppendTag(hash, (byte)'s');
                AppendBytes(hash, text.Bytes);
                break;

            case PdfReference reference:
                AppendTag(hash, (byte)'R');
                hash.AppendData(BitConverter.GetBytes(reference.ObjectNumber));
                hash.AppendData(BitConverter.GetBytes(reference.Generation));
                break;

            case PdfArray array:
                AppendTag(hash, (byte)'[');
                hash.AppendData(BitConverter.GetBytes(array.Count));
                for (int i = 0; i < array.Count; i++)
                {
                    AppendCanonical(array[i], hash);
                }
                break;

            case PdfDictionary dict:
                AppendDictionary(dict, hash);
                break;

            case PdfStream stream:
                AppendTag(hash, (byte)'S');
                AppendDictionary(stream.Dictionary, hash);
                AppendBytes(hash, stream.RawBytes);
                break;

            default:
                AppendTag(hash, (byte)'n');   // PdfNull and anything else
                break;
        }
    }

    private static void AppendDictionary(PdfDictionary dict, IncrementalHash hash)
    {
        AppendTag(hash, (byte)'<');

        List<PdfName> keys = new(dict.Keys);
        keys.Sort((x, y) => string.CompareOrdinal(x.Value, y.Value));

        hash.AppendData(BitConverter.GetBytes(keys.Count));
        foreach (PdfName key in keys)
        {
            AppendBytes(hash, Encoding.UTF8.GetBytes(key.Value));
            AppendCanonical(dict[key], hash);
        }
    }

    private static void AppendTag(IncrementalHash hash, byte tag)
    {
        hash.AppendData(new byte[] { tag });
    }

    private static void AppendBytes(IncrementalHash hash, byte[] data)
    {
        hash.AppendData(BitConverter.GetBytes(data.Length));
        hash.AppendData(data);
    }

    // ── Content-stream minification ───────────────────────────────────────

    private static readonly FilterPipeline ContentPipeline = FilterRegistry.CreateDefaultPipeline();

    // Collects the object numbers of page content streams by walking the page
    // tree from the catalog. Form XObjects are recognised separately by their
    // dictionary, so they are not collected here.
    private static HashSet<int> CollectContentStreamNumbers(PdfDocument document)
    {
        HashSet<int> contents = new();
        HashSet<int> visited = new();
        PdfDictionary catalog = document.Catalog;
        if (catalog.TryGetValue(PdfName.Pages, out PdfPrimitive? pages))
        {
            WalkPageTree(pages, document.Objects, contents, visited);
        }
        return contents;
    }

    private static void WalkPageTree(
        PdfPrimitive node, PdfObjectStore resolver, HashSet<int> contents, HashSet<int> visited)
    {
        if (node is PdfReference nodeRef && !visited.Add(nodeRef.ObjectNumber))
        {
            return;
        }

        PdfPrimitive resolved = resolver.Resolve(node);
        if (resolved is not PdfDictionary dict)
        {
            return;
        }

        PdfName? type = dict.GetAs<PdfName>(PdfName.Type);
        if (type is not null && string.Equals(type.Value, "Pages", StringComparison.Ordinal))
        {
            if (dict.TryGetValue(PdfName.Kids, out PdfPrimitive? kidsValue) &&
                resolver.Resolve(kidsValue) is PdfArray kids)
            {
                for (int i = 0; i < kids.Count; i++)
                {
                    WalkPageTree(kids[i], resolver, contents, visited);
                }
            }
        }
        else if (dict.TryGetValue(PdfName.Contents, out PdfPrimitive? contentsValue))
        {
            AddContentTargets(contentsValue, resolver, contents);
        }
    }

    private static void AddContentTargets(
        PdfPrimitive contentsValue, PdfObjectStore resolver, HashSet<int> contents)
    {
        if (contentsValue is PdfReference contentRef)
        {
            if (resolver.Resolve(contentRef) is PdfArray array)
            {
                for (int i = 0; i < array.Count; i++)
                {
                    if (array[i] is PdfReference element)
                    {
                        contents.Add(element.ObjectNumber);
                    }
                }
            }
            else
            {
                contents.Add(contentRef.ObjectNumber);
            }
        }
        else if (contentsValue is PdfArray inlineArray)
        {
            for (int i = 0; i < inlineArray.Count; i++)
            {
                if (inlineArray[i] is PdfReference element)
                {
                    contents.Add(element.ObjectNumber);
                }
            }
        }
    }

    private static bool IsFormXObject(PdfDictionary dict)
    {
        PdfName? type = dict.GetAs<PdfName>(PdfName.Type);
        PdfName? subtype = dict.GetAs<PdfName>(PdfName.Subtype);
        return type is not null && string.Equals(type.Value, "XObject", StringComparison.Ordinal)
            && subtype is not null && string.Equals(subtype.Value, "Form", StringComparison.Ordinal);
    }

    // Decodes, minifies, and re-Flate-encodes a content stream. Returns null when
    // the stream carries decode parameters, cannot be decoded, is not safely
    // minifiable, or does not shrink. The dictionary mutated here is the freshly
    // copied one, so the source document is untouched.
    private static PdfStream? TryMinifyContentStream(PdfStream stream, CompressionOptions options)
    {
        if (stream.Dictionary.ContainsKey(PdfName.Intern("DecodeParms")) ||
            stream.Dictionary.ContainsKey(PdfName.Intern("DP")))
        {
            return null;
        }

        byte[]? decoded = DecodeContentStream(stream);
        if (decoded is null)
        {
            return null;
        }

        byte[]? minified = ContentStreamMinifier.Minify(decoded);
        if (minified is null)
        {
            return null;
        }

        byte[] encoded;
        try
        {
            using MemoryStream input = new(minified);
            using MemoryStream packed = new();
            (options.MaxDeflateStreams ? DeflateMax : Deflate).Encode(input, packed);
            encoded = packed.ToArray();
        }
        catch (FilterException)
        {
            return null;
        }

        if (encoded.Length >= stream.RawBytes.Length)
        {
            return null;
        }

        PdfDictionary dict = stream.Dictionary;
        dict.Set(PdfName.Filter, PdfName.FlateDecode);
        dict.Remove(PdfName.Intern("DecodeParms"));
        dict.Remove(PdfName.Intern("DP"));
        return new PdfStream(dict, encoded);
    }

    private static byte[]? DecodeContentStream(PdfStream stream)
    {
        try
        {
            if (!stream.IsFiltered)
            {
                return stream.RawBytes;
            }

            PdfPrimitive? filter = stream.Filter;
            if (filter is PdfName name)
            {
                return ContentPipeline.Decode(FilterRegistry.ResolveAlias(name.Value), stream.RawBytes, null);
            }

            if (filter is PdfArray array)
            {
                byte[] data = stream.RawBytes;
                for (int i = 0; i < array.Count; i++)
                {
                    PdfName? element = array.GetAs<PdfName>(i);
                    if (element is null)
                    {
                        return null;
                    }
                    data = ContentPipeline.Decode(FilterRegistry.ResolveAlias(element.Value), data, null);
                }
                return data;
            }

            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ── Reachability over an in-memory object set ─────────────────────────

    // Drops objects no longer reachable from the trailer (e.g. a stripped
    // /Metadata stream and anything only it referenced).
    private static void GarbageCollect(List<PdfIndirectObject> objects, PdfDictionary trailer)
    {
        Dictionary<int, PdfPrimitive> byNumber = new(objects.Count);
        foreach (PdfIndirectObject obj in objects)
        {
            byNumber[obj.Id.ObjectNumber] = obj.Value;
        }

        HashSet<int> reached = new();
        foreach (PdfPrimitive value in trailer.Values)
        {
            Mark(value, byNumber, reached);
        }

        objects.RemoveAll(obj => !reached.Contains(obj.Id.ObjectNumber));
    }

    private static void Mark(PdfPrimitive primitive, Dictionary<int, PdfPrimitive> byNumber, HashSet<int> reached)
    {
        switch (primitive)
        {
            case PdfReference reference:
                if (reached.Add(reference.ObjectNumber) &&
                    byNumber.TryGetValue(reference.ObjectNumber, out PdfPrimitive? target))
                {
                    Mark(target, byNumber, reached);
                }
                break;

            case PdfDictionary dict:
                foreach (PdfPrimitive value in dict.Values)
                {
                    Mark(value, byNumber, reached);
                }
                break;

            case PdfArray array:
                for (int i = 0; i < array.Count; i++)
                {
                    Mark(array[i], byNumber, reached);
                }
                break;

            case PdfStream stream:
                foreach (PdfPrimitive value in stream.Dictionary.Values)
                {
                    Mark(value, byNumber, reached);
                }
                break;

            default:
                break;
        }
    }

    // ── Stripping ─────────────────────────────────────────────────────────

    // True when any opt-in strip flag is set and a strip pass is therefore
    // required. RemoveDocumentInfo is excluded: the Info dictionary is dropped
    // by omitting it from the reachability roots, not by a strip pass.
    private static bool StripRequested(CompressionOptions options) =>
        options.RemoveMetadata
        || options.RemoveJavaScript
        || options.RemoveAttachments
        || options.RemoveThumbnails
        || options.RemovePieceInfo
        || options.RemoveStructTree
        || options.RemoveAnnotations;

    // Removes the requested catalog- and page-level entries. The objects thereby
    // made unreachable (XMP streams, JavaScript, embedded files, thumbnails,
    // structure elements, annotations) are dropped by the garbage-collect pass
    // that follows in Compress.
    private static void StripCategories(
        List<PdfIndirectObject> objects, PdfDictionary trailer, CompressionOptions options)
    {
        Dictionary<int, PdfPrimitive> byNumber = new(objects.Count);
        foreach (PdfIndirectObject obj in objects)
        {
            byNumber[obj.Id.ObjectNumber] = obj.Value;
        }

        PdfDictionary? catalog = FindCatalog(objects, trailer);
        if (catalog is null)
        {
            return;
        }

        // Catalog-level removals.
        if (options.RemoveMetadata)
        {
            catalog.Remove(PdfName.Intern("Metadata"));
        }
        if (options.RemoveStructTree)
        {
            catalog.Remove(PdfName.Intern("StructTreeRoot"));
            catalog.Remove(PdfName.Intern("MarkInfo"));
        }
        if (options.RemovePieceInfo)
        {
            catalog.Remove(PdfName.Intern("PieceInfo"));
        }
        if (options.RemoveJavaScript)
        {
            StripJavaScript(catalog, byNumber);
        }
        if (options.RemoveAttachments)
        {
            StripAttachments(catalog, byNumber);
        }

        // Page-level removals.
        bool perPage = options.RemoveThumbnails
            || options.RemovePieceInfo
            || options.RemoveAnnotations
            || options.RemoveAttachments;
        if (!perPage)
        {
            return;
        }

        foreach (PdfDictionary page in EnumeratePageDicts(catalog, byNumber))
        {
            if (options.RemoveThumbnails)
            {
                page.Remove(PdfName.Intern("Thumb"));
            }
            if (options.RemovePieceInfo)
            {
                page.Remove(PdfName.Intern("PieceInfo"));
            }
            if (options.RemoveAnnotations)
            {
                page.Remove(PdfName.Intern("Annots"));
            }
            else if (options.RemoveAttachments)
            {
                DropFileAttachmentAnnots(page, byNumber);
            }
        }
    }

    // Locates the catalog dictionary in the rewritten object set via the
    // trailer /Root reference.
    private static PdfDictionary? FindCatalog(List<PdfIndirectObject> objects, PdfDictionary trailer)
    {
        if (!trailer.TryGetValue(PdfName.Root, out PdfPrimitive? rootValue) ||
            rootValue is not PdfReference rootRef)
        {
            return null;
        }

        foreach (PdfIndirectObject obj in objects)
        {
            if (obj.Id.ObjectNumber == rootRef.ObjectNumber && obj.Value is PdfDictionary catalog)
            {
                return catalog;
            }
        }

        return null;
    }

    // Resolves a single indirect reference against the rewritten object set;
    // returns the primitive unchanged when it is direct or the target is absent.
    private static PdfPrimitive? ResolveLocal(PdfPrimitive? primitive, Dictionary<int, PdfPrimitive> byNumber)
    {
        if (primitive is PdfReference reference &&
            byNumber.TryGetValue(reference.ObjectNumber, out PdfPrimitive? target))
        {
            return target;
        }

        return primitive;
    }

    // Fetches dictionary entry <paramref name="key"/> with one level of
    // reference resolution, or null when absent.
    private static PdfPrimitive? GetResolved(
        PdfDictionary dict, PdfName key, Dictionary<int, PdfPrimitive> byNumber)
    {
        return dict.TryGetValue(key, out PdfPrimitive? value)
            ? ResolveLocal(value, byNumber)
            : null;
    }

    // Walks the page tree from the catalog /Pages node, yielding each leaf page
    // dictionary. Internal nodes (those with /Kids) are descended; a visited set
    // guards against malformed cyclic trees.
    private static IEnumerable<PdfDictionary> EnumeratePageDicts(
        PdfDictionary catalog, Dictionary<int, PdfPrimitive> byNumber)
    {
        if (GetResolved(catalog, PdfName.Pages, byNumber) is not PdfDictionary pages)
        {
            yield break;
        }

        Stack<PdfDictionary> stack = new();
        stack.Push(pages);
        HashSet<PdfDictionary> seen = new();
        while (stack.Count > 0)
        {
            PdfDictionary node = stack.Pop();
            if (!seen.Add(node))
            {
                continue;
            }

            if (node.TryGetValue(PdfName.Kids, out PdfPrimitive? kidsValue) &&
                ResolveLocal(kidsValue, byNumber) is PdfArray kids)
            {
                for (int i = kids.Count - 1; i >= 0; i--)
                {
                    if (ResolveLocal(kids[i], byNumber) is PdfDictionary kid)
                    {
                        stack.Push(kid);
                    }
                }
            }
            else
            {
                yield return node;
            }
        }
    }

    // Removes document-level JavaScript: the /Names /JavaScript name tree, an
    // /OpenAction that runs a script, and the document /AA additional-actions.
    private static void StripJavaScript(PdfDictionary catalog, Dictionary<int, PdfPrimitive> byNumber)
    {
        if (GetResolved(catalog, PdfName.Intern("Names"), byNumber) is PdfDictionary names)
        {
            names.Remove(PdfName.Intern("JavaScript"));
        }

        if (GetResolved(catalog, PdfName.Intern("OpenAction"), byNumber) is PdfDictionary action &&
            GetResolved(action, PdfName.Intern("S"), byNumber) is PdfName verb &&
            verb.Value == "JavaScript")
        {
            catalog.Remove(PdfName.Intern("OpenAction"));
        }

        catalog.Remove(PdfName.Intern("AA"));
    }

    // Removes embedded file attachments: the /Names /EmbeddedFiles name tree and
    // the catalog /AF associated files. File-attachment annotations that point at
    // those streams are dropped per page in DropFileAttachmentAnnots, so the
    // embedded streams become unreachable and are swept.
    private static void StripAttachments(PdfDictionary catalog, Dictionary<int, PdfPrimitive> byNumber)
    {
        if (GetResolved(catalog, PdfName.Intern("Names"), byNumber) is PdfDictionary names)
        {
            names.Remove(PdfName.Intern("EmbeddedFiles"));
        }

        catalog.Remove(PdfName.Intern("AF"));
    }

    // Rewrites a page's /Annots array to exclude file-attachment annotations,
    // used when attachments are stripped but other annotations are kept.
    private static void DropFileAttachmentAnnots(PdfDictionary page, Dictionary<int, PdfPrimitive> byNumber)
    {
        if (GetResolved(page, PdfName.Intern("Annots"), byNumber) is not PdfArray annots)
        {
            return;
        }

        List<PdfPrimitive> kept = new(annots.Count);
        for (int i = 0; i < annots.Count; i++)
        {
            PdfPrimitive entry = annots[i];
            if (ResolveLocal(entry, byNumber) is PdfDictionary annot &&
                GetResolved(annot, PdfName.Intern("Subtype"), byNumber) is PdfName sub &&
                sub.Value == "FileAttachment")
            {
                continue;
            }

            kept.Add(entry);
        }

        if (kept.Count == annots.Count)
        {
            return;
        }

        if (kept.Count == 0)
        {
            page.Remove(PdfName.Intern("Annots"));
        }
        else
        {
            page.Set(PdfName.Intern("Annots"), new PdfArray(kept.ToArray()));
        }
    }

    // ── Stream reduction ──────────────────────────────────────────────────

    private static readonly DeflateFilter Deflate = new();
    private static readonly DeflateFilter DeflateMax = new(Chuvadi.Pdf.Filters.DeflateEffort.Maximum);

    // Flate-compresses an unfiltered stream when it shrinks. Returns null
    // when the stream is already filtered, too small, or grows.
    private static PdfStream? TryFlateRawStream(PdfStream stream, CompressionOptions options)
    {
        if (stream.IsFiltered || stream.RawBytes.Length < options.MinStreamLengthToCompress)
        {
            return null;
        }

        byte[] compressed;
        try
        {
            using MemoryStream input = new(stream.RawBytes);
            using MemoryStream packed = new();
            (options.MaxDeflateStreams ? DeflateMax : Deflate).Encode(input, packed);
            compressed = packed.ToArray();
        }
        catch (FilterException)
        {
            return null;
        }

        if (compressed.Length >= stream.RawBytes.Length)
        {
            return null;
        }

        PdfDictionary dict = (PdfDictionary)DeepCopy(stream.Dictionary, EmptyRemap);
        dict.Set(PdfName.Intern("Filter"), PdfName.Intern("FlateDecode"));
        return new PdfStream(dict, compressed);
    }

    private static readonly Dictionary<int, int> EmptyRemap = new();

    // Re-encodes an eligible 8-bit RGB/grayscale image as baseline JPEG.
    // Returns null when the image isn't eligible or JPEG doesn't shrink it.
    private static PdfStream? TryRecompressImage(
        PdfStream stream, PdfObjectStore resolver, CompressionOptions options)
    {
        PdfDictionary dict = stream.Dictionary;

        if (!IsName(dict, resolver, "Subtype", "Image") ||
            ReadBool(dict, resolver, "ImageMask") ||
            dict.ContainsKey(PdfName.Intern("SMask")) ||
            ReadInt(dict, resolver, "BitsPerComponent") != 8)
        {
            return null;
        }

        int width = ReadInt(dict, resolver, "Width");
        int height = ReadInt(dict, resolver, "Height");
        if (width <= 0 || height <= 0 ||
            (long)width * height < options.MinImagePixelsToRecompress)
        {
            return null;
        }

        int components = ComponentCount(dict, resolver);
        if (components is not 1 and not 3)
        {
            return null;
        }

        // Only raw or single-Flate sample storage qualifies; everything else
        // (DCT, CCITT, filter chains) is already compressed or out of scope.
        byte[] samples;
        if (!stream.IsFiltered)
        {
            samples = stream.RawBytes;
        }
        else if (stream.Filter is PdfName f &&
                 FilterRegistry.ResolveAlias(f.Value) == "FlateDecode")
        {
            try
            {
                PdfPrimitive? parms = dict.TryGetValue(
                    PdfName.Intern("DecodeParms"), out PdfPrimitive? p) ? p : null;
                using MemoryStream input = new(stream.RawBytes);
                using MemoryStream unpacked = new();
                Deflate.Decode(input, unpacked, FilterParameters.FromDictionary(parms));
                samples = unpacked.ToArray();
            }
            catch (FilterException)
            {
                return null;
            }
        }
        else
        {
            return null;
        }

        if (samples.Length < width * height * components)
        {
            return null;
        }

        ImageFrame frame = ImageFrame.Create(
            width, height, components == 1 ? ImageColorFormat.Gray8 : ImageColorFormat.Rgb24);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (components == 1)
                {
                    byte v = samples[(y * width) + x];
                    frame.Pixels.SetPixelBgra(x, y, v, v, v, 255);
                }
                else
                {
                    int si = ((y * width) + x) * 3;
                    frame.Pixels.SetPixelBgra(
                        x, y, samples[si + 2], samples[si + 1], samples[si], 255);
                }
            }
        }

        byte[] jpeg;
        try
        {
            using MemoryStream encoded = new();
            JpegEncoder.Encode(frame, encoded, options.JpegQuality);
            jpeg = encoded.ToArray();
        }
        catch (ImageException)
        {
            return null;
        }

        if (jpeg.Length >= stream.RawBytes.Length)
        {
            return null;
        }

        PdfDictionary newDict = (PdfDictionary)DeepCopy(dict, EmptyRemap);
        newDict.Set(PdfName.Intern("Filter"), PdfName.Intern("DCTDecode"));
        newDict.Remove(PdfName.Intern("DecodeParms"));
        newDict.Remove(PdfName.Intern("Decode"));
        newDict.Set(
            PdfName.Intern("ColorSpace"),
            PdfName.Intern(components == 1 ? "DeviceGray" : "DeviceRGB"));
        return new PdfStream(newDict, jpeg);
    }

    // ── Dictionary helpers ────────────────────────────────────────────────

    private static int ComponentCount(PdfDictionary dict, PdfObjectStore resolver)
    {
        if (!dict.TryGetValue(PdfName.Intern("ColorSpace"), out PdfPrimitive? csRef))
        {
            return 0;
        }

        PdfPrimitive cs = resolver.Resolve(csRef);
        if (cs is PdfName name)
        {
            return name.Value switch
            {
                "DeviceGray" or "CalGray" => 1,
                "DeviceRGB" or "CalRGB" => 3,
                _ => 0,
            };
        }

        if (cs is PdfArray array && array.Count >= 2 &&
            array[0] is PdfName family && family.Value == "ICCBased" &&
            resolver.Resolve(array[1]) is PdfStream icc &&
            icc.Dictionary.TryGetValue(PdfName.Intern("N"), out PdfPrimitive? n) &&
            n is PdfInteger count)
        {
            return count.Value;
        }

        return 0;
    }

    private static bool IsName(
        PdfDictionary dict, PdfObjectStore resolver, string key, string expected)
    {
        return dict.TryGetValue(PdfName.Intern(key), out PdfPrimitive? value) &&
               resolver.Resolve(value) is PdfName name &&
               name.Value == expected;
    }

    private static int ReadInt(PdfDictionary dict, PdfObjectStore resolver, string key)
    {
        return dict.TryGetValue(PdfName.Intern(key), out PdfPrimitive? value) &&
               resolver.Resolve(value) is PdfInteger i
            ? i.Value
            : 0;
    }

    private static bool ReadBool(PdfDictionary dict, PdfObjectStore resolver, string key)
    {
        return dict.TryGetValue(PdfName.Intern(key), out PdfPrimitive? value) &&
               resolver.Resolve(value) is PdfBoolean b &&
               b.Value;
    }
}
