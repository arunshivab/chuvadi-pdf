// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.7.8 (XFA Forms)
//        XFA 3.3 §A.2 — datasets packet (<xfa:data>)
// PHASE: Document introspection — XFA data layer.

namespace Chuvadi.Pdf.Documents;

/// <summary>
/// A single value drawn from the XFA <c>datasets</c> packet's data layer
/// (<c>&lt;xfa:data&gt;</c>): the data element's path, its text value, and a
/// best-effort widget geometry the host can use to overlay the value onto the
/// rendered template. XFA 3.3 §A.2.
/// </summary>
public sealed class XfaDataField
{
    internal XfaDataField(string path, string? value, XfaGeometry? geometry)
    {
        NodePath = path;
        Value = value;
        Geometry = geometry;
    }

    /// <summary>
    /// Gets the dotted element path beneath <c>&lt;xfa:data&gt;</c>, built from
    /// the data elements' local names — for example
    /// <c>"data.ZMCA_NCA_INC29_STRUCT.CIN"</c>.
    /// </summary>
    public string NodePath { get; }

    /// <summary>
    /// Gets the leaf element's text value. Empty string for an element present
    /// but empty (for example <c>&lt;CIN/&gt;</c>); null is not produced by the
    /// datasets walker but the type permits it for callers that synthesise
    /// fields.
    /// </summary>
    public string? Value { get; }

    /// <summary>
    /// Gets best-effort widget geometry for overlaying the value, or null when
    /// no AcroForm widget matched this field's name. See <see cref="XfaGeometry"/>.
    /// </summary>
    public XfaGeometry? Geometry { get; }
}
