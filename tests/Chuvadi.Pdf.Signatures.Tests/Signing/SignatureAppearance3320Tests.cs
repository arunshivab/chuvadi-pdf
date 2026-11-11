// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.32.0 (D-208 1c, D-230 Q1 and Q2) — the default signature
//        appearance: text, an optional image, and a border.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Chuvadi.Cryptography.PathValidation;
using Chuvadi.Cryptography.Signing;
using Chuvadi.Cryptography.X509;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Images;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Rendering;
using Chuvadi.Pdf.Signatures.Signing;
using Chuvadi.Pdf.Text;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Signatures.Tests.Signing;

public sealed class SignatureAppearance3320Tests
{
    private static readonly double[] WidgetRect = { 72.0, 72.0, 272.0, 132.0 };

    private const string TamilName = "கையொப்பம் அருண்";

    // ── default text, on every signing path ─────────────────────────────

    [Fact]
    public void Sign_DefaultAppearance_ShowsSignerAndDate()
    {
        (ISigner signer, X509Certificate cert, TrustStore trust) = Phase126OperationsTests.MakeFixture();
        byte[] signed = Sign(signer, Appearance());

        Signing3312Tests.AssertAllVerify(signed, trust, expectedCount: 1);
        string text = WidgetText(signed);
        text.Should().Contain("Digitally signed by " + cert.Subject.CommonName);
        text.Should().Contain("Date: 2025-06-01T00:00:00Z");
        InkInsideWidget(signed).Should().BeGreaterThan(50, "the text is drawn");
    }

    [Fact]
    public async Task SignAsync_DefaultAppearance_ShowsSignerAndDate()
    {
        (ISigner signer, X509Certificate cert, TrustStore trust) = Phase126OperationsTests.MakeFixture();
        byte[] signed;
        using (PdfDocument d = PdfDocument.Open(new MemoryStream(Signing3312Tests.BuildPageWithContents()), leaveOpen: false))
        {
            signed = await PdfSigner.SignAsync(d, signer, Options(Appearance()));
        }

        Signing3312Tests.AssertAllVerify(signed, trust, expectedCount: 1);
        WidgetText(signed).Should().Contain("Digitally signed by " + cert.Subject.CommonName);
    }

    [Fact]
    public void AddSignature_DefaultAppearance_ShowsSignerAndDate_AndBothVerify()
    {
        (ISigner signer, X509Certificate cert, TrustStore trust) = Phase126OperationsTests.MakeFixture();
        byte[] first = Sign(signer, null);
        byte[] second = PdfCounterSigner.AddSignature(first, signer, Options(Appearance()));

        Signing3312Tests.AssertAllVerify(second, trust, expectedCount: 2);
        WidgetText(second).Should().Contain("Digitally signed by " + cert.Subject.CommonName);
    }

    [Fact]
    public void DefaultText_ShowsTheSigningTimeInUtc()
    {
        (ISigner signer, _, _) = Phase126OperationsTests.MakeFixture();
        using PdfDocument d = PdfDocument.Open(new MemoryStream(Signing3312Tests.BuildPageWithContents()), leaveOpen: false);
        byte[] signed = PdfSigner.Sign(d, signer, new PdfSigningOptions
        {
            SigningTime = new DateTimeOffset(2026, 11, 7, 10, 30, 5, TimeSpan.FromHours(5.5)),
            Appearance = Appearance(),
        });

        WidgetText(signed).Should().Contain("Date: 2026-11-07T05:00:05Z");
    }

    // ── custom text, LiPi fallback ──────────────────────────────────────

    [Fact]
    public void CustomText_ReplacesTheDefaultLines()
    {
        (ISigner signer, _, TrustStore trust) = Phase126OperationsTests.MakeFixture();
        byte[] signed = Sign(signer, Appearance(text: "Approved\nDr. A. Shiva"));

        Signing3312Tests.AssertAllVerify(signed, trust, expectedCount: 1);
        string text = WidgetText(signed);
        text.Should().Contain("Approved");
        text.Should().Contain("Dr. A. Shiva");
        text.Should().NotContain("Digitally signed");
    }

