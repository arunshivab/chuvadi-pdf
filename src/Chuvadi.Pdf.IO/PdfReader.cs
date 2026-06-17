// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.5 — File structure
// PHASE: Phase 1 — Chuvadi.Pdf.IO
// Opens a PDF file, locates its xref, and provides lazy object access.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Encryption;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.IO;

/// <summary>
/// Opens an existing PDF file and provides access to its object graph.
/// PDF 32000-1:2008 §7.5 — File structure.
/// </summary>
public sealed class PdfReader : IDisposable
{
    private const int BackwardScanLimit = 1024;

    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private bool _disposed;

    private PdfReader(
        Stream stream,
        bool leaveOpen,
        PdfDictionary trailer,
        PdfObjectStore objects)
    {
        _stream = stream;
        _leaveOpen = leaveOpen;
        _disposed = false;
        Trailer = trailer;
        Objects = objects;
        Warnings = RecoverPageTree(objects, trailer);
    }

    // Validates the page tree at open time and repairs kids whose cross-reference
    // entry resolves to the wrong object (e.g. a duplicate object number left by
    // an older writer). Healthy files do no scanning. Recovery never throws: a
    // file that cannot be validated is left as-is for the normal code path to
    // report when a page is accessed.
    private IReadOnlyList<string> RecoverPageTree(PdfObjectStore objects, PdfDictionary trailer)
    {
        try
        {
            if (trailer.GetAs<PdfPrimitive>(PdfName.Root) is not PdfReference rootRef)
            {
                return Array.Empty<string>();
            }

            if (objects.ResolveById(rootRef.ObjectId) is not PdfDictionary catalog)
            {
                return Array.Empty<string>();
            }

            if (catalog.GetAs<PdfPrimitive>(PdfName.Pages) is not PdfPrimitive pagesPrim)
            {
                return Array.Empty<string>();
            }

            if (objects.Resolve(pagesPrim) is not PdfDictionary pagesRoot)
            {
                return Array.Empty<string>();
            }

            return PageTreeRecovery.Recover(this, objects, pagesRoot);
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    // ── Factory: synchronous ──────────────────────────────────────────────

    /// <summary>Opens a PDF file from the given readable, seekable stream.</summary>
    /// <remarks>
    /// Performs synchronous blocking I/O against <paramref name="stream"/>. The
    /// reader holds a reference to the stream for the lifetime of the document
    /// and reads objects lazily on demand. Memory-efficient for large files
    /// because only xref tables and accessed objects are materialised.
    ///
    /// Not supported on WebAssembly (browser blocks on synchronous I/O against
    /// network resources). Use <see cref="OpenAsync(Stream, CancellationToken)"/>
    /// for cross-platform code.
    /// </remarks>
    /// <exception cref="PdfParseException">
    /// Thrown when the file is encrypted. Use the password overload to open
    /// encrypted PDFs.
    /// </exception>
    public static PdfReader Open(Stream stream, bool leaveOpen = false)
    {
        return OpenInternal(stream, password: null, leaveOpen);
    }

    /// <summary>
    /// Opens a PDF file with the given password. For unencrypted PDFs the
    /// password is ignored.
    /// </summary>
    /// <remarks>
    /// Synchronous blocking I/O. Not supported on WebAssembly; use
    /// <see cref="OpenAsync(Stream, string, CancellationToken)"/> for
    /// cross-platform code.
    /// </remarks>
    /// <param name="stream">Readable, seekable stream containing the PDF.</param>
    /// <param name="password">User or owner password. Empty string for default.</param>
    /// <param name="leaveOpen">Whether to leave the stream open on dispose.</param>
    /// <exception cref="PdfParseException">Thrown when the password is incorrect.</exception>
    public static PdfReader Open(Stream stream, string password, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(password);
        return OpenInternal(stream, password, leaveOpen);
    }

    // ── Factory: asynchronous (WASM-friendly) ─────────────────────────────

    /// <summary>
    /// Asynchronously opens a PDF file from the given readable stream.
    /// </summary>
    /// <remarks>
    /// The input stream is fully buffered into memory before parsing begins,
    /// making this method WebAssembly-compatible and tolerant of non-seekable
    /// streams (HTTP responses, decompression streams, pipes). The cost is a
    /// full-file buffer in RAM for the lifetime of the document; for large
    /// PDFs on a desktop runtime, the synchronous <see cref="Open(Stream, bool)"/>
    /// overload is more memory-efficient.
    ///
    /// Cancellation is checked before and after the buffer fill. Once parsing
    /// begins it runs to completion on the buffered bytes.
    ///
    /// The reader owns the internal memory buffer; the caller retains
    /// responsibility for disposing <paramref name="stream"/>.
    /// </remarks>
    /// <param name="stream">A readable stream containing the PDF. Need not be seekable.</param>
    /// <param name="cancellationToken">A token that cancels the buffer fill.</param>
    public static Task<PdfReader> OpenAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        return OpenAsyncInternal(stream, password: null, cancellationToken);
    }

    /// <summary>
    /// Asynchronously opens an encrypted PDF file with the given password.
    /// </summary>
    /// <remarks>
    /// See <see cref="OpenAsync(Stream, CancellationToken)"/> for the buffering
    /// and cancellation semantics. For unencrypted PDFs the password is ignored.
    /// </remarks>
    /// <param name="stream">A readable stream containing the PDF. Need not be seekable.</param>
    /// <param name="password">User or owner password. Empty string for default.</param>
    /// <param name="cancellationToken">A token that cancels the buffer fill.</param>
    /// <exception cref="PdfParseException">Thrown when the password is incorrect.</exception>
    public static Task<PdfReader> OpenAsync(
        Stream stream,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(password);
        return OpenAsyncInternal(stream, password, cancellationToken);
    }

    private static async Task<PdfReader> OpenAsyncInternal(
        Stream stream,
        string? password,
        CancellationToken cancellationToken)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        if (!stream.CanRead)
        {
            throw new ArgumentException(
                "Stream must be readable.", nameof(stream));
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Buffer the entire stream into memory. The resulting MemoryStream is
        // seekable, satisfying OpenInternal's precondition without imposing
        // seekability on the caller's input stream.
        MemoryStream buffer = new MemoryStream();
        try
        {
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            buffer.Dispose();
            throw;
        }

        cancellationToken.ThrowIfCancellationRequested();

        buffer.Position = 0;

        // The reader takes ownership of the internal buffer (leaveOpen: false).
        // The caller's input stream is not owned and not disposed by us.
        try
        {
            return OpenInternal(buffer, password, leaveOpen: false);
        }
        catch
        {
            buffer.Dispose();
            throw;
        }
    }

    // ── Internal open ─────────────────────────────────────────────────────

    private static PdfReader OpenInternal(Stream stream, string? password, bool leaveOpen)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException(
                "Stream must be readable and seekable.", nameof(stream));
        }

