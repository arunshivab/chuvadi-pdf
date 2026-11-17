// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 5652 — CMS, §5.4 Message Digest Calculation Process
//                  §5.6 Signature Verification Process
//        PDF 32000-1 §12.8 — Digital Signatures
// PHASE: Phase 1.1.4 — Signature verification orchestration
//
// Verification recipe for a CMS-based PDF signature:
//
//   1. Decode the /Contents bytes as a CMS SignedData container.
//   2. Locate the SignerInfo (PDF signatures always carry exactly one).
//   3. Find the signer's X.509 certificate inside SignedData.Certificates,
//      keyed by SignerInfo.SignerId (issuer+serial or subjectKeyIdentifier).
//   4. Extract the signer's public key from the certificate's
//      SubjectPublicKeyInfo, dispatching to RsaPublicKey or EcdsaPublicKey.
//   5. Compute the digest of the file bytes covered by /ByteRange using the
//      hash algorithm named in SignerInfo.DigestAlgorithm.
//   6. Two paths depending on whether SignerInfo has signed attributes:
//      a) WITH signed attributes (typical for PDF):
//         - The messageDigest signed attribute MUST equal the hash from step 5.
//           If not, the document was tampered with after signing.
//         - The signature itself is over the DER-encoded SET of signed attributes
//           (CmsAttributeTable.DerEncodedForVerification). Hash that with
//           DigestAlgorithm, then verify the signature.
//      b) WITHOUT signed attributes (rare in PDF):
//         - The signature is over the raw signed bytes from step 5 directly.
//   7. Call SignatureVerifier.Verify with the SignerInfo.SignatureAlgorithm,
//      the extracted public key, the chosen hash, and SignerInfo.Signature.

using System;
using Chuvadi.Cryptography.Asn1;
using System.Collections.Generic;
using Chuvadi.Cryptography.Cms;
using Chuvadi.Cryptography.PathValidation;
using Chuvadi.Cryptography.Ocsp;
using Chuvadi.Cryptography.Revocation;
using Chuvadi.Cryptography.Timestamps;
using Chuvadi.Cryptography.Hashing;
using Chuvadi.Cryptography.Oids;
using Chuvadi.Cryptography.PublicKey;
using Chuvadi.Cryptography.X509;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Signatures.Dss;

namespace Chuvadi.Pdf.Signatures.Verification;

/// <summary>
/// Orchestrates verification of a single <see cref="PdfSignature"/>.
/// </summary>
public static class PdfSignatureVerifier
{
    /// <summary>
    /// Verifies <paramref name="signature"/> against the bytes it covers in
    /// <paramref name="document"/>.
    /// </summary>
    /// <remarks>
    /// Since 3.35.0 (D-396) <see cref="SignatureVerificationResult.IsValid"/>
    /// requires all three: the signature is mathematically intact
    /// (<see cref="SignatureVerificationResult.IntegrityVerified"/>), nothing
    /// but later signatures or validation data was added after signing
    /// (<see cref="SignatureVerificationResult.ModificationsAfterSigning"/>),
    /// and the signer's certificate chains to an anchor in
    /// <see cref="SignatureVerifyOptions.TrustStore"/>. Without a trust store
    /// an intact, unmodified signature is
    /// <see cref="SignatureVerificationStatus.TrustUnknown"/>, not valid.
    /// </remarks>
    public static SignatureVerificationResult Verify(
        PdfSignature signature,
        PdfDocument document,
        SignatureVerifyOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(document);
        SignatureVerifyOptions opts = options ?? SignatureVerifyOptions.Default;
        SignatureVerificationResult core = VerifyCryptographyAndTrust(signature, document, opts);
        SignatureModificationAnalyzer.Outcome changes = SignatureModificationAnalyzer.Analyze(signature, document);
        return Combine(core, changes, opts);
    }

