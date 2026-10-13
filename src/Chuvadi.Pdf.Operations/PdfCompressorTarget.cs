// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.4 (filters); §7.6 (encryption guard).
// LA-27 — fold the descending-quality compression loop into one call.

using System;
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
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(output);
        options ??= new CompressToTargetOptions();

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
    private static byte[] BuildRasterPdf(PdfDocument document, double dpi, int quality)
    {
        PageRasterizer rasterizer = new PageRasterizer(
            document.Objects,
            new RenderOptions
            {
                Dpi = dpi,
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
