// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.7.2 (interactive form dictionary),
//        §12.7.3.1 (field dictionaries, inheritable entries), §12.7.3.3 (/DA)
// PHASE: 3.34.0 (D-335) — PageOperations.Merge carries the inputs' form
//        fields (MergeOptions.FormFields).

using System;
using System.Collections.Generic;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// The form part of one merge input: its interactive form dictionary and the
/// objects of its field tree (fields, widgets, and what they reference).
/// </summary>
internal sealed class FormSource
{
    internal FormSource(int docIndex, PdfDictionary acroForm, List<PdfIndirectObject> objects)
    {
        DocIndex = docIndex;
        AcroForm = acroForm;
        Objects = objects;
    }

    internal int DocIndex { get; }

    internal PdfDictionary AcroForm { get; }

    internal List<PdfIndirectObject> Objects { get; }
}

/// <summary>
/// Builds the merged <c>/AcroForm</c> from the inputs' forms after their
/// objects are copied: field names that clash are renamed or shared,
/// <c>/DR</c> is merged (clashing resource names renamed and every
/// <c>/DA</c> rewritten to match), widgets point at their merged page,
/// <c>NeedAppearances</c> and <c>SigFlags</c> are OR'd, <c>/CO</c> is
/// concatenated, and XFA is dropped.
/// </summary>
internal static class MergedAcroForm
{
    private static readonly PdfName Fields = PdfName.Intern("Fields");
    private static readonly PdfName Kids = PdfName.Intern("Kids");
    private static readonly PdfName T = PdfName.Intern("T");
    private static readonly PdfName FT = PdfName.Intern("FT");
    private static readonly PdfName DA = PdfName.Intern("DA");
    private static readonly PdfName Q = PdfName.Intern("Q");
    private static readonly PdfName DR = PdfName.Intern("DR");
    private static readonly PdfName CO = PdfName.Intern("CO");
    private static readonly PdfName P = PdfName.Intern("P");
    private static readonly PdfName Subtype = PdfName.Intern("Subtype");
    private static readonly PdfName Widget = PdfName.Intern("Widget");
    private static readonly PdfName Rect = PdfName.Intern("Rect");
    private static readonly PdfName NeedAppearances = PdfName.Intern("NeedAppearances");
    private static readonly PdfName SigFlags = PdfName.Intern("SigFlags");

    // Field-only keys (Table 220, 222, 226, 228-230): moved to the shared
    // parent when a merged field/widget becomes a widget of a shared field.
    private static readonly string[] FieldOnlyKeys =
        ["T", "TU", "TM", "FT", "Ff", "V", "DV", "Opt", "TI", "I", "MaxLen", "Lock", "SV"];

    /// <summary>
    /// Collects the form of <paramref name="document"/>, or returns null when
    /// it has no fields. The field tree is walked through <c>/Kids</c>;
    /// <c>/Parent</c> and a widget's <c>/P</c> are not followed (parents are
    /// reached from the roots, and <c>/P</c> is set to the merged page later).
    /// </summary>
    internal static FormSource? Collect(PdfDocument document, int docIndex)
    {
        if (document.Catalog.TryGetValue(PdfName.Intern("AcroForm"), out PdfPrimitive? raw)
            && document.Objects.Resolve(raw) is PdfDictionary acroForm
            && document.Objects.Resolve(acroForm.TryGetValue(Fields, out PdfPrimitive? f) ? f : PdfNull.Value) is PdfArray fields
            && fields.Count > 0)
        {
            List<PdfIndirectObject> objects = new List<PdfIndirectObject>();
            HashSet<int> visited = new HashSet<int>();
            for (int i = 0; i < fields.Count; i++)
            {
                CollectField(fields[i], document.Objects, objects, visited, depth: 0);
            }

            foreach (string key in new[] { "DR", "CO" })
            {
                if (acroForm.TryGetValue(PdfName.Intern(key), out PdfPrimitive? value))
                {
                    ObjectImporter.CollectReferences(value, document.Objects, objects, visited);
                }
            }

            return new FormSource(docIndex, acroForm, objects);
        }

        return null;
    }

