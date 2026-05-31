// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.4 — /Filter chains (Name or Array of Names)
// PHASE: v2.1.8 — Chuvadi.Pdf.IO tests
//
// Direct unit tests for the chained-/Filter path in
// Chuvadi.Pdf.IO.ObjectStreamReader.Decode (shared with
// PdfReader.DecodeStreamBytes as of v2.1.8). Up to v2.1.7 the PdfReader
// path silently emitted raw bytes when /Filter was an Array; v2.1.8
// promotes the array-aware helper from ObjectStreamReader so both
// callers use one implementation.
//
// Test inputs are produced via FilterPipeline.Encode (the same library
// the production decode path consults), inverting through the same
// codebase the SUT calls. This means a co-incident encode+decode bug
// could theoretically fool the test — but encoders and decoders live in
// separate methods on separate filter classes (e.g. AsciiHexFilter.Encode
// vs AsciiHexFilter.Decode), and the practical alternative (hand-rolling
// FlateDecode-compatible zlib output without first verifying Chuvadi's
// wire format) carried its own different and worse risk.

using System.Text;
using Chuvadi.Pdf.Filters;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.IO.Tests;

public sealed class ChainedFilterDecodeTests
{
    private static readonly FilterPipeline Pipeline = FilterRegistry.CreateDefaultPipeline();

    // ── /Filter chain decode ──────────────────────────────────────────────

    [Fact]
    public void Decode_SingleFlateDecode_RoundTripsThroughHelper()
    {
        // Sanity check that the single-Name path still works after v2.1.8's
        // refactor (the function used to live in PdfReader and was lifted
        // into ObjectStreamReader; we want to confirm the path that ran on
        // every v2.1.7 PDF still produces identical output).

        byte[] original = Encoding.ASCII.GetBytes("Hello, world!");
        byte[] flateEncoded = Pipeline.Encode("FlateDecode", original);

        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Filter, PdfName.Intern("FlateDecode"));

        byte[] decoded = ObjectStreamReader.Decode(dict, flateEncoded);

