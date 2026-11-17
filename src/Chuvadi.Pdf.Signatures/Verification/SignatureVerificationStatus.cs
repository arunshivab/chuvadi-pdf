// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Signature verification orchestration

namespace Chuvadi.Pdf.Signatures.Verification;

/// <summary>
/// The overall outcome of verifying a PDF signature.
/// </summary>
public enum SignatureVerificationStatus
{
    /// <summary>
    /// The signature is cryptographically valid, nothing but later signatures
    /// or validation data was added after signing, and the signer's
    /// certificate chain validates to an anchor in the trust store. Since
    /// 3.35.0 a signature is never valid without a trust store (see
    /// <see cref="TrustUnknown"/>) or after other changes (see
    /// <see cref="ModifiedAfterSigning"/>).
    /// </summary>
    Valid = 0,

    /// <summary>The cryptographic signature does not match.</summary>
    Invalid = 1,

    /// <summary>The signature's message digest does not match the hash of the signed bytes.</summary>
    DigestMismatch = 2,

    /// <summary>The signer certificate could not be located inside the CMS envelope.</summary>
    SignerCertificateNotFound = 3,

    /// <summary>The /SubFilter is not CMS-based; Chuvadi does not know how to verify it.</summary>
    UnsupportedSubFilter = 4,

    /// <summary>The signature uses an algorithm Chuvadi does not implement.</summary>
    UnsupportedAlgorithm = 5,

    /// <summary>The signature container could not be parsed.</summary>
    MalformedSignature = 6,

    /// <summary>
    /// The signature is cryptographically valid, but the signer's certificate
    /// does not chain to any trust anchor in the supplied trust store.
    /// </summary>
    TrustChainBroken = 7,

    /// <summary>
    /// The signature is cryptographically valid, but a certificate in the
    /// chain has expired or is not yet valid at the validation time.
    /// </summary>
    TrustChainCertificateOutOfValidity = 8,

    /// <summary>
    /// The signature is cryptographically valid, but the signer's certificate
    /// chain failed RFC 5280 §6.1 path validation for a reason other than
    /// validity-period violation.
    /// </summary>
    TrustChainInvalid = 9,

    /// <summary>
    /// The signature is cryptographically valid and chains to a trust anchor,
    /// but a certificate in the chain is on a Certificate Revocation List.
    /// </summary>
    TrustChainCertificateRevoked = 10,

    /// <summary>
    /// The signature is intact and the file was not changed after signing,
    /// but no trust store was supplied, so who signed it is not verified
    /// (3.35.0). Supply <see cref="SignatureVerifyOptions.TrustStore"/> to
    /// evaluate the signer's certificate chain.
    /// </summary>
    TrustUnknown = 11,

    /// <summary>
    /// The signed revision is intact, but the file was changed after signing
    /// in a way other than adding later signatures or validation data, or the
    /// signature's byte range leaves part of the file unsigned (3.35.0). What
    /// a reader sees is not what was signed; see
    /// <see cref="SignatureVerificationResult.ModificationsAfterSigning"/>.
    /// </summary>
    ModifiedAfterSigning = 12,
}
