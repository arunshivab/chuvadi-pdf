// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.4 (filters); §7.6 (encryption guard).
// LA-27 — fold the descending-quality compression loop into one call.

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Images;
using Chuvadi.Pdf.Rendering;

namespace Chuvadi.Pdf.Operations;

public static partial class PdfCompressor
{
    /// <summary>
    /// Compresses <paramref name="document"/> to <paramref name="output"/>, binary-searching
    /// JPEG quality for the highest setting whose output is at or below
    /// <paramref name="targetBytes"/>. When no quality meets the target, the smallest
    /// achievable output is written and <see cref="CompressToTargetResult.TargetMet"/> is
    /// <see langword="false"/>. Signed or encrypted documents that are not opted in for
    /// rewrite are re-serialized unchanged and reported via
    /// <see cref="CompressToTargetResult.SkipReason"/>.
    /// </summary>
    /// <param name="document">The document to compress.</param>
    /// <param name="output">The stream the compressed document is written to.</param>
    /// <param name="targetBytes">The desired maximum output size, in bytes.</param>
    /// <param name="options">The search bounds and base compression options, or null for defaults.</param>
    /// <returns>The size, quality, and target outcome of the written document.</returns>
    public static CompressToTargetResult CompressToTarget(
        PdfDocument document, Stream output, long targetBytes, CompressToTargetOptions? options = null)
    {
        return PdfWriteGuard.Run(document, "compressed", () =>
        {
            ArgumentNullException.ThrowIfNull(document);
            ArgumentNullException.ThrowIfNull(output);
            options ??= new CompressToTargetOptions();

            if (options.MaxLevel is PdfCompressionLevel maxLevel)
            {
                return CompressToTargetByLevel(document, output, targetBytes, options, maxLevel);
            }

            int low = Math.Clamp(options.MinQuality, 1, 100);
            int high = Math.Clamp(options.MaxQuality, low, 100);

            byte[]? best = null;
            int bestQuality = 0;

            while (low <= high)
            {
                int mid = low + ((high - low) / 2);
                (byte[] bytes, CompressionSkipReason skip) = CompressAt(document, options, mid);

                if (skip != CompressionSkipReason.None)
                {
                    return WriteOriginal(document, output, targetBytes, skip);
                }

                if (bytes.Length <= targetBytes)
                {
                    best = bytes;
                    bestQuality = mid;
                    low = mid + 1; // fits — try higher quality
                }
                else
                {
                    high = mid - 1; // too big — drop quality
                }
            }

            if (best is not null)
            {
                output.Write(best, 0, best.Length);
                return new CompressToTargetResult
                {
                    FinalSize = best.Length,
                    QualityUsed = bestQuality,
                    TargetMet = true,
                    SkipReason = CompressionSkipReason.None,
                };
            }

            // No quality met the target: the document is at its vector floor —
            // content streams and fonts dominate and no image re-encode can help.
            (byte[] smallest, CompressionSkipReason minSkip) = CompressAt(document, options, options.MinQuality);
            if (minSkip != CompressionSkipReason.None)
            {
                return WriteOriginal(document, output, targetBytes, minSkip);
            }

            // Opt-in rasterize fallback: replace each page with a JPEG raster,
            // descending in DPI until the target is met. Never silent — the
            // caller chose to trade selectable text for bounded size.
            if (options.AllowRasterizeFallback)
            {
                CompressToTargetResult? rasterized = TryRasterizeToTarget(
                    document, output, targetBytes, options, smallest);
                if (rasterized is not null)
                {
                    return rasterized;
                }
            }

            output.Write(smallest, 0, smallest.Length);
            return new CompressToTargetResult
            {
                FinalSize = smallest.Length,
                QualityUsed = Math.Clamp(options.MinQuality, 1, 100),
                TargetMet = smallest.Length <= targetBytes,
                SkipReason = CompressionSkipReason.None,
            };
        });
    }

