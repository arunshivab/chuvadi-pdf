# API Reference

Auto-generated from XML doc comments. One file per public type, grouped by module.

Regenerate with:

```bash
python tools/gen_api_docs.py
```

## Chuvadi.Pdf.Annotations

| Type | Kind | Description |
|---|---|---|
| [AnnotationException](Annotations/AnnotationException.md) | class | Thrown when an annotation operation fails. |
| [AnnotationReader](Annotations/AnnotationReader.md) | class | Reads annotations from a PDF document. |
| [AnnotationType](Annotations/AnnotationType.md) | enum | PDF annotation subtype. |
| [AnnotationWriter](Annotations/AnnotationWriter.md) | class | Adds new annotations to a PDF document and writes the result. |
| [BorderStyle](Annotations/BorderStyle.md) | class | Border style for an annotation, describing width, style, and (for dashed borders) dash pattern. |
| [BorderStyleType](Annotations/BorderStyleType.md) | enum | PDF border-style kind. |
| [CircleAnnotation](Annotations/CircleAnnotation.md) | class | Circle (ellipse outline) annotation. |
| [FreeTextAnnotation](Annotations/FreeTextAnnotation.md) | class | Free-text annotation drawn directly on the page (§12.5.6.6). |
| [GenericAnnotation](Annotations/GenericAnnotation.md) | class | Catch-all annotation for subtypes not specifically modelled. |
| [InkAnnotation](Annotations/InkAnnotation.md) | class | Free-hand ink annotation (§12.5.6.13). |
| [LineAnnotation](Annotations/LineAnnotation.md) | class | Line annotation. |
| [LineEnding](Annotations/LineEnding.md) | enum | Line ending style for Line and PolyLine annotations. |
| [LinkAnnotation](Annotations/LinkAnnotation.md) | class | Hyperlink annotation (§12.5.6.5). |
| [MarkupAnnotation](Annotations/MarkupAnnotation.md) | class | Text-markup annotation (§12.5.6.10): Highlight, Underline, Squiggly, or StrikeOut. |
| [PdfAnnotation](Annotations/PdfAnnotation.md) | class | Base class for all modelled annotations. |
| [PolyLineAnnotation](Annotations/PolyLineAnnotation.md) | class | PolyLine annotation: an open shape connecting `Vertices`. |
| [PolygonAnnotation](Annotations/PolygonAnnotation.md) | class | Polygon annotation: a closed shape connecting `Vertices`. |
| [SquareAnnotation](Annotations/SquareAnnotation.md) | class | Square (rectangle outline) annotation. |
| [StampAnnotation](Annotations/StampAnnotation.md) | class | Rubber-stamp annotation (§12.5.6.12), e.g., "Approved", "Confidential". |
| [TextAnnotation](Annotations/TextAnnotation.md) | class | Sticky-note text annotation (§12.5.6.4). |

## Chuvadi.Pdf.Authoring

| Type | Kind | Description |
|---|---|---|
| [BorderStyle](Authoring/BorderStyle.md) | enum | Border style for tables and rectangles. |
| [CellOverflow](Authoring/CellOverflow.md) | enum | How cell text that exceeds the cell width is handled. |
| [Color](Authoring/Color.md) | record struct | An RGB color in [0, 1] floating-point space. |
| [Colors](Authoring/Colors.md) | class | Common named colors. |
| [ColumnWidthMode](Authoring/ColumnWidthMode.md) | enum | How a column's width is specified. |
| [EmbeddedFontObjects](Authoring/EmbeddedFontObjects.md) | class | The result of embedding a TrueType font: the top-level Type0 font object id to reference from a page's `/Font` resource, and every object that must be added to the document. |
| [HeaderFooterStyle](Authoring/HeaderFooterStyle.md) | class | Header / footer band styling. |
| [ImagePageSizing](Authoring/ImagePageSizing.md) | enum | How `ImagePdfConverter` sizes each PDF page relative to its image. |
| [ImagePdfConverter](Authoring/ImagePdfConverter.md) | class | Converts images (JPEG, PNG, TIFF, BMP) into PDF documents — one page per image (and, optionally, one page per TIFF frame). |
| [ImagePdfOptions](Authoring/ImagePdfOptions.md) | class | Options for `ImagePdfConverter`. |
| [ListStyle](Authoring/ListStyle.md) | class | List styling for bulleted and numbered lists. |
| [NumberingFormat](Authoring/NumberingFormat.md) | enum | The numbering scheme of an ordered list or a page number. |
| [PageBuilder](Authoring/PageBuilder.md) | class | Per-page drawing API. |
| [PageNumberFormatter](Authoring/PageNumberFormatter.md) | class | Formats integers in the report numbering schemes. |
| [PageSize](Authoring/PageSize.md) | record struct | A page size in PDF points (1 pt = 1/72 inch). |
| [ParagraphStyle](Authoring/ParagraphStyle.md) | class | Paragraph styling: font, size, colour, alignment, spacing, and indents. |
| [PdfDocumentBuilder](Authoring/PdfDocumentBuilder.md) | class | Top-level entry point for creating fresh PDF documents. |
| [ReportBuilder](Authoring/ReportBuilder.md) | class | Composes multi-page PDF reports from flowing content blocks — headings, paragraphs, bulleted and numbered lists, tables, images, rules, and page breaks — with automatic pagination. |
| [ReportCell](Authoring/ReportCell.md) | class | One table cell: text or an image, an optional column/row span, and optional per-cell style overrides. |
| [ReportColumn](Authoring/ReportColumn.md) | class | A table column: optional header text, width, and default cell styling. |
| [ReportFont](Authoring/ReportFont.md) | class | A report font: a Standard-14 family plus bold/italic flags, resolved to the matching Standard-14 PostScript name at draw time. |
| [ReportFontFamily](Authoring/ReportFontFamily.md) | enum | The Standard-14 font families available to report content. |
| [ReportPageSetup](Authoring/ReportPageSetup.md) | class | Page geometry for a report: paper size and the four margins. |
| [ReportRow](Authoring/ReportRow.md) | class | One table row: its cells plus optional height and background overrides. |
| [ReportTable](Authoring/ReportTable.md) | class | A report table: columns, rows, and a style. |
| [StandardFonts](Authoring/StandardFonts.md) | class | The PDF Standard 14 fonts. |
| [TableBorderMode](Authoring/TableBorderMode.md) | enum | Which grid lines a table draws. |
| [TableBuilder](Authoring/TableBuilder.md) | class | Fluent table builder. |
| [TableRenderResult](Authoring/TableRenderResult.md) | class | Outcome of rendering a table; may contain overflow rows. |
| [TableStyle](Authoring/TableStyle.md) | class | Table-level styling: fonts, borders, padding, header and fills. |
| [TextAlignment](Authoring/TextAlignment.md) | enum | Text alignment within a block or table cell. |
| [TextBlockResult](Authoring/TextBlockResult.md) | class | Result of a `PageBuilder.DrawTextBlock` call. |
| [TrueTypeFontEmbedder](Authoring/TrueTypeFontEmbedder.md) | class | Builds the PDF object graph that embeds a TrueType (`glyf`) font as a composite Type0 font with Identity-H encoding, so authored content can draw text in a custom font. |
| [VerticalAlignment](Authoring/VerticalAlignment.md) | enum | Vertical alignment within a table cell. |

## Chuvadi.Pdf.Color

| Type | Kind | Description |
|---|---|---|
| [ColorConversion](Color/ColorConversion.md) | class | Static color-conversion helpers. |
| [IccColorSpace](Color/IccColorSpace.md) | enum | The source color space declared by an ICC profile. |
| [IccException](Color/IccException.md) | class | Thrown when an ICC profile is malformed or unsupported. |
| [IccProfile](Color/IccProfile.md) | class | Parsed ICC color profile. |

## Chuvadi.Pdf.Content

| Type | Kind | Description |
|---|---|---|
| [ContentException](Content/ContentException.md) | class | Thrown when a PDF content stream contains invalid or unsupported operators. |
| [ContentStreamParser](Content/ContentStreamParser.md) | class | Parses a PDF content stream and extracts text fragments with their approximate positions. |
| [GraphicsState](Content/GraphicsState.md) | class | Represents the graphics and text state at a point in content stream processing. |
| [Matrix3x3](Content/Matrix3x3.md) | struct | A 3x3 matrix used for 2D affine transformations in PDF user space. |
| [PdfFunction](Content/PdfFunction.md) | class | A PDF function (PDF 32000-1:2008 §7.10): a mapping from an `InputCount`-dimensional input to an `OutputCount`-dimensional output. |
| [PdfShading](Content/PdfShading.md) | class | An axial (Type 2) or radial (Type 3) shading (PDF 32000-1:2008 §8.7.4.5). |
| [ResolvedColorSpace](Content/ResolvedColorSpace.md) | class | A resolved PDF colour space that converts its colour components to sRGB. |
| [TextFragment](Content/TextFragment.md) | class | A piece of text extracted from a PDF content stream, together with its approximate position in user space. |

## Chuvadi.Cryptography

