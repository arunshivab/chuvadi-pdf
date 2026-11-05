// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.31 — these value types now live in Chuvadi.Pdf.Rendering.Walking,
//        keeping this namespace (D-143). The forwards keep code compiled
//        against 3.30 and earlier working.

using System.Runtime.CompilerServices;
using Chuvadi.Pdf.Rendering.DisplayList;

[assembly: TypeForwardedTo(typeof(PdfBlendMode))]
[assembly: TypeForwardedTo(typeof(BlendModes))]
[assembly: TypeForwardedTo(typeof(Type3Font))]
[assembly: TypeForwardedTo(typeof(Type3Glyph))]
