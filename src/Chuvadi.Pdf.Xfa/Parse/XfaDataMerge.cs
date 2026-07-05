// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  XFA 3.3 — data binding ("dataRef" SOM expressions).
// PHASE: LA-23b Phase C — datasets merge.

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Xfa.Model;

namespace Chuvadi.Pdf.Xfa.Parse;

/// <summary>
/// Merges values from the datasets packet into a parsed template tree by
/// resolving each field's <see cref="XfaField.DataRef"/> against the document's
/// extracted <see cref="XfaDataField"/> list.
/// </summary>
public static class XfaDataMerge
{
    /// <summary>
    /// Fills field values in <paramref name="root"/> from <paramref name="dataFields"/>.
    /// A field whose <see cref="XfaField.DataRef"/> resolves to a data value has its
    /// <see cref="XfaField.Value"/> text replaced by the merged value.
    /// </summary>
    /// <param name="root">The parsed template root to populate in place.</param>
    /// <param name="dataFields">The datasets fields extracted from the document.</param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public static void Apply(XfaNode root, IReadOnlyList<XfaDataField> dataFields)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(dataFields);

        Dictionary<string, string> byPath = BuildIndex(dataFields);
        MergeNode(root, byPath);
    }

    private static Dictionary<string, string> BuildIndex(IReadOnlyList<XfaDataField> dataFields)
    {
        Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (XfaDataField field in dataFields)
        {
            // NodePath is e.g. "data.ZMCA_NCA_INC29_STRUCT.COMPANY_NAME".
            // Index by the path with the leading "data." segment removed so it
            // aligns with bind refs of the form "$record.ZMCA_...COMPANY_NAME".
            string key = StripLeadingSegment(field.NodePath, "data");
            map[key] = field.Value ?? string.Empty;

            // Also index by the leaf name as a fallback for simple references.
            string leaf = LeafOf(field.NodePath);
            if (!map.ContainsKey(leaf))
            {
                map[leaf] = field.Value ?? string.Empty;
            }
        }

        return map;
    }

    private static void MergeNode(XfaNode node, Dictionary<string, string> byPath)
    {
        if (node is XfaField field && field.DataRef is { Length: > 0 } dataRef)
        {
            string key = NormalizeRef(dataRef);
            if (byPath.TryGetValue(key, out string? value)
                || byPath.TryGetValue(LeafOf(key), out value))
            {
                field.Value ??= new XfaValue();
                field.Value.Text = value;
            }
        }

        foreach (XfaNode child in node.Children)
        {
            MergeNode(child, byPath);
        }
    }

    // Converts a bind ref such as "$record.A.B.C" or "$.A.B" into a dotted path
    // "A.B.C" aligned with the data index keys.
    private static string NormalizeRef(string dataRef)
    {
        string r = dataRef.Trim();
        if (r.StartsWith("$record.", StringComparison.Ordinal))
        {
            return r.Substring("$record.".Length);
        }

        if (r.StartsWith("$data.", StringComparison.Ordinal))
        {
            return r.Substring("$data.".Length);
        }

        if (r.StartsWith("$.", StringComparison.Ordinal))
        {
            return r.Substring("$.".Length);
        }

        if (r.StartsWith("$record", StringComparison.Ordinal))
        {
            return r.Substring("$record".Length).TrimStart('.');
        }

        return r;
    }

    private static string StripLeadingSegment(string path, string segment)
    {
        string prefix = segment + ".";
        return path.StartsWith(prefix, StringComparison.Ordinal)
            ? path.Substring(prefix.Length)
            : path;
    }

    private static string LeafOf(string path)
    {
        int dot = path.LastIndexOf('.');
        return dot >= 0 ? path.Substring(dot + 1) : path;
    }
}