    private static void CollectField(
        PdfPrimitive primitive, IPdfObjectResolver resolver, List<PdfIndirectObject> objects, HashSet<int> visited, int depth)
    {
        if (depth > 64 || primitive is not PdfReference reference || !visited.Add(reference.ObjectId.ObjectNumber))
        {
            return;
        }

        PdfWorkScope.ThrowIfCancellationRequested();
        if (resolver.Resolve(reference) is not PdfDictionary field)
        {
            return;
        }

        objects.Add(new PdfIndirectObject(reference.ObjectId, field));
        foreach (KeyValuePair<PdfName, PdfPrimitive> entry in field)
        {
            if (entry.Key == Kids && resolver.Resolve(entry.Value) is PdfArray kids)
            {
                for (int i = 0; i < kids.Count; i++)
                {
                    CollectField(kids[i], resolver, objects, visited, depth + 1);
                }
            }
            else if (entry.Key != PdfName.Parent && entry.Key != P)
            {
                ObjectImporter.CollectReferences(entry.Value, resolver, objects, visited);
            }
        }
    }

    /// <summary>
    /// Builds the merged interactive form dictionary from the copied objects.
    /// </summary>
    /// <param name="sources">The inputs' forms, in merge order.</param>
    /// <param name="mode">How clashing field names are handled (not Drop).</param>
    /// <param name="objects">Every output object; shared parents are added and absorbed fields removed.</param>
    /// <param name="idRemap">(input, source number) to output number.</param>
    /// <param name="widgetPages">(input, source widget number) to the output page reference.</param>
    /// <param name="nextId">The next free output object number.</param>
    /// <returns>The merged <c>/AcroForm</c>, or null when no input has fields.</returns>
    internal static PdfDictionary? Build(
        IReadOnlyList<FormSource> sources,
        MergeFormFields mode,
        List<PdfIndirectObject> objects,
        Dictionary<(int Doc, int Num), int> idRemap,
        Dictionary<(int Doc, int Num), PdfReference> widgetPages,
        ref int nextId)
    {
        if (sources.Count == 0)
        {
            return null;
        }

        Dictionary<int, PdfDictionary> byId = new Dictionary<int, PdfDictionary>();
        Dictionary<int, PdfPrimitive> values = new Dictionary<int, PdfPrimitive>();
        foreach (PdfIndirectObject obj in objects)
        {
            values[obj.Id.ObjectNumber] = obj.Value;
            if (obj.Value is PdfDictionary d)
            {
                byId[obj.Id.ObjectNumber] = d;
            }
        }

        PdfArray mergedFields = new PdfArray([]);
        PdfArray mergedCo = new PdfArray([]);
        PdfDictionary mergedDr = new PdfDictionary();
        Dictionary<string, PdfDictionary> sharedParents = new Dictionary<string, PdfDictionary>(StringComparer.Ordinal);
        Dictionary<string, int> sharedParentIds = new Dictionary<string, int>(StringComparer.Ordinal);
        HashSet<string> usedNames = new HashSet<string>(StringComparer.Ordinal);
        HashSet<int> removed = new HashSet<int>();
        Dictionary<int, int> redirected = new Dictionary<int, int>();
        PdfPrimitive? defaultDa = null;
        PdfPrimitive? defaultQ = null;
        bool needAppearances = false;
        int sigFlags = 0;

        foreach (FormSource source in sources)
        {
            PdfDictionary acroForm = (PdfDictionary)ObjectImporter.DeepCopyPrimitive(source.AcroForm, source.DocIndex, idRemap, nullUnmapped: true);
            Dictionary<string, string> fontRenames = MergeResources(mergedDr, Resolve(acroForm, DR, values), mergedDr.Count == 0, values);

            PdfPrimitive? sourceDa = acroForm.TryGetValue(DA, out PdfPrimitive? da) ? RewriteDa(da, fontRenames) : null;
            PdfPrimitive? sourceQ = acroForm.TryGetValue(Q, out PdfPrimitive? q) ? q : null;
            defaultDa ??= sourceDa;
            defaultQ ??= sourceQ;

            needAppearances |= acroForm.TryGetValue(NeedAppearances, out PdfPrimitive? na) && na is PdfBoolean { Value: true };
            if (acroForm.TryGetValue(SigFlags, out PdfPrimitive? sf) && sf is PdfInteger flags)
            {
                sigFlags |= flags.Value;
            }

            // Fix every widget's /P and every /DA of this input's tree.
            List<int> treeIds = new List<int>();
            if (acroForm.TryGetValue(Fields, out PdfPrimitive? rawFields) && Resolve(rawFields, values) is PdfArray roots)
            {
                for (int i = 0; i < roots.Count; i++)
                {
                    VisitTree(roots[i], byId, treeIds, depth: 0);
                }

                foreach (int id in treeIds)
                {
                    PdfDictionary node = byId[id];
                    if (fontRenames.Count > 0 && node.TryGetValue(DA, out PdfPrimitive? nodeDa))
                    {
                        node.Set(DA, RewriteDa(nodeDa, fontRenames));
                    }

                    if (IsWidget(node) && SourceNumber(id, source.DocIndex, idRemap) is int src
                        && widgetPages.TryGetValue((source.DocIndex, src), out PdfReference? page))
                    {
                        node.Set(P, page);
                    }
                }

                for (int i = 0; i < roots.Count; i++)
                {
                    if (roots[i] is not PdfReference rootRef || !byId.TryGetValue(rootRef.ObjectId.ObjectNumber, out PdfDictionary? root))
                    {
                        continue;
                    }

                    // An inherited /DA or /Q that differs from the merged
                    // default is pinned on the root so it keeps applying.
                    if (!root.ContainsKey(DA) && sourceDa is not null && !SameValue(sourceDa, defaultDa))
                    {
                        root.Set(DA, sourceDa);
                    }

                    if (!root.ContainsKey(Q) && sourceQ is not null && !SameValue(sourceQ, defaultQ))
                    {
                        root.Set(Q, sourceQ);
                    }

                    string? name = root.TryGetValue(T, out PdfPrimitive? t) && t is PdfString ts ? ts.ToTextString() : null;
                    if (name is null || usedNames.Add(name))
                    {
                        mergedFields.Add(rootRef);
                        continue;
                    }

                    if (mode == MergeFormFields.ShareSameName)
                    {
                        ShareState state = new ShareState(byId, sharedParents, sharedParentIds, objects, removed, redirected, nextId);
                        bool shared = Share(name, root, rootRef.ObjectId.ObjectNumber, mergedFields, state);
                        nextId = state.NextId;
                        if (shared)
                        {
                            continue;
                        }
                    }

                    root.Set(T, TextString(FreeName(name, usedNames)));
                    mergedFields.Add(rootRef);
                }
            }

            if (acroForm.TryGetValue(CO, out PdfPrimitive? co) && Resolve(co, values) is PdfArray order)
            {
                for (int i = 0; i < order.Count; i++)
                {
                    mergedCo.Add(order[i]);
                }
            }
        }

        if (removed.Count > 0)
        {
            objects.RemoveAll(o => removed.Contains(o.Id.ObjectNumber));
            for (int i = 0; i < mergedCo.Count; i++)
            {
                if (mergedCo[i] is PdfReference r && redirected.TryGetValue(r.ObjectId.ObjectNumber, out int to))
                {
                    Replace(mergedCo, i, new PdfReference(new PdfObjectId(to, 0)));
                }
            }
        }

        if (mergedFields.Count == 0)
        {
            return null;
        }

        PdfDictionary merged = new PdfDictionary();
        merged.Set(Fields, mergedFields);
        if (mergedDr.Count > 0)
        {
            merged.Set(DR, mergedDr);
        }

        if (defaultDa is not null)
        {
            merged.Set(DA, defaultDa);
        }

        if (defaultQ is not null)
        {
            merged.Set(Q, defaultQ);
        }

        if (needAppearances)
        {
            merged.Set(NeedAppearances, true);
        }

        if (sigFlags != 0)
        {
            merged.Set(SigFlags, sigFlags);
        }

        if (mergedCo.Count > 0)
        {
            merged.Set(CO, mergedCo);
        }

        return merged;
    }

