// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — End-to-end CAdES + signature timestamp tests
//
// Fixture: full chain (Root → Intermediate → Leaf) signing a PDF, plus a real
// RFC 3161 TimeStampToken embedded in id-aa-signatureTimeStampToken in the
// CMS SignerInfo unsigned attributes.

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

public sealed class PdfSignatureTimestampTests
{
    private const string SignedBytesHex = "255044462d312e370a2e2e2e2074696d657374616d70207465737420646f63756d656e74202e2e2e0a";

    private const string CmsWithTimestampHex =
        "30820dd006092a864886f70d010702a0820dc130820dbd020101310f300d06096086480165030402" +
        "010500300b06092a864886f70d010701a08205d0308202e3308201cba0030201020203012345300d" +
        "06092a864886f70d01010b050030263124302206035504030c1b54696d657374616d702054657374" +
        "20496e7465726d656469617465301e170d3234303130313030303030305a170d3237303130313030" +
        "303030305a3020311e301c06035504030c1554696d657374616d702054657374205369676e657230" +
        "820122300d06092a864886f70d01010105000382010f003082010a0282010100e5e6a0a860a739d6" +
        "6bf60476c654ea357cb37cf4f7b76159509078a93bdecbf049850b6a9a34331fae6898553270b345" +
        "017bb34bd74f440fe224e51967e1f0b3fd015643bd4b9b7c556c8b0b3d25b4f8e31422c0eb906ed9" +
        "c294429b6121c03dd5b9550d77d0e4649bbb27f830ce9e0d0bfd0f54202d6441bd381dc80b025346" +
        "88ca6d379aa52de1d015598f317da5350dc8c9993b897c967c99b02c4454362f248ec388316ef098" +
        "c95a9df756e85c8892a7647da3ac3a0c14eb6d1b3d3edbe253efa150be8cc811d583a3043b25cdac" +
        "f4b996adcce92acca498dd00935195289a022704b7a99596a60e9e65695662c45bfbc93ac3c1d6dd" +
        "87eb7dc45f2a404f0203010001a320301e300c0603551d130101ff04023000300e0603551d0f0101" +
        "ff0404030206c0300d06092a864886f70d01010b05000382010100346cd6d9052862c6c60c3da5bc" +
        "b3c6b7d25cb7d2311ff37a6bebe37ca4cb6beb3e0f1ba5bbbf95abc4e8288a5050a438cd936d9ec5" +
        "5c8324a2a695ed743036b49f4d92cf21b2c076ec5c65a9816372ad4e22d5fc5bee4ec83a1d7530b7" +
        "b40b567a53adfa352f3d32d7d77c55e92455b660262d1d11bb42614539dff68652dd8d050ac98b65" +
        "9c16df3dbf8f4b6f95987ac1a7017a9be007196707cead0f0022e3a803e08db73b762aac05881b74" +
        "2d8b6ee811aaffac219e916b249d681e833919c4be46b6c0528767283e4b9394cf4a1c8c30cfa20e" +
        "37b3a4ebe64ff69272e9662a13da01fa07a9f133473c6c54113929a19f55de89df8db6adf88f5eeb" +
        "d3f008308202e5308201cda003020102020102300d06092a864886f70d01010b0500301e311c301a" +
        "06035504030c1354696d657374616d70205465737420526f6f74301e170d32323031303130303030" +
        "30305a170d3332303130313030303030305a30263124302206035504030c1b54696d657374616d70" +
        "205465737420496e7465726d65646961746530820122300d06092a864886f70d0101010500038201" +
        "0f003082010a0282010100ab9a84fde5c48ddbd89d165b36dd66d486cf1479af37042be6ea7edcfc" +
        "6986ceb1b883e691907ad138a34b9017547d833a3e3e170dfa0dd3897020d8a520f9287eb298c942" +
        "a14e56ce867413bc930943e253f0a84f0e377b0667f768a49509d2425e76a49ed34851578ef3ba3a" +
        "18ce3a07eb7b85574a9d4127c66d4934067c824b8d03abe9cdeb6d809f5d91a95061bcdd342af4e0" +
        "19e1546d50392c2309b9b45dcb81c00cc25ae29a01925785cc77115c616a3b983742625b3c794fcd" +
        "a20bbcf372f6556975bcd348cf9651012203381d6434fa4bcbbde4d48bc668606d29a6cfa70701eb" +
        "b95809cb8f567e606457f25e29e30670be6ab887fcde348b87cbc90203010001a326302430120603" +
        "551d130101ff040830060101ff020100300e0603551d0f0101ff040403020106300d06092a864886" +
        "f70d01010b050003820101005b8867351c86582f3ac1d7e60ddc494e5fe6e9f4c33527282d0a2a39" +
        "eef7ee3bd4851b18b83d1adbe18ff19edbeca745794544eb5c0f23baeb5e8e7f0b6b60691347ad34" +
        "eae4c4e98df72690711988da78fad0da2f9438c2838e6f5963ff3d5d969904e304d45b24e6784231" +
        "f4b80e9056ab97767b711b1238f3d87afe4aa8d13c4e63f0fee8cbb77346fdcfb60bd75cf2522d4d" +
        "894cb3aa96cb46410c53b2abcaabc0eda356cc3ccaa887ce270e3cbbc6717a58cc00bd74fe977117" +
        "09c82532189ebb2f6e7ceb75a09d651bb87d49621ea19dc33b619e6d6bfe22dd75882eb7f453bb83" +
        "2d3335a01b0e24b6c610690bd0e7155516c8ceb47a07ca62cf42a1f1318207c4308207c002010130" +
        "2d30263124302206035504030c1b54696d657374616d70205465737420496e7465726d6564696174" +
        "650203012345300d06096086480165030402010500a081a1301806092a864886f70d010903310b06" +
        "092a864886f70d010701301c06092a864886f70d010905310f170d3236303531373035313533335a" +
        "302f06092a864886f70d010904312204206c1359a86b2c75dbde833b8512c2822942d05b40149e20" +
        "e004a9671ea00908a8303606092a864886f70d01090f31293027300b060960864801650304012a30" +
        "0b0609608648016503040116300b0609608648016503040102300d06092a864886f70d0101010500" +
        "0482010073bd26cda7aa2b1a9c13e76a67db3c70d1e5ab9fa6cd6b573a314bdc1deb1465176ba05a" +
        "821307dc5e863147b3b7d15bf7179bd7639eb38ae1a35a47d54dc8ef64b8cbec7f50df3615588369" +
        "7d772835fe8c84027c88827f93ae7c8636648198f1c2792504c16f13cadac4f2de7eba6827ffca61" +
        "58db6c67f8b650511fef2c5a87414844684979ae52668b089f472638a7110bb48ecbed26709548f6" +
        "0255203fad8014e09270ac68bb35d0d13ad4100e4d6e5bf69893ca5a02a9db1a86add25eaff47f50" +
        "958dd607de4112157010dda7c1bfefd5ca83e6fc5d7e5dafa2cdf588e66fc14c355ec5b3daaaf491" +
        "699e18d919d9a468e0bbb6f9306d7bcb9bf58a8da18205c4308205c0060b2a864886f70d01091002" +
        "0e318205af308205ab06092a864886f70d010702a082059c30820598020103310d300b0609608648" +
        "0165030402013066060b2a864886f70d0109100104a0570455305302010106052a03040506303130" +
        "0d0609608648016503040201050004207fc7c06a6fbcf14b1c8fdbb9248d682d125259c804ae0b87" +
        "23f54990f6271f0d020300cafe180f32303235303630313132303030305aa08202e7308202e33082" +
        "01cba003020102020163300d06092a864886f70d01010b050030183116301406035504030c0d5469" +
        "6d657374616d7020545341301e170d3230303130313030303030305a170d34303031303130303030" +
        "30305a30183116301406035504030c0d54696d657374616d702054534130820122300d06092a8648" +
        "86f70d01010105000382010f003082010a0282010100b10f56ac764f2376ae7504a4b65a807c1487" +
        "b16f38ce644438eb4ac2ad64b1ebde7b57409b38fbf99833188ab0fc5477af0c2dfdc2f50f5d9e7b" +
        "0bd9bf2432eee975f6a8b94588e2b3ed41aae6d0370c0d31dfd0efb5bfabd31ac8bc608dfd07fc0d" +
        "1886b3d3cda59e24c1a89357ea7c8c9002a1ddc5d80074dd7dfa2427c78c631abbbf6d06f19ed429" +
        "c12c044b6806d4c4600954bca4e0618fb8af1cda7924488e9ea0bb20365c5e82068dd45e9440b958" +
        "d14e555626a0b9d4897033efa394dc30237f50f41924f95af6593eb60dae1fa91975c4db653d9e2e" +
        "1e87e57629eee17d346c6c661304b42267659308fe9a7aec10cbca5fa238a042e21dcc358a230203" +
        "010001a3383036300c0603551d130101ff0402300030160603551d250101ff040c300a06082b0601" +
        "0505070308300e0603551d0f0101ff0404030206c0300d06092a864886f70d01010b050003820101" +
        "0088e65da1a7f722cbe1ac659313b9806a1b35bf42bc244840691fd0fa6ff6711b4e55b037a0fb14" +
        "77e4653e08ad925a5c2dd4f40f82a9862f1234555fe38e5cf6a2430fe985792c4f7ecb85f08aed2a" +
        "71d9e02ef2b2a53854e5666132b8ef15a120bc5a7367d91f588751b01b0f732c85bd20af6ab0f58f" +
        "5ee407fd502c28cc531e4b359e7aa672128ffddd7d8ceee41090a694932d1b141bf03beed9bf8cbc" +
        "7c279c6574ff86d0f59c177eb48a877e872a0ed56bd66a2dbf509c30e3c5a5a6e2d627a47514d1bd" +
        "68bb328d145a832d1c8ca4dc9966780847d8307240ddbd8960beaec0628f0c127ce212ada7f6fd95" +
        "6ed78b24cc45d53e7ba9d1cebd3e7fc5293182022f3082022b020101301d30183116301406035504" +
        "030c0d54696d657374616d7020545341020163300b0609608648016503040201a081e6301a06092a" +
        "864886f70d010903310d060b2a864886f70d0109100104301c06092a864886f70d010905310f170d" +
        "3236303531373035313533335a302f06092a864886f70d01090431220420bb5300298b15720a2869" +
        "67016ae03519a27d9a001a598fe030e76de11cc66f6b307906092a864886f70d01090f316c306a30" +
        "0b060960864801650304012a300b0609608648016503040116300b0609608648016503040102300a" +
        "06082a864886f70d0307300e06082a864886f70d030202020080300d06082a864886f70d03020201" +
        "40300706052b0e030207300d06082a864886f70d0302020128300d06092a864886f70d0101010500" +
        "048201003ce99a1c8a8a94001b97b9c110bde391f94089fa6debd023bd28d25ff1b3c89a5dbd62d1" +
        "bb704925ea130442e4c8c6d2694154a863e2a63a21ca434fc65744b573a0aa184c1d87b4cb322c5c" +
        "c2db4e10447a9dd91ba8a5a30038a900159590225709d0f7ed05b3f0453de66bfa9b66281bc1cc12" +
        "9ea10adb7503c17f14b72eeacb3146605e119ae1e82f5a93b7c6af0d40b63ca5be1e2cb8dcb3b8c6" +
        "0f07d303bac90cc0809dee6bb2befda1d2fdfeb7f9b511ea8936b7e180613c32e9d34e41d403e258" +
        "f053ef185816b33214d234541b1fbd363bcc519b8661c34fca6c7ee87d5905fb2111f5cc9854d402" +
        "94fb5257a4140291d2fe2ee17274bddee87aca4e";

