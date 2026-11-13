// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.7.2 — AcroForm dictionary (/NeedAppearances)
//        PDF 32000-1:2008 §12.7.3 — Field value (/V)
//        PDF 32000-1:2008 §12.7.4.3 — /MaxLen, /RV; §12.7.4.4 — /Opt, /I
// PHASE: Phase 2 — Chuvadi.Pdf.Forms; 3.34.0 (D-358, D-362) several values,
//        /RV and /I, optional checks, unknown names.
// Updates AcroForm field values and writes a new PDF.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Forms;

/// <summary>
/// Fills AcroForm field values in a PDF document and writes the result.
/// </summary>
/// <remarks>
/// For each fully-qualified field name in <c>values</c>, locates the field's
/// indirect object in the document, replaces its <c>/V</c> entry, and writes
/// a new PDF with the updated objects. Also sets
/// <c>/AcroForm/NeedAppearances=true</c> so that PDF viewers regenerate
/// the visible appearance streams from the new values.
///
/// PDF 32000-1:2008 §12.7.2 — Interactive form dictionary.
/// </remarks>
public static class FormFiller
{
    /// <summary>
    /// Fills the given field values and writes the resulting PDF to
    /// <paramref name="output"/>.
    /// </summary>
    /// <param name="output">The stream to write the filled PDF to.</param>
    /// <param name="document">The source document.</param>
    /// <param name="values">
    /// Map of fully-qualified field name → value. For text and choice fields the
    /// value is the string content (any Unicode text); a choice field's value
    /// is its option's export value. For a check box: the
    /// widget's on-state or export value (see <see cref="FormWidget"/>), or
    /// <c>Yes</c>, <c>On</c> or <c>true</c> (any case) to turn it on, and
    /// <c>Off</c>, <c>false</c> or an empty string to turn it off. For a radio
    /// group: the on-state or export value of the button to turn on, or
    /// <c>Off</c> for none. Pushbuttons have no value and are left unchanged.
    /// An empty string clears a text or choice field.
    /// Names that match no field are ignored.
    /// </param>
    /// <exception cref="FormException">
    /// A check box or radio-group value matches none of the field's states
    /// (3.34.0; <see cref="FormException.Reason"/> is
    /// <see cref="FormErrorReason.UnknownState"/>).
    /// </exception>
    /// <remarks>
    /// Since 3.34.0 a button's <c>/V</c> is written as a name and every widget's
    /// <c>/AS</c> is set: the matching widget gets its own on-state, the others
    /// <c>/Off</c>. Text values outside Latin-1 are written as UTF-16 text
    /// strings. Also since 3.34.0, a text or choice value removes the field's
    /// rich-text value (<c>/RV</c>), which would otherwise still show the old
    /// text, and a choice value sets the selected option indices (<c>/I</c>)
    /// to the options it matches, or removes them when it matches none. To
    /// produce fixed page content from the filled values, open the
    /// result and flatten it with <c>AnnotationFlattener</c> (Operations): the
    /// flattener draws text and choice values itself when
    /// <c>NeedAppearances</c> is set, which <c>Fill</c> does.
    /// </remarks>
    public static void Fill(
        Stream output,
        PdfDocument document,
        IReadOnlyDictionary<string, string> values)
    {
        Fill(output, document, values, new FormFillOptions());
    }

