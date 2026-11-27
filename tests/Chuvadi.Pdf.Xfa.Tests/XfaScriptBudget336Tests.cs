// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.36.0 (D-410 A, XFA-01) — when scripts run, a per-render budget
//        bounds them: a step budget stops a runaway loop and a string cap
//        stops runaway concatenation, each raising XfaScriptException so the
//        runner fails soft. These tests only terminate because the budget
//        fires; without it the loops would never end.

using System;
using Chuvadi.Pdf.Xfa.Model;
using Chuvadi.Pdf.Xfa.Scripting;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Xfa.Tests;

public sealed class XfaScriptBudget336Tests
{
    [Fact]
    public void JavaScript_RunawayLoop_StopsAtTheStepBudget()
    {
        (XfaScriptHost host, XfaNode node) = NewHost();

        Action run = () => new XfaJavaScriptEngine(host).Execute("while (true) { var x = 1; }", node);

        run.Should().Throw<XfaScriptException>();
    }

    [Fact]
    public void FormCalc_RunawayLoop_StopsAtTheStepBudget()
    {
        (XfaScriptHost host, XfaNode node) = NewHost();

        Action run = () => new XfaFormCalcEngine(host).Execute("for i = 1 upto 1000000000 do 1 + 1 endfor", node);

        run.Should().Throw<XfaScriptException>();
    }

    [Fact]
    public void JavaScript_DoublingString_StopsAtTheStringCap()
    {
        (XfaScriptHost host, XfaNode node) = NewHost();

        Action run = () => new XfaJavaScriptEngine(host).Execute("var s = \"a\"; while (true) { s = s + s; }", node);

        run.Should().Throw<XfaScriptException>();
    }

    private static (XfaScriptHost Host, XfaNode Node) NewHost()
    {
        XfaSubform root = new XfaSubform { Name = "form1" };
        XfaField field = new XfaField { Name = "F" };
        field.Value = new XfaValue { Text = string.Empty };
        root.AddChild(field);
        return (new XfaScriptHost(root), field);
    }
}
