// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ISO 19005-1 §6.1.10, §6.1.11, §6.1.13, §6.2.3.3, §6.4, §6.5.2;
//        ISO 19005-2 §6.1.7.2, §6.2.4.3, §6.3.3, §6.8, §6.9
// PHASE: 3.35.0 (PDA-01, D-402; D-403 backlog 75 and 78) — a self-check
//        before a PDF/A file is written
//
// The writer fixes what it can (fonts, JavaScript, annotation flags, form
// appearances, transfer functions). What it cannot fix is checked here, after
// those fixes and before anything is written, so that a file which would not
// conform is reported as a violation rather than written as if it did.
//
// Transparency and colour are checked only where the pages use them: the
// content streams of each page, of the forms, patterns, Type 3 glyphs and
// annotation appearances they draw, and the graphics states, colour spaces,
// shadings and images those streams name. A resource no page uses does not
// count (D-403). LZW compression and embedded files count anywhere in the
// file.

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.PdfA;

internal static class PdfAConformanceCheck
{
    /// <summary>
    /// Adds a violation for each construct the requested level does not allow
    /// that remains in <paramref name="document"/>.
    /// </summary>
    internal static void Run(
        PdfDocument document,
        PdfPrimitive rootRef,
        PdfObjectStore store,
        PdfAConformance conformance,
        List<string> violations)
    {
        Checker checker = new Checker(store, conformance == PdfAConformance.PdfA1B);
        checker.WalkAllObjects(rootRef);
        for (int i = 0; i < document.PageCount; i++)
        {
            PdfPage page = document.Pages[i];
            checker.CheckPage(page.Dictionary, page.Resources);
        }

        checker.Drain();
        checker.Report(violations);
    }

    private sealed class Checker
    {
        private readonly PdfObjectStore _store;
        private readonly bool _partOne;
        private readonly string _level;
        private readonly HashSet<object> _scanned = new HashSet<object>(ReferenceEqualityComparer.Instance);
        private readonly Stack<(PdfStream Stream, PdfDictionary? Resources)> _pending = new Stack<(PdfStream, PdfDictionary?)>();

        private bool _lzw;
        private bool _opacity;
        private bool _blendMode;
        private bool _softMask;
        private bool _transparencyGroup;
        private bool _deviceCmyk;
        private bool _optionalContent;
        private bool _unnamedLayerConfig;
        private bool _embeddedFile;
        private bool _embeddedFilesTree;
        private bool _fileAttachment;
        private string? _noAppearance;

        public Checker(PdfObjectStore store, bool partOne)
        {
            _store = store;
            _partOne = partOne;
            _level = partOne ? "PDF/A-1b" : "PDF/A-2b";
        }

        public void Report(List<string> violations)
        {
            if (_lzw)
            {
                violations.Add(_level + " does not allow LZW compression (LZWDecode); the file holds an LZW-compressed stream.");
            }

            if (_opacity)
            {
                violations.Add("PDF/A-1b does not allow transparency: a graphics state or annotation a page uses has a constant opacity (/CA or /ca) other than 1.");
            }

            if (_blendMode)
            {
                violations.Add("PDF/A-1b does not allow transparency: a graphics state a page uses has a blend mode other than Normal or Compatible.");
            }

            if (_softMask)
            {
                violations.Add("PDF/A-1b does not allow transparency: a graphics state or image a page uses has a soft mask (/SMask).");
            }

            if (_transparencyGroup)
            {
                violations.Add("PDF/A-1b does not allow transparency: a page or a form it draws has a transparency group (/Group /S /Transparency).");
            }

            if (_deviceCmyk)
            {
                violations.Add(_level + " allows DeviceCMYK colour only with a CMYK output intent; a page uses DeviceCMYK and the output intent is RGB.");
            }

            if (_partOne && _optionalContent)
            {
                violations.Add("PDF/A-1b does not allow optional content; the document has /OCProperties.");
            }

            if (!_partOne && _unnamedLayerConfig)
            {
                violations.Add("PDF/A-2b requires every optional content configuration to have a /Name; one has none.");
            }

            if (_embeddedFile || (_partOne && _embeddedFilesTree))
            {
                violations.Add(_partOne
                    ? "PDF/A-1b does not allow embedded files; the document holds one."
                    : "PDF/A-2b allows an embedded file only when it is itself a PDF/A file, which cannot be confirmed here; the document holds one.");
            }

            if (_partOne && _fileAttachment)
            {
                violations.Add("PDF/A-1b does not allow file attachment annotations; a page has one.");
            }

            if (_noAppearance is not null)
            {
                violations.Add("PDF/A-2b requires an appearance stream (/AP /N) on every annotation except Popup and Link; a "
                    + _noAppearance + " annotation has none.");
            }
        }