    // The working state of ShareSameName across one merge.
    private sealed class ShareState
    {
        internal ShareState(
            Dictionary<int, PdfDictionary> byId,
            Dictionary<string, PdfDictionary> sharedParents,
            Dictionary<string, int> sharedParentIds,
            List<PdfIndirectObject> objects,
            HashSet<int> removed,
            Dictionary<int, int> redirected,
            int nextId)
        {
            ById = byId;
            SharedParents = sharedParents;
            SharedParentIds = sharedParentIds;
            Objects = objects;
            Removed = removed;
            Redirected = redirected;
            NextId = nextId;
        }

        internal Dictionary<int, PdfDictionary> ById { get; }

        internal Dictionary<string, PdfDictionary> SharedParents { get; }

        internal Dictionary<string, int> SharedParentIds { get; }

        internal List<PdfIndirectObject> Objects { get; }

        internal HashSet<int> Removed { get; }

        internal Dictionary<int, int> Redirected { get; }

        internal int NextId { get; set; }
    }

    // Keys a field passes down to its kids (PDF 32000-1:2008 §12.7.3.1,
    // Table 220; §12.7.3.3; Table 229 for /MaxLen, as FormReader reads it).
    private static readonly string[] InheritableKeys = ["FT", "Ff", "V", "DV", "DA", "Q", "MaxLen"];

