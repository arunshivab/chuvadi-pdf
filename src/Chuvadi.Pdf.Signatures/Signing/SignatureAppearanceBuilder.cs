// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ISO 32000-1 §12.7.4.5 (signature fields), §12.5.5 (appearance streams),
//        §9.6.4 (font subsets), §8.9.5 (image XObjects), §11.6.5.2 (soft masks)
// PHASE: 3.32.0 (D-208 1c, D-230 Q1 and Q2) — the default signature
//        appearance: text, an optional image, and a border.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Forms.Appearance;
using Chuvadi.Pdf.Images;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Signatures.Signing;

/// <summary>
/// Builds the default appearance of a visible signature: the text lines
/// (by default "Digitally signed by {name}" and "Date: {UTC time}"), the image
/// when one is given, and a thin border. Fixed layout (D-230 Q2): with an image
/// and text, the image takes the left third and the text the rest; with only
/// one of them, it takes the whole box.
/// </summary>
/// <remarks>
/// Text is drawn by the same code as generated form-field appearances:
/// Helvetica for what it can write, LiPi Sans (from Chuvadi.Fonts) for other
/// scripts. A LiPi face is embedded as a subset of the glyphs used. The
/// objects the appearance needs (the fonts' streams, the image and its soft
/// mask) become indirect objects numbered from <c>firstId</c>.
/// </remarks>
internal static class SignatureAppearanceBuilder
{
    // Inner margin, in points, between the border and the text or image.
    private const double Padding = 2.0;

    // Line height as a multiple of the font size.
    private const double Leading = 1.2;

    // The text is sized to fit, but never larger than this.
    private const double MaxFontSize = 24.0;

    /// <summary>The appearance content and resources, and the objects it refers to.</summary>
    internal sealed record Result(byte[] Content, PdfDictionary Resources, IReadOnlyList<PdfIndirectObject> Objects);

    /// <summary>
    /// The default text: "Digitally signed by {name}" (or "Digitally signed"
    /// with no name) and "Date: {time}", the time in UTC, ISO 8601 to the second.
    /// </summary>
    internal static string DefaultText(string? signerName, DateTimeOffset signingTime)
    {
        string first = string.IsNullOrWhiteSpace(signerName) ? "Digitally signed" : "Digitally signed by " + signerName;
        string date = signingTime.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        return first + "\nDate: " + date;
    }

