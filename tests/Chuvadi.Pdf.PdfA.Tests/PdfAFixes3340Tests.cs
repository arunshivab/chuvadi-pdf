// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ISO 19005-1 §6.5.3 (annotation flags), §6.3.5 (Type 1 /CharSet),
//        §6.7.3 (dates in /Info and XMP)
// PHASE: 3.34.0 (#49c, #49d, #49g; D-364 zone-less dates)

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.PdfA.Tests;

public sealed class PdfAFixes3340Tests
{
    [Theory]
    [InlineData("D:20261112093015+05'30'", "2026-11-12T09:30:15+05:30")]
    [InlineData("D:20261112093015-08'00", "2026-11-12T09:30:15-08:00")]
    [InlineData("D:20261112093015Z", "2026-11-12T09:30:15Z")]
    [InlineData("D:2026111209Z", "2026-11-12T09:00:00Z")]
    [InlineData("D:20261112093015", null)]
    [InlineData("D:2026111209", null)]
    [InlineData("D:20261112", null)]
    [InlineData("D:202611", null)]
    [InlineData("2026", null)]
    [InlineData("D:20261112093015+5", null)]
    [InlineData("D:2026x", null)]
    [InlineData("yesterday", null)]
    public void PdfDate_BecomesAnXmpDate(string pdf, string? xmp)
    {
        PdfAWriter.XmpDate(pdf).Should().Be(xmp);
    }

    [Theory]
    [InlineData("D:20261112093015", "2026-11-12T09:30:15Z")]
    [InlineData("D:2026111209", "2026-11-12T09:00:00Z")]
    [InlineData("D:20261112", "2026-11-12T00:00:00Z")]
    [InlineData("2026", "2026-01-01T00:00:00Z")]
    [InlineData("D:20261112093015+05'30'", "2026-11-12T09:30:15+05:30")]
    [InlineData("D:2026x", null)]
    public void AssumeUtc_WritesZonelessDatesAsUtc(string pdf, string? xmp)
    {
        PdfAWriter.XmpDate(pdf, assumeUtc: true, out _).Should().Be(xmp);
    }

    [Fact]
    public void PdfA1B_CarriesInfoDatesIntoXmp_AndWarnsAboutAZonelessOne()
    {
        using PdfDocument document = WithDates("D:20261112093015+05'30'", "D:20261113");

        (string xml, PdfAResult result) = Xmp(document, new PdfAOptions { Conformance = PdfAConformance.PdfA1B });

        xml.Should().Contain("<xmp:CreateDate>2026-11-12T09:30:15+05:30</xmp:CreateDate>");
        xml.Should().NotContain("xmp:ModifyDate", "a date without a time zone stays out of the XMP");
        result.Warnings.Should().ContainSingle().Which.Should().Contain("/ModDate").And.Contain("no time zone").And.Contain("left out");
    }

    [Fact]
    public void PdfA1B_AssumeUtc_WritesTheZonelessDate_WithAWarning()
    {
        using PdfDocument document = WithDates("D:20261112093015+05'30'", "D:20261113");

        (string xml, PdfAResult result) = Xmp(document, new PdfAOptions { Conformance = PdfAConformance.PdfA1B, AssumeUtcForZonelessDates = true });

        xml.Should().Contain("<xmp:ModifyDate>2026-11-13T00:00:00Z</xmp:ModifyDate>");
        result.Warnings.Should().ContainSingle().Which.Should().Contain("/ModDate").And.Contain("written as UTC");
    }

    [Fact]
    public void PdfA1B_UnreadableDate_IsLeftOutWithAWarning()
    {
        using PdfDocument document = WithDates("yesterday", null);

        (string xml, PdfAResult result) = Xmp(document, new PdfAOptions { Conformance = PdfAConformance.PdfA1B });

        xml.Should().NotContain("xmp:CreateDate");
        result.Warnings.Should().ContainSingle().Which.Should().Contain("not a date that can be read");
    }

