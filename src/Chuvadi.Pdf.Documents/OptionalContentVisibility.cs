// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.11.2 (optional content groups and membership
//        dictionaries, visibility expressions), §8.11.4.3 (default
//        configuration: /BaseState, /ON, /OFF, /AS usage application)
// PHASE: 3.21 — layer visibility honoured when rendering and extracting.

using System;
using System.Collections.Generic;
using System.Text;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Documents;

/// <summary>
/// Decides whether optional content (layers) is visible: the document's
/// default configuration (<c>/OCProperties /D</c>: <c>/BaseState</c>,
/// <c>/ON</c>, <c>/OFF</c>, and the <c>/AS</c> auto-state for viewing),
/// optionally overridden per layer name. Renderers and text extraction use it
/// to skip content in hidden layers, as Acrobat does.
/// </summary>
/// <remarks>
/// Content is tied to a layer by a <c>BDC /OC</c> marked-content sequence or
/// by an <c>/OC</c> entry on a form XObject, image XObject or annotation. The
/// entry is an optional content group (OCG) or a membership dictionary (OCMD)
/// combining several groups through <c>/P</c> (<c>AnyOn</c> — the default —
/// <c>AllOn</c>, <c>AnyOff</c>, <c>AllOff</c>) or a <c>/VE</c> visibility
/// expression (<c>And</c>, <c>Or</c>, <c>Not</c>). Anything that cannot be
/// resolved is treated as visible, as the specification requires.
/// </remarks>
public sealed class OptionalContentVisibility
{
    private const int MaxExpressionDepth = 32;

    private readonly PdfObjectStore? _store;
    private readonly Dictionary<int, bool> _groupStates;
    private readonly Dictionary<int, string> _groupNames;
    private readonly IReadOnlyDictionary<string, bool>? _overrides;
    private readonly bool _all;

    private OptionalContentVisibility(
        PdfObjectStore? store,
        Dictionary<int, bool> groupStates,
        Dictionary<int, string> groupNames,
        IReadOnlyDictionary<string, bool>? overrides,
        bool all)
    {
        _store = store;
        _groupStates = groupStates;
        _groupNames = groupNames;
        _overrides = overrides;
        _all = all;
    }

    /// <summary>Gets a visibility that shows every layer (optional content ignored).</summary>
    public static OptionalContentVisibility AllVisible { get; } = new OptionalContentVisibility(
        null, new Dictionary<int, bool>(), new Dictionary<int, string>(), null, all: true);

    /// <summary>Gets whether this visibility shows every layer regardless of state.</summary>
    public bool ShowsEverything => _all;

    /// <summary>
    /// Builds the visibility of <paramref name="document"/>'s default
    /// configuration, optionally overriding layers by name.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="layerVisibility">
    /// Layer name → visible. Applies to every group with that name; layers not
    /// listed keep their default-configuration state. May be null.
    /// </param>
    /// <returns>The visibility.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="document"/> is null.</exception>
    public static OptionalContentVisibility FromDocument(
        PdfDocument document, IReadOnlyDictionary<string, bool>? layerVisibility = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        return FromCatalog(document.Catalog, document.Objects).WithOverrides(layerVisibility);
    }

    /// <summary>
    /// Returns a copy with layers overridden by name (null or empty keeps this
    /// visibility). Overrides replace any earlier ones.
    /// </summary>
    /// <param name="layerVisibility">Layer name → visible.</param>
    /// <returns>The visibility with the overrides applied.</returns>
    public OptionalContentVisibility WithOverrides(IReadOnlyDictionary<string, bool>? layerVisibility)
    {
        if (_all || layerVisibility is null || layerVisibility.Count == 0)
        {
            return this;
        }

        return new OptionalContentVisibility(_store, _groupStates, _groupNames, layerVisibility, all: false);
    }

    /// <summary>
    /// Returns whether the layer named <paramref name="name"/> is visible
    /// (true when no group has that name).
    /// </summary>
    /// <param name="name">The layer name.</param>
    /// <returns>Whether the layer is visible.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="name"/> is null.</exception>
    public bool IsLayerVisible(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_all)
        {
            return true;
        }

        if (_overrides is not null && _overrides.TryGetValue(name, out bool forced))
        {
            return forced;
        }

        bool found = false;
        foreach (KeyValuePair<int, string> entry in _groupNames)
        {
            if (entry.Value == name)
            {
                found = true;
                if (GroupState(entry.Key))
                {
                    return true;
                }
            }
        }

