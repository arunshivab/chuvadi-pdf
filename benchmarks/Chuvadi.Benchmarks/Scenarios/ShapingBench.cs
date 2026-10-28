// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using BenchmarkDotNet.Attributes;
using Chuvadi.Pdf.Fonts.Rendering;
using Chuvadi.Pdf.Fonts.Woff2;
using Chuvadi.Pdf.Text.Shaping;

namespace Chuvadi.Benchmarks.Scenarios;

/// <summary>
/// Shaping inside a line-fitting loop: the cost of shaping one line with the
/// font bytes (the font is parsed on every call) against shaping with a loaded
/// <see cref="FontFace"/> (parsed once, feature lookups cached), plus the plain
/// advance-sum measure and the one-off face load. Uses the LiPi Sans fonts
/// shipped in <c>assets/fonts/lipi-sans</c>. Run with
/// <c>dotnet run -c Release -- --filter *Shaping*</c>.
/// </summary>
[MemoryDiagnoser]
public class ShapingBench
{
    private byte[] _font = Array.Empty<byte>();
    private FontFace? _face;
    private string _text = string.Empty;
    private LipiScript _script;

    /// <summary>Gets or sets the LiPi Sans font and sample line to shape.</summary>
    [Params("Latin", "Tamil", "Devanagari")]
    public string Script { get; set; } = "Latin";

    [GlobalSetup]
    public void Setup()
    {
        (_script, _text) = Script switch
        {
            "Tamil" => (LipiScript.Tamil, "தமிழ் நாட்டின் தலைநகரம் சென்னை ஆகும்"),
            "Devanagari" => (LipiScript.Devanagari, "हिन्दी भारत की राजभाषा है और क्षेत्र"),
            _ => (LipiScript.Latin, "The quick brown fox jumps over the lazy dog, office ffi."),
        };
        _font = Woff2Unpacker.Unpack(File.ReadAllBytes(Path.Combine(FindFontDir(), $"LiPi-Sans-{Script}.woff2")));
        _face = FontFace.Load(_font);
    }

    [Benchmark(Baseline = true, Description = "Shape(bytes) - parses the font each call")]
    public IReadOnlyList<ShapedGlyph> ShapeBytes() => TextShaper.Shape(_font, _text, _script);

    [Benchmark(Description = "Shape(FontFace) - loaded once, lookups cached")]
    public IReadOnlyList<ShapedGlyph> ShapeFace() => TextShaper.Shape(_face!, _text, _script);

    [Benchmark(Description = "FontFace.MeasureText - advance sum, no shaping")]
    public int Measure() => _face!.MeasureText(_text);

    [Benchmark(Description = "FontFace.Load - one-off per font")]
    public FontFace Load() => FontFace.Load(_font);

    private static string FindFontDir()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "assets", "fonts", "lipi-sans", "fonts");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("assets/fonts/lipi-sans/fonts not found above " + AppContext.BaseDirectory);
    }
}