    // Rasterizes every page to a JPEG image and writes an image-only PDF,
    // descending in DPI from RasterizeDpi to MinRasterizeDpi (and dropping the
    // JPEG quality to MinQuality for the final attempt) until the output meets
    // the target. Returns null when even the smallest raster output is larger
    // than the smallest vector output — the caller then writes the vector one.
    private static CompressToTargetResult? TryRasterizeToTarget(
        PdfDocument document, Stream output, long targetBytes,
        CompressToTargetOptions options, byte[] smallestVector)
    {
        double maxDpi = Math.Max(options.RasterizeDpi, 1);
        double minDpi = Math.Clamp(options.MinRasterizeDpi, 1, maxDpi);
        int minQuality = Math.Clamp(options.MinQuality, 1, 100);

        byte[]? smallestRaster = null;
        double smallestRasterDpi = 0;
        int smallestRasterQuality = 0;

        // DPI ladder: maxDpi, maxDpi/1.5, … down to minDpi (inclusive), each
        // at a mid JPEG quality; then one last attempt at minDpi + minQuality.
        double dpi = maxDpi;
        while (true)
        {
            const int LadderQuality = 60;
            byte[] attempt = BuildRasterPdf(document, dpi, LadderQuality);
            if (smallestRaster is null || attempt.Length < smallestRaster.Length)
            {
                smallestRaster = attempt;
                smallestRasterDpi = dpi;
                smallestRasterQuality = LadderQuality;
            }

            if (attempt.Length <= targetBytes)
            {
                output.Write(attempt, 0, attempt.Length);
                return new CompressToTargetResult
                {
                    FinalSize = attempt.Length,
                    QualityUsed = LadderQuality,
                    TargetMet = true,
                    SkipReason = CompressionSkipReason.None,
                    UsedRasterizeFallback = true,
                    RasterizeDpiUsed = dpi,
                };
            }

            if (dpi <= minDpi)
            {
                break;
            }

            dpi = Math.Max(dpi / 1.5, minDpi);
        }

        byte[] floor = BuildRasterPdf(document, minDpi, minQuality);
        if (floor.Length < smallestRaster!.Length)
        {
            smallestRaster = floor;
            smallestRasterDpi = minDpi;
            smallestRasterQuality = minQuality;
        }

        if (floor.Length <= targetBytes)
        {
            output.Write(floor, 0, floor.Length);
            return new CompressToTargetResult
            {
                FinalSize = floor.Length,
                QualityUsed = minQuality,
                TargetMet = true,
                SkipReason = CompressionSkipReason.None,
                UsedRasterizeFallback = true,
                RasterizeDpiUsed = minDpi,
            };
        }

        // Target unreachable either way: write whichever output is smaller.
        if (smallestRaster.Length < smallestVector.Length)
        {
            output.Write(smallestRaster, 0, smallestRaster.Length);
            return new CompressToTargetResult
            {
                FinalSize = smallestRaster.Length,
                QualityUsed = smallestRasterQuality,
                TargetMet = false,
                SkipReason = CompressionSkipReason.None,
                UsedRasterizeFallback = true,
                RasterizeDpiUsed = smallestRasterDpi,
            };
        }

        return null;
    }

    // Builds an image-only PDF: each page of the source rasterized at the
    // given DPI and embedded as a JPEG filling a page of the same point size.
    // 3.32.0 (D-240 R1): the page's /Rotate is applied to the raster, so a
    // rotated page shows the same way up as in the source (the new page has no
    // /Rotate of its own and takes the rotated size).
    private static byte[] BuildRasterPdf(PdfDocument document, double dpi, int quality)
    {
        PageRasterizer rasterizer = new PageRasterizer(
            document.Objects,
            new RenderOptions
            {
                Dpi = dpi,
                ApplyPageRotation = true,
            });

        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        for (int i = 0; i < document.PageCount; i++)
        {
            PixelBuffer buffer = rasterizer.Rasterize(document.Pages[i]);
            ImageFrame frame = new ImageFrame(buffer, ImageColorFormat.Rgb24);

            byte[] jpeg;
            using (MemoryStream ms = new MemoryStream())
            {
                JpegEncoder.Encode(frame, ms, quality);
                jpeg = ms.ToArray();
            }

            double widthPts = buffer.Width * 72.0 / dpi;
            double heightPts = buffer.Height * 72.0 / dpi;
            builder.AddPage(new PageSize(widthPts, heightPts))
                .DrawImage(jpeg, 0, 0, widthPts, heightPts);
        }

        return builder.ToByteArray();
    }