| Type | Kind | Description |
|---|---|---|
| [AccessDescription](Cryptography/AccessDescription.md) | class | One access description inside an AuthorityInfoAccess extension. |
| [AlgorithmIdentifier](Cryptography/AlgorithmIdentifier.md) | class | An ASN.1 AlgorithmIdentifier as defined by RFC 5280 §4.1.1.2. |
| [Asn1BitString](Cryptography/Asn1BitString.md) | class | Encode and decode ASN.1 BIT STRING values. |
| [Asn1Boolean](Cryptography/Asn1Boolean.md) | class | Encode and decode ASN.1 BOOLEAN values. |
| [Asn1Exception](Cryptography/Asn1Exception.md) | class | Raised when an ASN.1 decoder encounters malformed or non-conforming input. |
| [Asn1Integer](Cryptography/Asn1Integer.md) | class | Encode and decode ASN.1 INTEGER values. |
| [Asn1Null](Cryptography/Asn1Null.md) | class | Encode and decode ASN.1 NULL values. |
| [Asn1ObjectIdentifier](Cryptography/Asn1ObjectIdentifier.md) | class | Encode and decode ASN.1 OBJECT IDENTIFIER values. |
| [Asn1OctetString](Cryptography/Asn1OctetString.md) | class | Encode and decode ASN.1 OCTET STRING values. |
| [Asn1Reader](Cryptography/Asn1Reader.md) | class | Pull-style reader for nested ASN.1 BER/DER structures. |
| [Asn1String](Cryptography/Asn1String.md) | class | Encode and decode ASN.1 character string types. |
| [Asn1Tag](Cryptography/Asn1Tag.md) | struct | Immutable description of an ASN.1 tag. |
| [Asn1TagClass](Cryptography/Asn1TagClass.md) | enum | ASN.1 tag class. |
| [Asn1TagLength](Cryptography/Asn1TagLength.md) | class | Stateless low-level codec for ASN.1 BER/DER tag and length prefixes. |
| [Asn1Time](Cryptography/Asn1Time.md) | class | Encode and decode ASN.1 UTCTime and GeneralizedTime values. |
| [Asn1UniversalTag](Cryptography/Asn1UniversalTag.md) | enum | Universal-class ASN.1 tag numbers as assigned by ITU-T X.680. |
| [Asn1Writer](Cryptography/Asn1Writer.md) | class | Build-style writer for nested ASN.1 DER structures. |
| [AttributeTypeAndValue](Cryptography/AttributeTypeAndValue.md) | class | One attribute within a RelativeDistinguishedName — an OID identifying the attribute type plus its value. |
| [AuthorityInformationAccessExtension](Cryptography/AuthorityInformationAccessExtension.md) | class | The Authority Information Access extension — pointers to additional resources about the certificate's issuer (typically caIssuers and OCSP). |
| [AuthorityKeyIdentifierExtension](Cryptography/AuthorityKeyIdentifierExtension.md) | class | The Authority Key Identifier extension — identifies the public key whose holder signed this certificate. |
| [BasicConstraintsExtension](Cryptography/BasicConstraintsExtension.md) | class | The Basic Constraints extension — identifies CA certificates and bounds the depth of the chain they may issue. |
| [BasicOcspResponse](Cryptography/BasicOcspResponse.md) | class | A parsed BasicOCSPResponse — the typical OCSP response payload. |
| [BitStringValue](Cryptography/BitStringValue.md) | class | A decoded ASN.1 BIT STRING — an octet sequence plus a count of unused trailing bits in the final octet. |
| [CertId](Cryptography/CertId.md) | class | Identifies a certificate inside an OCSP response. |
| [CertStatus](Cryptography/CertStatus.md) | class | The OCSP responder's verdict on one certificate. |
| [CertStatusKind](Cryptography/CertStatusKind.md) | enum | The three possible `CertStatus.Kind` values. |
| [CertificateList](Cryptography/CertificateList.md) | class | A parsed X.509 Certificate Revocation List (CRL). |
| [CertificateListSignatureVerifier](Cryptography/CertificateListSignatureVerifier.md) | class | Verifies the signature on a `CertificateList` against the issuing CA's public key. |
| [CertificatePath](Cryptography/CertificatePath.md) | class | A certificate path: an ordered sequence from the end-entity (leaf) to a trust anchor, plus the matching anchor. |
| [CertificatePathBuilder](Cryptography/CertificatePathBuilder.md) | class | Builds candidate certificate paths from a leaf certificate to a trust anchor. |
| [CertificatePathValidationResult](Cryptography/CertificatePathValidationResult.md) | class | The result of running the path-validation algorithm against one or more candidate paths. |
| [CertificatePathValidationStatus](Cryptography/CertificatePathValidationStatus.md) | enum | The outcome of validating a single certificate path. |
| [CertificatePathValidator](Cryptography/CertificatePathValidator.md) | class | Validates X.509 certificate paths against a trust store, per RFC 5280 §6.1. |
| [CmsAttribute](Cryptography/CmsAttribute.md) | class | A generic CMS Attribute — an OID identifying the attribute type, plus a SET of one or more values whose content is defined per OID. |
| [CmsAttributeTable](Cryptography/CmsAttributeTable.md) | class | A collection of `CmsAttribute` values, with OID lookup and the raw encoded bytes preserved for signature verification. |
| [CmsDecoder](Cryptography/CmsDecoder.md) | class | Decodes CMS / PKCS#7 byte streams into structured Chuvadi objects. |
| [CmsSignedDataBuilder](Cryptography/CmsSignedDataBuilder.md) | class | Builds a CMS SignedData (RFC 5652 §5) wrapped in a ContentInfo, ready for embedding in a PDF signature dictionary's `/Contents`. |
| [ContentInfo](Cryptography/ContentInfo.md) | class | The outermost CMS structure — a tagged container that says "the following bytes are of contentType X." |
| [CrlDistributionPointsExtension](Cryptography/CrlDistributionPointsExtension.md) | class | The CRL Distribution Points extension — locations from which the issuer's Certificate Revocation List may be retrieved. |
| [CrlReason](Cryptography/CrlReason.md) | enum | The reason a certificate was revoked, as encoded in the per-entry `reasonCode` CRL extension (OID 2.5.29.21). |
| [DistributionPoint](Cryptography/DistributionPoint.md) | class | One distribution point inside a CRLDistributionPoints extension. |
| [EcCurve](Cryptography/EcCurve.md) | class | A named elliptic curve over a prime field — the parameters needed to perform ECDSA verification. |
| [EcPoint](Cryptography/EcPoint.md) | class | A point on a short Weierstrass elliptic curve in affine coordinates. |
| [EcdsaCmsSigner](Cryptography/EcdsaCmsSigner.md) | class | An `ISigner` backed by Chuvadi's hand-rolled ECDSA primitive (`Chuvadi.Cryptography.PublicKey.EcdsaSigner`). |
| [EcdsaPrivateKey](Cryptography/EcdsaPrivateKey.md) | class | An ECDSA private key — a scalar d in [1, n-1] on a fixed curve. |
| [EcdsaPublicKey](Cryptography/EcdsaPublicKey.md) | class | An ECDSA public key — a point on a named curve. |
| [EcdsaSigner](Cryptography/EcdsaSigner.md) | class | Hand-rolled ECDSA signing per FIPS 186-4 §6.4. |
| [EcdsaVerifier](Cryptography/EcdsaVerifier.md) | class | Verifies ECDSA signatures per FIPS 186-4 §6.4. |
| [EncapsulatedContentInfo](Cryptography/EncapsulatedContentInfo.md) | class | The content being signed (attached) or referenced (detached) by a SignedData. |
| [ExtendedKeyUsageExtension](Cryptography/ExtendedKeyUsageExtension.md) | class | The Extended Key Usage extension — additional or alternative purposes for which the certified public key may be used. |
| [GeneralName](Cryptography/GeneralName.md) | class | One alternative naming form for a certificate subject or other entity. |
| [GeneralNameKind](Cryptography/GeneralNameKind.md) | enum | The variant types within a GeneralName CHOICE. |
| [HashAlgorithmName](Cryptography/HashAlgorithmName.md) | enum | Enumeration of the hash algorithms Chuvadi implements. |
| [HashFactory](Cryptography/HashFactory.md) | class | Constructs hash algorithm instances by name or by OID. |
| [Hmac](Cryptography/Hmac.md) | class | HMAC keyed-hash message authentication code per RFC 2104. |
| [HttpTsaClient](Cryptography/HttpTsaClient.md) | class | An `ITsaClient` that POSTs RFC 3161 requests over HTTP(S) using `HttpClient`. |
| [IAsyncTsaClient](Cryptography/IAsyncTsaClient.md) | interface | An asynchronous TSA client. |
| [IHashAlgorithm](Cryptography/IHashAlgorithm.md) | interface | A streaming cryptographic hash function. |
| [IPublicKey](Cryptography/IPublicKey.md) | interface | Marker interface implemented by all Chuvadi public-key types. |
| [ISigner](Cryptography/ISigner.md) | interface | A pluggable signing primitive used by `CmsSignedDataBuilder` to produce a CMS SignerInfo signature. |
| [ITsaClient](Cryptography/ITsaClient.md) | interface | A client capable of fetching an RFC 3161 timestamp from a TSA. |
| [IssuerAndSerialNumber](Cryptography/IssuerAndSerialNumber.md) | class | Identifies an X.509 certificate by its issuer's distinguished name and the certificate's serial number. |
| [KeyUsageExtension](Cryptography/KeyUsageExtension.md) | class | The Key Usage extension — restricts the cryptographic operations the certified key may participate in. |
| [KeyUsageFlags](Cryptography/KeyUsageFlags.md) | enum | — |
| [KnownOids](Cryptography/KnownOids.md) | class | Named ObjectIdentifier constants for the OIDs Chuvadi cares about. |
| [MessageImprint](Cryptography/MessageImprint.md) | class | The cryptographic commitment that a timestamp token covers. |
| [ObjectIdentifier](Cryptography/ObjectIdentifier.md) | class | An ASN.1 OBJECT IDENTIFIER — an ordered sequence of non-negative arcs. |
| [OcspResponse](Cryptography/OcspResponse.md) | class | A parsed OCSP response. |
| [OcspResponseSignatureVerifier](Cryptography/OcspResponseSignatureVerifier.md) | class | Verifies the signature on a `BasicOcspResponse`. |
| [OcspResponseStatus](Cryptography/OcspResponseStatus.md) | enum | The top-level status of an OCSP response. |
| [OidNameLookup](Cryptography/OidNameLookup.md) | class | Maps an `ObjectIdentifier` to the friendly name from `KnownOids` for diagnostics and error messages. |
| [PublicKeyAlgorithm](Cryptography/PublicKeyAlgorithm.md) | enum | Public-key algorithm families Chuvadi recognises. |
| [RelativeDistinguishedName](Cryptography/RelativeDistinguishedName.md) | class | A SET of one or more attributes that together form one component of a DN. |
| [ResponderID](Cryptography/ResponderID.md) | class | Identifies the responder that signed an OCSP response. |
| [RevokedCertificate](Cryptography/RevokedCertificate.md) | class | One revocation entry from a CRL. |
| [Rfc6979](Cryptography/Rfc6979.md) | class | Deterministic ECDSA / DSA nonce generation per RFC 6979. |
| [RsaPkcs1V15Signer](Cryptography/RsaPkcs1V15Signer.md) | class | An `ISigner` implementation backed by Chuvadi's hand-rolled RSASSA-PKCS1-v1_5 signing primitive (`RsaSigner`). |
| [RsaPrivateKey](Cryptography/RsaPrivateKey.md) | class | An RSA private key — modulus n, public exponent e, and private exponent d. |
| [RsaPssSigner](Cryptography/RsaPssSigner.md) | class | An `ISigner` backed by Chuvadi's RSASSA-PSS primitive. |
| [RsaPublicKey](Cryptography/RsaPublicKey.md) | class | An RSA public key — modulus n and public exponent e. |
| [RsaSigner](Cryptography/RsaSigner.md) | class | Hand-rolled RSASSA-PKCS1-v1_5 signing per RFC 8017 §8.2. |
| [RsaVerifier](Cryptography/RsaVerifier.md) | class | Verifies RSA signatures in PKCS#1 v1.5 (RSASSA-PKCS1-v1_5) and PSS (RSASSA-PSS) formats per RFC 8017. |
| [Sha1](Cryptography/Sha1.md) | class | SHA-1 used only for lookup-key purposes mandated by external specs: RFC 6960 §4.1.1 (OCSP CertID IssuerNameHash / IssuerKeyHash) and ISO 32000-2 §12.8.4.3 (PDF DSS VRI keys). |
| [Sha256](Cryptography/Sha256.md) | class | SHA-256 hash function per FIPS 180-4 §6.2. |
| [Sha512](Cryptography/Sha512.md) | class | SHA-512 and SHA-384 hash functions per FIPS 180-4 §6.4 and §6.5. |
| [SignatureVerifier](Cryptography/SignatureVerifier.md) | class | Top-level signature-verification dispatcher. |
| [SignedData](Cryptography/SignedData.md) | class | A decoded CMS SignedData structure. |
| [SignerIdentifier](Cryptography/SignerIdentifier.md) | class | Identifies which certificate in the SignedData.certificates set produced a particular SignerInfo. |
| [SignerIdentifierKind](Cryptography/SignerIdentifierKind.md) | enum | The two variants of a SignerIdentifier. |
| [SignerInfo](Cryptography/SignerInfo.md) | class | One signer's contribution to a SignedData structure. |
| [SingleResponse](Cryptography/SingleResponse.md) | class | One certificate's entry within an OCSP response's `responses` field. |
| [SubjectAlternativeNameExtension](Cryptography/SubjectAlternativeNameExtension.md) | class | The Subject Alternative Name extension — additional naming forms for the certificate subject. |
| [SubjectKeyIdentifierExtension](Cryptography/SubjectKeyIdentifierExtension.md) | class | The Subject Key Identifier extension — a short octet string identifying the certificate's public key, used to find issuer certificates during path building. |
| [SubjectPublicKeyInfo](Cryptography/SubjectPublicKeyInfo.md) | class | The public key carried by an X.509 certificate, together with the algorithm identifier needed to interpret its bytes. |
| [TbsCertificate](Cryptography/TbsCertificate.md) | class | The "to-be-signed" body of an X.509 certificate. |
| [TimeStampRequest](Cryptography/TimeStampRequest.md) | class | An RFC 3161 Time-Stamp Protocol request, ready to POST to a TSA. |
| [TimeStampResponse](Cryptography/TimeStampResponse.md) | class | An RFC 3161 Time-Stamp Protocol response, as returned by a TSA. |
| [TimeStampStatus](Cryptography/TimeStampStatus.md) | enum | Status code from a TSA response per RFC 3161 §2.4.2 (PKIStatus). |
| [TimeStampToken](Cryptography/TimeStampToken.md) | class | An RFC 3161 TimeStampToken — a CMS SignedData wrapping a TSTInfo payload. |
| [TimeStampTokenVerifier](Cryptography/TimeStampTokenVerifier.md) | class | Verifies an RFC 3161 TimeStampToken cryptographically and against a known message-imprint payload. |
| [TimeStampVerificationResult](Cryptography/TimeStampVerificationResult.md) | class | The result of verifying a TimeStampToken. |
| [TimeStampVerificationStatus](Cryptography/TimeStampVerificationStatus.md) | enum | Outcome of `TimeStampTokenVerifier`. |
| [TrustAnchor](Cryptography/TrustAnchor.md) | class | A trust anchor — a CA the verifier trusts to vouch for certificates it issues. |
| [TrustStore](Cryptography/TrustStore.md) | class | A collection of trust anchors, with subject-name lookup. |
| [TsaException](Cryptography/TsaException.md) | class | Thrown when a TSA returns a non-success HTTP status or otherwise fails to produce a usable response. |
| [TstInfo](Cryptography/TstInfo.md) | class | The structured timestamp content inside a TimeStampToken. |
| [Validity](Cryptography/Validity.md) | class | The validity period of an X.509 certificate. |
| [X509Certificate](Cryptography/X509Certificate.md) | class | A fully-decoded X.509 certificate. |
| [X509Extension](Cryptography/X509Extension.md) | class | A single X.509 v3 extension — an OID, a criticality flag, and an opaque OCTET STRING value whose contents are defined per OID. |
| [X509Name](Cryptography/X509Name.md) | class | An X.500 distinguished name — a sequence of Relative Distinguished Names. |