    // Shares a clashing root with the earlier root of the same fully
    // qualified name. Terminal fields of the same type become one field with
    // the widgets of both. Since 3.34.0 (found by Arun's visual check), two
    // fields with named children are merged too: a child name both have is
    // shared the same way (recursively), and a child only the later one has
    // moves under the earlier parent. Returns false, changing nothing, when
    // the two trees cannot be shared (a type differs, or a terminal field
    // meets a field with children); the caller then renames.
    private static bool Share(string name, PdfDictionary later, int laterId, PdfArray mergedFields, ShareState state)
    {
        int earlierIndex = FindRoot(mergedFields, name, state.ById);
        if (earlierIndex < 0 || mergedFields[earlierIndex] is not PdfReference earlierRef
            || !state.ById.TryGetValue(earlierRef.ObjectId.ObjectNumber, out PdfDictionary? earlier)
            || !CanShare(earlier, later, null, null, state.ById, depth: 0))
        {
            return false;
        }

        if (IsTerminal(earlier, state.ById))
        {
            ShareTerminal(name, mergedFields, earlierIndex, null, later, laterId, state);
        }
        else
        {
            MergeParents(name, earlier, earlierRef, later, laterId, new Dictionary<string, PdfPrimitive>(StringComparer.Ordinal), state);
        }

        return true;
    }

    // Whether the later tree can be shared with the earlier one; ftA and ftB
    // are the field types the two inherit from their ancestors.
    private static bool CanShare(
        PdfDictionary earlier, PdfDictionary later, PdfPrimitive? ftA, PdfPrimitive? ftB, Dictionary<int, PdfDictionary> byId, int depth)
    {
        if (depth > 64)
        {
            return false;
        }

        ftA = FieldType(earlier) ?? ftA;
        ftB = FieldType(later) ?? ftB;
        bool terminalA = IsTerminal(earlier, byId);
        bool terminalB = IsTerminal(later, byId);
        if (terminalA || terminalB)
        {
            return terminalA && terminalB && SameValue(ftA, ftB);
        }

        if (Resolve(earlier, Kids, byId) is not PdfArray earlierKids || Resolve(later, Kids, byId) is not PdfArray laterKids)
        {
            return false;
        }

        for (int i = 0; i < laterKids.Count; i++)
        {
            if (laterKids[i] is not PdfReference || Resolve(laterKids[i], byId) is not PdfDictionary kid || KidName(kid) is not string kidName)
            {
                return false;
            }

            int match = FindKid(earlierKids, kidName, byId);
            if (match >= 0 && (earlierKids[match] is not PdfReference || Resolve(earlierKids[match], byId) is not PdfDictionary earlierKid
                || !CanShare(earlierKid, kid, ftA, ftB, byId, depth + 1)))
            {
                return false;
            }
        }

        return true;
    }

