// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.7.2 — Document Catalog
//        PDF 32000-1:2008 §14.3.3 — Document information dictionary
//        PDF 32000-1:2008 §7.5.2 — File header
//        PDF 32000-1:2008 §7.6 — Encryption
//        PDF 32000-1:2008 §7.9.4 — Date strings
// PHASE: Phase 1 — Chuvadi.Pdf.Documents (v2.0.0 R3 surface)
// High-level document model over a PdfReader.

using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Documents;

/// <summary>
/// Represents an opened PDF document.
/// </summary>
/// <remarks>
/// <see cref="PdfDocument"/> wraps a <see cref="PdfReader"/> and exposes
/// the document-level object model: pages, metadata, and the document catalog.
///
/// Open a document with <see cref="Open(Stream, bool)"/> or
/// <see cref="Open(string)"/> on desktop runtimes, or
/// <see cref="OpenAsync(Stream, CancellationToken)"/> /
/// <see cref="OpenAsync(string, CancellationToken)"/> on WebAssembly or
/// any caller that needs to integrate with asynchronous I/O. Dispose the
/// document when finished — it owns the underlying reader and stream.
///
/// PDF 32000-1:2008 §7.7.2 — Document Catalog.
/// PDF 32000-1:2008 §14.3.3 — Document information dictionary.
/// </remarks>
public sealed class PdfDocument : IDisposable
{
    private readonly PdfReader _reader;
    private PdfPageCollection? _pages;
    private DocumentInfo? _documentInfo;
    private string? _pdfVersionCache;
    private bool _pdfVersionProbed;
    private bool _disposed;

    private PdfDocument(PdfReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _disposed = false;
    }

    // ── Factory: synchronous ──────────────────────────────────────────────