## Chuvadi.Pdf.Documents

| Type | Kind | Description |
|---|---|---|
| [EncryptionInfo](Documents/EncryptionInfo.md) | class | Describes the encryption properties of a `PdfDocument`. |
| [OptionalContentGroup](Documents/OptionalContentGroup.md) | class | An Optional Content Group (OCG) — a named, toggleable layer in a PDF. |
| [OptionalContentReader](Documents/OptionalContentReader.md) | class | Reads optional content groups (layers) from a PDF document. |
| [OptionalContentWriter](Documents/OptionalContentWriter.md) | class | Writes optional-content (layer) visibility changes to a PDF document. |
| [PdfDocument](Documents/PdfDocument.md) | class | Represents an opened PDF document. |
| [PdfDocumentAsync](Documents/PdfDocumentAsync.md) | class | Async-capable entry points for `PdfDocument`. |
| [PdfPage](Documents/PdfPage.md) | class | Represents a single page in a PDF document. |
| [PdfPageCollection](Documents/PdfPageCollection.md) | class | Provides lazy, random-access to the pages of a PDF document. |
| [PdfRectangle](Documents/PdfRectangle.md) | struct | An immutable rectangle in PDF user space (points, 1/72 inch). |
| [XfaDataField](Documents/XfaDataField.md) | class | A single value drawn from the XFA `datasets` packet's data layer (`&lt;xfa:data&gt;`): the data element's path, its text value, and a best-effort widget geometry the host can use to overlay the value onto the rendered template. |
| [XfaGeometry](Documents/XfaGeometry.md) | class | Best-effort geometry for an `XfaDataField`, taken from a matching AcroForm widget annotation's `/Rect`. |
| [XfaKind](Documents/XfaKind.md) | enum | Classifies how a document uses XFA (XML Forms Architecture), so a consumer can tell forms that render from the page content apart from dynamic XFA that needs a dedicated processor and may otherwise appear blank. |
| [XfaPacket](Documents/XfaPacket.md) | class | A single named packet of an XFA (XML Forms Architecture) form, such as the `template`, `datasets`, or `config` packet. |
| [XfaPackets](Documents/XfaPackets.md) | class | Provides read access to a document's XFA (XML Forms Architecture) packets and to the data layer they carry. |

## Chuvadi.Pdf.Encryption

