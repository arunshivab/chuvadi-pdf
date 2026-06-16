// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §14.3.3 (document information), §14.3.2 (metadata streams)
// PHASE: Phase 1 — Chuvadi.Pdf.IO

using System;

namespace Chuvadi.Pdf.IO;

/// <summary>
/// Selects which document-level metadata <see cref="PdfWriter"/> synthesises when
/// it is absent from the objects being written. The file identifier (/ID) is
/// always written, because it carries no document content and keeps output
/// deterministic and viewer-friendly. The default is <see cref="All"/>, which
/// preserves the writer's standard behaviour; pass a reduced set to suppress
/// synthesis (for example when deliberately stripping metadata). Metadata that is
/// already present in the written objects is never altered — these flags govern
/// synthesis of absent entries only.
/// </summary>
[Flags]
public enum SynthesizedMetadata
{
    /// <summary>Synthesise neither the information dictionary nor the XMP packet.</summary>
    None = 0,

    /// <summary>Synthesise a generic document information dictionary (/Info) when absent.</summary>
    Info = 1,

    /// <summary>Synthesise an XMP metadata packet (/Metadata on the catalog) when absent.</summary>
    Metadata = 2,

    /// <summary>Synthesise both the information dictionary and the XMP packet. This is the default.</summary>
    All = Info | Metadata,
}
