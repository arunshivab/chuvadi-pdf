// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.6.6 (character encoding); ISO 19005-1 §6.3.7 and
//        ISO 19005-2 §6.2.11.6 (TrueType encodings)
// PHASE: 3.33.0 (ticket 46, D-314) — a substitute keeps the glyphs the
//        original encoding named: /Differences over WinAnsi for PDF/A-2b, a
//        symbolic font with a (3,0) cmap and /ToUnicode for PDF/A-1b.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Filters;
using Chuvadi.Pdf.Fonts.Rendering;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Text;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.PdfA.Tests;

public sealed class FontEncoding3330Tests
{
    // ' and ` are quoteright and quoteleft in StandardEncoding; 0xE1 is AE.
    private static readonly byte[] StandardText = { (byte)'I', (byte)'t', 0x27, (byte)'s', (byte)' ', 0x60, (byte)'x', 0x27, (byte)' ', 0xE1 };

    // TeX-style: 11 ff, 12 fi, 39 quoteright, 96 quoteleft.
    private static readonly byte[] TexText = { 0x0B, (byte)'e', 0x0C, (byte)'x', 0x27, 0x60 };

    // MacRoman: 0x8A adieresis, 0xD2 quotedblleft, 0xA5 bullet.
    private static readonly byte[] MacText = { (byte)'a', 0x8A, 0xD2, 0xA5 };

    public static TheoryData<PdfAConformance> Levels => new TheoryData<PdfAConformance>
    {
        PdfAConformance.PdfA1B,
        PdfAConformance.PdfA2B,
    };

    [Theory]
    [MemberData(nameof(Levels))]
    public void StandardEncodingDefault_KeepsTheQuotesAndAccents(PdfAConformance level)
    {
        byte[] pdf = OneFont("Helvetica", null, StandardText);

        string text = Convert(pdf, level, out PdfDocument output);
        using (output)
        {
            text.Should().Be(OriginalText(pdf)).And.Contain("It’s ‘x’ Æ");
            CheckForm(output, level);
        }
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void TexStyleDifferences_KeepsTheLigaturesAndQuotes(PdfAConformance level)
    {
        PdfDictionary encoding = new PdfDictionary();
        encoding.Set(PdfName.Intern("Differences"), new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(11), PdfName.Intern("ff"), PdfName.Intern("fi"),
            new PdfInteger(39), PdfName.Intern("quoteright"),
            new PdfInteger(96), PdfName.Intern("quoteleft"),
        }));
        byte[] pdf = OneFont("Times-Roman", encoding, TexText);

