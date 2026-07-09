// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.3 — Objects, §7.3.8 — Stream objects
// PHASE: Phase 1 — Chuvadi.Pdf.IO
// Converts a token stream into a tree of PdfPrimitive objects.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.IO;

/// <summary>
/// Parses PDF objects from a <see cref="PdfTokenizer"/> token stream.
/// Maintains a 2-token lookahead buffer to resolve integer ambiguity
/// (an integer may begin an indirect reference n m R, or be a bare value).
/// PDF 32000-1:2008 §7.3 — Objects.
/// </summary>
internal sealed class PdfObjectParser
{
    private readonly PdfTokenizer _tokenizer;
    private readonly Stream _stream;
    private PdfToken? _pending1;
    private PdfToken? _pending2;

    internal PdfObjectParser(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _tokenizer = new PdfTokenizer(stream, leaveOpen: true);
        _pending1 = null;
        _pending2 = null;
    }

    /// <summary>Gets the current byte offset in the underlying stream.</summary>
    internal long Position => _tokenizer.Position;

    /// <summary>Reads the next token (internal, for use by PdfReader).</summary>
    internal PdfToken ReadToken() => NextToken();

    /// <summary>Seeks the tokenizer to the given byte offset.</summary>
    internal void Seek(long offset)
    {
        _tokenizer.Seek(offset);
        _pending1 = null;
        _pending2 = null;
    }

    // ── Token buffer ──────────────────────────────────────────────────────

    private PdfToken NextToken()
    {
        if (_pending1.HasValue)
        {
            PdfToken t = _pending1.Value;
            _pending1 = _pending2;
            _pending2 = null;
            return t;
        }

        return _tokenizer.Read();
    }

    private PdfToken PeekToken()
    {
        if (!_pending1.HasValue)
        {
            _pending1 = _tokenizer.Read();
        }

        return _pending1.Value;
    }

    private PdfToken PeekToken2()
    {
        if (!_pending1.HasValue)
        {
            _pending1 = _tokenizer.Read();
        }

        if (!_pending2.HasValue)
        {
            _pending2 = _tokenizer.Read();
        }

        return _pending2.Value;
    }

    // ── Public parsing API ────────────────────────────────────────────────

    /// <summary>Reads one complete PDF value from the current position.</summary>
    internal PdfPrimitive ReadValue()
    {
        PdfToken token = NextToken();

        switch (token.Type)
        {
            case PdfTokenType.True:
                return PdfBoolean.True;

            case PdfTokenType.False:
                return PdfBoolean.False;

            case PdfTokenType.Null:
                return PdfNull.Value;

            case PdfTokenType.Name:
                return PdfName.FromRawBytes(token.RawBytes);

            case PdfTokenType.LiteralString:
                return ParseLiteralString(token);

            case PdfTokenType.HexString:
                return ParseHexString(token);

            case PdfTokenType.Real:
                return new PdfReal(double.Parse(
                    token.RawText,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture));

            case PdfTokenType.Integer:
                return ReadIntegerOrReference(token);

            case PdfTokenType.DictionaryStart:
                return ReadDictionaryOrStream();

            case PdfTokenType.ArrayStart:
                return ReadArray();

            case PdfTokenType.EndOfStream:
            case PdfTokenType.EndOfFile:
                return PdfNull.Value;

            default:
                return PdfNull.Value;
        }
    }

    /// <summary>
    /// Reads an indirect object definition at the current position.
    /// The stream must be positioned at the object-number integer.
    /// </summary>
    internal PdfIndirectObject ReadIndirectObject()
    {
        PdfToken numToken = NextToken();

        if (numToken.Type != PdfTokenType.Integer)
        {
            throw new PdfParseException(
                $"Expected object number integer, got {numToken.Type} at offset {numToken.ByteOffset}.");
        }

        PdfToken genToken = NextToken();

        if (genToken.Type != PdfTokenType.Integer)
        {
            throw new PdfParseException(
                $"Expected generation number integer, got {genToken.Type} at offset {genToken.ByteOffset}.");
        }

        PdfToken objToken = NextToken();

        if (objToken.Type != PdfTokenType.ObjectStart)
        {
            throw new PdfParseException(
                $"Expected 'obj', got {objToken.Type} at offset {objToken.ByteOffset}.");
        }

        int objectNumber = ParseInt32(numToken, NumberStyles.None, "object number");
        int generation = ParseInt32(genToken, NumberStyles.None, "generation number");

        PdfPrimitive value = ReadValue();

        PdfToken next = PeekToken();

        if (next.Type == PdfTokenType.ObjectEnd)
        {
            NextToken();
        }

        PdfObjectId id = new PdfObjectId(objectNumber, generation);
        return new PdfIndirectObject(id, value);
    }