| Type | Kind | Description |
|---|---|---|
| [AesCrypto](Encryption/AesCrypto.md) | class | AES-CBC encryption/decryption with PDF's IV-prefix wire format. |
| [Decryptor](Encryption/Decryptor.md) | class | Decrypts individual strings and streams in an encrypted PDF. |
| [EncryptionAlgorithm](Encryption/EncryptionAlgorithm.md) | enum | Identifies which encryption algorithm a PDF uses. |
| [EncryptionDictionary](Encryption/EncryptionDictionary.md) | class | Parsed view of a PDF's /Encrypt trailer entry. |
| [Encryptor](Encryption/Encryptor.md) | class | Encrypts individual strings and streams for writing an encrypted PDF. |
| [PdfEncryption](Encryption/PdfEncryption.md) | class | Top-level helper for decrypting an encrypted PDF. |
| [Rc4](Encryption/Rc4.md) | class | RC4 stream cipher. |
| [StandardSecurityHandler](Encryption/StandardSecurityHandler.md) | class | Standard security handler: derives a file encryption key from a user/owner password and the document's /Encrypt dictionary. |

## Chuvadi.Pdf.Filters

| Type | Kind | Description |
|---|---|---|
| [Adler32](Filters/Adler32.md) | class | Computes and verifies Adler-32 checksums as defined in RFC 1950. |
| [Ascii85Filter](Filters/Ascii85Filter.md) | class | Implements the PDF ASCII85Decode filter. 4 binary bytes → 5 ASCII characters. |
| [AsciiHexFilter](Filters/AsciiHexFilter.md) | class | Implements the PDF ASCIIHexDecode filter. |
| [CcittFaxFilter](Filters/CcittFaxFilter.md) | class | Implements the `CCITTFaxDecode` filter: Group 3 one-dimensional (Modified Huffman), Group 3 two-dimensional (Modified READ), and Group 4 (Modified Modified READ) decoding of bilevel image data, as used by scanned-document PDFs. |
| [DeflateEffort](Filters/DeflateEffort.md) | enum | Effort level for FlateDecode (DEFLATE) compression. |
| [DeflateFilter](Filters/DeflateFilter.md) | class | Implements the PDF FlateDecode filter using zlib-framed DEFLATE. |
| [FilterException](Filters/FilterException.md) | class | Thrown when a PDF stream filter encounters data it cannot decode or encode. |
| [FilterParameters](Filters/FilterParameters.md) | record | Parameters passed to a filter's Decode or Encode operation, derived from the `/DecodeParms` or `/EncodeParms` dictionary in the stream dictionary. |
| [FilterPipeline](Filters/FilterPipeline.md) | class | Applies and removes chains of PDF stream filters. |
| [FilterRegistry](Filters/FilterRegistry.md) | class | Central registry of PDF stream filter implementations. |
| [IStreamFilter](Filters/IStreamFilter.md) | interface | Defines the contract for a PDF stream filter. |
| [Jbig2Filter](Filters/Jbig2Filter.md) | class | The `JBIG2Decode` filter (PDF 32000-1:2008 §7.4.7). |
| [LzwFilter](Filters/LzwFilter.md) | class | Implements the PDF LZWDecode filter. |
| [RunLengthFilter](Filters/RunLengthFilter.md) | class | Implements the PDF RunLengthDecode filter (PackBits algorithm). |

## Chuvadi.Pdf.Fonts

| Type | Kind | Description |
|---|---|---|
| [AdobeGlyphList](Fonts/AdobeGlyphList.md) | class | Provides the canonical Adobe Glyph List (AGL) version 2.0, mapping PostScript glyph names to their Unicode scalar values. |
| [BrotliEncoder](Fonts/BrotliEncoder.md) | class | Pure-C# Brotli encoder. |
| [BrotliStoredEncoder](Fonts/BrotliStoredEncoder.md) | class | Emits Brotli-compatible bitstreams for WOFF2 packaging. |
| [CMapParseResult](Fonts/CMapParseResult.md) | class | The full result of parsing a ToUnicode CMap: the bf-char/bf-range mappings and the declared codespace ranges. |
| [CMapParser](Fonts/CMapParser.md) | class | Parses a PDF ToUnicode CMap stream and builds a character code to Unicode string mapping. |
| [CffLoader](Fonts/CffLoader.md) | class | Loads a Compact Font Format (CFF) / Type 1C font program and produces glyph outlines. |
| [CodespaceRange](Fonts/CodespaceRange.md) | record struct | A declared codespace range from a CMap's `begincodespacerange ... endcodespacerange` block. |
| [FontException](Fonts/FontException.md) | class | Thrown when a font dictionary cannot be parsed or a character code cannot be mapped to a Unicode codepoint. |
| [FontRenderer](Fonts/FontRenderer.md) | class | High-level API for extracting glyph outlines from a TrueType, OpenType, or CFF (Type 1C) font. |
| [FontRenderingException](Fonts/FontRenderingException.md) | class | Thrown when a font file cannot be parsed or a glyph outline cannot be extracted due to an invalid or unsupported font structure. |
| [GlyphMetrics](Fonts/GlyphMetrics.md) | class | Typographic metrics for a single glyph, in font units (unscaled). |
| [GlyphNameToUnicode](Fonts/GlyphNameToUnicode.md) | class | Implements the Adobe Glyph List algorithm for deriving Unicode scalar values from a glyph name. |
| [GlyphOutline](Fonts/GlyphOutline.md) | class | The outline of a single glyph as a `Path` of contours, together with its `GlyphMetrics`. |
| [OpenTypeFontBuilder](Fonts/OpenTypeFontBuilder.md) | class | Wraps a raw CFF font program in an OpenType (OTTO) SFNT envelope with the synthesised tables a browser requires (`CFF `, `cmap`, `head`, `hhea`, `hmtx`, `maxp`, `name`, `OS/2`, `post`), so the font can be embedded in an SVG `@font-face` rule and located by semantic Unicode code point. |
| [PdfFont](Fonts/PdfFont.md) | class | Represents a PDF font and provides character code to Unicode mapping for text extraction purposes. |
| [PdfFontEncoding](Fonts/PdfFontEncoding.md) | class | Maps 1-byte character codes (0-255) to Unicode codepoints for simple fonts. |
| [RenderableFont](Fonts/RenderableFont.md) | class | A PDF font that supports both text decoding (character codes to Unicode) and glyph rendering (character codes to vector outlines + metrics). |
| [Standard14GlyphWidths](Fonts/Standard14GlyphWidths.md) | class | Per-character widths for the PDF Standard 14 fonts. |
| [Standard14Outlines](Fonts/Standard14Outlines.md) | class | Provides glyph outlines for the PDF Standard 14 fonts from an embedded resource, so they work even on hosts that lack the fonts (Blazor WASM, headless servers). |
| [Standard14Widths](Fonts/Standard14Widths.md) | class | Provides advance-width metrics for the PDF Standard 14 fonts in 1/1000-em font design units. |
| [TrueTypeFontPatch](Fonts/TrueTypeFontPatch.md) | class | Rewrites the cmap table of an embedded TrueType font program so the browser can locate the embedded glyph by its semantic Unicode code point rather than the font's legacy encoding code point. |
| [TrueTypeLoader](Fonts/TrueTypeLoader.md) | class | Loads a TrueType or OpenType font from raw bytes and provides access to glyph outlines and metrics. |
| [Type1FontRenderer](Fonts/Type1FontRenderer.md) | class | Renders glyphs from an embedded Type1 (PostScript) font program — the `FontFile` stream of a simple Type1 font. |
| [Woff2Packer](Fonts/Woff2Packer.md) | class | Packs a TrueType / OpenType font into the WOFF2 container format. |
| [Woff2Unpacker](Fonts/Woff2Unpacker.md) | class | Decodes a WOFF2 font into an sfnt (TrueType) byte array. |

## Chuvadi.Pdf.Forms

| Type | Kind | Description |
|---|---|---|
| [FormException](Forms/FormException.md) | class | Thrown when an AcroForm or outline operation fails. |
| [FormField](Forms/FormField.md) | class | A single AcroForm field, read from a PDF document. |
| [FormFieldType](Forms/FormFieldType.md) | enum | Type of an AcroForm field. |
| [FormFiller](Forms/FormFiller.md) | class | Fills AcroForm field values in a PDF document and writes the result. |
| [FormReader](Forms/FormReader.md) | class | Reads AcroForm interactive form fields from a PDF document. |
| [OutlineItem](Forms/OutlineItem.md) | class | A single bookmark in the document outline tree. |
| [OutlineReader](Forms/OutlineReader.md) | class | Reads the document outline (bookmark) tree from a PDF. |

## Chuvadi.Pdf.Graphics