    // The status, in order: an integrity failure first, then a change after
    // signing, then trust (unknown without a trust store, or the trust
    // failure). The modification and integrity findings are always reported
    // in their own properties.
    private static SignatureVerificationResult Combine(
        SignatureVerificationResult core, SignatureModificationAnalyzer.Outcome changes, SignatureVerifyOptions opts)
    {
        SignatureVerificationStatus status = core.Status;
        string message = core.Message;
        if (core.IntegrityVerified)
        {
            if (changes.Kind == SignatureModifications.Other)
            {
                status = SignatureVerificationStatus.ModifiedAfterSigning;
                message = changes.Message + " The signed revision itself is intact. " + core.Message;
            }
            else if (core.Status == SignatureVerificationStatus.Valid && opts.TrustStore is null)
            {
                status = SignatureVerificationStatus.TrustUnknown;
                message = "The signature is intact, but no trust store was supplied, so who signed it is not verified. "
                    + changes.Message;
            }
            else if (core.Status == SignatureVerificationStatus.Valid)
            {
                message = core.Message + " " + changes.Message;
            }
        }

        return new SignatureVerificationResult(
            status,
            message,
            core.SignerCertificate,
            core.IntegrityVerified,
            core.TrustValidated,
            core.ValidatedPath,
            core.TimestampValidated,
            core.SignatureTimestamp,
            core.TimestampCertificate,
            core.TimestampTrustValidated,
            core.TimestampValidatedPath)
        {
            ModificationsAfterSigning = changes.Kind,
            CoversWholeDocument = changes.CoversWholeDocument,
            TrustStatus = TrustFinding(core, opts),
        };
    }

    private static SignatureVerificationStatus TrustFinding(SignatureVerificationResult core, SignatureVerifyOptions opts)
    {
        if (!core.IntegrityVerified)
        {
            return SignatureVerificationStatus.TrustUnknown;
        }

        if (core.Status == SignatureVerificationStatus.Valid)
        {
            return opts.TrustStore is null ? SignatureVerificationStatus.TrustUnknown : SignatureVerificationStatus.Valid;
        }

        return core.Status;
    }