    /// <summary>
    /// Fills the given field values and writes the resulting PDF to
    /// <paramref name="output"/>, with options (3.34.0).
    /// </summary>
    /// <param name="output">The stream to write the filled PDF to.</param>
    /// <param name="document">The source document.</param>
    /// <param name="values">
    /// Map of fully-qualified field name → value, as for
    /// <see cref="Fill(Stream, PdfDocument, IReadOnlyDictionary{string, string})"/>.
    /// </param>
    /// <param name="options">
    /// Fill options. <see cref="FormFillOptions.Encryption"/> encrypts the
    /// output; without it the output is not encrypted, even when the source was.
    /// <see cref="FormFillOptions.Validate"/> checks values first.
    /// <see cref="FormFillOptions.UnknownFields"/> cannot be
    /// <see cref="FormUnknownFields.Report"/> here, as this overload returns
    /// nothing: use the overload that returns a <see cref="FormFillResult"/>.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <see cref="FormFillOptions.UnknownFields"/> is <see cref="FormUnknownFields.Report"/>.
    /// </exception>
    /// <exception cref="FormException">
    /// A value is refused; <see cref="FormException.Reason"/> says why.
    /// </exception>
    public static void Fill(
        Stream output,
        PdfDocument document,
        IReadOnlyDictionary<string, string> values,
        FormFillOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(values);
        if (options.UnknownFields == FormUnknownFields.Report)
        {
            throw new ArgumentException(
                "FormUnknownFields.Report needs the FormFiller.Fill overload that returns a FormFillResult "
                + "(values as IReadOnlyDictionary<string, IReadOnlyList<string>>); this overload returns nothing.",
                nameof(options));
        }

        List<KeyValuePair<string, IReadOnlyList<string>>> entries = new List<KeyValuePair<string, IReadOnlyList<string>>>(values.Count);
        foreach (KeyValuePair<string, string> kv in values)
        {
            entries.Add(new KeyValuePair<string, IReadOnlyList<string>>(kv.Key, new[] { kv.Value }));
        }

        FillCore(output, document, entries, options);
    }

    /// <summary>
    /// Fills the given field values, several per field where the field takes
    /// them, writes the resulting PDF to <paramref name="output"/> and reports
    /// what was done (3.34.0).
    /// </summary>
    /// <param name="output">The stream to write the filled PDF to.</param>
    /// <param name="document">The source document.</param>
    /// <param name="values">
    /// Map of fully-qualified field name → values. A multi-select list box
    /// takes any number of its options' export values: <c>/V</c> is written
    /// as one string for one value and as an array for more, and <c>/I</c>
    /// lists the indices of the options chosen. Every other field takes one
    /// value, as for
    /// <see cref="Fill(Stream, PdfDocument, IReadOnlyDictionary{string, string})"/>.
    /// An empty list, like an empty string, clears a text or choice field and
    /// turns a button off.
    /// </param>
    /// <param name="options">Fill options; see <see cref="FormFillOptions"/>.</param>
    /// <returns>
    /// The fields written and, with <see cref="FormUnknownFields.Report"/>, the
    /// names that matched no field.
    /// </returns>
    /// <exception cref="FormException">
    /// A value is refused; <see cref="FormException.Reason"/> says why. Nothing
    /// is written.
    /// </exception>
    public static FormFillResult Fill(
        Stream output,
        PdfDocument document,
        IReadOnlyDictionary<string, IReadOnlyList<string>> values,
        FormFillOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(values);
        List<KeyValuePair<string, IReadOnlyList<string>>> entries = new List<KeyValuePair<string, IReadOnlyList<string>>>(values.Count);
        foreach (KeyValuePair<string, IReadOnlyList<string>> kv in values)
        {
            if (kv.Value is null)
            {
                throw new ArgumentException("The values for field '" + kv.Key + "' are null.", nameof(values));
            }

            foreach (string value in kv.Value)
            {
                if (value is null)
                {
                    throw new ArgumentException("A value for field '" + kv.Key + "' is null.", nameof(values));
                }
            }

            entries.Add(kv);
        }

        return FillCore(output, document, entries, options);
    }

