// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Signature verification orchestration

using System;
using Chuvadi.Cryptography.PathValidation;
using Chuvadi.Cryptography.X509;

namespace Chuvadi.Pdf.Signatures.Verification;

/// <summary>
/// The result of verifying a PDF digital signature.
/// </summary>
public sealed class SignatureVerificationResult
{
    /// <summary>Initialises a new result.</summary>
    public SignatureVerificationResult(
        SignatureVerificationStatus status,
        string message,
        X509Certificate? signerCertificate,
        bool integrityVerified,
        bool trustValidated = false,
        CertificatePath? validatedPath = null,
        bool timestampValidated = false,
        DateTimeOffset? signatureTimestamp = null,
        X509Certificate? timestampCertificate = null,
        bool timestampTrustValidated = false,
        CertificatePath? timestampValidatedPath = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        Status = status;
        Message = message;
        SignerCertificate = signerCertificate;
        IntegrityVerified = integrityVerified;
        TrustValidated = trustValidated;
        ValidatedPath = validatedPath;
        TimestampValidated = timestampValidated;
        SignatureTimestamp = signatureTimestamp;
        TimestampCertificate = timestampCertificate;
        TimestampTrustValidated = timestampTrustValidated;
        TimestampValidatedPath = timestampValidatedPath;
    }

    /// <summary>The overall outcome.</summary>
    public SignatureVerificationStatus Status { get; }

    /// <summary>A human-readable explanation of the result.</summary>
    public string Message { get; }

    /// <summary>The signer's certificate, when located inside the CMS envelope.</summary>
    public X509Certificate? SignerCertificate { get; }

    /// <summary>
    /// True iff the cryptographic signature checks out AND the signed bytes' digest
    /// matches the messageDigest signed attribute. This is the strict cryptographic
    /// answer for the signed bytes only, regardless of whether the signer is to be
    /// believed or the file was changed after signing (see
    /// <see cref="ModificationsAfterSigning"/> and <see cref="IsValid"/>).
    /// </summary>
    public bool IntegrityVerified { get; }

    /// <summary>
    /// True iff <see cref="IntegrityVerified"/> is true AND the signer's certificate
    /// chain validates to a configured trust anchor per RFC 5280 §6.1.
    /// False when no trust store was supplied or path validation failed.
    /// </summary>
    public bool TrustValidated { get; }

    /// <summary>
    /// The certificate path that validated against the trust store, when
    /// <see cref="TrustValidated"/> is true.
    /// </summary>
    public CertificatePath? ValidatedPath { get; }

    /// <summary>
    /// True iff a signature timestamp (RFC 3161) was present in the CMS
    /// unsigned attributes, decoded cleanly, signature-verified, and its
    /// messageImprint matched the SignerInfo signature bytes.
    /// </summary>
    public bool TimestampValidated { get; }

    /// <summary>
    /// When a signature timestamp was present, the genTime claimed by the TSA.
    /// Even when <see cref="TimestampValidated"/> is false (e.g. the TST was
    /// present but did not verify), this carries the genTime declared by the
    /// token for debugging.
    /// </summary>
    public DateTimeOffset? SignatureTimestamp { get; }

    /// <summary>The TSA's signing certificate, when a timestamp was found.</summary>
    public X509Certificate? TimestampCertificate { get; }

    /// <summary>
    /// True iff the TSA's certificate chain was successfully path-validated
    /// against the supplied <c>TsaTrustStore</c> at the timestamp's genTime.
    /// Always false when no <c>TsaTrustStore</c> was supplied. Independent
    /// of <see cref="TimestampValidated"/>, which only reports the
    /// cryptographic verification.
    /// </summary>
    public bool TimestampTrustValidated { get; }

    /// <summary>
    /// The validated path for the TSA's certificate, when
    /// <see cref="TimestampTrustValidated"/> is true. Null otherwise.
    /// </summary>
    public CertificatePath? TimestampValidatedPath { get; }

    /// <summary>
    /// What the file holds after the bytes the signature covers (3.35.0):
    /// nothing, only later signatures and validation data, or other changes.
    /// Reported apart from <see cref="IntegrityVerified"/>, which checks only
    /// the signed bytes.
    /// </summary>
    public SignatureModifications ModificationsAfterSigning { get; init; } = SignatureModifications.Other;

    /// <summary>
    /// The trust finding on its own (3.35.0):
    /// <see cref="SignatureVerificationStatus.Valid"/> when the signer's
    /// certificate chains to a trust anchor,
    /// <see cref="SignatureVerificationStatus.TrustUnknown"/> when no trust
    /// store was supplied or the signature is not intact, else the trust
    /// failure (<see cref="SignatureVerificationStatus.TrustChainBroken"/>,
    /// <see cref="SignatureVerificationStatus.TrustChainCertificateRevoked"/>
    /// and so on). <see cref="Status"/> reports a change after signing ahead
    /// of trust; this keeps the trust answer visible.
    /// </summary>
    public SignatureVerificationStatus TrustStatus { get; init; } = SignatureVerificationStatus.TrustUnknown;

    /// <summary>
    /// True when the signature's byte range covers the whole file (trailing
    /// white space aside), so nothing can have changed after signing (3.35.0).
    /// </summary>
    public bool CoversWholeDocument { get; init; }

    /// <summary>
    /// True when <see cref="Status"/> is <see cref="SignatureVerificationStatus.Valid"/>:
    /// the signature is intact, the file was not changed after signing beyond
    /// later signatures or validation data, and the signer's certificate
    /// chains to a trust anchor (3.35.0; before, an intact signature was valid
    /// without a trust store and after later changes).
    /// </summary>
    public bool IsValid => Status == SignatureVerificationStatus.Valid;
}
