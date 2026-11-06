# PdfCounterSigner

**Class** in `Chuvadi.Pdf.Signatures.Signing` (Signatures)

Adds a second (or third, ...) signature to an already-signed PDF without invalidating the existing signatures.

```csharp
public static class PdfCounterSigner
```

## Remarks

Mechanically a fresh signing operation appended via incremental update: a new `/Sig` dictionary and field are added through `PdfWriter.WriteIncrementalUpdate`, so all original bytes are preserved and earlier signatures still hash to their recorded digests.  

 The new signature's byte range covers everything except its own `/Contents` placeholder — including the bytes of all earlier signatures. This means a counter-signer cryptographically attests to the full state of the document, including the prior signatures' CMS bytes. Tampering with any earlier signature after counter-signing will therefore invalidate the counter-signature, even though it would not directly affect the prior signature itself.

## Methods

### `AddSignature`

__static__

```csharp
static byte[] AddSignature(byte[] signedPdfBytes, ISigner signer, PdfSigningOptions options)
```

Counter-signs the document, returning the augmented bytes.

**Parameters**

- `signedPdfBytes` — The source document (must already carry at least one signature).
- `signer` — The new signer.
- `options` — Signing options (Reason, Location, TsaClient, etc.). LtvOptions on this options instance are ignored — use `PdfLtvUpdater` for LTV material on counter-signed documents. Since 3.31.2 `PdfSigningOptions.Appearance` is honoured: the signature gets a visible widget on the given page, drawn as `PdfSigner` draws it, and the page is rewritten in the update so that its `/Annots` lists the widget.

**Returns:** The source bytes followed by one incremental update.

---

_Source: [`src/Chuvadi.Pdf.Signatures/Signing/PdfCounterSigner.cs`](../../../src/Chuvadi.Pdf.Signatures/Signing/PdfCounterSigner.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
