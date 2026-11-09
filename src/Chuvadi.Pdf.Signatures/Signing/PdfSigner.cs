// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ISO 32000-2 §12.8 — Digital signatures in PDF
// PHASE: Phase 1.2.1 — PDF signing API

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Cryptography.Cms;
using Chuvadi.Cryptography.Signing;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Cryptography.Ocsp;
using Chuvadi.Cryptography.Revocation;
using Chuvadi.Cryptography.X509;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Signatures.Signing;

/// <summary>
/// Adds a CMS signature to a PDF document and returns the signed bytes.
/// </summary>
/// <remarks>
/// <para>
/// Implements the canonical PDF signing protocol:
/// </para>
/// <list type="number">
///   <item>Add a signature field + signature dictionary referencing fixed-width
///         <c>/ByteRange</c> placeholder slots and a zero-byte <c>/Contents</c>
///         reservation of <see cref="PdfSigningOptions.ContentsPlaceholderSize"/>
///         bytes.</item>
///   <item>Write the full PDF to memory; find the signature dictionary through
///         the written cross-reference table, and locate the <c>/ByteRange</c>
///         placeholder slots and the <c>/Contents</c> value inside it.</item>
///   <item>Patch the <c>/ByteRange</c> slots with the actual byte positions, using
///         leading-zero padding to preserve their fixed widths so no downstream
///         positions shift.</item>
///   <item>Sign the bytes covered by <c>/ByteRange</c> with the supplied
///         <see cref="ISigner"/> via <see cref="CmsSignedDataBuilder.BuildDetached"/>.</item>
///   <item>Hex-encode the CMS and splice it into the <c>/Contents</c> placeholder;
///         the remaining placeholder bytes stay zero.</item>
/// </list>
/// <para>
/// This is a full-rewrite signing flow: the output is a freshly-written PDF
/// carrying the new signature. A document that is already signed is refused,
/// because rewriting it would break the existing signature; use
/// <see cref="PdfCounterSigner.AddSignature"/> for it. Incremental update support (preserving the
/// original byte stream and appending) is deferred to a future session.
/// </para>
/// </remarks>
public static class PdfSigner
{
    // Width of each numeric slot inside the fixed-width /ByteRange placeholder.
    // 10 ASCII digits cover any positive int (max 2_147_483_647 fits in 10).
    private const int ByteRangeSlotWidth = 10;

    // Maximum placeholder value used during layout; recovered/patched after writing.
    private const int ByteRangePlaceholderValue = 999_999_999;