        long startXrefOffset = FindStartXref(stream);

        PdfDictionary trailer = new PdfDictionary();
        XrefTable xref = new XrefTable();
        LoadXrefChain(stream, startXrefOffset, xref, trailer);

        PdfObjectParser parser = new PdfObjectParser(stream);

        // Check for encryption. The /Encrypt entry in the trailer holds the
        // encryption dictionary (or an indirect reference to it).
        Decryptor? decryptor = null;
        int encryptObjectNumber = -1;
        bool encryptMetadata = true;

        if (trailer.TryGetValue(PdfName.Intern("Encrypt"), out PdfPrimitive? encryptPrim))
        {
            (decryptor, encryptObjectNumber, encryptMetadata) = BuildDecryptor(
                parser, xref, trailer, encryptPrim, password);
        }

        Decryptor? finalDecryptor = decryptor;
        int finalEncryptObjNum = encryptObjectNumber;
        bool finalEncryptMetadata = encryptMetadata;

        // The object-stream reader caches decoded /ObjStm containers so
        // repeated lookups against the same container are cheap. We construct
        // it once and share it across all lazy loads. PDF 32000-1:2008 §7.5.7.
        ObjectStreamReader streamReader = new ObjectStreamReader();

        // The store has to be referenced from inside the lazy loader (so that
        // the Compressed-entry branch in LoadObjectFromFile can ask for the
        // container object through the same resolution + decryption path).
        // We use a single-element holder to defer the back-reference until
        // after the store is constructed; the loader is only invoked lazily
        // when the store is queried, by which time the holder is populated.
        PdfObjectStore[] storeHolder = new PdfObjectStore[1];

