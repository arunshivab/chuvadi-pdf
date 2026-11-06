// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.2.6 — combined tests for SignAsync, PdfLtvUpdater, PdfCounterSigner, appearance widgets

using System;
using System.IO;
using Chuvadi.Cryptography.Hashing;
using Chuvadi.Cryptography.PathValidation;
using Chuvadi.Cryptography.PublicKey;
using Chuvadi.Cryptography.Signing;
using Chuvadi.Cryptography.X509;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Signatures.Dss;
using Chuvadi.Pdf.Signatures.Verification;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Signatures.Tests.Signing;

public sealed class Phase126OperationsTests
{
    private const string Pkcs8Hex =
        "308204bf020100300d06092a864886f70d0101010500048204a9308204a50201000282010100b9c5" +
        "0729fca5b148ff03a6fabc563f64fd73941a2176ee259731707285628a70c024f897a6ac3c956687" +
        "5048f74a1c09cb631c50842b465bfbd432b81f9c2ea7a989d6bfb462a2dadee693731f99061e849c" +
        "cad1da67443428ccb8829c9951832322334b030f0b4e15303d1872cde4baf5a83046f5c26cbf5454" +
        "88b14b157ff6ee06e5562fcd5096c107c99655b7287c0348454d6d99686e9790936713bc7d3626ad" +
        "73939e7c38e2aff4ee618381dbae9835cc087ea4ff767f015d5f64c855b8a8e5fb376be746d67bb0" +
        "c20303868d056b9aa4faf015e31cf0ed12d4aa2f9584de6d986204b5878a61626a03896fe0d7b649" +
        "66305b7a35da54fa267bc3a9c46f0203010001028201000f23f26edc45da2a7633a355a6d3eb5c1d" +
        "16b668b576d94ca703432ee795731862bb4b522626137f4f03e83f34d30d709eeaf7a67849d94a2b" +
        "3ecd836ed01932abac02f1f78f07c6dede86ab48aaa81d04a1e2c5dd0f083802bb4203cdcf911d27" +
        "028fe64fdc2cb25dcd0e0302c2ffc95d0c2acdc869e2d86a7f1944ef36feb42032e0148d02e21ea9" +
        "9812f5e48a63f016b46e21e05e5a2ed2f3122f0ba3923fd5e1938d4d0914bcadf57329739a838c7d" +
        "019e979607d5f514ace83f7d71cee024e79cfd263637cf62a769f1877e3d878afb6ebf4466a8cf57" +
        "2675d07bcd709354fb1a540b077e0b8fb4ed187220357fa2748406b63f4a320b7430f6da8d6d0102" +
        "818100f8889a6a8cf51815cd678057cf01559a04c5e111957f4952ff39d7e38af6c52d7c4e2fb834" +
        "0d49c907916c4d4df5f836d5ce03fdba4e59effd0116afb661565892417a6150ada0560f7859689a" +
        "b7f5ef0171a8208231fcc67294a62221acd7463ea9d5547554e5d0adf2201092d4d32fd913201c6a" +
        "67d50deceaa31d8a34b33102818100bf59b9e2b37c9fb2dfe05afaaf15877c21d2ce860b78525dcf" +
        "349e0436b8c89d0b70529bddebf92d3606aa642604f6afd733769205ad1c060fe69b365fbceeec1c" +
        "eab5026e23a2661a9bc03c049dc845ade23a1262decb152f771dc332d9d38d372a2b62ade1cb5f52" +
        "067d32c6e6dc95b7b16d0854797f894abfcdfd6ecdc99f02818100f0b5cd68f950c09d0d2dfb7e10" +
        "3de89c9d96d19fe83d39d52ae0e919b713be71897d68766de398dd1d79597d9dce673324ecbdacd6" +
        "eedfe8b21085da7537dd1b37bc373d5d986c3c2e0b8ffce22cde033850ce577e01d0229c0320ccd9" +
        "f4bf2387b991a695653e985880b3519a048aee42be655160356482723de6f1cb53b3610281810098" +
        "b5f41b0fe1a2d62fc3aef827e907b2b28fba10d270995392bd4c6ad27d5065bd2e4c4f66a21fbfcc" +
        "412f95339e7c7dc342a81b4b7a674613449894a17d783469b38af8408c21dc58d9fa662bccfc7b57" +
        "959780faf511a07bbc15bda6049fc830c16fd4962f008eb738c48c549f04665c2eb674926e50b172" +
        "3d77190e681fc302818100ca034bbd527beb5754ed24c17281b0b7ab403fec106489a97ee8f4c307" +
        "e258ae58fc7ecf74ee9739ff80f40862aefe8b4adab12eb541351f738129b11d17cd9d4a4164cae0" +
        "2ee6611641b708668398fa32d6b895ea1d612f6fcff268f06f9602d6ad541ac689ff14396d20c90d" +
        "c3be3335608a82115ae1aad1563b8eceae6262";

