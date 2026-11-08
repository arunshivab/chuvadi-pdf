// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.32.0 (D-218 PDF-09 P4, D-243, D-268, D-270) — named compression
//        levels and the image-level settings.

using System;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// A named compression level, lightest first (3.32.0). Used by
/// <see cref="PdfCompressor.Compress(Chuvadi.Pdf.Documents.PdfDocument, System.IO.Stream, PdfCompressionLevel, ImageLevelOptions?, CompressionOptions?)"/>
/// and by <see cref="CompressToTargetOptions.MaxLevel"/>.
/// </summary>
/// <remarks>
/// The first three keep the document as it is (text stays selectable). The
/// last three are image levels: every page becomes one image at 150 dpi by
/// default (see <see cref="ImageLevelOptions"/>), so text can no longer be
/// selected or searched, links and form fields are gone, and signatures and
/// encryption are dropped like in any rewrite.
/// </remarks>
public enum PdfCompressionLevel
{
    /// <summary>
    /// Streams re-compressed, identical objects merged, unused objects dropped.
    /// Pages look exactly the same; images are not touched.
    /// </summary>
    Lossless = 0,

    /// <summary>Lossless, plus images re-encoded as JPEG at quality 75 and reduced to 150 dpi. Colour is kept.</summary>
    Medium = 1,

    /// <summary>Lossless, plus images re-encoded as JPEG at quality 50 and reduced to 100 dpi. Colour is kept.</summary>
    High = 2,

    /// <summary>
    /// Image level: every page becomes one image of up to 16 colours (white,
    /// black and the page's 14 most used other colours). Dark greys (darker
    /// than about 63% grey) become black, so thin text stays dark. The closest
    /// image level to the original look; the largest of the three.
    /// </summary>
    ReducedColor = 3,

    /// <summary>
    /// Image level: clearly coloured marks keep their colour (up to 14 per
    /// page); everything else becomes black and white by the
    /// <see cref="BlackAndWhite"/> rules, so grey text stays sharp. Smaller
    /// than <see cref="ReducedColor"/>.
    /// </summary>
    BlackAndWhiteWithColor = 4,

    /// <summary>
    /// Image level: every page becomes one black-and-white (1-bit) image, the
    /// smallest output for line drawings. Colour is lost. Every pixel that is
    /// not near-white becomes black, so thin grey text stays readable; see
    /// <see cref="BlackAndWhiteFills"/> for large fills.
    /// </summary>
    BlackAndWhite = 5,
}

/// <summary>
/// How <see cref="PdfCompressionLevel.BlackAndWhite"/> and
/// <see cref="PdfCompressionLevel.BlackAndWhiteWithColor"/> draw large grey
/// fills (3.32.0). Lines and text are the same in both: every pixel that is
/// not near-white becomes black, so thin grey text stays readable.
/// </summary>
public enum BlackAndWhiteFills
{
    /// <summary>
    /// Large fills become a dot pattern, so they stay visible as fills and the
    /// marks drawn over them stay readable. The default. Slightly larger.
    /// </summary>
    DotPattern = 0,

    /// <summary>Large fills become solid black. Slightly smaller.</summary>
    Solid = 1,
}

/// <summary>
/// Settings for the image levels (<see cref="PdfCompressionLevel.ReducedColor"/>,
/// <see cref="PdfCompressionLevel.BlackAndWhiteWithColor"/> and
/// <see cref="PdfCompressionLevel.BlackAndWhite"/>), 3.32.0 (D-270). The
/// defaults suit most uses; change them only for a file just over a limit.
/// </summary>
public sealed record ImageLevelOptions
{
    /// <summary>
    /// The lowest resolution at which small text on large drawings stays
    /// readable (measured on A1 drawings, D-269): 130 dpi. Resolutions below
    /// it need <see cref="AllowBelowReadableResolution"/>.
    /// </summary>
    public const double ReadableDpi = 130;

    /// <summary>The lowest resolution accepted at all: 50 dpi.</summary>
    public const double MinimumDpi = 50;

    /// <summary>
    /// The highest resolution accepted: 300 dpi. An A1 page at 300 dpi needs
    /// about 280 MB of memory while it is drawn.
    /// </summary>
    public const double MaximumDpi = 300;

    /// <summary>
    /// The resolution of the page images. Default 150 dpi. With
    /// <see cref="PdfCompressor.Compress(Chuvadi.Pdf.Documents.PdfDocument, System.IO.Stream, PdfCompressionLevel, ImageLevelOptions?, CompressionOptions?)"/>
    /// it is the resolution used. With <see cref="PdfCompressor.CompressToTarget(Chuvadi.Pdf.Documents.PdfDocument, System.IO.Stream, long, CompressToTargetOptions?)"/>
    /// the image levels are tried at 150 dpi and then at 130 dpi, and this
    /// value is used only when it is below 130 dpi and
    /// <see cref="AllowBelowReadableResolution"/> is true: black and white
    /// then steps down (by 15% each time) to it.
    /// </summary>
    public double Dpi { get; init; } = 150;

    /// <summary>How large grey fills are drawn. Default <see cref="BlackAndWhiteFills.DotPattern"/>.</summary>
    public BlackAndWhiteFills Fills { get; init; }

    /// <summary>
    /// Allows a resolution below <see cref="ReadableDpi"/>. Default false: a
    /// lower <see cref="Dpi"/> is rejected. When an output below it is
    /// written, the result says so (<see cref="CompressionResult.BelowReadableResolution"/>,
    /// <see cref="CompressToTargetResult.BelowReadableResolution"/>) so an
    /// app can warn that small text may be hard to read.
    /// </summary>
    public bool AllowBelowReadableResolution { get; init; }

    /// <summary>Checks the settings; throws for a resolution out of range or below the readable floor without leave.</summary>
    internal void Validate(string paramName)
    {
        if (double.IsNaN(Dpi) || Dpi < MinimumDpi || Dpi > MaximumDpi)
        {
            throw new ArgumentOutOfRangeException(paramName, Dpi, "ImageLevelOptions.Dpi must be between 50 and 300.");
        }

        if (Dpi < ReadableDpi && !AllowBelowReadableResolution)
        {
            throw new ArgumentOutOfRangeException(
                paramName, Dpi, "ImageLevelOptions.Dpi is below 130, the readable resolution; set AllowBelowReadableResolution to use it.");
        }

        if (!Enum.IsDefined(Fills))
        {
            throw new ArgumentOutOfRangeException(paramName, Fills, "Unknown ImageLevelOptions.Fills.");
        }
    }
}
