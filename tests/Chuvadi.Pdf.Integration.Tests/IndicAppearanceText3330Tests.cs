// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.33.0 (ticket 45, D-325) — shaped Indic text in generated field and
//        comment appearances copies and searches as written: each shaped
//        cluster carries its source text (ToUnicode or /ActualText).
// SPEC:  PDF 32000-1:2008 §9.10.3 (ToUnicode CMaps), §14.9.4 (/ActualText).

using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Operations;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Text;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Integration.Tests;

public sealed class IndicAppearanceText3330Tests
{
    private const string HelvResources = "<</Font<</Helv<</Type/Font/Subtype/Type1/BaseFont/Helvetica/Encoding/WinAnsiEncoding>>>>>>";

    [Theory]
    [InlineData("தமிழ்நாடு கொள்கை")]
    [InlineData("க கொ கோ")]
    [InlineData("क्षत्रिय नमस्ते")]
    [InlineData("হিন্দি শ্রীকৃষ্ণ")]
    [InlineData("Arun அருண்")]
    public void FieldValue_ExtractsAsWritten(string value)
    {
        using PdfDocument doc = TestPdf.Open(Field(value));

        new TextExtractor(doc.Objects).ExtractText(doc.Pages[0]).Trim().Should().Be(value);
    }

    [Fact]
    public void FlattenedField_ExtractsAsWritten_AndUsesActualText()
    {
        const string Value = "தமிழ்நாடு கொள்கை";
        using PdfDocument doc = TestPdf.Open(Field(Value));
        using MemoryStream output = new MemoryStream();
        AnnotationFlattener.Flatten(output, doc);

        using PdfDocument flat = PdfDocument.Open(new MemoryStream(output.ToArray()));
        new TextExtractor(flat.Objects).ExtractText(flat.Pages[0]).Trim().Should().Be(Value);
        AllStreamText(flat).Should().Contain("/ActualText");
    }

    [Fact]
    public void FreeTextComment_ExtractsAsWritten()
    {
        const string Value = "क्षत्रिय नमस्ते";
        string annot = "<</Type/Annot/Subtype/FreeText/Rect[10 60 190 100]/Contents" + Hex(Value) + "/DA(/Helv 9 Tf 0 g)>>";
        using PdfDocument doc = TestPdf.Open(TestPdf.OnePage(string.Empty, HelvResources, new[] { annot }));

        new TextExtractor(doc.Objects).ExtractText(doc.Pages[0]).Trim().Should().Be(Value);
    }

    [Fact]
    public void LatinValue_KeepsItsSpacing()
    {
        using PdfDocument doc = TestPdf.Open(Field("Arun Shiva B"));

        new TextExtractor(doc.Objects).ExtractText(doc.Pages[0]).Trim().Should().Be("Arun Shiva B");
    }

    private static byte[] Field(string value)
    {
        string widget = "<</Type/Annot/Subtype/Widget/FT/Tx/T(name)/V" + Hex(value) + "/Rect[10 100 190 130]/F 4/P PAGE/DA(/Helv 12 Tf 0 g)>>";
        return TestPdf.OnePage(string.Empty, HelvResources, new[] { widget }, "<</Fields[FIELDS]/NeedAppearances false/DR " + HelvResources + ">>");
    }

    // A UTF-16BE text string with a byte order mark, as a hex string.
    private static string Hex(string text)
    {
        StringBuilder sb = new StringBuilder("<FEFF");
        foreach (char c in text)
        {
            sb.Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
        }

        return sb.Append('>').ToString();
    }

    private static string AllStreamText(PdfDocument doc)
    {
        StringBuilder sb = new StringBuilder();
        foreach (Chuvadi.Pdf.Objects.PdfIndirectObject obj in doc.Objects.Objects)
        {
            if (doc.Objects.Resolve(obj.Value) is PdfStream stream && !stream.Dictionary.ContainsKey(PdfName.Intern("Length1")))
            {
                sb.Append(Encoding.Latin1.GetString(Decoded(stream)));
            }
        }

        return sb.ToString();
    }

    private static byte[] Decoded(PdfStream stream)
    {
        if (!stream.Dictionary.ContainsKey(PdfName.Intern("Filter")))
        {
            return stream.RawBytes;
        }

        using System.IO.Compression.ZLibStream z = new System.IO.Compression.ZLibStream(new MemoryStream(stream.RawBytes), System.IO.Compression.CompressionMode.Decompress);
        using MemoryStream output = new MemoryStream();
        z.CopyTo(output);
        return output.ToArray();
    }
}