    private static FormFillResult FillCore(
        Stream output,
        PdfDocument document,
        List<KeyValuePair<string, IReadOnlyList<string>>> values,
        FormFillOptions options)
    {
        return PdfWriteGuard.Run(document, "filled", () =>
        {
            if (output is null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            if (document is null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            // Force-load all reachable objects so the document's store contains
            // the complete object graph before we iterate.
            PreloadAllObjects(document);

            IReadOnlyList<FormField> leafFields = FormReader.GetLeafFields(document);

            // Map field name → field (the last field of a name wins, as before).
            Dictionary<string, FormField> fieldsByName = new Dictionary<string, FormField>();

            foreach (FormField field in leafFields)
            {
                if (field.ObjectId.ObjectNumber != 0)
                {
                    fieldsByName[field.FullyQualifiedName] = field;
                }
            }

            List<string> unknown = new List<string>();
            foreach (KeyValuePair<string, IReadOnlyList<string>> kv in values)
            {
                if (!fieldsByName.ContainsKey(kv.Key))
                {
                    unknown.Add(kv.Key);
                }
            }

            if (unknown.Count > 0 && options.UnknownFields == FormUnknownFields.Throw)
            {
                throw new FormException("No field is named '" + unknown[0] + "'.")
                {
                    Reason = FormErrorReason.UnknownField,
                    FieldName = unknown[0],
                };
            }

            // Objects being rewritten, by object number (a field and its merged
            // widget are one object).
            Dictionary<int, PdfIndirectObject> pending = new Dictionary<int, PdfIndirectObject>();
            List<string> filled = new List<string>();

            foreach (KeyValuePair<string, IReadOnlyList<string>> kv in values)
            {
                if (!fieldsByName.TryGetValue(kv.Key, out FormField? field))
                {
                    continue;
                }

                IReadOnlyList<string> given = kv.Value;
                bool multiSelect = field.Type == FormFieldType.Choice && (field.Flags & FormFieldFlags.MultiSelect) != 0;
                if (given.Count > 1 && !multiSelect)
                {
                    throw new FormException(
                        "Field '" + field.FullyQualifiedName + "' takes one value; "
                        + given.Count.ToString(CultureInfo.InvariantCulture) + " were given.")
                    {
                        Reason = FormErrorReason.TooManyValues,
                        FieldName = field.FullyQualifiedName,
                    };
                }

                if (field.Type == FormFieldType.Button && (field.Flags & FormFieldFlags.Pushbutton) != 0)
                {
                    continue;
                }

                string first = given.Count == 0 ? string.Empty : given[0];
                if (field.Type == FormFieldType.Button)
                {
                    string? chosen = ChooseOnState(field, first);
                    PdfDictionary? buttonDict = Editable(document, field.ObjectId, pending);
                    if (buttonDict is null)
                    {
                        continue;
                    }

                    buttonDict.Set(PdfName.Intern("V"), PdfName.Intern(chosen ?? "Off"));
                    SetAppearanceStates(document, field, buttonDict, chosen, pending);
                    filled.Add(field.FullyQualifiedName);
                    continue;
                }

                bool clear = given.Count == 0 || (given.Count == 1 && first.Length == 0);
                if (options.Validate && !clear)
                {
                    Check(field, given);
                }

                PdfDictionary? fieldDict = Editable(document, field.ObjectId, pending);
                if (fieldDict is null)
                {
                    continue;
                }

                fieldDict.Set(PdfName.Intern("V"), clear || given.Count == 1 ? MakeFieldValue(first) : MakeFieldValues(given));

                // 3.34.0 (D-358 c): a plain value replaces the rich-text one.
                fieldDict.Remove(PdfName.Intern("RV"));
                if (field.Type == FormFieldType.Choice)
                {
                    SetSelectedIndices(field, fieldDict, clear ? Array.Empty<string>() : given, multiSelect);
                }

                filled.Add(field.FullyQualifiedName);
            }

            List<PdfIndirectObject> updatedObjects = new List<PdfIndirectObject>(pending.Values);
            HashSet<int> rewrittenFieldNums = new HashSet<int>(pending.Keys);

            // Update the AcroForm dictionary to set NeedAppearances = true
            UpdateAcroFormNeedAppearances(document, updatedObjects, rewrittenFieldNums);

            // Build full output object list
            List<PdfIndirectObject> allObjects = new List<PdfIndirectObject>();
            allObjects.AddRange(updatedObjects);

            foreach (PdfIndirectObject obj in document.Objects.Objects)
            {
                if (!rewrittenFieldNums.Contains(obj.Id.ObjectNumber))
                {
                    allObjects.Add(obj);
                }
            }

            // Build trailer. 3.34.0: the source's /Info and /ID are kept (before,
            // the trailer had only /Root, so the document information and the
            // file identifier were lost).
            PdfDictionary trailer = new PdfDictionary();
            if (document.Trailer.TryGetValue(PdfName.Info, out PdfPrimitive? info) && info is not null)
            {
                trailer.Set(PdfName.Info, info);
            }

            if (document.Trailer.TryGetValue(PdfName.Intern("ID"), out PdfPrimitive? fileId) && fileId is not null)
            {
                trailer.Set(PdfName.Intern("ID"), fileId);
            }

            foreach (PdfIndirectObject obj in document.Objects.Objects)
            {
                if (obj.Value is PdfDictionary dict &&
                    dict.TryGetValue(PdfName.Type, out PdfPrimitive? t) &&
                    t is PdfName tn && tn.Value == "Catalog")
                {
                    trailer.Set(PdfName.Root, new PdfReference(obj.Id));
                    break;
                }
            }

            PdfWriter.Write(output, allObjects, trailer, options.Encryption);
            return new FormFillResult
            {
                FilledFields = filled,
                UnknownFields = options.UnknownFields == FormUnknownFields.Report ? unknown : Array.Empty<string>(),
            };
        });
    }

    // FormFillOptions.Validate (3.34.0, D-362): a choice value must be one of
    // the options' export values unless the field is an editable combo box;
    // a text value must not be longer than /MaxLen, counted in code points.
    private static void Check(FormField field, IReadOnlyList<string> given)
    {
        if (field.Type == FormFieldType.Text && field.MaxLength is int max)
        {
            string value = given[0];
            int length = CodePoints(value);
            if (length > max)
            {
                throw new FormException(
                    "Text field '" + field.FullyQualifiedName + "' takes at most "
                    + max.ToString(CultureInfo.InvariantCulture) + " characters; the value has "
                    + length.ToString(CultureInfo.InvariantCulture) + ".")
                {
                    Reason = FormErrorReason.TooLong,
                    FieldName = field.FullyQualifiedName,
                    Value = value,
                    MaxLength = max,
                };
            }

            return;
        }

        const FormFieldFlags EditableCombo = FormFieldFlags.Combo | FormFieldFlags.Edit;
        if (field.Type != FormFieldType.Choice || (field.Flags & EditableCombo) == EditableCombo)
        {
            return;
        }

        foreach (string value in given)
        {
            if (OptionIndex(field, value) >= 0)
            {
                continue;
            }

            List<string> exports = new List<string>(field.Options.Count);
            foreach (FormChoiceOption option in field.Options)
            {
                exports.Add(option.ExportValue);
            }

            throw new FormException(
                "Choice field '" + field.FullyQualifiedName + "' has no option '" + value
                + "'. Its options are: " + (exports.Count == 0 ? "(none)" : string.Join(", ", exports)) + ".")
            {
                Reason = FormErrorReason.NotInOptions,
                FieldName = field.FullyQualifiedName,
                Value = value,
                ValidStates = exports,
            };
        }
    }

    private static int CodePoints(string value)
    {
        int count = 0;
        foreach (Rune _ in value.EnumerateRunes())
        {
            count++;
        }

        return count;
    }

    // The index of the first option whose export value is `value`, or -1.
    private static int OptionIndex(FormField field, string value)
    {
        for (int i = 0; i < field.Options.Count; i++)
        {
            if (string.Equals(field.Options[i].ExportValue, value, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    // /I (PDF 32000-1:2008 §12.7.4.4): the indices of the chosen options, in
    // ascending order. Written for a multi-select list box or a field that
    // already had /I; removed when the values match no option, so a stale
    // selection never contradicts /V (3.34.0, D-358).
    private static void SetSelectedIndices(FormField field, PdfDictionary fieldDict, IReadOnlyList<string> given, bool multiSelect)
    {
        SortedSet<int> indices = new SortedSet<int>();
        foreach (string value in given)
        {
            int index = OptionIndex(field, value);
            if (index >= 0)
            {
                indices.Add(index);
            }
        }

        PdfName key = PdfName.Intern("I");
        if (indices.Count == 0)
        {
            fieldDict.Remove(key);
            return;
        }

        if (!multiSelect && !fieldDict.ContainsKey(key))
        {
            return;
        }

        List<PdfPrimitive> items = new List<PdfPrimitive>(indices.Count);
        foreach (int index in indices)
        {
            items.Add(new PdfInteger(index));
        }

        fieldDict.Set(key, new PdfArray(items));
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static void PreloadAllObjects(PdfDocument document)
    {
        HashSet<int> visited = new HashSet<int>();
        int pageCount = document.PageCount;

        for (int i = 0; i < pageCount; i++)
        {
            PdfPage page = document.Pages[i];
            Visit(document.Objects, page.Dictionary, visited);
        }

        // 3.34.0: the whole catalog (its AcroForm, outlines, metadata, name
        // trees, ...) and the document information dictionary are loaded too.
        // Before, only the pages and the AcroForm were, so the written catalog
        // could name objects the output did not contain, and the /Info that
        // 3.34.0 keeps in the trailer could be missing from the file.
        Visit(document.Objects, document.Catalog, visited);

        if (document.Trailer.TryGetValue(PdfName.Info, out PdfPrimitive? info))
        {
            Visit(document.Objects, info, visited);
        }
    }

    private static void Visit(PdfObjectStore store, PdfPrimitive? p, HashSet<int> visited)
    {
        if (p is null)
        {
            return;
        }

        if (p is PdfReference reference)
        {
            int num = reference.ObjectId.ObjectNumber;

            if (!visited.Add(num))
            {
                return;
            }

            PdfPrimitive resolved = store.Resolve(reference);
            Visit(store, resolved, visited);
            return;
        }

        if (p is PdfArray arr)
        {
            for (int i = 0; i < arr.Count; i++)
            {
                Visit(store, arr[i], visited);
            }
            return;
        }

        if (p is PdfDictionary dict)
        {
            foreach (KeyValuePair<PdfName, PdfPrimitive> entry in dict)
            {
                Visit(store, entry.Value, visited);
            }
            return;
        }

        if (p is PdfStream stream)
        {
            Visit(store, stream.Dictionary, visited);
        }
    }

    // The on-state a button value selects, or null for "all off" (3.34.0).
    private static string? ChooseOnState(FormField field, string value)
    {
        foreach (FormWidget widget in field.Widgets)
        {
            if (widget.OnState is not null
                && (string.Equals(widget.OnState, value, StringComparison.Ordinal)
                    || string.Equals(widget.ExportValue, value, StringComparison.Ordinal)))
            {
                return widget.OnState;
            }
        }

        bool isRadio = (field.Flags & FormFieldFlags.Radio) != 0;
        if (value.Length == 0
            || string.Equals(value, "Off", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!isRadio
            && (string.Equals(value, "Yes", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "On", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (FormWidget widget in field.Widgets)
            {
                if (widget.OnState is not null)
                {
                    return widget.OnState;
                }
            }

            // A check box with no appearance states: keep the conventional name.
            return "Yes";
        }

        List<string> known = new List<string>();
        foreach (FormWidget widget in field.Widgets)
        {
            if (widget.OnState is not null)
            {
                known.Add(widget.ExportValue is not null && widget.ExportValue != widget.OnState
                    ? widget.OnState + " (" + widget.ExportValue + ")"
                    : widget.OnState);
            }
        }

        List<string> valid = new List<string>();
        foreach (FormWidget widget in field.Widgets)
        {
            if (widget.OnState is not null)
            {
                valid.Add(widget.OnState);
                if (widget.ExportValue is not null && widget.ExportValue != widget.OnState)
                {
                    valid.Add(widget.ExportValue);
                }
            }
        }

        valid.Add("Off");
        throw new FormException(
            (isRadio ? "Radio group '" : "Check box '") + field.FullyQualifiedName + "' has no state '" + value
            + "'. Its states are: " + (known.Count == 0 ? "(none)" : string.Join(", ", known)) + ", Off.")
        {
            Reason = FormErrorReason.UnknownState,
            FieldName = field.FullyQualifiedName,
            Value = value,
            ValidStates = valid,
        };
    }

    // Sets /AS on every widget of a button field: its own on-state when it is
    // the chosen one, /Off otherwise (3.34.0).
    private static void SetAppearanceStates(
        PdfDocument document, FormField field, PdfDictionary fieldDict, string? chosen, Dictionary<int, PdfIndirectObject> pending)
    {
        PdfArray? kids = document.Objects.ResolveAs<PdfArray>(
            fieldDict.TryGetValue(PdfName.Kids, out PdfPrimitive? k) ? k : PdfNull.Value);
        List<PdfPrimitive>? newKids = null;

        foreach (FormWidget widget in field.Widgets)
        {
            PdfName state = widget.OnState is not null && widget.OnState == chosen
                ? PdfName.Intern(widget.OnState)
                : PdfName.Intern("Off");

            if (widget.ObjectId.ObjectNumber == field.ObjectId.ObjectNumber && widget.ObjectId.ObjectNumber != 0)
            {
                fieldDict.Set(PdfName.Intern("AS"), state);
                continue;
            }

            if (widget.ObjectId.ObjectNumber != 0)
            {
                Editable(document, widget.ObjectId, pending)?.Set(PdfName.Intern("AS"), state);
                continue;
            }

            // A widget written directly in /Kids: copy the array with the
            // widget's dictionary changed.
            if (kids is null)
            {
                continue;
            }

            newKids ??= new List<PdfPrimitive>(kids);
            for (int i = 0; i < newKids.Count; i++)
            {
                if (newKids[i] is PdfDictionary direct && !direct.ContainsKey(PdfName.Intern("T")) && SameRect(direct, widget, document))
                {
                    PdfDictionary copy = CopyDictionary(direct);
                    copy.Set(PdfName.Intern("AS"), state);
                    newKids[i] = copy;
                    break;
                }
            }
        }

        if (newKids is not null)
        {
            fieldDict.Set(PdfName.Kids, new PdfArray(newKids));
        }
    }

    private static bool SameRect(PdfDictionary widget, FormWidget expected, PdfDocument document)
    {
        PdfArray? r = document.Objects.ResolveAs<PdfArray>(
            widget.TryGetValue(PdfName.Intern("Rect"), out PdfPrimitive? rect) ? rect : PdfNull.Value);
        if (r is null || r.Count < 4)
        {
            return false;
        }

        double x1 = Number(document.Objects.Resolve(r[0]));
        double y1 = Number(document.Objects.Resolve(r[1]));
        return Math.Abs(Math.Min(x1, Number(document.Objects.Resolve(r[2]))) - expected.Rect.X1) < 1e-6
            && Math.Abs(Math.Min(y1, Number(document.Objects.Resolve(r[3]))) - expected.Rect.Y1) < 1e-6;
    }

    private static double Number(PdfPrimitive value) => value switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0,
    };

    // The dictionary of object `id` as it will be written: the pending copy
    // when one exists, otherwise a fresh copy of the original.
    private static PdfDictionary? Editable(PdfDocument document, PdfObjectId id, Dictionary<int, PdfIndirectObject> pending)
    {
        if (pending.TryGetValue(id.ObjectNumber, out PdfIndirectObject? existing))
        {
            return existing.Value as PdfDictionary;
        }

        PdfIndirectObject? original = FindObjectById(document, id);
        if (original is null || original.Value is not PdfDictionary dict)
        {
            return null;
        }

        PdfDictionary copy = CopyDictionary(dict);
        pending[id.ObjectNumber] = new PdfIndirectObject(id, copy);
        return copy;
    }

    // A text or choice value as a PDF text string: the Latin-1 bytes when every
    // character fits (byte-identical to before 3.34.0), otherwise UTF-16BE with
    // a byte order mark (PDF 32000-1:2008 §7.9.2.2). Before 3.34.0 a character
    // outside Latin-1, such as Tamil, was written as '?'.
    private static PdfPrimitive MakeFieldValue(string value)
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

    // Several choice values as an array of text strings (§12.7.4.4).
    private static PdfArray MakeFieldValues(IReadOnlyList<string> values)
    {
        List<PdfPrimitive> items = new List<PdfPrimitive>(values.Count);
        foreach (string value in values)
        {
            items.Add(MakeFieldValue(value));
        }

        return new PdfArray(items);
    }

    private static PdfIndirectObject? FindObjectById(PdfDocument document, PdfObjectId id)
    {
        foreach (PdfIndirectObject obj in document.Objects.Objects)
        {
            if (obj.Id.ObjectNumber == id.ObjectNumber &&
                obj.Id.Generation == id.Generation)
            {
                return obj;
            }
        }

        return null;
    }

    private static void UpdateAcroFormNeedAppearances(
        PdfDocument document,
        List<PdfIndirectObject> updatedObjects,
        HashSet<int> rewrittenNums)
    {
        PdfDictionary catalog = document.Catalog;

        if (!catalog.TryGetValue(PdfName.Intern("AcroForm"), out PdfPrimitive? acroFormPrim))
        {
            return;
        }

        // AcroForm may be inline in catalog or an indirect reference.
        if (acroFormPrim is PdfReference acroRef)
        {
            PdfIndirectObject? acroObj = FindObjectById(document, acroRef.ObjectId);

            if (acroObj is null || acroObj.Value is not PdfDictionary origDict)
            {
                return;
            }

            PdfDictionary newDict = CopyDictionary(origDict);
            newDict.Set(PdfName.Intern("NeedAppearances"), true);
            updatedObjects.Add(new PdfIndirectObject(acroRef.ObjectId, newDict));
            rewrittenNums.Add(acroRef.ObjectId.ObjectNumber);
            return;
        }

        if (acroFormPrim is PdfDictionary inlineDict)
        {
            // AcroForm is inline in catalog — must update catalog itself
            PdfDictionary newAcro = CopyDictionary(inlineDict);
            newAcro.Set(PdfName.Intern("NeedAppearances"), true);

            // Find catalog object and update it
            foreach (PdfIndirectObject obj in document.Objects.Objects)
            {
                if (obj.Value is not PdfDictionary cdict)
                {
                    continue;
                }

                if (cdict.TryGetValue(PdfName.Type, out PdfPrimitive? t) &&
                    t is PdfName tn && tn.Value == "Catalog")
                {
                    PdfDictionary newCatalog = CopyDictionary(cdict);
                    newCatalog.Set(PdfName.Intern("AcroForm"), newAcro);
                    updatedObjects.Add(new PdfIndirectObject(obj.Id, newCatalog));
                    rewrittenNums.Add(obj.Id.ObjectNumber);
                    return;
                }
            }
        }
    }

    private static PdfDictionary CopyDictionary(PdfDictionary source)
    {
        PdfDictionary copy = new PdfDictionary();

        foreach (KeyValuePair<PdfName, PdfPrimitive> entry in source)
        {
            copy.Set(entry.Key, entry.Value);
        }

        return copy;
    }
}
