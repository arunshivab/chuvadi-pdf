// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.31 — one all-fonts-embedded verdict for a whole document (Print
//        item d): every page, form XObject, pattern, Type 3 glyph procedure
//        and annotation appearance, so a printer can decide whether the PDF
//        can be sent as it is.
// SPEC:  PDF 32000-1:2008 §7.8.3 (resources), §9.9 (embedded font programs),
//        §12.5.5 (appearance streams), §12.7.2 (/NeedAppearances).

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>The whole-document answer of <see cref="FontEmbeddingReport"/>.</summary>
public enum FontEmbeddingVerdict
{
    /// <summary>Every font the document can use is embedded (Type 3 fonts count as embedded).</summary>
    AllEmbedded = 0,

    /// <summary>At least one font is not embedded; see <see cref="FontEmbeddingReport.NotEmbedded"/>.</summary>
    NotAllEmbedded = 1,

    /// <summary>Part of the document could not be read, so the answer is not known.</summary>
    Unknown = 2,
}

/// <summary>A font that is not embedded, and where it is used.</summary>
/// <param name="PageNumber">The 1-based page.</param>
/// <param name="Location">Where: "page", "form XObject 12 0 R", "pattern …", "Type 3 font …" or "annotation …".</param>
/// <param name="ResourceName">The font's resource name, for example "F1".</param>
/// <param name="BaseFont">The <c>/BaseFont</c> name.</param>
/// <param name="Subtype">The font <c>/Subtype</c>.</param>
public sealed record FontUse(int PageNumber, string Location, string ResourceName, string BaseFont, string Subtype);

/// <summary>
/// Checks whether every font a document can use is embedded (3.31). The check
/// reads resources, so a font that a resource dictionary names but no content
/// uses still counts: the answer errs towards <see cref="FontEmbeddingVerdict.NotAllEmbedded"/>.
/// Standard 14 fonts that are not embedded count as not embedded.
/// </summary>
public sealed class FontEmbeddingReport
{
    private const int MaxDepth = 32;

    private FontEmbeddingReport(FontEmbeddingVerdict verdict, IReadOnlyList<FontUse> notEmbedded, bool needsGenerated)
    {
        Verdict = verdict;
        NotEmbedded = notEmbedded;
        NeedsGeneratedAppearances = needsGenerated;
    }

    /// <summary>The verdict.</summary>
    public FontEmbeddingVerdict Verdict { get; }

    /// <summary>
    /// Each font that is not embedded, with the first page and place it was
    /// found (resources shared by several pages are read once), in page order.
    /// </summary>
    public IReadOnlyList<FontUse> NotEmbedded { get; }

    /// <summary>
    /// True when some form field or comment is drawn from its own entries
    /// rather than its stored appearance (<c>/NeedAppearances</c> is true for
    /// text or choice fields, or an annotation has no <c>/AP</c>). Such a PDF
    /// does not print as it is, whatever the verdict.
    /// </summary>
    public bool NeedsGeneratedAppearances { get; }

    /// <summary>Checks <paramref name="document"/>.</summary>
    /// <param name="document">The document.</param>
    /// <returns>The report.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is null.</exception>
    public static FontEmbeddingReport Check(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Walker w = new Walker(document.Objects);
        bool unknown = false;
        bool needsGenerated = false;
        PdfDictionary? acroForm = document.Catalog.TryGetValue(PdfName.Intern("AcroForm"), out PdfPrimitive? af)
            ? document.Objects.ResolveAs<PdfDictionary>(af ?? PdfNull.Value)
            : null;
        bool needAppearances = acroForm is not null
            && acroForm.TryGetValue(PdfName.Intern("NeedAppearances"), out PdfPrimitive? na)
            && document.Objects.Resolve(na ?? PdfNull.Value) is PdfBoolean { Value: true };

        for (int i = 0; i < document.PageCount; i++)
        {
            try
            {
                PdfPage page = document.Pages[i];
                w.Page = page.PageNumber;
                w.Resources(page.Resources, "page", 0);
                if (page.Dictionary.TryGetValue(PdfName.Intern("Annots"), out PdfPrimitive? ar)
                    && document.Objects.ResolveAs<PdfArray>(ar ?? PdfNull.Value) is PdfArray annots)
                {
                    foreach (PdfPrimitive a in annots)
                    {
                        if (document.Objects.ResolveAs<PdfDictionary>(a) is not PdfDictionary annot)
                        {
                            continue;
                        }

                        string subtype = document.Objects.ResolveAs<PdfName>(annot.TryGetValue(PdfName.Subtype, out PdfPrimitive? st) ? st ?? PdfNull.Value : PdfNull.Value)?.Value ?? string.Empty;
                        string where = "annotation (" + subtype + ")";
                        bool hasAp = w.Appearances(annot, where);
                        if (subtype == "Widget")
                        {
                            string? ft = InheritedName(annot, "FT", document.Objects);
                            if (ft is not null and not "Sig" && (!hasAp || (needAppearances && ft is "Tx" or "Ch")))
                            {
                                needsGenerated = true;
                            }
                        }
                        else if (!hasAp && subtype is not ("Link" or "Popup" or "Sound" or "Movie" or "Screen" or "3D" or "RichMedia" or "PrinterMark" or "TrapNet" or "Watermark" or "Redact" or ""))
                        {
                            needsGenerated = true;
                        }
                    }
                }
            }
            catch (Exception ex) when (!PdfWorkScope.IsAbort(ex))
            {
                unknown = true;
            }
        }

        unknown |= w.Unknown;
        FontEmbeddingVerdict verdict = unknown ? FontEmbeddingVerdict.Unknown
            : w.Found.Count > 0 ? FontEmbeddingVerdict.NotAllEmbedded
            : FontEmbeddingVerdict.AllEmbedded;
        return new FontEmbeddingReport(verdict, w.Found, needsGenerated);
    }

