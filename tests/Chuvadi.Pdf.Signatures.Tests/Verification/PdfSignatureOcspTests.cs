// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — End-to-end OCSP tests via signature.Verify
//
// Fixture: full chain (Root → Intermediate → Leaf) + two OCSP responses signed
// by Intermediate — one Good and one Revoked.

using System;
using System.IO;
using Chuvadi.Cryptography.Ocsp;
using Chuvadi.Cryptography.PathValidation;
using Chuvadi.Cryptography.X509;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Signatures.Verification;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Signatures.Tests.Verification;

public sealed class PdfSignatureOcspTests
{
    private const string SignedBytesHex = "255044462d312e370a2e2e2e204f435350207465737420646f63756d656e74202e2e2e0a";

    private const string CmsBlobHex =
        "308207eb06092a864886f70d010702a08207dc308207d8020101310f300d06096086480165030402" +
        "010500300b06092a864886f70d010701a08205ba308202d7308201bfa003020102020142300d0609" +
        "2a864886f70d01010b05003021311f301d06035504030c164f435350205465737420496e7465726d" +
        "656469617465301e170d3234303130313030303030305a170d3237303130313030303030305a301b" +
        "3119301706035504030c104f4353502054657374205369676e657230820122300d06092a864886f7" +
        "0d01010105000382010f003082010a0282010100c12825affd818591aaa5f11fdcb5504f90506086" +
        "c65fdd65cd64d135c1312278669f665b346f769d7ea93233ac178879de7c3e2ca80279ed57606eda" +
        "db8ad5fc8ec55b4b96369c963f5d0217d5bc447b52fd358d0b32a1006ac587320abe69f88f5a0e34" +
        "16f12bdab665293241157b321dab7c1c93a8df885440547509151949d7890f39379be9b44819085b" +
        "f96ef5558f0b34be7c12e02d8ac380b0cec0d4c72547dd360e66f9824adfaa9448519d824cfd86f9" +
        "58ed8f95fffae40f666e6b874bd520cd1c542bcdf43f9579000699200a8229ce6907dfb09cd54609" +
        "d312a9a48d03a9b4e7d5a8f3e0b27c3870b0e2055ec726849f7d62084f3fc8ebe95e13bf02030100" +
        "01a320301e300c0603551d130101ff04023000300e0603551d0f0101ff0404030206c0300d06092a" +
        "864886f70d01010b0500038201010088b7128498deee19e8d9e0546037a25e05eacf0ac2c751c618" +
        "eba8cb4759d775dd061ea197c049073f88295adad6e682e6a6a2a7ffc1d1f730213021072b82f3ac" +
        "54d57acba802f258425d2c21e712ac168cbd703f75f29288f8afeab0a718964779b6f4ad73809cf4" +
        "6783edb30d6b5bcd611f059e62ad0f9d2afa8888dbb8298f65a1b029687ee64c1baba660fba2e420" +
        "2fa41692e2f21b6f6db8b60df55418bb8c825f28fda42ace2c7316251322f19686e837d11505ee2f" +
        "0a680de9ea4be0e9359d3e725717a1eb11e6eb8208f2809b96cde9293cbf9dc8d6378a84bc8e729a" +
        "15b4bfe5126975acd0c18786a0bde7a0e437165be4d4cb90e7d090a25c1927308202db308201c3a0" +
        "03020102020102300d06092a864886f70d01010b050030193117301506035504030c0e4f43535020" +
        "5465737420526f6f74301e170d3232303130313030303030305a170d333230313031303030303030" +
        "5a3021311f301d06035504030c164f435350205465737420496e7465726d65646961746530820122" +
        "300d06092a864886f70d01010105000382010f003082010a0282010100dd18c02d3aebe1258389cf" +
        "7c7af9746b159e3c1f3017f107c6c88da029753f9b5927d862d61b23972658dcfd625097c08b96f5" +
        "595c732a6eaf2c123953d1eaaf15de704b30c42aaa0ed1b47c4bf58dfd03f48ce6aa7eed0c158b4a" +
        "2e07618fc4ed93faf6af45670b0f844287bb1662f1444f250ff1f158473b14f664bb0947598bb424" +
        "ed18c5dae04057b960d06415cd88c7da25e472803d73be37017a0f1c5d00c142e60d2c9ea90673ce" +
        "4d3a7bcd57643b00ec1c3f9bdae4d194d75ac87296b5694714db5e02e4b0779289951f7bc04b0e7e" +
        "e6f26a77c658f7c6a00ccd4def392613cb6e631206f5177cbc46e7febf2dff7bfdff35175939ebed" +
        "3bad3380570203010001a326302430120603551d130101ff040830060101ff020100300e0603551d" +
        "0f0101ff040403020186300d06092a864886f70d01010b0500038201010046b82adcb9d445248ec3" +
        "345a3f7744963ecc4ba63c28eae813fdedc521df9ea6110601cf47a2e4b8e1bb6f6d6ab34602f77c" +
        "84d4adac561ef3e6ba4fcb7820b32cd74bc11847260c829bf34c8969cd85bd8d73f724e35166aa3f" +
        "d8376d5d03f89e61fbfe0bab7afce65f7b9985a28564c761485238f29bb29d28688fbb944afa7ffd" +
        "c291a9773a8d9f8ccc3dbbb3d6c354358d118ad4ca62d61e4c4873e8bfbee5c6467f907d0ed4d3e4" +
        "90a00843bdf36c591575eb19ead6b214ca44c2839c8dd8274e3ccdc9c4c11d6e0d4806d86d67a738" +
        "b1505fa3a0a7c7b90fac90bc5aafb7914961f6c87e6043dd895516adf5e8f4265b3d3cc3bf63f77d" +
        "6ec77b5a6821318201f5308201f102010130263021311f301d06035504030c164f43535020546573" +
        "7420496e7465726d656469617465020142300d06096086480165030402010500a081a1301806092a" +
        "864886f70d010903310b06092a864886f70d010701301c06092a864886f70d010905310f170d3236" +
        "303531363231343431365a302f06092a864886f70d01090431220420022e62f6e26b66276ac2f0fb" +
        "ce0b8b9894e5ee6c83d983fafde9f02b59052167303606092a864886f70d01090f31293027300b06" +
        "0960864801650304012a300b0609608648016503040116300b0609608648016503040102300d0609" +
        "2a864886f70d01010105000482010028a321413f89d6163dd5afcd8fb5c126ec8098e588e686f294" +
        "ee2dae9e5cc287c60518d92d72c413a88f3e71d547a01029fea82b52e2d497ccd583beb29ae935df" +
        "05b94f850d7c205d2049c7a6c1d5140ca819cccca05234ab7fb6b2aadd85a594b96c36c1ed5f7d54" +
        "78c9c94c35b86bd16d58ad15f94b0b1a2d8b1e12de94dc90c09bc6e20c08f210c35e06266a71c9fe" +
        "57709de963619c9be4861aa1c4493017a0b3553379d635cbebdadc93c1a9ef97c0b6912f500929fa" +
        "3f3a512366b89bcc81f1163eba8a6e50735dd27b697da6bb1e76445ae699eef1a751d7d54a8cd7ec" +
        "b352cef5825d06114467bc5141d82c103eb201ec566f9258781bc626ef5172";