    private static SignatureVerificationResult VerifyCryptographyAndTrust(
        PdfSignature signature,
        PdfDocument document,
        SignatureVerifyOptions? options)
    {

        if (!signature.IsCmsBased)
        {
            return Fail(SignatureVerificationStatus.UnsupportedSubFilter,
                $"SubFilter '{signature.SubFilter ?? "<null>"}' is not CMS-based; Chuvadi can verify only CMS-based signatures.");
        }

        SignedData signedData;
        try
        {
            signedData = signature.DecodeCms();
        }
        catch (Exception ex) when (ex is Chuvadi.Cryptography.Asn1.Asn1Exception
                                     or ArgumentException
                                     or InvalidOperationException)
        {
            return Fail(SignatureVerificationStatus.MalformedSignature,
                $"Could not parse the CMS envelope: {ex.Message}");
        }

        if (signedData.SignerInfos.Count == 0)
        {
            return Fail(SignatureVerificationStatus.MalformedSignature,
                "The CMS envelope contains no SignerInfo.");
        }

        // PDF signatures carry exactly one signer. If more are present, only
        // the first is verified — this matches the practical interpretation in
        // every PDF viewer Chuvadi targets.
        SignerInfo signer = signedData.SignerInfos[0];

        // Step 3 — locate the signing certificate.
        X509Certificate? signerCert = signer.FindSignerCertificate(signedData.Certificates);
        if (signerCert is null)
        {
            return Fail(SignatureVerificationStatus.SignerCertificateNotFound,
                "The signer's certificate is not embedded in the CMS envelope.");
        }

        // Step 4 — extract the public key.
        IPublicKey publicKey;
        try
        {
            publicKey = ExtractPublicKey(signerCert);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return new SignatureVerificationResult(
                SignatureVerificationStatus.UnsupportedAlgorithm,
                $"Could not extract a usable public key from the signer's certificate: {ex.Message}",
                signerCert,
                integrityVerified: false);
        }

        // Step 5 — hash the byte-range bytes with the digest algorithm.
        IHashAlgorithm hash;
        try
        {
            hash = HashFactory.CreateFromOid(signer.DigestAlgorithm.Algorithm);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return new SignatureVerificationResult(
                SignatureVerificationStatus.UnsupportedAlgorithm,
                $"DigestAlgorithm {signer.DigestAlgorithm.Algorithm} is not supported by Chuvadi: {ex.Message}",
                signerCert,
                integrityVerified: false);
        }

        byte[] signedBytes = document.GetSignedBytes(signature);
        hash.Update(signedBytes);
        byte[] eContentHash = new byte[hash.DigestSize];
        hash.Finish(eContentHash);

        // Step 6 — decide what to hash and verify.
        byte[] toHash;
        if (signer.HasSignedAttributes)
        {
            // 6a — verify messageDigest attribute then sign over the DER-encoded SET.
            byte[]? declaredDigest = signer.MessageDigest;
            if (declaredDigest is null)
            {
                return new SignatureVerificationResult(
                    SignatureVerificationStatus.MalformedSignature,
                    "SignedAttributes is present but the required messageDigest attribute is missing.",
                    signerCert,
                    integrityVerified: false);
            }
            if (!ConstantTimeEquals(declaredDigest, eContentHash))
            {
                return new SignatureVerificationResult(
                    SignatureVerificationStatus.DigestMismatch,
                    "The messageDigest signed attribute does not match the hash of the document's byte-range content. The document was tampered with after signing.",
                    signerCert,
                    integrityVerified: false);
            }
            // The signature is over the DER encoding of the SET OF signed attributes.
            toHash = signer.SignedAttributes!.DerEncodedForVerification;
        }
        else
        {
            // 6b — signature is over the eContent hash itself. With detached signatures,
            // that's the byte-range content directly.
            toHash = signedBytes;
        }

        // Compute the actual hash to feed to the verifier.
        IHashAlgorithm sigHash = HashFactory.CreateFromOid(signer.DigestAlgorithm.Algorithm);
        sigHash.Update(toHash);
        byte[] finalDigest = new byte[sigHash.DigestSize];
        sigHash.Finish(finalDigest);

        // Step 7 — verify the signature.
        AlgorithmIdentifier sigAlg = NormaliseSignatureAlgorithm(
            signer.SignatureAlgorithm, signer.DigestAlgorithm, publicKey);

        bool cryptoOk;
        try
        {
            cryptoOk = SignatureVerifier.Verify(
                sigAlg,
                publicKey,
                finalDigest,
                signer.Signature);
        }
        catch (NotSupportedException ex)
        {
            return new SignatureVerificationResult(
                SignatureVerificationStatus.UnsupportedAlgorithm,
                $"Signature algorithm not supported: {ex.Message}",
                signerCert,
                integrityVerified: false);
        }
        catch (ArgumentException ex)
        {
            return new SignatureVerificationResult(
                SignatureVerificationStatus.MalformedSignature,
                $"Signature could not be verified: {ex.Message}",
                signerCert,
                integrityVerified: false);
        }

        if (!cryptoOk)
        {
            return new SignatureVerificationResult(
                SignatureVerificationStatus.Invalid,
                "The signature does not verify against the signer's public key.",
                signerCert,
                integrityVerified: false);
        }

        // Integrity verified. Optionally run path validation against the trust store.
        SignatureVerifyOptions opts = options ?? SignatureVerifyOptions.Default;
        if (opts.TrustStore is null)
        {
            return new SignatureVerificationResult(
                SignatureVerificationStatus.Valid,
                "The signature is cryptographically valid.",
                signerCert,
                integrityVerified: true,
                trustValidated: false,
                validatedPath: null);
        }

        return RunPathValidation(
            signerCert,
            signedData,
            opts,
            signature.SigningTimeFromDictionary,
            signer,
            document,
            signature);
    }

