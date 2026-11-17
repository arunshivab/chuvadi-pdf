// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — End-to-end revocation tests
//
// Fixture: real chain (Root → Intermediate → Leaf) with two CRLs signed by
// Intermediate — one revoking the leaf and one empty. Leaf signs a PDF.

using System;
using System.IO;
using Chuvadi.Cryptography.PathValidation;
using Chuvadi.Cryptography.Revocation;
using Chuvadi.Cryptography.X509;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Signatures.Verification;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Signatures.Tests.Verification;

public sealed class PdfSignatureRevocationTests
{
    private const string SignedBytesHex = "255044462d312e370a2e2e2e207265766f636174696f6e207465737420646f63756d656e74202e2e2e0a";

    private const string CmsBlobHex =
        "3082080306092a864886f70d010702a08207f4308207f0020101310f300d06096086480165030402" +
        "010500300b06092a864886f70d010701a08205cb308202dc308201c4a00302010202021234300d06" +
        "092a864886f70d01010b050030273125302306035504030c1c5265766f636174696f6e2054657374" +
        "20496e7465726d656469617465301e170d3234303130313030303030305a170d3237303130313030" +
        "303030305a30193117301506035504030c0e5265766f6b6564205369676e657230820122300d0609" +
        "2a864886f70d01010105000382010f003082010a0282010100d80ae74159f217fd4285ffa1cb1229" +
        "cc01186d628b8d559ab22b406bfb50d06409948823e1457eac78407603252e113dcccd7bd1ab53ab" +
        "c00937fe4f49562f63c3afdea3effa638466ecaa6130ed87dc4612d63389e8f49549d65984961443" +
        "fb7489588e68dab170d94ad7595ac75df108e7d4b1887140c6b5619e7f214a6b1e554da51d9b1670" +
        "3bd7e693eeb83024ad2a9eaa12ee1e28290e8b1cd751512250007b1957c3169a87ecfd54caf2872d" +
        "fdcf3ca650b1d161674c8e3d8159e5f525885c5560eea1cc70ad6ae02f8b4089692eff296676daaa" +
        "72dbd60a82096a541e12702b2580848b2d1c670029061e7133ec0520abf568b9a293efbbd0bf532d" +
        "350203010001a320301e300c0603551d130101ff04023000300e0603551d0f0101ff0404030206c0" +
        "300d06092a864886f70d01010b05000382010100784f65ee9689d990c43cda16a08502993d946652" +
        "02bb84c2789293d63616278bf8b5c291d23109edc126d90f932a042dbca7d978abae031d179e76f9" +
        "c486b9ab713f33293800a134e6d9c209fc46a3c20c2f2de53807a496ddac53cc3cf1e77bb3365e54" +
        "f11ae5ac01bf2a25e9d8b8febb0a194df03e47b6b3da8c83d4acdcdd3dcbc10ee5d6ede8bfc13c4c" +
        "c6f5c59d312d6d86afecb8fe293426538fb1f2b843658a9adbcd826ffaf2e72c0a6da02dc3a182b0" +
        "82454b89e80f21f426463387aa955e9bee127a61c5f51f719d7c3c3e66be0bf40bfe08d212bfc5ce" +
        "dbeecab64be95c5100aba0e61a52fcc3609ac1a0111f83acda8a6da4a5f54333b3bba550308202e7" +
        "308201cfa003020102020102300d06092a864886f70d01010b0500301f311d301b06035504030c14" +
        "5265766f636174696f6e205465737420526f6f74301e170d3232303130313030303030305a170d33" +
        "32303130313030303030305a30273125302306035504030c1c5265766f636174696f6e2054657374" +
        "20496e7465726d65646961746530820122300d06092a864886f70d01010105000382010f00308201" +
        "0a0282010100d74f4fc46137eb1f39f26b83f78f4406aaab8ec71646658498b535b658c546935662" +
        "324d448f82ff9adb94222a7886d6f8dbedf9f3ba523996f9c9a24c296818f595ab6ca4c8b41dcbd5" +
        "08272d1451deb8a2e148cc96fe545b17ef206c977483e19d69ba646f12dd20c8668a9ce1b080b096" +
        "0a756c647e3a809a738bec9535fe2c17009375d38d383a6ec3ad9c5608c0d6e3eff868ba9ee915c3" +
        "733e9f87cee9fbec0a0a6152bdbb3b9c36032261b3ba8ff4325b6922ea4ccf4c8065556fca52a637" +
        "5f69566fbd63a434680cbe81fe0d0482239d802c4798e5e50354cecdc5d7b2386a73c02dd2869b94" +
        "59150719e5f4568966e250253c6059857acff88b4dd50203010001a326302430120603551d130101" +
        "ff040830060101ff020100300e0603551d0f0101ff040403020106300d06092a864886f70d01010b" +
        "050003820101002f668706abeb2e7f743033f08ada4ed1f5c0d27ab167a3e7286d8ba82dc1248728" +
        "d60ca3fdebb46b00f5a42e0ce0bd783f50b9f77691b4e5f72208e6e34f6efce748f383bc2bd6f33a" +
        "de8d74ff3095b628a75db1caad0fbcc7307ea828f61d7d68b6a27e190a51cc457ff22728632badd0" +
        "4dabcb890bcd2e52122036ecf011cbd984b5d35517e312b8a011a206d1779b68758faa1d291e4596" +
        "9fb7eb23eec05982cb79f0be46f6ab6a5a82dc2d5cbf4214b8963cd09df6352e5d42675abf7ae8b9" +
        "5ae332c4e85584ae0b89ab15852e9b866c1f07d2d147127666549b7c0af75e04ff7740337277ce94" +
        "58804b31f18bbfd083048ddbad0a32e4dce7e3c22aca02318201fc308201f8020101302d30273125" +
        "302306035504030c1c5265766f636174696f6e205465737420496e7465726d656469617465020212" +
        "34300d06096086480165030402010500a081a1301806092a864886f70d010903310b06092a864886" +
        "f70d010701301c06092a864886f70d010905310f170d3236303531363137353032395a302f06092a" +
        "864886f70d01090431220420f417f9f6fa29f5eccf8dd8f46c33df8da9cdc6905846229e8fe6b9fd" +
        "32c54e8a303606092a864886f70d01090f31293027300b060960864801650304012a300b06096086" +
        "48016503040116300b0609608648016503040102300d06092a864886f70d01010105000482010066" +
        "d541e45ca0995d2423773d12534e33772bd7bdc8049e71bb765c7a58411f0c8436d2938245a20ac4" +
        "d6c8770db9cd72c5b402b8532a6c135241bb0877979ee6dfd5fe4949e3f813896140b4fae7b030ee" +
        "6c7e9ca10754b5a9855e47634af510c88d82622e7f15a6704b2c95b3027ac0e7e90a3c1394b83752" +
        "f65c9aa682aea27ba24de074e1ec8e361cde35208606ca73669b0b5559dc308143893566cfb2eac3" +
        "cdced56b38d17ec40eb2d99eefbb0545a252d9551919da4fbba6a448a35ef927dc604e1f03f5b03d" +
        "f0f17a8c05966314fb96f88538ea7320fd2357c69764bf66ca22ade4b6dd66e9f603447af3262f7b" +
        "e2fcbfee90aa4f7d87a77119db878c";

