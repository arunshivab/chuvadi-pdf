// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  XFA 3.3 — Scripting Object Model (SOM) reference resolution.
// PHASE: LA-23b Phase E — scripting host.

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Xfa.Model;

namespace Chuvadi.Pdf.Xfa.Scripting;

/// <summary>
/// The host surface shared by the FormCalc and JavaScript engines. Resolves SOM
/// references (dotted node paths such as <c>Certificate.CompanyName</c> or
/// <c>data.Certificate.City</c>) against the template tree and reads or writes
/// node properties (<c>rawValue</c>, <c>value</c>, <c>presence</c>).
/// </summary>
public sealed class XfaScriptHost
{
    private readonly XfaNode _root;
    private readonly Dictionary<string, XfaNode> _byName =
        new Dictionary<string, XfaNode>(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="XfaScriptHost"/> class.</summary>
    /// <param name="root">The template root the scripts resolve references against.</param>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is null.</exception>
    public XfaScriptHost(XfaNode root)
    {
        ArgumentNullException.ThrowIfNull(root);
        _root = root;
        IndexNames(root);
    }

    /// <summary>
    /// Resolves a SOM reference to a node, or null when it cannot be resolved.
    /// Supports dotted paths, an optional leading <c>data.</c> / <c>xfa.</c> /
    /// <c>$record.</c> root, and a bare leaf name.
    /// </summary>
    /// <param name="reference">The SOM reference expression.</param>
    /// <param name="context">The node bound to <c>this</c>, for relative refs.</param>
    /// <returns>The resolved node, or null.</returns>
    public XfaNode? Resolve(string reference, XfaNode? context)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        string path = StripRoot(reference.Trim());
        if (path.Length == 0)
        {
            return context ?? _root;
        }

        string[] segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries);

        // Try a strict walk from the root, then from the context, then fall back
        // to a leaf-name lookup (forms often reference fields by short name).
        XfaNode? node = WalkFrom(_root, segments)
            ?? (context is not null ? WalkFrom(context, segments) : null)
            ?? WalkFromAnySubform(segments);

        if (node is not null)
        {
            return node;
        }

        string leaf = segments[^1];
        return _byName.TryGetValue(leaf, out XfaNode? byLeaf) ? byLeaf : null;
    }

    /// <summary>Reads a property of a node as a string.</summary>
    /// <param name="node">The node to read.</param>
    /// <param name="property">The property name (rawValue / value / text / presence / name).</param>
    /// <returns>The property value, or the empty string when unset.</returns>
    public static string GetProperty(XfaNode node, string property)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(property);

        switch (property)
        {
            case "rawValue":
            case "value":
            case "text":
                return ValueTextOf(node) ?? string.Empty;
            case "presence":
                return PresenceString(node.Presence);
            case "name":
                return node.Name ?? string.Empty;
            default:
                return string.Empty;
        }
    }

    /// <summary>Writes a property of a node.</summary>
    /// <param name="node">The node to modify.</param>
    /// <param name="property">The property name (rawValue / value / text / presence).</param>
    /// <param name="value">The new value.</param>
    public static void SetProperty(XfaNode node, string property, string value)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(property);

        switch (property)
        {
            case "rawValue":
            case "value":
            case "text":
                SetValueText(node, value);
                break;
            case "presence":
                node.Presence = ParsePresence(value);
                break;
            default:
                break;
        }
    }

    private static string? ValueTextOf(XfaNode node) => node switch
    {
        XfaField field => field.Value?.Text,
        XfaDraw draw => draw.Value?.Text,
        XfaValue value => value.Text,
        _ => null,
    };

    private static void SetValueText(XfaNode node, string value)
    {
        switch (node)
        {
            case XfaField field:
                field.Value ??= new XfaValue();
                field.Value.Text = value;
                break;
            case XfaDraw draw:
                draw.Value ??= new XfaValue();
                draw.Value.Text = value;
                break;
            case XfaValue valueNode:
                valueNode.Text = value;
                break;
            default:
                break;
        }
    }

    private static string PresenceString(XfaPresence presence) => presence switch
    {
        XfaPresence.Visible => "visible",
        XfaPresence.Hidden => "hidden",
        XfaPresence.Invisible => "invisible",
        XfaPresence.Inactive => "inactive",
        _ => "visible",
    };

    private static XfaPresence ParsePresence(string value) => value switch
    {
        "hidden" => XfaPresence.Hidden,
        "invisible" => XfaPresence.Invisible,
        "inactive" => XfaPresence.Inactive,
        _ => XfaPresence.Visible,
    };

    private static string StripRoot(string path)
    {
        foreach (string root in new[] { "xfa.form.", "xfa.record.", "xfa.", "$record.", "$data.", "$.", "data.", "form." })
        {
            if (path.StartsWith(root, StringComparison.Ordinal))
            {
                return path.Substring(root.Length);
            }
        }

        // A bare "data" / "$record" with no trailing segment resolves to root.
        if (path is "data" or "$record" or "xfa" or "form")
        {
            return string.Empty;
        }

        return path;
    }

    private static XfaNode? WalkFrom(XfaNode start, string[] segments)
    {
        XfaNode current = start;
        foreach (string segment in segments)
        {
            XfaNode? next = ChildByName(current, segment);
            if (next is null)
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    // Some references omit the top container name; try resolving from any
    // subform directly under the root before giving up.
    private XfaNode? WalkFromAnySubform(string[] segments)
    {
        foreach (XfaNode child in _root.Children)
        {
            if (child is XfaSubform)
            {
                XfaNode? found = WalkFrom(child, segments);
                if (found is not null)
                {
                    return found;
                }
            }
        }

        return null;
    }

    private static XfaNode? ChildByName(XfaNode node, string name)
    {
        foreach (XfaNode child in node.Children)
        {
            if (string.Equals(child.Name, name, StringComparison.Ordinal))
            {
                return child;
            }
        }

        return null;
    }

    private void IndexNames(XfaNode node)
    {
        if (node.Name is { Length: > 0 } name && !_byName.ContainsKey(name))
        {
            _byName[name] = node;
        }

        foreach (XfaNode child in node.Children)
        {
            IndexNames(child);
        }
    }
}
