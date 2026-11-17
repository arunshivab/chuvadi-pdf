// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — VRI sub-dictionary tests
//
// Fixture reuses the TSA trust test chain. A revoking CRL is placed inside a
// per-signature /DSS /VRI entry keyed by SHA-1(Contents). Verification should
// pick up the CRL via the VRI lookup and report the signer as revoked.

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

public sealed class PdfSignatureVriTests
{
    private const string SignedBytesHex = "255044462d312e370a2e2e2e205453412076616c69646174696f6e207465737420646f63756d656e74202e2e2e0a";
    private const string VriKeyStamped = "FC7640BDA551693AEF6AF26C79FBF9E1435A5AA2";
    private const string VriKeyUnstamped = "BB802C17561DD656D8D08B348F8F926D55992F7B";

    private const string CmsStampedHex =
        "308210af06092a864886f70d010702a08210a03082109c020101310f300d06096086480165030402" +
        "010500300b06092a864886f70d010701a08205c7308202dc308201c4a0030201020203055555300d" +
        "06092a864886f70d01010b050030253123302106035504030c1a5453412054657374205369676e20" +
        "496e7465726d656469617465301e170d3234303130313030303030305a170d323730313031303030" +
        "3030305a301a3118301606035504030c0f5453412054657374205369676e657230820122300d0609" +
        "2a864886f70d01010105000382010f003082010a0282010100c94549352f0176a3472e2f0463cade" +
        "e2931da1e3c443aeff2774a3b6724108f67e95aa1b76978984324967f22f22605222041781cea016" +
        "9407382b67cd30198da3e70d032b224d5e1a19fe2c8926546fdee951f607feaa3fb8ebc5b7d55bb2" +
        "0e68bed488e726308fe7481f0993e89633c7f391d3d1ea647de1c09d11d3b881364f2b021ee5b4a1" +
        "83e724f737d92558f735fa7adf01d353eac88d391b2f52d4e97bcd5258703de4436e740f3e83e10c" +
        "b48dff10d925628acf27f9c32c66f01546c22475030219ca1f3f923fc3a406bb327f560fd22d8541" +
        "05817834aef870132981cde14ab9524860a43fd8dd2ee566dfa873f7cd97b6ee07881be1bcac1338" +
        "a30203010001a320301e300c0603551d130101ff04023000300e0603551d0f0101ff0404030206c0" +
        "300d06092a864886f70d01010b050003820101008ed744b50204fc2aca9093da9b5516e05c190858" +
        "d4ce49b77dad854800a0c403e451106ef4b729239eb52522c60ef8427796fe76cdb8dacb182e1115" +
        "7e8ccfddcfd236eb4f23d5baac262b146fedb6fd48f4eb44a60e3dcc35ee0e009d02d36032584e83" +
        "3dcfba25a9004c6cd4be1cd5bb4be86c3105805e83f13d525751cc058e55b8f6d68f2b36b8bcea55" +
        "1ddfb4b1a3820846f6fff6c3b3698b057db15e77661229b7c1aa43b14b3a4361bb756f98401915f4" +
        "0c8ad914d5fb04330585924390034cbd3948e1be943d7bba3dc8da3b264e318da6e853ae6885dce3" +
        "1aaaaa6dd727abe978f9a14d2ead3746ab844dee3300eb991f654d0f363872e1f7a3c9bd308202e3" +
        "308201cba003020102020102300d06092a864886f70d01010b0500301d311b301906035504030c12" +
        "5453412054657374205369676e20526f6f74301e170d3232303130313030303030305a170d333230" +
        "3130313030303030305a30253123302106035504030c1a5453412054657374205369676e20496e74" +
        "65726d65646961746530820122300d06092a864886f70d01010105000382010f003082010a028201" +
        "01009e5dfd556df214efaa4227eacf2d1f7649885e1d30c9160fdb6878ae5b0192fda822338a8bec" +
        "524adf6f524a0ff58bc3b818ad4b7ce1e1f739d5b8d25724ba41436efd817b01cde16a3d6949b23f" +
        "616278a225c8e3aa404643ef2388152a6f13287f41c233ec80a7e86dee52445f8e1ceb6c2aa0c3a4" +
        "81c337bfcce59f65ee4169cd7d51036b46c28671ddadef34f0808e52238fcf0acaf0e2ee2f506f48" +
        "b28ad32df4ee2439694a783317464526dff6e41a68e84fa5d850b17523af7670fdae614c5f7230a9" +
        "1d4bed293c566dc5ebac940756a6c1af4cad02ca1d263207a9a743ab569366661cb1b363b892090c" +
        "4c69f136374a5ef765ab590be8d93e7f3f430203010001a326302430120603551d130101ff040830" +
        "060101ff020100300e0603551d0f0101ff040403020106300d06092a864886f70d01010b05000382" +
        "010100833f345eba7c85a6991cc04e2dba4e0b9cb449f3e292408e3729dd96a63b4f677da7aa127c" +
        "dc2779436651505a4b6cc2027b999fe651a3d3e076f05a0e6e7bb4182539f1e7821a670d553d544b" +
        "543deeeeab572ec7906a1ff19bb06de3dc448ec5997dd84c739b75ff19c042b773a4146da94cc266" +
        "2021109edf6aa24699130732c01e6398c0b35728de1dd16de09a774b85e7cf7a0f95c9cd032f9a69" +
        "b4ced544b52769d908be6af41565ecb8e0641cdbe4da7de7cfbe9a2571d5094dcdc2fefa0d885d0e" +
        "0b06b04867cca211bc0426fb7a00c21728e65252ba1c8229bd33283387634c2c50505e9cb661cc8c" +
        "3a2ae96a8ecdd39a72cd1744055251e618429a31820aac30820aa8020101302c3025312330210603" +
        "5504030c1a5453412054657374205369676e20496e7465726d6564696174650203055555300d0609" +
        "6086480165030402010500a081a1301806092a864886f70d010903310b06092a864886f70d010701" +
        "301c06092a864886f70d010905310f170d3236303531373036323033385a302f06092a864886f70d" +
        "010904312204201f7521704e8ffa415d6e2471bae16afdffe7df9193b739cdc29f11ee75fe32e230" +
        "3606092a864886f70d01090f31293027300b060960864801650304012a300b060960864801650304" +
        "0116300b0609608648016503040102300d06092a864886f70d010101050004820100842271476582" +
        "c9797db2d0642b26aa020a7b5873dd4fc18ea54dcd5b77242d4b733b6f9826a153021ff7613268d8" +
        "c6522f5273f8f171e537af3d9d58535913d2e05555223452e46a4f882a72df91431c637234b960ca" +
        "e9ea889e95fbad697940256e0059dba5c1b78ab42e39d4b2c4dcc3eec4d2f8635250c630442f8962" +
        "4f8398ef7fad0cda36d4fa0f5bb4538b4593f82f317c9fd3094de933c0c3e0586c131bdbd7138aff" +
        "0f83dccdc8a361a2d4886854afba3e9693c47fd55f847c066289b9b78bda41076f9b918c9375e678" +
        "a2268ce1dfbc4549f719616729b86a2a40602a699808280f65b37fa584a919570dbf8eb8b48896f6" +
        "c63cda68bf47a64b9e71a18208ad308208a9060b2a864886f70d010910020e318208983082089406" +
        "092a864886f70d010702a082088530820881020103310d300b06096086480165030402013066060b" +
        "2a864886f70d0109100104a0570455305302010106052a030405063031300d060960864801650304" +
        "020105000420705b962aa59b970895b8c20be7ae1ff0243a968eab481df648ca8ac231a388ec0203" +
        "00cafe180f32303235303630313132303030305aa08205cc308202d9308201c1a003020102020164" +
        "300d06092a864886f70d01010b0500301c311a301806035504030c11545341205465737420545341" +
        "20526f6f74301e170d3230303130313030303030305a170d3430303130313030303030305a301c31" +
        "1a301806035504030c1154534120546573742054534120526f6f7430820122300d06092a864886f7" +
        "0d01010105000382010f003082010a0282010100bc4895fbacfb59a8b7a2f7ac5744d7e75591e9cc" +
        "67e75277d5038efe45f0f0352235bd0f01a47a21de2712e611a1444e73956f4f27e04356084d5b92" +
        "c581b09a24b2fbc63eb4af7bdbbd1ecfeef4040edd3abb31e64cb0befdaf9a8f559ed3752b10901e" +
        "122bc8d340a67e969b885affe40c28d8afccdf518383ead490ea6ed3b8d0217232da8e0ee5a303ea" +
        "0eda76f127603ae61db09cfd8318d11c3d66a68c6c7a0a4112669662893999a409b95c3c624e0341" +
        "47b3dd98c0d006181b2c51b5ac1d14a213564c21d1a905617fe068f16702761c8a7a3447fc026ee4" +
        "0abfd29ef857edf669ca43a9bdb4717bbfc54d32bc7ba558f9ccc3dede4f1639f382819902030100" +
        "01a326302430120603551d130101ff040830060101ff020101300e0603551d0f0101ff0404030201" +
        "06300d06092a864886f70d01010b05000382010100295e63ee5a4561f1916112702564bad6b8209d" +
        "8a471f17924de1bb983c88ad4b5dbbaa47f86c9eac5f7fac43fabe84caff271250c17e2e1c94079c" +
        "02e189300f6426f4a862f3da72506243d25358c19ce78393726210a7c176da170e01615353b08f28" +
        "b28906a6449770170a3104b7095b92cccd4a0c6b535f1cfae71c9f6904db76d90d396e684bf83d67" +
        "50348c489937acdd7688b0826f709f3e7eadf4232bbd8f0574a4948f766e178cfd8d1eabf694aaef" +
        "d221cbc6fbc4cd35a0b022529c011db7bcf4b7c45daee9ac3ed79ede35cc47530877a2edc32e1682" +
        "225ab508030558e726487d03b24080f6a33653f2dd310a463d63af3c2adab43061ed8df656308202" +
        "eb308201d3a003020102020165300d06092a864886f70d01010b0500301c311a301806035504030c" +
        "1154534120546573742054534120526f6f74301e170d3234303130313030303030305a170d333030" +
        "3130313030303030305a301c311a301806035504030c11545341205465737420545341204c656166" +
        "30820122300d06092a864886f70d01010105000382010f003082010a0282010100a0fa46b636231b" +
        "5b3e163356bb8abc20ca7065a228268b55610b6170629f77d48aa9ebcc6c5fa6a2656866a939980c" +
        "3ac640ddf6daa91e01989b93ec156b0027c6a543e9d96e9269b6ea0924a6832301376943c28704e2" +
        "90c9ef21b8d07009c4c40d9b5be6c7ccd09e365a8383a8013032136522353d9d27729380a7b8aa89" +
        "0bba79e32c8cdcde2dcaf4e38c217d3829945bea82a65d9d774fb2a463a9b14e08bc45846a59d930" +
        "7405721d4720c2a8b1c60e238c6d9543414eca4ab088d0eb585d806e0d8573df1d7b0c6ff6ae5856" +
        "5ad00a97d47f540c9eae5af7550a70c4663d6b4230318ff088d91f0b2c9ccccbafb506fbbfa90d1e" +
        "3b2f4b6dfb65542b3f0203010001a3383036300c0603551d130101ff0402300030160603551d2501" +
        "01ff040c300a06082b06010505070308300e0603551d0f0101ff0404030206c0300d06092a864886" +
        "f70d01010b05000382010100a2218fbd6ecaa7bbfdb34d07ee8b48dcb49540d195274fb905ef10da" +
        "d2783a1aa742d3add9cfc948a3bb95f3ce567e115b54a582f7ce92893947ba3683c059a52336b454" +
        "0cfafcff29384fb2c3e217082e3e2ed7f5a75c3756114301b8755977971aa44aeb89115089c9accc" +
        "3063f60a6cc97e34924e51a55b492cbb2508cce6e3ff14394fddf1dc97a6fc0b7883503ed444f5d6" +
        "1171d03744cadf0a2306c64d6931ae8b7f4705733b2d346e197d82aaa74637209dee67ad5e12ae01" +
        "2dbbc51d0d1e4e2188d1d1be93381ef776874b7439a4ff736337f6a53e2663d662e018119b74d7e0" +
        "bfbe96a3d3181f57bcd2b878bf6fa37699118396f92a866b628bba49318202333082022f02010130" +
        "21301c311a301806035504030c1154534120546573742054534120526f6f74020165300b06096086" +
        "48016503040201a081e6301a06092a864886f70d010903310d060b2a864886f70d0109100104301c" +
        "06092a864886f70d010905310f170d3236303531373036323033385a302f06092a864886f70d0109" +
        "04312204200f4cec80fe68e39469dc2b972c26ac7041980850a50d9d581f119b288efd3496307906" +
        "092a864886f70d01090f316c306a300b060960864801650304012a300b0609608648016503040116" +
        "300b0609608648016503040102300a06082a864886f70d0307300e06082a864886f70d0302020200" +
        "80300d06082a864886f70d0302020140300706052b0e030207300d06082a864886f70d0302020128" +
        "300d06092a864886f70d010101050004820100031401e8fced693162070b7c1e5f1c72f5966eda3d" +
        "a9d9b5223cb5f9c2690ca7a69651894a29f9cf7e49e271e34713591663b7c2eb7c111de1bb6e9f8a" +
        "ec5594d4e72e544e35caeaf251a70e0dd0895f2af889f4c8e25b50fd5245a10cea831f46337aa365" +
        "c77efb0cee3ece8732359d5da2fc0fbc2019aaaf9655b5a49f2719e56676307c18db6e66559d6bd0" +
        "9c990985cd8426efa837b5290d1f588b8a7a4ec40a9ed8ee27dddbb39ad858ad9009c1c99fb49213" +
        "f3ee026480902367dc820cc32f75a12d0f37b2f470a2475b747e608e97d44e5fa12c3bc0e300ab44" +
        "37f6d85ded135af318f960420201bff62314e84652ecc8a812d1e536233f3c49968d25";