    private const string RootCertDerHex =
        "308202d3308201bba003020102020101300d06092a864886f70d01010b0500301931173015060355" +
        "04030c0e4f435350205465737420526f6f74301e170d3230303130313030303030305a170d343030" +
        "3130313030303030305a30193117301506035504030c0e4f435350205465737420526f6f74308201" +
        "22300d06092a864886f70d01010105000382010f003082010a0282010100c08c9e3a0807bcbff256" +
        "0b3cb4ceb2f804e9f588c9ae5fcd0083ab3b091d4dc7926c9f1c1bd78837fe853249e0d45b71d0e9" +
        "a2f8887ef3981735188c55b3c802742e013989753320c2c10cc4f882d269f2ab2d8056235c03abb7" +
        "5a2e41b89aade4c320fb53fda869e15ff3592229f27774807bd1c0624aaf31f333ce8089a99822bc" +
        "4530329acfe8af282fcffd862b1ebceff96c1bf9f17f10cae7382af8eb2cf410a773aeabf4aff0b1" +
        "efc55df26ef8408936a73f8aab89ec180807c2b0dd23e7343621041286ad9fb1a030a349a01f4261" +
        "18dbd64b9945b09f267e62641f4c8fbfc96f7b91cdcfce2c093d5d13e695af622d04e43069755330" +
        "f4f9190cbc2b0203010001a326302430120603551d130101ff040830060101ff020102300e060355" +
        "1d0f0101ff040403020106300d06092a864886f70d01010b0500038201010095711595a9815da8ca" +
        "01a86873892a6fd5107e0fd746416d5b871d63c5624185c5ceb782962cb5d4ebe22a4c108b930456" +
        "3079b0b1bf9364d9f43d575358ec3da4dd9a3072933661074d3dc7e6a827af199f0f1987c02c83bc" +
        "0165898ca8de2626894c15d01303baafa27a8585a41e4a9e840976955bd6a3c1bd58fd4039955b4c" +
        "72b9ee7405cf26910fc567f159b0cf2be0517587ca5c2191a721ab595ea047855f4f53338e31c343" +
        "43d0a20a8da7838360ae1695f25638f41ec15bfbdc918d9d47e6f2333cdcb0376d97fedcb16affa9" +
        "2f8e9873ff3a3e05bad9f09158c99bf5f1afe3b20553b01f7d5502e22d44fd8e161b2583eb82dc3b" +
        "1ebb360dc5139e";

