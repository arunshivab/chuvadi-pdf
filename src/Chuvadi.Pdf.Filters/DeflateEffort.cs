// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.4.4 — FlateDecode filter
//
// Selects how hard the FlateDecode encoder works to minimise output size.

namespace Chuvadi.Pdf.Filters;

/// <summary>
/// Effort level for FlateDecode (DEFLATE) compression.
/// </summary>
public enum DeflateEffort
{
    /// <summary>
    /// Fast path: a single greedy LZ77 parse emitted with whichever of the
    /// stored, fixed-Huffman, or dynamic-Huffman encodings is smallest.
    /// </summary>
    Default = 0,

    /// <summary>
    /// Maximum effort: in addition to the <see cref="Default"/> candidates, also
    /// tries the runtime (BCL) deflater and an iterated optimal-parse
    /// ("zopfli-style") encoding, keeping the smallest result. Slower, but yields
    /// the best lossless ratio. Output stays a valid zlib/DEFLATE stream.
    /// </summary>
    Maximum = 1,
}
