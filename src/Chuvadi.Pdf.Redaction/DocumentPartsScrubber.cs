// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.11 (file specifications, embedded files),
//        §7.9.6 (name trees), §12.6.4.16 (JavaScript actions),
//        §12.6.4.7 (URI actions), §14.9.3–14.9.5 (alternate and
//        replacement text)
// PHASE: 3.35.0 (D-396, D-397, audit A-08 RED-03/RED-05) — the parts of a
//        document outside its pages that redaction keeps: attachments,
//        document JavaScript, actions and alternate texts.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Chuvadi.Pdf.Filters;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Redaction;

// Runs over the objects about to be written. With the document-wide patterns
// (those with no page list):
//  - a JavaScript action whose script matches has its script removed;
//  - a URI action whose address matches has its address removed;
//  - an /ActualText, /Alt or /E entry that matches is removed.
// Attachments (embedded files) are kept only when they can be read in full
// as text and nothing in them or their names matches a pattern; any other
// attachment is removed (its file data leaves the output) unless the caller
// keeps unscanned attachments, which are then listed as not scanned. With
// no patterns at all (rectangles only) attachments are kept and listed as
// not scanned (D-398): there is nothing to check them against. The
// objects changed replace their originals in the list; the source document
// is unchanged.
internal static class DocumentPartsScrubber
{
    private const int MaxDepth = 64;

    private const int MaxNameTreeNodes = 100_000;

    // Attachments larger than this are not read as text.
    private const int MaxScannedAttachmentBytes = 64 * 1024 * 1024;

    private static readonly string[] ReplacementTextKeys = { "ActualText", "Alt", "E" };

    private static readonly string[] FileNameKeys = { "UF", "F", "DOS", "Mac", "Unix", "Desc" };

    internal sealed class Result
    {
        internal int ScriptsRemoved { get; set; }

        internal int TextMatchesRemoved { get; set; }

        internal List<string> AttachmentsRemoved { get; } = new List<string>();

        internal List<string> AttachmentsNotScanned { get; } = new List<string>();
    }

    internal static Result Scrub(
        IList<PatternRule> patterns,
        List<PdfIndirectObject> objects,
        PdfDictionary trailer,
        FilterPipeline pipeline,
        bool keepAttachments)
    {
        List<PatternRule> rules = new List<PatternRule>();
        foreach (PatternRule rule in patterns)
        {
            if (rule.PageIndices is null)
            {
                rules.Add(rule);
            }
        }

        // D-398: rectangles only keep attachments (listed as not scanned).
        bool keepUnscanned = keepAttachments || patterns.Count == 0;
        Scrubbing run = new Scrubbing(rules, objects, pipeline, keepUnscanned);
        run.DecideAttachments();
        run.PruneEmbeddedFilesTree(trailer);
        run.RewriteObjects();
        return run.Outcome;
    }

    private sealed class Scrubbing
    {
        private readonly List<PatternRule> _rules;
        private readonly List<PdfIndirectObject> _objects;
        private readonly FilterPipeline _pipeline;
        private readonly bool _keepAttachments;
        private readonly Dictionary<int, int> _index = new Dictionary<int, int>();

        // Attachments to remove, by file specification dictionary.
        private readonly HashSet<PdfDictionary> _removed = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);