    /// <summary>
    /// Signs a PDF document and returns the signed bytes.
    /// </summary>
    /// <param name="document">The unsigned source document. Not modified.</param>
    /// <param name="signer">The signer.</param>
    /// <param name="options">Signing options (signature field name, signing time, reason, etc.).</param>
    /// <returns>The signed PDF bytes.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="document"/> already has a signature (since
    /// 3.31.2): rewriting it would break that signature. Use
    /// <see cref="PdfCounterSigner.AddSignature"/> instead.
    /// </exception>
    public static byte[] Sign(PdfDocument document, ISigner signer, PdfSigningOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(signer);
        ArgumentNullException.ThrowIfNull(options);
        ThrowIfAlreadySigned(document);

        DateTimeOffset signingTime = options.SigningTime ?? DateTimeOffset.UtcNow;

        // ── Step 1: snapshot the source PDF's object table ───────────────
        // Eagerly resolve every indirect object from the source so the writer
        // can emit them all. New objects (signature dict, field, AcroForm)
        // get IDs starting beyond the trailer's /Size to avoid collision.
        int sourceSize = ReadTrailerSize(document);
        List<PdfIndirectObject> objects = new();
        for (int n = 1; n < sourceSize; n++)
        {
            PdfObjectId id = new(n, 0);
            if (document.Objects.TryGet(id, out PdfIndirectObject? io) && io is not null)
            {
                objects.Add(io);
            }
        }

        PdfObjectId sigDictId = new(sourceSize, 0);
        PdfObjectId sigFieldId = new(sourceSize + 1, 0);
        PdfObjectId acroFormId = new(sourceSize + 2, 0);

        // ── Step 2: build the placeholder signature dictionary ───────────
        byte[] contentsPlaceholder = new byte[options.ContentsPlaceholderSize];
        PdfDictionary sigDict = new();
        sigDict.Set(PdfName.Type, PdfName.Intern("Sig"));
        sigDict.Set(PdfName.Filter, PdfName.Intern("Adobe.PPKLite"));
        sigDict.Set(PdfName.Intern("SubFilter"), PdfName.Intern("adbe.pkcs7.detached"));

        // Fixed-width ByteRange placeholder so the byte positions of later
        // entries don't shift when we patch in the real values.
        PdfArray placeholderByteRange = new();
        placeholderByteRange.Add(new PdfPaddedInteger(0, ByteRangeSlotWidth));
        placeholderByteRange.Add(new PdfPaddedInteger(ByteRangePlaceholderValue, ByteRangeSlotWidth));
        placeholderByteRange.Add(new PdfPaddedInteger(ByteRangePlaceholderValue, ByteRangeSlotWidth));
        placeholderByteRange.Add(new PdfPaddedInteger(ByteRangePlaceholderValue, ByteRangeSlotWidth));
        sigDict.Set(PdfName.Intern("ByteRange"), placeholderByteRange);

        sigDict.Set(PdfName.Intern("Contents"), new PdfString(contentsPlaceholder, preferHexForm: true));

        sigDict.Set(PdfName.Intern("M"), new PdfString(FormatPdfDate(signingTime)));
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

        // ── Step 3: build the signature field ────────────────────────────
        PdfDictionary sigField = new();
        sigField.Set(PdfName.Intern("FT"), PdfName.Intern("Sig"));
        sigField.Set(PdfName.Intern("T"), new PdfString(options.SignatureFieldName));
        sigField.Set(PdfName.Intern("V"), new PdfReference(sigDictId));

        // ── Step 3b: visible appearance widget (when requested) ─────────
        List<PdfIndirectObject> widgetObjects = new();
        PdfObjectId? targetPageId = null;
        PdfDictionary? clonedPage = null;
        if (options.Appearance is SignatureAppearance ap)
        {
            (targetPageId, clonedPage) = BuildAppearanceWidget(
                document, ap, sigField, sigFieldId, widgetObjects, acroFormId, signer.Certificate.Subject.CommonName, signingTime);
        }


        // ── Step 4: build (or extend) AcroForm ───────────────────────────
        PdfArray fields = new();
        // Preserve any existing AcroForm.Fields entries.
        if (document.Catalog.TryGetValue(PdfName.Intern("AcroForm"), out PdfPrimitive? acroFormVal))
        {
            PdfDictionary? existingAcro = document.Objects.ResolveAs<PdfDictionary>(acroFormVal);
            if (existingAcro is not null
                && existingAcro.TryGetValue(PdfName.Intern("Fields"), out PdfPrimitive? existingFieldsVal))
            {
                PdfArray? existingFields = document.Objects.ResolveAs<PdfArray>(existingFieldsVal);
                if (existingFields is not null)
                {
                    foreach (PdfPrimitive f in existingFields)
                    {
                        fields.Add(f);
                    }
                }
            }
        }
        fields.Add(new PdfReference(sigFieldId));

        PdfDictionary acroForm = new();
        acroForm.Set(PdfName.Intern("Fields"), fields);
        // SigFlags 3 = SignaturesExist (bit 0) + AppendOnly (bit 1).
        acroForm.Set(PdfName.Intern("SigFlags"), (PdfPrimitive)new PdfInteger(3));

        // ── Step 4b: build LTV objects (/DSS + /VRI) when requested ─────
        // Allocate IDs after acroFormId and after the appearance widget's
        // objects, which start at acroFormId + 1. We track the IDs into a
        // helper that builds and returns the new indirect objects + the
        // catalog's /DSS reference (or null when LTV isn't requested).
        // 3.31.2 (D-226): before, LTV also started at acroFormId + 1, so with
        // an Appearance the first LTV stream and the appearance XObject shared
        // an object number and one of them was lost.
        int nextLtvId = acroFormId.ObjectNumber + 1 + widgetObjects.Count;
        List<PdfIndirectObject> ltvObjects = new();
        PdfReference? dssRef = null;
        if (options.LtvOptions is { HasMaterial: true } ltv)
        {
            if (ltv.IncludeVri)
            {
                throw new NotSupportedException(
                    "/VRI emission is not yet supported in single-pass signing; see LtvOptions.IncludeVri docs.");
            }
            dssRef = BuildLtvObjects(ltv, ref nextLtvId, ltvObjects);
        }

        // ── Step 5: clone the catalog with an updated /AcroForm ─────────
        PdfDictionary newCatalog = new();
        foreach (KeyValuePair<PdfName, PdfPrimitive> kv in document.Catalog)
        {
            newCatalog.Set(kv.Key, kv.Value);
        }
        newCatalog.Set(PdfName.Intern("AcroForm"), new PdfReference(acroFormId));
        if (dssRef is not null)
        {
            newCatalog.Set(PdfName.Intern("DSS"), dssRef);
        }

        // Replace the original catalog object in our list. The catalog ID is
        // recovered from the trailer's /Root reference.
        PdfObjectId catalogId = ResolveCatalogId(document);
        bool catalogReplaced = false;
        for (int i = 0; i < objects.Count; i++)
        {
            if (objects[i].Id.Equals(catalogId))
            {
                objects[i] = new PdfIndirectObject(catalogId, newCatalog);
                catalogReplaced = true;
                break;
            }
        }
        if (!catalogReplaced)
        {
            objects.Add(new PdfIndirectObject(catalogId, newCatalog));
        }

        objects.Add(new PdfIndirectObject(sigDictId, sigDict));
        objects.Add(new PdfIndirectObject(sigFieldId, sigField));
        objects.Add(new PdfIndirectObject(acroFormId, acroForm));
        objects.AddRange(ltvObjects);
        objects.AddRange(widgetObjects);
        ReplacePage(objects, targetPageId, clonedPage);

        PdfDictionary trailer = new();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        // ── Step 6: write the PDF with placeholder ByteRange + zero /Contents ──
        MemoryStream ms = new();
        PdfWriter.Write(ms, objects.ToArray(), trailer);
        byte[] pdfBytes = ms.ToArray();

        // ── Step 7: locate /ByteRange and /Contents in the output ────────
        // 3.31.2 (D-207 item 1): search only inside the signature dictionary's
        // own object, found through the xref just written. Before, the first
        // /Contents in the file was taken, which is a page's on any real document.
        int sigObjectStart = SignatureContentsHelper.LocateObjectStart(pdfBytes, sigDictId.ObjectNumber);
        int byteRangeStart = SignatureContentsHelper.LocateByteRangeArrayStart(pdfBytes, sigObjectStart);
        if (byteRangeStart < 0)
        {
            throw new InvalidOperationException(
                "Could not locate /ByteRange in the signed PDF output.");
        }

        int contentsValueStart = SignatureContentsHelper.LocateContentsHexStart(pdfBytes, sigObjectStart);
        if (contentsValueStart < 0)
        {
            throw new InvalidOperationException(
                "Could not locate /Contents value in the signed PDF output.");
        }
        // /Contents value spans <HEX> where HEX has 2 * ContentsPlaceholderSize chars.
        int contentsValueEnd = contentsValueStart + 1 + (options.ContentsPlaceholderSize * 2) + 1;

        // ── Step 8: patch ByteRange with real positions ──────────────────
        int firstLen = contentsValueStart;
        int secondStart = contentsValueEnd;
        int secondLen = pdfBytes.Length - contentsValueEnd;
        PatchByteRange(pdfBytes, byteRangeStart, 0, firstLen, secondStart, secondLen);

        // ── Step 9: extract the byte-range-covered content and sign it ──
        byte[] toSign = new byte[firstLen + secondLen];
        Buffer.BlockCopy(pdfBytes, 0, toSign, 0, firstLen);
        Buffer.BlockCopy(pdfBytes, secondStart, toSign, firstLen, secondLen);

        byte[] cms = options.TsaClient is null
            ? CmsSignedDataBuilder.BuildDetached(
                toSign, signer, signingTime, options.ExtraCertificates)
            : CmsSignedDataBuilder.BuildDetachedWithTimestamp(
                toSign, signer, options.TsaClient, signingTime, options.ExtraCertificates);
        if (cms.Length > options.ContentsPlaceholderSize)
        {
            throw new InvalidOperationException(
                $"CMS signature ({cms.Length} bytes) exceeds /Contents placeholder size "
                + $"({options.ContentsPlaceholderSize} bytes). Increase ContentsPlaceholderSize.");
        }

        // ── Step 10: splice CMS into /Contents placeholder ──────────────
        WriteHexAt(pdfBytes, contentsValueStart + 1, cms);

        return pdfBytes;
    }