| Type | Kind | Description |
|---|---|---|
| [ColorF](Graphics/ColorF.md) | struct | An immutable colour value, with support for DeviceGray, DeviceRGB, and DeviceCMYK colour spaces. |
| [ColorSpace](Graphics/ColorSpace.md) | enum | The colour space of a `ColorF` value. |
| [FillRule](Graphics/FillRule.md) | enum | Determines how the interior of a path is defined when the path self-intersects or has nested sub-paths. |
| [LineCap](Graphics/LineCap.md) | enum | Specifies the shape of the ends of open subpaths when stroked. |
| [LineJoin](Graphics/LineJoin.md) | enum | Specifies the shape of corners where two path segments meet when stroked. |
| [Path](Graphics/Path.md) | class | A mutable vector graphics path built from moveto, lineto, curve, and close operations. |
| [PathFlattener](Graphics/PathFlattener.md) | class | Flattens a `Path` containing cubic Bezier curves into a sequence of straight line segments suitable for scanline rasterization. |
| [PathSegment](Graphics/PathSegment.md) | struct | A single segment in a vector graphics path. |
| [PathSegmentKind](Graphics/PathSegmentKind.md) | enum | The kind of a `PathSegment`. |
| [PixelBuffer](Graphics/PixelBuffer.md) | class | A packed BGRA (Blue, Green, Red, Alpha) pixel buffer. |
| [PointF](Graphics/PointF.md) | struct | An immutable point in 2D user space, measured in PDF points (1/72 inch). |
| [RectangleF](Graphics/RectangleF.md) | struct | An immutable axis-aligned rectangle in PDF user space (points, 1/72 inch). |
| [SizeF](Graphics/SizeF.md) | struct | An immutable size (width × height) in PDF points (1/72 inch). |
| [StrokeStyle](Graphics/StrokeStyle.md) | class | Encapsulates all stroke properties: width, cap, join, dash pattern, and miter limit. |
| [Transform](Graphics/Transform.md) | struct | An immutable 2D affine transformation matrix. |

## Chuvadi.Pdf.IO

| Type | Kind | Description |
|---|---|---|
| [EncryptionOptions](IO/EncryptionOptions.md) | class | Options that drive encrypted PDF writing. |
| [LinearizationInfo](IO/LinearizationInfo.md) | class | Parsed view of a PDF's linearization parameter dictionary. |
| [LinearizationReader](IO/LinearizationReader.md) | class | Detects linearization and parses the parameter dictionary. |
| [PdfReader](IO/PdfReader.md) | class | Opens an existing PDF file and provides access to its object graph. |
| [PdfRepairer](IO/PdfRepairer.md) | class | Repairs structurally damaged PDFs that standard readers reject — broken or missing cross-reference tables, wrong `startxref` offsets, missing or corrupt trailers, leading junk before the header, truncated files, and duplicate objects from incremental updates. |
| [PdfWriter](IO/PdfWriter.md) | class | Writes a complete PDF file to an output stream. |
| [RepairReport](IO/RepairReport.md) | class | Describes what `PdfRepairer` did while reconstructing a damaged PDF. |
| [SynthesizedMetadata](IO/SynthesizedMetadata.md) | enum | — |
| [XrefStyle](IO/XrefStyle.md) | enum | Selects the cross-reference format `PdfWriter` writes. |

## Chuvadi.Pdf.Images

| Type | Kind | Description |
|---|---|---|
| [BmpDecoder](Images/BmpDecoder.md) | class | Decodes a Windows BMP image into an `ImageFrame`. |
| [BmpEncoder](Images/BmpEncoder.md) | class | Encodes an `ImageFrame` to Windows BMP format. |
| [CmykConverter](Images/CmykConverter.md) | class | Converts `PixelBuffer` BGRA data to packed CMYK 8 bits per channel. |
| [CmykImage](Images/CmykImage.md) | class | A planar CMYK 8-bit-per-channel image. |
| [CmykTiffEncoder](Images/CmykTiffEncoder.md) | class | Encodes `CmykImage` objects to a baseline TIFF 6.0 byte stream with CMYK photometric interpretation (5). |
| [ImageColorFormat](Images/ImageColorFormat.md) | enum | Specifies the colour format of a decoded image. |
| [ImageException](Images/ImageException.md) | class | Thrown when an image cannot be decoded or encoded due to an invalid format, unsupported feature, or data corruption. |
| [ImageFrame](Images/ImageFrame.md) | class | A decoded image frame held in a `PixelBuffer`. |
| [JpegDecoder](Images/JpegDecoder.md) | class | Decodes baseline sequential (SOF0) and progressive (SOF2) DCT JPEG images into an `ImageFrame`. |
| [JpegEncoder](Images/JpegEncoder.md) | class | Encodes images as baseline sequential JPEG (SOF0) with JFIF headers. |
| [PngDecoder](Images/PngDecoder.md) | class | Decodes a PNG image into an `ImageFrame`. |
| [PngEncoder](Images/PngEncoder.md) | class | Encodes an `ImageFrame` to PNG format. |
| [TiffDecoder](Images/TiffDecoder.md) | class | Decodes TIFF images per TIFF 6.0 baseline. |
| [TiffEncoder](Images/TiffEncoder.md) | class | Encodes one or more `ImageFrame` objects to a baseline TIFF 6.0 byte stream. |
| [TiffException](Images/TiffException.md) | class | Thrown when a TIFF operation fails. |

## Chuvadi.Pdf.Objects

| Type | Kind | Description |
|---|---|---|
| [IPdfObjectResolver](Objects/IPdfObjectResolver.md) | interface | Resolves PDF indirect object references to their primitive values. |
| [PdfIndirectObject](Objects/PdfIndirectObject.md) | class | Represents an indirect object — a `PdfPrimitive` paired with the `PdfObjectId` that identifies it in the PDF file. |
| [PdfObjectStore](Objects/PdfObjectStore.md) | class | In-memory store for PDF indirect objects, with lazy indirect reference resolution. |
| [XrefEntry](Objects/XrefEntry.md) | struct | Represents one entry in a PDF cross-reference table or stream. |
| [XrefEntryType](Objects/XrefEntryType.md) | enum | Identifies the type of a `XrefEntry`. |
| [XrefStreamTable](Objects/XrefStreamTable.md) | class | Reads and writes PDF 1.5+ cross-reference streams. |
| [XrefTable](Objects/XrefTable.md) | class | Represents a classic PDF cross-reference table. |

## Chuvadi.Pdf.Operations

| Type | Kind | Description |
|---|---|---|
| [AnnotationFlattenKinds](Operations/AnnotationFlattenKinds.md) | enum | — |
| [AnnotationFlattenOptions](Operations/AnnotationFlattenOptions.md) | class | Options controlling `AnnotationFlattener`: which annotation kinds to bake, whether to strip the AcroForm field tree once its widgets are baked, whether to skip invisible annotations, and whether to drop any annotations left live after baking. |
| [AnnotationFlattener](Operations/AnnotationFlattener.md) | class | Flattens annotations and AcroForm field widgets by baking each annotation's normal appearance stream (`/AP /N`) into the page content as a form XObject, then removing the live annotation. |
| [BandText](Operations/BandText.md) | class | The left, centre, and right text segments of a header or footer band. |
| [BookletOptions](Operations/BookletOptions.md) | class | Options for `Imposition.Booklet(System.IO.Stream, Chuvadi.Pdf.Documents.PdfDocument, BookletOptions)`: the size of each source-page slot (the output sheet is twice this width) and the margin around each slot. |
| [CompressToTargetOptions](Operations/CompressToTargetOptions.md) | record | Options for `PdfCompressor.CompressToTarget`. |
| [CompressToTargetResult](Operations/CompressToTargetResult.md) | record | The outcome of a `PdfCompressor.CompressToTarget` call. |
| [CompressionOptions](Operations/CompressionOptions.md) | record | Options controlling `PdfCompressor.Compress`. |
| [CompressionResult](Operations/CompressionResult.md) | record | Statistics describing what `PdfCompressor.Compress` did. |
| [CompressionSkipReason](Operations/CompressionSkipReason.md) | enum | Why `PdfCompressor.Compress` declined to rewrite a document. |
| [DocumentInfo](Operations/DocumentInfo.md) | class | Sets document-information metadata (Title, Author, Subject, Keywords) on an existing document and writes the result. |
| [HeaderFooter](Operations/HeaderFooter.md) | class | Adds running headers and/or footers to a document, with three strategies for how the bands interact with existing content (see `PageContentFit`): overlay in the margins, always reserve-and-scale, or scale only when content intrudes. |
| [HeaderFooterOptions](Operations/HeaderFooterOptions.md) | class | Options controlling header/footer content, geometry, and the content-fit strategy. |
| [Imposition](Operations/Imposition.md) | class | Composes the pages of a source document onto larger sheets: N-up grids and 2-up saddle-stitch booklets. |
| [MergeOptions](Operations/MergeOptions.md) | class | Options for `PageOperations.Merge(System.IO.Stream, System.Collections.Generic.IReadOnlyList{Chuvadi.Pdf.Documents.PdfDocument}, MergeOptions)`. |
| [NUpOptions](Operations/NUpOptions.md) | class | Options for `Imposition.NUp(System.IO.Stream, Chuvadi.Pdf.Documents.PdfDocument, NUpOptions)`: how many source pages to place per sheet, the sheet size, and the spacing around and between cells. |
| [NUpOrder](Operations/NUpOrder.md) | enum | The order in which source pages fill the cells of an N-up sheet. |
| [OperationsException](Operations/OperationsException.md) | class | Thrown when a PDF page operation (merge, split, delete, rotate, reorder) cannot be completed due to an invalid argument or document structure. |
| [OutlineEntry](Operations/OutlineEntry.md) | class | A bookmark to write into a document outline: a title, the zero-based page it targets, and optional nested children. |
| [OutlineWriter](Operations/OutlineWriter.md) | class | Writes a document outline (bookmark tree) onto an existing document, replacing any existing outline. |
| [PageComposer](Operations/PageComposer.md) | class | Builds a new PDF by placing pages from existing documents onto target sheets under arbitrary affine transforms. |
| [PageContentFit](Operations/PageContentFit.md) | enum | How header/footer bands interact with existing page content. |
| [PageCrop](Operations/PageCrop.md) | struct | Identifies a single page to crop and the crop rectangle, in PDF user-space points (origin at the bottom-left of the page), to confine it to. |
| [PageCropMode](Operations/PageCropMode.md) | enum | Selects how `PageCropper` confines a page to its crop rectangle. |
| [PageCropper](Operations/PageCropper.md) | class | Crops pages to a rectangle by setting their `/MediaBox` and `/CropBox` to the crop rectangle and wrapping the existing content in a hard clip (`q &lt;rect&gt; re W n &#8230; Q`) so nothing outside the rectangle is painted. |
| [PageOperations](Operations/PageOperations.md) | class | Provides static methods for high-level PDF page operations: merge, split, delete, rotate, and reorder. |
| [PageOverlay](Operations/PageOverlay.md) | class | Recolours existing pages by drawing a solid background fill behind the page content and/or rendering the existing content at reduced opacity. |
| [PageSelector](Operations/PageSelector.md) | struct | Identifies a single source page for `PageOperations.Assemble(System.IO.Stream, System.Collections.Generic.IReadOnlyList{PageSelector})`: a source document paired with a zero-based page index. |
| [PageStamper](Operations/PageStamper.md) | class | Stamps a source page onto one or more existing pages of a target document under an affine transform, preserving the rest of the document. |
| [PdfCompressor](Operations/PdfCompressor.md) | class | Rewrites a PDF document to a smaller equivalent. |
| [PdfCompressor](Operations/PdfCompressor.md) | class | — |
| [PlacePageOptions](Operations/PlacePageOptions.md) | class | Optional per-placement controls for `PageComposer.PlacePage(Chuvadi.Pdf.Documents.PdfDocument, int, Transform, PlacePageOptions)`. |
| [Placement](Operations/Placement.md) | class | Builds the affine transforms most often needed when placing a page: scale a source box to fit a destination, centre it, or rotate it by an arbitrary angle. |
| [StampAnchor](Operations/StampAnchor.md) | enum | The twelve positions a text stamp can be anchored to on a page: the three top and three bottom positions (horizontal text), plus three on each vertical edge (text rotated 90° to read up the left edge or down the right edge). |
| [StampContext](Operations/StampContext.md) | class | Substitutes tokens in a stamp template into final text for one page. |
| [StampFirstPageMode](Operations/StampFirstPageMode.md) | enum | Controls how the document's first page (page index 0) participates in a `StampNumbering` running sequence. |
| [StampNumbering](Operations/StampNumbering.md) | class | Describes a running numbering sequence for the `{number}` stamp token: a free prefix and suffix, a start value, an optional zero-pad width, a `NumberingFormat` style, and first-page handling. |
| [StampPipeline](Operations/StampPipeline.md) | class | Accumulates several overlay operations — text stamps, page numbers, text watermarks, headers and footers — plus an optional outline, and applies them all in a single write. |
| [StampPlacement](Operations/StampPlacement.md) | enum | Whether a stamp is drawn over or under the existing page content. |
| [StampTokens](Operations/StampTokens.md) | class | Resolves stamp templates against a `StampContext`. |
| [TextStamper](Operations/TextStamper.md) | class | Draws a single line of text at one of twelve anchor positions on selected pages, with template-token substitution (page numbers in several styles, file name/path, caller-supplied date/time, literal text). |