    // The XMP must not depend on the machine's time zone (D-364): the same
    // file is written the same way with the zone set to IST and to UTC. (On
    // Windows TZ is not read, so both runs use the machine's zone.)
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PdfA1B_XmpDates_DoNotDependOnTheMachineZone(bool assumeUtc)
    {
        string? saved = System.Environment.GetEnvironmentVariable("TZ");
        try
        {
            List<string> runs = new List<string>();
            foreach (string zone in Zones)
            {
                System.Environment.SetEnvironmentVariable("TZ", zone);
                System.TimeZoneInfo.ClearCachedData();
                using PdfDocument document = WithDates("D:20261112093015", "D:20261113100000+05'30'");
                (string xml, _) = Xmp(document, new PdfAOptions { Conformance = PdfAConformance.PdfA1B, AssumeUtcForZonelessDates = assumeUtc });
                runs.Add(DatesOf(xml));
            }

            runs[0].Should().Be(runs[1]);
            runs[0].Should().Contain("2026-11-13T10:00:00+05:30");
            if (assumeUtc)
            {
                runs[0].Should().Contain("2026-11-12T09:30:15Z");
            }
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("TZ", saved);
            System.TimeZoneInfo.ClearCachedData();
        }
    }

    private static readonly string[] Zones = { "Asia/Kolkata", "UTC" };

    private static string DatesOf(string xml)
    {
        StringBuilder sb = new StringBuilder();
        foreach (string tag in new[] { "xmp:CreateDate", "xmp:ModifyDate" })
        {
            int at = xml.IndexOf("<" + tag + ">", System.StringComparison.Ordinal);
            if (at >= 0)
            {
                sb.Append(xml, at, xml.IndexOf("</" + tag + ">", at, System.StringComparison.Ordinal) - at).Append('|');
            }
        }

        return sb.ToString();
    }

    private static PdfDocument WithDates(string? creation, string? modified)
    {
        PdfDocument document = PdfDocument.Open(new MemoryStream(TestPdf.WithSimpleFont("Helvetica")));
        PdfDictionary info = (PdfDictionary)document.Objects.Resolve(document.Trailer[PdfName.Info]);
        if (creation is not null)
        {
            info.Set(PdfName.Intern("CreationDate"), new PdfString(Encoding.Latin1.GetBytes(creation)));
        }

        if (modified is not null)
        {
            info.Set(PdfName.Intern("ModDate"), new PdfString(Encoding.Latin1.GetBytes(modified)));
        }

        return document;
    }

    [Fact]
    public void Annotations_ArePrintableAndShown_HiddenOnesAreRemoved()
    {
        using PdfDocument document = PdfDocument.Open(new MemoryStream(TestPdf.WithSimpleFont("Helvetica")));
        PdfArray annots = new PdfArray();
        foreach (int? flags in new int?[] { null, 2, 4 | 32, 4 })
        {
            PdfDictionary annot = new PdfDictionary();
            annot.Set(PdfName.Type, PdfName.Intern("Annot"));
            annot.Set(PdfName.Subtype, PdfName.Intern("Square"));
            annot.Set(PdfName.Intern("Rect"), new PdfArray(new PdfPrimitive[] { new PdfInteger(0), new PdfInteger(0), new PdfInteger(10), new PdfInteger(10) }));
            if (flags is int f)
            {
                annot.Set(PdfName.Intern("F"), f);
            }

            annots.Add(annot);
        }

        document.Pages[0].Dictionary.Set(PdfName.Intern("Annots"), annots);
        using MemoryStream output = new MemoryStream();
        PdfAWriter.Write(output, document, new PdfAOptions { Conformance = PdfAConformance.PdfA1B }).Succeeded.Should().BeTrue();

        using PdfDocument reopened = PdfDocument.Open(new MemoryStream(output.ToArray()));
        PdfArray written = (PdfArray)reopened.Objects.Resolve(reopened.Pages[0].Dictionary[PdfName.Intern("Annots")]);
        written.Count.Should().Be(3, "the Hidden one is removed");
        foreach (PdfPrimitive entry in written)
        {
            ((PdfDictionary)reopened.Objects.Resolve(entry)).GetInteger(PdfName.Intern("F"), 0).Should().Be(4);
        }
    }

