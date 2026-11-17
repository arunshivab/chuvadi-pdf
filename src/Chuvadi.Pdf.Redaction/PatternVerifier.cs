// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.7.2 (document catalog), §7.11 (file
//        specifications), §14.9.3–14.9.5 (alternate and replacement text)
// PHASE: 3.35.0 (D-397, audit A-08 RED-09) — verify a pattern redaction:
//        what of a document still matches the patterns.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Filters;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Redaction;

// Finds what still matches the patterns: on each page, the text a viewer
// shows (as Redactor matches it) and the annotations; outside the pages,
// every string in an object reachable from the trailer, the replacement
// text inside content streams, the XMP metadata, JavaScript streams and
// the attachments, which are reported when they cannot be read as text.
internal static class PatternVerifier
{
    private const int MaxObjects = 1_000_000;

    private static readonly string[] ReplacementTextKeys = { "ActualText", "Alt", "E" };

    internal static List<RedactionSurvivor> Find(PdfDocument document, IReadOnlyList<PatternRule> patterns)
    {
        List<RedactionSurvivor> survivors = new List<RedactionSurvivor>();
        for (int p = 0; p < document.PageCount; p++)
        {
            PdfWorkScope.ThrowIfCancellationRequested();
            List<PatternRule> rules = new List<PatternRule>();
            foreach (PatternRule rule in patterns)
            {
                if (rule.AppliesToPage(p))
                {
                    rules.Add(rule);
                }
            }

            if (rules.Count == 0)
            {
                continue;
            }

            PatternLocator.PageMatches matches = PatternLocator.Locate(document, p, rules);
            int found = 0;
            foreach (RectangleF box in matches.OverlayBoxes)
            {
                string? text = found < matches.Found.Count ? matches.Found[found] : null;
                survivors.Add(new RedactionSurvivor(p, RedactionSurvivorKind.Text, box, text));
                found++;
            }

            foreach (PdfDictionary annotation in matches.Annotations)
            {
                survivors.Add(new RedactionSurvivor(p, RedactionSurvivorKind.Annotation, AnnotationRect(document, annotation), null));
            }

            foreach (string text in matches.Unplaced)
            {
                survivors.Add(new RedactionSurvivor(p, RedactionSurvivorKind.Text, default, text));
            }
        }

        List<PatternRule> documentRules = new List<PatternRule>();
        foreach (PatternRule rule in patterns)
        {
            if (rule.PageIndices is null)
            {
                documentRules.Add(rule);
            }
        }

        if (documentRules.Count > 0)
        {
            FindOutsidePages(document, documentRules, survivors);
        }

        return survivors;
    }

    private static void FindOutsidePages(PdfDocument document, List<PatternRule> rules, List<RedactionSurvivor> survivors)
    {
        FilterPipeline pipeline = FilterRegistry.CreateDefaultPipeline();
        HashSet<int> visited = new HashSet<int>();
        HashSet<PdfPrimitive> seenDirect = new HashSet<PdfPrimitive>(ReferenceEqualityComparer.Instance);
        Stack<(PdfPrimitive Value, string? Key)> pending = new Stack<(PdfPrimitive Value, string? Key)>();
        pending.Push((document.Trailer, null));
        while (pending.Count > 0 && visited.Count < MaxObjects)
        {
            PdfWorkScope.ThrowIfCancellationRequested();
            (PdfPrimitive value, string? key) = pending.Pop();
            if (value is PdfReference reference)
            {
                if (!visited.Add(reference.ObjectId.ObjectNumber))
                {
                    continue;
                }

                PdfPrimitive resolved;
                try
                {
                    resolved = document.Objects.Resolve(reference);
                }
                catch (PdfException)
                {
                    continue;
                }

                pending.Push((resolved, key));
                continue;
            }

            switch (value)
            {
                case PdfString text:
                    if (Matches(text.ToTextString(), rules, out string? match))
                    {
                        survivors.Add(new RedactionSurvivor(-1, RedactionSurvivorKind.DocumentText, default, match));
                    }

                    break;
                case PdfArray array:
                    if (seenDirect.Add(array))
                    {
                        foreach (PdfPrimitive item in array)
                        {
                            pending.Push((item, key));
                        }
                    }

                    break;
                case PdfDictionary dict:
                    if (seenDirect.Add(dict))
                    {
                        PushEntries(dict, pending);
                    }

                    break;
                case PdfStream stream:
                    if (seenDirect.Add(stream))
                    {
                        PushEntries(stream.Dictionary, pending);
                        InspectStream(stream, key, rules, pipeline, survivors);
                    }

                    break;
            }
        }
    }

    private static void PushEntries(PdfDictionary dict, Stack<(PdfPrimitive Value, string? Key)> pending)
    {
        foreach (KeyValuePair<PdfName, PdfPrimitive> entry in dict)
        {
            // The pages' parent links lead back up the tree; everything is
            // reached from the catalog anyway.
            if (entry.Key.Value is "Parent" or "P")
            {
                continue;
            }

            pending.Push((entry.Value, entry.Key.Value));
        }
    }