    private static SignatureVerificationResult RunPathValidation(
        X509Certificate signerCert,
        SignedData signedData,
        SignatureVerifyOptions opts,
        DateTimeOffset? declaredSigningTime,
        SignerInfo signer,
        PdfDocument document,
        PdfSignature signature)
    {
        // ── PDF DSS + VRI extraction ────────────────────────────────────
        // Read the document's /DSS dictionary once, up-front, so its certs /
        // CRLs / OCSPs are available everywhere below. Then look up the
        // per-signature /VRI entry for THIS signature.
        DocumentSecurityStore? dss = opts.AutoExtractDss
            ? document.GetDocumentSecurityStore()
            : null;
        VriEntry? vri = dss?.TryFindForSignature(signature.Contents);
        // ────────────────────────────────────────────────────────────────
        // ── CAdES: signature timestamp ──────────────────────────────────
        // If a signatureTimeStampToken is present and verifies, it gives us a
        // trustworthy "when was this signature made" assertion. We can then
        // evaluate the cert chain at THAT time even if certs have since
        // expired or been revoked. This is the CAdES-T trick.
        bool tsValidated = false;
        DateTimeOffset? tsTime = null;
        X509Certificate? tsCert = null;
        TimeStampToken? signatureTst = null;
        if (opts.AutoVerifySignatureTimestamp)
        {
            signatureTst = TryDecodeSignatureTimestamp(signer);
            if (signatureTst is not null)
            {
                tsTime = signatureTst.TstInfo.GenTime;
                TimeStampVerificationResult tsr =
                    TimeStampTokenVerifier.Verify(signatureTst, signer.Signature);
                if (tsr.IsValid)
                {
                    tsValidated = true;
                    tsCert = tsr.SignerCertificate;
                }
            }
        }
        // ────────────────────────────────────────────────────────────────

        // Choose validation time. Prefer signer's CMS signingTime attribute (more
        // trustworthy than the /M field in the PDF dict, but still untimestamped).
        // Fall back to the /M field, and finally to UtcNow.
        DateTimeOffset validationTime = opts.ValidationTime
            ?? (tsValidated ? tsTime : null)
            ?? signer.SigningTime
            ?? declaredSigningTime
            ?? DateTimeOffset.UtcNow;

        // Assemble intermediates: cert envelope + any extras.
        List<X509Certificate> intermediates = new();
        foreach (X509Certificate c in signedData.Certificates)
        {
            // Skip the signer's own certificate — it's the leaf, not an intermediate.
            if (c.RawEncoding.Length == signerCert.RawEncoding.Length)
            {
                bool same = true;
                for (int i = 0; i < c.RawEncoding.Length; i++)
                {
                    if (c.RawEncoding[i] != signerCert.RawEncoding[i]) { same = false; break; }
                }
                if (same) { continue; }
            }
            intermediates.Add(c);
        }
        if (opts.AutoExtractCadesValues)
        {
            foreach (X509Certificate c in ExtractCadesCertValues(signer))
            {
                intermediates.Add(c);
            }
        }
        if (dss is not null)
        {
            foreach (X509Certificate c in dss.Certificates)
            {
                intermediates.Add(c);
            }
        }
        if (vri is not null)
        {
            foreach (X509Certificate c in vri.Certificates)
            {
                intermediates.Add(c);
            }
        }
        if (opts.ExtraIntermediates is not null)
        {
            intermediates.AddRange(opts.ExtraIntermediates);
        }

        IReadOnlyList<CertificatePath> paths = CertificatePathBuilder.BuildPaths(
            signerCert, intermediates, opts.TrustStore!);

        if (paths.Count == 0)
        {
            return new SignatureVerificationResult(
                SignatureVerificationStatus.TrustChainBroken,
                "Signature is cryptographically valid, but the signer's certificate does not chain to any configured trust anchor.",
                signerCert,
                integrityVerified: true,
                trustValidated: false,
                validatedPath: null,
                timestampValidated: tsValidated,
                signatureTimestamp: tsTime,
                timestampCertificate: tsCert,
                timestampTrustValidated: false,
                timestampValidatedPath: null);
        }

        // Gather CRLs: ones embedded in the CMS envelope (when AutoExtractCmsCrls)
        // plus any caller-supplied extras.
        List<CertificateList> allCrls = new();
        if (opts.AutoExtractCmsCrls)
        {
            foreach (byte[] crlBytes in signedData.Crls)
            {
                try { allCrls.Add(CertificateList.Decode(crlBytes)); }
                catch (Exception ex) when (ex is Chuvadi.Cryptography.Asn1.Asn1Exception
                                              or NotSupportedException
                                              or ArgumentException)
                {
                    // Skip malformed or unsupported CRLs rather than failing the whole
                    // verification — a bad CRL shouldn't poison a valid signature.
                }
            }
        }
        if (opts.ExtraCrls is not null)
        {
            allCrls.AddRange(opts.ExtraCrls);
        }
        if (dss is not null)
        {
            allCrls.AddRange(dss.Crls);
        }
        if (vri is not null)
        {
            allCrls.AddRange(vri.Crls);
        }

        List<OcspResponse> allOcsp = new();
        if (opts.AutoExtractCadesValues)
        {
            ExtractCadesRevocationValues(signer, allCrls, allOcsp);
        }
        if (dss is not null)
        {
            allOcsp.AddRange(dss.OcspResponses);
        }
        if (vri is not null)
        {
            allOcsp.AddRange(vri.OcspResponses);
        }
        if (opts.ExtraOcspResponses is not null)
        {
            allOcsp.AddRange(opts.ExtraOcspResponses);
        }

        // ── TSA chain validation ────────────────────────────────────────
        // If the caller supplied a TSA trust store and we verified a timestamp,
        // path-validate the TSA's cert at the timestamp's genTime using all the
        // revocation material we've gathered. The TSA-side intermediates are
        // the certs embedded in the TST itself plus anything in the DSS / VRI /
        // caller's ExtraIntermediates. We do NOT mix TSA validation into the
        // signer's path — they are independent trust regimes.
        bool tsTrustValidated = false;
        CertificatePath? tsPath = null;
        if (opts.TsaTrustStore is not null && tsValidated && tsCert is not null && signatureTst is not null)
        {
            List<X509Certificate> tsaIntermediates = new();
            foreach (X509Certificate c in signatureTst.SignedData.Certificates)
            {
                // Skip the TSA's leaf cert itself; it's the subject, not an intermediate.
                if (CertsEqual(c, tsCert)) { continue; }
                tsaIntermediates.Add(c);
            }
            if (dss is not null) { tsaIntermediates.AddRange(dss.Certificates); }
            if (vri is not null) { tsaIntermediates.AddRange(vri.Certificates); }
            if (opts.ExtraIntermediates is not null) { tsaIntermediates.AddRange(opts.ExtraIntermediates); }

            IReadOnlyList<CertificatePath> tsaPaths = CertificatePathBuilder.BuildPaths(
                tsCert, tsaIntermediates, opts.TsaTrustStore);
            if (tsaPaths.Count > 0)
            {
                CertificatePathValidationResult tsaResult = CertificatePathValidator.Validate(
                    tsaPaths, tsTime ?? DateTimeOffset.UtcNow, allCrls, allOcsp);
                if (tsaResult.IsValid)
                {
                    tsTrustValidated = true;
                    tsPath = tsaResult.ValidatedPath;
                }
            }
        }
        // ────────────────────────────────────────────────────────────────

        CertificatePathValidationResult pathResult =
            CertificatePathValidator.Validate(paths, validationTime, allCrls, allOcsp);

        if (pathResult.IsValid)
        {
            return new SignatureVerificationResult(
                SignatureVerificationStatus.Valid,
                $"Signature is cryptographically valid and the signer's certificate chain validates to a trust anchor ('{pathResult.ValidatedPath!.Anchor.Subject}').",
                signerCert,
                integrityVerified: true,
                trustValidated: true,
                validatedPath: pathResult.ValidatedPath,
                timestampValidated: tsValidated,
                signatureTimestamp: tsTime,
                timestampCertificate: tsCert,
                timestampTrustValidated: tsTrustValidated,
                timestampValidatedPath: tsPath);
        }

        SignatureVerificationStatus status = pathResult.Status switch
        {
            CertificatePathValidationStatus.NoPathFound => SignatureVerificationStatus.TrustChainBroken,
            CertificatePathValidationStatus.CertificateExpired => SignatureVerificationStatus.TrustChainCertificateOutOfValidity,
            CertificatePathValidationStatus.CertificateNotYetValid => SignatureVerificationStatus.TrustChainCertificateOutOfValidity,
            CertificatePathValidationStatus.CertificateRevoked => SignatureVerificationStatus.TrustChainCertificateRevoked,
            _ => SignatureVerificationStatus.TrustChainInvalid,
        };
        return new SignatureVerificationResult(
            status,
            $"Signature is cryptographically valid but trust-chain validation failed: {pathResult.Message}",
            signerCert,
            integrityVerified: true,
            trustValidated: false,
            validatedPath: null,
            timestampValidated: tsValidated,
            signatureTimestamp: tsTime,
            timestampCertificate: tsCert,
            timestampTrustValidated: tsTrustValidated,
            timestampValidatedPath: tsPath);
    }