    /// <summary>Builds the appearance for a box of the given size.</summary>
    /// <exception cref="ArgumentException">The image is not a JPEG or PNG that can be embedded.</exception>
    internal static Result Build(
        PdfObjectStore objects,
        SignatureAppearance appearance,
        string? signerName,
        DateTimeOffset signingTime,
        double width,
        double height,
        int firstId)
    {
        EmbeddedImage? image = appearance.Image is byte[] imageBytes ? LoadImage(imageBytes) : null;
        string text = appearance.Text ?? DefaultText(signerName, signingTime);
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        bool hasText = text.Length > 0;

        PdfDictionary resources = new PdfDictionary();
        AppearanceContent content = new AppearanceContent();
        List<PdfIndirectObject> created = new List<PdfIndirectObject>();
        int nextId = firstId;

        double innerX = Padding;
        double innerY = Padding;
        double innerW = Math.Max(0, width - (2 * Padding));
        double innerH = Math.Max(0, height - (2 * Padding));

        double textX = innerX;
        double textW = innerW;
        if (image is not null)
        {
            double imageW = hasText ? (innerW / 3.0) - (Padding / 2.0) : innerW;
            PdfObjectId imageId = AddImage(image, created, ref nextId);
            PdfDictionary xobjects = new PdfDictionary();
            xobjects.Set(PdfName.Intern("CvSigImage"), new PdfReference(imageId));
            resources.Set(PdfName.Intern("XObject"), xobjects);

            // Fit the image inside its area, keeping its aspect ratio, centred.
            double scale = Math.Min(imageW / image.Width, innerH / image.Height);
            if (scale > 0 && !double.IsInfinity(scale))
            {
                double drawW = image.Width * scale;
                double drawH = image.Height * scale;
                double x = innerX + ((imageW - drawW) / 2.0);
                double y = innerY + ((innerH - drawH) / 2.0);
                content.Op("q");
                content.Op("cm", drawW, 0, 0, drawH, x, y);
                content.Raw("/CvSigImage Do\n");
                content.Op("Q");
            }

            textX = innerX + imageW + Padding;
            textW = Math.Max(0, innerW - imageW - Padding);
        }

        if (hasText && textW > 0 && innerH > 0)
        {
            // Measure at 1 pt with a throwaway resource dictionary, then draw
            // at the largest size that fits both ways.
            AppearanceText probe = new AppearanceText(objects, new PdfDictionary(), null, "Helv", 1.0);
            double widest = 0;
            foreach (string line in lines)
            {
                widest = Math.Max(widest, probe.Measure(line));
            }

            double size = Math.Min(MaxFontSize, innerH / (lines.Length * Leading));
            if (widest > 0)
            {
                size = Math.Min(size, textW / widest);
            }

            if (size > 0)
            {
                AppearanceText writer = new AppearanceText(objects, resources, null, "Helv", size);
                double blockH = lines.Length * size * Leading;
                double top = innerY + innerH - ((innerH - blockH) / 2.0);
                double baseline = top - (writer.Ascent * size);
                foreach (string line in lines)
                {
                    writer.Show(content, PdfColorSpec.Black, line, textX, baseline);
                    baseline -= size * Leading;
                }

                writer.Finish();
            }
        }

        // The border, as before 3.32.0.
        content.Raw(string.Format(CultureInfo.InvariantCulture, "q 0.5 w 0 0 {0:0.##} {1:0.##} re S Q\n", width, height));

        SubsetFallbackFonts(resources);
        PdfDictionary hoisted = (PdfDictionary)Hoist(resources, created, ref nextId);
        return new Result(content.ToBytes(), hoisted, created);
    }

    // JPEG or PNG only (D-208 1c); anything else, or a PNG that cannot be
    // embedded, is an ArgumentException naming the problem.
    private static EmbeddedImage LoadImage(byte[] bytes)
    {
        ImageFormat format = ImageEmbedder.Sniff(bytes);
        if (format is not (ImageFormat.Jpeg or ImageFormat.Png))
        {
            string found = format == ImageFormat.Unknown ? "an unrecognised format" : format.ToString().ToUpperInvariant();
            throw new ArgumentException(
                $"SignatureAppearance.Image must be a JPEG or PNG image; the bytes are {found}.", nameof(bytes));
        }

        try
        {
            return ImageEmbedder.Build(bytes);
        }
        catch (ImageException ex)
        {
            throw new ArgumentException(
                $"SignatureAppearance.Image is a {format.ToString().ToUpperInvariant()} that cannot be embedded: {ex.Message}", nameof(bytes), ex);
        }
    }

    private static PdfObjectId AddImage(EmbeddedImage image, List<PdfIndirectObject> created, ref int nextId)
    {
        PdfObjectId imageId = new PdfObjectId(nextId++, 0);
        if (image.MaskDictionary is not null)
        {
            PdfObjectId maskId = new PdfObjectId(nextId++, 0);
            image.MaskDictionary.Set(PdfName.Length, image.MaskData.Length);
            created.Add(new PdfIndirectObject(maskId, new PdfStream(image.MaskDictionary, image.MaskData)));
            image.ImageDictionary.Set(PdfName.Intern("SMask"), new PdfReference(maskId));
        }

        image.ImageDictionary.Set(PdfName.Length, image.ImageData.Length);
        created.Add(new PdfIndirectObject(imageId, new PdfStream(image.ImageDictionary, image.ImageData)));
        return imageId;
    }