    private const string RootCertDerHex =
        "308202df308201c7a003020102020101300d06092a864886f70d01010b0500301f311d301b060355" +
        "04030c145265766f636174696f6e205465737420526f6f74301e170d323030313031303030303030" +
        "5a170d3430303130313030303030305a301f311d301b06035504030c145265766f636174696f6e20" +
        "5465737420526f6f7430820122300d06092a864886f70d01010105000382010f003082010a028201" +
        "0100c0a3a8f37a4e811dfb6fdea8f42d34f31f032a4b0e818e9468184cc19e940eebd8f5281e18b4" +
        "4a6285b79cf5b38e0c865530699ad6d65b8401149589f383cfb65c1e7da16c5a40bf3c3175aaf6d5" +
        "1f8bf6980797eada65b6c865442cb7feccf8aa039ee23f515175fd0c60b095ef9be4b8c723e0299e" +
        "7d688eeaabadd84a73682a3ed98b8631d2ef4da9ec1f71c642c82b518ce1b528835a739fe895a149" +
        "b6facfa80baf5d1a2a179935ca4fa71fad9482cbf643405dc880fd6f1ef34ddacfea9d96a02f5436" +
        "f67d82803a9e1878c64181ab2b5a0e749e949ad3643d8b3d6f77724781432347aa4192aabd5c8bdd" +
        "553181f97e688330c3b247a887084558f7050203010001a326302430120603551d130101ff040830" +
        "060101ff020102300e0603551d0f0101ff040403020106300d06092a864886f70d01010b05000382" +
        "0101008d9438362de94a471fad75bc8976c8a2b135ef7f5795a2fd0b758999b5806acb4e8005455c" +
        "5b7a8a3fadc722afff8040dcea8c509a02fd82fbe168989a8b62c46e21dfb12c0cd840d3109b00f3" +
        "4569efe7b203e2fe8a58b11c6f6b67855e53b6ecdc0a0406e8bafb470f3b63c1a35621f441d6b799" +
        "03027c801f50f4454c278020ef24e6b2a55d3166c4e3fc4a1d4e410f7054fdb5c23cf206bdc91487" +
        "d9477ebca710f7702b804088b09813ae3732a3f66d66804836d38722d5ec000bc5d18c1467b4081b" +
        "6a4809e675015d6680a1526c46f65cb533940cd79b549278db03dbb7e02b08451d6d5fe90aa2dea4" +
        "2c90f3e27b957d2dce436f69d72e6874539b0e";

