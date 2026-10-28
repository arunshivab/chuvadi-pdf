// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;

namespace Chuvadi.Pdf.Primitives;

/// <summary>
/// A forward-only, byte-level tokenizer for PDF streams.
/// </summary>
/// <remarks>
/// The tokenizer reads bytes from a <see cref="Stream"/> and produces
/// a sequence of <see cref="PdfToken"/> values. It is the lowest layer
/// of the Chuvadi parser stack — it knows nothing about PDF object
/// structure, only about lexical tokens.
///
/// Calling conventions:
/// <list type="bullet">
///   <item>Call <see cref="Read"/> to advance to the next token.</item>
///   <item>Call <see cref="Unread"/> to push the last token back; the next
///         <see cref="Read"/> will return it again. Only one token of
///         pushback is supported.</item>
///   <item>The tokenizer is not thread-safe.</item>
/// </list>
///
/// The tokenizer is allocation-conscious: it reuses an internal buffer
/// for token bytes. Callers that need to keep token bytes beyond the next
/// <see cref="Read"/> call must copy <see cref="PdfToken.RawBytes"/>.
///
/// PDF 32000-1:2008 §7.2 — Lexical conventions.
/// </remarks>
public sealed class PdfTokenizer : IDisposable
{
    // Internal read buffer — we read in chunks to avoid one-byte reads.
    private const int BufferSize = 4096;

    // Maximum token size we will buffer before throwing.
    // Protects against malformed PDFs with pathologically large tokens.
    private const int MaxTokenBytes = 10 * 1024 * 1024; // 10 MB

    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly byte[] _readBuffer;

    // Current position within _readBuffer.
    private int _bufferPos;

    // Number of valid bytes currently in _readBuffer.
    private int _bufferLen;

    // Byte offset of _readBuffer[0] within the underlying stream.
    private long _bufferStreamOffset;

    // True when the underlying stream is exhausted.
    private bool _streamExhausted;

    // One-token pushback.
    private PdfToken _pushedBack;
    private bool _hasPushedBack;

    // Reusable scratch buffer for building token bytes.
    private readonly List<byte> _tokenBytes;

    private bool _disposed;

    /// <summary>
    /// Initialises a new <see cref="PdfTokenizer"/> over the given stream.
    /// </summary>
    /// <param name="stream">
    /// The PDF byte stream to tokenize. Must be readable.
    /// </param>
    /// <param name="leaveOpen">
    /// True to leave <paramref name="stream"/> open when this tokenizer
    /// is disposed; false to close it.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="stream"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="stream"/> is not readable.
    /// </exception>
    public PdfTokenizer(Stream stream, bool leaveOpen = false)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        if (!stream.CanRead)
        {
            throw new ArgumentException("Stream must be readable.", nameof(stream));
        }