    /// <summary>
    /// Some signers (notably OpenSSL's PKCS7_sign) set SignerInfo.signatureAlgorithm
    /// to the bare key-algorithm OID (rsaEncryption / id-ecPublicKey) rather than
    /// the combined "hashWithEncryption" OID. RFC 5652 §10.1.2 allows this; the
    /// hash is then taken from <c>digestAlgorithm</c>. This helper normalises that
    /// case so the downstream dispatcher always sees a fully-specified OID.
    /// </summary>
    private static AlgorithmIdentifier NormaliseSignatureAlgorithm(
        AlgorithmIdentifier signatureAlgorithm,
        AlgorithmIdentifier digestAlgorithm,
        IPublicKey publicKey)
    {
        ObjectIdentifier alg = signatureAlgorithm.Algorithm;

        if (alg.Equals(KnownOids.RsaEncryption) && publicKey is RsaPublicKey)
        {
            ObjectIdentifier digest = digestAlgorithm.Algorithm;
            if (digest.Equals(KnownOids.Sha256)) { return new AlgorithmIdentifier(KnownOids.Sha256WithRsa, null); }
            if (digest.Equals(KnownOids.Sha384)) { return new AlgorithmIdentifier(KnownOids.Sha384WithRsa, null); }
            if (digest.Equals(KnownOids.Sha512)) { return new AlgorithmIdentifier(KnownOids.Sha512WithRsa, null); }
        }
        if (alg.Equals(KnownOids.EcPublicKey) && publicKey is EcdsaPublicKey)
        {
            ObjectIdentifier digest = digestAlgorithm.Algorithm;
            if (digest.Equals(KnownOids.Sha256)) { return new AlgorithmIdentifier(KnownOids.Sha256WithEcdsa, null); }
            if (digest.Equals(KnownOids.Sha384)) { return new AlgorithmIdentifier(KnownOids.Sha384WithEcdsa, null); }
            if (digest.Equals(KnownOids.Sha512)) { return new AlgorithmIdentifier(KnownOids.Sha512WithEcdsa, null); }
        }

        return signatureAlgorithm;
    }