        // ---- What counts anywhere in the file --------------------------------

        public void WalkAllObjects(PdfPrimitive root)
        {
            HashSet<int> visited = new HashSet<int>();
            Stack<PdfPrimitive> pending = new Stack<PdfPrimitive>();
            pending.Push(root);
            bool isCatalog = true;
            while (pending.Count > 0)
            {
                PdfPrimitive item = pending.Pop();
                if (item is PdfReference reference)
                {
                    if (visited.Add(reference.ObjectId.ObjectNumber))
                    {
                        pending.Push(_store.Resolve(reference));
                    }

                    continue;
                }

                if (item is PdfArray array)
                {
                    foreach (PdfPrimitive element in array)
                    {
                        pending.Push(element);
                    }

                    continue;
                }

                PdfDictionary? dict = item as PdfDictionary;
                if (item is PdfStream stream)
                {
                    dict = stream.Dictionary;
                    if (IsLzw(Get(dict, "Filter")))
                    {
                        _lzw = true;
                    }
                }

                if (dict is null)
                {
                    continue;
                }

                if (isCatalog)
                {
                    isCatalog = false;
                    InspectCatalog(dict);
                }

                if (dict.ContainsKey(PdfName.Intern("EF")))
                {
                    _embeddedFile = true;
                }

                foreach (KeyValuePair<PdfName, PdfPrimitive> entry in dict)
                {
                    if (entry.Key != PdfName.Parent)
                    {
                        pending.Push(entry.Value);
                    }
                }
            }
        }

        private void InspectCatalog(PdfDictionary catalog)
        {
            if (Get(catalog, "OCProperties") is PdfDictionary properties)
            {
                _optionalContent = true;
                if (Get(properties, "D") is PdfDictionary defaultConfig && Get(defaultConfig, "Name") is not PdfString)
                {
                    _unnamedLayerConfig = true;
                }

                if (Get(properties, "Configs") is PdfArray configs)
                {
                    foreach (PdfPrimitive config in configs)
                    {
                        if (_store.Resolve(config) is PdfDictionary configDict && Get(configDict, "Name") is not PdfString)
                        {
                            _unnamedLayerConfig = true;
                        }
                    }
                }
            }

            if (Get(catalog, "Names") is PdfDictionary names && names.ContainsKey(PdfName.Intern("EmbeddedFiles")))
            {
                _embeddedFilesTree = true;
            }
        }

        // ---- What the pages use -----------------------------------------------

        public void CheckPage(PdfDictionary page, PdfDictionary? resources)
        {
            CheckGroup(Get(page, "Group"));
            PdfPrimitive contents = Get(page, "Contents");
            if (contents is PdfStream single)
            {
                Queue(single, resources);
            }
            else if (contents is PdfArray parts)
            {
                // The parts of a page's contents are one stream; they are
                // scanned in order with the page's resources.
                foreach (PdfPrimitive part in parts)
                {
                    if (_store.Resolve(part) is PdfStream piece)
                    {
                        Queue(piece, resources);
                    }
                }
            }

            if (Get(page, "Annots") is PdfArray annots)
            {
                foreach (PdfPrimitive item in annots)
                {
                    if (_store.Resolve(item) is PdfDictionary annot)
                    {
                        CheckAnnotation(annot);
                    }
                }
            }
        }

        private void CheckAnnotation(PdfDictionary annot)
        {
            string subtype = Get(annot, "Subtype") is PdfName s ? s.Value : string.Empty;
            if (_partOne)
            {
                CheckOpacity(annot);
                if (subtype == "FileAttachment")
                {
                    _fileAttachment = true;
                }
            }

            PdfPrimitive appearance = Get(annot, "AP");
            if (!_partOne
                && _noAppearance is null
                && subtype is not "Popup" and not "Link"
                && !HasNormalAppearance(appearance)
                && !IsZeroArea(Get(annot, "Rect")))
            {
                _noAppearance = subtype.Length > 0 ? subtype : "untyped";
            }

            if (appearance is PdfDictionary states)
            {
                foreach (PdfPrimitive value in states.Values)
                {
                    PdfPrimitive resolved = _store.Resolve(value);
                    if (resolved is PdfStream form)
                    {
                        QueueForm(form, null);
                    }
                    else if (resolved is PdfDictionary byState)
                    {
                        foreach (PdfPrimitive state in byState.Values)
                        {
                            if (_store.Resolve(state) is PdfStream stateForm)
                            {
                                QueueForm(stateForm, null);
                            }
                        }
                    }
                }
            }
        }