## Chuvadi.Pdf.PdfA

| Type | Kind | Description |
|---|---|---|
| [PdfAConformance](PdfA/PdfAConformance.md) | enum | The PDF/A conformance level to target. |
| [PdfAOptions](PdfA/PdfAOptions.md) | class | Options controlling PDF/A production. |
| [PdfAResult](PdfA/PdfAResult.md) | class | The outcome of a PDF/A write attempt. |
| [PdfAWriter](PdfA/PdfAWriter.md) | class | Writes PDF/A-1b and PDF/A-2b conforming documents. |

## Chuvadi.Pdf.Primitives

| Type | Kind | Description |
|---|---|---|
| [PdfArray](Primitives/PdfArray.md) | class | Represents a PDF array object — an ordered sequence of primitives. |
| [PdfBoolean](Primitives/PdfBoolean.md) | class | Represents a PDF boolean value (`true` or `false`). |
| [PdfCorruptionException](Primitives/PdfCorruptionException.md) | class | Thrown when a PDF parses cleanly at the byte level but is semantically inconsistent: cyclic `/Kids` page tree references, missing required catalog entries, unresolvable indirect references, pages claiming a count that does not match their actual children, and other structural integrity failures. |
| [PdfDictionary](Primitives/PdfDictionary.md) | class | Represents a PDF dictionary object — a map from `PdfName` keys to `PdfPrimitive` values. |
| [PdfEncryptionException](Primitives/PdfEncryptionException.md) | class | Thrown when an encryption or decryption operation fails: wrong password, unsupported security handler revision, malformed encryption dictionary, missing required encryption metadata, or a cryptographic primitive that could not produce the expected output. |
| [PdfException](Primitives/PdfException.md) | class | Abstract base class for every exception raised by the Chuvadi library. |
| [PdfInteger](Primitives/PdfInteger.md) | class | Represents a PDF integer object. |
| [PdfName](Primitives/PdfName.md) | class | Represents a PDF name object (e.g. `/Type`, `/Page`). |
| [PdfNull](Primitives/PdfNull.md) | class | Represents the PDF null object. |
| [PdfObjectId](Primitives/PdfObjectId.md) | record struct | Uniquely identifies an indirect object in a PDF file. |
| [PdfPaddedInteger](Primitives/PdfPaddedInteger.md) | class | A PDF integer that serialises to exactly `PaddedWidth` ASCII characters, left-padded with leading zeros. |
| [PdfParseException](Primitives/PdfParseException.md) | class | Thrown when the bytes of a PDF cannot be parsed because they violate the PDF syntax: malformed tokens, structural errors in dictionaries or arrays, invalid integer or real literals, missing required keywords. |
| [PdfPermissionException](Primitives/PdfPermissionException.md) | class | Thrown when an operation is blocked because the document's permission flags forbid it: extracting text from a copy-restricted document, modifying a write-protected document, assembling a no-assembly document. |
| [PdfPermissions](Primitives/PdfPermissions.md) | enum | — |
| [PdfPrimitive](Primitives/PdfPrimitive.md) | class | Abstract base class for all PDF primitive object types. |
| [PdfPrimitiveType](Primitives/PdfPrimitiveType.md) | enum | Identifies the concrete type of a `PdfPrimitive`. |
| [PdfReal](Primitives/PdfReal.md) | class | Represents a PDF real (floating-point) object. |
| [PdfReference](Primitives/PdfReference.md) | class | Represents a PDF indirect object reference, e.g. `12 0 R`. |
| [PdfStream](Primitives/PdfStream.md) | class | Represents a PDF stream object — a dictionary plus a binary byte payload. |
| [PdfString](Primitives/PdfString.md) | class | Represents a PDF string object. |
| [PdfToken](Primitives/PdfToken.md) | struct | A lightweight token produced by `PdfTokenizer`. |
| [PdfTokenType](Primitives/PdfTokenType.md) | enum | Identifies the type of a token produced by `PdfTokenizer`. |
| [PdfTokenizer](Primitives/PdfTokenizer.md) | class | A forward-only, byte-level tokenizer for PDF streams. |

## Chuvadi.Pdf.Reader

| Type | Kind | Description |
|---|---|---|
| [ChuvadiPdfReader](Reader/ChuvadiPdfReader.md) | class | Production implementation of `IPdfReader` backed by the Chuvadi PDF library. |
| [IPdfReader](Reader/IPdfReader.md) | interface | High-level facade over the Chuvadi library for interactive PDF readers. |
| [PdfRenderExtensions](Reader/PdfRenderExtensions.md) | class | One-call rendering of PDF pages to common output formats. |

## Chuvadi.Pdf.Redaction

| Type | Kind | Description |
|---|---|---|
| [CommonPatterns](Redaction/CommonPatterns.md) | class | Pre-built regex strings for common PHI / PII tokens. |
| [PatternRule](Redaction/PatternRule.md) | class | A regex pattern that locates text to redact, with optional per-page filtering. |
| [PatternSets](Redaction/PatternSets.md) | class | Curated groups of `PatternRule` with checksum validators already attached, for common redaction scenarios. |
| [PatternValidators](Redaction/PatternValidators.md) | class | Reusable checksum validators for `PatternRule` post-match predicates. |
| [RedactionException](Redaction/RedactionException.md) | class | Thrown when a redaction operation fails. |
| [RedactionOptions](Redaction/RedactionOptions.md) | class | Top-level configuration for a redaction operation. |
| [RedactionRect](Redaction/RedactionRect.md) | class | One rectangle of content to permanently remove from a PDF page. |
| [Redactor](Redaction/Redactor.md) | class | Applies true PHI-safe redactions to a PDF document. |

## Chuvadi.Pdf.Rendering