    private const string CmsWithoutTimestampHex =
        "3082080806092a864886f70d010702a08207f9308207f5020101310f300d06096086480165030402" +
        "010500300b06092a864886f70d010701a08205d0308202e3308201cba0030201020203012345300d" +
        "06092a864886f70d01010b050030263124302206035504030c1b54696d657374616d702054657374" +
        "20496e7465726d656469617465301e170d3234303130313030303030305a170d3237303130313030" +
        "303030305a3020311e301c06035504030c1554696d657374616d702054657374205369676e657230" +
        "820122300d06092a864886f70d01010105000382010f003082010a0282010100e5e6a0a860a739d6" +
        "6bf60476c654ea357cb37cf4f7b76159509078a93bdecbf049850b6a9a34331fae6898553270b345" +
        "017bb34bd74f440fe224e51967e1f0b3fd015643bd4b9b7c556c8b0b3d25b4f8e31422c0eb906ed9" +
        "c294429b6121c03dd5b9550d77d0e4649bbb27f830ce9e0d0bfd0f54202d6441bd381dc80b025346" +
        "88ca6d379aa52de1d015598f317da5350dc8c9993b897c967c99b02c4454362f248ec388316ef098" +
        "c95a9df756e85c8892a7647da3ac3a0c14eb6d1b3d3edbe253efa150be8cc811d583a3043b25cdac" +
        "f4b996adcce92acca498dd00935195289a022704b7a99596a60e9e65695662c45bfbc93ac3c1d6dd" +
        "87eb7dc45f2a404f0203010001a320301e300c0603551d130101ff04023000300e0603551d0f0101" +
        "ff0404030206c0300d06092a864886f70d01010b05000382010100346cd6d9052862c6c60c3da5bc" +
        "b3c6b7d25cb7d2311ff37a6bebe37ca4cb6beb3e0f1ba5bbbf95abc4e8288a5050a438cd936d9ec5" +
        "5c8324a2a695ed743036b49f4d92cf21b2c076ec5c65a9816372ad4e22d5fc5bee4ec83a1d7530b7" +
        "b40b567a53adfa352f3d32d7d77c55e92455b660262d1d11bb42614539dff68652dd8d050ac98b65" +
        "9c16df3dbf8f4b6f95987ac1a7017a9be007196707cead0f0022e3a803e08db73b762aac05881b74" +
        "2d8b6ee811aaffac219e916b249d681e833919c4be46b6c0528767283e4b9394cf4a1c8c30cfa20e" +
        "37b3a4ebe64ff69272e9662a13da01fa07a9f133473c6c54113929a19f55de89df8db6adf88f5eeb" +
        "d3f008308202e5308201cda003020102020102300d06092a864886f70d01010b0500301e311c301a" +
        "06035504030c1354696d657374616d70205465737420526f6f74301e170d32323031303130303030" +
        "30305a170d3332303130313030303030305a30263124302206035504030c1b54696d657374616d70" +
        "205465737420496e7465726d65646961746530820122300d06092a864886f70d0101010500038201" +
        "0f003082010a0282010100ab9a84fde5c48ddbd89d165b36dd66d486cf1479af37042be6ea7edcfc" +
        "6986ceb1b883e691907ad138a34b9017547d833a3e3e170dfa0dd3897020d8a520f9287eb298c942" +
        "a14e56ce867413bc930943e253f0a84f0e377b0667f768a49509d2425e76a49ed34851578ef3ba3a" +
        "18ce3a07eb7b85574a9d4127c66d4934067c824b8d03abe9cdeb6d809f5d91a95061bcdd342af4e0" +
        "19e1546d50392c2309b9b45dcb81c00cc25ae29a01925785cc77115c616a3b983742625b3c794fcd" +
        "a20bbcf372f6556975bcd348cf9651012203381d6434fa4bcbbde4d48bc668606d29a6cfa70701eb" +
        "b95809cb8f567e606457f25e29e30670be6ab887fcde348b87cbc90203010001a326302430120603" +
        "551d130101ff040830060101ff020100300e0603551d0f0101ff040403020106300d06092a864886" +
        "f70d01010b050003820101005b8867351c86582f3ac1d7e60ddc494e5fe6e9f4c33527282d0a2a39" +
        "eef7ee3bd4851b18b83d1adbe18ff19edbeca745794544eb5c0f23baeb5e8e7f0b6b60691347ad34" +
        "eae4c4e98df72690711988da78fad0da2f9438c2838e6f5963ff3d5d969904e304d45b24e6784231" +
        "f4b80e9056ab97767b711b1238f3d87afe4aa8d13c4e63f0fee8cbb77346fdcfb60bd75cf2522d4d" +
        "894cb3aa96cb46410c53b2abcaabc0eda356cc3ccaa887ce270e3cbbc6717a58cc00bd74fe977117" +
        "09c82532189ebb2f6e7ceb75a09d651bb87d49621ea19dc33b619e6d6bfe22dd75882eb7f453bb83" +
        "2d3335a01b0e24b6c610690bd0e7155516c8ceb47a07ca62cf42a1f1318201fc308201f802010130" +
        "2d30263124302206035504030c1b54696d657374616d70205465737420496e7465726d6564696174" +
        "650203012345300d06096086480165030402010500a081a1301806092a864886f70d010903310b06" +
        "092a864886f70d010701301c06092a864886f70d010905310f170d3236303531373035313533335a" +
        "302f06092a864886f70d010904312204206c1359a86b2c75dbde833b8512c2822942d05b40149e20" +
        "e004a9671ea00908a8303606092a864886f70d01090f31293027300b060960864801650304012a30" +
        "0b0609608648016503040116300b0609608648016503040102300d06092a864886f70d0101010500" +
        "0482010073bd26cda7aa2b1a9c13e76a67db3c70d1e5ab9fa6cd6b573a314bdc1deb1465176ba05a" +
        "821307dc5e863147b3b7d15bf7179bd7639eb38ae1a35a47d54dc8ef64b8cbec7f50df3615588369" +
        "7d772835fe8c84027c88827f93ae7c8636648198f1c2792504c16f13cadac4f2de7eba6827ffca61" +
        "58db6c67f8b650511fef2c5a87414844684979ae52668b089f472638a7110bb48ecbed26709548f6" +
        "0255203fad8014e09270ac68bb35d0d13ad4100e4d6e5bf69893ca5a02a9db1a86add25eaff47f50" +
        "958dd607de4112157010dda7c1bfefd5ca83e6fc5d7e5dafa2cdf588e66fc14c355ec5b3daaaf491" +
        "699e18d919d9a468e0bbb6f9306d7bcb9bf58a8d";