        return !found;
    }

    /// <summary>
    /// Returns whether content tagged with <paramref name="optionalContent"/>
    /// (an OCG or OCMD, usually an indirect reference; the value of a
    /// <c>/OC</c> entry or of a <c>BDC /OC</c> property) is visible.
    /// </summary>
    /// <param name="optionalContent">The optional content entry; null is visible.</param>
    /// <returns>Whether the content is visible.</returns>
    public bool IsVisible(PdfPrimitive? optionalContent)
    {
        if (_all || optionalContent is null || _store is null)
        {
            return true;
        }

        return Evaluate(optionalContent, 0);
    }

    internal static OptionalContentVisibility FromCatalog(PdfDictionary catalog, PdfObjectStore store)
    {
        Dictionary<int, bool> states = new Dictionary<int, bool>();
        Dictionary<int, string> names = new Dictionary<int, string>();

        if (!catalog.TryGetValue(PdfName.Intern("OCProperties"), out PdfPrimitive? ocPrim)
            || store.ResolveAs<PdfDictionary>(ocPrim) is not PdfDictionary ocProps
            || !ocProps.TryGetValue(PdfName.Intern("OCGs"), out PdfPrimitive? ocgsPrim)
            || store.ResolveAs<PdfArray>(ocgsPrim) is not PdfArray ocgs)
        {
            return AllVisible;
        }

        PdfDictionary? config = ocProps.TryGetValue(PdfName.Intern("D"), out PdfPrimitive? dPrim)
            ? store.ResolveAs<PdfDictionary>(dPrim)
            : null;
        bool baseOn = config?.GetName(PdfName.Intern("BaseState"))?.Value != "OFF";

        for (int i = 0; i < ocgs.Count; i++)
        {
            if (ocgs[i] is PdfReference r && store.ResolveAs<PdfDictionary>(r) is PdfDictionary ocg)
            {
                states[r.ObjectId.ObjectNumber] = baseOn;
                names[r.ObjectId.ObjectNumber] = NameOf(ocg) ?? string.Empty;
            }
        }

        if (config is not null)
        {
            ApplyList(config, "OFF", false, store, states);
            ApplyList(config, "ON", true, store, states);
            ApplyViewAutoState(config, store, states);
        }

        return new OptionalContentVisibility(store, states, names, null, all: false);
    }

    // /AS: usage application dictionaries; for the View event, groups whose
    // /Usage /View /ViewState names a state take that state (§8.11.4.4).
    private static void ApplyViewAutoState(PdfDictionary config, PdfObjectStore store, Dictionary<int, bool> states)
    {
        if (!config.TryGetValue(PdfName.Intern("AS"), out PdfPrimitive? asPrim)
            || store.ResolveAs<PdfArray>(asPrim) is not PdfArray autoStates)
        {
            return;
        }

        for (int i = 0; i < autoStates.Count; i++)
        {
            if (store.ResolveAs<PdfDictionary>(autoStates[i]) is not PdfDictionary usage
                || usage.GetName(PdfName.Intern("Event"))?.Value != "View"
                || !HasCategory(usage, "View", store)
                || !usage.TryGetValue(PdfName.Intern("OCGs"), out PdfPrimitive? groupsPrim)
                || store.ResolveAs<PdfArray>(groupsPrim) is not PdfArray groups)
            {
                continue;
            }

            for (int g = 0; g < groups.Count; g++)
            {
                if (groups[g] is not PdfReference r
                    || store.ResolveAs<PdfDictionary>(r) is not PdfDictionary ocg
                    || !ocg.TryGetValue(PdfName.Intern("Usage"), out PdfPrimitive? usagePrim)
                    || store.ResolveAs<PdfDictionary>(usagePrim) is not PdfDictionary ocgUsage
                    || !ocgUsage.TryGetValue(PdfName.Intern("View"), out PdfPrimitive? viewPrim)
                    || store.ResolveAs<PdfDictionary>(viewPrim) is not PdfDictionary view)
                {
                    continue;
                }

                string? state = view.GetName(PdfName.Intern("ViewState"))?.Value;
                if (state == "ON")
                {
                    states[r.ObjectId.ObjectNumber] = true;
                }
                else if (state == "OFF")
                {
                    states[r.ObjectId.ObjectNumber] = false;
                }
            }
        }
    }

    private static bool HasCategory(PdfDictionary usage, string category, PdfObjectStore store)
    {
        if (!usage.TryGetValue(PdfName.Intern("Category"), out PdfPrimitive? catPrim))
        {
            return false;
        }

        PdfPrimitive resolved = store.Resolve(catPrim);
        if (resolved is PdfName single)
        {
            return single.Value == category;
        }

        if (resolved is PdfArray array)
        {
            for (int i = 0; i < array.Count; i++)
            {
                if (store.Resolve(array[i]) is PdfName name && name.Value == category)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void ApplyList(
        PdfDictionary config, string key, bool state, PdfObjectStore store, Dictionary<int, bool> states)
    {
        if (!config.TryGetValue(PdfName.Intern(key), out PdfPrimitive? prim)
            || store.ResolveAs<PdfArray>(prim) is not PdfArray array)
        {
            return;
        }

        for (int i = 0; i < array.Count; i++)
        {
            if (array[i] is PdfReference r)
            {
                states[r.ObjectId.ObjectNumber] = state;
            }
        }
    }

    private bool Evaluate(PdfPrimitive entry, int depth)
    {
        if (depth > MaxExpressionDepth)
        {
            return true;
        }

        int objectNumber = entry is PdfReference reference ? reference.ObjectId.ObjectNumber : -1;
        PdfDictionary? dict = _store!.ResolveAs<PdfDictionary>(entry);
        if (dict is null)
        {
            return true;
        }

        string? type = dict.GetName(PdfName.Type)?.Value;
        if (type == "OCMD")
        {
            return EvaluateMembership(dict, depth);
        }

        // An OCG (typed or not): its state, by reference when known.
        return objectNumber >= 0 ? GroupState(objectNumber) : NamedState(NameOf(dict));
    }

    private bool EvaluateMembership(PdfDictionary ocmd, int depth)
    {
        if (ocmd.TryGetValue(PdfName.Intern("VE"), out PdfPrimitive? ve)
            && _store!.ResolveAs<PdfArray>(ve) is PdfArray expression)
        {
            return EvaluateExpression(expression, depth + 1);
        }

        List<bool> members = new List<bool>();
        if (ocmd.TryGetValue(PdfName.Intern("OCGs"), out PdfPrimitive? ocgsPrim))
        {
            PdfPrimitive resolved = _store!.Resolve(ocgsPrim);
            if (resolved is PdfArray array)
            {
                for (int i = 0; i < array.Count; i++)
                {
                    if (array[i] is not PdfNull)
                    {
                        members.Add(Evaluate(array[i], depth + 1));
                    }
                }
            }
            else if (resolved is PdfDictionary)
            {
                members.Add(Evaluate(ocgsPrim, depth + 1));
            }
        }

        if (members.Count == 0)
        {
            return true;
        }

        string policy = ocmd.GetName(PdfName.Intern("P"))?.Value ?? "AnyOn";
        return policy switch
        {
            "AllOn" => members.TrueForAll(v => v),
            "AnyOff" => members.Exists(v => !v),
            "AllOff" => members.TrueForAll(v => !v),
            _ => members.Exists(v => v),
        };
    }

    // [/And e1 e2 …], [/Or e1 e2 …], [/Not e]; operands are OCG references
    // or nested expressions (§8.11.2.3).
    private bool EvaluateExpression(PdfArray expression, int depth)
    {
        if (depth > MaxExpressionDepth || expression.Count == 0
            || _store!.Resolve(expression[0]) is not PdfName op)
        {
            return true;
        }

        List<bool> operands = new List<bool>();
        for (int i = 1; i < expression.Count; i++)
        {
            PdfPrimitive item = expression[i];
            operands.Add(_store.Resolve(item) is PdfArray nested
                ? EvaluateExpression(nested, depth + 1)
                : Evaluate(item, depth + 1));
        }

        return op.Value switch
        {
            "And" => operands.Count == 0 || operands.TrueForAll(v => v),
            "Or" => operands.Count == 0 || operands.Exists(v => v),
            "Not" => operands.Count == 0 || !operands[0],
            _ => true,
        };
    }

    private bool GroupState(int objectNumber)
    {
        if (_overrides is not null
            && _groupNames.TryGetValue(objectNumber, out string? name)
            && _overrides.TryGetValue(name, out bool forced))
        {
            return forced;
        }

        // A group the configuration does not list is visible.
        return !_groupStates.TryGetValue(objectNumber, out bool state) || state;
    }

    private bool NamedState(string? name)
    {
        if (name is not null && _overrides is not null && _overrides.TryGetValue(name, out bool forced))
        {
            return forced;
        }

        return true;
    }

    private static string? NameOf(PdfDictionary ocg)
    {
        if (ocg.TryGetValue(PdfName.Intern("Name"), out PdfPrimitive? namePrim) && namePrim is PdfString name)
        {
            return Encoding.Latin1.GetString(name.Bytes);
        }

        return null;
    }
}