    private const string OcspGoodDerHex =
        "308201d10a0100a08201ca308201c606092b0601050507300101048201b7308201b330819ca12330" +
        "21311f301d06035504030c164f435350205465737420496e7465726d656469617465180f32303236" +
        "303531363231343431365a30643062303a300906052b0e03021a050004146bf04ec34bc97ce92d24" +
        "88a41e5fac49c88bb3e904141a67e77bfd07f4600db6f68aee87e70348b182720201428000180f32" +
        "303235303530313030303030305aa011180f32303235303830313030303030305a300d06092a8648" +
        "86f70d01010b050003820101001121627f6782e8fd4150ae3c2c5d79d4b07f30f97d5c4819d6f58f" +
        "37a8765b49ee8c46f936df245549856d75fa98cdc03a81b2b4007a2a705452934ec2b4745785608a" +
        "714ec7d3fd029770e581dcd41dbd85d8ad19768893e85113c1fee2a2c3a3e27dece3478ef1ba02df" +
        "6240a8de981e74de693fd2e782c13d591a3148a0b89262aa9abb0934f25ce9a1002cf2e582d03323" +
        "2f12c1809969afe285af574c9a1cc8857ab5ab486d2defcdc70d4b2043281c00989a76bc61309868" +
        "11362653bfc5af35c0df599b843388cda752f36f9efdf327139641b2211af37a931747f6d8d2a6f1" +
        "1a0fc52dfdeda27ca44e9a30f142a1ecd16f6c437ab09f6eaec0582d21";

    private const string OcspRevokedDerHex =
        "308201e70a0100a08201e0308201dc06092b0601050507300101048201cd308201c93081b2a12330" +
        "21311f301d06035504030c164f435350205465737420496e7465726d656469617465180f32303236" +
        "303531363231343431365a307a3078303a300906052b0e03021a050004146bf04ec34bc97ce92d24" +
        "88a41e5fac49c88bb3e904141a67e77bfd07f4600db6f68aee87e70348b18272020142a116180f32" +
        "303235303431353030303030305aa0030a0101180f32303235303530313030303030305aa011180f" +
        "32303235303830313030303030305a300d06092a864886f70d01010b050003820101009586f2f1c5" +
        "eed155121b779cd11664f52745927b3b95a123abf78d25dcc239e0cc3133d9a1048d536bad7a0c7a" +
        "0eb64acab05b4345d9322b131f86395784fa9d00f37e79f721cb311bfe7fe20383c8b50008df942f" +
        "1fba176c507a3c6f9dfa3d5facb312f8d3dbc61f4c3e73490b89f5ba98cb29ecb2f2f92987b12e43" +
        "8b172a4c09b1dfa80bae6a525be0859cb9b7a1decc274e7a51dab6475b74fd77cdfb490975d7a2b1" +
        "6197cdeadef87c887c354597f44262447b6137cf1da92bca0777619a047daf6688f38de87e7ff129" +
        "be46b795d81e1281f16e7d1429a59fa6357d7e11de4798cebd03e60e5ed952d530a5b78f3ce1830c" +
        "4e25ed6a2b51b55de8a0d9";