    // ── Private parsing helpers ───────────────────────────────────────────

    private PdfPrimitive ReadIntegerOrReference(PdfToken intToken)
    {
        PdfToken next = PeekToken();

        if (next.Type != PdfTokenType.Integer)
        {
            int v = ParseInt32(intToken, NumberStyles.AllowLeadingSign, "integer");
            return new PdfInteger(v);
        }

        PdfToken next2 = PeekToken2();

        if (next2.Type == PdfTokenType.Reference)
        {
            NextToken(); // consume gen number
            NextToken(); // consume R
            int objNum = ParseInt32(intToken, NumberStyles.None, "reference object number");
            int gen = ParseInt32(next, NumberStyles.None, "reference generation number");
            return new PdfReference(new PdfObjectId(objNum, gen));
        }

        int value = ParseInt32(intToken, NumberStyles.AllowLeadingSign, "integer");
        return new PdfInteger(value);
    }

    /// <summary>
    /// Parses an Int32 from a token's raw text, throwing <see cref="PdfParseException"/>
    /// with diagnostic detail on overflow or malformed input. Prevents framework
    /// exceptions (<see cref="OverflowException"/>, <see cref="FormatException"/>)
    /// from leaking out of the parser, so callers see only the documented
    /// PDF-typed exception set even on malformed input.
    /// </summary>
    private static int ParseInt32(PdfToken token, NumberStyles style, string what)
    {
        if (!int.TryParse(token.RawText, style, CultureInfo.InvariantCulture, out int value))
        {
            string snippet = token.RawText.Length <= 32
                ? token.RawText
                : token.RawText.Substring(0, 32) + "...";
            throw new PdfParseException(
                $"Invalid {what} '{snippet}' at offset {token.ByteOffset}: " +
                "must be a valid 32-bit signed integer.");
        }

        return value;
    }

    private PdfPrimitive ReadDictionaryOrStream()
    {
        PdfDictionary dict = new PdfDictionary();

        while (true)
        {
            PdfToken key = NextToken();

            if (key.Type == PdfTokenType.DictionaryEnd)
            {
                break;
            }

            if (key.Type == PdfTokenType.EndOfStream)
            {
                throw new PdfParseException("Unexpected end of stream inside dictionary.");
            }

            if (key.Type != PdfTokenType.Name)
            {
                throw new PdfParseException(
                    $"Dictionary key must be a name, got {key.Type} at offset {key.ByteOffset}.");
            }

            PdfName keyName = PdfName.FromRawBytes(key.RawBytes);
            PdfPrimitive val = ReadValue();
            dict.Set(keyName, val);
        }

        PdfToken after = PeekToken();

        if (after.Type != PdfTokenType.StreamStart)
        {
            return dict;
        }

        NextToken(); // consume 'stream'
        ConsumeStreamLineEnding();

        int length = ResolveLength(dict);
        byte[] rawBytes = ReadStreamBytes(length);

        PdfToken endstream = NextToken();

        if (endstream.Type != PdfTokenType.StreamEnd)
        {
            // Tolerate missing endstream in malformed PDFs.
        }

        return new PdfStream(dict, rawBytes);
    }

