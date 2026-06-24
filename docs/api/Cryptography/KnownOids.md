# KnownOids

**Class** in `Chuvadi.Cryptography.Oids` (Cryptography)

Named ObjectIdentifier constants for the OIDs Chuvadi cares about.

```csharp
public static class KnownOids
```

## Methods

### `new`

__static__

```csharp
static readonly ObjectIdentifier Sha1 = new("1.3.14.3.2.26")
```

SHA-1. RFC 8017.

### `new`

__static__

```csharp
static readonly ObjectIdentifier Sha224 = new("2.16.840.1.101.3.4.2.4")
```

SHA-224. NIST hash family OID arc.

### `new`

__static__

```csharp
static readonly ObjectIdentifier Sha256 = new("2.16.840.1.101.3.4.2.1")
```

SHA-256.

### `new`

__static__

```csharp
static readonly ObjectIdentifier Sha384 = new("2.16.840.1.101.3.4.2.2")
```

SHA-384.

### `new`

__static__

```csharp
static readonly ObjectIdentifier Sha512 = new("2.16.840.1.101.3.4.2.3")
```

SHA-512.

### `new`

__static__

```csharp
static readonly ObjectIdentifier Sha3_256 = new("2.16.840.1.101.3.4.2.8")
```

SHA-3 256.

### `new`

__static__

```csharp
static readonly ObjectIdentifier Sha3_384 = new("2.16.840.1.101.3.4.2.9")
```

SHA-3 384.

### `new`

__static__

```csharp
static readonly ObjectIdentifier Sha3_512 = new("2.16.840.1.101.3.4.2.10")
```

SHA-3 512.

### `new`

__static__

```csharp
static readonly ObjectIdentifier RsaEncryption = new("1.2.840.113549.1.1.1")
```

