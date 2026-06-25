// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.7.2 — Document Catalog
//        PDF 32000-1:2008 §14.3.3 — Document information dictionary
//        PDF 32000-1:2008 §7.9.4 — Date strings
//        PDF 32000-1:2008 §7.6 — Encryption
// PHASE: Phase 1 — Chuvadi.Pdf.Documents
// High-level document model over a PdfReader.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Chuvadi.Pdf.Encryption;
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

    // ── Document metadata ─────────────────────────────────────────────────

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

    /// <summary>
    /// Gets the date and time the document was created, or null when not
    /// set or unparsable. PDF 32000-1:2008 §14.3.3, Table 317 — CreationDate.
    /// </summary>
    /// <remarks>
    /// The /CreationDate entry is a PDF date string per §7.9.4 of the form
    /// <c>D:YYYYMMDDHHmmSSOHH'mm'</c>. Missing trailing fields default to zero;
    /// a missing timezone offset is treated as UTC.
    /// </remarks>
    public DateTimeOffset? CreationDate => TryParseDate(GetInfoString(PdfName.Intern("CreationDate")));

    /// <summary>
    /// Gets the date and time the document was last modified, or null when
    /// not set or unparsable. PDF 32000-1:2008 §14.3.3, Table 317 — ModDate.
    /// </summary>
    /// <remarks>
    /// Same format and parsing semantics as <see cref="CreationDate"/>.
    /// </remarks>
    public DateTimeOffset? ModDate => TryParseDate(GetInfoString(PdfName.Intern("ModDate")));

    /// <summary>
    /// Gets the /Trapped entry indicating whether the document has been
    /// modified to include trapping information.
    /// </summary>
    /// <remarks>
    /// Returns the name as a string (typically <c>"True"</c>, <c>"False"</c>,
    /// or <c>"Unknown"</c>) or null when absent. Some producers erroneously
    /// store this entry as a PDF string instead of a PDF name; both forms
    /// are accepted.
    /// </remarks>
    public string? Trapped => GetInfoName(PdfName.Intern("Trapped"));

    /// <summary>
    /// Gets the XMP metadata stream bytes, or null when the document has no
    /// /Metadata entry in its Catalog. PDF 32000-1:2008 §14.3.2 — Metadata streams.
    /// </summary>
    /// <remarks>
    /// Returns the raw stream bytes as they appear in the file. The XMP
    /// specification recommends that metadata streams be uncompressed for
    /// searchability; if a producer has chosen to apply a filter, the
    /// returned bytes will be in their filtered form. Callers needing the
    /// decoded form can read <see cref="Catalog"/>'s /Metadata entry directly
    /// and apply the appropriate filter.
    /// </remarks>
    public byte[]? XmpMetadata => GetXmpMetadata();

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
                // Determine the highest object number from the trailer's /Size.
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

    /// <summary>
    /// Returns true when the document is an XFA form, i.e. its
    /// <c>/AcroForm</c> dictionary carries an <c>/XFA</c> entry. XFA content
    /// lives outside standard page content, so rendering such a document
    /// produces an essentially empty page; consumers can use this flag to show
    /// a notice instead of a blank page. Equivalent to
    /// <c>XfaKind != XfaKind.None</c>; see <see cref="XfaKind"/> to distinguish
    /// static, hybrid, and dynamic XFA.
    /// </summary>
    public bool IsXfa => XfaKind != XfaKind.None;

    /// <summary>
    /// Classifies the document's use of XFA (none, static, hybrid, or dynamic),
    /// so a consumer can tell forms that render from the page content apart from
    /// dynamic XFA that needs a processor. PDF 32000-1:2008 §12.7.8.
    /// </summary>
    public XfaKind XfaKind { get => ComputeXfaKind(); }

    private XfaPackets? _xfa;
    private bool _xfaProbed;

    /// <summary>
    /// Gets the document's XFA (XML Forms Architecture) packets and data layer,
    /// or null when the document has no XFA form (<see cref="XfaKind"/> is
    /// <see cref="XfaKind.None"/>). Exposes the raw packets (template, datasets,
    /// config, …) and, from the datasets packet, the data layer as
    /// <see cref="XfaDataField"/> values with best-effort widget geometry. The
    /// result is computed once and cached. PDF 32000-1:2008 §12.7.8.
    /// </summary>
    public XfaPackets? Xfa
    {
        get
        {
            if (!_xfaProbed)
            {
                _xfa = XfaPackets.TryRead(this);
                _xfaProbed = true;
            }

            return _xfa;
        }
    }

    /// <summary>
    /// Gets the document's structure-tree root dictionary (the catalog's
    /// <c>/StructTreeRoot</c>), or null when the document has no resolvable
    /// structure tree. PDF 32000-1:2008 §14.7.2 — Structure Hierarchy.
    /// </summary>
    public PdfDictionary? StructTreeRoot { get => ResolveCatalogDictionary("StructTreeRoot"); }

    /// <summary>
    /// Returns true when the document carries a structure tree (a resolvable
    /// <c>/StructTreeRoot</c> in the catalog). Independent of <see cref="IsTagged"/>:
    /// a file may carry a structure tree without formally declaring itself tagged,
    /// and a <c>/StructTreeRoot</c> that resolves to nothing reports false here.
    /// </summary>
    public bool HasStructTree => StructTreeRoot is not null;

    /// <summary>
    /// Returns true when the document declares itself a Tagged PDF, i.e. its
    /// catalog <c>/MarkInfo</c> dictionary has <c>/Marked true</c>.
    /// PDF 32000-1:2008 §14.8 — Tagged PDF.
    /// </summary>
    public bool IsTagged { get => MarkInfoIsMarked(); }

    private XfaKind ComputeXfaKind()
    {
        PdfDictionary? acro = ResolveAcroForm();
        if (acro is null || !acro.ContainsKey(PdfName.Intern("XFA")))
        {
            return XfaKind.None;
        }

        if (CatalogFlagIsTrue("NeedsRendering"))
        {
            return XfaKind.Dynamic;
        }

        return AcroFormHasFields(acro) ? XfaKind.Hybrid : XfaKind.Static;
    }

    private bool MarkInfoIsMarked()
    {
        PdfDictionary? mark = ResolveCatalogDictionary("MarkInfo");
        return mark is not null
            && mark.TryGetValue(PdfName.Intern("Marked"), out PdfPrimitive? marked)
            && marked is PdfBoolean flag
            && flag.Value;
    }

    private PdfDictionary? ResolveCatalogDictionary(string key)
    {
        PdfDictionary? catalog = _reader.Catalog;
        if (catalog is null)
        {
            return null;
        }

        if (!catalog.TryGetValue(PdfName.Intern(key), out PdfPrimitive? value))
        {
            return null;
        }

        return Objects.ResolveAs<PdfDictionary>(value);
    }

    private PdfDictionary? ResolveAcroForm() => ResolveCatalogDictionary("AcroForm");

    private bool CatalogFlagIsTrue(string key)
    {
        PdfDictionary? catalog = _reader.Catalog;
        return catalog is not null
            && catalog.TryGetValue(PdfName.Intern(key), out PdfPrimitive? value)
            && Objects.Resolve(value) is PdfBoolean flag
            && flag.Value;
    }

    private bool AcroFormHasFields(PdfDictionary acroForm)
    {
        if (!acroForm.TryGetValue(PdfName.Intern("Fields"), out PdfPrimitive? fieldsPrim))
        {
            return false;
        }

        PdfArray? fields = Objects.ResolveAs<PdfArray>(fieldsPrim);
        return fields is not null && fields.Count > 0;
    }

    private LinearizationInfo? _linearization;
    private bool _linearizationProbed;

    /// <summary>
    /// Gets the raw document information dictionary, or null when absent.
    /// </summary>
    public PdfDictionary? Info => _reader.Info;

    /// <summary>
    /// Gets the encryption properties of this document, or null when the
    /// document is not encrypted. PDF 32000-1:2008 §7.6 — Encryption.
    /// </summary>
    /// <remarks>
    /// Parses the trailer's /Encrypt entry into a strongly-typed view. The
    /// document has been successfully opened, so when this returns non-null,
    /// the encryption was understood and decryption is in effect for all
    /// subsequent object access. Returns null when no /Encrypt entry is
    /// present (the document is not encrypted) or when the security handler
    /// is recognised by the reader but not exposed via this typed view.
    /// </remarks>
    public EncryptionInfo? Encryption
    {
        get
        {
            if (!_reader.Trailer.TryGetValue(PdfName.Intern("Encrypt"), out PdfPrimitive? encryptPrim))
            {
                return null;
            }

            PdfDictionary? encryptDict = _reader.Objects.ResolveAs<PdfDictionary>(encryptPrim);

            if (encryptDict is null)
            {
                return null;
            }

            EncryptionDictionary? parsed = EncryptionDictionary.Parse(encryptDict);

            if (parsed is null)
            {
                return null;
            }

            return new EncryptionInfo(
                algorithm: parsed.Algorithm,
                keyLength: parsed.KeyBytes,
                revision: parsed.R,
                version: parsed.V,
                permissions: parsed.Permissions,
                encryptMetadata: parsed.EncryptMetadata);
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

    /// <summary>
    /// Gets warnings raised while opening the document. A non-empty list means
    /// the file was structurally repaired during loading — for example, a
    /// page-tree object recovered from a corrupt cross-reference table. The
    /// document is usable, and writing it back out (merge, reorder, extract,
    /// save) emits a clean, repaired file.
    /// </summary>
    public IReadOnlyList<string> Warnings => _reader.Warnings;

    /// <summary>
    /// Gets a value indicating whether the document was opened in a repaired
    /// state. Equivalent to <see cref="Warnings"/> being non-empty.
    /// </summary>
    public bool IsRecovered => _reader.Warnings.Count > 0;

    // ── Saving ────────────────────────────────────────────────────────────

    /// <summary>
    /// Re-serializes the document to <paramref name="output"/>, optionally
    /// encrypting it. Passing <see langword="null"/> writes an unencrypted copy
    /// (decrypting the document when it was opened from an encrypted file);
    /// passing an <see cref="EncryptionOptions"/> (for example
    /// <see cref="EncryptionOptions.Aes256(string, string?)"/>, the recommended
    /// AES-256 / V5 / R6 scheme) writes an encrypted file.
    /// PDF 32000-1:2008 §7.6 — encryption.
    /// </summary>
    /// <param name="output">The stream the document is written to.</param>
    /// <param name="encryption">The encryption options, or null for no encryption.</param>
    public void Save(Stream output, EncryptionOptions? encryption = null)
    {
        ArgumentNullException.ThrowIfNull(output);

        PdfDictionary trailer = new PdfDictionary();
        if (Trailer.TryGetValue(PdfName.Root, out PdfPrimitive? root))
        {
            trailer.Set(PdfName.Root, root);
        }

        if (Trailer.TryGetValue(PdfName.Info, out PdfPrimitive? info))
        {
            trailer.Set(PdfName.Info, info);
        }

        PdfWriter.Write(output, Objects.Objects, trailer, encryption);
    }

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

    private string? GetInfoName(PdfName key)
    {
        PdfDictionary? info = _reader.Info;

        if (info is null)
        {
            return null;
        }

        if (!info.TryGetValue(key, out PdfPrimitive? value))
        {
            return null;
        }

        return value switch
        {
            PdfName n => n.Value,
            PdfString s => s.ToTextString(),
            _ => null,
        };
    }

    private byte[]? GetXmpMetadata()
    {
        PdfDictionary? catalog = _reader.Catalog;

        if (catalog is null)
        {
            return null;
        }

        if (!catalog.TryGetValue(PdfName.Intern("Metadata"), out PdfPrimitive? metaRef))
        {
            return null;
        }

        PdfStream? stream = _reader.Objects.ResolveAs<PdfStream>(metaRef);

        if (stream is null)
        {
            return null;
        }

        return stream.RawBytes;
    }

    /// <summary>
    /// Parses a PDF date string per §7.9.4 into a <see cref="DateTimeOffset"/>.
    /// Returns null when the input is missing or unparsable.
    /// </summary>
    private static DateTimeOffset? TryParseDate(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        string s = raw;

        if (s.StartsWith("D:", StringComparison.Ordinal))
        {
            s = s.Substring(2);
        }

        if (s.Length < 4)
        {
            return null;
        }

        try
        {
            int year = int.Parse(s.AsSpan(0, 4), CultureInfo.InvariantCulture);
            int month = s.Length >= 6 ? int.Parse(s.AsSpan(4, 2), CultureInfo.InvariantCulture) : 1;
            int day = s.Length >= 8 ? int.Parse(s.AsSpan(6, 2), CultureInfo.InvariantCulture) : 1;
            int hour = s.Length >= 10 ? int.Parse(s.AsSpan(8, 2), CultureInfo.InvariantCulture) : 0;
            int minute = s.Length >= 12 ? int.Parse(s.AsSpan(10, 2), CultureInfo.InvariantCulture) : 0;
            int second = s.Length >= 14 ? int.Parse(s.AsSpan(12, 2), CultureInfo.InvariantCulture) : 0;

            TimeSpan offset = TimeSpan.Zero;
            int pos = 14;

            if (pos < s.Length)
            {
                char sign = s[pos];

                if (sign == 'Z')
                {
                    offset = TimeSpan.Zero;
                }
                else if ((sign == '+' || sign == '-') && pos + 3 <= s.Length)
                {
                    int sgn = sign == '+' ? 1 : -1;
                    int oh = int.Parse(s.AsSpan(pos + 1, 2), CultureInfo.InvariantCulture);
                    int om = 0;
                    int oPos = pos + 3;

                    if (oPos < s.Length && s[oPos] == '\'')
                    {
                        oPos++;
                    }

                    if (oPos + 2 <= s.Length)
                    {
                        if (!int.TryParse(s.AsSpan(oPos, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out om))
                        {
                            om = 0;
                        }
                    }

                    offset = new TimeSpan(sgn * oh, sgn * om, 0);
                }
            }

            return new DateTimeOffset(year, month, day, hour, minute, second, offset);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