    /// <summary>
    /// Async variant of <see cref="Sign"/>: when <see cref="PdfSigningOptions.AsyncTsaClient"/> is set, the TSA fetch is awaited rather than blocked on. All other behavior is identical to <see cref="Sign"/>, including the visible <see cref="PdfSigningOptions.Appearance"/> widget (honoured since 3.31.2; before, it was ignored here).
    /// </summary>
    /// <param name="cancellationToken">Propagated to the async TSA fetch.</param>
    /// <param name="document">The unsigned source document. Not modified.</param>
    /// <param name="signer">The signer.</param>
    /// <param name="options">Signing options (signature field name, signing time, reason, etc.).</param>
    /// <returns>The signed PDF bytes.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="document"/> already has a signature (since
    /// 3.31.2): rewriting it would break that signature. Use
    /// <see cref="PdfCounterSigner.AddSignature"/> instead.
    /// </exception>
    public static async System.Threading.Tasks.Task<byte[]> SignAsync(PdfDocument document, ISigner signer, PdfSigningOptions options, System.Threading.CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(signer);
        ArgumentNullException.ThrowIfNull(options);
        ThrowIfAlreadySigned(document);

        DateTimeOffset signingTime = options.SigningTime ?? DateTimeOffset.UtcNow;

        // ── Step 1: snapshot the source PDF's object table ───────────────
        // Eagerly resolve every indirect object from the source so the writer
        // can emit them all. New objects (signature dict, field, AcroForm)
        // get IDs starting beyond the trailer's /Size to avoid collision.
        int sourceSize = ReadTrailerSize(document);
        List<PdfIndirectObject> objects = new();
        for (int n = 1; n < sourceSize; n++)
        {
            PdfObjectId id = new(n, 0);
            if (document.Objects.TryGet(id, out PdfIndirectObject? io) && io is not null)
            {
                objects.Add(io);
            }
        }

        PdfObjectId sigDictId = new(sourceSize, 0);
        PdfObjectId sigFieldId = new(sourceSize + 1, 0);
        PdfObjectId acroFormId = new(sourceSize + 2, 0);

        // ── Step 2: build the placeholder signature dictionary ───────────
        byte[] contentsPlaceholder = new byte[options.ContentsPlaceholderSize];
        PdfDictionary sigDict = new();
        sigDict.Set(PdfName.Type, PdfName.Intern("Sig"));
        sigDict.Set(PdfName.Filter, PdfName.Intern("Adobe.PPKLite"));
        sigDict.Set(PdfName.Intern("SubFilter"), PdfName.Intern("adbe.pkcs7.detached"));

        // Fixed-width ByteRange placeholder so the byte positions of later
        // entries don't shift when we patch in the real values.
        PdfArray placeholderByteRange = new();
        placeholderByteRange.Add(new PdfPaddedInteger(0, ByteRangeSlotWidth));
        placeholderByteRange.Add(new PdfPaddedInteger(ByteRangePlaceholderValue, ByteRangeSlotWidth));
        placeholderByteRange.Add(new PdfPaddedInteger(ByteRangePlaceholderValue, ByteRangeSlotWidth));
        placeholderByteRange.Add(new PdfPaddedInteger(ByteRangePlaceholderValue, ByteRangeSlotWidth));
        sigDict.Set(PdfName.Intern("ByteRange"), placeholderByteRange);

        sigDict.Set(PdfName.Intern("Contents"), new PdfString(contentsPlaceholder, preferHexForm: true));

        sigDict.Set(PdfName.Intern("M"), new PdfString(FormatPdfDate(signingTime)));
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

        // ── Step 3: build the signature field ────────────────────────────
        PdfDictionary sigField = new();
        sigField.Set(PdfName.Intern("FT"), PdfName.Intern("Sig"));
        sigField.Set(PdfName.Intern("T"), new PdfString(options.SignatureFieldName));
        sigField.Set(PdfName.Intern("V"), new PdfReference(sigDictId));

        // ── Step 3b: visible appearance widget (when requested) ─────────
        // 3.31.2 (D-226): the same widget code as Sign. Before, SignAsync
        // skipped this step and options.Appearance was silently ignored.
        List<PdfIndirectObject> widgetObjects = new();
        PdfObjectId? targetPageId = null;
        PdfDictionary? clonedPage = null;
        if (options.Appearance is SignatureAppearance ap)
        {
            (targetPageId, clonedPage) = BuildAppearanceWidget(
                document, ap, sigField, sigFieldId, widgetObjects, acroFormId, signer.Certificate.Subject.CommonName, signingTime);
        }

        // ── Step 4: build (or extend) AcroForm ───────────────────────────
        PdfArray fields = new();
        // Preserve any existing AcroForm.Fields entries.
        if (document.Catalog.TryGetValue(PdfName.Intern("AcroForm"), out PdfPrimitive? acroFormVal))
        {
            PdfDictionary? existingAcro = document.Objects.ResolveAs<PdfDictionary>(acroFormVal);
            if (existingAcro is not null
                && existingAcro.TryGetValue(PdfName.Intern("Fields"), out PdfPrimitive? existingFieldsVal))
            {
                PdfArray? existingFields = document.Objects.ResolveAs<PdfArray>(existingFieldsVal);
                if (existingFields is not null)
                {
                    foreach (PdfPrimitive f in existingFields)
                    {
                        fields.Add(f);
                    }
                }
            }
        }
        fields.Add(new PdfReference(sigFieldId));

        PdfDictionary acroForm = new();
        acroForm.Set(PdfName.Intern("Fields"), fields);
        // SigFlags 3 = SignaturesExist (bit 0) + AppendOnly (bit 1).
        acroForm.Set(PdfName.Intern("SigFlags"), (PdfPrimitive)new PdfInteger(3));

        // ── Step 4b: build LTV objects (/DSS + /VRI) when requested ─────
        // Allocate IDs after acroFormId and after the appearance widget's
        // objects, which start at acroFormId + 1. We track the IDs into a
        // helper that builds and returns the new indirect objects + the
        // catalog's /DSS reference (or null when LTV isn't requested).
        // 3.31.2 (D-226): before, LTV also started at acroFormId + 1, so with
        // an Appearance the first LTV stream and the appearance XObject shared
        // an object number and one of them was lost.
        int nextLtvId = acroFormId.ObjectNumber + 1 + widgetObjects.Count;
        List<PdfIndirectObject> ltvObjects = new();
        PdfReference? dssRef = null;
        if (options.LtvOptions is { HasMaterial: true } ltv)
        {
            if (ltv.IncludeVri)
            {
                throw new NotSupportedException(
                    "/VRI emission is not yet supported in single-pass signing; see LtvOptions.IncludeVri docs.");
            }
            dssRef = BuildLtvObjects(ltv, ref nextLtvId, ltvObjects);
        }

        // ── Step 5: clone the catalog with an updated /AcroForm ─────────
        PdfDictionary newCatalog = new();
        foreach (KeyValuePair<PdfName, PdfPrimitive> kv in document.Catalog)
        {
            newCatalog.Set(kv.Key, kv.Value);
        }
        newCatalog.Set(PdfName.Intern("AcroForm"), new PdfReference(acroFormId));
        if (dssRef is not null)
        {
            newCatalog.Set(PdfName.Intern("DSS"), dssRef);
        }

        // Replace the original catalog object in our list. The catalog ID is
        // recovered from the trailer's /Root reference.
        PdfObjectId catalogId = ResolveCatalogId(document);
        bool catalogReplaced = false;
        for (int i = 0; i < objects.Count; i++)
        {
            if (objects[i].Id.Equals(catalogId))
            {
                objects[i] = new PdfIndirectObject(catalogId, newCatalog);
                catalogReplaced = true;
                break;
            }
        }
        if (!catalogReplaced)
        {
            objects.Add(new PdfIndirectObject(catalogId, newCatalog));
        }

        objects.Add(new PdfIndirectObject(sigDictId, sigDict));
        objects.Add(new PdfIndirectObject(sigFieldId, sigField));
        objects.Add(new PdfIndirectObject(acroFormId, acroForm));
        objects.AddRange(ltvObjects);
        objects.AddRange(widgetObjects);
        ReplacePage(objects, targetPageId, clonedPage);

        PdfDictionary trailer = new();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        // ── Step 6: write the PDF with placeholder ByteRange + zero /Contents ──
        MemoryStream ms = new();
        PdfWriter.Write(ms, objects.ToArray(), trailer);
        byte[] pdfBytes = ms.ToArray();

        // ── Step 7: locate /ByteRange and /Contents in the output ────────
        // 3.31.2 (D-207 item 1): search only inside the signature dictionary's
        // own object, found through the xref just written. Before, the first
        // /Contents in the file was taken, which is a page's on any real document.
        int sigObjectStart = SignatureContentsHelper.LocateObjectStart(pdfBytes, sigDictId.ObjectNumber);
        int byteRangeStart = SignatureContentsHelper.LocateByteRangeArrayStart(pdfBytes, sigObjectStart);
        if (byteRangeStart < 0)
        {
            throw new InvalidOperationException(
                "Could not locate /ByteRange in the signed PDF output.");
        }

        int contentsValueStart = SignatureContentsHelper.LocateContentsHexStart(pdfBytes, sigObjectStart);
        if (contentsValueStart < 0)
        {
            throw new InvalidOperationException(
                "Could not locate /Contents value in the signed PDF output.");
        }
        // /Contents value spans <HEX> where HEX has 2 * ContentsPlaceholderSize chars.
        int contentsValueEnd = contentsValueStart + 1 + (options.ContentsPlaceholderSize * 2) + 1;

        // ── Step 8: patch ByteRange with real positions ──────────────────
        int firstLen = contentsValueStart;
        int secondStart = contentsValueEnd;
        int secondLen = pdfBytes.Length - contentsValueEnd;
        PatchByteRange(pdfBytes, byteRangeStart, 0, firstLen, secondStart, secondLen);

        // ── Step 9: extract the byte-range-covered content and sign it ──
        byte[] toSign = new byte[firstLen + secondLen];
        Buffer.BlockCopy(pdfBytes, 0, toSign, 0, firstLen);
        Buffer.BlockCopy(pdfBytes, secondStart, toSign, firstLen, secondLen);

        byte[] cms;
        if (options.AsyncTsaClient is not null)
        {
            cms = await CmsSignedDataBuilder.BuildDetachedWithTimestampAsync(
                toSign, signer, options.AsyncTsaClient, signingTime, options.ExtraCertificates,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        else if (options.TsaClient is not null)
        {
            cms = CmsSignedDataBuilder.BuildDetachedWithTimestamp(
                toSign, signer, options.TsaClient, signingTime, options.ExtraCertificates);
        }
        else
        {
            cms = CmsSignedDataBuilder.BuildDetached(
                toSign, signer, signingTime, options.ExtraCertificates);
        }
        if (cms.Length > options.ContentsPlaceholderSize)
        {
            throw new InvalidOperationException(
                $"CMS signature ({cms.Length} bytes) exceeds /Contents placeholder size "
                + $"({options.ContentsPlaceholderSize} bytes). Increase ContentsPlaceholderSize.");
        }

        // ── Step 10: splice CMS into /Contents placeholder ──────────────
        WriteHexAt(pdfBytes, contentsValueStart + 1, cms);

        return pdfBytes;
    }

    /// <summary>Walks the /Pages tree, collecting page object IDs in document order.</summary>
    // Puts the cloned target page (the one that now lists the signature
    // widget in /Annots) in place of the original page object, or adds it.
    private static void ReplacePage(List<PdfIndirectObject> objects, PdfObjectId? pageId, PdfDictionary? clonedPage)
    {
        if (pageId is not PdfObjectId id || clonedPage is null)
        {
            return;
        }

        for (int i = 0; i < objects.Count; i++)
        {
            if (objects[i].Id.Equals(id))
            {
                objects[i] = new PdfIndirectObject(id, clonedPage);
                return;
            }
        }

        objects.Add(new PdfIndirectObject(id, clonedPage));
    }

    private static void CollectPageIds(
        PdfPrimitive node, Chuvadi.Pdf.Objects.PdfObjectStore objects, List<PdfObjectId> sink)
    {
        if (node is not PdfReference nodeRef) { return; }
        PdfDictionary? dict = objects.ResolveAs<PdfDictionary>(nodeRef);
        if (dict is null) { return; }
        if (dict.TryGetValue(PdfName.Type, out PdfPrimitive? typeVal)
            && typeVal is PdfName typeName
            && typeName.Value == "Page")
        {
            sink.Add(nodeRef.ObjectId);
            return;
        }
        if (dict.TryGetValue(PdfName.Kids, out PdfPrimitive? kidsVal))
        {
            PdfArray? kids = objects.ResolveAs<PdfArray>(kidsVal);
            if (kids is not null)
            {
                foreach (PdfPrimitive child in kids) { CollectPageIds(child, objects, sink); }
            }
        }
    }

    /// <summary>
    /// Mutates <paramref name="sigField"/> to act as a Widget annotation, and
    /// emits the supporting objects (Form XObject for the appearance, possibly
    /// a cloned page with an extended /Annots). The new objects are numbered
    /// from <paramref name="acroFormId"/> + 1. Shared with
    /// <see cref="PdfCounterSigner"/> since 3.31.2 (D-207 1b).
    /// </summary>
    internal static (PdfObjectId? pageId, PdfDictionary? clonedPage) BuildAppearanceWidget(
        PdfDocument document,
        SignatureAppearance ap,
        PdfDictionary sigField,
        PdfObjectId sigFieldId,
        List<PdfIndirectObject> widgetObjects,
        PdfObjectId acroFormId,
        string? signerName,
        DateTimeOffset signingTime)
    {
        // Resolve target page by walking the /Pages tree.
        List<PdfObjectId> pageIds = new();
        PdfDictionary catalogForPages = document.Catalog;
        if (catalogForPages.TryGetValue(PdfName.Pages, out PdfPrimitive? pagesVal))
        {
            CollectPageIds(pagesVal, document.Objects, pageIds);
        }
        if (ap.PageIndex < 0 || ap.PageIndex >= pageIds.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ap),
                $"Page index {ap.PageIndex} is out of range [0, {pageIds.Count}).");
        }
        PdfObjectId pageId = pageIds[ap.PageIndex];
        PdfDictionary page = document.Objects.ResolveAs<PdfDictionary>(new PdfReference(pageId))
            ?? throw new InvalidOperationException($"Could not resolve page {pageId}.");

        // Build appearance content stream. The appearance XObject is the first
        // new object (acroFormId + 1); any objects the default appearance
        // needs (fonts, image) follow it.
        int nextIdSeed = acroFormId.ObjectNumber + 1;
        PdfObjectId apnId = new(nextIdSeed++, 0);
        byte[] content;
        PdfDictionary apResources;
        IReadOnlyList<PdfIndirectObject> apObjects = Array.Empty<PdfIndirectObject>();
        if (ap.PreRenderedAppearanceStream is byte[] supplied)
        {
            content = supplied;
            apResources = new PdfDictionary();
        }
        else
        {
            // 3.32.0 (D-208 1c): text, an optional image and the border.
            // Before, the default was the border alone.
            SignatureAppearanceBuilder.Result built = SignatureAppearanceBuilder.Build(
                document.Objects,
                ap,
                signerName,
                signingTime,
                ap.Rectangle[2] - ap.Rectangle[0],
                ap.Rectangle[3] - ap.Rectangle[1],
                nextIdSeed);
            content = built.Content;
            apResources = built.Resources;
            apObjects = built.Objects;
        }

        // Form XObject for the appearance.
        PdfDictionary apnDict = new();
        apnDict.Set(PdfName.Type, PdfName.Intern("XObject"));
        apnDict.Set(PdfName.Intern("Subtype"), PdfName.Intern("Form"));
        apnDict.Set(PdfName.Intern("FormType"), (PdfPrimitive)new PdfInteger(1));
        PdfArray bbox = new();
        bbox.Add(new PdfReal(0));
        bbox.Add(new PdfReal(0));
        bbox.Add(new PdfReal(ap.Rectangle[2] - ap.Rectangle[0]));
        bbox.Add(new PdfReal(ap.Rectangle[3] - ap.Rectangle[1]));
        apnDict.Set(PdfName.Intern("BBox"), bbox);
        apnDict.Set(PdfName.Intern("Resources"), apResources);
        apnDict.Set(PdfName.Length, content.Length);
        widgetObjects.Add(new PdfIndirectObject(apnId, new PdfStream(apnDict, content)));
        widgetObjects.AddRange(apObjects);

        // /AP dictionary referencing /N.
        PdfDictionary apDict = new();
        apDict.Set(PdfName.Intern("N"), new PdfReference(apnId));

        // Make the sig field a Widget annotation.
        sigField.Set(PdfName.Intern("Subtype"), PdfName.Intern("Widget"));
        sigField.Set(PdfName.Intern("F"), (PdfPrimitive)new PdfInteger(4));   // /F Print
        PdfArray rect = new();
        foreach (double v in ap.Rectangle) { rect.Add(new PdfReal(v)); }
        sigField.Set(PdfName.Intern("Rect"), rect);
        sigField.Set(PdfName.Intern("P"), new PdfReference(pageId));
        sigField.Set(PdfName.Intern("AP"), apDict);

        // Clone the page dict with the sig field appended to /Annots.
        PdfDictionary clonedPage = new();
        foreach (KeyValuePair<PdfName, PdfPrimitive> kv in page)
        {
            clonedPage.Set(kv.Key, kv.Value);
        }
        PdfArray newAnnots = new();
        if (page.TryGetValue(PdfName.Intern("Annots"), out PdfPrimitive? existingAnnotsVal))
        {
            PdfArray? existing = document.Objects.ResolveAs<PdfArray>(existingAnnotsVal);
            if (existing is not null)
            {
                foreach (PdfPrimitive a in existing) { newAnnots.Add(a); }
            }
        }
        newAnnots.Add(new PdfReference(sigFieldId));
        clonedPage.Set(PdfName.Intern("Annots"), newAnnots);

        return (pageId, clonedPage);
    }