    /// <summary>
    /// Opens a PDF document from the given stream.
    /// </summary>
    /// <remarks>
    /// Synchronous blocking I/O. Not supported on WebAssembly; use
    /// <see cref="OpenAsync(Stream, CancellationToken)"/> for cross-platform code.
    /// </remarks>
    /// <param name="stream">A readable, seekable PDF stream.</param>
    /// <param name="leaveOpen">
    /// True to leave the stream open when this document is disposed.
    /// </param>
    public static PdfDocument Open(Stream stream, bool leaveOpen = false)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        PdfReader reader = PdfReader.Open(stream, leaveOpen);
        return new PdfDocument(reader);
    }

    /// <summary>
    /// Opens an encrypted PDF using the given user or owner password.
    /// </summary>
    /// <remarks>
    /// Synchronous blocking I/O. Not supported on WebAssembly; use
    /// <see cref="OpenAsync(Stream, string, CancellationToken)"/> for
    /// cross-platform code.
    /// </remarks>
    /// <param name="stream">Readable, seekable PDF stream.</param>
    /// <param name="password">User or owner password. Empty string for default.</param>
    /// <param name="leaveOpen">Whether to leave the underlying stream open on dispose.</param>
    public static PdfDocument Open(Stream stream, string password, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(password);

        PdfReader reader = PdfReader.Open(stream, password, leaveOpen);
        return new PdfDocument(reader);
    }

    /// <summary>Opens an encrypted PDF from a file path using the given password.</summary>
    /// <remarks>
    /// Synchronous blocking I/O against the file system. Use
    /// <see cref="OpenAsync(string, string, CancellationToken)"/> for
    /// cross-platform code.
    /// </remarks>
    public static PdfDocument Open(string path, string password)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(password);

        FileStream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        PdfReader reader = PdfReader.Open(stream, password, leaveOpen: false);
        return new PdfDocument(reader);
    }

    /// <summary>
    /// Opens a PDF document from a file path.
    /// </summary>
    /// <remarks>
    /// Synchronous blocking I/O against the file system. Use
    /// <see cref="OpenAsync(string, CancellationToken)"/> for cross-platform code.
    /// </remarks>
    /// <param name="path">The path to the PDF file.</param>
    public static PdfDocument Open(string path)
    {
        if (path is null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        FileStream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        PdfReader reader = PdfReader.Open(stream, leaveOpen: false);
        return new PdfDocument(reader);
    }

    // ── Factory: asynchronous (WASM-friendly) ─────────────────────────────

    /// <summary>
    /// Asynchronously opens a PDF document from the given stream.
    /// </summary>
    /// <remarks>
    /// The input stream is fully buffered into memory before parsing begins,
    /// making this method WebAssembly-compatible and tolerant of non-seekable
    /// streams. The document owns the internal buffer; the caller retains
    /// responsibility for disposing <paramref name="stream"/>.
    /// </remarks>
    /// <param name="stream">A readable PDF stream. Need not be seekable.</param>
    /// <param name="cancellationToken">A token that cancels the buffer fill.</param>
    public static async Task<PdfDocument> OpenAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        PdfReader reader = await PdfReader.OpenAsync(stream, cancellationToken)
            .ConfigureAwait(false);
        return new PdfDocument(reader);
    }

    /// <summary>
    /// Asynchronously opens an encrypted PDF document with the given password.
    /// </summary>
    /// <remarks>
    /// See <see cref="OpenAsync(Stream, CancellationToken)"/> for the buffering
    /// and cancellation semantics. For unencrypted PDFs the password is ignored.
    /// </remarks>
    /// <param name="stream">A readable PDF stream. Need not be seekable.</param>
    /// <param name="password">User or owner password. Empty string for default.</param>
    /// <param name="cancellationToken">A token that cancels the buffer fill.</param>
    public static async Task<PdfDocument> OpenAsync(
        Stream stream,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(password);

        PdfReader reader = await PdfReader.OpenAsync(stream, password, cancellationToken)
            .ConfigureAwait(false);
        return new PdfDocument(reader);
    }

    /// <summary>
    /// Asynchronously opens a PDF document from a file path.
    /// </summary>
    /// <remarks>
    /// Opens the file with <see cref="FileStream"/> configured for async I/O,
    /// buffers it fully into memory, then parses. The file handle is released
    /// before this method returns.
    /// </remarks>
    /// <param name="path">The path to the PDF file.</param>
    /// <param name="cancellationToken">A token that cancels the buffer fill.</param>
    public static async Task<PdfDocument> OpenAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);

        using FileStream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);

        return await OpenAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously opens an encrypted PDF document from a file path.
    /// </summary>
    /// <remarks>
    /// See <see cref="OpenAsync(string, CancellationToken)"/> for I/O semantics.
    /// </remarks>
    /// <param name="path">The path to the PDF file.</param>
    /// <param name="password">User or owner password. Empty string for default.</param>
    /// <param name="cancellationToken">A token that cancels the buffer fill.</param>
    public static async Task<PdfDocument> OpenAsync(
        string path,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(password);

        using FileStream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);

        return await OpenAsync(stream, password, cancellationToken).ConfigureAwait(false);
    }

    // ── Pages ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Gets the collection of pages in the document.
    /// Pages are resolved lazily from the page tree.
    /// PDF 32000-1:2008 §7.7.3 — Page tree.
    /// </summary>
    public PdfPageCollection Pages
    {
        get
        {
            if (_pages is null)
            {
                PdfDictionary? pagesDict = GetPagesRoot();
                _pages = new PdfPageCollection(pagesDict, _reader.Objects);
            }

            return _pages;
        }
    }

    /// <summary>Gets the total number of pages.</summary>
    public int PageCount => Pages.Count;

    // ── Document metadata: scalar shortcuts ──────────────────────────────

    /// <summary>
    /// Gets the document Title, or null when not set.
    /// PDF 32000-1:2008 §14.3.3, Table 317 — Title.
    /// </summary>
    public string? Title => GetInfoString(PdfName.Intern("Title"));

    /// <summary>Gets the document Author, or null when not set.</summary>
    public string? Author => GetInfoString(PdfName.Intern("Author"));

    /// <summary>Gets the document Subject, or null when not set.</summary>
    public string? Subject => GetInfoString(PdfName.Intern("Subject"));

    /// <summary>Gets the document Keywords, or null when not set.</summary>
    public string? Keywords => GetInfoString(PdfName.Intern("Keywords"));

    /// <summary>Gets the name of the application that created the document.</summary>
    public string? Creator => GetInfoString(PdfName.Intern("Creator"));

    /// <summary>Gets the name of the PDF producer application.</summary>
    public string? Producer => GetInfoString(PdfName.Intern("Producer"));

    // ── Document catalog ──────────────────────────────────────────────────

    /// <summary>
    /// Gets the raw document Catalog dictionary.
    /// PDF 32000-1:2008 §7.7.2 — Document Catalog.
    /// </summary>
    public PdfDictionary Catalog
    {
        get
        {
            return _reader.Catalog ??
                throw new PdfCorruptionException(
                    "The PDF file does not have a valid document Catalog. " +
                    "The trailer /Root entry is missing or does not point to a dictionary.");
        }
    }

    /// <summary>
    /// Gets the raw trailer dictionary.
    /// </summary>
    public PdfDictionary Trailer => _reader.Trailer;

    /// <summary>
    /// Gets the document's linearization parameter dictionary, or null when the
    /// document is not linearized (Fast Web View).
    /// </summary>
    public LinearizationInfo? Linearization
    {
        get
        {
            if (_linearization is null && !_linearizationProbed)
            {
                int maxObjNum = 5;
                if (_reader.Trailer.TryGetValue(PdfName.Size, out PdfPrimitive? sizePrim) &&
                    sizePrim is PdfInteger sizeInt && sizeInt.Value > 0)
                {
                    maxObjNum = sizeInt.Value - 1;
                }
                _linearization = LinearizationReader.TryRead(_reader.Objects, maxObjNum);
                _linearizationProbed = true;
            }
            return _linearization;
        }
    }

    /// <summary>Returns true when the document is linearized (Fast Web View).</summary>
    public bool IsLinearized => Linearization is not null;

    private LinearizationInfo? _linearization;
    private bool _linearizationProbed;

    /// <summary>
    /// Gets the raw document information dictionary, or null when absent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In v2.0.0 this property is the renamed successor of the previous
    /// <c>PdfDocument.Info</c> (which returned a
    /// <see cref="PdfDictionary"/>). The new <see cref="Info"/> property
    /// returns the higher-level <see cref="DocumentInfo"/> aggregate.
    /// </para>
    /// </remarks>
    public PdfDictionary? InfoDictionary => _reader.Info;

    /// <summary>
    /// Gets an aggregate view of the document's metadata, structural
    /// properties, and security state.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Computed lazily on first access and cached for the lifetime of the
    /// document. For mutating workflows that need to re-read the info
    /// dictionary after modifying it, use <see cref="InfoDictionary"/>
    /// directly.
    /// </para>
    /// </remarks>
    public DocumentInfo Info
    {
        get
        {
            if (_documentInfo is null)
            {
                _documentInfo = BuildDocumentInfo();
            }

            return _documentInfo;
        }
    }

    /// <summary>
    /// Gets the underlying object store for direct object access.
    /// </summary>
    public PdfObjectStore Objects => _reader.Objects;

    /// <summary>
    /// Gets the underlying <see cref="PdfReader"/> for low-level access such as
    /// reading raw file bytes for signature byte-range extraction.
    /// </summary>
    public PdfReader Reader => _reader;

    // ── IDisposable ───────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _reader.Dispose();
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private PdfDictionary GetPagesRoot()
    {
        PdfDictionary catalog = Catalog;

        if (!catalog.TryGetValue(PdfName.Pages, out PdfPrimitive? pagesRef))
        {
            throw new PdfCorruptionException(
                "Document Catalog is missing the required /Pages entry.");
        }

        PdfPrimitive resolved = _reader.Objects.Resolve(pagesRef);

        if (resolved is not PdfDictionary pagesDict)
        {
            throw new PdfCorruptionException(
                "The /Pages entry in the Catalog does not resolve to a dictionary.");
        }

        return pagesDict;
    }

    private string? GetInfoString(PdfName key)
    {
        PdfDictionary? info = _reader.Info;

        if (info is null)
        {
            return null;
        }

        PdfString? value = info.GetAs<PdfString>(key);

        if (value is null)
        {
            return null;
        }

        return value.ToTextString();
    }

    private DocumentInfo BuildDocumentInfo()
    {
        DateTimeOffset? creation = TryGetInfoDate(PdfName.Intern("CreationDate"));
        DateTimeOffset? modified = TryGetInfoDate(PdfName.Intern("ModDate"));
        string? version = GetPdfVersion();
        EncryptionInfo encryption = BuildEncryptionInfo();

        return new DocumentInfo(
            title: Title,
            author: Author,
            subject: Subject,
            keywords: Keywords,
            creator: Creator,
            producer: Producer,
            creationDate: creation,
            modificationDate: modified,
            pdfVersion: version,
            pageCount: PageCount,
            fileSize: null,
            encryption: encryption);
    }

    private DateTimeOffset? TryGetInfoDate(PdfName key)
    {
        string? raw = GetInfoString(key);

        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        return TryParsePdfDate(raw);
    }

    /// <summary>
    /// Parses a PDF date string in the format
    /// <c>D:YYYYMMDDHHmmSSOHH'mm'</c> per PDF 32000-1:2008 §7.9.4.
    /// Returns null when the string is malformed.
    /// </summary>
    private static DateTimeOffset? TryParsePdfDate(string value)
    {
        // Strip the optional D: prefix.
        string s = value.StartsWith("D:", StringComparison.Ordinal)
            ? value.Substring(2)
            : value;

        if (s.Length < 4)
        {
            return null;
        }

        // Defaults per spec when later fields are absent.
        int year;
        int month = 1;
        int day = 1;
        int hour = 0;
        int minute = 0;
        int second = 0;
        TimeSpan offset = TimeSpan.Zero;

        if (!TryParseInt(s, 0, 4, out year))
        {
            return null;
        }

        if (s.Length >= 6 && !TryParseInt(s, 4, 2, out month))
        {
            return null;
        }

        if (s.Length >= 8 && !TryParseInt(s, 6, 2, out day))
        {
            return null;
        }

        if (s.Length >= 10 && !TryParseInt(s, 8, 2, out hour))
        {
            return null;
        }

        if (s.Length >= 12 && !TryParseInt(s, 10, 2, out minute))
        {
            return null;
        }

        if (s.Length >= 14 && !TryParseInt(s, 12, 2, out second))
        {
            return null;
        }

        // Timezone marker, if present, is at index 14.
        if (s.Length >= 15)
        {
            char tz = s[14];

            if (tz == 'Z')
            {
                offset = TimeSpan.Zero;
            }
            else if (tz == '+' || tz == '-')
            {
                int sign = tz == '+' ? 1 : -1;
                int hh = 0;
                int mm = 0;

                if (s.Length >= 17 && !TryParseInt(s, 15, 2, out hh))
                {
                    return null;
                }

                // The minutes field is bracketed by apostrophes in the spec
                // form: "+05'30'". Allow either "+0530" or "+05'30'".
                int mmStart = -1;

                if (s.Length >= 20 && s[17] == '\'' && s[20] == '\'')
                {
                    mmStart = 18;
                }
                else if (s.Length >= 19)
                {
                    mmStart = 17;
                }

                if (mmStart >= 0 && mmStart + 2 <= s.Length &&
                    !TryParseInt(s, mmStart, 2, out mm))
                {
                    return null;
                }

                offset = new TimeSpan(sign * hh, sign * mm, 0);
            }
        }

        try
        {
            return new DateTimeOffset(year, month, day, hour, minute, second, offset);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static bool TryParseInt(string s, int start, int length, out int value)
    {
        return int.TryParse(
            s.AsSpan(start, length),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out value);
    }

    private string? GetPdfVersion()
    {
        if (_pdfVersionProbed)
        {
            return _pdfVersionCache;
        }

        _pdfVersionProbed = true;

        try
        {
            byte[] header = _reader.ReadFileBytes(0, 16);
            string headerText = Encoding.ASCII.GetString(header);
            int sigIdx = headerText.IndexOf("%PDF-", StringComparison.Ordinal);

            if (sigIdx < 0 || sigIdx + 8 > headerText.Length)
            {
                _pdfVersionCache = null;
                return null;
            }

            // After "%PDF-" the version is at most "X.Y" — 3 characters.
            int versionStart = sigIdx + 5;
            int versionEnd = versionStart;

            while (versionEnd < headerText.Length &&
                   versionEnd < versionStart + 3 &&
                   (char.IsDigit(headerText[versionEnd]) || headerText[versionEnd] == '.'))
            {
                versionEnd++;
            }

            if (versionEnd == versionStart)
            {
                _pdfVersionCache = null;
                return null;
            }

            _pdfVersionCache = headerText.Substring(versionStart, versionEnd - versionStart);
            return _pdfVersionCache;
        }
        catch (IOException)
        {
            _pdfVersionCache = null;
            return null;
        }
        catch (ArgumentException)
        {
            _pdfVersionCache = null;
            return null;
        }
    }

    private EncryptionInfo BuildEncryptionInfo()
    {
        if (!_reader.Trailer.TryGetValue(PdfName.Intern("Encrypt"), out PdfPrimitive? encPrim))
        {
            return EncryptionInfo.None;
        }

        PdfPrimitive resolved = _reader.Objects.Resolve(encPrim);

        if (resolved is not PdfDictionary encDict)
        {
            return EncryptionInfo.None;
        }

        int v = GetEncInt(encDict, PdfName.Intern("V"), defaultValue: 0);
        int r = GetEncInt(encDict, PdfName.Intern("R"), defaultValue: 0);
        int length = GetEncInt(encDict, PdfName.Intern("Length"), defaultValue: 0);
        int pFlags = GetEncInt(encDict, PdfName.Intern("P"), defaultValue: 0);

        string? algorithm = ClassifyAlgorithm(v, r, length);
        int? keyLength = length > 0 ? length : DefaultKeyLength(v, r);
        PdfPermissions permissions = DecodePermissions(pFlags);

        return new EncryptionInfo(
            isEncrypted: true,
            algorithm: algorithm,
            keyLengthBits: keyLength,
            permissions: permissions);
    }

    private int GetEncInt(PdfDictionary dict, PdfName key, int defaultValue)
    {
        if (!dict.TryGetValue(key, out PdfPrimitive? prim))
        {
            return defaultValue;
        }

        PdfPrimitive resolved = _reader.Objects.Resolve(prim);
        return resolved is PdfInteger i ? i.Value : defaultValue;
    }

    private static string? ClassifyAlgorithm(int v, int r, int length)
    {
        // PDF 32000-1:2008 §7.6.1 + ISO 32000-2:2020 §7.6 — algorithm matrix.
        if (v == 1)
        {
            return "RC4-40";
        }

        if (v == 2)
        {
            return length >= 128 ? "RC4-128" : "RC4-40";
        }

        if (v == 4)
        {
            // /CFM determines actual cipher; default to AES-128 since v=4 is
            // dominantly AES in practice. RC4 with v=4 is rare and legacy.
            return "AES-128";
        }

        if (v == 5 || r == 6)
        {
            return "AES-256";
        }

        return null;
    }

    private static int? DefaultKeyLength(int v, int r)
    {
        if (v == 1)
        {
            return 40;
        }

        if (v == 2 || v == 4)
        {
            return 128;
        }

        if (v == 5 || r == 6)
        {
            return 256;
        }

        return null;
    }

    /// <summary>
    /// Decodes the PDF /P permissions bitfield. Per PDF 32000-1:2008
    /// §7.6.3.2 the value is a signed 32-bit integer; bits 1, 2, 7, and 8
    /// are reserved; bits 13..32 must be 1. Granted permissions correspond
    /// to bits set to 1.
    /// </summary>
    private static PdfPermissions DecodePermissions(int pFlags)
    {
        PdfPermissions result = PdfPermissions.None;

        // PDF bit numbers are 1-based. Bit 3 = bit index 2 (value 0x04).
        if ((pFlags & (1 << 2)) != 0)
        {
            result |= PdfPermissions.Print;
        }

        if ((pFlags & (1 << 3)) != 0)
        {
            result |= PdfPermissions.ModifyContents;
        }

        if ((pFlags & (1 << 4)) != 0)
        {
            result |= PdfPermissions.CopyContents;
        }

        if ((pFlags & (1 << 5)) != 0)
        {
            result |= PdfPermissions.ModifyAnnotations;
        }

        if ((pFlags & (1 << 8)) != 0)
        {
            result |= PdfPermissions.FillForms;
        }

        if ((pFlags & (1 << 9)) != 0)
        {
            result |= PdfPermissions.ExtractAccessibility;
        }

        if ((pFlags & (1 << 10)) != 0)
        {
            result |= PdfPermissions.Assemble;
        }

        if ((pFlags & (1 << 11)) != 0)
        {
            result |= PdfPermissions.PrintHighQuality;
        }

        return result;
    }
}
