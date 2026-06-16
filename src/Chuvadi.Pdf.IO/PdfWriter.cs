// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.5 — File structure
// PHASE: Phase 1 — Chuvadi.Pdf.IO
// Writes PDF files in full-rewrite mode with classic xref tables.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Encryption;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.IO;

/// <summary>
/// Writes a complete PDF file to an output stream.
/// </summary>
/// <remarks>
/// <see cref="PdfWriter"/> performs a full rewrite — it serialises all
/// provided indirect objects, builds a fresh cross-reference table, and
/// writes a valid PDF trailer.
///
/// Streams are written with their existing raw bytes unchanged.
/// The <c>/Length</c> entry is updated to reflect the actual byte count.
///
/// PDF version written: <c>%PDF-1.7</c>.
/// xref format: classic cross-reference table (not a cross-reference stream).
///
/// PDF 32000-1:2008 §7.5 — File structure.
/// </remarks>
public static class PdfWriter
{
    private static readonly byte[] PdfHeader =
        Encoding.ASCII.GetBytes("%PDF-1.7\n%\xE2\xE3\xCF\xD3\n");

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Writes a complete PDF file containing the given indirect objects.
    /// </summary>
    /// <param name="output">The stream to write to. Must be writable.</param>
    /// <param name="objects">
    /// The indirect objects to include. Object 0 (the free list head) is
    /// added automatically and must not be included by the caller.
    /// </param>
    /// <param name="trailer">
    /// The trailer dictionary. <c>/Size</c> is computed automatically.
    /// <c>/Root</c> must be set by the caller.
    /// </param>
    public static void Write(
        Stream output,
        IEnumerable<PdfIndirectObject> objects,
        PdfDictionary trailer)
    {
        Write(output, objects, trailer, encryption: null);
    }

    /// <summary>
    /// Writes a linearized (Fast Web View) PDF.
    /// </summary>
    /// <param name="output">Writable output stream.</param>
    /// <param name="objects">Indirect objects to write.</param>
    /// <param name="trailer">Trailer dictionary with /Root.</param>
    /// <remarks>
    /// Per ISO 32000-1 Annex F, the output is laid out so that the first page
    /// can be rendered after reading only the file's prefix. Encryption is not
    /// yet supported in combination with linearization.
    /// </remarks>
    public static void WriteLinearized(
        Stream output,
        IEnumerable<PdfIndirectObject> objects,
        PdfDictionary trailer)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(trailer);

