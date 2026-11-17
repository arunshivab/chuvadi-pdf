// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — End-to-end PDF DSS dictionary tests
//
// Fixture: a 3-cert chain plus a leaf-revoking CRL signed by the intermediate.
// The PDF embeds the CRL inside a /DSS dictionary (ISO 32000-2 §12.8.4.3) on
// the Catalog. Verification with a trust anchor should auto-extract the DSS
// CRL and report the signer as revoked.

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Cryptography.PathValidation;
using Chuvadi.Cryptography.X509;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Signatures.Dss;
using Chuvadi.Pdf.Signatures.Verification;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Signatures.Tests.Dss;

public sealed class PdfSignatureDssTests
{
    private const string SignedBytesHex = "255044462d312e370a2e2e2e20445353207465737420646f63756d656e74202e2e2e0a";

    private const string CmsBlobHex =
        "308207ea06092a864886f70d010702a08207db308207d7020101310f300d06096086480165030402" +
        "010500300b06092a864886f70d010701a08205b8308202d7308201bfa003020102020300abcd300d" +
        "06092a864886f70d01010b05003020311e301c06035504030c15445353205465737420496e746572" +
        "6d656469617465301e170d3234303130313030303030305a170d3237303130313030303030305a30" +
        "1a3118301606035504030c0f4453532054657374205369676e657230820122300d06092a864886f7" +
        "0d01010105000382010f003082010a02820101009f859a00c7376aa2bd6fef9d506c978e546f8640" +
        "e4dfffbc6c6802048476073c2df594bb7f518e5c9717509a11ec3e0e20a2165a153af31f395f548d" +
        "ce36a5c97f75fd593b8742df6454175d41e2408084675a8d3e794b04f41b1258b3a6bdbdb809dea7" +
        "e5a895788b30d031356683159c9a12c29c4dee462505f34c972a0b9567e2e2ffd688cdfcf27bea68" +
        "9ebd9398c40df6d02045c10a350334cde70f5381d286df7d15ba2120e42a359d05f84c38cb2c1c31" +
        "3573e45c04c2abd687d8c61066e95c22b451a2c0c9f722e05152fbcdce4cbef3bf2c9f622fac054b" +
        "1299848bcbb01a94c7aee738f0e162e0e7995eb509892e4a099ec8b470fa8e6bf54e34e102030100" +
        "01a320301e300c0603551d130101ff04023000300e0603551d0f0101ff0404030206c0300d06092a" +
        "864886f70d01010b050003820101005e2da840911c6df2c0c73594c40f8a9277f46c0035ffd09879" +
        "aa01d8528cafdf497f7ab46ce75a87da19a4a49e392c507c3c3513675dbbbffbb2d5a5a56a2a9f7b" +
        "269feda042ba2173c891ac2b340e5b53f953479ba96c24a53c5d51d843ec7d6501c5fd157eb71303" +
        "40e1e14f70aa3ab61a6911339b34c5b5240c2d860efc41c1359334804b26404deb03dcfdda58be3e" +
        "50bc8b91a17e75a87b2a28bbae44e2751a177dc9d08e147bdde5fcb7168249e02901c42b1317f3f3" +
        "ba48036cbaa7056bf77b2c3b1f890c110a4f44493404eca64a548e46f18d36014f0cdb2f2605229b" +
        "66db7865795ded378b3f15b6763d77000ea2cff6d4a3d44a499863a37068c7308202d9308201c1a0" +
        "03020102020102300d06092a864886f70d01010b050030183116301406035504030c0d4453532054" +
        "65737420526f6f74301e170d3232303130313030303030305a170d3332303130313030303030305a" +
        "3020311e301c06035504030c15445353205465737420496e7465726d65646961746530820122300d" +
        "06092a864886f70d01010105000382010f003082010a0282010100b02ec977e943ba021b7fd759f7" +
        "8837851670eec51e6dc8840c86f53049a086858df9859ef42c30eedb74b90bf4d00e59dac6f6131e" +
        "fb22cca282a62e0be9822abf1a8286c21edd466747a072c21bac773f8e3f59c49c1e642676235b78" +
        "f642a921387b2542b5b933c2e1ebf4ad21801b29aa9f838177c9efad28cad489d65533cbe70c317d" +
        "a364142a8286cd51e48a2925f3ca10c6ffb68b72358fb9e00efeb4faa4cca65da9f23dde51268d2c" +
        "fded777c1759a3c196d0740ff86370607b6c47b71e719ea0173522becc6851fb2c2740601d505326" +
        "e736675fcd3b56ea322a39c0c8f542955127afa3b648b1e9867db16cd5fd50636dffb4fa9571a7d6" +
        "f9bd470203010001a326302430120603551d130101ff040830060101ff020100300e0603551d0f01" +
        "01ff040403020106300d06092a864886f70d01010b050003820101007ce12f62b5d881440088d250" +
        "75ba0041b974726f445263e246a2a5be43bd6f63a6d33bbde06ea742b379da18f7b7b425f6e4013a" +
        "be96f6e16d026fcaaf4e72230e02ff191627a40f18a9f27b2fba72dc809fcd51d693f7d15ab969c4" +
        "c181f9d0b688db0e99d9b4209d44a85919922975eb5cf08c4e63fd5eb08a6a2731604a7ff00ab030" +
        "8b5d0812b6a727faa81186b55db524f8604f02f5da0b2c990a50287a7942b7257daa33f0eb536e0b" +
        "77d227380e3ba71dd122d3874c546238ce37beaf70fe179294e5498ce01eeaad440d7405088c39ef" +
        "a2d51a751389ece2bea1b69c2cc0fa330b2827520c927f6c7c55c284572ffc3d5a6754387426f007" +
        "12a4fa4d318201f6308201f202010130273020311e301c06035504030c1544535320546573742049" +
        "6e7465726d656469617465020300abcd300d06096086480165030402010500a081a1301806092a86" +
        "4886f70d010903310b06092a864886f70d010701301c06092a864886f70d010905310f170d323630" +
        "3531373035343831315a302f06092a864886f70d010904312204202edd9335b2a5e345c8cc018032" +
        "17763155e9d8e395a6fb3ff79cb836518eb2bb303606092a864886f70d01090f31293027300b0609" +
        "60864801650304012a300b0609608648016503040116300b0609608648016503040102300d06092a" +
        "864886f70d0101010500048201005228f0328bf21d1468834334e48af62645d9e36c0d3d8322e65c" +
        "5abfdb20e949531a9e2a4b6954116184284c5a5560053a08be166189526e25ec639e13c056bb2cd0" +
        "4ab4ca719b850c4a204039037d18e6086a5f642dceae396093d319eac232adca666eb4cab0e4d561" +
        "16b07a5ed114b4413216e590957b3963bbd29cfe1150b3f169914c0216f694301bb1fba885c615c6" +
        "179775873ab5c54a28aa9ef8c3df691104d8ef53d849edbfd162296c550f5fadd909802c881bd9f2" +
        "fc877c56cef912fc63dc84f851e10558e853e7ed404cf2ba79cb41800622f1716ee2308c40424e8f" +
        "047cc9ea9593b9c93cf31e2799ae2a95ac0dbbc761eab2738b9e4553d508";