    private static void InspectStream(
        PdfStream stream, string? key, List<PatternRule> rules, FilterPipeline pipeline, List<RedactionSurvivor> survivors)
    {
        PdfDictionary dict = stream.Dictionary;
        string? type = (dict.TryGetValue(PdfName.Type, out PdfPrimitive? t) ? t : null) is PdfName typeName ? typeName.Value : null;
        string? subtype = (dict.TryGetValue(PdfName.Subtype, out PdfPrimitive? st) ? st : null) is PdfName subtypeName ? subtypeName.Value : null;
        bool attachment = type == "EmbeddedFile" || (key is "F" or "UF" && subtype is not ("Form" or "Image"));
        bool text = type == "Metadata" || key == "JS";
        bool content = !attachment && !text
            && (key == "Contents" || subtype == "Form"
                || (dict.TryGetValue(PdfName.Intern("PatternType"), out PdfPrimitive? pt) && pt is PdfInteger { Value: 1 }));
        if (!attachment && !text && !content)
        {
            return;
        }

        byte[] data;
        try
        {
            data = Redactor.DecodeStream(stream, pipeline);
        }
        catch (Exception ex) when (ex is PdfException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            if (attachment || text)
            {
                survivors.Add(new RedactionSurvivor(-1, attachment ? RedactionSurvivorKind.Attachment : RedactionSurvivorKind.DocumentText, default, null));
            }

            return;
        }

        if (content)
        {
            foreach (string replacement in ReplacementTexts(data))
            {
                if (Matches(replacement, rules, out string? match))
                {
                    survivors.Add(new RedactionSurvivor(-1, RedactionSurvivorKind.DocumentText, default, match));
                }
            }

            return;
        }

        string? decoded = TryText(data);
        if (decoded is null)
        {
            // An attachment that is not text cannot be checked.
            if (attachment)
            {
                survivors.Add(new RedactionSurvivor(-1, RedactionSurvivorKind.Attachment, default, null));
            }

            return;
        }

        if (Matches(decoded, rules, out string? found))
        {
            survivors.Add(new RedactionSurvivor(
                -1, attachment ? RedactionSurvivorKind.Attachment : RedactionSurvivorKind.DocumentText, default, found));
        }
    }

    private static IEnumerable<string> ReplacementTexts(byte[] content)
    {
        List<string> texts = new List<string>();
        using (MemoryStream input = new MemoryStream(content, writable: false))
        using (PdfTokenizer tokens = new PdfTokenizer(input))
        {
            bool afterKey = false;
            while (true)
            {
                PdfToken token;
                try
                {
                    token = tokens.Read();
                }
                catch (PdfException)
                {
                    break;
                }

                if (token.IsEndOfStream)
                {
                    break;
                }

                if (afterKey && (token.Type == PdfTokenType.LiteralString || token.Type == PdfTokenType.HexString))
                {
                    byte[] bytes = token.Type == PdfTokenType.LiteralString
                        ? PdfString.DecodeLiteralToken(token.RawBytes)
                        : PdfString.DecodeHexToken(token.RawBytes);
                    texts.Add(new PdfString(bytes).ToTextString());
                }

                afterKey = token.Type == PdfTokenType.Name && Array.IndexOf(ReplacementTextKeys, token.RawText) >= 0;
            }
        }

        return texts;
    }

    private static string? TryText(byte[] data)
    {
        if (data.Length >= 2 && ((data[0] == 0xFE && data[1] == 0xFF) || (data[0] == 0xFF && data[1] == 0xFE)))
        {
            try
            {
                return new UnicodeEncoding(data[0] == 0xFE, false, true).GetString(data, 2, data.Length - 2);
            }
            catch (DecoderFallbackException)
            {
                return null;
            }
        }

        if (Array.IndexOf(data, (byte)0) >= 0)
        {
            return null;
        }

        try
        {
            Encoding strictUtf8 = new UTF8Encoding(false, true);
            return strictUtf8.GetString(data);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static bool Matches(string text, List<PatternRule> rules, out string? match)
    {
        foreach (PatternRule rule in rules)
        {
            foreach (Match m in rule.Regex.Matches(text))
            {
                if (m.Length > 0 && (rule.Validator is null || rule.Validator(m.Value)))
                {
                    match = m.Value;
                    return true;
                }
            }
        }

        match = null;
        return false;
    }

    private static RectangleF AnnotationRect(PdfDocument document, PdfDictionary annotation)
    {
        if (annotation.TryGetValue(PdfName.Intern("Rect"), out PdfPrimitive? value)
            && document.Objects.Resolve(value) is PdfArray rect && rect.Count >= 4)
        {
            double[] n = new double[4];
            for (int i = 0; i < 4; i++)
            {
                n[i] = document.Objects.Resolve(rect[i]) switch
                {
                    PdfInteger integer => integer.Value,
                    PdfReal real => real.Value,
                    _ => 0,
                };
            }

            return RectangleF.FromCorners(Math.Min(n[0], n[2]), Math.Min(n[1], n[3]), Math.Max(n[0], n[2]), Math.Max(n[1], n[3]));
        }

        return default;
    }
}