    // Two fields with named children: the later one's children join the
    // earlier one, and the later field object is dropped. laterInherited holds
    // what the later field inherits from its own ancestors.
    private static void MergeParents(
        string fullName,
        PdfDictionary earlier,
        PdfReference earlierRef,
        PdfDictionary later,
        int laterId,
        Dictionary<string, PdfPrimitive> laterInherited,
        ShareState state)
    {
        Dictionary<string, PdfPrimitive> inherited = new Dictionary<string, PdfPrimitive>(laterInherited, StringComparer.Ordinal);
        foreach (string key in InheritableKeys)
        {
            if (later.TryGetValue(PdfName.Intern(key), out PdfPrimitive? own))
            {
                inherited[key] = own;
            }
        }

        PdfArray earlierKids = (PdfArray)Resolve(earlier, Kids, state.ById)!;
        PdfArray laterKids = (PdfArray)Resolve(later, Kids, state.ById)!;
        for (int i = 0; i < laterKids.Count; i++)
        {
            PdfReference kidRef = (PdfReference)laterKids[i];
            PdfDictionary kid = state.ById[kidRef.ObjectId.ObjectNumber];
            string kidName = KidName(kid)!;
            int match = FindKid(earlierKids, kidName, state.ById);
            if (match < 0)
            {
                // Only the later tree has this child: it moves, keeping what it
                // inherited from the later tree.
                foreach (KeyValuePair<string, PdfPrimitive> entry in inherited)
                {
                    PdfName pdfKey = PdfName.Intern(entry.Key);
                    if (!kid.ContainsKey(pdfKey))
                    {
                        kid.Set(pdfKey, entry.Value);
                    }
                }

                kid.Set(PdfName.Parent, earlierRef);
                earlierKids.Add(kidRef);
                continue;
            }

            string childName = fullName + "." + kidName;
            PdfReference earlierKidRef = (PdfReference)earlierKids[match];
            PdfDictionary earlierKid = state.ById[earlierKidRef.ObjectId.ObjectNumber];
            if (IsTerminal(earlierKid, state.ById))
            {
                ShareTerminal(childName, earlierKids, match, earlierRef, kid, kidRef.ObjectId.ObjectNumber, state);
            }
            else
            {
                MergeParents(childName, earlierKid, earlierKidRef, kid, kidRef.ObjectId.ObjectNumber, inherited, state);
            }
        }

        state.Removed.Add(laterId);
        state.Redirected[laterId] = earlierRef.ObjectId.ObjectNumber;
    }

    // Shares a later terminal field with the earlier one at
    // container[index] (the form's /Fields, or a parent's /Kids, whose owner
    // is containerOwner). The earlier field becomes a pure field (split from
    // its widget when merged) and gains the later field's widgets; the later
    // field object is dropped.
    private static void ShareTerminal(
        string fullName, PdfArray container, int index, PdfReference? containerOwner, PdfDictionary later, int laterId, ShareState state)
    {
        if (!state.SharedParents.TryGetValue(fullName, out PdfDictionary? parent))
        {
            PdfReference earlierRef = (PdfReference)container[index];
            PdfDictionary earlier = state.ById[earlierRef.ObjectId.ObjectNumber];
            if (earlier.ContainsKey(Kids))
            {
                parent = earlier;
                state.SharedParentIds[fullName] = earlierRef.ObjectId.ObjectNumber;
            }
            else
            {
                // A merged field/widget: split it into a field and a widget.
                int parentId = state.NextId++;
                PdfReference parentRef0 = new PdfReference(new PdfObjectId(parentId, 0));
                parent = SplitField(earlier);
                parent.Set(Kids, new PdfArray([earlierRef]));
                if (containerOwner is not null)
                {
                    parent.Set(PdfName.Parent, containerOwner);
                }

                earlier.Set(PdfName.Parent, parentRef0);
                state.Objects.Add(new PdfIndirectObject(new PdfObjectId(parentId, 0), parent));
                state.ById[parentId] = parent;
                Replace(container, index, parentRef0);
                state.SharedParentIds[fullName] = parentId;
                state.Redirected[earlierRef.ObjectId.ObjectNumber] = parentId;
            }

            state.SharedParents[fullName] = parent;
        }

        int sharedId = state.SharedParentIds[fullName];
        PdfReference parentRef = new PdfReference(new PdfObjectId(sharedId, 0));
        PdfArray parentKids = (PdfArray)Resolve(parent, Kids, state.ById)!;
        if (Resolve(later, Kids, state.ById) is PdfArray laterKids)
        {
            for (int i = 0; i < laterKids.Count; i++)
            {
                if (laterKids[i] is PdfReference kidRef && state.ById.TryGetValue(kidRef.ObjectId.ObjectNumber, out PdfDictionary? kid))
                {
                    kid.Set(PdfName.Parent, parentRef);
                    parentKids.Add(kidRef);
                }
            }

            state.Removed.Add(laterId);
            state.Redirected[laterId] = sharedId;
        }
        else
        {
            foreach (string key in FieldOnlyKeys)
            {
                later.Remove(PdfName.Intern(key));
            }

            later.Set(PdfName.Parent, parentRef);
            parentKids.Add(new PdfReference(new PdfObjectId(laterId, 0)));
            state.Redirected[laterId] = sharedId;
        }
    }