    [Fact]
    public void Type1Subset_GetsACharSet()
    {
        using PdfDocument document = PdfDocument.Open(new MemoryStream(WithType1Subset()));

        using MemoryStream output = new MemoryStream();
        PdfAWriter.Write(output, document, new PdfAOptions { Conformance = PdfAConformance.PdfA1B }).Succeeded.Should().BeTrue();

        using PdfDocument reopened = PdfDocument.Open(new MemoryStream(output.ToArray()));
        PdfDictionary font = (PdfDictionary)reopened.Objects.Resolve(((PdfDictionary)reopened.Objects.Resolve(reopened.Pages[0].Resources![PdfName.Intern("Font")]))[PdfName.Intern("F1")]);
        PdfDictionary descriptor = (PdfDictionary)reopened.Objects.Resolve(font[PdfName.Intern("FontDescriptor")]);
        ((PdfString)descriptor[PdfName.Intern("CharSet")]).ToTextString().Should().Be("/A/B");
    }

    private static (string Xml, PdfAResult Result) Xmp(PdfDocument document, PdfAOptions options)
    {
        using MemoryStream output = new MemoryStream();
        PdfAResult result = PdfAWriter.Write(output, document, options);
        result.Succeeded.Should().BeTrue();
        using PdfDocument reopened = PdfDocument.Open(new MemoryStream(output.ToArray()));
        return (Encoding.UTF8.GetString(reopened.XmpMetadata!), result);
    }