    [Fact]
    public void TamilText_IsDrawnWithASubsetOfLiPiSans()
    {
        (ISigner signer, _, TrustStore trust) = Phase126OperationsTests.MakeFixture();
        byte[] signed = Sign(signer, Appearance(text: TamilName));

        Signing3312Tests.AssertAllVerify(signed, trust, expectedCount: 1);

        // 3.33.0 (ticket 45): the shaped clusters carry their source text.
        WidgetText(signed).Should().Contain(TamilName);
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(signed), leaveOpen: false);
        PdfDictionary type0 = FallbackFont(doc);
        string baseFont = doc.Objects.ResolveAs<PdfName>(type0[PdfName.Intern("BaseFont")])!.Value;
        baseFont.Should().MatchRegex("^[A-Z]{6}\\+", "a subset font name carries a tag");
        baseFont.Should().Contain("Tamil");

        PdfDictionary cid = doc.Objects.ResolveAs<PdfDictionary>(doc.Objects.ResolveAs<PdfArray>(type0[PdfName.Intern("DescendantFonts")])![0])!;
        PdfDictionary fd = doc.Objects.ResolveAs<PdfDictionary>(cid[PdfName.Intern("FontDescriptor")])!;
        PdfStream fontFile = (PdfStream)doc.Objects.Resolve(fd[PdfName.Intern("FontFile2")]);
        fontFile.Dictionary.GetName(PdfName.Intern("Filter")).Should().Be(PdfName.Intern("FlateDecode"));
        fontFile.RawBytes.Length.Should().BeLessThan(40_000, "only the glyphs used are embedded");
        InkInsideWidget(signed).Should().BeGreaterThan(50);