        _stream = stream;
        _leaveOpen = leaveOpen;
        _readBuffer = new byte[BufferSize];
        _bufferPos = 0;
        _bufferLen = 0;
        _bufferStreamOffset = 0;
        _streamExhausted = false;
        _tokenBytes = new List<byte>(256);
        _pushedBack = PdfToken.EndOfStream;
        _hasPushedBack = false;
    }

    /// <summary>
    /// Gets the current byte offset in the underlying stream.
    /// This is the position of the byte that will be read next.
    /// </summary>
    public long Position => _bufferStreamOffset + _bufferPos;

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Reads and returns the next token from the stream.
    /// Returns <see cref="PdfToken.EndOfStream"/> when there are no more tokens.
    /// </summary>
    /// <exception cref="PdfParseException">
    /// Thrown when the stream contains bytes that cannot form a valid token.
    /// </exception>
    public PdfToken Read()
    {
        ThrowIfDisposed();

        if (_hasPushedBack)
        {
            _hasPushedBack = false;
            return _pushedBack;
        }

        return ReadNextToken();
    }

    /// <summary>
    /// Pushes the given token back so that the next call to <see cref="Read"/>
    /// returns it again.
    /// </summary>
    /// <param name="token">The token to push back.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a token has already been pushed back without being consumed.
    /// </exception>
    public void Unread(PdfToken token)
    {
        ThrowIfDisposed();

        if (_hasPushedBack)
        {
            throw new InvalidOperationException(
                "Cannot push back more than one token.");
        }

        _pushedBack = token;
        _hasPushedBack = true;
    }

    /// <summary>
    /// Reads tokens until a token of the given type is found, then returns it.
    /// Skips all intervening tokens.
    /// Returns <see cref="PdfToken.EndOfStream"/> if the type is not found.
    /// </summary>
    public PdfToken ReadUntil(PdfTokenType type)
    {
        ThrowIfDisposed();

        while (true)
        {
            PdfToken token = Read();

            if (token.Type == type || token.IsEndOfStream)
            {
                return token;
            }
        }
    }

    /// <summary>
    /// Seeks the underlying stream to the given byte offset and resets
    /// the tokenizer's internal buffer.
    /// </summary>
    /// <param name="offset">The byte offset to seek to.</param>
    /// <exception cref="NotSupportedException">
    /// Thrown when the underlying stream does not support seeking.
    /// </exception>
    public void Seek(long offset)
    {
        ThrowIfDisposed();

        _stream.Seek(offset, SeekOrigin.Begin);
        _bufferPos = 0;
        _bufferLen = 0;
        _bufferStreamOffset = offset;
        _streamExhausted = false;
        _hasPushedBack = false;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (!_leaveOpen)
        {
            _stream.Dispose();
        }
    }

    // ── Core tokenizer ────────────────────────────────────────────────────

    private PdfToken ReadNextToken()
    {
        // Skip whitespace and comments.
        SkipWhitespaceAndComments();

        long tokenStart = Position;

        int b = PeekByte();

        if (b == -1)
        {
            return PdfToken.EndOfStream;
        }

        // Dispatch on the first byte.
        return (byte)b switch
        {
            // Delimiter: start of literal string
            (byte)'(' => ReadLiteralString(tokenStart),

            // Delimiter: start of hex string or dictionary
            (byte)'<' => ReadAngleBracketToken(tokenStart),

            // Delimiter: end of dictionary
            (byte)'>' => ReadDictionaryEnd(tokenStart),

            // Delimiter: start of array
            (byte)'[' => ReadSingleByteToken(PdfTokenType.ArrayStart, tokenStart),

            // Delimiter: end of array
            (byte)']' => ReadSingleByteToken(PdfTokenType.ArrayEnd, tokenStart),

            // Name: starts with solidus
            (byte)'/' => ReadName(tokenStart),

            // Numeric: digit, plus, minus, or decimal point
            (byte)'+' or (byte)'-' or (byte)'.' => ReadNumber(tokenStart),
            >= (byte)'0' and <= (byte)'9' => ReadNumber(tokenStart),

            // Keyword or special token
            _ => ReadKeywordOrSpecial(tokenStart)
        };
    }

    // ── Whitespace and comments ───────────────────────────────────────────

    private const int SkipCheckInterval = 1 << 20;
    private int _skippedSinceCheck;

    // Counts skipped bytes across calls (one comment line at a time would
    // never reach the interval otherwise) and checks cancellation every MiB.
    private void CountSkipped()
    {
        if (++_skippedSinceCheck >= SkipCheckInterval)
        {
            _skippedSinceCheck = 0;
            PdfWorkScope.ThrowIfCancellationRequested();
        }
    }

    private void SkipWhitespaceAndComments()
    {
        while (true)
        {
            int b = PeekByte();

            if (b == -1)
            {
                break;
            }

            // Megabytes of blank space or comments (a decompression bomb)
            // hold no token to check cancellation at: check here as well.
            CountSkipped();

            if (IsWhitespace((byte)b))
            {
                ConsumeByte();
                continue;
            }

            // PDF 32000-1:2008 §7.2.3 — Comments begin with % and run to end of line.
            if (b == (byte)'%')
            {
                ConsumeByte();
                SkipToEndOfLine();
                continue;
            }

            break;
        }
    }

    private void SkipToEndOfLine()
    {
        while (true)
        {
            CountSkipped();
            int b = PeekByte();

            if (b == -1 || b == '\r' || b == '\n')
            {
                break;
            }

            ConsumeByte();
        }
    }

    // ── Literal string ────────────────────────────────────────────────────

    private PdfToken ReadLiteralString(long tokenStart)
    {
        // PDF 32000-1:2008 §7.3.4.2
        // Literal strings are enclosed in balanced parentheses.
        // Backslash escapes and nested balanced parentheses are allowed.
        _tokenBytes.Clear();
        _tokenBytes.Add((byte)'(');

        ConsumeByte(); // consume opening '('

        int depth = 1;

        while (depth > 0)
        {
            int b = ReadByte();

            if (b == -1)
            {
                throw new PdfParseException(
                    "Unexpected end of stream inside literal string.",
                    tokenStart);
            }

            _tokenBytes.Add((byte)b);

            if (b == (byte)'\\')
            {
                // Escaped character: consume the next byte unconditionally.
                int escaped = ReadByte();

                if (escaped != -1)
                {
                    _tokenBytes.Add((byte)escaped);
                }
            }
            else if (b == (byte)'(')
            {
                depth++;
            }
            else if (b == (byte)')')
            {
                depth--;
            }
        }

        return new PdfToken(PdfTokenType.LiteralString, [.. _tokenBytes], tokenStart);
    }

    // ── Angle bracket: hex string or dictionary delimiter ─────────────────

    private PdfToken ReadAngleBracketToken(long tokenStart)
    {
        ConsumeByte(); // consume first '<'

        int next = PeekByte();

        if (next == (byte)'<')
        {
            // Dictionary start: <<
            ConsumeByte();
            return new PdfToken(PdfTokenType.DictionaryStart, [(byte)'<', (byte)'<'], tokenStart);
        }

        // Hex string: <hexdigits>
        return ReadHexString(tokenStart);
    }

    private PdfToken ReadHexString(long tokenStart)
    {
        // PDF 32000-1:2008 §7.3.4.3
        // Hex strings contain hex digits and whitespace, terminated by '>'.
        _tokenBytes.Clear();
        _tokenBytes.Add((byte)'<');

        while (true)
        {
            int b = ReadByte();

            if (b == -1)
            {
                throw new PdfParseException(
                    "Unexpected end of stream inside hex string.",
                    tokenStart);
            }

            _tokenBytes.Add((byte)b);

            if (b == (byte)'>')
            {
                break;
            }
        }

        return new PdfToken(PdfTokenType.HexString, [.. _tokenBytes], tokenStart);
    }

    // ── Dictionary end ────────────────────────────────────────────────────

    private PdfToken ReadDictionaryEnd(long tokenStart)
    {
        ConsumeByte(); // consume first '>'

        int next = PeekByte();

        if (next == (byte)'>')
        {
            ConsumeByte();
            return new PdfToken(PdfTokenType.DictionaryEnd, [(byte)'>', (byte)'>'], tokenStart);
        }

        // A lone '>' is not valid PDF syntax outside a hex string.
        // We return it as a keyword so the parser can produce a better error.
        return new PdfToken(PdfTokenType.Keyword, [(byte)'>'], tokenStart);
    }

    // ── Single-byte delimiter ─────────────────────────────────────────────

    private PdfToken ReadSingleByteToken(PdfTokenType type, long tokenStart)
    {
        byte b = (byte)ReadByte()!;
        return new PdfToken(type, [b], tokenStart);
    }

    // ── Name ──────────────────────────────────────────────────────────────

    private PdfToken ReadName(long tokenStart)
    {
        // PDF 32000-1:2008 §7.3.5
        // Names begin with '/' and continue until a delimiter or whitespace.
        // We do NOT include the leading '/' in the raw bytes — PdfName.FromRawBytes
        // expects just the name content.
        ConsumeByte(); // consume '/'

        _tokenBytes.Clear();

        while (true)
        {
            int b = PeekByte();

            if (b == -1 || IsWhitespace((byte)b) || IsDelimiter((byte)b))
            {
                break;
            }

            _tokenBytes.Add((byte)b);
            ConsumeByte();
        }

        return new PdfToken(PdfTokenType.Name, [.. _tokenBytes], tokenStart);
    }

    // ── Number ────────────────────────────────────────────────────────────

    private PdfToken ReadNumber(long tokenStart)
    {
        // PDF 32000-1:2008 §7.3.3
        // Numbers: optional sign, digits, optional decimal point, more digits.
        _tokenBytes.Clear();

        bool isReal = false;

        // Optional sign
        int first = PeekByte();

        if (first == (byte)'+' || first == (byte)'-')
        {
            _tokenBytes.Add((byte)first);
            ConsumeByte();
        }

        // Digits before decimal point
        while (true)
        {
            int b = PeekByte();

            if (b >= (byte)'0' && b <= (byte)'9')
            {
                _tokenBytes.Add((byte)b);
                ConsumeByte();
            }
            else
            {
                break;
            }
        }

        // Optional decimal point
        if (PeekByte() == (byte)'.')
        {
            isReal = true;
            _tokenBytes.Add((byte)'.');
            ConsumeByte();

            // Digits after decimal point
            while (true)
            {
                int b = PeekByte();

                if (b >= (byte)'0' && b <= (byte)'9')
                {
                    _tokenBytes.Add((byte)b);
                    ConsumeByte();
                }
                else
                {
                    break;
                }
            }
        }

        PdfTokenType type = isReal ? PdfTokenType.Real : PdfTokenType.Integer;
        return new PdfToken(type, [.. _tokenBytes], tokenStart);
    }

    // ── Keyword or special token ──────────────────────────────────────────

    private PdfToken ReadKeywordOrSpecial(long tokenStart)
    {
        // Read until delimiter or whitespace.
        _tokenBytes.Clear();

        while (true)
        {
            int b = PeekByte();

            if (b == -1 || IsWhitespace((byte)b) || IsDelimiter((byte)b))
            {
                break;
            }

            _tokenBytes.Add((byte)b);
            ConsumeByte();

            if (_tokenBytes.Count > MaxTokenBytes)
            {
                throw new PdfParseException(
                    $"Token exceeds maximum size of {MaxTokenBytes} bytes.",
                    tokenStart);
            }
        }

        if (_tokenBytes.Count == 0)
        {
            // Should not happen — we peeked a non-whitespace byte above.
            return PdfToken.EndOfStream;
        }

        return ClassifyKeyword([.. _tokenBytes], tokenStart);
    }

    private static PdfToken ClassifyKeyword(byte[] bytes, long tokenStart)
    {
        // Match known PDF keywords.
        // Using span comparison to avoid string allocations on the hot path.
        ReadOnlySpan<byte> span = bytes;

        if (span.SequenceEqual("true"u8))
        {
            return new PdfToken(PdfTokenType.True, bytes, tokenStart);
        }

        if (span.SequenceEqual("false"u8))
        {
            return new PdfToken(PdfTokenType.False, bytes, tokenStart);
        }

        if (span.SequenceEqual("null"u8))
        {
            return new PdfToken(PdfTokenType.Null, bytes, tokenStart);
        }

        if (span.SequenceEqual("R"u8))
        {
            return new PdfToken(PdfTokenType.Reference, bytes, tokenStart);
        }

        if (span.SequenceEqual("obj"u8))
        {
            return new PdfToken(PdfTokenType.ObjectStart, bytes, tokenStart);
        }

        if (span.SequenceEqual("endobj"u8))
        {
            return new PdfToken(PdfTokenType.ObjectEnd, bytes, tokenStart);
        }

        if (span.SequenceEqual("stream"u8))
        {
            return new PdfToken(PdfTokenType.StreamStart, bytes, tokenStart);
        }

        if (span.SequenceEqual("endstream"u8))
        {
            return new PdfToken(PdfTokenType.StreamEnd, bytes, tokenStart);
        }

        if (span.SequenceEqual("xref"u8))
        {
            return new PdfToken(PdfTokenType.XRef, bytes, tokenStart);
        }

        if (span.SequenceEqual("trailer"u8))
        {
            return new PdfToken(PdfTokenType.Trailer, bytes, tokenStart);
        }

        if (span.SequenceEqual("startxref"u8))
        {
            return new PdfToken(PdfTokenType.StartXRef, bytes, tokenStart);
        }

        if (span.SequenceEqual("%%EOF"u8))
        {
            return new PdfToken(PdfTokenType.EndOfFile, bytes, tokenStart);
        }

        return new PdfToken(PdfTokenType.Keyword, bytes, tokenStart);
    }

    // ── Byte-level I/O ────────────────────────────────────────────────────

    /// <summary>
    /// Reads the next byte from the stream without advancing the position.
    /// Returns -1 at end of stream.
    /// </summary>
    private int PeekByte()
    {
        if (!EnsureBuffer())
        {
            return -1;
        }

        return _readBuffer[_bufferPos];
    }

    /// <summary>
    /// Reads and consumes the next byte from the stream.
    /// Returns -1 at end of stream.
    /// </summary>
    private int ReadByte()
    {
        if (!EnsureBuffer())
        {
            return -1;
        }

        return _readBuffer[_bufferPos++];
    }

    /// <summary>
    /// Advances past the current peek byte without reading it.
    /// Must be called only after a successful <see cref="PeekByte"/>.
    /// </summary>
    private void ConsumeByte()
    {
        _bufferPos++;
    }

    /// <summary>
    /// Ensures the read buffer has at least one byte available.
    /// Returns true if a byte is available; false at end of stream.
    /// </summary>
    private bool EnsureBuffer()
    {
        if (_bufferPos < _bufferLen)
        {
            return true;
        }

        if (_streamExhausted)
        {
            return false;
        }

        _bufferStreamOffset += _bufferLen;
        _bufferPos = 0;
        _bufferLen = _stream.Read(_readBuffer, 0, BufferSize);

        if (_bufferLen == 0)
        {
            _streamExhausted = true;
            return false;
        }

        return true;
    }

    // ── Character classification ──────────────────────────────────────────

    /// <summary>
    /// Returns true for PDF whitespace characters.
    /// PDF 32000-1:2008 §7.2.2 — Character set.
    /// Whitespace: NUL (0), TAB (9), LF (10), FF (12), CR (13), SP (32).
    /// </summary>
    private static bool IsWhitespace(byte b) =>
        b == 0 || b == 9 || b == 10 || b == 12 || b == 13 || b == 32;

    /// <summary>
    /// Returns true for PDF delimiter characters.
    /// PDF 32000-1:2008 §7.2.2 — Character set.
    /// Delimiters: ( ) &lt; &gt; [ ] { } / %
    /// </summary>
    private static bool IsDelimiter(byte b) =>
        b == '(' || b == ')' ||
        b == '<' || b == '>' ||
        b == '[' || b == ']' ||
        b == '{' || b == '}' ||
        b == '/' || b == '%';

    // ── Guards ────────────────────────────────────────────────────────────

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PdfTokenizer));
        }
    }
}