    private static PdfReference BuildLtvObjects(
        LtvOptions ltv, ref int nextId, List<PdfIndirectObject> objects)
    {
        // Allocate stream objects for every cert / CRL / OCSP, and gather
        // their PdfReferences into per-kind arrays for the /DSS dictionary.
        PdfArray certsArray = new();
        if (ltv.Certificates is not null)
        {
            foreach (X509Certificate cert in ltv.Certificates)
            {
                certsArray.Add(NewStreamRef(cert.RawEncoding, ref nextId, objects));
            }
        }
        PdfArray crlsArray = new();
        if (ltv.Crls is not null)
        {
            foreach (CertificateList crl in ltv.Crls)
            {
                crlsArray.Add(NewStreamRef(crl.RawEncoding, ref nextId, objects));
            }
        }
        PdfArray ocspArray = new();
        if (ltv.OcspResponses is not null)
        {
            foreach (OcspResponse ocsp in ltv.OcspResponses)
            {
                ocspArray.Add(NewStreamRef(ocsp.RawEncoding, ref nextId, objects));
            }
        }

        PdfDictionary dss = new();
        dss.Set(PdfName.Type, PdfName.Intern("DSS"));
        if (certsArray.Count > 0) { dss.Set(PdfName.Intern("Certs"), certsArray); }
        if (crlsArray.Count > 0) { dss.Set(PdfName.Intern("CRLs"), crlsArray); }
        if (ocspArray.Count > 0) { dss.Set(PdfName.Intern("OCSPs"), ocspArray); }

        PdfObjectId dssId = new(nextId++, 0);
        objects.Add(new PdfIndirectObject(dssId, dss));
        return new PdfReference(dssId);
    }

