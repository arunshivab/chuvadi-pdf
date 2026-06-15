// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 0 — compression measurement foundations

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Benchmarks.Compression;

/// <summary>
/// A single named document in the compression corpus together with whether the
/// lossy image path (<see cref="Chuvadi.Pdf.Operations.CompressionOptions.RecompressImages"/>)
/// should be exercised for it.
/// </summary>
public sealed class CompressionScenario
{
    /// <summary>Creates a scenario.</summary>
    /// <param name="name">Stable identifier used as the baseline key.</param>
    /// <param name="pdf">The source PDF bytes.</param>
    /// <param name="lossy">Whether image recompression should be enabled.</param>
    /// <param name="imageRgb">
    /// For lossy scenarios, the source image's raw 8-bit RGB samples, used to
    /// measure post-recompression quality; null when the scenario has no image.
    /// </param>
    /// <param name="imageWidth">Source image width in pixels (0 when none).</param>
    /// <param name="imageHeight">Source image height in pixels (0 when none).</param>
    public CompressionScenario(
        string name, byte[] pdf, bool lossy,
        byte[]? imageRgb = null, int imageWidth = 0, int imageHeight = 0)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(pdf);
        Name = name;
        Pdf = pdf;
        Lossy = lossy;
        ImageRgb = imageRgb;
        ImageWidth = imageWidth;
        ImageHeight = imageHeight;
    }

    /// <summary>Stable identifier used as the baseline key.</summary>
    public string Name { get; }

    /// <summary>The source PDF bytes.</summary>
    public byte[] Pdf { get; }

    /// <summary>Whether image recompression should be enabled when measuring.</summary>
    public bool Lossy { get; }

    /// <summary>Source image RGB samples for quality measurement, or null.</summary>
    public byte[]? ImageRgb { get; }

    /// <summary>Source image width in pixels (0 when there is no image).</summary>
    public int ImageWidth { get; }

    /// <summary>Source image height in pixels (0 when there is no image).</summary>
    public int ImageHeight { get; }
}

/// <summary>
/// Builds a deterministic synthetic corpus of PDFs, each crafted to exercise a
/// specific reduction in <see cref="Chuvadi.Pdf.Operations.PdfCompressor"/>:
/// raw-stream Flate compression, unreachable-object garbage collection, and
/// lossy image recompression. The documents are generated in code (no binary
/// fixtures) so the corpus is reproducible and diff-friendly.
/// </summary>
public static class CompressionCorpus
{
    /// <summary>Returns every corpus scenario in a stable order.</summary>
    public static IReadOnlyList<CompressionScenario> All()
    {
        List<CompressionScenario> scenarios = new List<CompressionScenario>
        {
            new CompressionScenario("raw-text-streams", BuildRawTextStreams(), lossy: false),
            new CompressionScenario("orphan-objects", BuildOrphanObjects(), lossy: false),
            new CompressionScenario(
                "raw-rgb-image", BuildRawRgbImage(), lossy: true,
                GradientImage(96, 96), 96, 96),
            new CompressionScenario(
                "mixed", BuildMixed(), lossy: true,
                GradientImage(96, 96), 96, 96),
        };

        return scenarios;
    }

    // A page whose content stream is large, raw (unfiltered), and highly
    // repetitive — the ideal case for raw-stream Flate compression.
    private static byte[] BuildRawTextStreams()
    {
        byte[] content = RepetitiveContent(24);
        return BuildSinglePage(content, image: null, extraOrphans: 0);
    }

    // A small valid document padded with unreachable indirect objects that a
    // reachability pass should drop.
    private static byte[] BuildOrphanObjects()
    {
        byte[] content = RepetitiveContent(2);
        return BuildSinglePage(content, image: null, extraOrphans: 40);
    }

    // A page that paints a raw 8-bit DeviceRGB image — the lossy recompression
    // path re-encodes it as JPEG.
    private static byte[] BuildRawRgbImage()
    {
        return BuildSinglePage(SimpleContent(), image: GradientImage(96, 96), extraOrphans: 0);
    }

    // A document combining repetitive raw content, an image, and orphans.
    private static byte[] BuildMixed()
    {
        return BuildSinglePage(RepetitiveContent(12), image: GradientImage(96, 96), extraOrphans: 16);
    }