        // Attachments whose names match, so their names are removed too.
        private readonly HashSet<PdfDictionary> _namesMatch = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);

        // Objects (embedded file streams, script streams) emptied.
        private readonly HashSet<int> _emptied = new HashSet<int>();

        internal Scrubbing(List<PatternRule> rules, List<PdfIndirectObject> objects, FilterPipeline pipeline, bool keepAttachments)
        {
            _rules = rules;
            _objects = objects;
            _pipeline = pipeline;
            _keepAttachments = keepAttachments;
            for (int i = 0; i < objects.Count; i++)
            {
                _index[objects[i].Id.ObjectNumber] = i;
            }
        }

        internal Result Outcome { get; } = new Result();

        // ── Attachments ──────────────────────────────────────────────────

        internal void DecideAttachments()
        {
            HashSet<PdfDictionary> seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
            for (int i = 0; i < _objects.Count; i++)
            {
                PdfWorkScope.ThrowIfCancellationRequested();
                FindFileSpecs(_objects[i].Value, seen, 0);
            }
        }

        private void FindFileSpecs(PdfPrimitive value, HashSet<PdfDictionary> seen, int depth)
        {
            if (depth > MaxDepth)
            {
                return;
            }

            switch (value)
            {
                case PdfDictionary dict:
                    if (dict.TryGetValue(PdfName.Intern("EF"), out PdfPrimitive? ef) && Written(ef) is PdfDictionary)
                    {
                        if (seen.Add(dict))
                        {
                            Decide(dict);
                        }

                        return;
                    }

                    foreach (KeyValuePair<PdfName, PdfPrimitive> entry in dict)
                    {
                        FindFileSpecs(entry.Value, seen, depth + 1);
                    }

                    break;
                case PdfArray array:
                    foreach (PdfPrimitive item in array)
                    {
                        FindFileSpecs(item, seen, depth + 1);
                    }

                    break;
            }
        }

        private void Decide(PdfDictionary spec)
        {
            string name = FileName(spec);
            bool nameMatches = false;
            foreach (string key in FileNameKeys)
            {
                if (spec.TryGetValue(PdfName.Intern(key), out PdfPrimitive? v) && Written(v) is PdfString s && Matches(s.ToTextString()))
                {
                    nameMatches = true;
                }
            }

            if (nameMatches)
            {
                _namesMatch.Add(spec);
                Remove(spec, "(name redacted)");
                return;
            }

            if (_rules.Count > 0 && TryReadAll(spec, out List<string> texts))
            {
                foreach (string text in texts)
                {
                    if (Matches(text))
                    {
                        Remove(spec, name);
                        return;
                    }
                }

                return;
            }

            if (_keepAttachments)
            {
                Outcome.AttachmentsNotScanned.Add(name);
                return;
            }

            Remove(spec, name);
        }

        private void Remove(PdfDictionary spec, string reportedName)
        {
            _removed.Add(spec);
            Outcome.AttachmentsRemoved.Add(reportedName);
            if (spec.TryGetValue(PdfName.Intern("EF"), out PdfPrimitive? ef) && Written(ef) is PdfDictionary files)
            {
                foreach (KeyValuePair<PdfName, PdfPrimitive> entry in files)
                {
                    if (entry.Value is PdfReference r)
                    {
                        _emptied.Add(r.ObjectId.ObjectNumber);
                    }
                }
            }

            // Related files (/RF) hold further embedded streams.
            if (spec.TryGetValue(PdfName.Intern("RF"), out PdfPrimitive? rf) && Written(rf) is PdfDictionary related)
            {
                foreach (KeyValuePair<PdfName, PdfPrimitive> entry in related)
                {
                    if (Written(entry.Value) is PdfArray pairs)
                    {
                        foreach (PdfPrimitive item in pairs)
                        {
                            if (item is PdfReference r)
                            {
                                _emptied.Add(r.ObjectId.ObjectNumber);
                            }
                        }
                    }
                }
            }
        }

        // True when every embedded file of the specification reads as text.
        private bool TryReadAll(PdfDictionary spec, out List<string> texts)
        {
            texts = new List<string>();
            if (spec.ContainsKey(PdfName.Intern("RF"))
                || !spec.TryGetValue(PdfName.Intern("EF"), out PdfPrimitive? ef)
                || Written(ef) is not PdfDictionary files
                || files.Count == 0)
            {
                return false;
            }

            foreach (KeyValuePair<PdfName, PdfPrimitive> entry in files)
            {
                if (Written(entry.Value) is not PdfStream stream || !TryReadText(stream, out string? text))
                {
                    return false;
                }

                texts.Add(text);
            }

            return true;
        }

        // An embedded file read as text: a text media type or content that is
        // valid UTF-8 (or UTF-16 with a byte-order mark) without NUL bytes.
        private bool TryReadText(PdfStream stream, [NotNullWhen(true)] out string? text)
        {
            text = null;
            if (stream.RawLength > MaxScannedAttachmentBytes)
            {
                return false;
            }

            byte[] data;
            try
            {
                data = Redactor.DecodeStream(stream, _pipeline);
            }
            catch (Exception ex) when (ex is PdfException or InvalidOperationException or ArgumentException or NotSupportedException)
            {
                return false;
            }

            if (data.Length > MaxScannedAttachmentBytes)
            {
                return false;
            }

            if (data.Length >= 2 && ((data[0] == 0xFE && data[1] == 0xFF) || (data[0] == 0xFF && data[1] == 0xFE)))
            {
                Encoding utf16 = data[0] == 0xFE
                    ? new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true)
                    : new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true);
                try
                {
                    text = utf16.GetString(data, 2, data.Length - 2);
                }
                catch (DecoderFallbackException)
                {
                    return false;
                }

                return true;
            }

            if (Array.IndexOf(data, (byte)0) >= 0)
            {
                return false;
            }

            try
            {
                text = new UTF8Encoding(false, true).GetString(data);
            }
            catch (DecoderFallbackException)
            {
                return false;
            }

            return true;
        }

        private string FileName(PdfDictionary spec)
        {
            foreach (string key in new[] { "UF", "F" })
            {
                if (spec.TryGetValue(PdfName.Intern(key), out PdfPrimitive? v) && Written(v) is PdfString s)
                {
                    return s.ToTextString();
                }
            }

            return "(unnamed)";
        }

        // ── The embedded-files name tree ─────────────────────────────────

        internal void PruneEmbeddedFilesTree(PdfDictionary trailer)
        {
            if (_removed.Count == 0
                || !trailer.TryGetValue(PdfName.Root, out PdfPrimitive? root)
                || Written(root) is not PdfDictionary catalog
                || !catalog.TryGetValue(PdfName.Intern("Names"), out PdfPrimitive? namesValue)
                || Written(namesValue) is not PdfDictionary names
                || !names.TryGetValue(PdfName.Intern("EmbeddedFiles"), out PdfPrimitive? tree))
            {
                return;
            }

            int budget = MaxNameTreeNodes;
            PdfPrimitive? pruned = PruneNode(tree, ref budget, 0, out _);
            if (pruned is not null && !ReferenceEquals(pruned, tree))
            {
                PdfDictionary namesCopy = Copy(names);
                namesCopy.Set(PdfName.Intern("EmbeddedFiles"), pruned);
                Replace(namesValue, namesCopy, catalog, "Names", root);
            }
        }

        // Returns the node with removed attachments taken out (the same
        // instance when unchanged; an indirect node is replaced in the
        // object list and its reference returned), and its key limits.
        private PdfPrimitive? PruneNode(PdfPrimitive node, ref int budget, int depth, out (PdfString? First, PdfString? Last) limits)
        {
            limits = (null, null);
            if (depth > MaxDepth || budget-- <= 0 || Written(node) is not PdfDictionary dict)
            {
                return null;
            }

            bool changed = false;
            PdfDictionary copy = Copy(dict);
            if (dict.TryGetValue(PdfName.Intern("Names"), out PdfPrimitive? leafValue) && Written(leafValue) is PdfArray leaf)
            {
                PdfArray kept = new PdfArray();
                for (int i = 0; i + 1 < leaf.Count; i += 2)
                {
                    if (Written(leaf[i + 1]) is PdfDictionary spec && _removed.Contains(spec))
                    {
                        changed = true;
                        continue;
                    }

                    kept.Add(leaf[i]);
                    kept.Add(leaf[i + 1]);
                    if (Written(leaf[i]) is PdfString key)
                    {
                        limits.First ??= key;
                        limits.Last = key;
                    }
                }

                copy.Set(PdfName.Intern("Names"), kept);
            }

            if (dict.TryGetValue(PdfName.Intern("Kids"), out PdfPrimitive? kidsValue) && Written(kidsValue) is PdfArray kids)
            {
                PdfArray newKids = new PdfArray();
                foreach (PdfPrimitive kid in kids)
                {
                    PdfPrimitive? prunedKid = PruneNode(kid, ref budget, depth + 1, out (PdfString? First, PdfString? Last) kidLimits);
                    if (prunedKid is null)
                    {
                        return null;
                    }

                    changed |= !ReferenceEquals(prunedKid, kid);
                    newKids.Add(prunedKid);
                    limits.First ??= kidLimits.First;
                    limits.Last = kidLimits.Last ?? limits.Last;
                }

                copy.Set(PdfName.Intern("Kids"), newKids);
            }

            if (!changed)
            {
                return node;
            }

            if (dict.ContainsKey(PdfName.Intern("Limits")))
            {
                if (limits.First is not null && limits.Last is not null)
                {
                    copy.Set(PdfName.Intern("Limits"), new PdfArray(new PdfPrimitive[] { limits.First, limits.Last }));
                }
                else
                {
                    copy.Remove(PdfName.Intern("Limits"));
                }
            }

            if (node is PdfReference r && _index.TryGetValue(r.ObjectId.ObjectNumber, out int at))
            {
                _objects[at] = new PdfIndirectObject(_objects[at].Id, copy);
                return new PdfReference(r.ObjectId);
            }

            return copy;
        }

        private void Replace(PdfPrimitive value, PdfDictionary replacement, PdfDictionary parent, string key, PdfPrimitive parentRef)
        {
            if (value is PdfReference r && _index.TryGetValue(r.ObjectId.ObjectNumber, out int at))
            {
                _objects[at] = new PdfIndirectObject(_objects[at].Id, replacement);
                return;
            }

            PdfDictionary parentCopy = Copy(parent);
            parentCopy.Set(PdfName.Intern(key), replacement);
            if (parentRef is PdfReference pr && _index.TryGetValue(pr.ObjectId.ObjectNumber, out int parentAt))
            {
                _objects[parentAt] = new PdfIndirectObject(_objects[parentAt].Id, parentCopy);
            }
        }

        // ── Scripts, addresses, alternate texts and removed attachments ──

        internal void RewriteObjects()
        {
            for (int i = 0; i < _objects.Count; i++)
            {
                PdfWorkScope.ThrowIfCancellationRequested();
                PdfIndirectObject obj = _objects[i];
                if (obj.Value is PdfStream)
                {
                    continue;
                }

                PdfPrimitive rewritten = Rewrite(obj.Value, 0);
                if (!ReferenceEquals(rewritten, obj.Value))
                {
                    _objects[i] = new PdfIndirectObject(obj.Id, rewritten);
                }
            }

            foreach (int number in _emptied)
            {
                if (_index.TryGetValue(number, out int at))
                {
                    _objects[at] = new PdfIndirectObject(_objects[at].Id, new PdfStream(new PdfDictionary(), Array.Empty<byte>()));
                }
            }
        }

        private PdfPrimitive Rewrite(PdfPrimitive value, int depth)
        {
            if (depth > MaxDepth)
            {
                return value;
            }

            switch (value)
            {
                case PdfDictionary dict:
                    return RewriteDictionary(dict, depth);
                case PdfArray array:
                    PdfArray? arrayCopy = null;
                    for (int i = 0; i < array.Count; i++)
                    {
                        PdfPrimitive item = Rewrite(array[i], depth + 1);
                        if (!ReferenceEquals(item, array[i]) && arrayCopy is null)
                        {
                            arrayCopy = new PdfArray(array.Count);
                            for (int k = 0; k < i; k++)
                            {
                                arrayCopy.Add(array[k]);
                            }
                        }

                        arrayCopy?.Add(item);
                    }

                    return arrayCopy ?? (PdfPrimitive)array;
                default:
                    return value;
            }
        }

        private PdfPrimitive RewriteDictionary(PdfDictionary dict, int depth)
        {
            PdfDictionary? copy = null;
            PdfDictionary Edit() => copy ??= Copy(dict);

            if (_removed.Contains(dict))
            {
                PdfDictionary spec = Edit();
                spec.Remove(PdfName.Intern("EF"));
                spec.Remove(PdfName.Intern("RF"));
                if (_namesMatch.Contains(dict))
                {
                    foreach (string key in FileNameKeys)
                    {
                        if (spec.ContainsKey(PdfName.Intern(key)))
                        {
                            spec.Set(PdfName.Intern(key), key == "Desc" ? new PdfString(string.Empty) : new PdfString("redacted"));
                        }
                    }
                }
            }

            if (_rules.Count > 0)
            {
                PdfName? action = Written(dict.TryGetValue(PdfName.Intern("S"), out PdfPrimitive? s) ? s : PdfNull.Value) as PdfName;
                if (action?.Value == "JavaScript" && dict.TryGetValue(PdfName.Intern("JS"), out PdfPrimitive? js) && ScriptMatches(js))
                {
                    Edit().Set(PdfName.Intern("JS"), new PdfString(string.Empty));
                    if (js is PdfReference jsRef)
                    {
                        _emptied.Add(jsRef.ObjectId.ObjectNumber);
                    }

                    Outcome.ScriptsRemoved++;
                }

                if (action?.Value == "URI" && dict.TryGetValue(PdfName.Intern("URI"), out PdfPrimitive? uri)
                    && Written(uri) is PdfString address && Matches(address.ToTextString()))
                {
                    Edit().Set(PdfName.Intern("URI"), new PdfString(string.Empty));
                    Outcome.TextMatchesRemoved++;
                }

                foreach (string key in ReplacementTextKeys)
                {
                    if (dict.TryGetValue(PdfName.Intern(key), out PdfPrimitive? alt)
                        && Written(alt) is PdfString text && Matches(text.ToTextString()))
                    {
                        Edit().Remove(PdfName.Intern(key));
                        Outcome.TextMatchesRemoved++;
                    }
                }
            }

            foreach (KeyValuePair<PdfName, PdfPrimitive> entry in dict)
            {
                if (entry.Value is not (PdfDictionary or PdfArray))
                {
                    continue;
                }

                PdfPrimitive inner = Rewrite(entry.Value, depth + 1);
                if (!ReferenceEquals(inner, entry.Value))
                {
                    Edit().Set(entry.Key, inner);
                }
            }

            return copy ?? (PdfPrimitive)dict;
        }

        private bool ScriptMatches(PdfPrimitive js)
        {
            switch (Written(js))
            {
                case PdfString text:
                    return Matches(text.ToTextString());
                case PdfStream stream:
                    try
                    {
                        byte[] data = Redactor.DecodeStream(stream, _pipeline);
                        return Matches(Encoding.UTF8.GetString(data)) || Matches(Encoding.Latin1.GetString(data));
                    }
                    catch (Exception ex) when (ex is PdfException or InvalidOperationException or ArgumentException or NotSupportedException)
                    {
                        // A script that cannot be read is removed (fail closed).
                        return true;
                    }

                default:
                    return false;
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private bool Matches(string text) => Redactor.AnyPatternMatches(text, _rules);

        // A value as it will be written: a reference to an object in the
        // output is followed; one to an object not written is null.
        private PdfPrimitive Written(PdfPrimitive value)
        {
            if (value is PdfReference r)
            {
                return _index.TryGetValue(r.ObjectId.ObjectNumber, out int at) ? _objects[at].Value : PdfNull.Value;
            }

            return value;
        }

        private static PdfDictionary Copy(PdfDictionary dict)
        {
            PdfDictionary copy = new PdfDictionary(dict.Count);
            foreach (KeyValuePair<PdfName, PdfPrimitive> entry in dict)
            {
                copy.Set(entry.Key, entry.Value);
            }

            return copy;
        }
    }
}