    private const string CertDerHex =
        "308202e8308201d0a003020102020101300d06092a864886f70d01010b0500302531233021060355" +
        "04030c1a436875766164692053656c662d5369676e6564205369676e6572301e170d323430313031" +
        "3030303030305a170d3330303130313030303030305a30253123302106035504030c1a4368757661" +
        "64692053656c662d5369676e6564205369676e657230820122300d06092a864886f70d0101010500" +
        "0382010f003082010a0282010100b9c50729fca5b148ff03a6fabc563f64fd73941a2176ee259731" +
        "707285628a70c024f897a6ac3c9566875048f74a1c09cb631c50842b465bfbd432b81f9c2ea7a989" +
        "d6bfb462a2dadee693731f99061e849ccad1da67443428ccb8829c9951832322334b030f0b4e1530" +
        "3d1872cde4baf5a83046f5c26cbf545488b14b157ff6ee06e5562fcd5096c107c99655b7287c0348" +
        "454d6d99686e9790936713bc7d3626ad73939e7c38e2aff4ee618381dbae9835cc087ea4ff767f01" +
        "5d5f64c855b8a8e5fb376be746d67bb0c20303868d056b9aa4faf015e31cf0ed12d4aa2f9584de6d" +
        "986204b5878a61626a03896fe0d7b64966305b7a35da54fa267bc3a9c46f0203010001a323302130" +
        "0f0603551d130101ff040530030101ff300e0603551d0f0101ff0404030202c4300d06092a864886" +
        "f70d01010b0500038201010074653878a8af7257342375f9632ba548b2b081af0364a8a1068ca5fc" +
        "456597f85d2a91b119be39d3ff2acbd482ba0bc5f31babaae1693ce2349b7747eac9b9961b64a6e6" +
        "99f3520170e01fa3c86263a6570f375847839f11de079f11d13087a6dab8f29e448aef7e700a5b2e" +
        "ab6b3216dee2acf61262e51ecacd40b0f54b02f552a49d3b3609fd4db14f5ce2f8e2b998454468ff" +
        "eca1d0093a2a5b16690bf10fdac4fc2e38d34b8bc8c72e8e425cbfd000205cff675623e5db07b2b6" +
        "3312db2f93867ec28c1d982298d25090a3df33ca65b91bf7682047d796b8c76f1fd4abf7c1cd9ccf" +
        "0e61017c2bc53f7bbd30d00efce40ba96871fb467a1c8741057e7846";