        string text = Convert(pdf, level, out PdfDocument output);
        using (output)
        {
            text.Should().Be(OriginalText(pdf)).And.Contain("ﬀeﬁx’‘");
            CheckForm(output, level);
        }
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void MacRomanEncoding_KeepsTheMacRomanCharacters(PdfAConformance level)
    {
        byte[] pdf = OneFont("Courier", PdfName.Intern("MacRomanEncoding"), MacText);

        string text = Convert(pdf, level, out PdfDocument output);
        using (output)
        {
            text.Should().Be(OriginalText(pdf)).And.Contain("aä“•");
            CheckForm(output, level);
        }
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void TwoEncodingsOnOneFace_GetTwoSubstitutes_SameEncodingsShareOne(PdfAConformance level)
    {
        byte[] pdf = Build(
            new[]
            {
                ("F1", "Helvetica", (PdfPrimitive?)PdfName.Intern("WinAnsiEncoding")),
                ("F2", "Helvetica", (PdfPrimitive?)null),
                ("F3", "Helvetica-Bold", (PdfPrimitive?)null),
                ("F4", "Helvetica", (PdfPrimitive?)null),
            },
            "BT /F1 12 Tf 72 700 Td (a) Tj /F2 12 Tf (b) Tj /F3 12 Tf (c) Tj /F4 12 Tf (d) Tj ET"u8.ToArray());

        Convert(pdf, level, out PdfDocument output);
        using (output)
        {
            PdfDictionary fonts = output.Objects.ResolveAs<PdfDictionary>(output.Pages[0].Resources![PdfName.Intern("Font")])!;
            PdfReference f1 = (PdfReference)fonts[PdfName.Intern("F1")];
            PdfReference f2 = (PdfReference)fonts[PdfName.Intern("F2")];
            PdfReference f4 = (PdfReference)fonts[PdfName.Intern("F4")];
            f1.ObjectId.Should().NotBe(f2.ObjectId);
            f2.ObjectId.Should().Be(f4.ObjectId);
        }
    }

    // PDF/A-1b (ISO 19005-1 §6.3.7): a symbolic TrueType font has no
    // /Encoding and exactly one cmap. PDF/A-2b (ISO 19005-2 §6.2.11.6): a
    // non-symbolic one has WinAnsiEncoding as its base, AGL names only, and a
    // (3,1) cmap. Either way every used code reaches a glyph.
    private static void CheckForm(PdfDocument output, PdfAConformance level)
    {
        PdfDictionary font = FirstFont(output);
        PdfDictionary descriptor = output.Objects.ResolveAs<PdfDictionary>(font[PdfName.Intern("FontDescriptor")])!;
        int flags = output.Objects.ResolveAs<PdfInteger>(descriptor[PdfName.Intern("Flags")])!.Value;
        byte[] program = Decoded(output.Objects.ResolveAs<PdfStream>(descriptor[PdfName.Intern("FontFile2")])!);
        Dictionary<(int Platform, int Encoding), Dictionary<int, int>> cmaps = ReadCmaps(program);
        int first = output.Objects.ResolveAs<PdfInteger>(font[PdfName.Intern("FirstChar")])!.Value;
        int last = output.Objects.ResolveAs<PdfInteger>(font[PdfName.Intern("LastChar")])!.Value;

        if (level == PdfAConformance.PdfA1B)
        {
            font.ContainsKey(PdfName.Intern("Encoding")).Should().BeFalse();
            (flags & 4).Should().Be(4, "the font is symbolic");
            (flags & 32).Should().Be(0);
            cmaps.Keys.Should().Equal((3, 0));
            font.ContainsKey(PdfName.Intern("ToUnicode")).Should().BeTrue();
            for (int code = first; code <= last; code++)
            {
                int width = WidthOf(output, font, first, code);
                if (width > 0)
                {
                    cmaps[(3, 0)].Should().ContainKey(0xF000 + code);
                }
            }

            return;
        }

        PdfDictionary encoding = output.Objects.ResolveAs<PdfDictionary>(font[PdfName.Intern("Encoding")])!;
        encoding.GetName(PdfName.Intern("BaseEncoding"))!.Value.Should().Be("WinAnsiEncoding");
        (flags & 32).Should().Be(32, "the font is non-symbolic");
        cmaps.Should().ContainKey((3, 1));
        PdfArray differences = output.Objects.ResolveAs<PdfArray>(encoding[PdfName.Intern("Differences")])!;
        foreach (PdfName name in differences.OfType<PdfName>())
        {
            AdobeGlyphList.TryGetCodePoint(name.Value, out int unicode).Should().BeTrue(name.Value + " is an AGL name");
            if (unicode != 0xFB00)
            {
                // Liberation has no ff ligature, as the standard 14 fonts have none.
                cmaps[(3, 1)].Should().ContainKey(unicode, name.Value + " reaches a glyph");
            }
        }
    }

    private static byte[] Decoded(PdfStream stream) => stream.Filter is PdfName filter
        ? new FilterPipeline().Decode(filter.Value, stream.RawBytes)
        : stream.RawBytes;

    private static int WidthOf(PdfDocument document, PdfDictionary font, int first, int code)
    {
        PdfArray widths = document.Objects.ResolveAs<PdfArray>(font[PdfName.Intern("Widths")])!;
        return ((PdfInteger)widths[code - first]).Value;
    }

    private static string Convert(byte[] pdf, PdfAConformance level, out PdfDocument output)
    {
        using PdfDocument source = PdfDocument.Open(new MemoryStream(pdf));
        using MemoryStream written = new MemoryStream();
        PdfAResult result = PdfAWriter.Write(written, source, new PdfAOptions { Conformance = level });
        result.Succeeded.Should().BeTrue(string.Join("; ", result.Violations));
        output = PdfDocument.Open(new MemoryStream(written.ToArray()));
        return new TextExtractor(output.Objects).ExtractText(output.Pages[0]);
    }

    private static string OriginalText(byte[] pdf)
    {
        using PdfDocument source = PdfDocument.Open(new MemoryStream(pdf));
        return new TextExtractor(source.Objects).ExtractText(source.Pages[0]);
    }

    private static PdfDictionary FirstFont(PdfDocument document)
    {
        PdfDictionary fonts = document.Objects.ResolveAs<PdfDictionary>(document.Pages[0].Resources![PdfName.Intern("Font")])!;
        return document.Objects.ResolveAs<PdfDictionary>(fonts.Values.First())!;
    }

    private static byte[] OneFont(string baseFont, PdfPrimitive? encoding, byte[] text)
    {
        List<byte> content = new List<byte>();
        content.AddRange("BT /F1 24 Tf 72 700 Td <"u8.ToArray());
        content.AddRange(System.Text.Encoding.ASCII.GetBytes(System.Convert.ToHexString(text)));
        content.AddRange("> Tj ET"u8.ToArray());
        return Build(new[] { ("F1", baseFont, encoding) }, content.ToArray());
    }

    private static byte[] Build((string Key, string BaseFont, PdfPrimitive? Encoding)[] fonts, byte[] content)
    {
        int number = 1;
        List<PdfIndirectObject> objects = new List<PdfIndirectObject>();
        PdfDictionary fontResources = new PdfDictionary();
        foreach ((string key, string baseFont, PdfPrimitive? encoding) in fonts)
        {
            PdfObjectId fontId = new PdfObjectId(number++, 0);
            PdfDictionary font = new PdfDictionary();
            font.Set(PdfName.Type, PdfName.Intern("Font"));
            font.Set(PdfName.Intern("Subtype"), PdfName.Intern("Type1"));
            font.Set(PdfName.Intern("BaseFont"), PdfName.Intern(baseFont));
            if (encoding is not null)
            {
                font.Set(PdfName.Intern("Encoding"), encoding);
            }

            objects.Add(new PdfIndirectObject(fontId, font));
            fontResources.Set(PdfName.Intern(key), new PdfReference(fontId));
        }

        PdfObjectId contentId = new PdfObjectId(number++, 0);
        objects.Add(new PdfIndirectObject(contentId, new PdfStream(new PdfDictionary(), content)));
        PdfDictionary resources = new PdfDictionary();
        resources.Set(PdfName.Intern("Font"), fontResources);

        PdfObjectId pagesId = new PdfObjectId(number++, 0);
        PdfObjectId pageId = new PdfObjectId(number++, 0);
        PdfDictionary page = new PdfDictionary();
        page.Set(PdfName.Type, PdfName.Intern("Page"));
        page.Set(PdfName.Intern("Parent"), new PdfReference(pagesId));
        page.Set(PdfName.Intern("MediaBox"), new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0),
            new PdfInteger(0),
            new PdfInteger(612),
            new PdfInteger(792),
        }));
        page.Set(PdfName.Intern("Resources"), resources);
        page.Set(PdfName.Intern("Contents"), new PdfReference(contentId));
        objects.Add(new PdfIndirectObject(pageId, page));

        PdfDictionary pages = new PdfDictionary();
        pages.Set(PdfName.Type, PdfName.Intern("Pages"));
        pages.Set(PdfName.Intern("Kids"), new PdfArray(new PdfPrimitive[] { new PdfReference(pageId) }));
        pages.Set(PdfName.Intern("Count"), 1);
        objects.Add(new PdfIndirectObject(pagesId, pages));

        PdfObjectId catalogId = new PdfObjectId(number++, 0);
        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Intern("Catalog"));
        catalog.Set(PdfName.Intern("Pages"), new PdfReference(pagesId));
        objects.Add(new PdfIndirectObject(catalogId, catalog));

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));
        trailer.Set(PdfName.Intern("Size"), number);
        using MemoryStream stream = new MemoryStream();
        PdfWriter.Write(stream, objects, trailer, null);
        return stream.ToArray();
    }

    // Reads every format-4 cmap subtable of an sfnt: (platform, encoding) to
    // code-point-to-glyph map (glyph 0 left out).
    private static Dictionary<(int Platform, int Encoding), Dictionary<int, int>> ReadCmaps(byte[] sfnt)
    {
        Dictionary<(int, int), Dictionary<int, int>> result = new Dictionary<(int, int), Dictionary<int, int>>();
        int numTables = U16(sfnt, 4);
        int cmap = -1;
        for (int i = 0; i < numTables; i++)
        {
            int record = 12 + (i * 16);
            if (System.Text.Encoding.ASCII.GetString(sfnt, record, 4) == "cmap")
            {
                cmap = (int)U32(sfnt, record + 8);
            }
        }

        cmap.Should().BeGreaterThan(0);
        int count = U16(sfnt, cmap + 2);
        for (int i = 0; i < count; i++)
        {
            int record = cmap + 4 + (i * 8);
            int platform = U16(sfnt, record);
            int encoding = U16(sfnt, record + 2);
            int sub = cmap + (int)U32(sfnt, record + 4);
            U16(sfnt, sub).Should().Be(4);
            int segX2 = U16(sfnt, sub + 6);
            int ends = sub + 14;
            int starts = ends + segX2 + 2;
            int deltas = starts + segX2;
            int ranges = deltas + segX2;
            Dictionary<int, int> map = new Dictionary<int, int>();
            for (int s = 0; s < segX2 / 2; s++)
            {
                int end = U16(sfnt, ends + (s * 2));
                int start = U16(sfnt, starts + (s * 2));
                int delta = U16(sfnt, deltas + (s * 2));
                int rangeOffset = U16(sfnt, ranges + (s * 2));
                for (int c = start; c <= end && c != 0xFFFF; c++)
                {
                    int gid = rangeOffset == 0
                        ? (c + delta) & 0xFFFF
                        : U16(sfnt, ranges + (s * 2) + rangeOffset + ((c - start) * 2));
                    if (gid != 0)
                    {
                        map[c] = gid;
                    }
                }
            }

            result[(platform, encoding)] = map;
        }

        return result;
    }

    private static int U16(byte[] d, int p) => (d[p] << 8) | d[p + 1];

    private static uint U32(byte[] d, int p) => ((uint)d[p] << 24) | ((uint)d[p + 1] << 16) | ((uint)d[p + 2] << 8) | d[p + 3];
}
