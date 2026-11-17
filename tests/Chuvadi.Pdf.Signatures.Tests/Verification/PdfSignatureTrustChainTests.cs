// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — End-to-end tests for trust-chain validation
//
// Fixtures: a real 3-cert chain (Root → Intermediate → Leaf) produced by
// Python's cryptography library, with a detached CMS SignedData over a
// known payload. The intermediate is embedded in the CMS envelope; the
// root is supplied as a trust anchor.

using System;
using System.IO;
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

public sealed class PdfSignatureTrustChainTests
{
    private const string SignedBytesHex = "255044462d312e370a2e2e2e74727573742d76616c69646174696f6e207465737420646f63756d656e742e2e2e0a";

    private const string CmsBlobHex =
        "308207fe06092a864886f70d010702a08207ef308207eb020101310f300d06096086480165030402" +
        "010500300b06092a864886f70d010701a08205c7308202d8308201c0a003020102020103300d0609" +
        "2a864886f70d01010b050030273125302306035504030c1c43687576616469205465737420496e74" +
        "65726d656469617465204341301e170d3234303130313030303030305a170d323730313031303030" +
        "3030305a30163114301206035504030c0b54657374205369676e657230820122300d06092a864886" +
        "f70d01010105000382010f003082010a028201010094865177d89e0b9b166998cf3c4ae8de082658" +
        "269181e3021b6e6d520b9d9226723e1d39d3395acda7d1f220fd6c03752e41d563dd519af4d6e518" +
        "bb3c87a3ee02d81b5cefa2911163b4623cf78d1bf8faf254c85f5aa9a512f51dc372d32d43676988" +
        "2e240a830fe22dbf48e96ad7e18494155f34fcb87fdc1aecc34f85e5e923dd1d420fc3f9092b9b44" +
        "1829eddec0aa592d23bbb5b4562437d718690dc82aebc0e031ab67e6553c0721da6c84a90c3833aa" +
        "7777273fed0f523ef2ba63753efa35ad721b6acb103405a2e265ff73159815c59a1561c04e85664d" +
        "3e05db19e39fbb046650aa973bcd2b9735eb4916cdddfe00c9aa19b19c5bd052780d9f5909020301" +
        "0001a320301e300c0603551d130101ff04023000300e0603551d0f0101ff0404030206c0300d0609" +
        "2a864886f70d01010b05000382010100741d5e558b20e25780a0f5416b1b73749aee61f4b23a5781" +
        "b9da5fe475e5578338e96c2b7ad91aad29a6040a5f53cc66c85a36fdde003f46800db1964fa5a9d9" +
        "a8eac69ece3068709337d19f269b87d02c72f44e1e197bfb9159ed114b1b3022523a26a205c1a84b" +
        "78e0859113a35f52ee7d1815fcd4b9e9c8339408b3e0904dead67970052196d6d1fac5d473e376f2" +
        "2fa6e62b7604581428608e7dc755933019d16673a7df2863e34a47abe48a0de5033f67000b4e87ff" +
        "3b7625955c7e1d0aa2c8816b8da4ff1b7d947c99deb47bf4f96f83fb7964571ce7d89204b31ea68e" +
        "2d36b7b9b85bd8a29309644e4c3c58e7048c1c770d43b23c9b1124d36fabf20f308202e7308201cf" +
        "a003020102020102300d06092a864886f70d01010b0500301f311d301b06035504030c1443687576" +
        "616469205465737420526f6f74204341301e170d3232303130313030303030305a170d3332303130" +
        "313030303030305a30273125302306035504030c1c43687576616469205465737420496e7465726d" +
        "65646961746520434130820122300d06092a864886f70d01010105000382010f003082010a028201" +
        "010090005be9dee6a3fcabfa8bc098434336ddde7f4c9bf1190b98eec128d6040b5fcc991dd3b39b" +
        "aa51baf5806508d53e2ee9ba2caa9830692b17ba0a81299e6184da038696696c4c719f2be3e30ecc" +
        "25d94e471ccf55b59b17e4711f88b3f5675c5acac137671dd52a58d786943da0da789af4d5548be0" +
        "b3f1251f94d813fbe3d1b79c9d3150631e031341a8cd2fc4998a9b155e9f6a59e0dbd7f72cb25280" +
        "4e48d3488865e3fdfd5f087443d274c9291dbd475f6c943c96b48b5b4f085b688f26171fbdb66a03" +
        "2611fa3ffe2c28f8f7bc2c2f67dfed64575536177798c4d887037b97ccb86c7f3f012f5dc3d02477" +
        "ffcbfb92b0dd9e907e99e839d9a35b1b21b30203010001a326302430120603551d130101ff040830" +
        "060101ff020100300e0603551d0f0101ff040403020106300d06092a864886f70d01010b05000382" +
        "010100b343afc4c21dfcd54365e96c282ec92a5303f2f51884e55280a9252684523009eb2a8ec4a5" +
        "e09c1516e4999112e6f2c0779b5cdaa9171e44e0947733bf5e48e3692402b801cca1fb52c902092f" +
        "1d8327964cce1fc13b60d0137db61e2a01cc81979cbad74adb683918dbba3472ab140a8f536f6c00" +
        "3e35706fe75a76c21f056c3aac97004be76a45a72f878e6bc380c93266de097520d4cc930a24cd2c" +
        "3d58991485bedda21640c767a8c5900d1467495fb73ec07ab869d50ef59a44740159a1ab77eb1f27" +
        "bd83b90351834a6ec6e93732f5aad58102310d0694bc80db13a62e6e96949c9244f3788bc85457b4" +
        "2d00912238e29312af9dde724743dde3fcbfe0318201fb308201f7020101302c3027312530230603" +
        "5504030c1c43687576616469205465737420496e7465726d656469617465204341020103300d0609" +
        "6086480165030402010500a081a1301806092a864886f70d010903310b06092a864886f70d010701" +
        "301c06092a864886f70d010905310f170d3236303531363137323734345a302f06092a864886f70d" +
        "01090431220420cb79f96c1188e9042d79f8ea9ceaf60147fb3aea66dbecf03912a97bac747a0d30" +
        "3606092a864886f70d01090f31293027300b060960864801650304012a300b060960864801650304" +
        "0116300b0609608648016503040102300d06092a864886f70d0101010500048201007359c6930048" +
        "0a66b877af2235588ade0c99ff908679fcafdbc66fc714a9300b322333f63e086e1a67c160225298" +
        "8992e5c7d5c883df0201408aa550393017cb98fd774ad1a4acc1a5b52b4e1abee2f4110af866c1ad" +
        "172b06cb15a48740db394ad9213ca3d5481e5014cb38d3bffa8a3c07e72648d284484eb290cf2f93" +
        "d0dbb5d583a2adc3fc508d5280ce34882ba855929efec5b2a216a0195f7c9b08199eb3870ac9b2f1" +
        "f45558aac6f65fd531613fc86f9c39b67743ee172ef7d32c85e58c4d07fddb00a9629d698729c820" +
        "4e3396fd617e26343d240847047213284d75b9ea2c1adfed309e656915ed588d6f1c079336e6f3cb" +
        "6204821b772e33633cac";

