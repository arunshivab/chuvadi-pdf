// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Authoring.Tests;

public sealed class CustomFontEmbeddingTests
{
    [Fact]
    public void EmbeddedFont_IsSubsetted_FarSmallerThanOriginal()
    {
        byte[] ttf = LoadFixtureFont();

        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddTrueTypeFont("Sub", ttf);
        builder.AddPage(PageSize.A4).DrawText("Hi", 50, 50, "Sub", 24, Colors.Black);
        byte[] bytes = builder.ToByteArray();

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(bytes), leaveOpen: false);
        PdfObjectStore store = doc.Objects;

        PdfDictionary type0 = ResolveFontResource(store, doc.Pages[0].Dictionary, "Sub");
        PdfArray descendants = store.ResolveAs<PdfArray>(
            type0[PdfName.Intern("DescendantFonts")])!;
        PdfDictionary cidFont = store.ResolveAs<PdfDictionary>(descendants[0])!;
        PdfDictionary descriptor = store.ResolveAs<PdfDictionary>(
            cidFont[PdfName.Intern("FontDescriptor")])!;
        PdfStream fontFile = store.ResolveAs<PdfStream>(
            descriptor[PdfName.Intern("FontFile2")])!;

        fontFile.RawBytes.Length.Should().BeGreaterThan(0);
        fontFile.RawBytes.Length.Should().BeLessThan(ttf.Length / 2,
            "the embedded subset for two glyphs must be far smaller than the full font");
    }

    private static byte[] LoadFixtureFont() =>
        File.ReadAllBytes(Path.Combine(
            System.AppContext.BaseDirectory, "Fixtures", "LiberationSerif-Regular.ttf"));

    [Fact]
    public void AddTrueTypeFont_EmbedsType0CompositeFont()
    {
        byte[] ttf = LoadFixtureFont();

        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddTrueTypeFont("MyFont", ttf);
        builder.AddPage(PageSize.A4)
            .DrawText("Hello", 50, 50, "MyFont", 24, Colors.Black);
        byte[] bytes = builder.ToByteArray();

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(bytes), leaveOpen: false);
        PdfObjectStore store = doc.Objects;

        PdfDictionary type0 = ResolveFontResource(store, doc.Pages[0].Dictionary, "MyFont");
        NameOf(type0, "Subtype").Should().Be("Type0");
        NameOf(type0, "Encoding").Should().Be("Identity-H");
        type0.TryGetValue(PdfName.Intern("ToUnicode"), out _).Should().BeTrue();

        PdfArray descendants = store.ResolveAs<PdfArray>(
            type0[PdfName.Intern("DescendantFonts")])!;
        PdfDictionary cidFont = store.ResolveAs<PdfDictionary>(descendants[0])!;
        NameOf(cidFont, "Subtype").Should().Be("CIDFontType2");
        NameOf(cidFont, "CIDToGIDMap").Should().Be("Identity");
        cidFont.TryGetValue(PdfName.Intern("W"), out _).Should().BeTrue();

        PdfDictionary descriptor = store.ResolveAs<PdfDictionary>(
            cidFont[PdfName.Intern("FontDescriptor")])!;
        descriptor.TryGetValue(PdfName.Intern("FontFile2"), out _).Should().BeTrue();
    }

    [Fact]
    public void CustomFont_UsedOnTwoPages_IsEmbeddedOnceAndShared()
    {
        byte[] ttf = LoadFixtureFont();

        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddTrueTypeFont("Shared", ttf);
        builder.AddPage(PageSize.A4).DrawText("One", 50, 50, "Shared", 24, Colors.Black);
        builder.AddPage(PageSize.A4).DrawText("Two", 50, 50, "Shared", 24, Colors.Black);
        byte[] bytes = builder.ToByteArray();

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(bytes), leaveOpen: false);
        PdfObjectStore store = doc.Objects;

        PdfReference first = FontRef(store, doc.Pages[0].Dictionary, "Shared");
        PdfReference second = FontRef(store, doc.Pages[1].Dictionary, "Shared");

        second.ObjectNumber.Should().Be(first.ObjectNumber);
    }

    [Fact]
    public void RegisteredButUnusedFont_IsNotEmbedded()
    {
        byte[] ttf = LoadFixtureFont();

        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddTrueTypeFont("Unused", ttf);
        builder.AddPage(PageSize.A4)
            .DrawText("plain", 50, 50, StandardFonts.Helvetica, 12, Colors.Black);
        byte[] bytes = builder.ToByteArray();

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(bytes), leaveOpen: false);
        PdfObjectStore store = doc.Objects;
        PdfDictionary resources = store.ResolveAs<PdfDictionary>(
            doc.Pages[0].Dictionary[PdfName.Intern("Resources")])!;
        PdfDictionary fonts = store.ResolveAs<PdfDictionary>(resources[PdfName.Intern("Font")])!;
        fonts.TryGetValue(PdfName.Intern("Unused"), out _).Should().BeFalse();
    }

    private static PdfReference FontRef(PdfObjectStore store, PdfDictionary page, string key)
    {
        PdfDictionary resources = store.ResolveAs<PdfDictionary>(page[PdfName.Intern("Resources")])!;
        PdfDictionary fonts = store.ResolveAs<PdfDictionary>(resources[PdfName.Intern("Font")])!;
        return (PdfReference)fonts[PdfName.Intern(key)];
    }

    private static PdfDictionary ResolveFontResource(
        PdfObjectStore store, PdfDictionary page, string key)
    {
        PdfDictionary resources = store.ResolveAs<PdfDictionary>(page[PdfName.Intern("Resources")])!;
        PdfDictionary fonts = store.ResolveAs<PdfDictionary>(resources[PdfName.Intern("Font")])!;
        return store.ResolveAs<PdfDictionary>(fonts[PdfName.Intern(key)])!;
    }

    private static string NameOf(PdfDictionary dict, string key) =>
        ((PdfName)dict[PdfName.Intern(key)]).Value;
}
