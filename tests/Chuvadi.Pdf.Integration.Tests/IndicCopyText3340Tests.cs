// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.34.0 — shaped clusters drawn in text order get per-glyph
//        ToUnicode instead of /ActualText, so viewers that copy span by span
//        (Edge) do not put a space between them ("அருண் சி வா").
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

public sealed class IndicCopyText3340Tests
{
    private const string HelvResources = "<</Font<</Helv<</Type/Font/Subtype/Type1/BaseFont/Helvetica/Encoding/WinAnsiEncoding>>>>>>";

    [Fact]
    public void InOrderClusters_UseToUnicode_NotActualText()
    {
        const string Value = "அருண் சிவா";
        string flat = FlattenedStreams(Value, out string text);

        text.Should().Be(Value);
        flat.Should().NotContain("/ActualText", "சி and வா are drawn in text order, one glyph per code point");
    }

    [Fact]
    public void ReorderedCluster_KeepsActualText()
    {
        // கொ draws the vowel sign ெ before க, so its text cannot come from the
        // glyphs in order.
        const string Value = "கொ";
        string flat = FlattenedStreams(Value, out string text);

        text.Should().Be(Value);
        flat.Should().Contain("/ActualText");
    }

    private static string FlattenedStreams(string value, out string text)
    {
        string widget = "<</Type/Annot/Subtype/Widget/FT/Tx/T(name)/V" + Hex(value) + "/Rect[10 100 190 130]/F 4/P PAGE/DA(/Helv 12 Tf 0 g)>>";
        byte[] pdf = TestPdf.OnePage(string.Empty, HelvResources, new[] { widget }, "<</Fields[FIELDS]/NeedAppearances false/DR " + HelvResources + ">>");
        using PdfDocument doc = TestPdf.Open(pdf);
        using MemoryStream output = new MemoryStream();
        AnnotationFlattener.Flatten(output, doc);

        using PdfDocument flat = PdfDocument.Open(new MemoryStream(output.ToArray()));
        text = new TextExtractor(flat.Objects).ExtractText(flat.Pages[0]).Trim();
        StringBuilder sb = new StringBuilder();
        foreach (Chuvadi.Pdf.Objects.PdfIndirectObject obj in flat.Objects.Objects)
        {
            if (flat.Objects.Resolve(obj.Value) is PdfStream stream && !stream.Dictionary.ContainsKey(PdfName.Intern("Length1")))
            {
                sb.Append(Encoding.Latin1.GetString(Decoded(stream)));
            }
        }

        return sb.ToString();
    }

    private static string Hex(string text)
    {
        StringBuilder sb = new StringBuilder("<FEFF");
        foreach (char c in text)
        {
            sb.Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
        }

        return sb.Append('>').ToString();
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
