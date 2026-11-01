// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.27 — warnings for fallbacks (D-62, D-63)

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Rendering.Raster;

// Sends fallback warnings to the document's receiver, once per kind and
// subject for one page build (a damaged font used by every line is reported
// once, not per glyph).
internal sealed class WarningReporter
{
    private readonly Action<PdfWarning>? _receiver;
    private readonly HashSet<(PdfWarningKind, string)> _reported = new HashSet<(PdfWarningKind, string)>();

    internal WarningReporter(Action<PdfWarning>? receiver)
    {
        _receiver = receiver;
    }

    // The 1-based page being built, or null for a content stream built on its own.
    internal int? PageNumber { get; set; }

    internal void Report(PdfWarningKind kind, string subject, string reason)
    {
        if (_receiver is null || !_reported.Add((kind, subject)))
        {
            return;
        }

        _receiver(new PdfWarning(kind, PageNumber, subject, reason));
    }

    // "shading 12 0 R" for an indirect object, "shading (inline)" otherwise.
    internal static string Describe(string what, PdfPrimitive value) =>
        value is PdfReference r ? what + " " + r.ObjectNumber.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + r.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture) + " R" : what + " (direct)";
}
