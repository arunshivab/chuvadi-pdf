// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.4 (filters); §7.6 (encryption guard).
// LA-27 — fold the descending-quality compression loop into one call.

using System;
using System.IO;
using Chuvadi.Pdf.Documents;

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

        // No quality met the target: write the smallest achievable (min quality).
        (byte[] smallest, CompressionSkipReason minSkip) = CompressAt(document, options, options.MinQuality);
        if (minSkip != CompressionSkipReason.None)
        {
            return WriteOriginal(document, output, targetBytes, minSkip);
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
