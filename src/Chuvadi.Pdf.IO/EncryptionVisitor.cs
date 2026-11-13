// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.6.2 — Per-object key derivation; recursive
//        application to every string and stream inside the indirect object.
// PHASE: Phase 1.1.5 (integration) — Chuvadi.Pdf.IO
//
// Walks an indirect object's value tree, applying a transform (decrypt or
// encrypt) to every PdfString and PdfStream encountered. Used by both the
// reader (decryption) and the writer (encryption). All transforms are keyed
// by the containing indirect object's object-number/generation.

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.IO;

/// <summary>
/// Recursively transforms strings and streams inside an indirect object's value.
/// </summary>
/// <remarks>
/// PDF encryption applies per-object: every <see cref="PdfString"/> and
/// <see cref="PdfStream"/> embedded inside an indirect object is encrypted with
/// a key derived from the object's number and generation. Dictionaries and
/// arrays themselves are not transformed — only the leaf strings and streams.
/// </remarks>
internal static class EncryptionVisitor
{
    /// <summary>
    /// Transforms an indirect object's value, returning a new PdfPrimitive with
    /// every string/stream replaced by <paramref name="transformBytes"/>(bytes).
    /// </summary>
    /// <param name="value">Root primitive (typically a dictionary or stream).</param>
    /// <param name="objectNumber">The containing indirect object's number.</param>
    /// <param name="generation">The containing indirect object's generation.</param>
    /// <param name="transformBytes">
    /// Function applied to every PdfString and PdfStream payload. Receives the
    /// raw bytes, the object number, and the generation. Returns the transformed
    /// bytes.
    /// </param>
    /// <param name="skipMetadataEncryption">
    /// When true, do not transform a stream whose dictionary /Type is /Metadata.
    /// Matches the /EncryptMetadata=false convention.
    /// </param>
    public static PdfPrimitive Transform(
        PdfPrimitive value,
        int objectNumber,
        int generation,
        Func<byte[], int, int, byte[]> transformBytes,
        bool skipMetadataEncryption = false)
    {
        if (value is PdfString s)
        {
            byte[] transformed = transformBytes(s.Bytes, objectNumber, generation);
            return new PdfString(transformed, s.PreferHexForm);
        }

        if (value is PdfStream stream)
        {
            // /Metadata streams are skipped when /EncryptMetadata is false.
            if (skipMetadataEncryption && IsMetadataStream(stream.Dictionary))
            {
                // Still transform the dictionary's string contents (very rare but spec-permitted).
                PdfDictionary newDict = (PdfDictionary)Transform(
                    stream.Dictionary, objectNumber, generation, transformBytes, skipMetadataEncryption);
                return new PdfStream(newDict, stream.RawBytes);
            }

            PdfDictionary streamDict = (PdfDictionary)Transform(
                stream.Dictionary, objectNumber, generation, transformBytes, skipMetadataEncryption);
            byte[] transformedBytes = transformBytes(stream.RawBytes, objectNumber, generation);
            return new PdfStream(streamDict, transformedBytes);
        }

        if (value is PdfDictionary dict)
        {
            PdfDictionary copy = new PdfDictionary();
            foreach (KeyValuePair<PdfName, PdfPrimitive> kvp in dict)
            {
                copy.Set(kvp.Key, Transform(
                    kvp.Value, objectNumber, generation, transformBytes, skipMetadataEncryption));
            }
            return copy;
        }

        if (value is PdfArray arr)
        {
            List<PdfPrimitive> items = new List<PdfPrimitive>(arr.Count);
            for (int i = 0; i < arr.Count; i++)
            {
                items.Add(Transform(
                    arr[i], objectNumber, generation, transformBytes, skipMetadataEncryption));
            }
            return new PdfArray(items);
        }

        // Numbers, names, booleans, null, references — unchanged.
        return value;
    }

    /// <summary>
    /// 3.34.0 (#52d): as <see cref="Transform(PdfPrimitive, int, int, Func{byte[], int, int, byte[]}, bool)"/>,
    /// with separate transforms for strings and for streams, so the /StrF,
    /// /StmF and /EFF crypt filters and a stream's own /Crypt filter can
    /// differ. The stream transform receives the stream's dictionary (its
    /// strings already transformed) and raw data, and returns the stream.
    /// </summary>
    public static PdfPrimitive Transform(
        PdfPrimitive value,
        int objectNumber,
        int generation,
        Func<byte[], int, int, byte[]> transformString,
        Func<PdfDictionary, byte[], int, int, PdfStream> transformStream,
        bool skipMetadataEncryption)
    {
        if (value is PdfString s)
        {
            return new PdfString(transformString(s.Bytes, objectNumber, generation), s.PreferHexForm);
        }

        if (value is PdfStream stream)
        {
            PdfDictionary streamDict = (PdfDictionary)Transform(
                stream.Dictionary, objectNumber, generation, transformString, transformStream, skipMetadataEncryption);
            if (skipMetadataEncryption && IsMetadataStream(stream.Dictionary))
            {
                return new PdfStream(streamDict, stream.RawBytes);
            }

            return transformStream(streamDict, stream.RawBytes, objectNumber, generation);
        }

        if (value is PdfDictionary dict)
        {
            PdfDictionary copy = new PdfDictionary();
            foreach (KeyValuePair<PdfName, PdfPrimitive> kvp in dict)
            {
                copy.Set(kvp.Key, Transform(
                    kvp.Value, objectNumber, generation, transformString, transformStream, skipMetadataEncryption));
            }

            return copy;
        }

        if (value is PdfArray arr)
        {
            List<PdfPrimitive> items = new List<PdfPrimitive>(arr.Count);
            for (int i = 0; i < arr.Count; i++)
            {
                items.Add(Transform(
                    arr[i], objectNumber, generation, transformString, transformStream, skipMetadataEncryption));
            }

            return new PdfArray(items);
        }

        return value;
    }

    private static bool IsMetadataStream(PdfDictionary dict)
    {
        PdfName? type = dict.GetName(PdfName.Type);
        return type is not null && type.Value == "Metadata";
    }
}
