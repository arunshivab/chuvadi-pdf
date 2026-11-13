// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.3.3 (document outline), §14.3.3 (document
//        information dictionary), §14.3.2 (metadata streams)
// PHASE: 3.34.0 (#18) — redaction patterns applied to the text a document
//        carries outside its pages: bookmark titles, /Info and XMP.

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Filters;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Redaction;

// Removes the matches of the document-wide redaction patterns (those with no
// page list) from the bookmark titles and the XMP metadata packet; the
// objects it changes replace their originals in the output list. The
// document information dictionary is not carried to the redacted file at
// all, and its object is left out too.
internal static class DocumentTextScrubber
{
    private const int MaxOutlineItems = 100_000;

    // Returns the number of matches removed.
    internal static int Scrub(
        PdfDocument document,
        IList<PatternRule> patterns,
        List<PdfIndirectObject> objects,
        FilterPipeline pipeline)
    {
        List<PatternRule> rules = new List<PatternRule>();
        foreach (PatternRule rule in patterns)
        {
            if (rule.PageIndices is null)
            {
                rules.Add(rule);
            }
        }

        Dictionary<int, int> index = new Dictionary<int, int>();
        for (int i = 0; i < objects.Count; i++)
        {
            index[objects[i].Id.ObjectNumber] = i;
        }

        int removed = 0;
        PdfObjectStore store = document.Objects;

        if (document.Trailer.TryGetValue(PdfName.Info, out PdfPrimitive? infoPrim))
        {
            DropInfo(infoPrim, objects, index);
        }

        if (rules.Count == 0)
        {
            return removed;
        }

        PdfDictionary catalog = document.Catalog;
        if (catalog.TryGetValue(PdfName.Intern("Outlines"), out PdfPrimitive? outlines)
            && store.ResolveAs<PdfDictionary>(outlines) is PdfDictionary root
            && root.TryGetValue(PdfName.Intern("First"), out PdfPrimitive? first))
        {
            removed += ScrubOutline(first, store, rules, objects, index);
        }

        if (catalog.TryGetValue(PdfName.Intern("Metadata"), out PdfPrimitive? metadata)
            && metadata is PdfReference metadataRef
            && store.Resolve(metadataRef) is PdfStream xmp
            && index.TryGetValue(metadataRef.ObjectId.ObjectNumber, out int at))
        {
            removed += ScrubXmp(xmp, metadataRef.ObjectId, rules, objects, at, pipeline);
        }

        return removed;
    }

    // The redacted file has no /Info (its trailer names only the catalog, as
    // before 3.34.0); 3.34.0 also leaves out the information dictionary's
    // object when it was loaded, so its text is not left in the file as an
    // object nothing refers to.
    private static void DropInfo(PdfPrimitive infoPrim, List<PdfIndirectObject> objects, Dictionary<int, int> index)
    {
        if (infoPrim is PdfReference infoRef && index.TryGetValue(infoRef.ObjectId.ObjectNumber, out int at))
        {
            objects[at] = new PdfIndirectObject(infoRef.ObjectId, PdfNull.Value);
        }
    }

    // Every outline item reached through /First and /Next, its /Title scrubbed.
    private static int ScrubOutline(
        PdfPrimitive first, PdfObjectStore store, List<PatternRule> rules,
        List<PdfIndirectObject> objects, Dictionary<int, int> index)
    {
        int removed = 0;
        HashSet<int> seen = new HashSet<int>();
        Stack<PdfPrimitive> pending = new Stack<PdfPrimitive>();
        pending.Push(first);
        while (pending.Count > 0 && seen.Count < MaxOutlineItems)
        {
            PdfWorkScope.ThrowIfCancellationRequested();
            if (pending.Pop() is not PdfReference itemRef
                || !seen.Add(itemRef.ObjectId.ObjectNumber)
                || store.Resolve(itemRef) is not PdfDictionary item)
            {
                continue;
            }

            if (item.TryGetValue(PdfName.Intern("Next"), out PdfPrimitive? next))
            {
                pending.Push(next);
            }

            if (item.TryGetValue(PdfName.Intern("First"), out PdfPrimitive? child))
            {
                pending.Push(child);
            }

            if (store.Resolve(item.TryGetValue(PdfName.Intern("Title"), out PdfPrimitive? t) ? t : PdfNull.Value) is not PdfString title
                || !index.TryGetValue(itemRef.ObjectId.ObjectNumber, out int at))
            {
                continue;
            }

            int before = removed;
            string scrubbed = Remove(title.ToTextString(), rules, xml: false, ref removed);
            if (removed == before)
            {
                continue;
            }

            PdfDictionary copy = new PdfDictionary();
            foreach (KeyValuePair<PdfName, PdfPrimitive> entry in item)
            {
                copy.Set(entry.Key, entry.Value);
            }

            copy.Set(PdfName.Intern("Title"), PdfString.FromUnicode(scrubbed));
            objects[at] = new PdfIndirectObject(itemRef.ObjectId, copy);
        }

        return removed;
    }

    // The XMP packet with the matches in its text removed, written unfiltered
    // (as PDF/A-1 wants a metadata stream). A match that would cut through
    // markup is left; a packet that cannot be decoded is removed whole.
    private static int ScrubXmp(
        PdfStream xmp, PdfObjectId id, List<PatternRule> rules,
        List<PdfIndirectObject> objects, int at, FilterPipeline pipeline)
    {
        PdfDictionary dict = new PdfDictionary();
        foreach (KeyValuePair<PdfName, PdfPrimitive> entry in xmp.Dictionary)
        {
            if (entry.Key.Value is not ("Filter" or "DecodeParms" or "Length"))
            {
                dict.Set(entry.Key, entry.Value);
            }
        }

        string text;
        try
        {
            text = Encoding.UTF8.GetString(Decode(xmp, pipeline));
        }
        catch (FilterException)
        {
            objects[at] = new PdfIndirectObject(id, new PdfStream(dict, Array.Empty<byte>()));
            return 1;
        }

        int removed = 0;
        string scrubbed = Remove(text, rules, xml: true, ref removed);
        if (removed > 0)
        {
            objects[at] = new PdfIndirectObject(id, new PdfStream(dict, Encoding.UTF8.GetBytes(scrubbed)));
        }

        return removed;
    }

    private static byte[] Decode(PdfStream stream, FilterPipeline pipeline)
    {
        PdfPrimitive? filter = stream.Filter;
        if (filter is PdfName single)
        {
            return pipeline.Decode(FilterRegistry.ResolveAlias(single.Value), stream.RawBytes, null);
        }

        byte[] data = stream.RawBytes;
        if (filter is PdfArray chain)
        {
            for (int i = 0; i < chain.Count; i++)
            {
                if (chain[i] is PdfName name)
                {
                    data = pipeline.Decode(FilterRegistry.ResolveAlias(name.Value), data, null);
                }
            }
        }

        return data;
    }

    // Removes every match each rule's validator accepts. In XML, a match
    // containing '<' or '>' is left, so the markup stays well formed.
    private static string Remove(string text, List<PatternRule> rules, bool xml, ref int removed)
    {
        int count = 0;
        foreach (PatternRule rule in rules)
        {
            Regex regex = rule.Regex;
            text = regex.Replace(text, match =>
            {
                if ((xml && (match.Value.Contains('<', StringComparison.Ordinal) || match.Value.Contains('>', StringComparison.Ordinal)))
                    || match.Length == 0
                    || (rule.Validator is not null && !rule.Validator(match.Value)))
                {
                    return match.Value;
                }

                count++;
                return string.Empty;
            });
        }

        removed += count;
        return text;
    }
}