        private bool HasNormalAppearance(PdfPrimitive appearance) =>
            appearance is PdfDictionary ap && Get(ap, "N") is PdfStream or PdfDictionary;

        private bool IsZeroArea(PdfPrimitive rect)
        {
            if (rect is not PdfArray r || r.Count < 4)
            {
                return false;
            }

            double? x1 = Number(_store.Resolve(r[0]));
            double? y1 = Number(_store.Resolve(r[1]));
            double? x2 = Number(_store.Resolve(r[2]));
            double? y2 = Number(_store.Resolve(r[3]));
            return x1 is not null && y1 is not null && x2 is not null && y2 is not null
                && (x1.Value == x2.Value || y1.Value == y2.Value);
        }

        private void QueueForm(PdfStream form, PdfDictionary? inherited)
        {
            if (_scanned.Contains(form))
            {
                return;
            }

            CheckGroup(Get(form.Dictionary, "Group"));
            PdfDictionary? own = Get(form.Dictionary, "Resources") as PdfDictionary;
            Queue(form, own ?? inherited);
        }

        private void Queue(PdfStream stream, PdfDictionary? resources)
        {
            if (_scanned.Add(stream))
            {
                _pending.Push((stream, resources));
            }
        }

        public void Drain()
        {
            while (_pending.Count > 0)
            {
                (PdfStream stream, PdfDictionary? resources) = _pending.Pop();
                ScanContent(stream, resources);
            }
        }

        private void CheckGroup(PdfPrimitive group)
        {
            if (group is not PdfDictionary groupDict)
            {
                return;
            }

            if (Get(groupDict, "S") is PdfName { Value: "Transparency" })
            {
                if (_partOne)
                {
                    _transparencyGroup = true;
                }

                if (UsesDeviceCmyk(Get(groupDict, "CS")))
                {
                    _deviceCmyk = true;
                }
            }
        }

        private void CheckOpacity(PdfDictionary dict)
        {
            foreach (string key in new[] { "CA", "ca" })
            {
                if (Number(Get(dict, key)) is double opacity && opacity != 1.0)
                {
                    _opacity = true;
                }
            }
        }

        private void CheckGraphicsState(PdfPrimitive state)
        {
            if (!_partOne || state is not PdfDictionary gs)
            {
                return;
            }

            CheckOpacity(gs);
            if (gs.ContainsKey(PdfName.Intern("BM")) && !IsNormalBlend(Get(gs, "BM")))
            {
                _blendMode = true;
            }

            if (gs.ContainsKey(PdfName.Intern("SMask")) && Get(gs, "SMask") is not PdfName { Value: "None" })
            {
                _softMask = true;
            }
        }

        private void CheckColourSpace(PdfPrimitive space, bool cmykAllowed)
        {
            if (!cmykAllowed && UsesDeviceCmyk(space))
            {
                _deviceCmyk = true;
            }
        }

        private void CheckXObject(PdfPrimitive xobject, PdfDictionary? resources, bool cmykAllowed)
        {
            if (xobject is not PdfStream stream)
            {
                return;
            }

            string subtype = Get(stream.Dictionary, "Subtype") is PdfName s ? s.Value : string.Empty;
            if (subtype == "Image")
            {
                CheckColourSpace(Get(stream.Dictionary, "ColorSpace"), cmykAllowed);
                if (_partOne && stream.Dictionary.ContainsKey(PdfName.Intern("SMask")))
                {
                    _softMask = true;
                }
            }
            else if (subtype == "Form")
            {
                QueueForm(stream, resources);
            }
        }

        private void CheckShading(PdfPrimitive shading, bool cmykAllowed)
        {
            PdfDictionary? dict = shading as PdfDictionary ?? (shading as PdfStream)?.Dictionary;
            if (dict is not null)
            {
                CheckColourSpace(Get(dict, "ColorSpace"), cmykAllowed);
            }
        }

        private void CheckPattern(PdfPrimitive pattern, PdfDictionary? resources, bool cmykAllowed)
        {
            if (pattern is PdfStream tiling)
            {
                QueueForm(tiling, resources);
            }
            else if (pattern is PdfDictionary shadingPattern)
            {
                CheckShading(Get(shadingPattern, "Shading"), cmykAllowed);
                CheckGraphicsState(Get(shadingPattern, "ExtGState"));
            }
        }