    private static PdfReference NewStreamRef(
        byte[] data, ref int nextId, List<PdfIndirectObject> objects)
    {
        PdfDictionary dict = new();
        dict.Set(PdfName.Length, data.Length);
        PdfObjectId id = new(nextId++, 0);
        objects.Add(new PdfIndirectObject(id, new PdfStream(dict, data)));
        return new PdfReference(id);
    }


    private static void PatchByteRange(
        byte[] bytes,
        int arrayStart,
        int v0, int v1, int v2, int v3)
    {
        // The array opens with '[' at arrayStart. Inside it we have:
        // [v0(10) ' ' v1(10) ' ' v2(10) ' ' v3(10)]
        int p = arrayStart;
        if (bytes[p] != (byte)'[')
        {
            throw new InvalidOperationException(
                $"Expected '[' at byte {p}, got 0x{bytes[p]:X2}.");
        }
        p++;
        WriteIntPadded(bytes, p, v0, ByteRangeSlotWidth); p += ByteRangeSlotWidth;
        if (bytes[p] != (byte)' ')
        {
            throw new InvalidOperationException(
                $"Expected separator ' ' at byte {p}, got 0x{bytes[p]:X2}.");
        }
        p++;
        WriteIntPadded(bytes, p, v1, ByteRangeSlotWidth); p += ByteRangeSlotWidth;
        p++; // skip space
        WriteIntPadded(bytes, p, v2, ByteRangeSlotWidth); p += ByteRangeSlotWidth;
        p++; // skip space
        WriteIntPadded(bytes, p, v3, ByteRangeSlotWidth); p += ByteRangeSlotWidth;
        if (bytes[p] != (byte)']')
        {
            throw new InvalidOperationException(
                $"Expected ']' at byte {p}, got 0x{bytes[p]:X2}.");
        }
    }

