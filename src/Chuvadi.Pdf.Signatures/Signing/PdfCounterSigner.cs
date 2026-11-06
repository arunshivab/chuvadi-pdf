// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.2.6 — counter-signing via incremental update

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Cryptography.Signing;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Signatures.Signing;

/// <summary>
/// Adds a second (or third, ...) signature to an already-signed PDF
/// without invalidating the existing signatures.
/// </summary>
/// <remarks>
/// <para>
/// Mechanically a fresh signing operation appended via incremental
/// update: a new <c>/Sig</c> dictionary and field are added through
/// <see cref="PdfWriter.WriteIncrementalUpdate"/>, so all original
/// bytes are preserved and earlier signatures still hash to their
/// recorded digests.
/// </para>
/// <para>
/// The new signature's byte range covers everything except its own
/// <c>/Contents</c> placeholder — including the bytes of all earlier
/// signatures. This means a counter-signer cryptographically attests
/// to the full state of the document, including the prior signatures'
/// CMS bytes. Tampering with any earlier signature after counter-signing
/// will therefore invalidate the counter-signature, even though it
/// would not directly affect the prior signature itself.
/// </para>
/// </remarks>
public static class PdfCounterSigner
{
    /// <summary>
    /// Counter-signs the document, returning the augmented bytes.
    /// </summary>
    /// <param name="signedPdfBytes">The source document (must already carry at least one signature).</param>
    /// <param name="signer">The new signer.</param>
    /// <param name="options">Signing options (Reason, Location, TsaClient, etc.).
    /// LtvOptions on this options instance are ignored — use
    /// <see cref="PdfLtvUpdater"/> for LTV material on counter-signed
    /// documents. Since 3.31.2 <see cref="PdfSigningOptions.Appearance"/> is
    /// honoured: the signature gets a visible widget on the given page, drawn
    /// as <see cref="PdfSigner"/> draws it, and the page is rewritten in the
    /// update so that its <c>/Annots</c> lists the widget.</param>
    /// <returns>The source bytes followed by one incremental update.</returns>
    public static byte[] AddSignature(byte[] signedPdfBytes, ISigner signer, PdfSigningOptions options)
    {
        ArgumentNullException.ThrowIfNull(signedPdfBytes);
        ArgumentNullException.ThrowIfNull(signer);
        ArgumentNullException.ThrowIfNull(options);

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(signedPdfBytes), leaveOpen: false);

        if (!doc.Trailer.TryGetValue(PdfName.Root, out PdfPrimitive? rootVal) || rootVal is not PdfReference rootRef)
        {
            throw new InvalidOperationException("Source trailer has no /Root reference.");
        }
        PdfObjectId catalogId = rootRef.ObjectId;

        if (!doc.Trailer.TryGetValue(PdfName.Size, out PdfPrimitive? sizeVal) || sizeVal is not PdfInteger sizeInt)
        {
            throw new InvalidOperationException("Source trailer has no /Size.");
        }
        int nextId = sizeInt.Value;

        // Allocate IDs.
        PdfObjectId sigDictId = new(nextId++, 0);
        PdfObjectId sigFieldId = new(nextId++, 0);
        PdfObjectId acroFormId = new(nextId++, 0);

        DateTimeOffset signingTime = options.SigningTime ?? DateTimeOffset.UtcNow;

        // Build placeholder signature dictionary.
        byte[] contentsPlaceholder = new byte[options.ContentsPlaceholderSize];
        PdfDictionary sigDict = new();
        sigDict.Set(PdfName.Type, PdfName.Intern("Sig"));
        sigDict.Set(PdfName.Filter, PdfName.Intern("Adobe.PPKLite"));
        sigDict.Set(PdfName.Intern("SubFilter"), PdfName.Intern("adbe.pkcs7.detached"));

        const int sw = SignatureContentsHelper.ByteRangeSlotWidth;
        const int ph = SignatureContentsHelper.ByteRangePlaceholderValue;
        PdfArray placeholderByteRange = new();
        placeholderByteRange.Add(new PdfPaddedInteger(0, sw));
        placeholderByteRange.Add(new PdfPaddedInteger(ph, sw));
        placeholderByteRange.Add(new PdfPaddedInteger(ph, sw));
        placeholderByteRange.Add(new PdfPaddedInteger(ph, sw));
        sigDict.Set(PdfName.Intern("ByteRange"), placeholderByteRange);

        sigDict.Set(PdfName.Intern("Contents"), new PdfString(contentsPlaceholder, preferHexForm: true));
        sigDict.Set(PdfName.Intern("M"), new PdfString(SignatureContentsHelper.FormatPdfDate(signingTime)));
        if (!string.IsNullOrEmpty(options.Reason))
        {
            sigDict.Set(PdfName.Intern("Reason"), new PdfString(options.Reason));
        }
        if (!string.IsNullOrEmpty(options.Location))
        {
            sigDict.Set(PdfName.Intern("Location"), new PdfString(options.Location));
        }
        if (!string.IsNullOrEmpty(options.ContactInfo))
        {
            sigDict.Set(PdfName.Intern("ContactInfo"), new PdfString(options.ContactInfo));
        }