    /// <summary>
    /// Extracts an <see cref="IPublicKey"/> from a certificate's SubjectPublicKeyInfo,
    /// dispatching based on the algorithm OID.
    /// </summary>
    private static IPublicKey ExtractPublicKey(X509Certificate cert)
    {
        SubjectPublicKeyInfo spki = cert.Tbs.SubjectPublicKeyInfo;
        ObjectIdentifier alg = spki.Algorithm.Algorithm;

        if (alg.Equals(KnownOids.RsaEncryption))
        {
            return RsaPublicKey.FromSubjectPublicKeyInfo(spki);
        }
        if (alg.Equals(KnownOids.EcPublicKey))
        {
            return EcdsaPublicKey.FromSubjectPublicKeyInfo(spki);
        }
        throw new NotSupportedException(
            $"Public-key algorithm {alg} in signer certificate is not supported. " +
            "Chuvadi supports RSA (rsaEncryption) and ECDSA (id-ecPublicKey).");
    }

    private static bool CertsEqual(X509Certificate a, X509Certificate b)
    {
        if (a.RawEncoding.Length != b.RawEncoding.Length) { return false; }
        for (int i = 0; i < a.RawEncoding.Length; i++)
        {
            if (a.RawEncoding[i] != b.RawEncoding[i]) { return false; }
        }
        return true;
    }