    private const string SignRootCertDerHex =
        "308202db308201c3a003020102020101300d06092a864886f70d01010b0500301d311b3019060355" +
        "04030c125453412054657374205369676e20526f6f74301e170d3230303130313030303030305a17" +
        "0d3430303130313030303030305a301d311b301906035504030c125453412054657374205369676e" +
        "20526f6f7430820122300d06092a864886f70d01010105000382010f003082010a0282010100c117" +
        "909516def2df9d58557987f84d4331e6c774c54a38ba3c6957a715ed67c132f5d16ba6e0015a18b8" +
        "0fb3293e9d9e0da7fa9fd28536c3c6e42fe5966b79d33323519370f6ef4a2d33cb503e176100ab58" +
        "9670aa7d77790ef84f4a7e250fced8442edb0867b07677dfd8bcdec5adfb86bc774adb0cdcc9cda7" +
        "64890958d06ebccf410f37de9723513bb4576dd5ad61ed7b33a19236926d206f8a69ef47b4fa0c56" +
        "1e2b4277a248fe2bad951d42fa12280d861240d94d9abfa9d28e260aa031af9487048e5d81d496cf" +
        "225bf110ec8b5858beb7f9bc413b8b27ca2b64e4e39b4f99fae59a7d1b94ee578dad8ebecf6ffa02" +
        "527e70ae191f4b727363068401570203010001a326302430120603551d130101ff040830060101ff" +
        "020102300e0603551d0f0101ff040403020106300d06092a864886f70d01010b0500038201010020" +
        "63e4bac4473e4e276bc9acfc0484a1e99d8f1478bd9c25d2cc0c6592a21e5daf5a08e03e64eb44c0" +
        "a97afc52789a1b3eedf56cf650a2151fc1e2b4f7db7590781fa4a1f2e5f602a5b8ecd157f85261ae" +
        "8deba559cbe2cb526f77aae3c532884b3d03215bb813fb9b0ccb31ab73532c54149a1ce78a7c9985" +
        "be6cf3f108e7f20d910836acd90f40bfe8002c84609eeb8a6fb7b977bd70a68c9c2b717638166c3f" +
        "3bec0c37e6ebe86c16a3e601ca1364a45b828a3c3c87bd788d399e61a0070d735a33a26bd5ae7632" +
        "9066cec6bb8fbd99167bff84f3322040959eb3ccaa373574bce5848818b68941fba705d79501e653" +
        "c6854e785981ef0c518b7d57d9d615";