    private const string RootCertDerHex =
        "308202d1308201b9a003020102020101300d06092a864886f70d01010b0500301831163014060355" +
        "04030c0d445353205465737420526f6f74301e170d3230303130313030303030305a170d34303031" +
        "30313030303030305a30183116301406035504030c0d445353205465737420526f6f743082012230" +
        "0d06092a864886f70d01010105000382010f003082010a0282010100ab25e2ff26cab115220ddc14" +
        "8bfe37a746ba7336891398e2ac55dd429ba10c0ed5ca14ae66868529c1bd9a3bbbb5c11e489a26d4" +
        "3f6594a873514a2b44a69fc662158c70feafd05f061bc4b94f1a9aa383a1b6bc67da47f1d74cca55" +
        "ceaac973af3e5557ec0a3bdb07f88e6af88ad984854ac13be2bf21877b995ead978057c23063633e" +
        "1142980b31be7d38a9b592268b169cab36ffad01e7caaa336d1b29985081ed5967303dec71c7df98" +
        "b72f4a52e70ac604263c5360c0a9dc65796096dab89bdfeeaa44a3d373ff9a049e087c44b0cd1797" +
        "5311741b434174518fc8e8eee133792c082ceb9c3fe607ddc0117049e65e50040ee733885b3773bd" +
        "937723a90203010001a326302430120603551d130101ff040830060101ff020102300e0603551d0f" +
        "0101ff040403020106300d06092a864886f70d01010b0500038201010016d86b953b9d7cde0996d7" +
        "69450e4be8911b32423130ab6eddfe811a5db237306f9adef4e55e4ea126cce7abe4fbead709f540" +
        "224eb976180382db5bc899798a32e6cee7f145c262054e447d8468b18105b0d6349bd1798ca867d1" +
        "6036e251e49849f76fc087d5aedee43c3b22f70abc99d0cecef11733a25c91c5c7161229bac1e9f4" +
        "aed1915e0bca8465d10d97749b46c09e9748790b45cce2bb1ea9c0a18b2cc0822dfee2163764c00d" +
        "aa4f77fd0c04ee5cb8638c2ac6d60ccefe197aca269e4934270d54a87d64ae473ddc7d400871f10f" +
        "84bff256ce3c43196e183f45ae04d143732ac0339f4d0b102e12de5c906555c83bb216cabddd8cb5" +
        "29c6fbae91";