    private static byte[] RepetitiveContent(int kilobytes)
    {
        StringBuilder builder = new StringBuilder();
        builder.Append("BT /F1 12 Tf 72 720 Td\n");
        string line = "(The quick brown fox jumps over the lazy dog 0123456789) Tj 0 -14 Td\n";
        int target = kilobytes * 1024;
        while (builder.Length < target)
        {
            builder.Append(line);
        }

        builder.Append("ET\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private static byte[] SimpleContent()
    {
        return Encoding.ASCII.GetBytes("q 96 0 0 96 100 600 cm /Im0 Do Q\n");
    }

    private static byte[] GradientImage(int width, int height)
    {
        // A high-frequency deterministic pattern (sharp transitions) so JPEG
        // recompression produces a measurable quality loss — making the SSIM
        // baseline a sensitive regression guard rather than a near-1.0 constant.
        byte[] samples = new byte[width * height * 3];
        int index = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                samples[index++] = (byte)((x ^ y) & 0xFF);
                samples[index++] = (byte)(((x * 7) + (y * 13)) & 0xFF);
                samples[index++] = (byte)(((x >> 1) ^ (y << 1)) & 0xFF);
            }
        }

        return samples;
    }

    private static byte[] BuildSinglePage(byte[] content, byte[]? image, int extraOrphans)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);
        PdfObjectId contentId = new PdfObjectId(4, 0);

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>();

        PdfDictionary contentDict = new PdfDictionary();
        contentDict.Set(PdfName.Length, content.Length);
        objects.Add(new PdfIndirectObject(contentId, new PdfStream(contentDict, content)));

        PdfDictionary pageDict = new PdfDictionary();
        pageDict.Set(PdfName.Type, PdfName.Page);
        pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
        pageDict.Set(PdfName.MediaBox, new PdfArray([
            new PdfInteger(0), new PdfInteger(0),
            new PdfInteger(612), new PdfInteger(792),
        ]));
        pageDict.Set(PdfName.Intern("Contents"), new PdfReference(contentId));

        int nextNumber = 5;
        if (image is not null)
        {
            PdfObjectId imageId = new PdfObjectId(nextNumber++, 0);
            PdfDictionary imageDict = new PdfDictionary();
            imageDict.Set(PdfName.Type, PdfName.XObject);
            imageDict.Set(PdfName.Intern("Subtype"), PdfName.Intern("Image"));
            imageDict.Set(PdfName.Intern("Width"), 96);
            imageDict.Set(PdfName.Intern("Height"), 96);
            imageDict.Set(PdfName.Intern("ColorSpace"), PdfName.Intern("DeviceRGB"));
            imageDict.Set(PdfName.Intern("BitsPerComponent"), 8);
            imageDict.Set(PdfName.Length, image.Length);
            objects.Add(new PdfIndirectObject(imageId, new PdfStream(imageDict, image)));

            PdfDictionary xobjects = new PdfDictionary();
            xobjects.Set(PdfName.Intern("Im0"), new PdfReference(imageId));
            PdfDictionary resources = new PdfDictionary();
            resources.Set(PdfName.Intern("XObject"), xobjects);
            pageDict.Set(PdfName.Intern("Resources"), resources);
        }

        objects.Add(new PdfIndirectObject(pageId, pageDict));

        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, new PdfArray([new PdfReference(pageId)]));
        pagesDict.Set(PdfName.Count, 1);
        objects.Add(new PdfIndirectObject(pagesId, pagesDict));

        PdfDictionary catalogDict = new PdfDictionary();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));
        objects.Add(new PdfIndirectObject(catalogId, catalogDict));

        // Unreachable padding objects: never referenced from the catalog graph,
        // so a reachability pass drops them.
        for (int i = 0; i < extraOrphans; i++)
        {
            PdfObjectId orphanId = new PdfObjectId(nextNumber++, 0);
            byte[] orphanBytes = RepetitiveContent(1);
            PdfDictionary orphanDict = new PdfDictionary();
            orphanDict.Set(PdfName.Length, orphanBytes.Length);
            objects.Add(new PdfIndirectObject(orphanId, new PdfStream(orphanDict, orphanBytes)));
        }

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        using MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        return ms.ToArray();
    }
}