        List<PdfIndirectObject> list = new List<PdfIndirectObject>(objects);
        LinearizedWriter.Write(output, list, trailer);
    }

    /// <summary>
    /// Writes a PDF with optional encryption applied to every string and stream
    /// inside the written objects.
    /// </summary>
    /// <param name="output">Writable, seekable output stream.</param>
    /// <param name="objects">Indirect objects to write.</param>
    /// <param name="trailer">
    /// Trailer dictionary. When <paramref name="encryption"/> is supplied, this
    /// method appends an /Encrypt entry referencing a newly created encryption
    /// dictionary; the trailer must NOT already contain one.
    /// </param>
    /// <param name="encryption">
    /// Encryption configuration. When null, no encryption is applied.
    /// </param>
    /// <param name="synthesized">
    /// Which absent document-level metadata to synthesise. Defaults to
    /// <see cref="SynthesizedMetadata.All"/>, matching the standard behaviour;
    /// reduce it to suppress synthesis of /Info and/or /Metadata.
    /// </param>
    public static void Write(
        Stream output,
        IEnumerable<PdfIndirectObject> objects,
        PdfDictionary trailer,
        EncryptionOptions? encryption,
        SynthesizedMetadata synthesized = SynthesizedMetadata.All)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(trailer);

        // Write PDF header.
        output.Write(PdfHeader, 0, PdfHeader.Length);

        // Build sorted object list.
        List<PdfIndirectObject> sortedObjects = new List<PdfIndirectObject>(objects);
        sortedObjects.Sort((a, b) => a.Id.ObjectNumber.CompareTo(b.Id.ObjectNumber));

        int maxObjectNumber = 0;
        foreach (PdfIndirectObject obj in sortedObjects)
        {
            if (obj.Id.ObjectNumber > maxObjectNumber)
            {
                maxObjectNumber = obj.Id.ObjectNumber;
            }
        }

        // Ensure the trailer carries a file identifier (/ID, ISO 32000-1 §14.4).
        // Without it some viewers (e.g. Adobe) synthesise one on open and then
        // prompt to save on close even after only viewing. The value is derived
        // from the document content, so the output is deterministic and any
        // caller-supplied /ID is preserved.
        byte[] fileId = GetOrCreateFileId(trailer, sortedObjects);

        // Document information (/Info) and XMP metadata (/Metadata on the
        // catalog). Deterministic — a fixed Producer plus identifiers derived
        // from the file id, no timestamps — so identical input stays
        // byte-identical. Any caller-supplied /Info or /Metadata is preserved.
        maxObjectNumber = AddDocumentMetadata(sortedObjects, trailer, fileId, maxObjectNumber, synthesized);

        // If encrypting, create the /Encrypt indirect object and append to objects.
        int encryptObjectNumber = -1;
        Encryptor? encryptor = null;
        bool encryptMetadata = true;

        if (encryption is not null)
        {
            encryptObjectNumber = maxObjectNumber + 1;
            PdfObjectId encryptId = new PdfObjectId(encryptObjectNumber, 0);

            PdfDictionary encryptDict = EncryptionDictionaryBuilder.Build(
                encryption, GetOrCreateFileId(trailer, sortedObjects));

            sortedObjects.Add(new PdfIndirectObject(encryptId, encryptDict));
            maxObjectNumber = encryptObjectNumber;

            trailer.Set(PdfName.Intern("Encrypt"), new PdfReference(encryptId));
            encryptor = new Encryptor(encryption.FileKey, encryption.Algorithm);
            encryptMetadata = encryption.EncryptMetadata;
        }

        XrefTable xref = new XrefTable();

        foreach (PdfIndirectObject obj in sortedObjects)
        {
            long offset = output.Position;
            PdfIndirectObject toWrite = obj;

            // Encrypt every object EXCEPT the /Encrypt dictionary itself.
            if (encryptor is not null && obj.Id.ObjectNumber != encryptObjectNumber)
            {
                PdfPrimitive encryptedValue = EncryptionVisitor.Transform(
                    obj.Value,
                    obj.Id.ObjectNumber,
                    obj.Id.Generation,
                    encryptor.Encrypt,
                    skipMetadataEncryption: !encryptMetadata);
                toWrite = new PdfIndirectObject(obj.Id, encryptedValue);
            }

            WriteIndirectObject(output, toWrite);
            xref.Set(new XrefEntry(obj.Id.ObjectNumber, obj.Id.Generation, offset));
        }

        // Xref + trailer + EOF.
        long xrefOffset = xref.Write(output);
        int size = maxObjectNumber + 1;
        trailer.Set(PdfName.Size, size);

        byte[] trailerLine = Encoding.ASCII.GetBytes("trailer\n");
        output.Write(trailerLine, 0, trailerLine.Length);
        WriteValue(output, trailer);
        output.WriteByte((byte)'\n');

        string startxref = $"\nstartxref\n{xrefOffset}\n%%EOF\n";
        byte[] startxrefBytes = Encoding.ASCII.GetBytes(startxref);
        output.Write(startxrefBytes, 0, startxrefBytes.Length);
    }

    /// <summary>
    /// Appends an incremental update section to an existing PDF, returning the
    /// resulting bytes. The original bytes are preserved verbatim, so any
    /// existing signatures on the source document remain valid.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ISO 32000-1 §7.5.6 / ISO 32000-2 §7.5.6. The update section consists of:
    /// </para>
    /// <list type="number">
    ///   <item>The <paramref name="updatedObjects"/>, written in declaration
    ///   order with their existing object numbers. New objects use IDs at or
    ///   above the source's <c>/Size</c>; modifications reuse the original ID
    ///   and bump the generation if appropriate.</item>
    ///   <item>An xref table containing entries only for the objects in this
    ///   update (the obj-0 free entry stays in the original xref section
    ///   reached via the new trailer's <c>/Prev</c>).</item>
    ///   <item>A trailer dictionary copying <c>/Root</c> and <c>/ID</c> from
    ///   the original trailer, with <c>/Size</c> auto-computed and
    ///   <c>/Prev</c> set to the original startxref offset. Any entries
    ///   supplied in <paramref name="trailerOverlay"/> are merged on top.</item>
    ///   <item><c>startxref</c> pointing at this update's xref offset, then
    ///   <c>%%EOF</c>.</item>
    /// </list>
    /// <para>
    /// The catalog cannot be replaced via this method (its identity is
    /// established by <c>/Root</c> in the original trailer); to change the
    /// catalog's contents, include a new <see cref="PdfIndirectObject"/> for
    /// the catalog's object ID in <paramref name="updatedObjects"/>.
    /// </para>
    /// </remarks>
    /// <param name="originalPdfBytes">The unmodified source PDF.</param>
    /// <param name="updatedObjects">Objects to add or modify. Each carries its
    /// own ID; reusing an existing ID replaces that object in the document.</param>
    /// <param name="trailerOverlay">Optional trailer entries to add or override
    /// in the new trailer (e.g. updating <c>/Info</c> for modification metadata).
    /// <c>/Size</c> and <c>/Prev</c> are always controlled by the writer.</param>
    public static byte[] WriteIncrementalUpdate(
        byte[] originalPdfBytes,
        IEnumerable<PdfIndirectObject> updatedObjects,
        PdfDictionary? trailerOverlay = null)
    {
        ArgumentNullException.ThrowIfNull(originalPdfBytes);
        ArgumentNullException.ThrowIfNull(updatedObjects);

        // Parse the source so we know its trailer (for /Root, /ID, /Size) and the
        // startxref offset to chain via /Prev.
        long priorStartXref;
        PdfDictionary priorTrailer;
        using (MemoryStream srcStream = new(originalPdfBytes, writable: false))
        using (PdfReader reader = PdfReader.Open(srcStream, leaveOpen: false))
        {
            priorTrailer = reader.Trailer;
            priorStartXref = FindStartXrefInBytes(originalPdfBytes);
        }

        List<PdfIndirectObject> updates = new(updatedObjects);

        MemoryStream output = new();
        // Copy the original verbatim — DO NOT modify any byte.
        output.Write(originalPdfBytes, 0, originalPdfBytes.Length);

        // ISO 32000 says the new section starts on a new line. If the original
        // ends with %%EOF and a newline, fine; otherwise add one. Detect by
        // looking at the last byte.
        if (originalPdfBytes.Length == 0 || originalPdfBytes[^1] != (byte)'\n')
        {
            output.WriteByte((byte)'\n');
        }

        // ── Write each updated object ───────────────────────────────────
        XrefTable xref = new();
        // Per ISO 32000-1 §7.5.6, an update section's xref contains entries only
        // for the objects in this update. XrefTable's constructor seeds the
        // obj-0 free entry (correct for the file's first xref, but wrong here —
        // obj 0 lives in the original xref reached via /Prev). Remove it.
        xref.Remove(0);
        int maxObjectNumber = 0;
        foreach (PdfIndirectObject obj in updates)
        {
            long offset = output.Position;
            if (obj.Id.ObjectNumber > maxObjectNumber)
            {
                maxObjectNumber = obj.Id.ObjectNumber;
            }
            WriteIndirectObject(output, obj);
            xref.Set(new XrefEntry(obj.Id.ObjectNumber, obj.Id.Generation, offset));
        }

        // ── Xref ────────────────────────────────────────────────────────
        long xrefOffset = xref.Write(output);

        // ── Trailer ─────────────────────────────────────────────────────
        // /Size = max(prior /Size, max-id-in-update + 1)
        int priorSize = 0;
        if (priorTrailer.TryGetValue(PdfName.Size, out PdfPrimitive? sizePrim)
            && sizePrim is PdfInteger sizeInt)
        {
            priorSize = sizeInt.Value;
        }
        int newSize = Math.Max(priorSize, maxObjectNumber + 1);

        PdfDictionary newTrailer = new();
        // Copy through the entries the new trailer should preserve.
        if (priorTrailer.TryGetValue(PdfName.Root, out PdfPrimitive? root))
        {
            newTrailer.Set(PdfName.Root, root);
        }
        if (priorTrailer.TryGetValue(PdfName.Intern("Info"), out PdfPrimitive? info))
        {
            newTrailer.Set(PdfName.Intern("Info"), info);
        }
        if (priorTrailer.TryGetValue(PdfName.Intern("Encrypt"), out PdfPrimitive? encrypt))
        {
            newTrailer.Set(PdfName.Intern("Encrypt"), encrypt);
        }
        if (priorTrailer.TryGetValue(PdfName.Intern("ID"), out PdfPrimitive? id))
        {
            newTrailer.Set(PdfName.Intern("ID"), id);
        }

        // Apply caller's overlay.
        if (trailerOverlay is not null)
        {
            foreach (KeyValuePair<PdfName, PdfPrimitive> kv in trailerOverlay)
            {
                newTrailer.Set(kv.Key, kv.Value);
            }
        }

        // /Size and /Prev are writer-controlled regardless of overlay.
        newTrailer.Set(PdfName.Size, newSize);
        newTrailer.Set(PdfName.Intern("Prev"), (PdfPrimitive)new PdfInteger((int)priorStartXref));

        byte[] trailerLine = Encoding.ASCII.GetBytes("trailer\n");
        output.Write(trailerLine, 0, trailerLine.Length);
        WriteValue(output, newTrailer);
        output.WriteByte((byte)'\n');

        string startxref = $"\nstartxref\n{xrefOffset}\n%%EOF\n";
        byte[] startxrefBytes = Encoding.ASCII.GetBytes(startxref);
        output.Write(startxrefBytes, 0, startxrefBytes.Length);

        return output.ToArray();
    }

    /// <summary>
    /// Scans the tail of a PDF byte array for the <c>startxref</c> keyword
    /// and returns the offset that follows it. Used by
    /// <see cref="WriteIncrementalUpdate"/>.
    /// </summary>
    private static long FindStartXrefInBytes(byte[] bytes)
    {
        const int BackwardScanLimit = 4096;
        int scanStart = Math.Max(0, bytes.Length - BackwardScanLimit);
        int scanLen = bytes.Length - scanStart;
        string tail = Encoding.Latin1.GetString(bytes, scanStart, scanLen);
        int idx = tail.LastIndexOf("startxref", StringComparison.Ordinal);
        if (idx < 0)
        {
            throw new InvalidOperationException(
                "Could not locate 'startxref' keyword near the end of the source PDF.");
        }
        int pos = idx + "startxref".Length;
        // Skip whitespace
        while (pos < tail.Length && (tail[pos] == ' ' || tail[pos] == '\r' || tail[pos] == '\n' || tail[pos] == '\t'))
        {
            pos++;
        }
        long value = 0;
        while (pos < tail.Length && tail[pos] >= '0' && tail[pos] <= '9')
        {
            value = (value * 10) + (tail[pos] - '0');
            pos++;
        }
        return value;
    }

    private static byte[] GetOrCreateFileId(
        PdfDictionary trailer,
        IReadOnlyList<PdfIndirectObject> objects)
    {
        if (trailer.TryGetValue(PdfName.Intern("ID"), out PdfPrimitive? idPrim) &&
            idPrim is PdfArray idArr && idArr.Count >= 1 && idArr[0] is PdfString idStr)
        {
            return idStr.Bytes;
        }

        // Derive a stable 16-byte identifier from the document content so the
        // output is deterministic (e.g. parallel and sequential redaction yield
        // byte-identical files) and §14.4-aligned (a content-based identifier).
        byte[] fid = new byte[16];
        using (IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            byte[] header = new byte[8];
            foreach (PdfIndirectObject obj in objects)
            {
                BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0, 4), obj.Id.ObjectNumber);
                BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4, 4), obj.Id.Generation);
                hash.AppendData(header);

                if (obj.Value is PdfStream stream && stream.RawBytes.Length > 0)
                {
                    hash.AppendData(stream.RawBytes);
                }
            }

            byte[] full = hash.GetHashAndReset();
            Array.Copy(full, fid, fid.Length);
        }

        PdfString idString = new PdfString(fid);
        trailer.Set(PdfName.Intern("ID"), new PdfArray([idString, idString]));
        return fid;
    }

    // ── Object serialisation ──────────────────────────────────────────────

    internal static void WriteIndirectObject(Stream output, PdfIndirectObject obj)
    {
        // Write: "N G obj\n<value>\nendobj\n"
        string header = $"{obj.Id.ObjectNumber} {obj.Id.Generation} obj\n";
        byte[] headerBytes = Encoding.ASCII.GetBytes(header);
        output.Write(headerBytes, 0, headerBytes.Length);
        WriteValue(output, obj.Value);
        byte[] endobj = Encoding.ASCII.GetBytes("\nendobj\n");
        output.Write(endobj, 0, endobj.Length);
    }

    internal static void WriteValue(Stream output, PdfPrimitive value)
    {
        switch (value)
        {
            case PdfNull _:
                WriteAscii(output, "null");
                break;

            case PdfBoolean b:
                WriteAscii(output, b.Value ? "true" : "false");
                break;

            case PdfPaddedInteger pi:
                // Width-preserving padded form, used by signature emitters that
                // need fixed-width /ByteRange slots so subsequent byte positions
                // don't shift when the placeholder is patched.
                WriteAscii(output, pi.ToString());
                break;

            case PdfInteger i:
                WriteAscii(output, i.Value.ToString(CultureInfo.InvariantCulture));
                break;

            case PdfReal r:
                WriteReal(output, r.Value);
                break;

            case PdfName n:
                WriteAscii(output, "/");
                WriteAscii(output, EncodeName(n.Value));
                break;

            case PdfString s:
                WriteString(output, s);
                break;

            case PdfReference rf:
                WriteAscii(output,
                    $"{rf.ObjectId.ObjectNumber} {rf.ObjectId.Generation} R");
                break;

            case PdfStream st:
                WriteStream(output, st);
                break;

            case PdfDictionary d:
                WriteDictionary(output, d);
                break;

            case PdfArray a:
                WriteArray(output, a);
                break;

            default:
                WriteAscii(output, "null");
                break;
        }
    }

    private static void WriteDictionary(Stream output, PdfDictionary dict)
    {
        WriteAscii(output, "<<");

        foreach (KeyValuePair<PdfName, PdfPrimitive> entry in dict)
        {
            WriteAscii(output, "\n/");
            WriteAscii(output, EncodeName(entry.Key.Value));
            WriteAscii(output, " ");
            WriteValue(output, entry.Value);
        }

        WriteAscii(output, "\n>>");
    }

    private static void WriteArray(Stream output, PdfArray array)
    {
        WriteAscii(output, "[");
        bool first = true;

        foreach (PdfPrimitive item in array)
        {
            if (!first)
            {
                WriteAscii(output, " ");
            }

            WriteValue(output, item);
            first = false;
        }

        WriteAscii(output, "]");
    }

    private static void WriteStream(Stream output, PdfStream stream)
    {
        // Update /Length to reflect actual raw bytes.
        PdfDictionary dict = stream.Dictionary;
        dict.Set(PdfName.Length, stream.RawBytes.Length);

        WriteDictionary(output, dict);
        WriteAscii(output, "\nstream\n");
        output.Write(stream.RawBytes, 0, stream.RawBytes.Length);
        WriteAscii(output, "\nendstream");
    }

    // "Chuvadi", hoisted per CA1861. Written as a hex string by WriteString.
    private static readonly byte[] ProducerBytes = Encoding.ASCII.GetBytes("Chuvadi");

    // Adds a deterministic /Info dictionary and an XMP /Metadata stream when the
    // document lacks them, returning the updated maximum object number. Both are
    // appended as indirect objects (so the encryption pass below covers them);
    // /Metadata is attached to a copy of the catalog to avoid mutating caller
    // state. Identifiers derive from the file id, keeping output reproducible.
    private static int AddDocumentMetadata(
        List<PdfIndirectObject> objects,
        PdfDictionary trailer,
        byte[] fileId,
        int maxObjectNumber,
        SynthesizedMetadata synthesized)
    {
        int next = maxObjectNumber;

        if ((synthesized & SynthesizedMetadata.Info) != 0 && !trailer.ContainsKey(PdfName.Intern("Info")))
        {
            next++;
            PdfDictionary info = new PdfDictionary();
            info.Set(PdfName.Intern("Producer"), new PdfString(ProducerBytes));
            info.Set(PdfName.Intern("Creator"), new PdfString(ProducerBytes));
            objects.Add(new PdfIndirectObject(new PdfObjectId(next, 0), info));
            trailer.Set(PdfName.Intern("Info"), new PdfReference(new PdfObjectId(next, 0)));
        }

        // /Metadata lives on the catalog (trailer /Root). Only add it when the
        // catalog is resolvable in this object set and does not already carry one.
        if ((synthesized & SynthesizedMetadata.Metadata) != 0
            && trailer.TryGetValue(PdfName.Root, out PdfPrimitive? rootPrim)
            && rootPrim is PdfReference rootRef)
        {
            int rootNum = rootRef.ObjectId.ObjectNumber;
            for (int i = 0; i < objects.Count; i++)
            {
                if (objects[i].Id.ObjectNumber != rootNum
                    || objects[i].Value is not PdfDictionary catalog
                    || catalog.ContainsKey(PdfName.Intern("Metadata")))
                {
                    continue;
                }

                next++;
                string idHex = Convert.ToHexString(fileId);
                byte[] xmp = BuildXmpPacket(idHex);

                PdfDictionary metaDict = new PdfDictionary();
                metaDict.Set(PdfName.Type, PdfName.Intern("Metadata"));
                metaDict.Set(PdfName.Intern("Subtype"), PdfName.Intern("XML"));
                metaDict.Set(PdfName.Length, xmp.Length);
                objects.Add(new PdfIndirectObject(new PdfObjectId(next, 0), new PdfStream(metaDict, xmp)));

                PdfDictionary catalogCopy = new PdfDictionary();
                foreach (KeyValuePair<PdfName, PdfPrimitive> entry in catalog)
                {
                    catalogCopy.Set(entry.Key, entry.Value);
                }

                catalogCopy.Set(PdfName.Intern("Metadata"), new PdfReference(new PdfObjectId(next, 0)));
                objects[i] = new PdfIndirectObject(objects[i].Id, catalogCopy);
                break;
            }
        }

        return next;
    }

    // Minimal, valid XMP packet. Identifiers come from the file id so the packet
    // is deterministic. No timestamps, for reproducible output.
    private static byte[] BuildXmpPacket(string idHex)
    {
        string xml =
            "<?xpacket begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>\n" +
            "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">\n" +
            "  <rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">\n" +
            "    <rdf:Description rdf:about=\"\"\n" +
            "        xmlns:pdf=\"http://ns.adobe.com/pdf/1.3/\"\n" +
            "        xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\"\n" +
            "        xmlns:dc=\"http://purl.org/dc/elements/1.1/\"\n" +
            "        xmlns:xmpMM=\"http://ns.adobe.com/xap/1.0/mm/\">\n" +
            "      <pdf:Producer>Chuvadi</pdf:Producer>\n" +
            "      <xmp:CreatorTool>Chuvadi</xmp:CreatorTool>\n" +
            "      <dc:format>application/pdf</dc:format>\n" +
            "      <xmpMM:DocumentID>uuid:" + idHex + "</xmpMM:DocumentID>\n" +
            "      <xmpMM:InstanceID>uuid:" + idHex + "</xmpMM:InstanceID>\n" +
            "    </rdf:Description>\n" +
            "  </rdf:RDF>\n" +
            "</x:xmpmeta>\n" +
            "<?xpacket end=\"w\"?>";
        return Encoding.UTF8.GetBytes(xml);
    }

    private static void WriteString(Stream output, PdfString s)
    {
        // Write as hex string for simplicity — always unambiguous.
        WriteAscii(output, "<");
        byte[] bytes = s.Bytes;

        foreach (byte b in bytes)
        {
            WriteAscii(output, b.ToString("X2", CultureInfo.InvariantCulture));
        }

        WriteAscii(output, ">");
    }

    private static void WriteReal(Stream output, double value)
    {
        // Use up to 6 significant digits, no trailing zeros, no scientific notation.
        string formatted = value.ToString("G6", CultureInfo.InvariantCulture);

        // Ensure a decimal point is present (PDF requires reals to have one).
        if (!formatted.Contains('.') && !formatted.Contains('E') && !formatted.Contains('e'))
        {
            formatted += ".0";
        }

        WriteAscii(output, formatted);
    }

    private static string EncodeName(string name)
    {
        // Encode characters that need #XX escaping in PDF names.
        // PDF 32000-1:2008 §7.3.5.
        StringBuilder sb = new StringBuilder(name.Length);

        foreach (char c in name)
        {
            if (c == '#' || c < 33 || c > 126)
            {
                sb.Append('#');
                sb.Append(((int)c).ToString("X2", CultureInfo.InvariantCulture));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private static void WriteAscii(Stream output, string text)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(text);
        output.Write(bytes, 0, bytes.Length);
    }
}