    // A page using an embedded Type 1 subset (ABCDEF+Test) defining /A, /B and .notdef.
    private static byte[] WithType1Subset()
    {
        byte[] program = Type1Program("A", "B", ".notdef");
        List<PdfIndirectObject> objects = new List<PdfIndirectObject>();
        PdfDictionary fileDict = new PdfDictionary();
        fileDict.Set(PdfName.Intern("Length1"), program.Length);
        objects.Add(new PdfIndirectObject(new PdfObjectId(1, 0), new PdfStream(fileDict, program)));
        PdfDictionary descriptor = new PdfDictionary();
        descriptor.Set(PdfName.Type, PdfName.Intern("FontDescriptor"));
        descriptor.Set(PdfName.Intern("FontName"), PdfName.Intern("ABCDEF+Test"));
        descriptor.Set(PdfName.Intern("Flags"), 32);
        descriptor.Set(PdfName.Intern("FontBBox"), new PdfArray(new PdfPrimitive[] { new PdfInteger(0), new PdfInteger(0), new PdfInteger(500), new PdfInteger(700) }));
        descriptor.Set(PdfName.Intern("ItalicAngle"), 0);
        descriptor.Set(PdfName.Intern("Ascent"), 700);
        descriptor.Set(PdfName.Intern("Descent"), 0);
        descriptor.Set(PdfName.Intern("CapHeight"), 700);
        descriptor.Set(PdfName.Intern("StemV"), 80);
        descriptor.Set(PdfName.Intern("FontFile"), new PdfReference(new PdfObjectId(1, 0)));
        objects.Add(new PdfIndirectObject(new PdfObjectId(2, 0), descriptor));
        PdfDictionary font = new PdfDictionary();
        font.Set(PdfName.Type, PdfName.Intern("Font"));
        font.Set(PdfName.Intern("Subtype"), PdfName.Intern("Type1"));
        font.Set(PdfName.Intern("BaseFont"), PdfName.Intern("ABCDEF+Test"));
        font.Set(PdfName.Intern("FirstChar"), 65);
        font.Set(PdfName.Intern("LastChar"), 66);
        font.Set(PdfName.Intern("Widths"), new PdfArray(new PdfPrimitive[] { new PdfInteger(500), new PdfInteger(500) }));
        font.Set(PdfName.Intern("FontDescriptor"), new PdfReference(new PdfObjectId(2, 0)));
        objects.Add(new PdfIndirectObject(new PdfObjectId(3, 0), font));
        objects.Add(new PdfIndirectObject(new PdfObjectId(4, 0), new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("BT /F1 24 Tf 72 700 Td (AB) Tj ET"))));
        PdfDictionary fonts = new PdfDictionary();
        fonts.Set(PdfName.Intern("F1"), new PdfReference(new PdfObjectId(3, 0)));
        PdfDictionary resources = new PdfDictionary();
        resources.Set(PdfName.Intern("Font"), fonts);
        PdfDictionary page = new PdfDictionary();
        page.Set(PdfName.Type, PdfName.Intern("Page"));
        page.Set(PdfName.Intern("Parent"), new PdfReference(new PdfObjectId(6, 0)));
        page.Set(PdfName.Intern("MediaBox"), new PdfArray(new PdfPrimitive[] { new PdfInteger(0), new PdfInteger(0), new PdfInteger(612), new PdfInteger(792) }));
        page.Set(PdfName.Intern("Resources"), resources);
        page.Set(PdfName.Intern("Contents"), new PdfReference(new PdfObjectId(4, 0)));
        objects.Add(new PdfIndirectObject(new PdfObjectId(5, 0), page));
        PdfDictionary pages = new PdfDictionary();
        pages.Set(PdfName.Type, PdfName.Intern("Pages"));
        pages.Set(PdfName.Intern("Kids"), new PdfArray(new PdfPrimitive[] { new PdfReference(new PdfObjectId(5, 0)) }));
        pages.Set(PdfName.Intern("Count"), 1);
        objects.Add(new PdfIndirectObject(new PdfObjectId(6, 0), pages));
        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Intern("Catalog"));
        catalog.Set(PdfName.Intern("Pages"), new PdfReference(new PdfObjectId(6, 0)));
        objects.Add(new PdfIndirectObject(new PdfObjectId(7, 0), catalog));
        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(new PdfObjectId(7, 0)));
        trailer.Set(PdfName.Intern("Size"), 8);
        using MemoryStream stream = new MemoryStream();
        PdfWriter.Write(stream, objects, trailer, null);
        return stream.ToArray();
    }

    // A Type 1 program whose /CharStrings defines the given names (each an
    // empty glyph: 0 500 hsbw endchar).
    private static byte[] Type1Program(params string[] names)
    {
        string clear = "%!PS-AdobeFont-1.0: Test\n/FontMatrix [0.001 0 0 0.001 0 0] readonly def\n/Encoding StandardEncoding def\ncurrentfile eexec\n";
        byte[] glyph = Encrypt(new byte[] { 139, 248, 136, 13, 14 }, 4330);
        List<byte> priv = new List<byte>(Encoding.Latin1.GetBytes("dup /Private 8 dict dup begin /lenIV 4 def\n/CharStrings " + names.Length.ToString(CultureInfo.InvariantCulture) + " dict dup begin\n"));
        foreach (string name in names)
        {
            priv.AddRange(Encoding.Latin1.GetBytes("/" + name + " " + glyph.Length.ToString(CultureInfo.InvariantCulture) + " RD "));
            priv.AddRange(glyph);
            priv.AddRange(Encoding.Latin1.GetBytes(" ND\n"));
        }

        priv.AddRange(Encoding.Latin1.GetBytes("end\nend\n"));
        List<byte> program = new List<byte>(Encoding.Latin1.GetBytes(clear));
        program.AddRange(Encrypt(priv.ToArray(), 55665));
        return program.ToArray();
    }

    private static byte[] Encrypt(byte[] plain, ushort r)
    {
        byte[] input = new byte[plain.Length + 4];
        input[0] = 0xF1;
        input[1] = 0xF2;
        input[2] = 0xF3;
        input[3] = 0xF4;
        plain.CopyTo(input, 4);
        byte[] output = new byte[input.Length];
        for (int i = 0; i < input.Length; i++)
        {
            byte c = (byte)(input[i] ^ (r >> 8));
            output[i] = c;
            r = (ushort)(((c + r) * 52845) + 22719);
        }

        return output;
    }
}
