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
/// Object streams and cross-reference streams are a recorded follow-up
/// (the writer currently emits classic cross-reference tables).
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

        // ── 1. Reachability from the trailer ─────────────────────────────
        Dictionary<int, PdfPrimitive> reachable = new();
        foreach (PdfName key in RootKeys)
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
        List<PdfIndirectObject> objects = new(reachable.Count);

        foreach (KeyValuePair<int, PdfPrimitive> entry in reachable)
        {
            PdfPrimitive copy = DeepCopy(entry.Value, remap);

            if (copy is PdfStream stream)
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

            objects.Add(new PdfIndirectObject(new PdfObjectId(remap[entry.Key], 0), copy));
        }

        // ── 4. New trailer ────────────────────────────────────────────────
        PdfDictionary trailer = new();
        foreach (PdfName key in RootKeys)
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

        PdfWriter.Write(output, objects, trailer);

        return new CompressionResult
        {
            ObjectsRemoved = Math.Max(0, totalObjects - reachable.Count),
            StreamsCompressed = streamsCompressed,
            ImagesRecompressed = imagesRecompressed,
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

    // ── Stream reduction ──────────────────────────────────────────────────

    private static readonly DeflateFilter Deflate = new();

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
            Deflate.Encode(input, packed);
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