    private static bool ConstantTimeEquals(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) { return false; }
        int diff = 0;
        for (int i = 0; i < a.Length; i++)
        {
            diff |= a[i] ^ b[i];
        }
        return diff == 0;
    }

    private static TimeStampToken? TryDecodeSignatureTimestamp(SignerInfo signer)
    {
        if (signer.UnsignedAttributes is null) { return null; }
        CmsAttribute? attr = signer.UnsignedAttributes.Find(KnownOids.SignatureTimeStampToken);
        if (attr is null || attr.Values.Count == 0) { return null; }
        try
        {
            // The attribute value IS a TimeStampToken (CMS ContentInfo) per RFC 3161 Appendix A.
            return TimeStampToken.Decode(attr.Values[0]);
        }
        catch (Exception ex) when (ex is Chuvadi.Cryptography.Asn1.Asn1Exception
                                      or ArgumentException
                                      or InvalidOperationException
                                      or NotSupportedException)
        {
            return null;
        }
    }

    private static List<X509Certificate> ExtractCadesCertValues(SignerInfo signer)
    {
        List<X509Certificate> result = new();
        if (signer.UnsignedAttributes is null) { return result; }
        CmsAttribute? attr = signer.UnsignedAttributes.Find(KnownOids.CertValues);
        if (attr is null) { return result; }
        foreach (byte[] value in attr.Values)
        {
            // CertificateValues ::= SEQUENCE OF Certificate
            try
            {
                Chuvadi.Cryptography.Asn1.Asn1Reader r = new(value);
                Chuvadi.Cryptography.Asn1.Asn1Reader seq = r.ReadSequence();
                while (!seq.IsAtEnd)
                {
                    byte[] certBytes = seq.ReadEncoded();
                    try { result.Add(X509Certificate.Decode(certBytes)); }
                    catch (Chuvadi.Cryptography.Asn1.Asn1Exception) { /* skip malformed */ }
                }
            }
            catch (Chuvadi.Cryptography.Asn1.Asn1Exception) { /* malformed attr, skip */ }
        }
        return result;
    }

    private static void ExtractCadesRevocationValues(
        SignerInfo signer,
        List<CertificateList> crls,
        List<OcspResponse> ocspResponses)
    {
        _ = ocspResponses;  // accepted for future symmetry; CAdES ocspVals parsing is deferred
        if (signer.UnsignedAttributes is null) { return; }
        CmsAttribute? attr = signer.UnsignedAttributes.Find(KnownOids.RevocationValues);
        if (attr is null) { return; }
        foreach (byte[] value in attr.Values)
        {
            // RevocationValues ::= SEQUENCE {
            //   crlVals      [0] EXPLICIT SEQUENCE OF CertificateList OPTIONAL,
            //   ocspVals     [1] EXPLICIT SEQUENCE OF BasicOCSPResponse OPTIONAL,
            //   otherRevVals [2] EXPLICIT OtherRevVals OPTIONAL }
            try
            {
                Chuvadi.Cryptography.Asn1.Asn1Reader r = new(value);
                Chuvadi.Cryptography.Asn1.Asn1Reader seq = r.ReadSequence();
                while (!seq.IsAtEnd)
                {
                    Chuvadi.Cryptography.Asn1.Asn1Tag peek = seq.PeekTag();
                    if (peek == Chuvadi.Cryptography.Asn1.Asn1Tag.ContextSpecific(0, isConstructed: true))
                    {
                        Chuvadi.Cryptography.Asn1.Asn1Reader crlWrap = seq.ReadExplicit(0);
                        Chuvadi.Cryptography.Asn1.Asn1Reader crlSeq = crlWrap.ReadSequence();
                        while (!crlSeq.IsAtEnd)
                        {
                            byte[] crlBytes = crlSeq.ReadEncoded();
                            try { crls.Add(CertificateList.Decode(crlBytes)); }
                            catch (Exception ex) when (ex is Chuvadi.Cryptography.Asn1.Asn1Exception
                                                          or NotSupportedException)
                            { /* skip */ }
                        }
                    }
                    else if (peek == Chuvadi.Cryptography.Asn1.Asn1Tag.ContextSpecific(1, isConstructed: true))
                    {
                        // ocspVals — SEQUENCE OF BasicOCSPResponse.
                        // To re-use OcspResponse.Decode we need to wrap each BasicOCSPResponse as
                        // an OCSPResponse with status Successful. Skip for now — most CAdES
                        // emitters that ship OCSP also ship the wrapped OCSPResponse via other
                        // means. Future session can lift this restriction.
                        seq.Skip();
                    }
                    else
                    {
                        seq.Skip();
                    }
                }
            }
            catch (Chuvadi.Cryptography.Asn1.Asn1Exception) { /* malformed attr, skip */ }
        }
    }

    private static SignatureVerificationResult Fail(SignatureVerificationStatus status, string message)
        => new(status, message, signerCertificate: null, integrityVerified: false);
}