        decoded.Should().Equal(original);
    }

    [Fact]
    public void Decode_NoFilter_ReturnsRawBytes()
    {
        // A stream without /Filter must be returned unchanged.
        byte[] original = Encoding.ASCII.GetBytes("uncompressed payload");
        PdfDictionary dict = new PdfDictionary();

        byte[] decoded = ObjectStreamReader.Decode(dict, original);

        decoded.Should().Equal(original);
    }

    [Fact]
    public void Decode_ChainedFilters_AppliesInOrder_AsciiHexThenFlate()
    {
        // /Filter [/ASCIIHexDecode /FlateDecode] reads decode-order: first
        // ASCIIHexDecode removes the outermost hex wrapping, then
        // FlateDecode decompresses the inner bytes. Encoding order is
        // reverse: Flate-compress original, then ASCIIHex-wrap the result.
        // This is the bug class v2.1.8 fixes — pre-v2.1.8 PdfReader saw
        // GetName(/Filter) return null on an Array and silently emitted
        // the raw (still hex-wrapped + flated) bytes.

        byte[] original = Encoding.ASCII.GetBytes("The quick brown fox jumps over the lazy dog.");
        byte[] flated = Pipeline.Encode("FlateDecode", original);
        byte[] hexWrappedFlated = Pipeline.Encode("ASCIIHexDecode", flated);

        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Filter, new PdfArray(
        [
            PdfName.Intern("ASCIIHexDecode"),
            PdfName.Intern("FlateDecode"),
        ]));

        byte[] decoded = ObjectStreamReader.Decode(dict, hexWrappedFlated);

        decoded.Should().Equal(original,
            "ASCIIHexDecode + FlateDecode applied in order must reproduce the source bytes");
    }

    [Fact]
    public void Decode_SingleNameInArray_BehavesLikeSingleName()
    {
        // /Filter [/FlateDecode] is the array form with one entry. Both
        // shapes are legal per §7.4 and must yield identical decode output.

        byte[] original = Encoding.ASCII.GetBytes("array-with-one-element edge case");
        byte[] flated = Pipeline.Encode("FlateDecode", original);

        PdfDictionary dictArray = new PdfDictionary();
        dictArray.Set(PdfName.Filter, new PdfArray([PdfName.Intern("FlateDecode")]));

        PdfDictionary dictName = new PdfDictionary();
        dictName.Set(PdfName.Filter, PdfName.Intern("FlateDecode"));

        byte[] decodedArray = ObjectStreamReader.Decode(dictArray, flated);
        byte[] decodedName = ObjectStreamReader.Decode(dictName, flated);

        decodedArray.Should().Equal(original);
        decodedName.Should().Equal(decodedArray, "array and Name forms must agree");
    }

    [Fact]
    public void Decode_FilterArrayContainingNonName_ThrowsPdfParseException()
    {
        // /Filter [42 /FlateDecode] is structurally malformed — every array
        // entry must be a Name. The helper must reject it loudly rather
        // than silently skip or coerce.
        // (Order matters here: Decode walks the array left-to-right. If
        // we put a real filter first it would run on the input bytes and
        // throw from DeflateFilter on bad input. We want to verify the
        // shape check, so we put the non-Name first.)

        byte[] anyBytes = [0x01, 0x02];
        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Filter, new PdfArray(
        [
            new PdfInteger(42),
            PdfName.Intern("FlateDecode"),
        ]));

        System.Action act = () => ObjectStreamReader.Decode(dict, anyBytes);

        act.Should().Throw<PdfParseException>()
            .WithMessage("*non-Name*");
    }

    // ── /DecodeParms threading (Predictor / per-filter parameters) ─────────

    [Fact]
    public void Decode_FlateWithPngPredictor_RoundTrips()
    {
        // The headline /DecodeParms gap: a FlateDecode stream whose
        // /DecodeParms specifies a PNG predictor. Before DecodeParms was
        // threaded through ObjectStreamReader.Decode, the predictor was
        // ignored on decode, so the bytes came back still predictor-filtered
        // (garbage). Encode applies the forward predictor, so a full
        // round-trip through the library exercises both sides.
        //
        // Colors=1, BitsPerComponent=8, Columns=4 => bytesPerRow=4. Source
        // length must be a whole number of rows (16 bytes = 4 rows) or the
        // encoder declines to filter.
        byte[] original =
        [
            0x10, 0x20, 0x30, 0x40,
            0x11, 0x22, 0x33, 0x44,
            0x09, 0x18, 0x27, 0x36,
            0xFF, 0xEE, 0xDD, 0xCC,
        ];

        FilterParameters parms = new FilterParameters
        {
            Predictor = 12,
            Colors = 1,
            BitsPerComponent = 8,
            Columns = 4,
        };

        byte[] encoded = Pipeline.Encode("FlateDecode", original, parms);

        PdfDictionary parmsDict = new PdfDictionary();
        parmsDict.Set(PdfName.Intern("Predictor"), 12);
        parmsDict.Set(PdfName.Intern("Colors"), 1);
        parmsDict.Set(PdfName.Intern("BitsPerComponent"), 8);
        parmsDict.Set(PdfName.Intern("Columns"), 4);

        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Filter, PdfName.Intern("FlateDecode"));
        dict.Set(PdfName.Intern("DecodeParms"), parmsDict);

        byte[] decoded = ObjectStreamReader.Decode(dict, encoded);

        decoded.Should().Equal(original,
            "the PNG predictor in /DecodeParms must be reversed on decode");
    }

    [Fact]
    public void Decode_FlateWithPredictor_IgnoringDecodeParms_WouldNotRoundTrip()
    {
        // Guards against silent regression: a predictor-encoded stream
        // decoded WITHOUT /DecodeParms (plain FlateDecode) must NOT equal the
        // original. This is exactly the broken pre-fix behaviour; if a future
        // change drops the predictor again, this assertion documents that the
        // raw inflate output is still predictor-filtered.
        byte[] original =
        [
            0x10, 0x20, 0x30, 0x40,
            0x11, 0x22, 0x33, 0x44,
        ];

        FilterParameters parms = new FilterParameters
        {
            Predictor = 12,
            Colors = 1,
            BitsPerComponent = 8,
            Columns = 4,
        };

        byte[] encoded = Pipeline.Encode("FlateDecode", original, parms);

        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Filter, PdfName.Intern("FlateDecode"));
        // Deliberately no /DecodeParms.

        byte[] decoded = ObjectStreamReader.Decode(dict, encoded);

        decoded.Should().NotEqual(original,
            "without /DecodeParms the predictor is not reversed, so output stays filtered");
    }

    [Fact]
    public void Decode_FilterArray_AppliesPerFilterDecodeParms()
    {
        // /Filter [/ASCIIHexDecode /FlateDecode] with
        // /DecodeParms [null <</Predictor 12 ...>>]: the first entry has no
        // parameters (null), the second carries the PNG predictor. The
        // predictor must be paired with the FlateDecode entry by position.
        byte[] original =
        [
            0x01, 0x02, 0x03, 0x04,
            0x05, 0x06, 0x07, 0x08,
        ];

        FilterParameters flateParms = new FilterParameters
        {
            Predictor = 12,
            Colors = 1,
            BitsPerComponent = 8,
            Columns = 4,
        };

        byte[] flated = Pipeline.Encode("FlateDecode", original, flateParms);
        byte[] hexWrappedFlated = Pipeline.Encode("ASCIIHexDecode", flated);

        PdfDictionary flateParmsDict = new PdfDictionary();
        flateParmsDict.Set(PdfName.Intern("Predictor"), 12);
        flateParmsDict.Set(PdfName.Intern("Colors"), 1);
        flateParmsDict.Set(PdfName.Intern("BitsPerComponent"), 8);
        flateParmsDict.Set(PdfName.Intern("Columns"), 4);

        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Filter, new PdfArray(
        [
            PdfName.Intern("ASCIIHexDecode"),
            PdfName.Intern("FlateDecode"),
        ]));
        dict.Set(PdfName.Intern("DecodeParms"), new PdfArray(
        [
            PdfNull.Value,
            flateParmsDict,
        ]));

        byte[] decoded = ObjectStreamReader.Decode(dict, hexWrappedFlated);

        decoded.Should().Equal(original,
            "the second filter's predictor parameters must be applied positionally");
    }

    // ── FilterParameters.FromDictionary (canonical converter) ──────────────

    [Fact]
    public void FromDictionary_Null_ReturnsNull()
    {
        FilterParameters.FromDictionary(null).Should().BeNull();
    }

    [Fact]
    public void FromDictionary_NonDictionary_ReturnsNull()
    {
        FilterParameters.FromDictionary(new PdfInteger(5)).Should().BeNull();
    }

    [Fact]
    public void FromDictionary_SingleDictionary_ReadsAllFields()
    {
        PdfDictionary parms = new PdfDictionary();
        parms.Set(PdfName.Intern("Predictor"), 15);
        parms.Set(PdfName.Intern("Colors"), 3);
        parms.Set(PdfName.Intern("BitsPerComponent"), 16);
        parms.Set(PdfName.Intern("Columns"), 7);
        parms.Set(PdfName.Intern("EarlyChange"), 0);

        FilterParameters? result = FilterParameters.FromDictionary(parms);

        result.Should().NotBeNull();
        result!.Predictor.Should().Be(15);
        result.Colors.Should().Be(3);
        result.BitsPerComponent.Should().Be(16);
        result.Columns.Should().Be(7);
        result.EarlyChange.Should().Be(0);
    }

    [Fact]
    public void FromDictionary_ReadsEarlyChange()
    {
        // Regression guard for the field the previous TextExtractor parser
        // dropped: EarlyChange must survive the conversion.
        PdfDictionary parms = new PdfDictionary();
        parms.Set(PdfName.Intern("EarlyChange"), 0);

        FilterParameters? result = FilterParameters.FromDictionary(parms);

        result.Should().NotBeNull();
        result!.EarlyChange.Should().Be(0);
    }

    [Fact]
    public void FromDictionary_Array_SelectsByFilterIndex()
    {
        PdfDictionary second = new PdfDictionary();
        second.Set(PdfName.Intern("Predictor"), 12);

        PdfArray array = new PdfArray([PdfNull.Value, second]);

        FilterParameters.FromDictionary(array, 0).Should().BeNull("index 0 is the null entry");

        FilterParameters? atOne = FilterParameters.FromDictionary(array, 1);
        atOne.Should().NotBeNull();
        atOne!.Predictor.Should().Be(12);
    }

    [Fact]
    public void FromDictionary_Defaults_WhenFieldsAbsent()
    {
        PdfDictionary parms = new PdfDictionary();
        parms.Set(PdfName.Intern("Predictor"), 2);

        FilterParameters? result = FilterParameters.FromDictionary(parms);

        result.Should().NotBeNull();
        result!.Predictor.Should().Be(2);
        result.Colors.Should().Be(1);
        result.BitsPerComponent.Should().Be(8);
        result.Columns.Should().Be(1);
        result.EarlyChange.Should().Be(1);
    }
}