    private PdfArray ReadArray()
    {
        List<PdfPrimitive> items = new List<PdfPrimitive>();

        while (true)
        {
            PdfToken next = PeekToken();

            if (next.Type == PdfTokenType.ArrayEnd)
            {
                NextToken();
                break;
            }

            if (next.Type == PdfTokenType.EndOfStream)
            {
                throw new PdfParseException("Unexpected end of stream inside array.");
            }

            items.Add(ReadValue());
        }

        return new PdfArray(items);
    }

    // ── String parsing ────────────────────────────────────────────────────

    private static PdfString ParseLiteralString(PdfToken token)
    {
        // Single authoritative escape decoder (§7.3.4.2) — see
        // PdfString.DecodeLiteralToken. Handles the full escape set including
        // octal \ddd, \b, \f, line continuations, and unescaped end-of-line
        // normalization, which the previous inline decoder lacked.
        return new PdfString(PdfString.DecodeLiteralToken(token.RawBytes));
    }

    private static PdfString ParseHexString(PdfToken token)
    {
        // Single authoritative hex decoder (§7.3.4.3) — see
        // PdfString.DecodeHexToken.
        return new PdfString(PdfString.DecodeHexToken(token.RawBytes));
    }

    // ── Stream helpers ────────────────────────────────────────────────────

    private int ResolveLength(PdfDictionary dict)
    {
        if (!dict.TryGetValue(PdfName.Length, out PdfPrimitive? lengthValue))
        {
            throw new PdfParseException("Stream dictionary missing /Length entry.");
        }

        if (lengthValue is PdfInteger directLength)
        {
            return directLength.Value;
        }

        // Indirect /Length reference — fall back to scanning for endstream.
        return -1;
    }

    private byte[] ReadStreamBytes(int length)
    {
        if (length >= 0)
        {
            long pos = _tokenizer.Position;
            _stream.Seek(pos, SeekOrigin.Begin);
            byte[] bytes = new byte[length];
            int read = 0;

            while (read < length)
            {
                int n = _stream.Read(bytes, read, length - read);

                if (n == 0)
                {
                    throw new PdfParseException(
                        $"Stream truncated: expected {length} bytes, got {read}.");
                }

                read += n;
            }

            _tokenizer.Seek(pos + length);
            _pending1 = null;
            _pending2 = null;
            return bytes;
        }
        else
        {
            return ScanForEndstream();
        }
    }

    private byte[] ScanForEndstream()
    {
        long startPos = _tokenizer.Position;
        _stream.Seek(startPos, SeekOrigin.Begin);

        using (MemoryStream buffer = new MemoryStream())
        {
            byte[] marker = Encoding.ASCII.GetBytes("endstream");
            int matchLen = 0;
            int b;

            while ((b = _stream.ReadByte()) != -1)
            {
                if (b == marker[matchLen])
                {
                    matchLen++;

                    if (matchLen == marker.Length)
                    {
                        byte[] result = buffer.ToArray();
                        int trim = result.Length;

                        while (trim > 0 && (result[trim - 1] == 13 || result[trim - 1] == 10))
                        {
                            trim--;
                        }

                        byte[] trimmed = new byte[trim];
                        Array.Copy(result, trimmed, trim);
                        _tokenizer.Seek(_stream.Position);
                        _pending1 = null;
                        _pending2 = null;
                        return trimmed;
                    }
                }
                else
                {
                    for (int i = 0; i < matchLen; i++)
                    {
                        buffer.WriteByte(marker[i]);
                    }

                    matchLen = 0;
                    buffer.WriteByte((byte)b);
                }
            }

            throw new PdfParseException("Stream endstream marker not found.");
        }
    }

    private void ConsumeStreamLineEnding()
    {
        long pos = _tokenizer.Position;
        _stream.Seek(pos, SeekOrigin.Begin);

        int b = _stream.ReadByte();

        if (b == 13) // CR
        {
            int b2 = _stream.ReadByte();

            if (b2 != 10) // not LF
            {
                _stream.Seek(-1, SeekOrigin.Current);
            }
        }
        else if (b != 10) // not LF
        {
            _stream.Seek(-1, SeekOrigin.Current);
        }

        _tokenizer.Seek(_stream.Position);
        _pending1 = null;
        _pending2 = null;
    }
}