    private const string RootCertDerHex =
        "308202dd308201c5a003020102020101300d06092a864886f70d01010b0500301e311c301a060355" +
        "04030c1354696d657374616d70205465737420526f6f74301e170d3230303130313030303030305a" +
        "170d3430303130313030303030305a301e311c301a06035504030c1354696d657374616d70205465" +
        "737420526f6f7430820122300d06092a864886f70d01010105000382010f003082010a0282010100" +
        "b07439ad97d64f3f4ed9646b7b00f860974471f5fd8bd18def94af7264bf6bac7da1f7c247e12ced" +
        "106531f4b5e3be77b994d8c72fee281a320443733e7330fc1da88c5406a53cfd78c35c74ca750c9e" +
        "36ef7fdfc69d543b3605370f133a145b81ad247c580a6489f3e6eb0b2562bb8c71f7590ddffbd59f" +
        "4b6eb8e8752072595207bc5e1aa0dcbcb1744f8712ca8518a34450086cc754a06ebffbbb9cc952e3" +
        "4cd34bce61720addf82b5c0db33b68aa2618db17ec7a24084a1385bdca3381cfcf5ab6eb47264e82" +
        "c01fced261a89c9b3c293e2dd03292b9a8257b4be939f869831673fca4f8979fcfc287abcc6a16b3" +
        "b04a664c53466274e4f95e2c6510c0990203010001a326302430120603551d130101ff0408300601" +
        "01ff020102300e0603551d0f0101ff040403020106300d06092a864886f70d01010b050003820101" +
        "008cd9097b385f7452015256ef75f164a00e71cce754b48476254ac502751eacf2849e39784305b6" +
        "9da9031ee44533f41772642f4aee633968fb06ba6876ff78e14f9c7b64182c9d0803b3e6892298c5" +
        "16c287ad33b1d654a024a1bdfd85e9763f3baa5a576210e9600cda8f0d0c8a3874fb7dcd0516c03b" +
        "86c67dbaf88c34099917fe2d32fac6cd41c2c43111a64117bb5d721ef5f59536f37cc6160853f94e" +
        "97b677e18d30e890bcdd02ad73e4a78a98a60d2c5b9d4c26b02437cde8ce95f5e12853586cff61b2" +
        "26340a8db9e19c5b05482b4729bbeaffe8f7c81510b1c3b97cfac360485d3e96a35ad15c0c15e5ea" +
        "22bad4a3c9d0a7247f6cb48e36cf80a60f";

