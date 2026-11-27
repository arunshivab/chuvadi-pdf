// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.8.1 (byte range), §7.5.6 (incremental updates),
//        ISO 32000-2 §12.8.4.3 (DSS)
// PHASE: 3.35.0 (D-396, audit A-09) — detect changes after the signed bytes.

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Signatures.Verification;

/// <summary>
/// Compares the revision a signature covers with the whole file. A change is
/// allowed only when it adds later signatures or validation data: new
/// signature fields, their widgets and appearances, signature values on
/// fields that were empty when signed, and the <c>/DSS</c> dictionary.
/// Anything else, or anything that cannot be analysed, is
/// <see cref="SignatureModifications.Other"/> (fail closed).
/// </summary>
internal static class SignatureModificationAnalyzer
{
    private const int MaxDepth = 64;

    private static readonly PdfName Ft = PdfName.Intern("FT");
    private static readonly PdfName Sig = PdfName.Intern("Sig");
    private static readonly PdfName V = PdfName.Intern("V");
    private static readonly PdfName Ap = PdfName.Intern("AP");
    private static readonly PdfName Kids = PdfName.Intern("Kids");
    private static readonly PdfName Fields = PdfName.Intern("Fields");
    private static readonly PdfName AcroForm = PdfName.Intern("AcroForm");
    private static readonly PdfName Dss = PdfName.Intern("DSS");
    private static readonly PdfName Annots = PdfName.Intern("Annots");
    private static readonly PdfName Parent = PdfName.Intern("Parent");

    private static readonly HashSet<string> CatalogKeys = new HashSet<string>(StringComparer.Ordinal) { "DSS", "AcroForm", "Extensions" };
    private static readonly HashSet<string> AcroFormKeys = new HashSet<string>(StringComparer.Ordinal) { "Fields", "SigFlags" };
    private static readonly HashSet<string> PageKeys = new HashSet<string>(StringComparer.Ordinal) { "Annots" };
    private static readonly HashSet<string> SignedFieldKeys = new HashSet<string>(StringComparer.Ordinal) { "V", "AP", "AS" };
    private static readonly HashSet<string> WidgetKeys = new HashSet<string>(StringComparer.Ordinal) { "AP", "AS" };

    internal readonly record struct Outcome(SignatureModifications Kind, bool CoversWholeDocument, string Message);

    internal static Outcome Analyze(PdfSignature signature, PdfDocument document)
    {
        ByteRange range = signature.ByteRange;
        long fileLength = document.Reader.FileLength;
        long signedEnd = range.SecondOffset + range.SecondLength;
        if (range.FirstOffset != 0 || range.FirstLength <= 0 || range.SecondOffset < range.GapOffset
            || range.SecondLength < 0 || signedEnd > fileLength)
        {
            return Other(false, "The signature's /ByteRange does not run from the start of the file over the signed revision, so part of what is shown is not signed.");
        }

        if (!GapIsSignatureValue(document, range))
        {
            return Other(false, "The gap in the signature's /ByteRange holds more than the signature value, so part of the signed revision is not signed.");
        }

        if (signedEnd == fileLength || OnlyWhiteSpace(document, signedEnd, fileLength - signedEnd))
        {
            return new Outcome(SignatureModifications.None, true, "The signature covers the whole file.");
        }

        if (fileLength > int.MaxValue)
        {
            return Other(false, "The file was extended after signing, and it is too large to compare the revisions.");
        }

        byte[] whole = document.Reader.ReadFileBytes(0, (int)fileLength);
        XrefTable wholeXref;
        XrefTable signedXref;
        PdfDocument signedRevision;
        try
        {
            wholeXref = PdfReader.LoadXref(new MemoryStream(whole, writable: false));
            byte[] prefix = new byte[signedEnd];
            Buffer.BlockCopy(whole, 0, prefix, 0, (int)signedEnd);
            signedXref = PdfReader.LoadXref(new MemoryStream(prefix, writable: false));
            signedRevision = PdfDocument.Open(new MemoryStream(prefix, writable: false), leaveOpen: false);
        }
        catch (Exception ex) when (ex is PdfException or IOException or InvalidOperationException or ArgumentException)
        {
            return Other(false, "The file was changed after signing, and the signed revision could not be read to compare it (" + ex.Message + ").");
        }

        using (signedRevision)
        {
            try
            {
                string? problem = FindDisallowedChange(document, signedRevision, wholeXref, signedXref);
                return problem is null
                    ? new Outcome(SignatureModifications.SignaturesOnly, false, "After signing, only later signatures or validation data were added.")
                    : Other(false, "The file was changed after signing: " + problem);
            }
            catch (Exception ex) when (ex is PdfException or IOException or InvalidOperationException or ArgumentException)
            {
                return Other(false, "The file was changed after signing, and the change could not be analysed (" + ex.Message + ").");
            }
        }
    }