    private static void WriteIntPadded(byte[] bytes, int offset, int value, int width)
    {
        if (value < 0) { throw new ArgumentOutOfRangeException(nameof(value)); }
        for (int i = width - 1; i >= 0; i--)
        {
            bytes[offset + i] = (byte)('0' + (value % 10));
            value /= 10;
        }
        if (value != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value),
                $"Value does not fit in {width} digits.");
        }
    }

    private static void WriteHexAt(byte[] bytes, int offset, byte[] data)
    {
        const string HexDigits = "0123456789ABCDEF";
        for (int i = 0; i < data.Length; i++)
        {
            bytes[offset + i * 2] = (byte)HexDigits[data[i] >> 4];
            bytes[offset + i * 2 + 1] = (byte)HexDigits[data[i] & 0x0F];
        }
        // Trailing bytes in the placeholder remain '0' (the placeholder was zeroed).
    }

    // 3.31.2 (D-207 item 1): Sign rewrites the whole file, which would break
    // every existing signature, so signed input is refused.
    private static void ThrowIfAlreadySigned(PdfDocument document)
    {
        if (document.Signatures().Count > 0)
        {
            throw new InvalidOperationException(
                "This document already has a signature; use PdfCounterSigner.AddSignature to add another.");
        }
    }

    private static int ReadTrailerSize(PdfDocument document)
    {
        if (document.Trailer.TryGetValue(PdfName.Intern("Size"), out PdfPrimitive? sizeVal)
            && sizeVal is PdfInteger sizeInt && sizeInt.Value > 0)
        {
            return sizeInt.Value;
        }
        // Fallback: probe upward until we find a few consecutive empty IDs.
        // /Size is mandatory per ISO 32000-2 §7.5.5 so this is defensive only.
        return 1;
    }

    private static PdfObjectId ResolveCatalogId(PdfDocument document)
    {
        if (!document.Trailer.TryGetValue(PdfName.Root, out PdfPrimitive? root)
            || root is not PdfReference rootRef)
        {
            throw new InvalidOperationException(
                "Document trailer has no /Root reference; cannot identify the catalog.");
        }
        return rootRef.ObjectId;
    }

    /// <summary>
    /// Formats a <see cref="DateTimeOffset"/> as a PDF date string per
    /// ISO 32000-2 §7.9.4: <c>D:YYYYMMDDHHmmSSZ</c> (always UTC).
    /// </summary>
    private static string FormatPdfDate(DateTimeOffset time)
    {
        DateTimeOffset u = time.ToUniversalTime();
        return $"D:{u.Year:D4}{u.Month:D2}{u.Day:D2}{u.Hour:D2}{u.Minute:D2}{u.Second:D2}Z";
    }
}