        // Signature field.
        PdfDictionary sigField = new();
        sigField.Set(PdfName.Intern("FT"), PdfName.Intern("Sig"));
        // Generate a default field name unique within the doc.
        string fieldName = options.SignatureFieldName;
        if (fieldName == "Signature1")
        {
            // Default — try Signature2 if Signature1 is taken.
            int n = doc.Signatures().Count + 1;
            fieldName = $"Signature{n}";
        }
        sigField.Set(PdfName.Intern("T"), new PdfString(fieldName));
        sigField.Set(PdfName.Intern("V"), new PdfReference(sigDictId));

        // 3.31.2 (D-207 1b): a visible appearance, as PdfSigner draws it. The
        // field becomes a widget on the target page, and the page is rewritten
        // in the update with the widget added to /Annots. Before 3.31.2,
        // options.Appearance was silently ignored here.
        List<PdfIndirectObject> widgetObjects = new();
        PdfObjectId? targetPageId = null;
        PdfDictionary? clonedPage = null;
        if (options.Appearance is SignatureAppearance appearance)
        {
            (targetPageId, clonedPage) = PdfSigner.BuildAppearanceWidget(
                doc, appearance, sigField, sigFieldId, widgetObjects, acroFormId);
        }

        // Extended AcroForm.
        PdfArray fields = new();
        if (doc.Catalog.TryGetValue(PdfName.Intern("AcroForm"), out PdfPrimitive? acroFormVal))
        {
            PdfDictionary? existingAcro = doc.Objects.ResolveAs<PdfDictionary>(acroFormVal);
            if (existingAcro is not null
                && existingAcro.TryGetValue(PdfName.Intern("Fields"), out PdfPrimitive? existingFieldsVal))
            {
                PdfArray? existingFields = doc.Objects.ResolveAs<PdfArray>(existingFieldsVal);
                if (existingFields is not null)
                {
                    foreach (PdfPrimitive f in existingFields) { fields.Add(f); }
                }
            }
        }
        fields.Add(new PdfReference(sigFieldId));

        PdfDictionary acroForm = new();
        acroForm.Set(PdfName.Intern("Fields"), fields);
        acroForm.Set(PdfName.Intern("SigFlags"), (PdfPrimitive)new PdfInteger(3));

        // New catalog.
        PdfDictionary newCatalog = new();
        foreach (KeyValuePair<PdfName, PdfPrimitive> kv in doc.Catalog)
        {
            newCatalog.Set(kv.Key, kv.Value);
        }
        newCatalog.Set(PdfName.Intern("AcroForm"), new PdfReference(acroFormId));

        List<PdfIndirectObject> newObjects = new()
        {
            new PdfIndirectObject(catalogId, newCatalog),
            new PdfIndirectObject(sigDictId, sigDict),
            new PdfIndirectObject(sigFieldId, sigField),
            new PdfIndirectObject(acroFormId, acroForm),
        };
        newObjects.AddRange(widgetObjects);
        if (targetPageId is PdfObjectId pageId && clonedPage is not null)
        {
            newObjects.Add(new PdfIndirectObject(pageId, clonedPage));
        }

        byte[] withPlaceholders = PdfWriter.WriteIncrementalUpdate(signedPdfBytes, newObjects);

        // Find the new sig dict's /ByteRange + /Contents inside its own object,
        // through the update's xref (3.31.2). The update can now hold a page,
        // whose /Contents must not be taken for the signature's.
        int sigObjectStart = SignatureContentsHelper.LocateObjectStart(withPlaceholders, sigDictId.ObjectNumber);
        if (sigObjectStart < signedPdfBytes.Length)
        {
            sigObjectStart = -1;
        }

        int byteRangeStart = SignatureContentsHelper.LocateByteRangeArrayStart(withPlaceholders, sigObjectStart);
        if (byteRangeStart < 0)
        {
            throw new InvalidOperationException("Could not locate /ByteRange in the appended section.");
        }

        int contentsValueStart = SignatureContentsHelper.LocateContentsHexStart(withPlaceholders, sigObjectStart);
        if (contentsValueStart < 0)
        {
            throw new InvalidOperationException("Could not locate /Contents in the appended section.");
        }
        int contentsValueEnd = contentsValueStart + 1 + (options.ContentsPlaceholderSize * 2) + 1;

        int firstLen = contentsValueStart;
        int secondStart = contentsValueEnd;
        int secondLen = withPlaceholders.Length - contentsValueEnd;
        SignatureContentsHelper.PatchByteRange(withPlaceholders, byteRangeStart, 0, firstLen, secondStart, secondLen);

        byte[] toSign = new byte[firstLen + secondLen];
        Buffer.BlockCopy(withPlaceholders, 0, toSign, 0, firstLen);
        Buffer.BlockCopy(withPlaceholders, secondStart, toSign, firstLen, secondLen);

        byte[] cms = options.TsaClient is null
            ? Chuvadi.Cryptography.Cms.CmsSignedDataBuilder.BuildDetached(
                toSign, signer, signingTime, options.ExtraCertificates)
            : Chuvadi.Cryptography.Cms.CmsSignedDataBuilder.BuildDetachedWithTimestamp(
                toSign, signer, options.TsaClient, signingTime, options.ExtraCertificates);

        if (cms.Length > options.ContentsPlaceholderSize)
        {
            throw new InvalidOperationException(
                $"CMS signature ({cms.Length} bytes) exceeds /Contents placeholder size "
                + $"({options.ContentsPlaceholderSize} bytes).");
        }

        SignatureContentsHelper.WriteHexAt(withPlaceholders, contentsValueStart + 1, cms);
        return withPlaceholders;
    }
}
