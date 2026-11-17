// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — End-to-end signature verification tests
//
// The fixtures embedded below are real CMS SignedData blobs (RSA-SHA256 and
// ECDSA-P256-SHA256) produced by Python's cryptography library (OpenSSL).
// Each test builds a synthetic PDF whose byte range covers a known payload,
// extracts the signature, and verifies it end-to-end.

using System;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Signatures.Verification;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Signatures.Tests.Verification;

public sealed class PdfSignatureVerifyEndToEndTests
{
    // RSA-SHA256 fixture (Python cryptography pkcs7 + DetachedSignature + Binary)
    private const string RsaSignedBytesHex = "255044462d312e370a2e2e2e20646f63756d656e7420636f6e74656e7420746865207369676e617475726520636f76657273202e2e2e";

    private const string RsaCmsBlobHex =
        "308204d106092a864886f70d010702a08204c2308204be020101310f300d06096086480165030402" +
        "010500300b06092a864886f70d010701a08202aa308202a63082018ea00302010202021234300d06" +
        "092a864886f70d01010b050030163114301206035504030c0b54657374205369676e6572301e170d" +
        "3234303130313030303030305a170d3330303130313030303030305a30163114301206035504030c" +
        "0b54657374205369676e657230820122300d06092a864886f70d01010105000382010f003082010a" +
        "0282010100c53675b2ceeca31d4ec041dc51bf6b604eaf7db24d18928d9ea12a4c6b154eb7c26fc1" +
        "2565f1ddc9683838df244ae285f894239322250df93d0d106dd71a5f54d1a7a876e320d91637fd14" +
        "134ce194e8f8f97e0d37d8b308553b40ccc2a31ef4b2b5e09f2f5e420f5ad53c0343567645dc431c" +
        "0468eb49b3089377ae64d468ad8045c5c158743fa02bd333ae65df01e39ea1a52baee3de5ee900e0" +
        "be1eacc5fc9ed060ed5abbb98a72b4bd862177b91b7098d3d0b00d5c83245426543852945016d133" +
        "c4bdd0cea581dd5bdf3de7b8fbf78d622d92e5ec89c8518a5dee02551ca027abea4d3d9c2ea4b84d" +
        "d029ae1a0581fd3b88ea201d907a6bf88927df76dd0203010001300d06092a864886f70d01010b05" +
        "00038201010003a39726281b99393cd4c0eb6f0225ae26fd9bd9e89a099b50546393fa7477210d4c" +
        "2b94ff6d9564364a50a4a5229cb1c8cc7fd1fcd81617b6a7002bb8ea7265d9a3b396ebde6a67cce6" +
        "b90301b2fb4c2c8aa54023d733a27272ae978f989ed5ebeb4b65533951a72412e929b90bfc6ff2a9" +
        "1bee011524ceb5d074cefe78658726fc1d816fa398c38971be509dc464edbf0c2b1c7356855e89c4" +
        "86e01c1b8898f66c568aba2611f52db346f43d33798496d6d96dad5e889588fd9ee3662253c28d74" +
        "310e8a6edade948ef5e520e381e2613868a39466b4b6d9fa33a131a631227c2f1121508783723353" +
        "bf5d5a556e2fe31657dfd25ac4fff716b5e8d059e0ee318201eb308201e7020101301c3016311430" +
        "1206035504030c0b54657374205369676e657202021234300d06096086480165030402010500a081" +
        "a1301806092a864886f70d010903310b06092a864886f70d010701301c06092a864886f70d010905" +
        "310f170d3236303531363136343532335a302f06092a864886f70d01090431220420e3bf39f73e4c" +
        "bf4aa14100d667ea982459612296375132aaa0e01d6b62c5e26d303606092a864886f70d01090f31" +
        "293027300b060960864801650304012a300b0609608648016503040116300b060960864801650304" +
        "0102300d06092a864886f70d0101010500048201001c72eca16c31cf802492c271a6343cd40ce38d" +
        "5e0d8330c5ab7527bd3d523dfb88b94718c5647613165ec4626f449e1f6223ac08034b3f3ab912d5" +
        "263efb48045f2e3af91fed57c4f51fd1519fcf0367a560876c8c470410ce984d845d15e81158a5b9" +
        "abe5c5717e6072c3592d6aa14f20b0a6e82c5bc6829921e60430875c99e0267fc879acad103fc639" +
        "d546869264b782a0771e58d0594783059cc411a4c1c69beb568c83521097a869266552cbfff7870d" +
        "67063e078daa94d18ee065dad1cf6bf2475f02e0d92172d7e9faaa2c0cfca4a5a935a28ec8cf4d67" +
        "69a03b50a898d5b1d89c0f9bb8671c5502d081115e89467217ba13c2e1632fdbad915df5bc";