    private const string RootCertDerHex =
        "308202df308201c7a003020102020101300d06092a864886f70d01010b0500301f311d301b060355" +
        "04030c1443687576616469205465737420526f6f74204341301e170d323030313031303030303030" +
        "5a170d3430303130313030303030305a301f311d301b06035504030c144368757661646920546573" +
        "7420526f6f7420434130820122300d06092a864886f70d01010105000382010f003082010a028201" +
        "0100e1dcd216d969415edd3be7f96afe1d3b552f4c46c13a6b40acfef91518ca52d7b7675219ae44" +
        "5f345efea40b2e3908fb57f2124e0f13affd483d06df812f4554d0d7ceb4d94299d57f5c900cd7d2" +
        "baeea93c86837903eaf21c2ee8f9919c8ebd073e40319815f02f137646308cce206663d6c705373d" +
        "82955658c4fdd82870222964e097a9647d89830f8c11e5ff75fd57769e97281dc484f5c056cd0432" +
        "c5430bca6f4a7d0e2f731c21452b871e5b605d44bb9180b44548f90933293a90387c0d72f5e77a4b" +
        "fa66b77a2602ed76f86e5bdb054ccff0dbcbec41bbb4015fbcfa651d18d8c61aec222e8c57330342" +
        "e0c0d4404a985ac9abe3c9b16c7a549664490203010001a326302430120603551d130101ff040830" +
        "060101ff020102300e0603551d0f0101ff040403020106300d06092a864886f70d01010b05000382" +
        "0101002b6a320016d6e76d87bc39547900e697543226750f655c992887572798ed926134cf7ecd14" +
        "b20489595dac50f6ec1b13694e2d5bb0ed1e5c4ec86c94482dfde7dd543274f4eb62263fc26860de" +
        "14db3f3b78f3847befd14b1cfaeb1f79bf62585b339933e28c9130f0b1550c07529617398bdb324f" +
        "bf19440843624eeacda2b40038dea114eef28fc70f54cd113d4856d57b30f49ec5416f5f2ff9733c" +
        "c36b0aac896be6d2f05a1d18417ff4517a98a8a0d8df748335ad0df403b389a2f514d22f03142946" +
        "e7c2ec8f2276e5e3b17621c85d7b16d3488052c01db0255fa4e8ecbf2f7544fd13d6554cd875f9d1" +
        "cf3606e835cbba275e07e4ba06529b134ef399";

