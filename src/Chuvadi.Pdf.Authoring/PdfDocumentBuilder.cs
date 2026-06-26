// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.7 (Document structure), §9.6.2 (Standard fonts)
// PHASE: Phase 1.3 — Authoring module

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Authoring;

/// <summary>
/// Top-level entry point for creating fresh PDF documents.
/// </summary>
/// <remarks>
/// <para>
/// Pages are added in order via <see cref="AddPage"/>. Each returns a
/// <see cref="PageBuilder"/> for drawing. Optional document-level header
/// and footer callbacks run for every page just before save, with the
/// final page number and total page count supplied.
/// </para>
/// <para>
/// Call <see cref="Save(System.IO.Stream)"/> or <see cref="ToByteArray()"/> to emit the PDF bytes.
/// </para>
/// </remarks>
public sealed class PdfDocumentBuilder
{
    private readonly List<PageBuilder> _pages = new();
    private readonly CustomFontRegistry _customFonts = new();
    private LipiFontSet? _lipi;
    private Action<PageBuilder, int, int>? _header;
    private Action<PageBuilder, int, int>? _footer;
    private string? _title;
    private string? _author;
    private string? _subject;

    private PdfDocumentBuilder() { }

    /// <summary>Creates a new empty document builder.</summary>
    public static PdfDocumentBuilder Create() => new();

    /// <summary>Sets the document's /Title metadata.</summary>
    public PdfDocumentBuilder SetTitle(string title) { _title = title; return this; }

    /// <summary>Sets the document's /Author metadata.</summary>
    public PdfDocumentBuilder SetAuthor(string author) { _author = author; return this; }

    /// <summary>Sets the document's /Subject metadata.</summary>
    public PdfDocumentBuilder SetSubject(string sub) { _subject = sub; return this; }

    /// <summary>
    /// Registers a page header callback. The callback receives the page,
    /// 1-based page number, and total page count; it should draw header content.
    /// </summary>
    public PdfDocumentBuilder SetHeader(Action<PageBuilder, int, int> draw)
    {
        _header = draw;
        return this;
    }

    /// <summary>Registers a page footer callback. Same shape as <see cref="SetHeader"/>.</summary>
    public PdfDocumentBuilder SetFooter(Action<PageBuilder, int, int> draw)
    {
        _footer = draw;
        return this;
    }