    private static readonly DateTimeOffset GoodTime
        = new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private static TrustStore LoadTrustStore()
    {
        TrustStore store = new();
        store.Add(X509Certificate.Decode(Convert.FromHexString(RootCertDerHex)));
        return store;
    }

    [Fact]
    public void Verify_NoOcsp_ReturnsValidTrusted()
    {
        using PdfDocument doc = BuildPdf();
        SignatureVerifyOptions options = new()
        {
            TrustStore = LoadTrustStore(),
            ValidationTime = GoodTime,
        };
        SignatureVerificationResult r = doc.Signatures()[0].Verify(doc, options);
        r.TrustStatus.Should().Be(SignatureVerificationStatus.Valid);
        r.TrustValidated.Should().BeTrue();
    }

    [Fact]
    public void Verify_GoodOcsp_ReturnsValidTrusted()
    {
        using PdfDocument doc = BuildPdf();
        OcspResponse good = OcspResponse.Decode(Convert.FromHexString(OcspGoodDerHex));
        SignatureVerifyOptions options = new()
        {
            TrustStore = LoadTrustStore(),
            ValidationTime = GoodTime,
            ExtraOcspResponses = new[] { good },
        };
        SignatureVerificationResult r = doc.Signatures()[0].Verify(doc, options);
        r.TrustStatus.Should().Be(SignatureVerificationStatus.Valid);
        r.TrustValidated.Should().BeTrue();
    }

    [Fact]
    public void Verify_RevokedOcsp_ReturnsRevoked()
    {
        using PdfDocument doc = BuildPdf();
        OcspResponse revoked = OcspResponse.Decode(Convert.FromHexString(OcspRevokedDerHex));
        SignatureVerifyOptions options = new()
        {
            TrustStore = LoadTrustStore(),
            ValidationTime = GoodTime,
            ExtraOcspResponses = new[] { revoked },
        };
        SignatureVerificationResult r = doc.Signatures()[0].Verify(doc, options);
        r.TrustStatus.Should().Be(SignatureVerificationStatus.TrustChainCertificateRevoked);
        r.IntegrityVerified.Should().BeTrue();
        r.TrustValidated.Should().BeFalse();
    }

    [Fact]
    public void Verify_ValidationTimeBeforeOcspThisUpdate_IgnoresOcsp()
    {
        using PdfDocument doc = BuildPdf();
        OcspResponse revoked = OcspResponse.Decode(Convert.FromHexString(OcspRevokedDerHex));
        SignatureVerifyOptions options = new()
        {
            TrustStore = LoadTrustStore(),
            ValidationTime = new DateTimeOffset(2025, 4, 1, 0, 0, 0, TimeSpan.Zero),
            ExtraOcspResponses = new[] { revoked },
        };
        SignatureVerificationResult r = doc.Signatures()[0].Verify(doc, options);
        r.TrustStatus.Should().Be(SignatureVerificationStatus.Valid);
        r.TrustValidated.Should().BeTrue();
    }

    private static PdfDocument BuildPdf()
    {
        byte[] signedBytes = Convert.FromHexString(SignedBytesHex);
        byte[] cms = Convert.FromHexString(CmsBlobHex);

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
        sigDict.Set(PdfName.Intern("SubFilter"), PdfName.Intern("adbe.pkcs7.detached"));
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

        MemoryStream stream = new();
        stream.Write(signedBytes, 0, signedBytes.Length);
        PdfWriter.Write(stream, objects, trailer);
        stream.Position = 0;
        return PdfDocument.Open(stream, leaveOpen: false);
    }
}
