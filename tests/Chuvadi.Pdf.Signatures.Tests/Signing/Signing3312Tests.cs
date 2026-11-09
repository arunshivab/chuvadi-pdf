// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.31.2 (D-207) — signing real documents, refusing signed input,
//        counter-signatures with a visible appearance

using System;
using System.IO;
using System.Threading.Tasks;
using Chuvadi.Cryptography.PathValidation;
using Chuvadi.Cryptography.Signing;
using Chuvadi.Cryptography.X509;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Signatures.Dss;
using Chuvadi.Pdf.Signatures.Signing;
using Chuvadi.Pdf.Signatures.Verification;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Signatures.Tests.Signing;

public sealed class Signing3312Tests
{
    private static readonly double[] WidgetRect = { 72.0, 72.0, 272.0, 132.0 };

    // ── item 1: a page with /Contents ────────────────────────────────────

    [Fact]
    public void Sign_DocumentWhosePageHasContents_SignsAndVerifies()
    {
        (ISigner signer, _, TrustStore trust) = Phase126OperationsTests.MakeFixture();
        byte[] signed = SignOnce(BuildPageWithContents(), signer);

        AssertAllVerify(signed, trust, expectedCount: 1);
    }

    [Fact]
    public async Task SignAsync_DocumentWhosePageHasContents_SignsAndVerifies()
    {
        (ISigner signer, _, TrustStore trust) = Phase126OperationsTests.MakeFixture();
        byte[] signed;
        using (PdfDocument d = PdfDocument.Open(new MemoryStream(BuildPageWithContents()), leaveOpen: false))
        {
            signed = await PdfSigner.SignAsync(d, signer, Options());
        }

        AssertAllVerify(signed, trust, expectedCount: 1);
    }

    [Fact]
    public void Sign_ByteRangeCoversEverythingButTheSignatureContents()
    {
        (ISigner signer, _, _) = Phase126OperationsTests.MakeFixture();
        byte[] signed = SignOnce(BuildPageWithContents(), signer);

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(signed), leaveOpen: false);
        PdfSignature sig = doc.Signatures()[0];
        sig.ByteRange.FirstOffset.Should().Be(0);
        (sig.ByteRange.SecondOffset + sig.ByteRange.SecondLength).Should().Be(signed.Length);