    private static string? KidName(PdfDictionary kid) =>
        kid.TryGetValue(T, out PdfPrimitive? t) && t is PdfString s ? s.ToTextString() : null;

    private static int FindKid(PdfArray kids, string name, Dictionary<int, PdfDictionary> byId)
    {
        for (int i = 0; i < kids.Count; i++)
        {
            if (Resolve(kids[i], byId) is PdfDictionary kid && KidName(kid) == name)
            {
                return i;
            }
        }

        return -1;
    }

    private static PdfDictionary SplitField(PdfDictionary mergedFieldWidget)
    {
        PdfDictionary field = new PdfDictionary();
        foreach (string key in FieldOnlyKeys)
        {
            PdfName name = PdfName.Intern(key);
            if (mergedFieldWidget.TryGetValue(name, out PdfPrimitive? value))
            {
                field.Set(name, value);
                mergedFieldWidget.Remove(name);
            }
        }

        return field;
    }

    private static int FindRoot(PdfArray fields, string name, Dictionary<int, PdfDictionary> byId)
    {
        for (int i = 0; i < fields.Count; i++)
        {
            if (fields[i] is PdfReference r && byId.TryGetValue(r.ObjectId.ObjectNumber, out PdfDictionary? d)
                && d.TryGetValue(T, out PdfPrimitive? t) && t is PdfString s && s.ToTextString() == name)
            {
                return i;
            }
        }

        return -1;
    }

    // A terminal field: no kids, or kids that are all widgets (no /T).
    private static bool IsTerminal(PdfDictionary field, Dictionary<int, PdfDictionary> byId)
    {
        if (Resolve(field, Kids, byId) is not PdfArray kids)
        {
            return !field.ContainsKey(Kids);
        }

        for (int i = 0; i < kids.Count; i++)
        {
            if (Resolve(kids[i], byId) is not PdfDictionary kid || kid.ContainsKey(T))
            {
                return false;
            }
        }

        return true;
    }

    private static PdfPrimitive? FieldType(PdfDictionary field) =>
        field.TryGetValue(FT, out PdfPrimitive? ft) ? ft : null;

    private static bool IsWidget(PdfDictionary node) =>
        (node.TryGetValue(Subtype, out PdfPrimitive? s) && s is PdfName n && n == Widget) || node.ContainsKey(Rect);

    private static void VisitTree(PdfPrimitive primitive, Dictionary<int, PdfDictionary> byId, List<int> ids, int depth)
    {
        if (depth > 64 || primitive is not PdfReference r || !byId.TryGetValue(r.ObjectId.ObjectNumber, out PdfDictionary? node)
            || ids.Contains(r.ObjectId.ObjectNumber))
        {
            return;
        }

        ids.Add(r.ObjectId.ObjectNumber);
        if (Resolve(node, Kids, byId) is PdfArray kids)
        {
            for (int i = 0; i < kids.Count; i++)
            {
                VisitTree(kids[i], byId, ids, depth + 1);
            }
        }
    }

