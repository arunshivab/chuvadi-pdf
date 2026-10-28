// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.6 — Actions (incl. §12.6.3 trigger events)
//        PDF 32000-1:2008 §7.11.4 — Embedded file streams
//        PDF 32000-1:2008 §12.7.8 — XFA forms
// PHASE: 3.24 — embedding API (A2): disarm active content on output.

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Operations;

/// <summary>Options for <see cref="PdfSanitizer"/>. The defaults disarm everything.</summary>
public sealed class SanitizeOptions
{
    /// <summary>
    /// Gets a value indicating whether embedded files leave the document: the
    /// <c>/EmbeddedFiles</c> name tree, associated files (<c>/AF</c>), the
    /// embedded streams of file specifications (<c>/EF</c>, <c>/RF</c>),
    /// portfolios (<c>/Collection</c>) and file-attachment annotations.
    /// Default true.
    /// </summary>
    public bool RemoveEmbeddedFiles { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether the XFA form packets (which carry
    /// scripts) leave the AcroForm. A hybrid form keeps its AcroForm fields
    /// and appearances; an XFA-only form loses its dynamic layout. Default true.
    /// </summary>
    public bool RemoveXfa { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether multimedia annotations (<c>RichMedia</c>,
    /// <c>Screen</c>, <c>Movie</c>, <c>Sound</c>, <c>3D</c>) leave the pages.
    /// Default true.
    /// </summary>
    public bool RemoveMultimedia { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether <c>URI</c> link actions are kept (they
    /// open a web address only when the reader clicks the link). Default false:
    /// removed, so the output never points anywhere outside itself.
    /// </summary>
    public bool KeepUriLinks { get; init; }

    /// <summary>
    /// Gets a value indicating whether an encrypted document may be sanitized.
    /// The output is written without encryption. Default false: an encrypted
    /// document throws <see cref="OperationsException"/>.
    /// </summary>
    public bool AllowEncryptedInput { get; init; }
}

/// <summary>What <see cref="PdfSanitizer"/> found (and, when sanitizing, removed).</summary>
public sealed class SanitizeReport
{
    /// <summary>Gets the number of actions removed (link, outline, widget, page and document actions, including <c>/OpenAction</c>).</summary>
    public int ActionsRemoved { get; internal set; }

    /// <summary>Gets the number of additional-actions dictionaries (<c>/AA</c>) removed.</summary>
    public int AdditionalActionsRemoved { get; internal set; }

    /// <summary>Gets the number of document-level JavaScript name trees removed.</summary>
    public int JavaScriptTreesRemoved { get; internal set; }

    /// <summary>Gets the number of embedded-file entries removed (name trees, <c>/AF</c>, <c>/EF</c>, <c>/RF</c>, portfolios).</summary>
    public int EmbeddedFilesRemoved { get; internal set; }

    /// <summary>Gets the number of annotations removed (file attachments, multimedia).</summary>
    public int AnnotationsRemoved { get; internal set; }

    /// <summary>Gets a value indicating whether XFA packets were removed.</summary>
    public bool XfaRemoved { get; internal set; }

    /// <summary>Gets a value indicating whether anything active was found.</summary>
    public bool FoundActiveContent =>
        ActionsRemoved + AdditionalActionsRemoved + JavaScriptTreesRemoved + EmbeddedFilesRemoved + AnnotationsRemoved > 0
        || XfaRemoved;
}

/// <summary>
/// Writes a disarmed copy of a document: no JavaScript, no actions that run
/// on open or on any event, no launch, import, submit or remote actions, no
/// embedded files, no XFA scripts and no multimedia — only what is needed to
/// show the pages. In-document navigation (<c>GoTo</c>, the four page-turning
/// named actions, threads, layer toggles, transitions, hide) is kept.
/// </summary>
/// <remarks>
/// Rendering and text extraction never execute any of this content (see
/// docs/EMBEDDING.md); sanitizing protects whoever opens the output in
/// another viewer. The output is a full rewrite: digital signatures no longer
/// validate, and the file is written unencrypted.
/// </remarks>
public static class PdfSanitizer
{
    private static readonly HashSet<string> SafeActions = new HashSet<string>(StringComparer.Ordinal)
    {
        "GoTo", "Thread", "SetOCGState", "Trans", "Hide", "Named",
    };

    private static readonly HashSet<string> SafeNamedActions = new HashSet<string>(StringComparer.Ordinal)
    {
        "NextPage", "PrevPage", "FirstPage", "LastPage",
    };

    private static readonly HashSet<string> MultimediaAnnotations = new HashSet<string>(StringComparer.Ordinal)
    {
        "RichMedia", "Screen", "Movie", "Sound", "3D",
    };

    /// <summary>
    /// Writes a disarmed copy of <paramref name="document"/> to <paramref name="output"/>.
    /// </summary>
    /// <param name="document">The document to sanitize. It is not changed.</param>
    /// <param name="output">The stream to write the sanitized PDF to.</param>
    /// <param name="options">What to remove; null for the defaults (everything).</param>
    /// <returns>What was removed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> or <paramref name="output"/> is null.</exception>
    /// <exception cref="OperationsException">
    /// The document is encrypted and <see cref="SanitizeOptions.AllowEncryptedInput"/> is false.
    /// </exception>
    public static SanitizeReport Sanitize(PdfDocument document, Stream output, SanitizeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(output);
        options ??= new SanitizeOptions();
        if (document.Encryption is not null && !options.AllowEncryptedInput)
        {
            throw new OperationsException(
                "The document is encrypted; set SanitizeOptions.AllowEncryptedInput to write a decrypted, sanitized copy.");
        }

        // Copy everything reachable from the trailer, renumbered densely.
        Dictionary<int, PdfPrimitive> reachable = new Dictionary<int, PdfPrimitive>();
        foreach (PdfName key in new[] { PdfName.Root, PdfName.Intern("Info") })
        {
            if (document.Trailer.TryGetValue(key, out PdfPrimitive? value))
            {
                PdfCompressor.Collect(value, document.Objects, reachable);
            }
        }

        Dictionary<int, int> remap = new Dictionary<int, int>(reachable.Count);
        int next = 1;
        foreach (int number in reachable.Keys)
        {
            remap[number] = next++;
        }

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>(reachable.Count);
        Dictionary<int, PdfPrimitive> byNumber = new Dictionary<int, PdfPrimitive>(reachable.Count);
        foreach (KeyValuePair<int, PdfPrimitive> entry in reachable)
        {
            PdfPrimitive copy = PdfCompressor.DeepCopy(entry.Value, remap);
            objects.Add(new PdfIndirectObject(new PdfObjectId(remap[entry.Key], 0), copy));
            byNumber[remap[entry.Key]] = copy;
        }

        PdfDictionary trailer = new PdfDictionary();
        foreach (PdfName key in new[] { PdfName.Root, PdfName.Intern("Info") })
        {
            if (document.Trailer.TryGetValue(key, out PdfPrimitive? value))
            {
                trailer.Set(key, PdfCompressor.DeepCopy(value, remap));
            }
        }

        if (document.Trailer.TryGetValue(PdfName.Intern("ID"), out PdfPrimitive? fileId))
        {
            trailer.Set(PdfName.Intern("ID"), PdfCompressor.DeepCopy(fileId, remap));
        }

        SanitizeReport report = new SanitizeReport();
        HashSet<PdfStream> embedded = Disarm(trailer, byNumber, options, report, apply: true);
        PdfCompressor.GarbageCollect(objects, trailer);

        // Embedded file streams still reachable some other way (a private
        // key, an unusual parent) keep their dictionary but lose their data.
        for (int i = 0; i < objects.Count; i++)
        {
            if (objects[i].Value is PdfStream stream && embedded.Contains(stream))
            {
                PdfDictionary dict = new PdfDictionary();
                foreach (KeyValuePair<PdfName, PdfPrimitive> entry in stream.Dictionary)
                {
                    dict.Set(entry.Key, entry.Value);
                }

                dict.Remove(PdfName.Intern("Filter"));
                dict.Remove(PdfName.Intern("DecodeParms"));
                dict.Remove(PdfName.Intern("Params"));
                dict.Set(PdfName.Length, 0);
                objects[i] = new PdfIndirectObject(objects[i].Id, new PdfStream(dict, Array.Empty<byte>()));
            }
        }

        PdfWriter.Write(output, objects, trailer);
        return report;
    }

    /// <summary>
    /// Reports the active content in <paramref name="document"/> without
    /// changing or writing anything: what <see cref="Sanitize"/> would remove
    /// with the same options.
    /// </summary>
    /// <param name="document">The document to inspect.</param>
    /// <param name="options">What counts as active; null for the defaults.</param>
    /// <returns>What was found.</returns>
    public static SanitizeReport Inspect(PdfDocument document, SanitizeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        options ??= new SanitizeOptions();
        SanitizeReport report = new SanitizeReport();
        Dictionary<int, PdfPrimitive> reachable = new Dictionary<int, PdfPrimitive>();
        PdfDictionary trailer = new PdfDictionary();
        foreach (PdfName key in new[] { PdfName.Root, PdfName.Intern("Info") })
        {
            if (document.Trailer.TryGetValue(key, out PdfPrimitive? value))
            {
                PdfCompressor.Collect(value, document.Objects, reachable);
                trailer.Set(key, value);
            }
        }

        Disarm(trailer, reachable, options, report, apply: false);
        return report;
    }

    // Walks every dictionary reachable from the trailer once, removing (or,
    // when apply is false, only counting) the active entries. What is removed
    // is not walked into, so inspecting reports exactly what sanitizing does.
    // Returns the embedded file streams found outside the removed entries.
    private static HashSet<PdfStream> Disarm(
        PdfDictionary trailer, Dictionary<int, PdfPrimitive> byNumber, SanitizeOptions options,
        SanitizeReport report, bool apply)
    {
        HashSet<PdfDictionary> seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        HashSet<int> seenRefs = new HashSet<int>();
        HashSet<PdfStream> embedded = new HashSet<PdfStream>(ReferenceEqualityComparer.Instance);
        Removals removed = new Removals();
        Stack<PdfPrimitive> work = new Stack<PdfPrimitive>();
        PdfDictionary? catalog = Resolve(trailer.GetAs<PdfPrimitive>(PdfName.Root), byNumber) as PdfDictionary;
        if (catalog is not null)
        {
            DisarmCatalog(catalog, byNumber, options, report, apply, removed);
        }

        work.Push(trailer);
        while (work.Count > 0)
        {
            PdfPrimitive item = work.Pop();
            switch (item)
            {
                case PdfReference reference:
                    if (seenRefs.Add(reference.ObjectId.ObjectNumber)
                        && byNumber.TryGetValue(reference.ObjectId.ObjectNumber, out PdfPrimitive? target))
                    {
                        work.Push(target);
                    }

                    break;
                case PdfStream stream:
                    if (options.RemoveEmbeddedFiles
                        && Resolve(stream.Dictionary.GetAs<PdfPrimitive>(PdfName.Type), byNumber) is PdfName { Value: "EmbeddedFile" }
                        && embedded.Add(stream))
                    {
                        report.EmbeddedFilesRemoved++;
                    }

                    work.Push(stream.Dictionary);
                    break;
                case PdfArray array:
                    for (int i = 0; i < array.Count; i++)
                    {
                        if (!removed.Annotations.Contains(array[i]))
                        {
                            work.Push(array[i]);
                        }
                    }

                    break;
                case PdfDictionary dict when seen.Add(dict):
                    DisarmDictionary(dict, byNumber, options, report, apply, removed);
                    foreach (KeyValuePair<PdfName, PdfPrimitive> entry in dict)
                    {
                        if (!removed.Contains(dict, entry.Key.Value))
                        {
                            work.Push(entry.Value);
                        }
                    }

                    break;
                default:
                    break;
            }
        }

        return embedded;
    }

    // The entries a walk has removed (or would remove), so it does not
    // descend into them.
    private sealed class Removals
    {
        private readonly HashSet<(PdfDictionary Dict, string Key)> _keys =
            new HashSet<(PdfDictionary Dict, string Key)>(new KeyComparer());

        internal HashSet<PdfPrimitive> Annotations { get; } = new HashSet<PdfPrimitive>(ReferenceEqualityComparer.Instance);

        internal void Add(PdfDictionary dict, string key) => _keys.Add((dict, key));

        internal bool Contains(PdfDictionary dict, string key) => _keys.Contains((dict, key));

        private sealed class KeyComparer : IEqualityComparer<(PdfDictionary Dict, string Key)>
        {
            public bool Equals((PdfDictionary Dict, string Key) x, (PdfDictionary Dict, string Key) y) =>
                ReferenceEquals(x.Dict, y.Dict) && string.Equals(x.Key, y.Key, StringComparison.Ordinal);

            public int GetHashCode((PdfDictionary Dict, string Key) obj) =>
                HashCode.Combine(ReferenceEqualityComparer.Instance.GetHashCode(obj.Dict), StringComparer.Ordinal.GetHashCode(obj.Key));
        }
    }

    private static void DisarmCatalog(
        PdfDictionary catalog, Dictionary<int, PdfPrimitive> byNumber, SanitizeOptions options,
        SanitizeReport report, bool apply, Removals removed)
    {
        if (catalog.ContainsKey(PdfName.Intern("OpenAction")))
        {
            report.ActionsRemoved++;
            Remove(catalog, "OpenAction", apply, removed);
        }

        if (Resolve(catalog.GetAs<PdfPrimitive>(PdfName.Intern("Names")), byNumber) is PdfDictionary names)
        {
            if (names.ContainsKey(PdfName.Intern("JavaScript")))
            {
                report.JavaScriptTreesRemoved++;
                Remove(names, "JavaScript", apply, removed);
            }

            if (options.RemoveEmbeddedFiles && names.ContainsKey(PdfName.Intern("EmbeddedFiles")))
            {
                report.EmbeddedFilesRemoved++;
                Remove(names, "EmbeddedFiles", apply, removed);
            }
        }

        if (options.RemoveEmbeddedFiles && catalog.ContainsKey(PdfName.Intern("Collection")))
        {
            report.EmbeddedFilesRemoved++;
            Remove(catalog, "Collection", apply, removed);
        }

        if (options.RemoveXfa
            && Resolve(catalog.GetAs<PdfPrimitive>(PdfName.Intern("AcroForm")), byNumber) is PdfDictionary acroForm
            && acroForm.ContainsKey(PdfName.Intern("XFA")))
        {
            report.XfaRemoved = true;
            Remove(acroForm, "XFA", apply, removed);
        }
    }

    private static void DisarmDictionary(
        PdfDictionary dict, Dictionary<int, PdfPrimitive> byNumber, SanitizeOptions options,
        SanitizeReport report, bool apply, Removals removed)
    {
        // Trigger events (page open/close, field keystroke/format/calculate,
        // annotation enter/exit/focus, document will-save/print ...) are all
        // actions run without a click: remove them whole.
        if (dict.ContainsKey(PdfName.Intern("AA")))
        {
            report.AdditionalActionsRemoved++;
            Remove(dict, "AA", apply, removed);
        }

        // A clicked action (links, outline items, widgets; /PA is a link's
        // parent action) is kept only when it stays inside the document.
        foreach (string key in new[] { "A", "PA" })
        {
            if (dict.TryGetValue(PdfName.Intern(key), out PdfPrimitive? a)
                && Resolve(a, byNumber) is PdfDictionary action
                && !IsSafe(action, byNumber, options, new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance)))
            {
                report.ActionsRemoved++;
                Remove(dict, key, apply, removed);
            }
        }

        if (options.RemoveEmbeddedFiles)
        {
            foreach (string key in new[] { "EF", "RF", "AF" })
            {
                if (dict.ContainsKey(PdfName.Intern(key)))
                {
                    report.EmbeddedFilesRemoved++;
                    Remove(dict, key, apply, removed);
                }
            }
        }

        // Annotations that carry files or run media leave the page.
        if (Resolve(dict.GetAs<PdfPrimitive>(PdfName.Intern("Annots")), byNumber) is PdfArray annots)
        {
            List<PdfPrimitive> kept = new List<PdfPrimitive>(annots.Count);
            for (int i = 0; i < annots.Count; i++)
            {
                if (Resolve(annots[i], byNumber) is PdfDictionary annot
                    && Resolve(annot.GetAs<PdfPrimitive>(PdfName.Subtype), byNumber) is PdfName subtype
                    && ((options.RemoveEmbeddedFiles && subtype.Value == "FileAttachment")
                        || (options.RemoveMultimedia && MultimediaAnnotations.Contains(subtype.Value))))
                {
                    report.AnnotationsRemoved++;
                    removed.Annotations.Add(annots[i]);
                    continue;
                }

                kept.Add(annots[i]);
            }

            if (apply && kept.Count != annots.Count)
            {
                dict.Set(PdfName.Intern("Annots"), new PdfArray(kept.ToArray()));
            }
        }
    }

    // True when the action, and every action chained after it with /Next, is
    // in-document navigation (or a URI link when those are kept).
    private static bool IsSafe(
        PdfDictionary action, Dictionary<int, PdfPrimitive> byNumber, SanitizeOptions options,
        HashSet<PdfDictionary> visited)
    {
        if (!visited.Add(action))
        {
            return true;
        }

        if (Resolve(action.GetAs<PdfPrimitive>(PdfName.Intern("S")), byNumber) is not PdfName type)
        {
            return false;
        }

        bool safe = type.Value switch
        {
            "Named" => Resolve(action.GetAs<PdfPrimitive>(PdfName.Intern("N")), byNumber) is PdfName n
                && SafeNamedActions.Contains(n.Value),
            "URI" => options.KeepUriLinks,
            _ => SafeActions.Contains(type.Value),
        };
        if (!safe)
        {
            return false;
        }

        switch (Resolve(action.GetAs<PdfPrimitive>(PdfName.Intern("Next")), byNumber))
        {
            case PdfDictionary nextAction:
                return IsSafe(nextAction, byNumber, options, visited);
            case PdfArray nextActions:
                for (int i = 0; i < nextActions.Count; i++)
                {
                    if (Resolve(nextActions[i], byNumber) is not PdfDictionary chained
                        || !IsSafe(chained, byNumber, options, visited))
                    {
                        return false;
                    }
                }

                return true;
            default:
                return true;
        }
    }

    private static PdfPrimitive? Resolve(PdfPrimitive? value, Dictionary<int, PdfPrimitive> byNumber) =>
        value is PdfReference reference
            ? byNumber.TryGetValue(reference.ObjectId.ObjectNumber, out PdfPrimitive? target) ? target : null
            : value;

    private static void Remove(PdfDictionary dict, string key, bool apply, Removals removed)
    {
        removed.Add(dict, key);
        if (apply)
        {
            dict.Remove(PdfName.Intern(key));
        }
    }
}
