// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.31 (D-172) — the Adobe Glyph List now lives in Chuvadi.Pdf.Fonts,
//        keeping this namespace, so simple-font encodings can resolve every
//        glyph name of a /Differences array. The forwards keep code compiled
//        against 3.30 and earlier working.

using System.Runtime.CompilerServices;
using Chuvadi.Pdf.Fonts.Rendering;

[assembly: TypeForwardedTo(typeof(AdobeGlyphList))]
[assembly: TypeForwardedTo(typeof(GlyphNameToUnicode))]