    private static readonly DateTimeOffset GoodTime
        = new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private static TrustStore LoadTrustStore()
    {
        TrustStore store = new();
        store.Add(X509Certificate.Decode(Convert.FromHexString(RootCertDerHex)));
        return store;
    }

    [Fact]
    public void Verify_WithEmbeddedTimestamp_ReportsTimestampValidated()
    {
        using PdfDocument doc = BuildPdf(CmsWithTimestampHex);
        SignatureVerifyOptions options = new()
        {
            TrustStore = LoadTrustStore(),
            ValidationTime = GoodTime,
        };
        SignatureVerificationResult r = doc.Signatures()[0].Verify(doc, options);
        r.TrustStatus.Should().Be(SignatureVerificationStatus.Valid);
        r.TrustValidated.Should().BeTrue();
        r.TimestampValidated.Should().BeTrue();
        r.SignatureTimestamp.Should().NotBeNull();
        r.TimestampCertificate.Should().NotBeNull();
    }

    [Fact]
    public void Verify_WithoutEmbeddedTimestamp_DoesNotReportTimestamp()
    {
        using PdfDocument doc = BuildPdf(CmsWithoutTimestampHex);
        SignatureVerifyOptions options = new()
        {
            TrustStore = LoadTrustStore(),
            ValidationTime = GoodTime,
        };
        SignatureVerificationResult r = doc.Signatures()[0].Verify(doc, options);
        r.TrustStatus.Should().Be(SignatureVerificationStatus.Valid);
        r.TimestampValidated.Should().BeFalse();
        r.SignatureTimestamp.Should().BeNull();
    }

