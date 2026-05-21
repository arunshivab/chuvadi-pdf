// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: v2.0.0 R2 — SVG renderer

using System;

namespace Chuvadi.Pdf.Svg;

/// <summary>
/// Options controlling the SVG output produced by <see cref="SvgRenderer"/>.
/// </summary>
/// <remarks>
/// <para>
/// All options have safe defaults. The defaults produce a deterministic,
/// glyph-outline SVG with no embedded fonts at <see cref="Scale"/> 1.0
/// (one SVG user unit per PDF point, 1/72 inch).
/// </para>
/// </remarks>
public sealed class SvgRenderOptions
{
    /// <summary>The default options.</summary>
    public static SvgRenderOptions Default { get; } = new SvgRenderOptions();

    /// <summary>Initialises an <see cref="SvgRenderOptions"/> with default values.</summary>
    public SvgRenderOptions()
    {
        Scale = 1.0;
        FontEmbedding = FontEmbedding.GlyphPaths;
        IncludeTextLayer = false;
        DeterministicOutput = true;
        DecimalPrecision = 4;
        IndentOutput = false;
    }

    /// <summary>
    /// Gets or initialises the multiplicative scale applied to the page in
    /// the output SVG.
    /// </summary>
    /// <remarks>
    /// The SVG <c>viewBox</c> is always set to the PDF user-space MediaBox
    /// dimensions; this property only changes the <c>width</c> and
    /// <c>height</c> attributes (e.g. setting <see cref="Scale"/> to 2 on
    /// an A4 page yields an SVG with width="1190" height="1684" and
    /// viewBox="0 0 595 842"). Default 1.0.
    /// </remarks>
    public double Scale { get; init; }

    /// <summary>
    /// Gets or initialises the font-embedding strategy. Default
    /// <see cref="Svg.FontEmbedding.GlyphPaths"/>.
    /// </summary>
    public FontEmbedding FontEmbedding { get; init; }

    /// <summary>
    /// Gets or initialises whether to include a transparent
    /// <c>&lt;text&gt;</c> layer above the glyph outlines so users can
    /// select text in the rendered SVG.
    /// </summary>
    /// <remarks>
    /// When <see cref="FontEmbedding"/> is
    /// <see cref="Svg.FontEmbedding.GlyphPaths"/>, enabling this layer
    /// requires a parallel text walk over the page content stream to
    /// recover Unicode and baseline information. The walker is shared
    /// with the R3 <c>TextRun</c> extraction infrastructure.
    /// Default false.
    /// </remarks>
    public bool IncludeTextLayer { get; init; }

    /// <summary>
    /// Gets or initialises whether to require byte-for-byte deterministic
    /// output across runs.
    /// </summary>
    /// <remarks>
    /// When true the renderer guarantees: invariant-culture float
    /// formatting, monotonic id allocation in walk order, no dictionary
    /// iteration. Default true.
    /// </remarks>
    public bool DeterministicOutput { get; init; }

    /// <summary>
    /// Gets or initialises the number of fractional digits for floating
    /// point coordinates in path data. Default 4.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when set to a value outside the range 0..12.
    /// </exception>
    public int DecimalPrecision
    {
        get => _decimalPrecision;
        init
        {
            if (value < 0 || value > 12)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(DecimalPrecision),
                    value,
                    "DecimalPrecision must be in the range 0..12.");
            }

            _decimalPrecision = value;
        }
    }

    private readonly int _decimalPrecision;

    /// <summary>
    /// Gets or initialises whether to insert newlines and indentation
    /// between SVG elements for readability.
    /// </summary>
    /// <remarks>
    /// Off by default — minified output is smaller and easier to compare
    /// in snapshot tests.
    /// </remarks>
    public bool IndentOutput { get; init; }
}