        private void CheckFont(PdfPrimitive font, PdfDictionary? resources)
        {
            if (font is not PdfDictionary dict || Get(dict, "Subtype") is not PdfName { Value: "Type3" })
            {
                return;
            }

            PdfDictionary? glyphResources = Get(dict, "Resources") as PdfDictionary ?? resources;
            if (Get(dict, "CharProcs") is PdfDictionary procs)
            {
                foreach (PdfPrimitive proc in procs.Values)
                {
                    if (_store.Resolve(proc) is PdfStream glyph)
                    {
                        Queue(glyph, glyphResources);
                    }
                }
            }
        }

        // Reads a content stream and checks each resource an operator uses:
        // gs, cs/CS, scn/SCN with a pattern, sh, Do, Tf (Type 3), the k/K
        // operators and inline images. A stream whose resources define
        // /DefaultCMYK may use DeviceCMYK. A stream that cannot be decoded or
        // read is skipped.
        private void ScanContent(PdfStream stream, PdfDictionary? resources)
        {
            bool cmykAllowed = Get(resources, "ColorSpace") is PdfDictionary spaces
                && spaces.ContainsKey(PdfName.Intern("DefaultCMYK"));
            byte[] content;
            try
            {
                content = Decode(stream);
            }
            catch (Exception ex) when (!PdfWorkScope.IsAbort(ex) && ex is not OutOfMemoryException)
            {
                return;
            }

            try
            {
                using MemoryStream input = new MemoryStream(content, writable: false);
                using PdfTokenizer tokenizer = new PdfTokenizer(input, leaveOpen: true);
                List<string> names = new List<string>();
                while (true)
                {
                    PdfToken token = tokenizer.Read();
                    if (token.IsEndOfStream)
                    {
                        break;
                    }

                    if (token.Type == PdfTokenType.Name)
                    {
                        names.Add(token.RawText.TrimStart('/'));
                        continue;
                    }

                    if (token.Type != PdfTokenType.Keyword)
                    {
                        continue;
                    }

                    string op = token.RawText;
                    string? first = names.Count > 0 ? names[0] : null;
                    string? last = names.Count > 0 ? names[^1] : null;
                    switch (op)
                    {
                        case "gs" when first is not null:
                            CheckGraphicsState(Lookup(resources, "ExtGState", first));
                            break;
                        case "cs" or "CS" when first is not null:
                            CheckColourSpace(first is "DeviceCMYK" ? PdfName.Intern(first) : Lookup(resources, "ColorSpace", first), cmykAllowed);
                            break;
                        case "scn" or "SCN" when last is not null:
                            CheckPattern(Lookup(resources, "Pattern", last), resources, cmykAllowed);
                            break;
                        case "k" or "K":
                            CheckColourSpace(PdfName.Intern("DeviceCMYK"), cmykAllowed);
                            break;
                        case "sh" when first is not null:
                            CheckShading(Lookup(resources, "Shading", first), cmykAllowed);
                            break;
                        case "Do" when first is not null:
                            CheckXObject(Lookup(resources, "XObject", first), resources, cmykAllowed);
                            break;
                        case "Tf" when first is not null:
                            CheckFont(Lookup(resources, "Font", first), resources);
                            break;
                        case "BI":
                            ScanInlineImage(tokenizer, content, resources, cmykAllowed);
                            break;
                        default:
                            break;
                    }

                    names.Clear();
                }
            }
            catch (Exception ex) when (!PdfWorkScope.IsAbort(ex) && ex is not OutOfMemoryException)
            {
                // A content stream the tokenizer cannot read is not checked further.
            }
        }