    internal static readonly DateTimeOffset SigningTime
        = new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);

    internal static (ISigner Signer, X509Certificate Cert, TrustStore Trust) MakeFixture()
    {
        RsaPrivateKey priv = RsaPrivateKey.FromPkcs8(Convert.FromHexString(Pkcs8Hex));
        X509Certificate cert = X509Certificate.Decode(Convert.FromHexString(CertDerHex));
        TrustStore trust = new();
        trust.Add(cert);
        return (new RsaPkcs1V15Signer(priv, cert, HashAlgorithmName.Sha256), cert, trust);
    }

    // ── SignAsync ────────────────────────────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task SignAsync_WithoutTsa_ProducesValidSignature()
    {
        (ISigner signer, _, TrustStore trust) = MakeFixture();
        byte[] unsigned_ = BuildMinimalPdf();
        byte[] signed_;
        using (PdfDocument d = PdfDocument.Open(new MemoryStream(unsigned_), leaveOpen: false))
        {
            signed_ = await Chuvadi.Pdf.Signatures.Signing.PdfSigner.SignAsync(
                d, signer, new Chuvadi.Pdf.Signatures.Signing.PdfSigningOptions { SigningTime = SigningTime });
        }
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(signed_), leaveOpen: false);
        SignatureVerificationResult r = doc.Signatures()[0].Verify(doc, new()
        {
            TrustStore = trust,
            ValidationTime = SigningTime,
        });
        r.IsValid.Should().BeTrue(r.Message);
    }

    // ── PdfLtvUpdater ────────────────────────────────────────────────────

    [Fact]
    public void AddLtvMaterial_AppendsDssAndPreservesSignature()
    {
        (ISigner signer, X509Certificate cert, TrustStore trust) = MakeFixture();
        byte[] signed_ = SignFresh(signer);
        byte[] withLtv = Chuvadi.Pdf.Signatures.Signing.PdfLtvUpdater.AddLtvMaterial(
            signed_,
            new Chuvadi.Pdf.Signatures.Signing.LtvOptions
            {
                Certificates = new[] { cert },
                IncludeVri = true,
            });

        // Original prefix preserved.
        for (int i = 0; i < signed_.Length; i++)
        {
            withLtv[i].Should().Be(signed_[i]);
        }

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(withLtv), leaveOpen: false);
        DocumentSecurityStore? dss = doc.GetDocumentSecurityStore();
        dss.Should().NotBeNull();
        dss!.Certificates.Should().HaveCount(1);
        dss.Vri.Should().HaveCount(1);

        SignatureVerificationResult r = doc.Signatures()[0].Verify(doc, new()
        {
            TrustStore = trust,
            ValidationTime = SigningTime,
        });
        r.IsValid.Should().BeTrue(r.Message);
    }

    [Fact]
    public void AddLtvMaterial_NoMaterial_Throws()
    {
        (ISigner signer, _, _) = MakeFixture();
        byte[] signed_ = SignFresh(signer);
        Action act = () => Chuvadi.Pdf.Signatures.Signing.PdfLtvUpdater.AddLtvMaterial(
            signed_, new Chuvadi.Pdf.Signatures.Signing.LtvOptions());
        act.Should().Throw<ArgumentException>();
    }

    // ── PdfCounterSigner ─────────────────────────────────────────────────

    [Fact]
    public void CounterSign_AddsSecondSignatureAndBothVerify()
    {
        (ISigner signer, _, TrustStore trust) = MakeFixture();
        byte[] signed1 = SignFresh(signer);

        byte[] signed2 = Chuvadi.Pdf.Signatures.Signing.PdfCounterSigner.AddSignature(
            signed1, signer,
            new Chuvadi.Pdf.Signatures.Signing.PdfSigningOptions
            {
                SigningTime = SigningTime,
                Reason = "Counter",
            });

        for (int i = 0; i < signed1.Length; i++)
        {
            signed2[i].Should().Be(signed1[i]);
        }

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(signed2), leaveOpen: false);
        doc.Signatures().Should().HaveCount(2);

        foreach (PdfSignature sig in doc.Signatures())
        {
            SignatureVerificationResult r = sig.Verify(doc, new()
            {
                TrustStore = trust,
                ValidationTime = SigningTime,
            });
            r.IsValid.Should().BeTrue(
                $"signature '{sig.FieldName}' must verify after counter-signing: {r.Message}");
        }
    }

    // ── Signature Appearance ─────────────────────────────────────────────

    [Fact]
    public void Sign_WithAppearance_AddsWidgetAnnotation()
    {
        (ISigner signer, _, TrustStore trust) = MakeFixture();
        byte[] unsigned_ = BuildMinimalPdfWithPage();
        byte[] signed_;
        using (PdfDocument d = PdfDocument.Open(new MemoryStream(unsigned_), leaveOpen: false))
        {
            signed_ = Chuvadi.Pdf.Signatures.Signing.PdfSigner.Sign(d, signer,
                new Chuvadi.Pdf.Signatures.Signing.PdfSigningOptions
                {
                    SigningTime = SigningTime,
                    Appearance = new Chuvadi.Pdf.Signatures.Signing.SignatureAppearance
                    {
                        PageIndex = 0,
                        Rectangle = new[] { 50.0, 50.0, 250.0, 150.0 },
                    },
                });
        }

        string asText = System.Text.Encoding.Latin1.GetString(signed_);
        asText.Should().Contain("/Subtype /Widget");
        asText.Should().Contain("/AP");

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(signed_), leaveOpen: false);
        SignatureVerificationResult r = doc.Signatures()[0].Verify(doc, new()
        {
            TrustStore = trust,
            ValidationTime = SigningTime,
        });
        r.IsValid.Should().BeTrue(r.Message);
    }

    [Fact]
    public void Sign_WithAppearance_OutOfRangePageIndex_Throws()
    {
        (ISigner signer, _, _) = MakeFixture();
        byte[] unsigned_ = BuildMinimalPdfWithPage();
        using PdfDocument d = PdfDocument.Open(new MemoryStream(unsigned_), leaveOpen: false);
        Action act = () => Chuvadi.Pdf.Signatures.Signing.PdfSigner.Sign(d, signer,
            new Chuvadi.Pdf.Signatures.Signing.PdfSigningOptions
            {
                SigningTime = SigningTime,
                Appearance = new Chuvadi.Pdf.Signatures.Signing.SignatureAppearance
                {
                    PageIndex = 99,
                    Rectangle = new[] { 0.0, 0.0, 100.0, 100.0 },
                },
            });
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static byte[] SignFresh(ISigner signer)
    {
        byte[] unsigned_ = BuildMinimalPdf();
        using PdfDocument d = PdfDocument.Open(new MemoryStream(unsigned_), leaveOpen: false);
        return Chuvadi.Pdf.Signatures.Signing.PdfSigner.Sign(d, signer,
            new Chuvadi.Pdf.Signatures.Signing.PdfSigningOptions { SigningTime = SigningTime });
    }

    private static byte[] BuildMinimalPdf()
    {
        PdfObjectId catalogId = new(1, 0);
        PdfObjectId pagesId = new(2, 0);
        PdfDictionary catalog = new();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(pagesId));
        PdfDictionary pages = new();
        pages.Set(PdfName.Type, PdfName.Pages);
        pages.Set(PdfName.Kids, new PdfArray());
        pages.Set(PdfName.Count, 0);
        PdfDictionary trailer = new();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));
        MemoryStream ms = new();
        PdfWriter.Write(ms,
            new[] { new PdfIndirectObject(catalogId, catalog), new PdfIndirectObject(pagesId, pages) },
            trailer);
        return ms.ToArray();
    }

    private static byte[] BuildMinimalPdfWithPage()
    {
        PdfObjectId catalogId = new(1, 0);
        PdfObjectId pagesId = new(2, 0);
        PdfObjectId pageId = new(3, 0);

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
        page.Set(PdfName.Type, PdfName.Intern("Page"));
        page.Set(PdfName.Intern("Parent"), new PdfReference(pagesId));
        PdfArray mediaBox = new();
        mediaBox.Add(new PdfReal(0));
        mediaBox.Add(new PdfReal(0));
        mediaBox.Add(new PdfReal(612));
        mediaBox.Add(new PdfReal(792));
        page.Set(PdfName.Intern("MediaBox"), mediaBox);
        page.Set(PdfName.Intern("Resources"), new PdfDictionary());

        PdfDictionary trailer = new();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));
        MemoryStream ms = new();
        PdfWriter.Write(ms,
            new[]
            {
                new PdfIndirectObject(catalogId, catalog),
                new PdfIndirectObject(pagesId, pages),
                new PdfIndirectObject(pageId, page),
            },
            trailer);
        return ms.ToArray();
    }
}