    private const string RevokingCrlDerHex =
        "308201a430818d020101300d06092a864886f70d01010b050030253123302106035504030c1a5453" +
        "412054657374205369676e20496e7465726d656469617465170d3235303530313030303030305a17" +
        "0d3235303830313030303030305a302430220203055555170d3235303431353030303030305a300c" +
        "300a0603551d1504030a0101a00e300c300a0603551d140403020101300d06092a864886f70d0101" +
        "0b05000382010100288d6caa2c5483e7c88a7bbbd8b126841e2cabf1ee773afe52dd3d63087dc31d" +
        "44a07a2118e010b26dcafa4a0ceaf2b82c1297e826291fc2c1e1e1eec86667f102c8414ec7977991" +
        "f1a24bdd6500d21d0e1a82a17b1f166c51889cc0266a6fcf34a9f033141ea9b62dfa5d9aced16c33" +
        "1e535b05352736c81292f2631d953085e31370ae1af98a70a04d37dc988f352c7826dd10cf4ea821" +
        "48f92026a8c7fac257448dfd800fa42f0dc787a2d2fcabaaf47532766ce89c4bcfe7e6acf98f8456" +
        "48d346fd26f82411dfc92a8767f1893660839b2e0f24ddcb09e11ed28733c5f620de7aea1098ecd1" +
        "47805b1690af16b59ee48cefe427f2d489f4a9c4372f3cf3";