    private const string CrlRevokingDerHex =
        "308201a530818e020101300d06092a864886f70d01010b050030273125302306035504030c1c5265" +
        "766f636174696f6e205465737420496e7465726d656469617465170d323530353031303030303030" +
        "5a170d3235303830313030303030305a3023302102021234170d3235303431353030303030305a30" +
        "0c300a0603551d1504030a0101a00e300c300a0603551d140403020107300d06092a864886f70d01" +
        "010b0500038201010056bdcf4193bf5cfc72fcc20ab76a2c49fabf63dd32af6b8eab5670871cb722" +
        "460775ee9ed4c499b04ccd26c21e05293895784b7e36bc6f1c9cf196894827c69b40d539af13ff2f" +
        "781527317c6d8ff9a55870783ed5a68f0466ce48ebc69f30f26680d8cd8cc1df79cc49bfe7d5de33" +
        "9e0f0c86de4bd8c9a248802391cb2782112f264ebde0323999f511d0bcada31c0c8ccf9f14120945" +
        "005d7157412a2f8dd5df01a0c72c90c5824d3322fb68956f03de1ab4fd3cc7088ec0d893a10f4002" +
        "9b5ed6b863af238a097b04cc7a31c63a42b8eafbb914cb05047912428eb48287ed2941b769b4c361" +
        "fe5e672b51795a50584c0a9fefbacd4f4ae4bdd7bfe9dd4624";

    private const string CrlCleanDerHex =
        "3082017f3069020101300d06092a864886f70d01010b050030273125302306035504030c1c526576" +
        "6f636174696f6e205465737420496e7465726d656469617465170d3235303530313030303030305a" +
        "170d3235303830313030303030305aa00e300c300a0603551d140403020107300d06092a864886f7" +
        "0d01010b05000382010100219b9fcab31bc252b872ea32131c19d26e7067354acb514e76bf609a9b" +
        "3bec68364e6f6c4f47437f5287111e4d09f0d209a1ea4b76478d9e67e5a643a78114f7d4e66758be" +
        "4f17f5abed2036a10a51f484b8fe5ea5510f1a083f5bc564f11349842b353005e29eae5300d86160" +
        "402abd10af2a18ca0d13cea93d9a4334c297ef3521a98132a7dead30307b6399c0aa22740dd9d579" +
        "be8aa2d6ecaf1d94b07476e486415f0c616bb572d9cc0a75dbd17df673f7479a6d317dfca01c7209" +
        "bb608f267126ca0ae4895e8a4dcfa160a2e0e8a56f4dd65c1610a1bdb101f0ade28fa05d79c2fdfc" +
        "03d8b6e79f2f0c252dd28a144d77b6193215a96a2b6e4d1a7b5d8d";