    private static Outcome Other(bool covers, string message) => new Outcome(SignatureModifications.Other, covers, message);

    // The excluded gap must hold the hex string of /Contents and nothing else:
    // either the whole <...> literal, or its digits between a < and > that are
    // themselves signed.
    private static bool GapIsSignatureValue(PdfDocument document, ByteRange range)
    {
        long gapLength = range.GapLength;
        if (gapLength <= 0 || gapLength > int.MaxValue)
        {
            return false;
        }

        byte[] gap = document.Reader.ReadFileBytes(range.GapOffset, (int)gapLength);
        int start = 0;
        int end = gap.Length;
        if (gap[0] == (byte)'<' && gap[^1] == (byte)'>')
        {
            start = 1;
            end = gap.Length - 1;
        }
        else
        {
            byte[] before = document.Reader.ReadFileBytes(range.GapOffset - 1, 1);
            byte[] after = document.Reader.ReadFileBytes(range.SecondOffset, 1);
            if (before.Length != 1 || after.Length != 1 || before[0] != (byte)'<' || after[0] != (byte)'>')
            {
                return false;
            }
        }

        for (int i = start; i < end; i++)
        {
            byte b = gap[i];
            bool hex = b is >= (byte)'0' and <= (byte)'9' or >= (byte)'a' and <= (byte)'f' or >= (byte)'A' and <= (byte)'F';
            if (!hex && !IsWhiteSpace(b))
            {
                return false;
            }
        }

        return true;
    }

