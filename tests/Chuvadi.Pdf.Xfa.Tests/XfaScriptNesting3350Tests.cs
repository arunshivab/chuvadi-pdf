// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.35.0 (XFA-01) — a script nested too deeply fails as an ordinary
//        script error instead of overflowing the stack.

using System;
using Chuvadi.Pdf.Xfa.Model;
using Chuvadi.Pdf.Xfa.Scripting;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Xfa.Tests;

public sealed class XfaScriptNesting3350Tests
{
    [Theory]
    [InlineData(10_000)]
    [InlineData(101)]
    public void JavaScript_DeepBrackets_FailAsAScriptError(int depth)
    {
        XfaScriptHost host = new XfaScriptHost(new XfaSubform { Name = "form1" });
        string script = "var x = " + new string('(', depth) + "1" + new string(')', depth) + ";";

        Action run = () => new XfaJavaScriptEngine(host).Execute(script, null);

        run.Should().Throw<XfaScriptException>().WithMessage("*nested*");
    }

    [Theory]
    [InlineData(10_000)]
    [InlineData(101)]
    public void FormCalc_DeepBrackets_FailAsAScriptError(int depth)
    {
        XfaScriptHost host = new XfaScriptHost(new XfaSubform { Name = "form1" });
        string script = new string('(', depth) + "1" + new string(')', depth);

        Action run = () => new XfaFormCalcEngine(host).Execute(script, null);

        run.Should().Throw<XfaScriptException>().WithMessage("*nested*");
    }

    [Fact]
    public void JavaScript_LongUnaryChain_FailsAsAScriptError()
    {
        XfaScriptHost host = new XfaScriptHost(new XfaSubform { Name = "form1" });
        string script = "var x = " + new string('!', 10_000) + "1;";

        Action run = () => new XfaJavaScriptEngine(host).Execute(script, null);

        run.Should().Throw<XfaScriptException>();
    }

    [Fact]
    public void ModerateNesting_StillRuns()
    {
        XfaScriptHost host = new XfaScriptHost(new XfaSubform { Name = "form1" });

        string fc = new XfaFormCalcEngine(host).Execute(new string('(', 40) + "1 + 2" + new string(')', 40), null);
        Action js = () => new XfaJavaScriptEngine(host).Execute("var x = " + new string('(', 40) + "1" + new string(')', 40) + ";", null);

        fc.Should().Be("3");
        js.Should().NotThrow();
    }
}