    private static readonly DateTimeOffset GoodTime
        = new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private static TrustStore SigningTrustStore()
    {
        TrustStore s = new();
        s.Add(X509Certificate.Decode(Convert.FromHexString(SignRootCertDerHex)));
        return s;
    }

    [Fact]
    public void TryFindForSignature_MatchingKey_ReturnsEntryWithCrl()
    {
        using PdfDocument doc = BuildPdf(VriKeyStamped);
        DocumentSecurityStore dss = doc.GetDocumentSecurityStore()!;
        dss.Vri.Should().ContainKey(VriKeyStamped);

        VriEntry? entry = dss.TryFindForSignature(doc.Signatures()[0].Contents);
        entry.Should().NotBeNull();
        entry!.Crls.Should().HaveCount(1);
        entry.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void TryFindForSignature_NonMatchingKey_ReturnsNull()
    {
        // VRI entry keyed by an unrelated SHA-1 hex — the lookup for the
        // current signature's hash should miss.
        using PdfDocument doc = BuildPdf(VriKeyUnstamped);
        DocumentSecurityStore dss = doc.GetDocumentSecurityStore()!;
        VriEntry? entry = dss.TryFindForSignature(doc.Signatures()[0].Contents);
        entry.Should().BeNull();
    }

    [Fact]
    public void Verify_VriContainsRevokingCrl_ReportsRevoked()
    {
        using PdfDocument doc = BuildPdf(VriKeyStamped);
        SignatureVerifyOptions opts = new()
        {
            TrustStore = SigningTrustStore(),
            ValidationTime = GoodTime,
            AutoExtractCmsCrls = false,
        };
        SignatureVerificationResult r = doc.Signatures()[0].Verify(doc, opts);
        r.TrustStatus.Should().Be(SignatureVerificationStatus.TrustChainCertificateRevoked);
        r.IntegrityVerified.Should().BeTrue();
        r.TrustValidated.Should().BeFalse();
    }

    [Fact]
    public void Verify_VriKeyDoesNotMatchSignature_RevocationIgnored()
    {
        using PdfDocument doc = BuildPdf(VriKeyUnstamped);
        SignatureVerifyOptions opts = new()
        {
            TrustStore = SigningTrustStore(),
            ValidationTime = GoodTime,
            AutoExtractCmsCrls = false,
        };
        SignatureVerificationResult r = doc.Signatures()[0].Verify(doc, opts);
        r.TrustStatus.Should().Be(SignatureVerificationStatus.Valid);
        r.TrustValidated.Should().BeTrue();
    }

    private static PdfDocument BuildPdf(string vriKey)
    {
        byte[] signedBytes = Convert.FromHexString(SignedBytesHex);
        byte[] cms = Convert.FromHexString(CmsStampedHex);
        byte[] crlDer = Convert.FromHexString(RevokingCrlDerHex);

        PdfObjectId catalogId = new(1, 0);
        PdfObjectId pagesId = new(2, 0);
        PdfObjectId acroFormId = new(3, 0);
        PdfObjectId sigFieldId = new(4, 0);
        PdfObjectId sigDictId = new(5, 0);
        PdfObjectId dssId = new(6, 0);
        PdfObjectId vriEntryId = new(7, 0);
        PdfObjectId crlStreamId = new(8, 0);

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

        PdfDictionary crlStreamDict = new();
        crlStreamDict.Set(PdfName.Intern("Length"), (PdfPrimitive)new PdfInteger(crlDer.Length));
        PdfStream crlStream = new(crlStreamDict, crlDer);

        PdfArray vriCrlArray = new();
        vriCrlArray.Add(new PdfReference(crlStreamId));
        PdfDictionary vriEntry = new();
        vriEntry.Set(PdfName.Intern("CRL"), vriCrlArray);

        PdfDictionary vriDict = new();
        vriDict.Set(PdfName.Intern(vriKey), new PdfReference(vriEntryId));

        PdfDictionary dssDict = new();
        dssDict.Set(PdfName.Intern("VRI"), vriDict);

        catalog.Set(PdfName.Intern("DSS"), new PdfReference(dssId));

        List<PdfIndirectObject> objects = new()
        {
            new(catalogId, catalog),
            new(pagesId, pages),
            new(acroFormId, acroForm),
            new(sigFieldId, sigField),
            new(sigDictId, sigDict),
            new(dssId, dssDict),
            new(vriEntryId, vriEntry),
            new(crlStreamId, crlStream),
        };

        PdfDictionary trailer = new();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new();
        ms.Write(signedBytes, 0, signedBytes.Length);
        PdfWriter.Write(ms, objects, trailer);
        ms.Position = 0;
        return PdfDocument.Open(ms, leaveOpen: false);
    }
}