    private static (byte[] Bytes, CompressionSkipReason Skip) CompressAt(
        PdfDocument document, CompressToTargetOptions options, int quality)
    {
        CompressionOptions step = options.BaseOptions with
        {
            RecompressImages = true,
            JpegQuality = Math.Clamp(quality, 1, 100),
        };

        using MemoryStream buffer = new MemoryStream();
        CompressionResult result = Compress(document, buffer, step);
        return result.Skipped
            ? (Array.Empty<byte>(), result.SkipReason)
            : (buffer.ToArray(), CompressionSkipReason.None);
    }

    // 3.32.0 (D-243, D-269, D-274): tries Lossless, Medium, High, reduced
    // colour 150, black and white with colour 150, reduced colour 130, black
    // and white with colour 130, black and white 150, black and white 130,
    // up to maxLevel, and writes the first output that fits; when none fits,
    // the smallest one. Below 130 dpi only black and white, and only with
    // leave (D-265).
    private static CompressToTargetResult CompressToTargetByLevel(
        PdfDocument document, Stream output, long targetBytes, CompressToTargetOptions options, PdfCompressionLevel maxLevel)
    {
        if (!Enum.IsDefined(maxLevel))
        {
            throw new ArgumentOutOfRangeException(nameof(options), maxLevel, "Unknown compression level in CompressToTargetOptions.MaxLevel.");
        }

        ImageLevelOptions imageOptions = options.ImageOptions ?? new ImageLevelOptions();
        imageOptions.Validate(nameof(options));

        byte[]? smallest = null;
        PdfCompressionLevel smallestLevel = PdfCompressionLevel.Lossless;
        double smallestDpi = 0;

        CompressToTargetResult Write(byte[] bytes, PdfCompressionLevel level, double dpi)
        {
            output.Write(bytes, 0, bytes.Length);
            return new CompressToTargetResult
            {
                FinalSize = bytes.Length,
                QualityUsed = QualityOf(level),
                TargetMet = bytes.Length <= targetBytes,
                SkipReason = CompressionSkipReason.None,
                LevelUsed = level,
                ImageDpiUsed = dpi,
                BelowReadableResolution = dpi > 0 && dpi < ImageLevelOptions.ReadableDpi,
            };
        }

        void Track(byte[] bytes, PdfCompressionLevel level, double dpi)
        {
            if (smallest is null || bytes.Length < smallest.Length)
            {
                smallest = bytes;
                smallestLevel = level;
                smallestDpi = dpi;
            }
        }

        // Each page is rendered once per resolution: the first image step at a
        // resolution builds every image level up to maxLevel at it.
        Dictionary<(PdfCompressionLevel Level, double Dpi), byte[]> built = new Dictionary<(PdfCompressionLevel Level, double Dpi), byte[]>();
        foreach ((PdfCompressionLevel level, double dpi) in LevelSteps(maxLevel, imageOptions))
        {
            byte[] bytes;
            if (IsImageLevel(level))
            {
                if (!built.TryGetValue((level, dpi), out byte[]? cached))
                {
                    PdfCompressionLevel[] levels = ImageLevelsFrom(level, maxLevel, dpi);
                    byte[][] outputs = BuildImageLevels(document, levels, dpi, imageOptions.Fills);
                    for (int i = 0; i < levels.Length; i++)
                    {
                        built[(levels[i], dpi)] = outputs[i];
                    }

                    cached = built[(level, dpi)];
                }

                bytes = cached;
                built.Remove((level, dpi));
            }
            else
            {
                using MemoryStream buffer = new MemoryStream();
                CompressionResult result = Compress(document, buffer, OptionsFor(level, options.BaseOptions));
                if (result.Skipped)
                {
                    return WriteOriginal(document, output, targetBytes, result.SkipReason);
                }

                bytes = buffer.ToArray();
            }

            if (bytes.Length <= targetBytes)
            {
                return Write(bytes, level, dpi);
            }

            Track(bytes, level, dpi);
        }

        return Write(smallest!, smallestLevel, smallestDpi);
    }