    private static bool OnlyWhiteSpace(PdfDocument document, long offset, long count)
    {
        if (count > 4096)
        {
            return false;
        }

        foreach (byte b in document.Reader.ReadFileBytes(offset, (int)count))
        {
            if (!IsWhiteSpace(b))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsWhiteSpace(byte b) => b is 0 or 9 or 10 or 12 or 13 or 32;

    // Returns a description of the first change that is not a later
    // signature or validation data, or null when every change is allowed.
    private static string? FindDisallowedChange(PdfDocument whole, PdfDocument signed, XrefTable wholeXref, XrefTable signedXref)
    {
        PdfObjectStore now = whole.Objects;
        PdfObjectStore then = signed.Objects;

        // Objects that signatures and validation data may add.
        HashSet<int> signatureFields = new HashSet<int>();
        HashSet<int> signatureWidgets = new HashSet<int>();
        HashSet<int> addedSignatureParts = new HashSet<int>();
        CollectSignatureParts(now, whole.Catalog, signatureFields, signatureWidgets, addedSignatureParts);
        if (whole.Catalog.TryGetValue(Dss, out PdfPrimitive? dss) && dss is not null)
        {
            CollectReachable(now, dss, addedSignatureParts, 0);
        }

        // SIG-12: a signature or validation part is legitimately new in the
        // update. An object the signed revision already reaches — pointed at by
        // a new /DSS or a new signature field's /V or /AP — is a smuggled
        // change, not a part. Dropping those from the admitted set makes a
        // later change to them (replaced page content, a rebuilt page tree)
        // report as a modification instead of being waved through. New parts
        // have new object numbers, which the signed revision cannot reach, so
        // they survive; the legitimate fill-of-an-empty-field and LTV/DSS cases
        // keep their own object-number branches below.
        HashSet<int> signedReachable = ReachableFromTrailer(signed);
        addedSignatureParts.ExceptWith(signedReachable);

        int catalogNumber = whole.Trailer.TryGetValue(PdfName.Intern("Root"), out PdfPrimitive? root) && root is PdfReference rootRef
            ? rootRef.ObjectNumber
            : -1;

        foreach (XrefEntry entry in signedXref.Entries)
        {
            if (!entry.IsFree && wholeXref.TryGet(entry.ObjectNumber, out XrefEntry later) && later.IsFree)
            {
                return "object " + entry.ObjectNumber + " was deleted.";
            }
        }

        // Changed objects first: allowed changes (a catalog that points at a
        // new form, a page that points at a new /Annots array) may admit added
        // objects, which are checked after.
        List<XrefEntry> addedEntries = new List<XrefEntry>();
        foreach (XrefEntry entry in wholeXref.Entries)
        {
            if (entry.IsFree)
            {
                continue;
            }

            bool existed = signedXref.TryGet(entry.ObjectNumber, out XrefEntry before) && !before.IsFree;
            if (existed && before == entry)
            {
                continue;
            }

            if (!existed)
            {
                addedEntries.Add(entry);
                continue;
            }

            PdfPrimitive current = now.ResolveById(entry.ObjectId);
            PdfPrimitive previous = then.ResolveById(before.ObjectId);
            if (DeepEquals(previous, current, 0) || (current is PdfStream stream && IsXrefOrObjectStream(stream)))
            {
                continue;
            }

            string? problem = CheckChangedObject(
                entry.ObjectNumber, previous, current, catalogNumber, signatureFields, signatureWidgets, addedSignatureParts, then, now);
            if (problem is not null)
            {
                return problem;
            }
        }

        // An added object nothing reaches from the trailer cannot change what
        // is shown; one that is reached and not admitted can (for example a
        // reference the signed revision left dangling, now filled in).
        HashSet<int>? reachable = null;
        foreach (XrefEntry entry in addedEntries)
        {
            PdfPrimitive current = now.ResolveById(entry.ObjectId);
            if ((current is PdfStream stream && IsXrefOrObjectStream(stream)) || addedSignatureParts.Contains(entry.ObjectNumber))
            {
                continue;
            }

            reachable ??= ReachableFromTrailer(whole);
            if (!reachable.Contains(entry.ObjectNumber))
            {
                continue;
            }

            return "object " + entry.ObjectNumber + " was added, and it is not part of a signature or of validation data.";
        }

        return null;
    }

    private static string? CheckChangedObject(
        int number, PdfPrimitive previous, PdfPrimitive current, int catalogNumber,
        HashSet<int> signatureFields, HashSet<int> signatureWidgets, HashSet<int> added, PdfObjectStore then, PdfObjectStore now)
    {
        string where = "object " + number;
        if (previous is PdfDictionary before && current is PdfDictionary after && previous is not PdfStream && current is not PdfStream)
        {
            HashSet<string> changed = ChangedKeys(before, after);
            if (number == catalogNumber)
            {
                return Within(changed, CatalogKeys) && AcroFormChangeAllowed(before, after, signatureFields, then, now, added)
                    ? null
                    : where + " (the document catalog) changed in " + string.Join(", ", changed) + ".";
            }

            if (IsName(after, PdfName.Type, "Page"))
            {
                return Within(changed, PageKeys) && ArrayGrowthAllowed(before, after, Annots, signatureWidgets, then, now, added)
                    ? null
                    : where + " (a page) changed in " + string.Join(", ", changed) + ".";
            }

            if (after.ContainsKey(Fields) && before.ContainsKey(Fields))
            {
                return Within(changed, AcroFormKeys) && ArrayGrowthAllowed(before, after, Fields, signatureFields, then, now, added)
                    ? null
                    : where + " (the form) changed in " + string.Join(", ", changed) + ".";
            }

            if (signatureFields.Contains(number) && !before.ContainsKey(V))
            {
                return Within(changed, SignedFieldKeys)
                    ? null
                    : where + " (an empty signature field) changed in " + string.Join(", ", changed) + ".";
            }

            if (signatureWidgets.Contains(number) && FieldWasEmpty(before, then))
            {
                return Within(changed, WidgetKeys)
                    ? null
                    : where + " (a signature widget) changed in " + string.Join(", ", changed) + ".";
            }

            if (added.Contains(number) && IsValidationData(after))
            {
                return null;
            }

            return where + " changed in " + string.Join(", ", changed) + ".";
        }

        if (previous is PdfArray oldArray && current is PdfArray newArray)
        {
            HashSet<int> allowedNew = new HashSet<int>(signatureFields);
            allowedNew.UnionWith(signatureWidgets);
            return GrowthOnly(oldArray, newArray, allowedNew) ? null : where + " (an array) changed.";
        }

        if (added.Contains(number) && current is PdfStream)
        {
            // A /DSS stream or a new appearance rewritten in place: only when
            // it was already part of the validation data.
            return null;
        }

        return where + " changed.";
    }

    // A widget's field (itself, or its /Parent) had no /V when signed.
    private static bool FieldWasEmpty(PdfDictionary widgetBefore, PdfObjectStore then)
    {
        if (widgetBefore.ContainsKey(Ft))
        {
            return !widgetBefore.ContainsKey(V);
        }

        return widgetBefore.TryGetValue(Parent, out PdfPrimitive? parent) && parent is not null
            && then.Resolve(parent) is PdfDictionary field && !field.ContainsKey(V);
    }

    private static bool IsValidationData(PdfDictionary dict) =>
        dict.ContainsKey(PdfName.Intern("Certs")) || dict.ContainsKey(PdfName.Intern("CRLs"))
        || dict.ContainsKey(PdfName.Intern("OCSPs")) || dict.ContainsKey(PdfName.Intern("VRI"))
        || IsName(dict, PdfName.Type, "DSS") || IsName(dict, PdfName.Type, "VRI");

    // The catalog's /AcroForm may stay, appear with only signature fields, or
    // point at a new form object that keeps the old fields and adds signature
    // fields (that object is then admitted).
    private static bool AcroFormChangeAllowed(
        PdfDictionary catalogBefore, PdfDictionary catalogAfter, HashSet<int> signatureFields,
        PdfObjectStore then, PdfObjectStore now, HashSet<int> added)
    {
        PdfPrimitive? before = catalogBefore.TryGetValue(AcroForm, out PdfPrimitive? b) ? b : null;
        PdfPrimitive? after = catalogAfter.TryGetValue(AcroForm, out PdfPrimitive? a) ? a : null;
        if (after is null)
        {
            return before is null;
        }

        if (before is not null && DeepEquals(before, after, 0))
        {
            return true;
        }

        if (now.Resolve(after) is not PdfDictionary newForm)
        {
            return false;
        }

        bool allowed;
        if (before is null)
        {
            allowed = OnlySignatureFields(newForm, signatureFields, now);
        }
        else
        {
            allowed = then.Resolve(before) is PdfDictionary oldForm
                && Within(ChangedKeys(oldForm, newForm), AcroFormKeys)
                && ArrayGrowthAllowed(oldForm, newForm, Fields, signatureFields, then, now, added);
        }

        if (allowed && after is PdfReference newRef)
        {
            added.Add(newRef.ObjectNumber);
        }

        return allowed;
    }

    private static bool OnlySignatureFields(PdfDictionary form, HashSet<int> signatureFields, PdfObjectStore now)
    {
        if (!form.TryGetValue(Fields, out PdfPrimitive? fields) || fields is null)
        {
            return true;
        }

        if (now.Resolve(fields) is not PdfArray array)
        {
            return false;
        }

        foreach (PdfPrimitive item in array)
        {
            if (item is not PdfReference r || !signatureFields.Contains(r.ObjectNumber))
            {
                return false;
            }
        }

        return true;
    }

    // The key's array (direct, or indirect in a new object, which is then
    // admitted) keeps every old element and only gains allowed references.
    // An indirect array kept under the same number is checked as its own
    // object.
    private static bool ArrayGrowthAllowed(
        PdfDictionary before, PdfDictionary after, PdfName key, HashSet<int> allowed,
        PdfObjectStore then, PdfObjectStore now, HashSet<int> added)
    {
        PdfPrimitive? oldValue = before.TryGetValue(key, out PdfPrimitive? o) ? o : null;
        PdfPrimitive? newValue = after.TryGetValue(key, out PdfPrimitive? n) ? n : null;
        if (newValue is null)
        {
            return oldValue is null;
        }

        if (oldValue is PdfReference oldRef && newValue is PdfReference sameRef && oldRef.ObjectId == sameRef.ObjectId)
        {
            return true;
        }

        if (now.Resolve(newValue) is not PdfArray newArray)
        {
            return false;
        }

        PdfArray oldArray = oldValue is null
            ? new PdfArray()
            : then.Resolve(oldValue) as PdfArray ?? new PdfArray();
        if (oldValue is not null && then.Resolve(oldValue) is not PdfArray)
        {
            return false;
        }

        if (!GrowthOnly(oldArray, newArray, allowed))
        {
            return false;
        }

        if (newValue is PdfReference newRef)
        {
            added.Add(newRef.ObjectNumber);
        }

        return true;
    }

    // Every old element is kept, in order, and every added one is a
    // reference to an allowed object.
    private static bool GrowthOnly(PdfArray before, PdfArray after, HashSet<int> allowed)
    {
        int j = 0;
        for (int i = 0; i < after.Count; i++)
        {
            if (j < before.Count && DeepEquals(before[j], after[i], 0))
            {
                j++;
                continue;
            }

            if (after[i] is not PdfReference r || !allowed.Contains(r.ObjectNumber))
            {
                return false;
            }
        }

        return j == before.Count;
    }

    private static bool Within(HashSet<string> changed, HashSet<string> allowed)
    {
        foreach (string key in changed)
        {
            if (!allowed.Contains(key))
            {
                return false;
            }
        }

        return true;
    }

    private static HashSet<string> ChangedKeys(PdfDictionary before, PdfDictionary after)
    {
        HashSet<string> changed = new HashSet<string>(StringComparer.Ordinal);
        foreach (PdfName key in after.Keys)
        {
            if (!before.TryGetValue(key, out PdfPrimitive? old) || old is null
                || !after.TryGetValue(key, out PdfPrimitive? value) || value is null
                || !DeepEquals(old, value, 0))
            {
                changed.Add(key.Value);
            }
        }

        foreach (PdfName key in before.Keys)
        {
            if (!after.ContainsKey(key))
            {
                changed.Add(key.Value);
            }
        }

        return changed;
    }

    private static bool IsName(PdfDictionary dict, PdfName key, string value) =>
        dict.TryGetValue(key, out PdfPrimitive? v) && v is PdfName name && name.Value == value;

    private static bool IsXrefOrObjectStream(PdfStream stream) =>
        IsName(stream.Dictionary, PdfName.Type, "XRef") || IsName(stream.Dictionary, PdfName.Type, "ObjStm");

    // Signature fields (FT Sig, own or inherited) in the field tree, their
    // widgets, and what their values and appearances reach.
    private static void CollectSignatureParts(
        PdfObjectStore store, PdfDictionary catalog, HashSet<int> fields, HashSet<int> widgets, HashSet<int> parts)
    {
        if (!catalog.TryGetValue(AcroForm, out PdfPrimitive? formValue) || formValue is null
            || store.Resolve(formValue) is not PdfDictionary form
            || !form.TryGetValue(Fields, out PdfPrimitive? fieldsValue) || fieldsValue is null
            || store.Resolve(fieldsValue) is not PdfArray top)
        {
            return;
        }

        if (fieldsValue is PdfReference fieldsRef)
        {
            parts.Add(fieldsRef.ObjectNumber);
        }

        HashSet<int> visited = new HashSet<int>();
        foreach (PdfPrimitive item in top)
        {
            WalkField(store, item, false, fields, widgets, parts, visited, 0);
        }
    }

    private static void WalkField(
        PdfObjectStore store, PdfPrimitive item, bool inheritedSig, HashSet<int> fields, HashSet<int> widgets,
        HashSet<int> parts, HashSet<int> visited, int depth)
    {
        if (depth > MaxDepth)
        {
            return;
        }

        int number = item is PdfReference r ? r.ObjectNumber : -1;
        if (number >= 0 && !visited.Add(number))
        {
            return;
        }

        if (store.Resolve(item) is not PdfDictionary field)
        {
            return;
        }

        bool isSig = field.TryGetValue(Ft, out PdfPrimitive? ft) && ft is PdfName name ? name.Value == Sig.Value : inheritedSig;
        if (isSig && number >= 0)
        {
            fields.Add(number);
            widgets.Add(number);
            parts.Add(number);
            if (field.TryGetValue(V, out PdfPrimitive? value) && value is PdfReference valueRef)
            {
                parts.Add(valueRef.ObjectNumber);
            }

            if (field.TryGetValue(Ap, out PdfPrimitive? ap) && ap is not null)
            {
                CollectReachable(store, ap, parts, 0);
            }
        }

        if (field.TryGetValue(Kids, out PdfPrimitive? kidsValue) && kidsValue is not null && store.Resolve(kidsValue) is PdfArray kids)
        {
            if (isSig && kidsValue is PdfReference kidsRef)
            {
                parts.Add(kidsRef.ObjectNumber);
            }

            foreach (PdfPrimitive kid in kids)
            {
                if (isSig && kid is PdfReference kidRef && store.Resolve(kid) is PdfDictionary kidDict && !kidDict.ContainsKey(Ft))
                {
                    widgets.Add(kidRef.ObjectNumber);
                    parts.Add(kidRef.ObjectNumber);
                    if (kidDict.TryGetValue(Ap, out PdfPrimitive? kidAp) && kidAp is not null)
                    {
                        CollectReachable(store, kidAp, parts, 0);
                    }
                }

                WalkField(store, kid, isSig, fields, widgets, parts, visited, depth + 1);
            }
        }
    }

    private static HashSet<int> ReachableFromTrailer(PdfDocument document)
    {
        HashSet<int> seen = new HashSet<int>();
        Stack<PdfPrimitive> pending = new Stack<PdfPrimitive>();
        pending.Push(document.Trailer);
        while (pending.Count > 0)
        {
            PdfWorkScope.ThrowIfCancellationRequested();
            PdfPrimitive value = pending.Pop();
            switch (value)
            {
                case PdfReference r:
                    if (seen.Add(r.ObjectNumber))
                    {
                        pending.Push(document.Objects.Resolve(r));
                    }

                    break;
                case PdfStream stream:
                    pending.Push(stream.Dictionary);
                    break;
                case PdfDictionary dict:
                    foreach (PdfName key in dict.Keys)
                    {
                        if (dict.TryGetValue(key, out PdfPrimitive? child) && child is not null)
                        {
                            pending.Push(child);
                        }
                    }

                    break;
                case PdfArray array:
                    foreach (PdfPrimitive child in array)
                    {
                        pending.Push(child);
                    }

                    break;
            }
        }

        return seen;
    }

    private static void CollectReachable(PdfObjectStore store, PdfPrimitive value, HashSet<int> into, int depth)
    {
        if (depth > MaxDepth)
        {
            return;
        }

        if (value is PdfReference r)
        {
            if (!into.Add(r.ObjectNumber))
            {
                return;
            }

            CollectReachable(store, store.Resolve(r), into, depth + 1);
            return;
        }

        if (value is PdfStream stream)
        {
            CollectReachable(store, stream.Dictionary, into, depth + 1);
            return;
        }

        if (value is PdfDictionary dict)
        {
            foreach (PdfName key in dict.Keys)
            {
                if (key.Value is "Parent" or "P")
                {
                    continue;
                }

                if (dict.TryGetValue(key, out PdfPrimitive? child) && child is not null)
                {
                    CollectReachable(store, child, into, depth + 1);
                }
            }

            return;
        }

        if (value is PdfArray array)
        {
            foreach (PdfPrimitive child in array)
            {
                CollectReachable(store, child, into, depth + 1);
            }
        }
    }

    // Structural equality; references compare by object number and
    // generation, streams by dictionary and raw bytes.
    internal static bool DeepEquals(PdfPrimitive? a, PdfPrimitive? b, int depth)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is null || b is null || depth > MaxDepth)
        {
            return false;
        }

        switch (a)
        {
            case PdfReference ra:
                return b is PdfReference rb && ra.ObjectId == rb.ObjectId;
            case PdfStream sa:
                return b is PdfStream sb && DeepEquals(sa.Dictionary, sb.Dictionary, depth + 1) && sa.RawSpan.SequenceEqual(sb.RawSpan);
            case PdfDictionary da:
                if (b is not PdfDictionary db || b is PdfStream || da.Count != db.Count)
                {
                    return false;
                }

                foreach (PdfName key in da.Keys)
                {
                    if (!db.TryGetValue(key, out PdfPrimitive? other) || !da.TryGetValue(key, out PdfPrimitive? mine)
                        || !DeepEquals(mine, other, depth + 1))
                    {
                        return false;
                    }
                }

                return true;
            case PdfArray aa:
                if (b is not PdfArray ab || aa.Count != ab.Count)
                {
                    return false;
                }

                for (int i = 0; i < aa.Count; i++)
                {
                    if (!DeepEquals(aa[i], ab[i], depth + 1))
                    {
                        return false;
                    }
                }

                return true;
            case PdfString sa2:
                return b is PdfString sb2 && sa2.Bytes.AsSpan().SequenceEqual(sb2.Bytes);
            case PdfName na:
                return b is PdfName nb && na.Value == nb.Value;
            case PdfInteger ia:
                return (b is PdfInteger ib && ia.Value == ib.Value) || (b is PdfReal rb2 && ia.Value == rb2.Value);
            case PdfReal fa:
                return (b is PdfReal fb && fa.Value == fb.Value) || (b is PdfInteger ib2 && fa.Value == ib2.Value);
            case PdfBoolean ba:
                return b is PdfBoolean bb && ba.Value == bb.Value;
            default:
                return a.GetType() == b.GetType() && a.ToString() == b.ToString();
        }
    }
}