        PdfObjectStore objects = new PdfObjectStore(id =>
        {
            PdfIndirectObject? loaded = LoadObjectFromFile(
                parser, xref, id, streamReader, storeHolder[0]);

            if (loaded is null || finalDecryptor is null)
            {
                return loaded;
            }

            // The /Encrypt object itself is NEVER decrypted (chicken-and-egg).
            if (id.ObjectNumber == finalEncryptObjNum)
            {
                return loaded;
            }

            // Objects stored inside an object stream are not encrypted
            // individually (ISO 32000-1 §7.6): the container stream is encrypted
            // as a whole and was already decrypted when it was loaded above, so
            // the extracted value is already plaintext. Decrypting it a second
            // time would corrupt it.
            if (xref.TryGet(id.ObjectNumber, out XrefEntry compressedEntry)
                && compressedEntry.IsCompressed)
            {
                return loaded;
            }

            PdfPrimitive decryptedValue = EncryptionVisitor.Transform(
                loaded.Value,
                id.ObjectNumber,
                id.Generation,
                finalDecryptor.Decrypt,
                skipMetadataEncryption: !finalEncryptMetadata);

            return new PdfIndirectObject(loaded.Id, decryptedValue);
        });

        storeHolder[0] = objects;

        return new PdfReader(stream, leaveOpen, trailer, objects);
    }

    private static (Decryptor decryptor, int encryptObjectNumber, bool encryptMetadata)
        BuildDecryptor(
            PdfObjectParser parser,
            XrefTable xref,
            PdfDictionary trailer,
            PdfPrimitive encryptPrim,
            string? password)
    {
        PdfDictionary encryptDict;
        int encryptObjectNumber = -1;

        if (encryptPrim is PdfReference encRef)
        {
            encryptObjectNumber = encRef.ObjectId.ObjectNumber;
            PdfIndirectObject? indirect = LoadInUseObjectFromFile(parser, xref, encRef.ObjectId);

            if (indirect?.Value is not PdfDictionary d)
            {
                throw new PdfParseException(
                    "Document /Encrypt entry could not be resolved to a dictionary.");
            }

            encryptDict = d;
        }
        else if (encryptPrim is PdfDictionary inlineDict)
        {
            encryptDict = inlineDict;
        }
        else
        {
            throw new PdfParseException("Document /Encrypt entry has an invalid type.");
        }

        byte[] firstFileId = Array.Empty<byte>();

        if (trailer.TryGetValue(PdfName.Intern("ID"), out PdfPrimitive? idPrim) &&
            idPrim is PdfArray idArr && idArr.Count >= 1 && idArr[0] is PdfString idStr)
        {
            firstFileId = idStr.Bytes;
        }

        Decryptor decryptor = PdfEncryption.TryOpen(encryptDict, firstFileId, password ?? string.Empty)
            ?? throw new PdfParseException(
                "PDF is encrypted; provide the correct user or owner password.");

        bool encryptMetadata = true;
        if (encryptDict.TryGetValue(PdfName.Intern("EncryptMetadata"), out PdfPrimitive? emPrim) &&
            emPrim is PdfBoolean emb)
        {
            encryptMetadata = emb.Value;
        }

        return (decryptor, encryptObjectNumber, encryptMetadata);
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>Gets the PDF trailer dictionary.</summary>
    public PdfDictionary Trailer { get; }

    /// <summary>Gets the lazy object store.</summary>
    public PdfObjectStore Objects { get; }

    /// <summary>
    /// Gets warnings raised while opening the document — for example, page-tree
    /// objects recovered from a corrupt cross-reference table. An empty list
    /// means the file opened without any structural repair.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>Gets the total length of the underlying file in bytes.</summary>
    public long FileLength
    {
        get
        {
            if (_disposed) { throw new ObjectDisposedException(nameof(PdfReader)); }
            return _stream.Length;
        }
    }

    /// <summary>Gets the document Catalog dictionary, or null.</summary>
    public PdfDictionary? Catalog
    {
        get
        {
            PdfPrimitive? root = Trailer.GetAs<PdfPrimitive>(PdfName.Root);

            if (root is PdfReference rootRef)
            {
                return Objects.ResolveById(rootRef.ObjectId) as PdfDictionary;
            }

            return root as PdfDictionary;
        }
    }

    /// <summary>Gets the document information dictionary, if present.</summary>
    public PdfDictionary? Info
    {
        get
        {
            PdfPrimitive? info = Trailer.GetAs<PdfPrimitive>(PdfName.Info);

            if (info is PdfReference infoRef)
            {
                return Objects.ResolveById(infoRef.ObjectId) as PdfDictionary;
            }

            return info as PdfDictionary;
        }
    }

    /// <summary>
    /// Reads a contiguous byte range directly from the underlying PDF file.
    /// </summary>
    /// <remarks>
    /// Signature verification needs the raw bytes of the file at known offsets —
    /// the signature dictionary's /ByteRange entry identifies them. This method
    /// exposes that capability without requiring callers to keep their own copy
    /// of the file.
    /// </remarks>
    /// <param name="offset">Absolute byte offset within the PDF file.</param>
    /// <param name="count">Number of bytes to read.</param>
    /// <returns>A newly allocated byte array of length <paramref name="count"/>.</returns>
    public byte[] ReadFileBytes(long offset, int count)
    {
        if (_disposed) { throw new ObjectDisposedException(nameof(PdfReader)); }
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "offset must be non-negative.");
        }
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "count must be non-negative.");
        }

        byte[] buffer = new byte[count];
        _stream.Position = offset;
        int total = 0;
        while (total < count)
        {
            int read = _stream.Read(buffer, total, count - total);
            if (read <= 0)
            {
                throw new EndOfStreamException(
                    $"Reached end of file while reading {count} bytes at offset {offset} " +
                    $"(read {total} bytes).");
            }
            total += read;
        }
        return buffer;
    }

    /// <summary>
    /// Copies a contiguous byte range from the file directly into <paramref name="destination"/>.
    /// </summary>
    /// <remarks>
    /// Use this for hash computation over large byte ranges — feeds the destination
    /// stream incrementally without materialising the bytes as a single array.
    /// </remarks>
    public void CopyFileBytes(long offset, long count, Stream destination)
    {
        if (_disposed) { throw new ObjectDisposedException(nameof(PdfReader)); }
        ArgumentNullException.ThrowIfNull(destination);
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "offset must be non-negative.");
        }
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "count must be non-negative.");
        }

        _stream.Position = offset;
        byte[] buffer = new byte[8192];
        long remaining = count;
        while (remaining > 0)
        {
            int want = (int)Math.Min(remaining, buffer.Length);
            int read = _stream.Read(buffer, 0, want);
            if (read <= 0)
            {
                throw new EndOfStreamException(
                    "Reached end of file while copying byte range.");
            }
            destination.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;

            if (!_leaveOpen)
            {
                _stream.Dispose();
            }
        }
    }

    // ── Public: xref loading ──────────────────────────────────────────────

    /// <summary>
    /// Locates the file's <c>startxref</c> offset, walks the complete
    /// cross-reference chain (classic xref tables and/or xref streams, following
    /// <c>/Prev</c> links), and returns the merged cross-reference table.
    /// </summary>
    /// <param name="stream">
    /// A readable, seekable stream positioned anywhere; this method seeks as
    /// needed and leaves the stream at an unspecified position.
    /// </param>
    /// <returns>The merged <see cref="XrefTable"/> for the whole file.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="PdfParseException">
    /// The <c>startxref</c> keyword or a valid offset could not be found, or an
    /// xref section could not be parsed.
    /// </exception>
    public static XrefTable LoadXref(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        long startXrefOffset = FindStartXref(stream);
        XrefTable xref = new();
        PdfDictionary trailer = new();
        LoadXrefChain(stream, startXrefOffset, xref, trailer);
        return xref;
    }

    // ── Private: xref location ────────────────────────────────────────────

    private static long FindStartXref(Stream stream)
    {
        long fileLen = stream.Length;
        long scanStart = Math.Max(0, fileLen - BackwardScanLimit);
        int scanLen = (int)(fileLen - scanStart);

        stream.Seek(scanStart, SeekOrigin.Begin);
        byte[] tail = new byte[scanLen];
        int read = 0;

        while (read < scanLen)
        {
            int n = stream.Read(tail, read, scanLen - read);

            if (n == 0)
            {
                break;
            }

            read += n;
        }

        string tailText = Encoding.Latin1.GetString(tail, 0, read);
        int startxrefIdx = tailText.LastIndexOf("startxref", StringComparison.Ordinal);

        if (startxrefIdx < 0)
        {
            throw new PdfParseException(
                "Could not locate 'startxref' keyword in the last 1024 bytes of the file.");
        }

        int pos = startxrefIdx + "startxref".Length;

        while (pos < tailText.Length && IsWhitespace(tailText[pos]))
        {
            pos++;
        }

        int numStart = pos;

        while (pos < tailText.Length && char.IsDigit(tailText[pos]))
        {
            pos++;
        }

        string offsetText = tailText[numStart..pos].Trim();

        if (!long.TryParse(offsetText, NumberStyles.None, CultureInfo.InvariantCulture,
            out long xrefOffset))
        {
            throw new PdfParseException(
                $"Invalid startxref offset value: '{offsetText}'.");
        }

        return xrefOffset;
    }

    // ── Private: xref chain ───────────────────────────────────────────────

    private static void LoadXrefChain(
        Stream stream,
        long offset,
        XrefTable xref,
        PdfDictionary mergedTrailer)
    {
        HashSet<long> visited = new HashSet<long>();

        while (offset >= 0 && !visited.Contains(offset))
        {
            visited.Add(offset);
            stream.Seek(offset, SeekOrigin.Begin);

            byte[] peek = new byte[5];
            int peekRead = stream.Read(peek, 0, 5);
            string peekText = Encoding.Latin1.GetString(peek, 0, peekRead);

            if (peekText.StartsWith("xref", StringComparison.Ordinal))
            {
                PdfDictionary sectionTrailer = LoadClassicXref(stream, offset, xref);
                MergeTrailer(mergedTrailer, sectionTrailer);
                offset = sectionTrailer.GetInteger(PdfName.Prev, -1);
            }
            else
            {
                PdfDictionary sectionTrailer = LoadXrefStream(stream, offset, xref);
                MergeTrailer(mergedTrailer, sectionTrailer);
                offset = sectionTrailer.GetInteger(PdfName.Prev, -1);
            }
        }
    }

    private static PdfDictionary LoadClassicXref(
        Stream stream,
        long offset,
        XrefTable xref)
    {
        // Parse the xref entries. XrefTable.Parse handles the "xref" keyword
        // itself (it skips it if present). Note: Parse uses StreamReader internally
        // which buffers ahead, so the stream position after Parse is unreliable.
        stream.Seek(offset, SeekOrigin.Begin);
        XrefTable section = XrefTable.Parse(stream);

        foreach (XrefEntry entry in section.Entries)
        {
            if (!xref.Contains(entry.ObjectNumber) || entry.IsFree)
            {
                xref.Set(entry);
            }
        }

        // Because StreamReader in XrefTable.Parse buffers ahead, the stream
        // position after Parse may be past the "trailer" keyword.
        // Scan the raw bytes from 'offset' to find "trailer" precisely.
        // PDF 32000-1:2008 §7.5.5 — the trailer keyword follows the xref entries.
        long trailerPos = ScanForKeyword(stream, offset, "trailer");

        if (trailerPos < 0)
        {
            throw new PdfParseException(
                $"Could not find 'trailer' keyword in xref section at offset {offset}.");
        }

        // Position past "trailer" keyword and read the trailer dictionary.
        PdfObjectParser trailerParser = new PdfObjectParser(stream);
        trailerParser.Seek(trailerPos + 7); // 7 = "trailer".Length
        PdfPrimitive trailerValue = trailerParser.ReadValue();

        if (trailerValue is not PdfDictionary trailerDict)
        {
            throw new PdfParseException(
                $"Trailer must be a dictionary, got {trailerValue.GetType().Name}.");
        }

        return trailerDict;
    }

    /// <summary>
    /// Scans raw bytes from <paramref name="startFrom"/> forward, looking
    /// for the ASCII keyword. Returns the absolute stream offset of the first
    /// character of the keyword, or -1 if not found.
    /// </summary>
    private static long ScanForKeyword(Stream stream, long startFrom, string keyword)
    {
        byte[] kw = Encoding.ASCII.GetBytes(keyword);
        stream.Seek(startFrom, SeekOrigin.Begin);

        // Read from startFrom to end of file (or a generous window).
        using (MemoryStream buffer = new MemoryStream())
        {
            stream.CopyTo(buffer);
            byte[] bytes = buffer.ToArray();

            for (int i = 0; i <= bytes.Length - kw.Length; i++)
            {
                bool match = true;

                for (int j = 0; j < kw.Length; j++)
                {
                    if (bytes[i + j] != kw[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    return startFrom + i;
                }
            }
        }

        return -1;
    }

    private static PdfDictionary LoadXrefStream(
        Stream stream,
        long offset,
        XrefTable xref)
    {
        PdfObjectParser parser = new PdfObjectParser(stream);
        parser.Seek(offset);

        PdfIndirectObject obj = parser.ReadIndirectObject();

        if (obj.Value is not PdfStream xrefStream)
        {
            throw new PdfParseException(
                $"Expected cross-reference stream at offset {offset}, " +
                $"got {obj.Value.GetType().Name}.");
        }

        PdfDictionary dict = xrefStream.Dictionary;
        byte[] rawBytes = xrefStream.RawBytes;
        byte[] decodedBytes = DecodeStreamBytes(dict, rawBytes);

        XrefStreamTable streamTable = XrefStreamTable.Parse(dict, decodedBytes);

        foreach (XrefEntry entry in streamTable.Entries)
        {
            if (!xref.Contains(entry.ObjectNumber) || entry.IsFree)
            {
                xref.Set(entry);
            }
        }

        return dict;
    }

    private static byte[] DecodeStreamBytes(PdfDictionary dict, byte[] rawBytes)
    {
        // v2.1.8: handles single-Name AND Array /Filter via the shared
        // helper in ObjectStreamReader. Prior to v2.1.8 this method only
        // honoured the single-Name case and silently emitted raw bytes
        // when /Filter was an array — see
        // docs/v2.1.8-filter-array-and-followups.md.
        return ObjectStreamReader.Decode(dict, rawBytes);
    }

    private static void MergeTrailer(PdfDictionary target, PdfDictionary source)
    {
        foreach (KeyValuePair<PdfName, PdfPrimitive> entry in source)
        {
            if (!target.ContainsKey(entry.Key))
            {
                target.Set(entry.Key, entry.Value);
            }
        }
    }

    // ── Private: object loading ───────────────────────────────────────────

    private static PdfIndirectObject? LoadObjectFromFile(
        PdfObjectParser parser,
        XrefTable xref,
        PdfObjectId id,
        ObjectStreamReader streamReader,
        PdfObjectStore store)
    {
        if (!xref.TryGet(id.ObjectNumber, out XrefEntry entry))
        {
            return null;
        }

        if (entry.IsInUse)
        {
            return LoadInUseObject(parser, id, entry.ByteOffset);
        }

        if (entry.IsCompressed)
        {
            // PDF 32000-1:2008 §7.5.7: the entry names the containing object
            // stream (StreamObjectNumber) and the zero-based index of this
            // object within it (IndexInStream). We resolve the container via
            // the store so it goes through caching + per-object decryption.
            PdfPrimitive? value = streamReader.TryRead(
                entry.StreamObjectNumber,
                entry.IndexInStream,
                containerObjNum => ResolveContainerAsIndirect(store, containerObjNum));

            if (value is null) { return null; }

            return new PdfIndirectObject(id, value);
        }

        // Free entry, or unknown type — caller treats as missing.
        return null;
    }

    /// <summary>
    /// Loads an object that is guaranteed to be a classic in-use indirect
    /// object (xref type 1). Used before the document's object store and
    /// object-stream reader exist, in particular for the /Encrypt object
    /// during decryption setup (which per PDF 32000-1:2008 §7.6 must not
    /// be stored in an object stream).
    /// </summary>
    private static PdfIndirectObject? LoadInUseObjectFromFile(
        PdfObjectParser parser,
        XrefTable xref,
        PdfObjectId id)
    {
        long offset = xref.GetOffset(id.ObjectNumber);
        if (offset < 0)
        {
            return null;
        }
        return LoadInUseObject(parser, id, offset);
    }

    /// <summary>
    /// Reads a classic in-use indirect object at the given byte offset.
    /// </summary>
    private static PdfIndirectObject LoadInUseObject(
        PdfObjectParser parser,
        PdfObjectId id,
        long offset)
    {
        try
        {
            parser.Seek(offset);
            return parser.ReadIndirectObject();
        }
        catch (Exception ex) when (ex is not PdfParseException)
        {
            throw new PdfParseException(
                $"Error reading object {id} at offset {offset}.", ex);
        }
    }

    /// <summary>
    /// Helper for <see cref="ObjectStreamReader"/>: loads the container
    /// (an object stream) through the store, so decryption and caching
    /// run normally, and re-wraps the result as a <see cref="PdfIndirectObject"/>.
    /// Returns null when the container is missing or not a stream.
    /// </summary>
    private static PdfIndirectObject? ResolveContainerAsIndirect(
        PdfObjectStore store,
        int objectNumber)
    {
        PdfObjectId id = new PdfObjectId(objectNumber, 0);
        PdfPrimitive value = store.ResolveById(id);
        if (value is PdfNull)
        {
            return null;
        }
        return new PdfIndirectObject(id, value);
    }

    // ── Utilities ─────────────────────────────────────────────────────────

    private static bool IsWhitespace(char c)
    {
        return c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\f';
    }
}