    private static readonly DateTimeOffset GoodTime
        = new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private static TrustStore LoadTrustStore()
    {
        TrustStore store = new();
        store.Add(X509Certificate.Decode(Convert.FromHexString(RootCertDerHex)));
        return store;
    }

    [Fact]
    public void Verify_NoCrls_ReturnsValidAndTrusted()
    {
        using PdfDocument doc = BuildPdf();
        SignatureVerifyOptions options = new()
        {
            TrustStore = LoadTrustStore(),
            ValidationTime = GoodTime,
            AutoExtractCmsCrls = false,
        };
        SignatureVerificationResult r = doc.Signatures()[0].Verify(doc, options);
        r.TrustStatus.Should().Be(SignatureVerificationStatus.Valid);
        r.TrustValidated.Should().BeTrue();
    }

    [Fact]
    public void Verify_WithRevokingCrl_ReturnsRevoked()
    {
        using PdfDocument doc = BuildPdf();
        CertificateList revokingCrl = CertificateList.Decode(Convert.FromHexString(CrlRevokingDerHex));
        SignatureVerifyOptions options = new()
        {
            TrustStore = LoadTrustStore(),
            ValidationTime = GoodTime,
            ExtraCrls = new[] { revokingCrl },
            AutoExtractCmsCrls = false,
        };
        SignatureVerificationResult r = doc.Signatures()[0].Verify(doc, options);
        r.TrustStatus.Should().Be(SignatureVerificationStatus.TrustChainCertificateRevoked);
        r.IntegrityVerified.Should().BeTrue();
        r.TrustValidated.Should().BeFalse();
    }

    [Fact]
    public void Verify_WithCleanCrl_ReturnsValid()
    {
        using PdfDocument doc = BuildPdf();
        CertificateList cleanCrl = CertificateList.Decode(Convert.FromHexString(CrlCleanDerHex));
        SignatureVerifyOptions options = new()
        {
            TrustStore = LoadTrustStore(),
            ValidationTime = GoodTime,
            ExtraCrls = new[] { cleanCrl },
            AutoExtractCmsCrls = false,
        };
        SignatureVerificationResult r = doc.Signatures()[0].Verify(doc, options);
        r.TrustStatus.Should().Be(SignatureVerificationStatus.Valid);
        r.TrustValidated.Should().BeTrue();
    }

    [Fact]
    public void Verify_ValidationTimeBeforeCrlThisUpdate_IgnoresCrl()
    {
        using PdfDocument doc = BuildPdf();
        CertificateList revokingCrl = CertificateList.Decode(Convert.FromHexString(CrlRevokingDerHex));
        SignatureVerifyOptions options = new()
        {
            TrustStore = LoadTrustStore(),
            ValidationTime = new DateTimeOffset(2025, 4, 1, 0, 0, 0, TimeSpan.Zero),
            ExtraCrls = new[] { revokingCrl },
            AutoExtractCmsCrls = false,
        };
        SignatureVerificationResult r = doc.Signatures()[0].Verify(doc, options);
        // CRL.ThisUpdate is 2025-05-01; validation time of 2025-04-01 means the CRL
        // wasn't yet "in force" at that retroactive moment, so it's correctly skipped
        // and the signature is considered valid as of the validation time.
        r.TrustStatus.Should().Be(SignatureVerificationStatus.Valid);
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