RSA encryption (PKCS#1 v1.5 signing uses this AlgorithmIdentifier for the key).

### `new`

__static__

```csharp
static readonly ObjectIdentifier RsaSsaPss = new("1.2.840.113549.1.1.10")
```

RSASSA-PSS. RFC 8017.

### `new`

__static__

```csharp
static readonly ObjectIdentifier Mgf1 = new("1.2.840.113549.1.1.8")
```

id-mgf1 mask generation function. RFC 4055 §2.1.

### `new`

__static__

```csharp
static readonly ObjectIdentifier EcPublicKey = new("1.2.840.10045.2.1")
```

ECDSA with public key. RFC 5480.

### `new`

__static__

```csharp
static readonly ObjectIdentifier Ed25519 = new("1.3.101.112")
```

Ed25519 (EdDSA). RFC 8032.

### `new`

__static__

```csharp
static readonly ObjectIdentifier Ed448 = new("1.3.101.113")
```

Ed448 (EdDSA). RFC 8032.

### `new`

__static__

```csharp
static readonly ObjectIdentifier Secp256r1 = new("1.2.840.10045.3.1.7")
```

NIST P-256 / secp256r1 / prime256v1.

### `new`

__static__

```csharp
static readonly ObjectIdentifier Secp384r1 = new("1.3.132.0.34")
```

NIST P-384 / secp384r1.

### `new`

__static__

```csharp
static readonly ObjectIdentifier Secp521r1 = new("1.3.132.0.35")
```

NIST P-521 / secp521r1.

### `new`

__static__

```csharp
static readonly ObjectIdentifier Secp256k1 = new("1.3.132.0.10")
```

secp256k1 (Bitcoin curve, occasionally seen).

### `new`

__static__

```csharp
static readonly ObjectIdentifier BrainpoolP256r1 = new("1.3.36.3.3.2.8.1.1.7")
```

brainpoolP256r1. RFC 5639.

### `new`

__static__

```csharp
static readonly ObjectIdentifier BrainpoolP384r1 = new("1.3.36.3.3.2.8.1.1.11")
```

brainpoolP384r1.

### `new`

__static__

```csharp
static readonly ObjectIdentifier BrainpoolP512r1 = new("1.3.36.3.3.2.8.1.1.13")
```

brainpoolP512r1.

### `new`

__static__

```csharp
static readonly ObjectIdentifier Sha1WithRsa = new("1.2.840.113549.1.1.5")
```

SHA-1 with RSA. RFC 8017. Deprecated for new signatures.

### `new`

__static__

```csharp
static readonly ObjectIdentifier Sha256WithRsa = new("1.2.840.113549.1.1.11")
```

SHA-256 with RSA.

### `new`

__static__

```csharp
static readonly ObjectIdentifier Sha384WithRsa = new("1.2.840.113549.1.1.12")
```

SHA-384 with RSA.

### `new`

__static__

```csharp
static readonly ObjectIdentifier Sha512WithRsa = new("1.2.840.113549.1.1.13")
```

SHA-512 with RSA.

### `new`

__static__

```csharp
static readonly ObjectIdentifier Sha1WithEcdsa = new("1.2.840.10045.4.1")
```

SHA-1 with ECDSA. Deprecated.

### `new`

__static__

```csharp
static readonly ObjectIdentifier Sha256WithEcdsa = new("1.2.840.10045.4.3.2")
```

SHA-256 with ECDSA.

### `new`

__static__

```csharp
static readonly ObjectIdentifier Sha384WithEcdsa = new("1.2.840.10045.4.3.3")
```

SHA-384 with ECDSA.

### `new`

__static__

```csharp
static readonly ObjectIdentifier Sha512WithEcdsa = new("1.2.840.10045.4.3.4")
```

SHA-512 with ECDSA.

### `new`

__static__

```csharp
static readonly ObjectIdentifier CommonName = new("2.5.4.3")
```

CN — common name. 2.5.4.3

### `new`

__static__

```csharp
static readonly ObjectIdentifier Surname = new("2.5.4.4")
```

SN — surname.

### `new`

__static__

```csharp
static readonly ObjectIdentifier SerialNumber = new("2.5.4.5")
```

serialNumber attribute (distinct from certificate serial number).

### `new`

__static__

```csharp
static readonly ObjectIdentifier CountryName = new("2.5.4.6")
```

C — country.

### `new`

__static__

```csharp
static readonly ObjectIdentifier LocalityName = new("2.5.4.7")
```

L — locality.

### `new`

__static__

```csharp
static readonly ObjectIdentifier StateOrProvinceName = new("2.5.4.8")
```

ST — state or province.

### `new`

__static__

```csharp
static readonly ObjectIdentifier OrganizationName = new("2.5.4.10")
```

O — organisation.

### `new`

__static__

```csharp
static readonly ObjectIdentifier OrganizationalUnitName = new("2.5.4.11")
```

OU — organisational unit.

### `new`

__static__

```csharp
static readonly ObjectIdentifier Title = new("2.5.4.12")
```

title.

### `new`

__static__

```csharp
static readonly ObjectIdentifier GivenName = new("2.5.4.42")
```

givenName.

### `new`

__static__

```csharp
static readonly ObjectIdentifier Initials = new("2.5.4.43")
```

initials.

### `new`

__static__

```csharp
static readonly ObjectIdentifier Pseudonym = new("2.5.4.65")
```

pseudonym.

### `new`

__static__

```csharp
static readonly ObjectIdentifier EmailAddress = new("1.2.840.113549.1.9.1")
```

emailAddress (legacy, deprecated by RFC 5280 but still common).

### `new`

__static__

```csharp
static readonly ObjectIdentifier DomainComponent = new("0.9.2342.19200300.100.1.25")
```

domainComponent.

### `new`

__static__

```csharp
static readonly ObjectIdentifier SubjectDirectoryAttributes = new("2.5.29.9")
```

subjectDirectoryAttributes.

### `new`

__static__

```csharp
static readonly ObjectIdentifier SubjectKeyIdentifier = new("2.5.29.14")
```

subjectKeyIdentifier.

### `new`

__static__

```csharp
static readonly ObjectIdentifier KeyUsage = new("2.5.29.15")
```

keyUsage.

### `new`

__static__

```csharp
static readonly ObjectIdentifier SubjectAltName = new("2.5.29.17")
```

subjectAltName.

### `new`

__static__

```csharp
static readonly ObjectIdentifier IssuerAltName = new("2.5.29.18")
```

issuerAltName.

### `new`

__static__

```csharp
static readonly ObjectIdentifier BasicConstraints = new("2.5.29.19")
```

basicConstraints.

### `new`

__static__

```csharp
static readonly ObjectIdentifier NameConstraints = new("2.5.29.30")
```

nameConstraints.

### `new`

__static__

```csharp
static readonly ObjectIdentifier CrlDistributionPoints = new("2.5.29.31")
```

cRLDistributionPoints.

### `new`

__static__

```csharp
static readonly ObjectIdentifier CrlNumber = new("2.5.29.20")
```

cRLNumber — monotonically increasing sequence number on a CRL. RFC 5280 §5.2.3.

### `new`

__static__

```csharp
static readonly ObjectIdentifier CrlReasonCode = new("2.5.29.21")
```

cRLReason — per-entry revocation reason code. RFC 5280 §5.3.1.

### `new`

__static__

```csharp
static readonly ObjectIdentifier InvalidityDate = new("2.5.29.24")
```

invalidityDate — per-entry invalidity time. RFC 5280 §5.3.2.

### `new`

__static__

```csharp
static readonly ObjectIdentifier DeltaCrlIndicator = new("2.5.29.27")
```

deltaCRLIndicator — marks a delta CRL. RFC 5280 §5.2.4. Chuvadi rejects delta CRLs.

### `new`

__static__

```csharp
static readonly ObjectIdentifier IssuingDistributionPoint = new("2.5.29.28")
```

issuingDistributionPoint — CRL scope marker. RFC 5280 §5.2.5.

### `new`

__static__

```csharp
static readonly ObjectIdentifier CertificatePolicies = new("2.5.29.32")
```

certificatePolicies.

### `new`

__static__

```csharp
static readonly ObjectIdentifier PolicyMappings = new("2.5.29.33")
```

policyMappings.

### `new`

__static__

```csharp
static readonly ObjectIdentifier AuthorityKeyIdentifier = new("2.5.29.35")
```

authorityKeyIdentifier.

### `new`

__static__

```csharp
static readonly ObjectIdentifier PolicyConstraints = new("2.5.29.36")
```

policyConstraints.

### `new`

__static__

```csharp
static readonly ObjectIdentifier ExtKeyUsage = new("2.5.29.37")
```

extKeyUsage.

### `new`

__static__

```csharp
static readonly ObjectIdentifier FreshestCrl = new("2.5.29.46")
```

freshestCRL.

### `new`

__static__

```csharp
static readonly ObjectIdentifier InhibitAnyPolicy = new("2.5.29.54")
```

inhibitAnyPolicy.

### `new`

__static__

```csharp
static readonly ObjectIdentifier AuthorityInfoAccess = new("1.3.6.1.5.5.7.1.1")
```

authorityInfoAccess (RFC 5280 §4.2.2.1).

### `new`

__static__

```csharp
static readonly ObjectIdentifier ServerAuth = new("1.3.6.1.5.5.7.3.1")
```

id-kp-serverAuth.

### `new`

__static__

```csharp
static readonly ObjectIdentifier ClientAuth = new("1.3.6.1.5.5.7.3.2")
```

id-kp-clientAuth.

### `new`

__static__

```csharp
static readonly ObjectIdentifier CodeSigning = new("1.3.6.1.5.5.7.3.3")
```

id-kp-codeSigning.

### `new`

__static__

```csharp
static readonly ObjectIdentifier EmailProtection = new("1.3.6.1.5.5.7.3.4")
```

id-kp-emailProtection.

### `new`

__static__

```csharp
static readonly ObjectIdentifier TimeStamping = new("1.3.6.1.5.5.7.3.8")
```

id-kp-timeStamping.

### `new`

__static__

```csharp
static readonly ObjectIdentifier OcspSigning = new("1.3.6.1.5.5.7.3.9")
```

id-kp-OCSPSigning.

### `new`

__static__

```csharp
static readonly ObjectIdentifier DocumentSigning = new("1.3.6.1.5.5.7.3.36")
```

id-kp-documentSigning. RFC 9336.

### `new`

__static__

```csharp
static readonly ObjectIdentifier CaIssuers = new("1.3.6.1.5.5.7.48.2")
```

id-ad-caIssuers.

### `new`

__static__

```csharp
static readonly ObjectIdentifier OcspAccess = new("1.3.6.1.5.5.7.48.1")
```

id-ad-ocsp.

### `new`

__static__

```csharp
static readonly ObjectIdentifier CmsData = new("1.2.840.113549.1.7.1")
```

id-data — CMS Data content type.

### `new`

__static__

```csharp
static readonly ObjectIdentifier CmsSignedData = new("1.2.840.113549.1.7.2")
```

id-signedData — CMS SignedData content type.

### `new`

__static__

```csharp
static readonly ObjectIdentifier CmsEnvelopedData = new("1.2.840.113549.1.7.3")
```

id-envelopedData — CMS EnvelopedData content type.

### `new`

__static__

```csharp
static readonly ObjectIdentifier CmsDigestedData = new("1.2.840.113549.1.7.5")
```

id-digestedData — CMS DigestedData content type.

### `new`

__static__

```csharp
static readonly ObjectIdentifier CmsEncryptedData = new("1.2.840.113549.1.7.6")
```

id-encryptedData — CMS EncryptedData content type.

### `new`

__static__

```csharp
static readonly ObjectIdentifier ContentType = new("1.2.840.113549.1.9.3")
```

id-contentType.

### `new`

__static__

```csharp
static readonly ObjectIdentifier MessageDigest = new("1.2.840.113549.1.9.4")
```

id-messageDigest.

### `new`

__static__

```csharp
static readonly ObjectIdentifier SigningTime = new("1.2.840.113549.1.9.5")
```

id-signingTime.

### `new`

__static__

```csharp
static readonly ObjectIdentifier CounterSignature = new("1.2.840.113549.1.9.6")
```

id-countersignature.

### `new`

__static__

```csharp
static readonly ObjectIdentifier SigningCertificate = new("1.2.840.113549.1.9.16.2.12")
```

id-aa-signingCertificate (ESS, RFC 2634).

### `new`

__static__

```csharp
static readonly ObjectIdentifier SigningCertificateV2 = new("1.2.840.113549.1.9.16.2.47")
```

id-aa-signingCertificateV2 (ESS, RFC 5035).

### `new`

__static__

```csharp
static readonly ObjectIdentifier SignatureTimeStampToken = new("1.2.840.113549.1.9.16.2.14")
```

id-aa-signatureTimeStampToken — RFC 3161 timestamp embedded as unsigned attribute.

### `new`

__static__

```csharp
static readonly ObjectIdentifier TstInfo = new("1.2.840.113549.1.9.16.1.4")
```

id-ct-TSTInfo — TSA response content type.

### `new`

__static__

```csharp
static readonly ObjectIdentifier OcspBasicResponse = new("1.3.6.1.5.5.7.48.1.1")
```

id-pkix-ocsp-basic — OCSP BasicResponse.

### `new`

__static__

```csharp
static readonly ObjectIdentifier OcspNonce = new("1.3.6.1.5.5.7.48.1.2")
```

id-pkix-ocsp-nonce.

### `new`

__static__

```csharp
static readonly ObjectIdentifier OcspNoCheck = new("1.3.6.1.5.5.7.48.1.5")
```

id-pkix-ocsp-nocheck — certificate skipping OCSP for its own OCSP-signing cert.

### `new`

__static__

```csharp
static readonly ObjectIdentifier ContentHint = new("1.2.840.113549.1.9.16.2.4")
```

id-aa-encryp-attribute-OID (rarely seen but defined).

### `new`

__static__

```csharp
static readonly ObjectIdentifier ArchiveTimestampV3 = new("0.4.0.1733.2.4")
```

id-aa-ets-archiveTimestampV3 — CAdES-B-LTA archive timestamp.

### `new`

__static__

```csharp
static readonly ObjectIdentifier CertValues = new("1.2.840.113549.1.9.16.2.23")
```

id-aa-ets-certValues — CAdES embedded certificates for LTV.

### `new`

__static__

```csharp
static readonly ObjectIdentifier RevocationValues = new("1.2.840.113549.1.9.16.2.24")
```

id-aa-ets-revocationValues — CAdES embedded revocation info.

### `new`

__static__

```csharp
static readonly ObjectIdentifier CompleteCertificateRefs = new("1.2.840.113549.1.9.16.2.21")
```

id-aa-ets-certificateRefs — CAdES references to certs.

### `new`

__static__

```csharp
static readonly ObjectIdentifier CompleteRevocationRefs = new("1.2.840.113549.1.9.16.2.22")
```

id-aa-ets-revocationRefs — CAdES references to revocation info.

## Fields

### `AdbePkcs7Detached`

```csharp
const string AdbePkcs7Detached = "adbe.pkcs7.detached"
```

adbe.pkcs7.detached — most common PDF signature SubFilter.

### `AdbePkcs7Sha1`

```csharp
const string AdbePkcs7Sha1 = "adbe.pkcs7.sha1"
```

adbe.pkcs7.sha1 — legacy PDF signature SubFilter.

### `AdbeX509RsaSha1`

```csharp
const string AdbeX509RsaSha1 = "adbe.x509.rsa_sha1"
```

adbe.x509.rsa_sha1 — legacy PDF signature SubFilter, deprecated.

### `EtsiCAdESDetached`

```csharp
const string EtsiCAdESDetached = "ETSI.CAdES.detached"
```

ETSI.CAdES.detached — CAdES-based PDF signature SubFilter (eIDAS).

### `EtsiRfc3161`

```csharp
const string EtsiRfc3161 = "ETSI.RFC3161"
```

ETSI.RFC3161 — PDF document timestamp SubFilter.

---

_Source: [`src/Chuvadi.Cryptography/Oids/KnownOids.cs`](../../../src/Chuvadi.Cryptography/Oids/KnownOids.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