    /// <summary>
    /// Registers a TrueType font program so pages can draw text in it. The font
    /// is embedded (as a Type0 / CIDFontType2 composite font) only if used, and
    /// only the glyphs actually drawn get width and ToUnicode entries.
    /// </summary>
    /// <param name="name">
    /// The font name to pass to <see cref="PageBuilder.DrawText"/>; also recorded
    /// as the PostScript base-font name.
    /// </param>
    /// <param name="fontData">The complete static TrueType (glyf) font program.</param>
    /// <remarks>
    /// The font must be a static TrueType font (convert variable fonts to a
    /// static instance first). Text is emitted in logical order without
    /// complex-script shaping, so Latin renders correctly and Indic renders
    /// correctly only for isolated or already-ordered glyphs.
    /// </remarks>
    public PdfDocumentBuilder AddTrueTypeFont(string name, byte[] fontData)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(fontData);
        _customFonts.Register(name, fontData);
        return this;
    }

    /// <summary>
    /// Enables the automatic LiPi Sans font. After calling this, drawing text
    /// with the font name "Lipi" selects the matching LiPi face per script and
    /// embeds each used face. Glyphs are emitted in logical order (no complex
    /// shaping); Latin renders correctly, and Indic text is embedded but not yet
    /// reordered.
    /// </summary>
    /// <returns>This builder.</returns>
    public PdfDocumentBuilder UseLipiFonts()
    {
        _lipi ??= new LipiFontSet();
        return this;
    }

    /// <summary>Adds a page of the given size and returns its builder.</summary>
    public PageBuilder AddPage(PageSize size)
    {
        PageBuilder p = new(size, _customFonts, _lipi);
        _pages.Add(p);
        return p;
    }

    /// <summary>Saves the document to a stream.</summary>
    public void Save(Stream output) => Save(output, null);

    /// <summary>
    /// Saves the document to a stream, optionally encrypting it. Pass an
    /// <see cref="EncryptionOptions"/> (for example
    /// <see cref="EncryptionOptions.Aes256(string, string?)"/>) to encrypt, or
    /// null for no encryption. PDF 32000-1:2008 §7.6 — encryption.
    /// </summary>
    /// <param name="output">The stream to write to.</param>
    /// <param name="encryption">The encryption options, or null for no encryption.</param>
    public void Save(Stream output, EncryptionOptions? encryption)
    {
        ArgumentNullException.ThrowIfNull(output);
        byte[] bytes = ToByteArray(encryption);
        output.Write(bytes, 0, bytes.Length);
    }

    /// <summary>Returns the document as a byte array.</summary>
    public byte[] ToByteArray() => ToByteArray(null);

    /// <summary>
    /// Returns the document as a byte array, optionally encrypting it. Pass an
    /// <see cref="EncryptionOptions"/> (for example
    /// <see cref="EncryptionOptions.Aes256(string, string?)"/>) to encrypt, or
    /// null for no encryption. PDF 32000-1:2008 §7.6 — encryption.
    /// </summary>
    /// <param name="encryption">The encryption options, or null for no encryption.</param>
    public byte[] ToByteArray(EncryptionOptions? encryption)
    {
        if (_pages.Count == 0)
        {
            throw new InvalidOperationException("Document has no pages.");
        }

        // Apply header/footer to each page so total-page-count is known.
        int total = _pages.Count;
        for (int i = 0; i < total; i++)
        {
            _header?.Invoke(_pages[i], i + 1, total);
            _footer?.Invoke(_pages[i], i + 1, total);
        }

        return EmitPdf(encryption);
    }

    private byte[] EmitPdf(EncryptionOptions? encryption)
    {
        // Object ID plan:
        // 1 = catalog, 2 = pages, 3..N = page objects, then content streams,
        // resource dicts, fonts, images, link annotations.
        List<PdfIndirectObject> objects = new();
        int nextId = 1;

        PdfObjectId catalogId = new(nextId++, 0);
        PdfObjectId pagesId = new(nextId++, 0);

        // Per-page IDs first; we need them for the /Kids array.
        PdfObjectId[] pageIds = new PdfObjectId[_pages.Count];
        for (int i = 0; i < _pages.Count; i++) { pageIds[i] = new(nextId++, 0); }

        // Embed each used custom font once; share it across pages.
        Dictionary<string, PdfObjectId> customFontIds =
            new Dictionary<string, PdfObjectId>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, CustomFont> entry in _customFonts.Fonts)
        {
            if (entry.Value.UsedCodepoints.Count == 0 && entry.Value.UsedGlyphs.Count == 0)
            {
                continue;
            }

            EmbeddedFontObjects embedded = TrueTypeFontEmbedder.Build(
                entry.Value.FontData,
                entry.Value.Loader,
                entry.Value.UsedCodepoints,
                entry.Key.Replace(" ", string.Empty),
                () => new PdfObjectId(nextId++, 0),
                entry.Value.UsedGlyphs);
            objects.AddRange(embedded.Objects);
            customFontIds[entry.Key] = embedded.Type0FontId;
        }

        // Per-page content stream + resources + annotations.
        for (int i = 0; i < _pages.Count; i++)
        {
            PageBuilder p = _pages[i];

            // Content stream
            PdfObjectId contentId = new(nextId++, 0);
            byte[] content = p.ContentStream();
            PdfDictionary contentDict = new();
            contentDict.Set(PdfName.Length, content.Length);
            objects.Add(new PdfIndirectObject(contentId, new PdfStream(contentDict, content)));

            // Font dictionary entries — one per used font.
            PdfDictionary fontDict = new();
            foreach (string fontName in p.Fonts)
            {
                if (customFontIds.TryGetValue(fontName, out PdfObjectId customId))
                {
                    fontDict.Set(
                        PdfName.Intern(PageBuilder.FontKey(fontName)), new PdfReference(customId));
                    continue;
                }

                PdfObjectId fontId = new(nextId++, 0);
                PdfDictionary font = new();
                font.Set(PdfName.Type, PdfName.Intern("Font"));
                font.Set(PdfName.Intern("Subtype"), PdfName.Intern("Type1"));
                font.Set(PdfName.Intern("BaseFont"), PdfName.Intern(fontName));
                font.Set(PdfName.Intern("Encoding"), PdfName.Intern("WinAnsiEncoding"));
                objects.Add(new PdfIndirectObject(fontId, font));
                fontDict.Set(PdfName.Intern(PageBuilder.FontKey(fontName)), new PdfReference(fontId));
            }

            // XObject dictionary for images. Alpha channels emit a second
            // DeviceGray soft-mask object referenced from the image's /SMask.
            PdfDictionary xobjectDict = new();
            foreach (ImageRef img in p.Images)
            {
                EmbeddedImage built = img.Frame is not null
                    ? ImageEmbedder.BuildFromFrame(img.Frame)
                    : ImageEmbedder.Build(img.Bytes!);
                PdfObjectId imgId = new(nextId++, 0);
                if (built.MaskDictionary is not null)
                {
                    PdfObjectId maskId = new(nextId++, 0);
                    objects.Add(new PdfIndirectObject(
                        maskId, new PdfStream(built.MaskDictionary, built.MaskData)));
                    built.ImageDictionary.Set(PdfName.Intern("SMask"), new PdfReference(maskId));
                }
                objects.Add(new PdfIndirectObject(
                    imgId, new PdfStream(built.ImageDictionary, built.ImageData)));
                xobjectDict.Set(PdfName.Intern(img.Key), new PdfReference(imgId));
            }

            // ExtGState dictionary for image-overlay constant alpha (/ca, /CA).
            PdfDictionary extGStateDict = new();
            foreach (KeyValuePair<string, double> gs in p.ExtGStateAlphas)
            {
                PdfDictionary gsDict = new();
                gsDict.Set(PdfName.Type, PdfName.Intern("ExtGState"));
                gsDict.Set(PdfName.Intern("ca"), new PdfReal(gs.Value));
                gsDict.Set(PdfName.Intern("CA"), new PdfReal(gs.Value));
                extGStateDict.Set(PdfName.Intern(gs.Key), gsDict);
            }

            // Resources
            PdfDictionary resources = new();
            if (p.Fonts.Count > 0) { resources.Set(PdfName.Intern("Font"), fontDict); }
            if (p.Images.Count > 0) { resources.Set(PdfName.Intern("XObject"), xobjectDict); }
            if (p.ExtGStateAlphas.Count > 0) { resources.Set(PdfName.Intern("ExtGState"), extGStateDict); }
            // Always declare ProcSet for older readers.
            PdfArray procSet = new();
            procSet.Add(PdfName.Intern("PDF"));
            procSet.Add(PdfName.Intern("Text"));
            if (p.Images.Count > 0)
            {
                procSet.Add(PdfName.Intern("ImageB"));
                procSet.Add(PdfName.Intern("ImageC"));
            }
            resources.Set(PdfName.Intern("ProcSet"), procSet);

            // Link annotations
            PdfArray annots = new();
            foreach (HyperlinkRect h in p.Hyperlinks)
            {
                PdfObjectId annotId = new(nextId++, 0);
                PdfDictionary annot = new();
                annot.Set(PdfName.Type, PdfName.Intern("Annot"));
                annot.Set(PdfName.Intern("Subtype"), PdfName.Intern("Link"));
                PdfArray rect = new();
                rect.Add(new PdfReal(h.XFromLeft));
                rect.Add(new PdfReal(h.YFromBottom));
                rect.Add(new PdfReal(h.XFromLeft + h.Width));
                rect.Add(new PdfReal(h.YFromBottom + h.Height));
                annot.Set(PdfName.Intern("Rect"), rect);
                PdfArray border = new();
                border.Add(new PdfInteger(0));
                border.Add(new PdfInteger(0));
                border.Add(new PdfInteger(0));
                annot.Set(PdfName.Intern("Border"), border);
                PdfDictionary action = new();
                action.Set(PdfName.Type, PdfName.Intern("Action"));
                action.Set(PdfName.Intern("S"), PdfName.Intern("URI"));
                action.Set(PdfName.Intern("URI"), new PdfString(h.LinkUri));
                annot.Set(PdfName.Intern("A"), action);
                objects.Add(new PdfIndirectObject(annotId, annot));
                annots.Add(new PdfReference(annotId));
            }

            // Page dictionary
            PdfDictionary page = new();
            page.Set(PdfName.Type, PdfName.Intern("Page"));
            page.Set(PdfName.Intern("Parent"), new PdfReference(pagesId));
            PdfArray mediaBox = new();
            mediaBox.Add(new PdfReal(0));
            mediaBox.Add(new PdfReal(0));
            mediaBox.Add(new PdfReal(p.Width));
            mediaBox.Add(new PdfReal(p.Height));
            page.Set(PdfName.Intern("MediaBox"), mediaBox);
            page.Set(PdfName.Intern("Resources"), resources);
            page.Set(PdfName.Intern("Contents"), new PdfReference(contentId));
            if (p.Hyperlinks.Count > 0) { page.Set(PdfName.Intern("Annots"), annots); }
            objects.Add(new PdfIndirectObject(pageIds[i], page));
        }

        // Pages root
        PdfDictionary pagesDict = new();
        pagesDict.Set(PdfName.Type, PdfName.Intern("Pages"));
        PdfArray kids = new();
        foreach (PdfObjectId id in pageIds) { kids.Add(new PdfReference(id)); }
        pagesDict.Set(PdfName.Intern("Kids"), kids);
        pagesDict.Set(PdfName.Intern("Count"), (PdfPrimitive)new PdfInteger(_pages.Count));
        objects.Add(new PdfIndirectObject(pagesId, pagesDict));

        // Catalog
        PdfDictionary catalog = new();
        catalog.Set(PdfName.Type, PdfName.Intern("Catalog"));
        catalog.Set(PdfName.Intern("Pages"), new PdfReference(pagesId));
        objects.Add(new PdfIndirectObject(catalogId, catalog));

        // Info dictionary (optional)
        PdfDictionary trailer = new();
        trailer.Set(PdfName.Intern("Root"), new PdfReference(catalogId));
        if (_title is not null || _author is not null || _subject is not null)
        {
            PdfObjectId infoId = new(nextId++, 0);
            PdfDictionary info = new();
            if (_title is not null) { info.Set(PdfName.Intern("Title"), new PdfString(_title)); }
            if (_author is not null) { info.Set(PdfName.Intern("Author"), new PdfString(_author)); }
            if (_subject is not null) { info.Set(PdfName.Intern("Subject"), new PdfString(_subject)); }
            objects.Add(new PdfIndirectObject(infoId, info));
            trailer.Set(PdfName.Intern("Info"), new PdfReference(infoId));
        }

        MemoryStream ms = new();
        PdfWriter.Write(ms, objects, trailer, encryption);
        return ms.ToArray();
    }
}