    private const string RevokingCrlDerHex =
        "3082019f308188020101300d06092a864886f70d01010b05003020311e301c06035504030c154453" +
        "53205465737420496e7465726d656469617465170d3235303530313030303030305a170d32353038" +
        "30313030303030305a30243022020300abcd170d3235303431353030303030305a300c300a060355" +
        "1d1504030a0101a00e300c300a0603551d140403020101300d06092a864886f70d01010b05000382" +
        "010100711a188baa944ccfec14d80562a8145fff38f43bdc814ebd97e06dcecff9afbc0746d3b5da" +
        "17ceb3538ae91279b65c035a38a8e12ea2ce75918c3aa6d0e6049cb678ee8fe2f0a20a046935f47a" +
        "dfdd8dd5d8a4a0d5f4a9754cc4612064743939249294daeccafd5c0aead1b800008f21ca51e84f9a" +
        "2ef763dcaee12d7982b93a111d274d899f84b28d30c2de08a9719d2dd2fe3e9fcc580de583466f85" +
        "ee0c9ef54381281ae95f0d61694af0ff631b0c16158576b07c27e678056e737afdf442214e5f64bd" +
        "b9a5a0210be84b8953e843a0ecc33b2b93e6dbf03d402ae3c68c9c5fa982b2b95273ee8d54dde0e0" +
        "00fe1647ae591a3529c58e5787cef7d7cee6eb";

    private static readonly DateTimeOffset GoodTime
        = new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private static TrustStore LoadTrustStore()
    {
        TrustStore store = new();
        store.Add(X509Certificate.Decode(Convert.FromHexString(RootCertDerHex)));
        return store;
    }