    // The LiPi fallback fonts carry the whole font program. Keep only the
    // glyphs used (their ids stay the same, so the Identity CID-to-GID map
    // holds), compress the program, and tag the names as a subset (§9.6.4).
    private static void SubsetFallbackFonts(PdfDictionary resources)
    {
        if (!resources.TryGetValue(PdfName.Intern("Font"), out PdfPrimitive? fontsValue) || fontsValue is not PdfDictionary fonts)
        {
            return;
        }

        foreach (KeyValuePair<PdfName, PdfPrimitive> entry in fonts)
        {
            if (entry.Value is not PdfDictionary type0
                || !type0.TryGetValue(PdfName.Intern("DescendantFonts"), out PdfPrimitive? descendants)
                || descendants is not PdfArray { Count: > 0 } array
                || array[0] is not PdfDictionary cidFont
                || !cidFont.TryGetValue(PdfName.Intern("FontDescriptor"), out PdfPrimitive? fdValue)
                || fdValue is not PdfDictionary descriptor
                || !descriptor.TryGetValue(PdfName.Intern("FontFile2"), out PdfPrimitive? ffValue)
                || ffValue is not PdfStream fontFile)
            {
                continue;
            }

            SortedSet<int> used = new SortedSet<int> { 0 };
            if (cidFont.TryGetValue(PdfName.Intern("W"), out PdfPrimitive? wValue) && wValue is PdfArray widths)
            {
                for (int i = 0; i + 1 < widths.Count; i += 2)
                {
                    if (widths[i] is PdfInteger gid)
                    {
                        used.Add(gid.Value);
                    }
                }
            }

            byte[] subset = TrueTypeSubsetter.Subset(fontFile.RawBytes, used);
            byte[] packed = Deflate(subset);
            PdfDictionary ffDict = new PdfDictionary();
            ffDict.Set(PdfName.Intern("Length1"), subset.Length);
            ffDict.Set(PdfName.Intern("Filter"), PdfName.Intern("FlateDecode"));
            ffDict.Set(PdfName.Length, packed.Length);
            descriptor.Set(PdfName.Intern("FontFile2"), new PdfStream(ffDict, packed));

            string tag = SubsetTag(used);
            Retag(type0, "BaseFont", tag);
            Retag(cidFont, "BaseFont", tag);
            Retag(descriptor, "FontName", tag);
        }
    }

    private static void Retag(PdfDictionary dict, string key, string tag)
    {
        if (dict.TryGetValue(PdfName.Intern(key), out PdfPrimitive? value) && value is PdfName name)
        {
            dict.Set(PdfName.Intern(key), PdfName.Intern(tag + "+" + name.Value));
        }
    }

    // Six upper-case letters from the glyph set, so the same glyphs give the same tag.
    private static string SubsetTag(IEnumerable<int> glyphs)
    {
        uint hash = 2166136261;
        foreach (int g in glyphs)
        {
            hash = (hash ^ (uint)g) * 16777619;
        }

        StringBuilder tag = new StringBuilder(6);
        for (int i = 0; i < 6; i++)
        {
            tag.Append((char)('A' + (hash % 26)));
            hash /= 26;
        }

        return tag.ToString();
    }

    private static byte[] Deflate(byte[] data)
    {
        using MemoryStream output = new MemoryStream();
        using (ZLibStream z = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            z.Write(data, 0, data.Length);
        }

        return output.ToArray();
    }

    // Replaces every stream inside the resources with a reference to a new
    // indirect object (a stream cannot be a direct object in a PDF file).
    private static PdfPrimitive Hoist(PdfPrimitive value, List<PdfIndirectObject> created, ref int nextId)
    {
        switch (value)
        {
            case PdfStream stream:
                {
                    PdfDictionary inner = (PdfDictionary)Hoist(stream.Dictionary, created, ref nextId);
                    inner.Set(PdfName.Length, stream.RawBytes.Length);
                    PdfObjectId id = new PdfObjectId(nextId++, 0);
                    created.Add(new PdfIndirectObject(id, new PdfStream(inner, stream.RawBytes)));
                    return new PdfReference(id);
                }

            case PdfDictionary dict:
                {
                    PdfDictionary copy = new PdfDictionary();
                    foreach (KeyValuePair<PdfName, PdfPrimitive> entry in dict)
                    {
                        copy.Set(entry.Key, Hoist(entry.Value, created, ref nextId));
                    }

                    return copy;
                }

            case PdfArray array:
                {
                    PdfArray copy = new PdfArray();
                    foreach (PdfPrimitive item in array)
                    {
                        copy.Add(Hoist(item, created, ref nextId));
                    }

                    return copy;
                }

            default:
                return value;
        }
    }
}