        // D-245, D-263, D-275: the fallback font's descriptor follows the one
        // rule: Symbolic (Identity-H), StemV from the font's weight class.
        ((PdfInteger)fd[PdfName.Intern("Flags")]).Value.Should().Be(4);
        int weight = WeightClass(Inflate(fontFile.RawBytes));
        ((PdfInteger)fd[PdfName.Intern("StemV")]).Value.Should().Be((int)Math.Round(50 + Math.Pow(weight / 65.0, 2), MidpointRounding.AwayFromZero));
    }

    private static byte[] Inflate(byte[] zlib)
    {
        using System.IO.Compression.ZLibStream z = new System.IO.Compression.ZLibStream(new MemoryStream(zlib), System.IO.Compression.CompressionMode.Decompress);
        using MemoryStream output = new MemoryStream();
        z.CopyTo(output);
        return output.ToArray();
    }

    // OS/2 usWeightClass of a TrueType font program.
    private static int WeightClass(byte[] font)
    {
        int tables = (font[4] << 8) | font[5];
        for (int i = 0; i < tables; i++)
        {
            int record = 12 + (16 * i);
            if (font[record] == 'O' && font[record + 1] == 'S' && font[record + 2] == '/' && font[record + 3] == '2')
            {
                int offset = (font[record + 8] << 24) | (font[record + 9] << 16) | (font[record + 10] << 8) | font[record + 11];
                return (font[offset + 4] << 8) | font[offset + 5];
            }
        }

        return 400;
    }

    // ── images ──────────────────────────────────────────────────────────

    [Fact]
    public void JpegImage_IsDrawnOnTheLeftThird_BesideTheText()
    {
        (ISigner signer, X509Certificate cert, TrustStore trust) = Phase126OperationsTests.MakeFixture();
        byte[] signed = Sign(signer, Appearance(image: Jpeg()));

        Signing3312Tests.AssertAllVerify(signed, trust, expectedCount: 1);
        WidgetText(signed).Should().Contain("Digitally signed by " + cert.Subject.CommonName);

        PixelBuffer page = Render(signed);
        IsRed(At(page, 72 + 33, 72 + 30)).Should().BeTrue("the red image fills the left third, centred");
        IsRed(At(page, 72 + 150, 72 + 30)).Should().BeFalse("the right two thirds hold the text");
    }

    [Fact]
    public void PngWithAlpha_KeepsItsTransparencyAsASoftMask()
    {
        (ISigner signer, _, TrustStore trust) = Phase126OperationsTests.MakeFixture();
        byte[] signed = Sign(signer, Appearance(image: Png(withAlpha: true)));

        Signing3312Tests.AssertAllVerify(signed, trust, expectedCount: 1);
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(signed), leaveOpen: false);
        PdfStream image = Image(doc);
        image.Dictionary.ContainsKey(PdfName.Intern("SMask")).Should().BeTrue();
    }

    [Fact]
    public void ImageOnly_FillsTheBox()
    {
        (ISigner signer, _, TrustStore trust) = Phase126OperationsTests.MakeFixture();
        byte[] signed = Sign(signer, Appearance(text: string.Empty, image: Png(withAlpha: false)));

        Signing3312Tests.AssertAllVerify(signed, trust, expectedCount: 1);
        WidgetText(signed).Trim().Should().BeEmpty();

        // The 40 x 20 image fills the 196 x 56 inner box as 112 x 56, centred.
        PixelBuffer page = Render(signed);
        IsRed(At(page, 72 + 100, 72 + 30)).Should().BeTrue();
        IsRed(At(page, 72 + 35, 72 + 30)).Should().BeFalse("outside the centred image");
    }

    [Fact]
    public void UnsupportedImage_Throws_NamingTheFormat()
    {
        (ISigner signer, _, _) = Phase126OperationsTests.MakeFixture();
        using MemoryStream bmp = new MemoryStream();
        BmpEncoder.Encode(Frame(withAlpha: false), bmp);

        Action act = () => Sign(signer, Appearance(image: bmp.ToArray()));

        act.Should().Throw<ArgumentException>().WithMessage("*JPEG or PNG*BMP*");
    }

    [Fact]
    public void UnrecognisedBytes_Throw()
    {
        (ISigner signer, _, _) = Phase126OperationsTests.MakeFixture();

        Action act = () => Sign(signer, Appearance(image: new byte[] { 1, 2, 3, 4 }));

        act.Should().Throw<ArgumentException>().WithMessage("*unrecognised format*");
    }

    // ── pre-rendered still wins; LTV numbering ──────────────────────────

    [Fact]
    public void PreRenderedStream_Wins_OverTextAndImage()
    {
        (ISigner signer, _, TrustStore trust) = Phase126OperationsTests.MakeFixture();
        byte[] supplied = Encoding.ASCII.GetBytes("0 0 1 rg 0 0 10 10 re f\n");
        SignatureAppearance appearance = new SignatureAppearance
        {
            PageIndex = 0,
            Rectangle = WidgetRect,
            PreRenderedAppearanceStream = supplied,
            Text = "ignored",
            Image = Jpeg(),
        };
        byte[] signed = Sign(signer, appearance);

        Signing3312Tests.AssertAllVerify(signed, trust, expectedCount: 1);
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(signed), leaveOpen: false);
        PdfStream normal = Normal(doc);
        normal.RawBytes.Should().Equal(supplied);
        WidgetText(signed).Should().NotContain("ignored");
    }

    [Fact]
    public void AppearanceWithImageAndText_AndLtv_KeepEveryObject()
    {
        (ISigner signer, X509Certificate cert, TrustStore trust) = Phase126OperationsTests.MakeFixture();
        PdfSigningOptions options = new PdfSigningOptions
        {
            SigningTime = Phase126OperationsTests.SigningTime,
            Appearance = Appearance(text: TamilName, image: Png(withAlpha: true)),
            LtvOptions = new LtvOptions { Certificates = new[] { cert } },
        };
        byte[] signed;
        using (PdfDocument d = PdfDocument.Open(new MemoryStream(Signing3312Tests.BuildPageWithContents()), leaveOpen: false))
        {
            signed = PdfSigner.Sign(d, signer, options);
        }

        Signing3312Tests.AssertAllVerify(signed, trust, expectedCount: 1);
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(signed), leaveOpen: false);
        Image(doc).Dictionary.ContainsKey(PdfName.Intern("SMask")).Should().BeTrue();
        FallbackFont(doc).Should().NotBeNull();
        Chuvadi.Pdf.Signatures.Dss.PdfDocumentDssExtensions.GetDocumentSecurityStore(doc)!.Certificates.Should().HaveCount(1);
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private static SignatureAppearance Appearance(string? text = null, byte[]? image = null) => new SignatureAppearance
    {
        PageIndex = 0,
        Rectangle = WidgetRect,
        Text = text,
        Image = image,
    };

    private static PdfSigningOptions Options(SignatureAppearance? appearance) => new PdfSigningOptions
    {
        SigningTime = Phase126OperationsTests.SigningTime,
        Appearance = appearance,
    };

    private static byte[] Sign(ISigner signer, SignatureAppearance? appearance)
    {
        using PdfDocument d = PdfDocument.Open(new MemoryStream(Signing3312Tests.BuildPageWithContents()), leaveOpen: false);
        return PdfSigner.Sign(d, signer, Options(appearance));
    }

    private static string WidgetText(byte[] pdf)
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        return new TextExtractor(doc.Objects).ExtractText(doc.Pages[0]);
    }

    private static PixelBuffer Render(byte[] pdf)
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        return new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72 }).Rasterize(doc.Pages[0]);
    }

    // The pixel at page point (x, y), y from the bottom of the 792 pt page.
    private static (byte B, byte G, byte R, byte A) At(PixelBuffer page, double x, double y) =>
        page.GetPixelBgra((int)x, (int)(792 - y));

    private static int InkInsideWidget(byte[] pdf)
    {
        PixelBuffer page = Render(pdf);
        int ink = 0;
        for (int y = 792 - 130; y < 792 - 74; y++)
        {
            for (int x = 74; x < 270; x++)
            {
                (byte b, byte g, byte r, _) = page.GetPixelBgra(x, y);
                if (r < 128 && g < 128 && b < 128)
                {
                    ink++;
                }
            }
        }

        return ink;
    }

    private static bool IsRed((byte B, byte G, byte R, byte A) p) => p.R > 180 && p.G < 90 && p.B < 90;

    private static PdfStream Normal(PdfDocument doc)
    {
        PdfObjectStore store = doc.Objects;
        PdfArray annots = store.ResolveAs<PdfArray>(doc.Pages[0].Dictionary[PdfName.Intern("Annots")])!;
        PdfDictionary widget = store.ResolveAs<PdfDictionary>(annots[0])!;
        PdfDictionary ap = store.ResolveAs<PdfDictionary>(widget[PdfName.Intern("AP")])!;
        return (PdfStream)store.Resolve(ap[PdfName.Intern("N")]);
    }

    private static PdfDictionary Resources(PdfDocument doc) =>
        doc.Objects.ResolveAs<PdfDictionary>(Normal(doc).Dictionary[PdfName.Intern("Resources")])!;

    private static PdfStream Image(PdfDocument doc)
    {
        PdfDictionary xobjects = doc.Objects.ResolveAs<PdfDictionary>(Resources(doc)[PdfName.Intern("XObject")])!;
        return (PdfStream)doc.Objects.Resolve(xobjects[PdfName.Intern("CvSigImage")]);
    }

    private static PdfDictionary FallbackFont(PdfDocument doc)
    {
        PdfDictionary fonts = doc.Objects.ResolveAs<PdfDictionary>(Resources(doc)[PdfName.Intern("Font")])!;
        foreach (KeyValuePair<PdfName, PdfPrimitive> entry in fonts)
        {
            PdfDictionary font = doc.Objects.ResolveAs<PdfDictionary>(entry.Value)!;
            if (font.GetName(PdfName.Intern("Subtype"))?.Value == "Type0")
            {
                return font;
            }
        }

        throw new InvalidOperationException("No Type0 fallback font in the appearance.");
    }

    // A 40 x 20 solid red image (with an alpha channel when asked).
    private static ImageFrame Frame(bool withAlpha)
    {
        PixelBuffer pixels = new PixelBuffer(40, 20);
        for (int y = 0; y < 20; y++)
        {
            for (int x = 0; x < 40; x++)
            {
                pixels.SetPixelBgra(x, y, 0, 0, 255, (byte)(withAlpha && x < 4 ? 0 : 255));
            }
        }

        return new ImageFrame(pixels, withAlpha ? ImageColorFormat.Rgba32 : ImageColorFormat.Rgb24);
    }

    private static byte[] Jpeg()
    {
        using MemoryStream ms = new MemoryStream();
        JpegEncoder.Encode(Frame(withAlpha: false), ms, 95);
        return ms.ToArray();
    }

    private static byte[] Png(bool withAlpha)
    {
        using MemoryStream ms = new MemoryStream();
        PngEncoder.Encode(Frame(withAlpha), ms, includeAlpha: withAlpha);
        return ms.ToArray();
    }
}