        // The gap is exactly the signature's own /Contents hex string.
        signed[(int)sig.ByteRange.FirstLength].Should().Be((byte)'<');
        signed[(int)sig.ByteRange.SecondOffset - 1].Should().Be((byte)'>');
    }

    // ── item 1: signed input is refused ──────────────────────────────────

    [Fact]
    public void Sign_AlreadySignedDocument_Throws()
    {
        (ISigner signer, _, _) = Phase126OperationsTests.MakeFixture();
        byte[] signed = SignOnce(BuildPageWithContents(), signer);

        using PdfDocument d = PdfDocument.Open(new MemoryStream(signed), leaveOpen: false);
        Action act = () => PdfSigner.Sign(d, signer, Options());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("This document already has a signature; use PdfCounterSigner.AddSignature to add another.");
    }

    [Fact]
    public async Task SignAsync_AlreadySignedDocument_Throws()
    {
        (ISigner signer, _, _) = Phase126OperationsTests.MakeFixture();
        byte[] signed = SignOnce(BuildPageWithContents(), signer);

        using PdfDocument d = PdfDocument.Open(new MemoryStream(signed), leaveOpen: false);
        Func<Task> act = () => PdfSigner.SignAsync(d, signer, Options());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("This document already has a signature; use PdfCounterSigner.AddSignature to add another.");
    }

    // ── 1b: AddSignature honours Appearance ──────────────────────────────

    [Fact]
    public void AddSignature_WithAppearance_AddsWidgetToPage_AndBothVerify()
    {
        (ISigner signer, _, TrustStore trust) = Phase126OperationsTests.MakeFixture();
        byte[] signed1 = SignOnce(BuildPageWithContents(), signer);

        PdfSigningOptions options = new PdfSigningOptions
        {
            SigningTime = Phase126OperationsTests.SigningTime,
            Appearance = new SignatureAppearance
            {
                PageIndex = 0,
                Rectangle = WidgetRect,
            },
        };
        byte[] signed2 = PdfCounterSigner.AddSignature(signed1, signer, options);

        // An incremental update: the first signature's bytes are untouched.
        signed2.AsSpan(0, signed1.Length).SequenceEqual(signed1).Should().BeTrue();

        AssertAllVerify(signed2, trust, expectedCount: 2);

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(signed2), leaveOpen: false);
        PdfObjectStore store = doc.Objects;
        PdfArray annots = store.ResolveAs<PdfArray>(doc.Pages[0].Dictionary[PdfName.Intern("Annots")])
            ?? throw new InvalidOperationException("The page has no /Annots.");
        annots.Count.Should().Be(1);

        PdfDictionary widget = store.ResolveAs<PdfDictionary>(annots[0])
            ?? throw new InvalidOperationException("The annotation is not a dictionary.");
        widget.GetName(PdfName.Intern("Subtype")).Should().Be(PdfName.Intern("Widget"));
        widget.GetName(PdfName.Intern("FT")).Should().Be(PdfName.Intern("Sig"));
        widget.ContainsKey(PdfName.Intern("AP")).Should().BeTrue();

        PdfArray rect = store.ResolveAs<PdfArray>(widget[PdfName.Intern("Rect")])
            ?? throw new InvalidOperationException("The widget has no /Rect.");
        rect.Count.Should().Be(4);
        ((PdfReal)rect[2]).Value.Should().Be(272.0);
    }

    [Fact]
    public void AddSignature_WithoutAppearance_LeavesThePageAlone()
    {
        (ISigner signer, _, TrustStore trust) = Phase126OperationsTests.MakeFixture();
        byte[] signed1 = SignOnce(BuildPageWithContents(), signer);

        byte[] signed2 = PdfCounterSigner.AddSignature(signed1, signer, Options());

        AssertAllVerify(signed2, trust, expectedCount: 2);
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(signed2), leaveOpen: false);
        doc.Pages[0].Dictionary.ContainsKey(PdfName.Intern("Annots")).Should().BeFalse();
    }

    // ── D-226: SignAsync honours Appearance ──────────────────────────────

    [Fact]
    public async Task SignAsync_WithAppearance_AddsWidgetToPage_AndVerifies()
    {
        (ISigner signer, _, TrustStore trust) = Phase126OperationsTests.MakeFixture();
        byte[] signed;
        using (PdfDocument d = PdfDocument.Open(new MemoryStream(BuildPageWithContents()), leaveOpen: false))
        {
            signed = await PdfSigner.SignAsync(d, signer, new PdfSigningOptions
            {
                SigningTime = Phase126OperationsTests.SigningTime,
                Appearance = new SignatureAppearance
                {
                    PageIndex = 0,
                    Rectangle = WidgetRect,
                },
            });
        }

        AssertAllVerify(signed, trust, expectedCount: 1);
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(signed), leaveOpen: false);
        AssertSignatureWidgetOnPage(doc);
    }

    // ── D-226: Appearance plus LtvOptions ────────────────────────────────

    [Fact]
    public void Sign_WithAppearanceAndLtv_KeepsBothTheWidgetAndTheDss()
    {
        (ISigner signer, X509Certificate cert, TrustStore trust) = Phase126OperationsTests.MakeFixture();
        byte[] signed;
        using (PdfDocument d = PdfDocument.Open(new MemoryStream(BuildPageWithContents()), leaveOpen: false))
        {
            signed = PdfSigner.Sign(d, signer, AppearanceAndLtv(cert));
        }

        AssertWidgetAndDss(signed, cert, trust);
    }

    [Fact]
    public async Task SignAsync_WithAppearanceAndLtv_KeepsBothTheWidgetAndTheDss()
    {
        (ISigner signer, X509Certificate cert, TrustStore trust) = Phase126OperationsTests.MakeFixture();
        byte[] signed;
        using (PdfDocument d = PdfDocument.Open(new MemoryStream(BuildPageWithContents()), leaveOpen: false))
        {
            signed = await PdfSigner.SignAsync(d, signer, AppearanceAndLtv(cert));
        }

        AssertWidgetAndDss(signed, cert, trust);
    }

    private static PdfSigningOptions AppearanceAndLtv(X509Certificate cert) => new PdfSigningOptions
    {
        SigningTime = Phase126OperationsTests.SigningTime,
        Appearance = new SignatureAppearance
        {
            PageIndex = 0,
            Rectangle = WidgetRect,
        },
        LtvOptions = new LtvOptions
        {
            Certificates = new[] { cert },
        },
    };

    // Before D-226 the appearance XObject and the first LTV stream were both
    // numbered acroFormId + 1, and only one of them was written.
    private static void AssertWidgetAndDss(byte[] signed, X509Certificate cert, TrustStore trust)
    {
        AssertAllVerify(signed, trust, expectedCount: 1);

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(signed), leaveOpen: false);
        AssertSignatureWidgetOnPage(doc);

        DocumentSecurityStore dss = doc.GetDocumentSecurityStore()
            ?? throw new InvalidOperationException("The signed document has no /DSS.");
        dss.Certificates.Should().HaveCount(1);
        dss.Certificates[0].RawEncoding.Should().Equal(cert.RawEncoding);
    }

    private static void AssertSignatureWidgetOnPage(PdfDocument doc)
    {
        PdfObjectStore store = doc.Objects;
        PdfArray annots = store.ResolveAs<PdfArray>(doc.Pages[0].Dictionary[PdfName.Intern("Annots")])
            ?? throw new InvalidOperationException("The page has no /Annots.");
        annots.Count.Should().Be(1);

        PdfDictionary widget = store.ResolveAs<PdfDictionary>(annots[0])
            ?? throw new InvalidOperationException("The annotation is not a dictionary.");
        widget.GetName(PdfName.Intern("Subtype")).Should().Be(PdfName.Intern("Widget"));

        PdfDictionary ap = store.ResolveAs<PdfDictionary>(widget[PdfName.Intern("AP")])
            ?? throw new InvalidOperationException("The widget has no /AP.");
        PdfStream normal = store.Resolve(ap[PdfName.Intern("N")]) as PdfStream
            ?? throw new InvalidOperationException("/AP /N is not a stream.");
        normal.Dictionary.GetName(PdfName.Intern("Subtype")).Should().Be(PdfName.Intern("Form"));
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static PdfSigningOptions Options() => new PdfSigningOptions
    {
        SigningTime = Phase126OperationsTests.SigningTime,
    };

    private static byte[] SignOnce(byte[] pdf, ISigner signer)
    {
        using PdfDocument d = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        return PdfSigner.Sign(d, signer, Options());
    }

    internal static void AssertAllVerify(byte[] pdf, TrustStore trust, int expectedCount)
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        doc.Signatures().Should().HaveCount(expectedCount);
        foreach (PdfSignature sig in doc.Signatures())
        {
            SignatureVerificationResult r = sig.Verify(doc, new()
            {
                TrustStore = trust,
                ValidationTime = Phase126OperationsTests.SigningTime,
            });
            r.IsValid.Should().BeTrue($"signature '{sig.FieldName}' must verify: {r.Message}");
        }
    }

    // One page whose /Contents is a stream, written before the signature
    // dictionary: the layout every real document has, and the one Sign used
    // to fail on, because it took the page's /Contents for the signature's.
    internal static byte[] BuildPageWithContents()
    {
        PdfObjectId catalogId = new(1, 0);
        PdfObjectId pagesId = new(2, 0);
        PdfObjectId pageId = new(3, 0);
        PdfObjectId contentsId = new(4, 0);

        PdfDictionary catalog = new();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(pagesId));

        PdfDictionary pages = new();
        pages.Set(PdfName.Type, PdfName.Pages);
        PdfArray kids = new();
        kids.Add(new PdfReference(pageId));
        pages.Set(PdfName.Kids, kids);
        pages.Set(PdfName.Count, 1);

        PdfDictionary page = new();
        page.Set(PdfName.Type, PdfName.Page);
        page.Set(PdfName.Intern("Parent"), new PdfReference(pagesId));
        PdfArray mediaBox = new();
        mediaBox.Add(new PdfInteger(0));
        mediaBox.Add(new PdfInteger(0));
        mediaBox.Add(new PdfInteger(612));
        mediaBox.Add(new PdfInteger(792));
        page.Set(PdfName.MediaBox, mediaBox);
        page.Set(PdfName.Resources, new PdfDictionary());
        page.Set(PdfName.Contents, new PdfReference(contentsId));

        byte[] content = "0 0 1 rg 72 600 200 100 re f\n"u8.ToArray();
        PdfDictionary contentsDict = new();
        contentsDict.Set(PdfName.Length, content.Length);

        PdfDictionary trailer = new();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));
        MemoryStream ms = new();
        PdfWriter.Write(
            ms,
            new[]
            {
                new PdfIndirectObject(catalogId, catalog),
                new PdfIndirectObject(pagesId, pages),
                new PdfIndirectObject(pageId, page),
                new PdfIndirectObject(contentsId, new PdfStream(contentsDict, content)),
            },
            trailer);
        return ms.ToArray();
    }
}
