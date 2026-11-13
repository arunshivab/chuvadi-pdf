// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.7.2 — Interactive form dictionary
//        PDF 32000-1:2008 §12.7.3 — Field dictionaries
// PHASE: Phase 2 — Chuvadi.Pdf.Forms
// Reads AcroForm field tree from a PDF document.

using System.Collections.Generic;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Forms;

/// <summary>
/// Reads AcroForm interactive form fields from a PDF document.
/// </summary>
/// <remarks>
/// Walks the form's <c>/Fields</c> array from <c>/Catalog/AcroForm</c>,
/// recursively resolving the field tree. Each leaf field carries its
/// fully-qualified name (ancestor partial names joined by periods),
/// type, current value, and indirect-object ID for later updating.
///
/// PDF 32000-1:2008 §12.7.2 — Interactive form dictionary.
/// </remarks>
public static class FormReader
{
    /// <summary>
    /// Returns all top-level form fields in the document. Empty when the
    /// document has no AcroForm.
    /// </summary>
    public static IReadOnlyList<FormField> GetFields(PdfDocument document)
    {
        if (document is null)
        {
            throw new System.ArgumentNullException(nameof(document));
        }

        PdfDictionary catalog = document.Catalog;
        PdfObjectStore store = document.Objects;

        if (!catalog.TryGetValue(PdfName.Intern("AcroForm"), out PdfPrimitive? acroFormRef))
        {
            return new List<FormField>();
        }

        PdfDictionary? acroForm = store.ResolveAs<PdfDictionary>(acroFormRef ?? PdfNull.Value);

        if (acroForm is null)
        {
            return new List<FormField>();
        }

        if (!acroForm.TryGetValue(PdfName.Intern("Fields"), out PdfPrimitive? fieldsPrim))
        {
            return new List<FormField>();
        }

        PdfArray? fieldsArray = store.ResolveAs<PdfArray>(fieldsPrim ?? PdfNull.Value);

        if (fieldsArray is null)
        {
            return new List<FormField>();
        }

        ReadContext context = new ReadContext(store, BuildWidgetPageMap(document));
        List<FormField> result = new List<FormField>();

        for (int i = 0; i < fieldsArray.Count; i++)
        {
            FormField? field = ReadField(fieldsArray[i], context, parentName: string.Empty, Inherited.None);

            if (field is not null)
            {
                result.Add(field);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns a flat list of every leaf field in the document, in tree order.
    /// Useful for callers that just want every fillable input.
    /// </summary>
    public static IReadOnlyList<FormField> GetLeafFields(PdfDocument document)
    {
        List<FormField> result = new List<FormField>();
        FlattenLeaves(GetFields(document), result);
        return result;
    }

    private static void FlattenLeaves(IReadOnlyList<FormField> fields, List<FormField> result)
    {
        foreach (FormField f in fields)
        {
            if (f.IsLeaf)
            {
                result.Add(f);
            }
            else
            {
                FlattenLeaves(f.Children, result);
            }
        }
    }

    // ── Recursive field reader ────────────────────────────────────────────

    // The inheritable entries a field takes from its ancestors (PDF 32000-1:2008
    // §12.7.3.1, Table 220: /FT, /Ff; Table 229: /MaxLen).
    private readonly record struct Inherited(PdfName? FieldType, int? Flags, int? MaxLength)
    {
        public static Inherited None => default;
    }

    private sealed class ReadContext
    {
        public ReadContext(PdfObjectStore store, Dictionary<int, int> widgetPages)
        {
            Store = store;
            WidgetPages = widgetPages;
        }

        public PdfObjectStore Store { get; }

        // Annotation object number → zero-based page index, from /Annots.
        public Dictionary<int, int> WidgetPages { get; }
    }

    private static Dictionary<int, int> BuildWidgetPageMap(PdfDocument document)
    {
        Dictionary<int, int> map = new Dictionary<int, int>();
        for (int i = 0; i < document.PageCount; i++)
        {
            PdfArray? annots = document.Objects.ResolveAs<PdfArray>(
                document.Pages[i].Dictionary.TryGetValue(PdfName.Intern("Annots"), out PdfPrimitive? a) ? a : PdfNull.Value);
            if (annots is null)
            {
                continue;
            }

            for (int j = 0; j < annots.Count; j++)
            {
                if (annots[j] is PdfReference reference)
                {
                    map.TryAdd(reference.ObjectId.ObjectNumber, i);
                }
            }
        }

        return map;
    }

    private static FormField? ReadField(
        PdfPrimitive fieldRef, ReadContext context, string parentName, Inherited inherited)
    {
        PdfObjectStore store = context.Store;
        PdfObjectId objId = ExtractObjectId(fieldRef);
        PdfDictionary? fieldDict = store.ResolveAs<PdfDictionary>(fieldRef);

        if (fieldDict is null)
        {
            return null;
        }

        // Partial name
        string partialName = ExtractPartialName(fieldDict);
        string fullName = string.IsNullOrEmpty(parentName)
            ? partialName
            : (string.IsNullOrEmpty(partialName) ? parentName : parentName + "." + partialName);

        // Inheritable entries (3.34.0): a field without its own /FT, /Ff or
        // /MaxLen takes its nearest ancestor's.
        Inherited own = new Inherited(
            store.ResolveAs<PdfName>(Get(fieldDict, "FT")) ?? inherited.FieldType,
            IntOf(store.Resolve(Get(fieldDict, "Ff"))) ?? inherited.Flags,
            IntOf(store.Resolve(Get(fieldDict, "MaxLen"))) ?? inherited.MaxLength);
        FormFieldType type = TypeOf(own.FieldType);

        // Value
        string? value = ExtractValue(fieldDict, store);

        // Children and widgets
        List<FormField> children = new List<FormField>();
        List<(PdfPrimitive Ref, PdfDictionary Dict)> widgetDicts = new List<(PdfPrimitive, PdfDictionary)>();

        if (fieldDict.TryGetValue(PdfName.Kids, out PdfPrimitive? kidsPrim))
        {
            PdfArray? kids = store.ResolveAs<PdfArray>(kidsPrim ?? PdfNull.Value);

            if (kids is not null)
            {
                for (int i = 0; i < kids.Count; i++)
                {
                    // A Kid with a /T entry is a child field; a Kid without one is
                    // a widget annotation of this field.
                    PdfDictionary? kidDict = store.ResolveAs<PdfDictionary>(kids[i]);

                    if (kidDict is null)
                    {
                        continue;
                    }

                    if (kidDict.TryGetValue(PdfName.Intern("T"), out _))
                    {
                        FormField? child = ReadField(kids[i], context, fullName, own);

                        if (child is not null)
                        {
                            children.Add(child);
                        }
                    }
                    else
                    {
                        widgetDicts.Add((kids[i], kidDict));
                    }
                }
            }
        }
        else if (IsWidget(fieldDict))
        {
            // The field and its only widget share one dictionary.
            widgetDicts.Add((fieldRef, fieldDict));
        }

        PdfArray? opt = store.ResolveAs<PdfArray>(Get(fieldDict, "Opt"));
        List<FormWidget> widgets = new List<FormWidget>();
        for (int i = 0; i < widgetDicts.Count; i++)
        {
            widgets.Add(ReadWidget(widgetDicts[i].Ref, widgetDicts[i].Dict, type, opt, i, context));
        }

        return new FormField(fullName, type, value, objId, children)
        {
            Widgets = widgets,
            Options = type == FormFieldType.Choice ? ReadOptions(opt, store) : System.Array.Empty<FormChoiceOption>(),
            Flags = (FormFieldFlags)(own.Flags ?? 0),
            MaxLength = own.MaxLength,
        };
    }

    private static readonly string[] AppearanceKeys = ["N", "D"];

    private static FormWidget ReadWidget(
        PdfPrimitive widgetRef, PdfDictionary widget, FormFieldType type, PdfArray? opt, int index, ReadContext context)
    {
        PdfObjectStore store = context.Store;
        PdfObjectId id = ExtractObjectId(widgetRef);
        int page = id.ObjectNumber != 0 && context.WidgetPages.TryGetValue(id.ObjectNumber, out int p) ? p : -1;

        PdfRectangle rect = new PdfRectangle(0, 0, 0, 0);
        PdfArray? r = store.ResolveAs<PdfArray>(Get(widget, "Rect"));
        if (r is not null && r.Count >= 4)
        {
            double x1 = NumberOf(store.Resolve(r[0]));
            double y1 = NumberOf(store.Resolve(r[1]));
            double x2 = NumberOf(store.Resolve(r[2]));
            double y2 = NumberOf(store.Resolve(r[3]));
            rect = new PdfRectangle(System.Math.Min(x1, x2), System.Math.Min(y1, y2), System.Math.Max(x1, x2), System.Math.Max(y1, y2));
        }

        string? onState = null;
        string? exportValue = null;
        if (type == FormFieldType.Button)
        {
            onState = OnStateOf(widget, store);
            exportValue = onState;
            if (opt is not null && index < opt.Count && store.Resolve(opt[index]) is PdfString exported)
            {
                exportValue = exported.ToTextString();
            }
        }

        return new FormWidget(page, rect, id, onState, exportValue);
    }

    // The /AP /N key other than Off: the state that shows the widget as on.
    // Falls back to /D, then to the current /AS when it is not Off.
    private static string? OnStateOf(PdfDictionary widget, PdfObjectStore store)
    {
        PdfDictionary? ap = store.ResolveAs<PdfDictionary>(Get(widget, "AP"));
        foreach (string key in AppearanceKeys)
        {
            PdfDictionary? states = ap is null ? null : store.ResolveAs<PdfDictionary>(Get(ap, key));
            if (states is null)
            {
                continue;
            }

            foreach (KeyValuePair<PdfName, PdfPrimitive> entry in states)
            {
                if (entry.Key.Value != "Off")
                {
                    return entry.Key.Value;
                }
            }
        }

        PdfName? appearanceState = store.ResolveAs<PdfName>(Get(widget, "AS"));
        return appearanceState is not null && appearanceState.Value != "Off" ? appearanceState.Value : null;
    }

    private static IReadOnlyList<FormChoiceOption> ReadOptions(PdfArray? opt, PdfObjectStore store)
    {
        if (opt is null)
        {
            return System.Array.Empty<FormChoiceOption>();
        }

        List<FormChoiceOption> options = new List<FormChoiceOption>();
        for (int i = 0; i < opt.Count; i++)
        {
            PdfPrimitive item = store.Resolve(opt[i]);
            if (item is PdfString single)
            {
                string text = single.ToTextString();
                options.Add(new FormChoiceOption(text, text));
            }
            else if (item is PdfArray pair && pair.Count >= 2
                && store.Resolve(pair[0]) is PdfString export && store.Resolve(pair[1]) is PdfString display)
            {
                options.Add(new FormChoiceOption(export.ToTextString(), display.ToTextString()));
            }
        }

        return options;
    }

    private static bool IsWidget(PdfDictionary dict) =>
        (dict.TryGetValue(PdfName.Subtype, out PdfPrimitive? subtype) && subtype is PdfName n && n.Value == "Widget")
        || dict.ContainsKey(PdfName.Intern("Rect"));

    private static PdfPrimitive Get(PdfDictionary dict, string key) =>
        dict.TryGetValue(PdfName.Intern(key), out PdfPrimitive? value) && value is not null ? value : PdfNull.Value;

    private static int? IntOf(PdfPrimitive value) => value switch
    {
        PdfInteger i => i.Value,
        PdfReal r => (int)r.Value,
        _ => null,
    };

    private static double NumberOf(PdfPrimitive value) => value switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0,
    };

    // ── Field property extraction ─────────────────────────────────────────

    // 3.34.0: names and values are PDF text strings (PDF 32000-1:2008 §7.9.2.2):
    // UTF-16BE with a byte order mark, or PDFDocEncoding. They were read as
    // Latin-1 bytes, so a Tamil or other non-Latin name or value came back as
    // mojibake.
    private static string ExtractPartialName(PdfDictionary fieldDict)
    {
        if (!fieldDict.TryGetValue(PdfName.Intern("T"), out PdfPrimitive? tPrim))
        {
            return string.Empty;
        }

        if (tPrim is PdfString s)
        {
            return s.ToTextString();
        }

        return string.Empty;
    }

    private static FormFieldType TypeOf(PdfName? fieldType) => fieldType?.Value switch
    {
        "Tx" => FormFieldType.Text,
        "Btn" => FormFieldType.Button,
        "Ch" => FormFieldType.Choice,
        "Sig" => FormFieldType.Signature,
        _ => FormFieldType.Unknown,
    };

    private static string? ExtractValue(PdfDictionary fieldDict, PdfObjectStore store)
    {
        if (!fieldDict.TryGetValue(PdfName.Intern("V"), out PdfPrimitive? vPrim))
        {
            return null;
        }

        PdfPrimitive resolved = store.Resolve(vPrim ?? PdfNull.Value);

        if (resolved is PdfString s)
        {
            return s.ToTextString();
        }

        if (resolved is PdfName n)
        {
            return n.Value;
        }

        if (resolved is PdfArray arr)
        {
            // Multi-select choice values
            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < arr.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                PdfPrimitive item = store.Resolve(arr[i]);

                if (item is PdfString itemStr)
                {
                    sb.Append(itemStr.ToTextString());
                }
                else if (item is PdfName itemName)
                {
                    sb.Append(itemName.Value);
                }
            }

            return sb.ToString();
        }

        return null;
    }

    private static PdfObjectId ExtractObjectId(PdfPrimitive primitive)
    {
        if (primitive is PdfReference reference)
        {
            return reference.ObjectId;
        }

        return new PdfObjectId(0, 0);
    }
}