| Type | Kind | Description |
|---|---|---|
| [AffineMatrix](Rendering/AffineMatrix.md) | record struct | 2D affine transformation matrix in PDF convention.  |
| [BlendModeOp](Rendering/BlendModeOp.md) | class | Pushes or pops a blend mode. |
| [BlendModes](Rendering/BlendModes.md) | class | Helpers for the PDF blend-mode (/BM) names. |
| [ClipOp](Rendering/ClipOp.md) | class | Pushes a clipping region. |
| [ClipPath](Rendering/ClipPath.md) | struct | A clipping path applied to a single render operation. |
| [ClipRegion](Rendering/ClipRegion.md) | class | A device-space clipping region used by `ScanlineRasterizer` to restrict where a fill is painted. |
| [DiagnosticKind](Rendering/DiagnosticKind.md) | enum | Classifies a `RenderingDiagnostic`. |
| [DisplayListBuilder](Rendering/DisplayListBuilder.md) | class | Builds a `PageDisplayList` by walking a page's content stream and translating each PDF operator to a `RenderOp`. |
| [DisplayListBuilder](Rendering/DisplayListBuilder.md) | class | Builds a `PageDisplayList` from a `PdfPage` by interpreting the page's content stream. |
| [DocumentSearch](Rendering/DocumentSearch.md) | class | Searches the text of a `PdfDocument` by page, streaming matches asynchronously. |
| [DrawGlyphOp](Rendering/DrawGlyphOp.md) | class | Paints a single glyph outline. |
| [DrawImageOp](Rendering/DrawImageOp.md) | class | Paints a decoded image at the position specified by a transformation matrix. |
| [FillPathOp](Rendering/FillPathOp.md) | class | Fills a path with a flat colour, applying the configured fill rule. |
| [FillRule](Rendering/FillRule.md) | enum | Fill rule for a path or clip region. |
| [FontSlant](Rendering/FontSlant.md) | enum | Slant classification for a text run. |
| [FontStyle](Rendering/FontStyle.md) | record struct | Resolved presentation style for a text run — family, weight, and slant — derived from a font's base name and FontDescriptor. |
| [FontStyleClassifier](Rendering/FontStyleClassifier.md) | class | Derives a `FontStyle` from a font's base name and, when available, its FontDescriptor `/Flags`, `/ItalicAngle`, and `/StemV`. |
| [GlyphPosition](Rendering/GlyphPosition.md) | record struct | The position of a single glyph in a `TextRun`. |
| [GradientStop](Rendering/GradientStop.md) | struct | A single sampled gradient stop: an offset in [0, 1] and its colour. |
| [HintingMode](Rendering/HintingMode.md) | enum | Controls how strongly the TrueType bytecode hinting interpreter adjusts glyph outlines before rasterization. |
| [ImageFormat](Rendering/ImageFormat.md) | enum | Raster format of image pixel data. |
| [ImageOp](Rendering/ImageOp.md) | class | Renders a raster image. |
| [LineCap](Rendering/LineCap.md) | enum | Line cap style (PDF §8.4.3.3). |
| [LineJoin](Rendering/LineJoin.md) | enum | Line join style (PDF §8.4.3.4). |
| [LineSegmentExtraction](Rendering/LineSegmentExtraction.md) | class | Extension accessor that presents a page's path content as a flat list of `LineSegment`s, flattening cubic curves to polylines. |
| [NestedDisplayListOp](Rendering/NestedDisplayListOp.md) | class | Paints another `PageDisplayList` with a composing transform. |
| [OpacityOp](Rendering/OpacityOp.md) | class | Pushes or pops an opacity group. |
| [PageDisplayList](Rendering/PageDisplayList.md) | class | A page's content as a neutral, ordered sequence of `RenderOp`s. |
| [PageDisplayList](Rendering/PageDisplayList.md) | class | An immutable, renderer-neutral representation of a PDF page's drawable content. |
| [PageRasterizer](Rendering/PageRasterizer.md) | class | Rasterizes a PDF page to a `PixelBuffer`. |
| [PaintMode](Rendering/PaintMode.md) | enum | Whether a path is filled, stroked, or both. |
| [PathCommand](Rendering/PathCommand.md) | enum | Type of a path command. |
| [PathGeometry](Rendering/PathGeometry.md) | class | An ordered sequence of path segments. |
| [PathGeometryAccessors](Rendering/PathGeometryAccessors.md) | class | Query accessors over `PathGeometry`: flattening, bounds, signed area, and point containment. |
| [PathOp](Rendering/PathOp.md) | class | Renders a path with fill and/or stroke. |
| [PathSegment](Rendering/PathSegment.md) | record struct | A single path segment. |
| [PdfBlendMode](Rendering/PdfBlendMode.md) | enum | PDF blend modes (§11.3.5). |
| [PdfColor](Rendering/PdfColor.md) | record struct | A color value with explicit source color space. |
| [PdfColorSpace](Rendering/PdfColorSpace.md) | enum | The source color space of a `PdfColor`. |
| [PdfPageExtensions](Rendering/PdfPageExtensions.md) | class | Extensions on `PdfDocument` and `PdfPage` for the display-list and text-run APIs. |
| [RasterSoftMaskInfo](Rendering/RasterSoftMaskInfo.md) | class | An active soft mask (ExtGState `/SMask`): the masking transparency group plus how to derive and place its per-pixel coverage. |
| [Rect](Rendering/Rect.md) | record struct | An axis-aligned bounding rectangle in PDF user-space coords. |
| [RenderOp](Rendering/RenderOp.md) | class | Abstract base for all operations in a `PageDisplayList`. |
| [RenderOp](Rendering/RenderOp.md) | class | Abstract base for all display-list operations. |
| [RenderOpBounds](Rendering/RenderOpBounds.md) | class | Axis-aligned bounds accessors for `RenderOp` subtypes. |
| [RenderOpKind](Rendering/RenderOpKind.md) | enum | Tag identifying the concrete `RenderOp` subtype. |
| [RenderOptions](Rendering/RenderOptions.md) | class | Options that control how a PDF page is rasterized. |
| [RenderingDiagnostic](Rendering/RenderingDiagnostic.md) | record | A single diagnostic event recorded by `DisplayListBuilder` during page construction. |
| [RenderingException](Rendering/RenderingException.md) | class | Thrown when a PDF page cannot be rasterized due to an unsupported feature, invalid data, or internal rasterizer error. |
| [ScanlineRasterizer](Rendering/ScanlineRasterizer.md) | class | Fills vector paths into a `PixelBuffer` using a scanline edge-crossing algorithm. |
| [SearchMatch](Rendering/SearchMatch.md) | class | A search match against the logical text of a page. |
| [SearchOptions](Rendering/SearchOptions.md) | class | Options controlling a search. |
| [ShadeOp](Rendering/ShadeOp.md) | class | Paints an axial or radial shading (the `sh` operator) across the active clip region. |
| [ShadingOp](Rendering/ShadingOp.md) | class | Paints an axial or radial shading (the `sh` operator). |
| [ShadingStop](Rendering/ShadingStop.md) | struct | A single colour stop of a shading gradient. |
| [SoftMaskInfo](Rendering/SoftMaskInfo.md) | class | An active soft mask (ExtGState `/SMask`) for the SVG path: the masking group's display list plus how its coverage is derived and placed. |
| [StrokeExpander](Rendering/StrokeExpander.md) | class | Converts a stroked path into a filled path by expanding each segment by half the stroke width on each side. |
| [StrokePathOp](Rendering/StrokePathOp.md) | class | Strokes a path with the supplied `StrokeStyle`. |
| [TextDirection](Rendering/TextDirection.md) | enum | Reading direction of a `TextRun`. |
| [TextOp](Rendering/TextOp.md) | class | Renders a positioned glyph run. |
| [TextRenderingMode](Rendering/TextRenderingMode.md) | enum | Rendering mode for a `TextOp` (PDF §9.3.6). |
| [TextRun](Rendering/TextRun.md) | class | A contiguous run of text on a page, with glyph-level positions for selection-overlay use cases. |
| [TextRunExtraction](Rendering/TextRunExtraction.md) | class | Extension surface for extracting text runs from a `PageDisplayList`. |
| [TextRunExtractor](Rendering/TextRunExtractor.md) | class | Walks a `PageDisplayList` and produces a sequence of `TextRun`s in reading order. |
| [TransformOp](Rendering/TransformOp.md) | class | Pushes or pops a graphics-state transformation matrix. |
| [Type3Font](Rendering/Type3Font.md) | class | A parsed Type 3 font: its FontMatrix, per-code glyph content streams, the font's own /Resources, and glyph-space widths. |
| [Type3Glyph](Rendering/Type3Glyph.md) | record struct | A single Type 3 glyph: its content stream and glyph-space width. |
| [Type3UseOp](Rendering/Type3UseOp.md) | class | Paints one Type 3 glyph: a cached glyph-space sub-display-list positioned by a composition transform (FontMatrix · text-scale · text matrix · CTM). |
| [WpfRenderer](Rendering/WpfRenderer.md) | class | Renders a `PageDisplayList` into a WPF `DrawingVisual`. |

## Chuvadi.Pdf.Signatures