    // ECDSA-P256-SHA256 fixture
    private const string EcSignedBytesHex = "255044462d312e370a2e2e2e20646f63756d656e7420636f6e74656e7420746865207369676e617475726520636f76657273202e2e2e";

    private const string EcCmsBlobHex =
        "3082028806092a864886f70d010702a082027930820275020101310f300d06096086480165030402" +
        "010500300b06092a864886f70d010701a082011e3082011a3081c0a00302010202025678300a0608" +
        "2a8648ce3d04030230163114301206035504030c0b54657374205369676e6572301e170d32343031" +
        "30313030303030305a170d3330303130313030303030305a30163114301206035504030c0b546573" +
        "74205369676e65723059301306072a8648ce3d020106082a8648ce3d0301070342000468674f9c87" +
        "04ca62f22106494d8873f495237738b6ea1a28c8f23dfc1cd6b1c87c138c85b20c005cbe76ec611f" +
        "b0a6e3196b8c8736338de6aea6da281fdc763e300a06082a8648ce3d040302034900304602210096" +
        "9531dbd876129b7aafb811298c3aa4bf3bdd5fc22270c4e710974f9bd7cf5f022100f0cc4facb21b" +
        "4dedaa10a149d7baacd3555954b1a85c6c2294d885655dfeac323182012e3082012a020101301c30" +
        "163114301206035504030c0b54657374205369676e657202025678300d0609608648016503040201" +
        "0500a081a1301806092a864886f70d010903310b06092a864886f70d010701301c06092a864886f7" +
        "0d010905310f170d3236303531363136343532335a302f06092a864886f70d01090431220420e3bf" +
        "39f73e4cbf4aa14100d667ea982459612296375132aaa0e01d6b62c5e26d303606092a864886f70d" +
        "01090f31293027300b060960864801650304012a300b0609608648016503040116300b0609608648" +
        "016503040102300a06082a8648ce3d04030204483046022100c7040adc75380852e48054350eac1e" +
        "e4b19879fb0ce352d0c607a0d53f0d57db0221009b911143ca4c07add24ade9e7dcb36158f179806" +
        "207d770c1cf12d85a9e2ecef";

    [Fact]
    public void Verify_RsaSha256_ValidSignature_ReturnsValid()
    {
        byte[] signedBytes = Convert.FromHexString(RsaSignedBytesHex);
        byte[] cms = Convert.FromHexString(RsaCmsBlobHex);

        using PdfDocument doc = BuildPdfWithSignature(signedBytes, cms);
        SignatureVerificationResult result = doc.Signatures()[0].Verify(doc);

        // 3.35.0 (D-396): the maths holds, but the fixture's /ByteRange
        // [0 n n 0] signs only a prefix, and no trust store is given, so the
        // signature is not valid.
        result.IntegrityVerified.Should().BeTrue();
        result.TrustStatus.Should().Be(SignatureVerificationStatus.TrustUnknown);
        result.ModificationsAfterSigning.Should().Be(SignatureModifications.Other);
        result.Status.Should().Be(SignatureVerificationStatus.ModifiedAfterSigning);
        result.IsValid.Should().BeFalse();
        result.SignerCertificate.Should().NotBeNull();
    }

    [Fact]
    public void Verify_RsaSha256_TamperedByteRange_ReturnsDigestMismatch()
    {
        byte[] signedBytes = Convert.FromHexString(RsaSignedBytesHex);
        byte[] cms = Convert.FromHexString(RsaCmsBlobHex);
        signedBytes[10] ^= 0x01;  // flip a bit in the signed region

        using PdfDocument doc = BuildPdfWithSignature(signedBytes, cms);
        SignatureVerificationResult result = doc.Signatures()[0].Verify(doc);

        result.Status.Should().Be(SignatureVerificationStatus.DigestMismatch);
        result.IntegrityVerified.Should().BeFalse();
    }