    // Adds an input's /DR to the merged one. A name already taken by a
    // different resource is renamed name_N; the font renames are returned so
    // that /DA strings can follow.
    private static Dictionary<string, string> MergeResources(
        PdfDictionary merged, PdfPrimitive? input, bool first, Dictionary<int, PdfPrimitive> values)
    {
        Dictionary<string, string> fontRenames = new Dictionary<string, string>(StringComparer.Ordinal);
        if (input is not PdfDictionary dr)
        {
            return fontRenames;
        }

        foreach (KeyValuePair<PdfName, PdfPrimitive> category in dr)
        {
            if (Resolve(category.Value, values) is not PdfDictionary entries)
            {
                if (!merged.ContainsKey(category.Key))
                {
                    merged.Set(category.Key, category.Value);
                }

                continue;
            }

            if (!merged.TryGetValue(category.Key, out PdfPrimitive? existing) || existing is not PdfDictionary target)
            {
                target = new PdfDictionary();
                merged.Set(category.Key, target);
            }

            foreach (KeyValuePair<PdfName, PdfPrimitive> entry in entries)
            {
                if (!target.TryGetValue(entry.Key, out PdfPrimitive? taken) || first || SameValue(taken, entry.Value))
                {
                    target.Set(entry.Key, entry.Value);
                    continue;
                }

                string renamed = entry.Key.Value;
                for (int n = 2; target.ContainsKey(PdfName.Intern(renamed)); n++)
                {
                    renamed = entry.Key.Value + "_" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                target.Set(PdfName.Intern(renamed), entry.Value);
                if (category.Key.Value == "Font")
                {
                    fontRenames[entry.Key.Value] = renamed;
                }
            }
        }

        return fontRenames;
    }

    // Rewrites the font name operand of `Tf` in a /DA string.
    private static PdfPrimitive RewriteDa(PdfPrimitive da, Dictionary<string, string> fontRenames)
    {
        if (fontRenames.Count == 0 || da is not PdfString s)
        {
            return da;
        }

        string text = Encoding.Latin1.GetString(s.Bytes);
        StringBuilder sb = new StringBuilder(text.Length + 8);
        int i = 0;
        while (i < text.Length)
        {
            if (text[i] == '/')
            {
                int end = i + 1;
                while (end < text.Length && !char.IsWhiteSpace(text[end]) && "/[]()<>{}%".IndexOf(text[end], StringComparison.Ordinal) < 0)
                {
                    end++;
                }

                string name = text.Substring(i + 1, end - i - 1);
                sb.Append('/').Append(fontRenames.TryGetValue(name, out string? to) ? to : name);
                i = end;
                continue;
            }

            sb.Append(text[i]);
            i++;
        }

        return new PdfString(Encoding.Latin1.GetBytes(sb.ToString()));
    }

    private static string FreeName(string name, HashSet<string> used)
    {
        for (int n = 2; ; n++)
        {
            string candidate = name + "_" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (used.Add(candidate))
            {
                return candidate;
            }
        }
    }

    private static PdfString TextString(string value)
    {
        foreach (char c in value)
        {
            if (c > 0xFF)
            {
                return PdfString.FromUnicode(value);
            }
        }

        return new PdfString(Encoding.Latin1.GetBytes(value));
    }

    private static int? SourceNumber(int outputId, int docIndex, Dictionary<(int Doc, int Num), int> idRemap)
    {
        foreach (KeyValuePair<(int Doc, int Num), int> kv in idRemap)
        {
            if (kv.Value == outputId && kv.Key.Doc == docIndex)
            {
                return kv.Key.Num;
            }
        }

        return null;
    }

    private static PdfPrimitive? Resolve<TValue>(PdfDictionary dict, PdfName key, Dictionary<int, TValue> objects)
        where TValue : PdfPrimitive =>
        dict.TryGetValue(key, out PdfPrimitive? value) ? Resolve(value, objects) : null;

    private static PdfPrimitive? Resolve<TValue>(PdfPrimitive value, Dictionary<int, TValue> objects)
        where TValue : PdfPrimitive =>
        value is PdfReference r ? (objects.TryGetValue(r.ObjectId.ObjectNumber, out TValue? d) ? d : null) : value;

    private static void Replace(PdfArray array, int index, PdfPrimitive value)
    {
        array.RemoveAt(index);
        array.Insert(index, value);
    }

    private static bool SameValue(PdfPrimitive? a, PdfPrimitive? b) => a switch
    {
        null => b is null,
        PdfReference ra => b is PdfReference rb && ra.ObjectId.ObjectNumber == rb.ObjectId.ObjectNumber,
        PdfString sa => b is PdfString sb && sa.Bytes.AsSpan().SequenceEqual(sb.Bytes),
        PdfName na => b is PdfName nb && na == nb,
        PdfInteger ia => b is PdfInteger ib && ia.Value == ib.Value,
        _ => ReferenceEquals(a, b),
    };
}