    private const string OtherRootCertDerHex =
        "308202d6308201bea003020102020163300d06092a864886f70d01010b0500301c311a3018060355" +
        "04030c11556e72656c6174656420526f6f74204341301e170d3230303130313030303030305a170d" +
        "3430303130313030303030305a301c311a301806035504030c11556e72656c6174656420526f6f74" +
        "20434130820122300d06092a864886f70d01010105000382010f003082010a0282010100d20c67d2" +
        "4c5498180278662dc25ac0019274e84cc7236502ff31dfc89356ec8ea10ead86f1408e5b6d395a49" +
        "bbec5102dffadf569604cd7b6658b267fc1d200c0fac230e3c5a16de42ead3d9c7bb0f3944939244" +
        "4ccf0763643ce09ba1725a039fbd103bbe7b1c63eff9019e4975d34e1822930ea77addcb15e01dc9" +
        "5eff465ff0a379c3eb215048b6e1ca045f0b38ae3f16d5894eca996cc888137bc34dbbcc1e7b66f2" +
        "170b4666eebce2d8bf242eabf2b17b8cdc6ca5ce614d2e99d28ccb1386e62711babe877481918be7" +
        "bf2dcf95a850c0282f20bd231721f7fece2b789891157f30c89b284883b9568d718e61ce80b46802" +
        "6f81d6224f8de1e3c02ca9af0203010001a3233021300f0603551d130101ff040530030101ff300e" +
        "0603551d0f0101ff040403020106300d06092a864886f70d01010b05000382010100478db7411d00" +
        "a7fb9511c23a1f80968b23704eedb94d4ddad2b0f126a676e82626b1f5cf5fa5aefb116fdbcf67ae" +
        "beebb949f40c160049d75b742e1b807340e01862630ec84d3099a3f32f8a3a9b404a307d69b0a249" +
        "3b18c962a68c4e406e72ad1e7097d72d59077391bd898d18f920ab983dc9bcd6361f2b14805a5658" +
        "516e0c48459f4d2eee4e09723ac3097195c9c1d8cb46ea545183601321a30d4faf345faa47777631" +
        "c17313a4de482ded81ddcdf94d421c6b45c43896f39926432c83af96694bf291db3d62252f2b77af" +
        "bf5c85ca7e614ba8f435e4aeddc80327ee3fc2db52181d3bf42cfd29fb3abd7efd9d8fd1e8e7455f" +
        "afa14d4f9031a4edad18";

    private static readonly DateTimeOffset KnownGoodValidationTime
        = new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Verify_NoTrustStore_ReportsTrustUnknown()
    {
        using PdfDocument doc = BuildPdf();
        SignatureVerificationResult result = doc.Signatures()[0].Verify(doc);

        // 3.35.0 (D-396): without a trust store the signer is not verified.
        result.TrustStatus.Should().Be(SignatureVerificationStatus.TrustUnknown);
        result.IsValid.Should().BeFalse();
        result.IntegrityVerified.Should().BeTrue();
        result.TrustValidated.Should().BeFalse();
        result.ValidatedPath.Should().BeNull();
    }

    [Fact]
    public void Verify_TrustStoreWithCorrectRoot_ReturnsValidAndTrusted()
    {
        using PdfDocument doc = BuildPdf();
        TrustStore store = new();
        store.Add(X509Certificate.Decode(Convert.FromHexString(RootCertDerHex)));
        SignatureVerifyOptions options = new()
        {
            TrustStore = store,
            ValidationTime = KnownGoodValidationTime,
        };

        SignatureVerificationResult result = doc.Signatures()[0].Verify(doc, options);

        result.TrustStatus.Should().Be(SignatureVerificationStatus.Valid);
        result.IntegrityVerified.Should().BeTrue();
        result.TrustValidated.Should().BeTrue();
        result.ValidatedPath.Should().NotBeNull();
        result.ValidatedPath!.Length.Should().Be(2);  // leaf + intermediate
    }

    [Fact]
    public void Verify_TrustStoreWithUnrelatedRoot_ReturnsTrustChainBroken()
    {
        using PdfDocument doc = BuildPdf();
        TrustStore store = new();
        store.Add(X509Certificate.Decode(Convert.FromHexString(OtherRootCertDerHex)));
        SignatureVerifyOptions options = new()
        {
            TrustStore = store,
            ValidationTime = KnownGoodValidationTime,
        };

        SignatureVerificationResult result = doc.Signatures()[0].Verify(doc, options);

        result.TrustStatus.Should().Be(SignatureVerificationStatus.TrustChainBroken);
        result.IntegrityVerified.Should().BeTrue();
        result.TrustValidated.Should().BeFalse();
    }

    [Fact]
    public void Verify_ValidationTimeAfterLeafExpiry_ReturnsValidityFailure()
    {
        using PdfDocument doc = BuildPdf();
        TrustStore store = new();
        store.Add(X509Certificate.Decode(Convert.FromHexString(RootCertDerHex)));
        SignatureVerifyOptions options = new()
        {
            TrustStore = store,
            ValidationTime = new DateTimeOffset(2028, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };

        SignatureVerificationResult result = doc.Signatures()[0].Verify(doc, options);

        result.TrustStatus.Should().Be(SignatureVerificationStatus.TrustChainCertificateOutOfValidity);
        result.IntegrityVerified.Should().BeTrue();
        result.TrustValidated.Should().BeFalse();
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