| Type | Kind | Description |
|---|---|---|
| [ByteRange](Signatures/ByteRange.md) | class | The /ByteRange of a PDF signature — two disjoint regions of the file that together form the bytes the signature actually covers. |
| [DocumentSecurityStore](Signatures/DocumentSecurityStore.md) | class | The Document Security Store as defined in ISO 32000-2 §12.8.4.3. |
| [LtvMaterialDiscovery](Signatures/LtvMaterialDiscovery.md) | class | Walks a certificate chain and fetches the validation material (CRLs, OCSP responses) advertised by each certificate's extensions. |
| [LtvOptions](Signatures/LtvOptions.md) | class | Long-term validation material to embed in a PDF at sign time. |
| [PdfCounterSigner](Signatures/PdfCounterSigner.md) | class | Adds a second (or third, ...) signature to an already-signed PDF without invalidating the existing signatures. |
| [PdfDocumentDssExtensions](Signatures/PdfDocumentDssExtensions.md) | class | Extension methods on `PdfDocument` for accessing its Document Security Store. |
| [PdfDocumentSignatureExtensions](Signatures/PdfDocumentSignatureExtensions.md) | class | Signature-related extensions on `PdfDocument`. |
| [PdfDocumentTimestamper](Signatures/PdfDocumentTimestamper.md) | class | Adds a document-wide RFC 3161 timestamp (`/Type /DocTimeStamp`) to a PDF via an incremental update. |
| [PdfLtvUpdater](Signatures/PdfLtvUpdater.md) | class | Adds (or augments) a Long-Term Validation `/DSS` dictionary on an already-signed PDF, optionally emitting `/VRI` entries keyed by SHA-1 of each signature's `/Contents`. |
| [PdfSignature](Signatures/PdfSignature.md) | class | One digital signature found in a PDF document. |
| [PdfSignatureVerifier](Signatures/PdfSignatureVerifier.md) | class | Orchestrates verification of a single `PdfSignature`. |
| [PdfSignatureVerifyExtensions](Signatures/PdfSignatureVerifyExtensions.md) | class | The user-visible `Verify()` entry point on `PdfSignature`. |
| [PdfSigner](Signatures/PdfSigner.md) | class | Adds a CMS signature to a PDF document and returns the signed bytes. |
| [PdfSigningOptions](Signatures/PdfSigningOptions.md) | class | Options for `PdfSigner.Sign`. |
| [SignatureAppearance](Signatures/SignatureAppearance.md) | class | Visible appearance for a signature field. |
| [SignatureReader](Signatures/SignatureReader.md) | class | Reads digital-signature fields out of a PDF document's AcroForm tree. |
| [SignatureSubFilter](Signatures/SignatureSubFilter.md) | class | Constants and helpers for the /SubFilter entry of a PDF signature dictionary. |
| [SignatureVerificationResult](Signatures/SignatureVerificationResult.md) | class | The result of verifying a PDF digital signature. |
| [SignatureVerificationStatus](Signatures/SignatureVerificationStatus.md) | enum | The overall outcome of verifying a PDF signature. |
| [SignatureVerifyOptions](Signatures/SignatureVerifyOptions.md) | class | Options controlling signature verification. |
| [VriEntry](Signatures/VriEntry.md) | class | Per-signature validation material from the `/DSS /VRI` sub-dictionary. |

## Chuvadi.Pdf.Svg

| Type | Kind | Description |
|---|---|---|
| [SvgExportOptions](Svg/SvgExportOptions.md) | class | Options for PDF → SVG export. |
| [SvgFontStrategy](Svg/SvgFontStrategy.md) | enum | How embedded fonts are handled. |
| [SvgRenderer](Svg/SvgRenderer.md) | class | Renders a `PageDisplayList` to SVG. |
| [SvgTextStrategy](Svg/SvgTextStrategy.md) | enum | How text is rendered to SVG. |

## Chuvadi.Pdf.Text

| Type | Kind | Description |
|---|---|---|
| [ExtractionStrategy](Text/ExtractionStrategy.md) | enum | Specifies the text extraction strategy. |
| [LayoutExtractor](Text/LayoutExtractor.md) | class | Extracts text from `TextFragment` objects by reconstructing the visual reading order: group by line (Y position), sort by column (X position). |
| [LipiScript](Text/LipiScript.md) | enum | A script covered by the LiPi Sans font family. |
| [OperatorExtractor](Text/OperatorExtractor.md) | class | Extracts text from a list of `TextFragment` objects in content stream order with simple heuristics for word and line breaks. |
| [ScriptClassifier](Text/ScriptClassifier.md) | class | Classifies code points by script and splits text into script runs. |
| [ScriptRun](Text/ScriptRun.md) | record struct | A maximal run of text in a single script. |
| [ShapedGlyph](Text/ShapedGlyph.md) | record struct | One glyph of a pre-shaped run, as produced by an external shaper or by `TextShaper`. |
| [ShapingFeatures](Text/ShapingFeatures.md) | class | Controls which OpenType features `TextShaper` applies when shaping a run. |
| [TextExtractor](Text/TextExtractor.md) | class | Extracts plain text from a PDF page. |
| [TextShaper](Text/TextShaper.md) | class | Shapes a run of text using the OpenType GSUB and GPOS tables embedded in a TrueType/OpenType font. |

## Chuvadi.Pdf.Watermark

| Type | Kind | Description |
|---|---|---|
| [ImageWatermarkOptions](Watermark/ImageWatermarkOptions.md) | class | Options for stamping an image watermark onto PDF pages. |
| [TextWatermarkOptions](Watermark/TextWatermarkOptions.md) | class | Options for stamping a text watermark onto PDF pages. |
| [WatermarkException](Watermark/WatermarkException.md) | class | Thrown when a watermark cannot be applied. |
| [WatermarkStamper](Watermark/WatermarkStamper.md) | class | Stamps text or image watermarks onto PDF pages by appending new content streams, preserving the original page content. |

## Chuvadi.Pdf.Xfa

| Type | Kind | Description |
|---|---|---|
| [XfaArea](Xfa/XfaArea.md) | class | A generic container element used for grouped positioning. |
| [XfaBorder](Xfa/XfaBorder.md) | class | A node border: edge stroke and optional fill. |
| [XfaBox](Xfa/XfaBox.md) | class | A single positioned box produced by the layout engine, expressed in device space (PDF points, origin at the page's top-left, y increasing downward — matching the authoring layer's top-left drawing API). |
| [XfaBreakTarget](Xfa/XfaBreakTarget.md) | enum | The target of a forced layout break (breakBefore / breakAfter). |
| [XfaCaption](Xfa/XfaCaption.md) | class | A field caption: its text and placement relative to the value. |
| [XfaCaptionPlacement](Xfa/XfaCaptionPlacement.md) | enum | Placement of a field caption relative to its value area. |
| [XfaContentArea](Xfa/XfaContentArea.md) | class | The drawable region within a page area where content flows. |
| [XfaDataMerge](Xfa/XfaDataMerge.md) | class | Merges values from the datasets packet into a parsed template tree by resolving each field's `XfaField.DataRef` against the document's extracted `XfaDataField` list. |
| [XfaDraw](Xfa/XfaDraw.md) | class | Static, non-interactive content such as boilerplate text or lines. |
| [XfaExclGroup](Xfa/XfaExclGroup.md) | class | A mutually-exclusive group of fields (for example radio buttons). |
| [XfaField](Xfa/XfaField.md) | class | An interactive field with an optional caption, value, and UI widget. |
| [XfaFont](Xfa/XfaFont.md) | class | A font descriptor applied to caption, value, or draw text. |
| [XfaHAlign](Xfa/XfaHAlign.md) | enum | Horizontal alignment of content within a box. |
| [XfaKeepScope](Xfa/XfaKeepScope.md) | enum | The scope of a keep-together / keep-with constraint. |
| [XfaLayout](Xfa/XfaLayout.md) | enum | The layout strategy a container applies to its children. |
| [XfaLayoutEngine](Xfa/XfaLayoutEngine.md) | class | Resolves an XFA model subtree into a flat list of positioned `XfaBox`es in device space. |
| [XfaMargin](Xfa/XfaMargin.md) | class | The four-sided margin (inset) box of a node. |
| [XfaMeasurement](Xfa/XfaMeasurement.md) | struct | A linear measurement parsed from an XFA template attribute (for example "12.7mm", "0.5in", "36pt"). |
| [XfaNode](Xfa/XfaNode.md) | class | Base type for every parsed XFA template node. |
| [XfaOddOrEven](Xfa/XfaOddOrEven.md) | enum | Which page parity a page area may be used for (duplex pagination). |
| [XfaPageArea](Xfa/XfaPageArea.md) | class | A single page area, defining its size and content region. |
| [XfaPageSet](Xfa/XfaPageSet.md) | class | A page-geometry container holding one or more page areas. |
| [XfaPageSetRelation](Xfa/XfaPageSetRelation.md) | enum | How a page set generates pages from its child page areas. |
| [XfaPresence](Xfa/XfaPresence.md) | enum | Whether and how a node participates in layout and rendering. |
| [XfaRenderException](Xfa/XfaRenderException.md) | class | The exception thrown when an XFA template cannot be rendered. |
| [XfaRenderOptions](Xfa/XfaRenderOptions.md) | class | Options controlling XFA rendering. |
| [XfaRenderer](Xfa/XfaRenderer.md) | class | Renders a document's XFA template to a new PDF. |
| [XfaScriptMode](Xfa/XfaScriptMode.md) | enum | How the renderer treats embedded XFA scripts. |
| [XfaSubform](Xfa/XfaSubform.md) | class | A container of fields and nested subforms; the primary layout unit. |
| [XfaTemplateParser](Xfa/XfaTemplateParser.md) | class | Parses the XFA `template` packet XML into a typed `XfaNode` tree. |
| [XfaUi](Xfa/XfaUi.md) | class | Describes the UI widget a field uses to present its value. |
| [XfaUiKind](Xfa/XfaUiKind.md) | enum | The kind of widget a field uses to present its value. |
| [XfaVAlign](Xfa/XfaVAlign.md) | enum | Vertical alignment of content within a box. |
| [XfaValue](Xfa/XfaValue.md) | class | The value of a field or draw, carrying its resolved text content. |