    [Fact]
    public void Verify_WithTimestampNoExplicitValidationTime_UsesTimestampAsValidationTime()
    {
        using PdfDocument doc = BuildPdf(CmsWithTimestampHex);
        SignatureVerifyOptions options = new()
        {
            TrustStore = LoadTrustStore(),
            // No ValidationTime supplied — orchestrator should pick TST genTime.
        };
        SignatureVerificationResult r = doc.Signatures()[0].Verify(doc, options);
        r.TrustStatus.Should().Be(SignatureVerificationStatus.Valid);
        r.TimestampValidated.Should().BeTrue();
    }

    [Fact]
    public void Verify_AutoVerifyTimestampOff_IgnoresTimestamp()
    {
        using PdfDocument doc = BuildPdf(CmsWithTimestampHex);
        SignatureVerifyOptions options = new()
        {
            TrustStore = LoadTrustStore(),
            ValidationTime = GoodTime,
            AutoVerifySignatureTimestamp = false,
        };
        SignatureVerificationResult r = doc.Signatures()[0].Verify(doc, options);
        r.TrustStatus.Should().Be(SignatureVerificationStatus.Valid);
        r.TimestampValidated.Should().BeFalse();
    }

    private static PdfDocument BuildPdf(string cmsHex)
    {
        byte[] signedBytes = Convert.FromHexString(SignedBytesHex);
        byte[] cms = Convert.FromHexString(cmsHex);

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