    [Fact]
    public void GetDocumentSecurityStore_PdfWithDss_ReturnsPopulatedStore()
    {
        using PdfDocument doc = BuildPdfWithDss(includeDss: true);
        DocumentSecurityStore? dss = doc.GetDocumentSecurityStore();
        dss.Should().NotBeNull();
        dss!.Crls.Should().HaveCount(1);
        dss.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void GetDocumentSecurityStore_PdfWithoutDss_ReturnsNull()
    {
        using PdfDocument doc = BuildPdfWithDss(includeDss: false);
        DocumentSecurityStore? dss = doc.GetDocumentSecurityStore();
        dss.Should().BeNull();
    }

    [Fact]
    public void Verify_PdfWithDssRevokingCrl_ReturnsRevoked()
    {
        using PdfDocument doc = BuildPdfWithDss(includeDss: true);
        SignatureVerifyOptions options = new()
        {
            TrustStore = LoadTrustStore(),
            ValidationTime = GoodTime,
            AutoExtractCmsCrls = false,
        };
        SignatureVerificationResult r = doc.Signatures()[0].Verify(doc, options);
        r.TrustStatus.Should().Be(SignatureVerificationStatus.TrustChainCertificateRevoked);
        r.IntegrityVerified.Should().BeTrue();
        r.TrustValidated.Should().BeFalse();
    }

    [Fact]
    public void Verify_AutoExtractDssOff_IgnoresDss()
    {
        using PdfDocument doc = BuildPdfWithDss(includeDss: true);
        SignatureVerifyOptions options = new()
        {
            TrustStore = LoadTrustStore(),
            ValidationTime = GoodTime,
            AutoExtractCmsCrls = false,
            AutoExtractDss = false,
        };
        SignatureVerificationResult r = doc.Signatures()[0].Verify(doc, options);
        r.TrustStatus.Should().Be(SignatureVerificationStatus.Valid);
        r.TrustValidated.Should().BeTrue();
    }

    [Fact]
    public void Verify_PdfWithoutDss_Valid()
    {
        using PdfDocument doc = BuildPdfWithDss(includeDss: false);
        SignatureVerifyOptions options = new()
        {
            TrustStore = LoadTrustStore(),
            ValidationTime = GoodTime,
        };
        SignatureVerificationResult r = doc.Signatures()[0].Verify(doc, options);
        r.TrustStatus.Should().Be(SignatureVerificationStatus.Valid);
        r.TrustValidated.Should().BeTrue();
    }

    private static PdfDocument BuildPdfWithDss(bool includeDss)
    {
        byte[] signedBytes = Convert.FromHexString(SignedBytesHex);
        byte[] cms = Convert.FromHexString(CmsBlobHex);
        byte[]? crlDer = includeDss ? Convert.FromHexString(RevokingCrlDerHex) : null;

        PdfObjectId catalogId = new(1, 0);
        PdfObjectId pagesId = new(2, 0);
        PdfObjectId acroFormId = new(3, 0);
        PdfObjectId sigFieldId = new(4, 0);
        PdfObjectId sigDictId = new(5, 0);
        PdfObjectId dssId = new(6, 0);
        PdfObjectId crlStreamId = new(7, 0);

        PdfArray byteRange = new();
        byteRange.Add(new PdfInteger(0));
        byteRange.Add(new PdfInteger(signedBytes.Length));
        byteRange.Add(new PdfInteger(signedBytes.Length));
        byteRange.Add(new PdfInteger(0));

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

        PdfArray fields = new();
        fields.Add(new PdfReference(sigFieldId));
        PdfDictionary acroForm = new();
        acroForm.Set(PdfName.Intern("Fields"), fields);

        PdfDictionary catalog = new();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(pagesId));
        catalog.Set(PdfName.Intern("AcroForm"), new PdfReference(acroFormId));

        PdfDictionary pages = new();
        pages.Set(PdfName.Type, PdfName.Pages);
        pages.Set(PdfName.Kids, new PdfArray());
        pages.Set(PdfName.Count, 0);

        List<PdfIndirectObject> objects = new()
        {
            new(catalogId, catalog),
            new(pagesId, pages),
            new(acroFormId, acroForm),
            new(sigFieldId, sigField),
            new(sigDictId, sigDict),
        };

        if (includeDss && crlDer is not null)
        {
            PdfDictionary crlStreamDict = new();
            crlStreamDict.Set(PdfName.Intern("Length"), (PdfPrimitive)new PdfInteger(crlDer.Length));
            PdfStream crlStream = new(crlStreamDict, crlDer);

            PdfArray crlsArray = new();
            crlsArray.Add(new PdfReference(crlStreamId));
            PdfDictionary dssDict = new();
            dssDict.Set(PdfName.Intern("CRLs"), crlsArray);

            catalog.Set(PdfName.Intern("DSS"), new PdfReference(dssId));

            objects.Add(new PdfIndirectObject(dssId, dssDict));
            objects.Add(new PdfIndirectObject(crlStreamId, crlStream));
        }

        PdfDictionary trailer = new();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new();
        ms.Write(signedBytes, 0, signedBytes.Length);
        PdfWriter.Write(ms, objects.ToArray(), trailer);
        ms.Position = 0;
        return PdfDocument.Open(ms, leaveOpen: false);
    }
}