        private void ScanInlineImage(PdfTokenizer tokenizer, byte[] content, PdfDictionary? resources, bool cmykAllowed)
        {
            string? key = null;
            bool inArray = false;
            while (true)
            {
                PdfToken token = tokenizer.Read();
                if (token.IsEndOfStream)
                {
                    return;
                }

                if (token.Type == PdfTokenType.Keyword && token.RawText == "ID")
                {
                    break;
                }

                if (token.Type == PdfTokenType.ArrayStart)
                {
                    inArray = true;
                    continue;
                }

                if (token.Type == PdfTokenType.ArrayEnd)
                {
                    inArray = false;
                    key = null;
                    continue;
                }

                if (token.Type != PdfTokenType.Name)
                {
                    if (!inArray)
                    {
                        key = null;
                    }

                    continue;
                }

                string name = token.RawText.TrimStart('/');
                if (key is null && !inArray)
                {
                    key = name;
                    continue;
                }

                if (key is "F" or "Filter" && name is "LZW" or "LZWDecode")
                {
                    _lzw = true;
                }
                else if (key is "CS" or "ColorSpace")
                {
                    PdfPrimitive space = name is "CMYK" or "DeviceCMYK" or "G" or "DeviceGray" or "RGB" or "DeviceRGB" or "I" or "Indexed"
                        ? PdfName.Intern(name)
                        : Lookup(resources, "ColorSpace", name);
                    CheckColourSpace(space, cmykAllowed);
                }

                if (!inArray)
                {
                    key = null;
                }
            }

            // The image data runs to an EI that stands as a word of its own.
            long start = tokenizer.Position + 1;
            for (long i = start; i + 1 < content.Length; i++)
            {
                if (content[i] == 'E' && content[i + 1] == 'I'
                    && IsWhite(content[i - 1])
                    && (i + 2 == content.Length || IsWhite(content[i + 2])))
                {
                    tokenizer.Seek(i + 2);
                    return;
                }
            }

            tokenizer.Seek(content.Length);
        }

        // ---- Helpers ----------------------------------------------------------

        private PdfPrimitive Get(PdfDictionary? dict, string key) =>
            dict is not null && dict.TryGetValue(PdfName.Intern(key), out PdfPrimitive? value)
                ? _store.Resolve(value)
                : PdfNull.Value;

        private PdfPrimitive Lookup(PdfDictionary? resources, string category, string name) =>
            Get(Get(resources, category) as PdfDictionary, name);

        private bool IsLzw(PdfPrimitive filter)
        {
            if (filter is PdfName name)
            {
                return name.Value is "LZWDecode" or "LZW";
            }

            if (filter is PdfArray chain)
            {
                foreach (PdfPrimitive item in chain)
                {
                    if (_store.Resolve(item) is PdfName { Value: "LZWDecode" or "LZW" })
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool IsNormalBlend(PdfPrimitive value)
        {
            if (value is PdfName name)
            {
                return name.Value is "Normal" or "Compatible";
            }

            if (value is PdfArray modes)
            {
                foreach (PdfPrimitive mode in modes)
                {
                    if (_store.Resolve(mode) is not PdfName { Value: "Normal" or "Compatible" })
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        // True when a colour space is DeviceCMYK or is built on it (Indexed base,
        // Separation or DeviceN alternate, Pattern underlying space). An ICCBased
        // space's /Alternate is not a use of the device space.
        private bool UsesDeviceCmyk(PdfPrimitive space)
        {
            for (int depth = 0; depth < 8; depth++)
            {
                if (space is PdfName name)
                {
                    return name.Value is "DeviceCMYK" or "CMYK";
                }

                if (space is not PdfArray array || array.Count == 0 || _store.Resolve(array[0]) is not PdfName family)
                {
                    return false;
                }

                int baseIndex = family.Value switch
                {
                    "Indexed" or "I" or "Pattern" => 1,
                    "Separation" or "DeviceN" => 2,
                    _ => -1,
                };
                if (baseIndex < 0 || baseIndex >= array.Count)
                {
                    return false;
                }

                space = _store.Resolve(array[baseIndex]);
            }

            return false;
        }

        private static double? Number(PdfPrimitive value) => value switch
        {
            PdfInteger i => i.Value,
            PdfReal r => r.Value,
            _ => null,
        };

        private static bool IsWhite(byte b) => b is (byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t' or (byte)'\f' or 0;

        private byte[] Decode(PdfStream stream)
        {
            Chuvadi.Pdf.Filters.FilterPipeline pipeline = Chuvadi.Pdf.Filters.FilterRegistry.CreateDefaultPipeline();
            byte[] data = stream.RawBytes;
            PdfPrimitive filter = _store.Resolve(stream.Filter ?? PdfNull.Value);
            if (filter is PdfName single)
            {
                return pipeline.Decode(Chuvadi.Pdf.Filters.FilterRegistry.ResolveAlias(single.Value), data, null);
            }

            if (filter is PdfArray chain)
            {
                foreach (PdfPrimitive item in chain)
                {
                    if (_store.Resolve(item) is PdfName name)
                    {
                        data = pipeline.Decode(Chuvadi.Pdf.Filters.FilterRegistry.ResolveAlias(name.Value), data, null);
                    }
                }
            }

            return data;
        }
    }
}
