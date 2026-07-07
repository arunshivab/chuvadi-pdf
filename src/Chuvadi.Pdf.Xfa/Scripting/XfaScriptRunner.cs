// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  XFA 3.3 — event processing (initialize / calculate / validate).
// PHASE: LA-23b Phase E — script runner.

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Xfa.Model;

namespace Chuvadi.Pdf.Xfa.Scripting;

/// <summary>
/// The result of running validate scripts: the nodes whose validation failed.
/// </summary>
public sealed class XfaValidationResult
{
    private readonly List<XfaNode> _failures = new List<XfaNode>();

    /// <summary>Gets the nodes whose validate script returned a falsy result.</summary>
    public IReadOnlyList<XfaNode> Failures => _failures;

    internal void AddFailure(XfaNode node) => _failures.Add(node);
}

/// <summary>
/// Runs the scripts attached to a template's nodes for a given event. Each
/// script executes in the context of its owning node. Any script that fails to
/// parse or evaluate is skipped (fail-soft) so a single bad script never aborts
/// rendering. Interactive events are never fired here — a static render has no
/// event source — but their scripts remain attached for hosts that can drive them.
/// </summary>
public static class XfaScriptRunner
{
    /// <summary>
    /// Runs all <c>initialize</c> scripts across the tree, in document order.
    /// Each script's writes (typically <c>this.rawValue = ...</c>) mutate the
    /// model so later layout and rendering observe the computed values.
    /// </summary>
    /// <param name="root">The template root.</param>
    /// <param name="host">The scripting host bound to the same root.</param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public static void RunInitialize(XfaNode root, XfaScriptHost host)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(host);
        RunEvent(root, host, XfaScriptEvent.Initialize);
    }

    /// <summary>Runs all <c>calculate</c> scripts across the tree, in document order.</summary>
    /// <param name="root">The template root.</param>
    /// <param name="host">The scripting host bound to the same root.</param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public static void RunCalculate(XfaNode root, XfaScriptHost host)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(host);
        RunEvent(root, host, XfaScriptEvent.Calculate);
    }

    /// <summary>
    /// Runs all <c>validate</c> scripts across the tree. A script whose result
    /// coerces to false marks its owning node as a validation failure.
    /// </summary>
    /// <param name="root">The template root.</param>
    /// <param name="host">The scripting host bound to the same root.</param>
    /// <returns>The validation result.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public static XfaValidationResult RunValidate(XfaNode root, XfaScriptHost host)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(host);

        XfaValidationResult result = new XfaValidationResult();
        Walk(root, node =>
        {
            foreach (XfaScript script in node.Scripts)
            {
                if (script.Event != XfaScriptEvent.Validate)
                {
                    continue;
                }

                if (!RunValidateScript(script, node, host))
                {
                    result.AddFailure(node);
                }
            }
        });

        return result;
    }

    private static void RunEvent(XfaNode root, XfaScriptHost host, XfaScriptEvent activity)
    {
        Walk(root, node =>
        {
            foreach (XfaScript script in node.Scripts)
            {
                if (script.Event == activity)
                {
                    RunOne(script, node, host);
                }
            }
        });
    }

    private static void RunOne(XfaScript script, XfaNode node, XfaScriptHost host)
    {
        try
        {
            if (script.Language == XfaScriptLanguage.JavaScript)
            {
                new XfaJavaScriptEngine(host).Execute(script.Source, node);
            }
            else
            {
                new XfaFormCalcEngine(host).Execute(script.Source, node);
            }
        }
        catch (XfaScriptException)
        {
            // Fail soft: skip this script, leave form state untouched.
        }
    }

    private static bool RunValidateScript(XfaScript script, XfaNode node, XfaScriptHost host)
    {
        try
        {
            if (script.Language == XfaScriptLanguage.JavaScript)
            {
                // The JS engine executes for side effects; a validate script that
                // needs a boolean should assign it, but absent a return channel we
                // treat successful execution as a pass.
                new XfaJavaScriptEngine(host).Execute(script.Source, node);
                return true;
            }

            string result = new XfaFormCalcEngine(host).Execute(script.Source, node);
            return CoerceBoolean(result);
        }
        catch (XfaScriptException)
        {
            // A script that cannot run does not fail validation.
            return true;
        }
    }

    private static bool CoerceBoolean(string result)
    {
        if (string.IsNullOrEmpty(result))
        {
            return false;
        }

        if (double.TryParse(result, out double number))
        {
            return number != 0.0;
        }

        return !string.Equals(result, "false", StringComparison.OrdinalIgnoreCase);
    }

    private static void Walk(XfaNode node, Action<XfaNode> visit)
    {
        visit(node);
        foreach (XfaNode child in node.Children)
        {
            Walk(child, visit);
        }
    }
}