    private static string? InheritedName(PdfDictionary dict, string key, PdfObjectStore objects)
    {
        PdfDictionary? d = dict;
        for (int depth = 0; d is not null && depth < MaxDepth; depth++)
        {
            if (d.TryGetValue(PdfName.Intern(key), out PdfPrimitive? v) && objects.ResolveAs<PdfName>(v ?? PdfNull.Value) is PdfName n)
            {
                return n.Value;
            }

            d = d.TryGetValue(PdfName.Intern("Parent"), out PdfPrimitive? p) ? objects.ResolveAs<PdfDictionary>(p ?? PdfNull.Value) : null;
        }

        return null;
    }

    private sealed class Walker
    {
        private readonly PdfObjectStore _objects;
        private readonly HashSet<object> _seenResources = new HashSet<object>(ReferenceEqualityComparer.Instance);
        private readonly HashSet<(int, object)> _reportedFonts = new HashSet<(int, object)>();

        internal Walker(PdfObjectStore objects)
        {
            _objects = objects;
        }

        internal int Page { get; set; }

        internal bool Unknown { get; private set; }

        internal List<FontUse> Found { get; } = new List<FontUse>();

        internal void Resources(PdfDictionary? resources, string where, int depth)
        {
            if (resources is null || depth > MaxDepth || !_seenResources.Add(resources))
            {
                return;
            }

            if (Dict(resources, "Font") is PdfDictionary fonts)
            {
                foreach (KeyValuePair<PdfName, PdfPrimitive> entry in fonts)
                {
                    if (_objects.ResolveAs<PdfDictionary>(entry.Value) is not PdfDictionary font)
                    {
                        Unknown = true;
                        continue;
                    }

                    Font(entry.Key.Value, font, where, depth);
                }
            }

            if (Dict(resources, "XObject") is PdfDictionary xobjects)
            {
                foreach (KeyValuePair<PdfName, PdfPrimitive> entry in xobjects)
                {
                    if (_objects.Resolve(entry.Value) is PdfStream xs
                        && _objects.ResolveAs<PdfName>(Get(xs.Dictionary, "Subtype"))?.Value == "Form")
                    {
                        Resources(Dict(xs.Dictionary, "Resources"), Describe("form XObject", entry.Value), depth + 1);
                    }
                }
            }

            if (Dict(resources, "Pattern") is PdfDictionary patterns)
            {
                foreach (KeyValuePair<PdfName, PdfPrimitive> entry in patterns)
                {
                    PdfPrimitive p = _objects.Resolve(entry.Value);
                    PdfDictionary? pd = p is PdfStream ps ? ps.Dictionary : p as PdfDictionary;
                    Resources(pd is null ? null : Dict(pd, "Resources"), Describe("pattern", entry.Value), depth + 1);
                }
            }
        }

        // Every appearance stream of the annotation (/N, /R, /D and their
        // states). Returns true when it has a normal appearance.
        internal bool Appearances(PdfDictionary annot, string where)
        {
            if (Dict(annot, "AP") is not PdfDictionary ap)
            {
                return false;
            }

            bool hasNormal = false;
            foreach (string key in new[] { "N", "R", "D" })
            {
                PdfPrimitive v = _objects.Resolve(Get(ap, key));
                if (v is PdfStream s)
                {
                    hasNormal |= key == "N";
                    Resources(Dict(s.Dictionary, "Resources"), where, 1);
                }
                else if (v is PdfDictionary states)
                {
                    foreach (KeyValuePair<PdfName, PdfPrimitive> st in states)
                    {
                        if (_objects.Resolve(st.Value) is PdfStream ss)
                        {
                            hasNormal |= key == "N";
                            Resources(Dict(ss.Dictionary, "Resources"), where, 1);
                        }
                    }
                }
            }

            return hasNormal;
        }

        private void Font(string name, PdfDictionary font, string where, int depth)
        {
            string subtype = _objects.ResolveAs<PdfName>(Get(font, "Subtype"))?.Value ?? string.Empty;
            if (subtype == "Type3")
            {
                Resources(Dict(font, "Resources"), "Type 3 font /" + name, depth + 1);
                return;
            }

            PdfDictionary target = font;
            if (subtype == "Type0")
            {
                if (_objects.ResolveAs<PdfArray>(Get(font, "DescendantFonts")) is not PdfArray { Count: > 0 } desc
                    || _objects.ResolveAs<PdfDictionary>(desc[0]) is not PdfDictionary d)
                {
                    Unknown = true;
                    return;
                }

                target = d;
            }

            PdfDictionary? fd = Dict(target, "FontDescriptor");
            bool embedded = fd is not null
                && (fd.ContainsKey(PdfName.Intern("FontFile")) || fd.ContainsKey(PdfName.Intern("FontFile2")) || fd.ContainsKey(PdfName.Intern("FontFile3")));
            if (!embedded && _reportedFonts.Add((Page, font)))
            {
                string baseFont = _objects.ResolveAs<PdfName>(Get(font, "BaseFont"))?.Value ?? string.Empty;
                Found.Add(new FontUse(Page, where, name, baseFont, subtype));
            }
        }

        private PdfDictionary? Dict(PdfDictionary d, string key) => _objects.ResolveAs<PdfDictionary>(Get(d, key));

        private static PdfPrimitive Get(PdfDictionary d, string key) =>
            d.TryGetValue(PdfName.Intern(key), out PdfPrimitive? v) && v is not null ? v : PdfNull.Value;

        private static string Describe(string what, PdfPrimitive value) =>
            value is PdfReference r
                ? what + " " + r.ObjectNumber.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + r.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture) + " R"
                : what + " (direct)";
    }
}