    // The image levels from `level` up to maxLevel; below 130 dpi only black
    // and white is tried.
    private static PdfCompressionLevel[] ImageLevelsFrom(PdfCompressionLevel level, PdfCompressionLevel maxLevel, double dpi)
    {
        if (dpi < ImageLevelOptions.ReadableDpi)
        {
            return new[] { level };
        }

        List<PdfCompressionLevel> levels = new List<PdfCompressionLevel>();
        for (PdfCompressionLevel l = level; l <= maxLevel; l++)
        {
            levels.Add(l);
        }

        return levels.ToArray();
    }

    // The level search's steps, in order, up to and including maxLevel. The
    // rewrite levels carry dpi 0.
    internal static IEnumerable<(PdfCompressionLevel Level, double Dpi)> LevelSteps(PdfCompressionLevel maxLevel, ImageLevelOptions imageOptions)
    {
        for (PdfCompressionLevel level = PdfCompressionLevel.Lossless; level <= maxLevel && !IsImageLevel(level); level++)
        {
            yield return (level, 0);
        }

        // D-274: the colour image levels resolution first (both at 150 dpi,
        // then both at 130), so the sharper black-and-white-with-colour at
        // 150 dpi is tried before reduced colour at 130; black and white last.
        PdfCompressionLevel lastColor = maxLevel < PdfCompressionLevel.BlackAndWhiteWithColor ? maxLevel : PdfCompressionLevel.BlackAndWhiteWithColor;
        foreach (double resolution in new[] { ImageLevelDpi, ImageLevelOptions.ReadableDpi })
        {
            for (PdfCompressionLevel level = PdfCompressionLevel.ReducedColor; level <= lastColor; level++)
            {
                yield return (level, resolution);
            }
        }

        if (maxLevel >= PdfCompressionLevel.BlackAndWhite)
        {
            yield return (PdfCompressionLevel.BlackAndWhite, ImageLevelDpi);
            yield return (PdfCompressionLevel.BlackAndWhite, ImageLevelOptions.ReadableDpi);
        }

        if (maxLevel == PdfCompressionLevel.BlackAndWhite
            && imageOptions.AllowBelowReadableResolution
            && imageOptions.Dpi < ImageLevelOptions.ReadableDpi)
        {
            double floor = imageOptions.Dpi;
            double dpi = ImageLevelOptions.ReadableDpi;
            while (dpi > floor)
            {
                dpi = Math.Max(Math.Round(dpi * 0.85, 1), floor);
                yield return (PdfCompressionLevel.BlackAndWhite, dpi);
            }
        }
    }

    private static int QualityOf(PdfCompressionLevel level) => level switch
    {
        PdfCompressionLevel.Medium => 75,
        PdfCompressionLevel.High => 50,
        _ => 0,
    };

    private static CompressToTargetResult WriteOriginal(
        PdfDocument document, Stream output, long targetBytes, CompressionSkipReason skip)
    {
        using MemoryStream buffer = new MemoryStream();
        document.Save(buffer);
        byte[] original = buffer.ToArray();
        output.Write(original, 0, original.Length);
        return new CompressToTargetResult
        {
            FinalSize = original.Length,
            QualityUsed = 0,
            TargetMet = original.Length <= targetBytes,
            SkipReason = skip,
        };
    }
}