    [Fact]
    public void Verify_EcdsaP256Sha256_ValidSignature_ReturnsValid()
    {
        byte[] signedBytes = Convert.FromHexString(EcSignedBytesHex);
        byte[] cms = Convert.FromHexString(EcCmsBlobHex);

        using PdfDocument doc = BuildPdfWithSignature(signedBytes, cms);
        SignatureVerificationResult result = doc.Signatures()[0].Verify(doc);

        result.IntegrityVerified.Should().BeTrue();
        result.TrustStatus.Should().Be(SignatureVerificationStatus.TrustUnknown);
    }

    [Fact]
    public void Verify_EcdsaP256Sha256_TamperedByteRange_ReturnsDigestMismatch()
    {
        byte[] signedBytes = Convert.FromHexString(EcSignedBytesHex);
        byte[] cms = Convert.FromHexString(EcCmsBlobHex);
        signedBytes[10] ^= 0x01;

        using PdfDocument doc = BuildPdfWithSignature(signedBytes, cms);
        SignatureVerificationResult result = doc.Signatures()[0].Verify(doc);

        result.Status.Should().Be(SignatureVerificationStatus.DigestMismatch);
    }

    [Fact]
    public void Verify_NonCmsBasedSubFilter_ReturnsUnsupportedSubFilter()
    {
        // Build a signature with the legacy adbe.x509.rsa_sha1 SubFilter.
        byte[] signedBytes = Convert.FromHexString(RsaSignedBytesHex);
        byte[] cms = Convert.FromHexString(RsaCmsBlobHex);
        using PdfDocument doc = BuildPdfWithSignature(signedBytes, cms, subFilter: "adbe.x509.rsa_sha1");
        SignatureVerificationResult result = doc.Signatures()[0].Verify(doc);
        result.Status.Should().Be(SignatureVerificationStatus.UnsupportedSubFilter);
    }

    private static PdfDocument BuildPdfWithSignature(byte[] signedBytes, byte[] cms,
        string subFilter = "adbe.pkcs7.detached")
    {
        PdfObjectId catalogId = new(1, 0);
        PdfObjectId pagesId = new(2, 0);
        PdfObjectId acroFormId = new(3, 0);
        PdfObjectId sigFieldId = new(4, 0);
        PdfObjectId sigDictId = new(5, 0);

        PdfArray byteRange = new(new PdfPrimitive[]
        {
            new PdfInteger(0),
            new PdfInteger(signedBytes.Length),
            new PdfInteger(signedBytes.Length),
            new PdfInteger(0),
        });

        PdfDictionary sigDict = new();
        sigDict.Set(PdfName.Type, PdfName.Intern("Sig"));
        sigDict.Set(PdfName.Filter, PdfName.Intern("Adobe.PPKLite"));
        sigDict.Set(PdfName.Intern("SubFilter"), PdfName.Intern(subFilter));
        sigDict.Set(PdfName.Intern("ByteRange"), byteRange);
        sigDict.Set(PdfName.Intern("Contents"), new PdfString(cms, preferHexForm: true));

        PdfDictionary sigField = new();
        sigField.Set(PdfName.Intern("FT"), PdfName.Intern("Sig"));
        sigField.Set(PdfName.Intern("T"), new PdfString("Signature1"));
        sigField.Set(PdfName.Intern("V"), new PdfReference(sigDictId));

        PdfDictionary acroForm = new();
        acroForm.Set(PdfName.Intern("Fields"), new PdfArray(new PdfPrimitive[] {
            new PdfReference(sigFieldId)
        }));

        PdfDictionary catalog = new();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(pagesId));
        catalog.Set(PdfName.Intern("AcroForm"), new PdfReference(acroFormId));

        PdfDictionary pages = new();
        pages.Set(PdfName.Type, PdfName.Pages);
        pages.Set(PdfName.Kids, new PdfArray(Array.Empty<PdfPrimitive>()));
        pages.Set(PdfName.Count, 0);

        PdfIndirectObject[] objects =
        {
            new(catalogId, catalog),
            new(pagesId, pages),
            new(acroFormId, acroForm),
            new(sigFieldId, sigField),
            new(sigDictId, sigDict),
        };

        PdfDictionary trailer = new();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream pdfStream = new();
        pdfStream.Write(signedBytes, 0, signedBytes.Length);
        PdfWriter.Write(pdfStream, objects, trailer);
        pdfStream.Position = 0;

        return PdfDocument.Open(pdfStream, leaveOpen: false);
    }
}
