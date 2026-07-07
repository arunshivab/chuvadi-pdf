// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Xfa.Model;
using Chuvadi.Pdf.Xfa.Parse;
using Chuvadi.Pdf.Xfa.Render;
using Chuvadi.Pdf.Xfa.Scripting;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Xfa.Tests;

public sealed class XfaScriptingTests
{
    private static readonly string FixturesDir =
        Path.Combine(System.AppContext.BaseDirectory, "Fixtures");

    // ── Event parsing ──────────────────────────────────────────────────────────

    [Fact]
    public void Parse_CapturesEventScriptsWithLanguageAndActivity()
    {
        XfaSubform root = LoadTemplate("synthetic-script-js.xml");

        XfaField certified = FindField(root, "Certified")!;
        certified.Scripts.Should().HaveCount(1);
        certified.Scripts[0].Language.Should().Be(XfaScriptLanguage.JavaScript);
        certified.Scripts[0].Event.Should().Be(XfaScriptEvent.Initialize);
    }

    [Fact]
    public void Parse_DefaultContentTypeIsFormCalc()
    {
        XfaSubform root = LoadTemplate("synthetic-script-formcalc.xml");

        XfaField fullName = FindField(root, "FullName")!;
        fullName.Scripts.Should().HaveCount(1);
        fullName.Scripts[0].Language.Should().Be(XfaScriptLanguage.FormCalc);
    }

    // ── SOM host resolution ────────────────────────────────────────────────────

    [Fact]
    public void Host_ResolvesDottedPathAndReadsRawValue()
    {
        XfaSubform root = BuildTree(("Company", "ACME"), ("City", "Springfield"));
        XfaScriptHost host = new XfaScriptHost(root);

        XfaNode? company = host.Resolve("Company", null);
        company.Should().NotBeNull();
        XfaScriptHost.GetProperty(company!, "rawValue").Should().Be("ACME");
    }

    [Fact]
    public void Host_ResolvesDataRootPrefix()
    {
        XfaSubform root = BuildTree(("City", "Metropolis"));
        XfaScriptHost host = new XfaScriptHost(root);

        XfaNode? viaData = host.Resolve("data.City", null);
        viaData.Should().NotBeNull();
        XfaScriptHost.GetProperty(viaData!, "value").Should().Be("Metropolis");
    }

    [Fact]
    public void Host_SetPropertyWritesFieldValue()
    {
        XfaSubform root = BuildTree(("Target", null));
        XfaScriptHost host = new XfaScriptHost(root);

        XfaNode target = host.Resolve("Target", null)!;
        XfaScriptHost.SetProperty(target, "rawValue", "written");

        ((XfaField)target).Value!.Text.Should().Be("written");
    }

    // ── JavaScript engine ──────────────────────────────────────────────────────

    [Fact]
    public void Js_ConcatenatesLiteralsAndSomReads()
    {
        XfaSubform root = BuildTree(("Company", "ACME"), ("City", "Springfield"), ("Out", null));
        XfaScriptHost host = new XfaScriptHost(root);
        XfaField outField = FindField(root, "Out")!;

        new XfaJavaScriptEngine(host).Execute(
            "this.rawValue = \"Certified: \" + Company.rawValue + \" of \" + City.rawValue + \".\";",
            outField);

        outField.Value!.Text.Should().Be("Certified: ACME of Springfield.");
    }

    [Fact]
    public void Js_NumericExpressionWithStringCoercion()
    {
        XfaSubform root = BuildTree(("Price", "250"), ("Qty", "4"), ("Out", null));
        XfaScriptHost host = new XfaScriptHost(root);
        XfaField outField = FindField(root, "Out")!;

        new XfaJavaScriptEngine(host).Execute(
            "this.rawValue = \"Total = \" + String(Number(Price.rawValue) * Number(Qty.rawValue));",
            outField);

        outField.Value!.Text.Should().Be("Total = 1000");
    }

    [Fact]
    public void Js_NewlineEscapeAndUpperCase()
    {
        XfaSubform root = BuildTree(("Company", "acme"), ("City", "Springfield"), ("Out", null));
        XfaScriptHost host = new XfaScriptHost(root);
        XfaField outField = FindField(root, "Out")!;

        new XfaJavaScriptEngine(host).Execute(
            "this.rawValue = Company.rawValue.toUpperCase() + '\\n' + City.rawValue;",
            outField);

        outField.Value!.Text.Should().Be("ACME\nSpringfield");
    }

    [Fact]
    public void Js_ForLoopAndMathBuiltins()
    {
        XfaSubform root = BuildTree(("Out", null));
        XfaScriptHost host = new XfaScriptHost(root);
        XfaField outField = FindField(root, "Out")!;

        new XfaJavaScriptEngine(host).Execute(
            "var y = 0; for (var i = 1; i <= 5; i = i + 1) { y = y + i; } "
            + "this.rawValue = String(Math.max(y, 10));",
            outField);

        outField.Value!.Text.Should().Be("15");
    }

    [Fact]
    public void Js_TernaryAndComparison()
    {
        XfaSubform root = BuildTree(("Price", "250"), ("Out", null));
        XfaScriptHost host = new XfaScriptHost(root);
        XfaField outField = FindField(root, "Out")!;

        new XfaJavaScriptEngine(host).Execute(
            "this.rawValue = Number(Price.rawValue) > 100 ? \"expensive\" : \"cheap\";",
            outField);

        outField.Value!.Text.Should().Be("expensive");
    }

    [Fact]
    public void Js_CommentedOutScriptIsNoOp()
    {
        XfaSubform root = BuildTree(("Out", null));
        XfaScriptHost host = new XfaScriptHost(root);
        XfaField outField = FindField(root, "Out")!;

        new XfaJavaScriptEngine(host).Execute("// this.rawValue = \"nope\";", outField);

        outField.Value.Should().BeNull("a fully commented script must not write");
    }

    // ── FormCalc engine ────────────────────────────────────────────────────────

    [Fact]
    public void FormCalc_ArithmeticAssignment()
    {
        XfaSubform root = BuildTree(("Base", "120"), ("Rate", "3"), ("Amount", null));
        XfaScriptHost host = new XfaScriptHost(root);
        XfaField amount = FindField(root, "Amount")!;

        new XfaFormCalcEngine(host).Execute("Amount = Base * Rate", amount);

        amount.Value!.Text.Should().Be("360");
    }

    [Fact]
    public void FormCalc_ConcatOperatorAndUpper()
    {
        XfaSubform root = BuildTree(("First", "ada"), ("Last", "lovelace"), ("FullName", null));
        XfaScriptHost host = new XfaScriptHost(root);
        XfaField fullName = FindField(root, "FullName")!;

        new XfaFormCalcEngine(host).Execute("FullName = Upper(First) & \" \" & Upper(Last)", fullName);

        fullName.Value!.Text.Should().Be("ADA LOVELACE");
    }

    [Fact]
    public void FormCalc_IfThenElse()
    {
        XfaSubform root = BuildTree(("Base", "120"));
        XfaScriptHost host = new XfaScriptHost(root);

        string result = new XfaFormCalcEngine(host).Execute(
            "if (Base > 100) then \"premium\" else \"standard\" endif", root);

        result.Should().Be("premium");
    }

    [Fact]
    public void FormCalc_ForLoopSum()
    {
        XfaSubform root = BuildTree(("Out", null));
        XfaScriptHost host = new XfaScriptHost(root);

        string result = new XfaFormCalcEngine(host).Execute(
            "var s = 0; for i = 1 upto 5 do s = s + i endfor; s", root);

        result.Should().Be("15");
    }

    [Fact]
    public void FormCalc_StringBuiltins()
    {
        XfaSubform root = BuildTree(("Out", null));
        XfaScriptHost host = new XfaScriptHost(root);
        XfaFormCalcEngine engine = new XfaFormCalcEngine(host);

        engine.Execute("Left(\"abcdef\", 3)", root).Should().Be("abc");
        engine.Execute("Right(\"abcdef\", 2)", root).Should().Be("ef");
        engine.Execute("Len(\"hello\")", root).Should().Be("5");
        engine.Execute("Substr(\"abcdef\", 2, 3)", root).Should().Be("bcd");
        engine.Execute("At(\"hello world\", \"world\")", root).Should().Be("7");
        engine.Execute("Sum(1, 2, 3, 4)", root).Should().Be("10");
        engine.Execute("Round(3.14159, 2)", root).Should().Be("3.14");
    }

    // ── Fail-soft ──────────────────────────────────────────────────────────────

    [Fact]
    public void Runner_FailsSoftOnUnsupportedScript()
    {
        XfaSubform root = new XfaSubform { Name = "form1" };
        XfaField good = new XfaField { Name = "Good" };
        XfaField bad = new XfaField { Name = "Bad" };
        root.AddChild(good);
        root.AddChild(bad);
        good.AddScript(new XfaScript(
            XfaScriptLanguage.JavaScript, XfaScriptEvent.Initialize, "this.rawValue = \"OK\";"));
        bad.AddScript(new XfaScript(
            XfaScriptLanguage.JavaScript, XfaScriptEvent.Initialize, "this.rawValue = /abc/.test('x');"));

        XfaScriptHost host = new XfaScriptHost(root);
        System.Action act = () => XfaScriptRunner.RunInitialize(root, host);

        act.Should().NotThrow("the runner must isolate a failing script");
        good.Value!.Text.Should().Be("OK");
        bad.Value.Should().BeNull("the unsupported script fails soft and writes nothing");
    }

    [Fact]
    public void Engine_ThrowsScriptExceptionOnUnsupportedConstruct()
    {
        XfaSubform root = BuildTree(("Out", null));
        XfaScriptHost host = new XfaScriptHost(root);
        XfaField outField = FindField(root, "Out")!;

        System.Action act = () =>
            new XfaJavaScriptEngine(host).Execute("this.rawValue = /abc/.test('x');", outField);

        act.Should().Throw<XfaScriptException>();
    }

    // ── Initialize fires through the tree ──────────────────────────────────────

    [Fact]
    public void RunInitialize_FillsAllScriptedFields()
    {
        XfaSubform root = LoadTemplate("synthetic-script-js.xml");
        XfaScriptHost host = new XfaScriptHost(root);

        XfaScriptRunner.RunInitialize(root, host);

        FindField(root, "Certified")!.Value!.Text
            .Should().Be("Certified: ACME WIDGETS of Springfield.");
        FindField(root, "Total")!.Value!.Text.Should().Be("Total = 1000");
        FindField(root, "Block")!.Value!.Text.Should().Be("ACME WIDGETS\nSpringfield");
        FindField(root, "Untouched")!.Value.Should().BeNull();
    }

    [Fact]
    public void RunInitialize_FormCalcFixtureComputesValues()
    {
        XfaSubform root = LoadTemplate("synthetic-script-formcalc.xml");
        XfaScriptHost host = new XfaScriptHost(root);

        XfaScriptRunner.RunInitialize(root, host);

        FindField(root, "FullName")!.Value!.Text.Should().Be("ADA LOVELACE");
        FindField(root, "Amount")!.Value!.Text.Should().Be("360");
        FindField(root, "Tier")!.Value!.Text.Should().Be("premium");
    }

    // ── Real COI scripts ───────────────────────────────────────────────────────

    [Fact]
    public void RunInitialize_RealCoiScriptsComputeSentences()
    {
        using PdfDocument doc = PdfDocument.Open(
            Path.Combine(FixturesDir, "livecycle-coi-redacted.pdf"));

        XfaSubform root = XfaTemplateParser.Parse(doc.Xfa!.Template!.Xml)!;
        XfaDataMerge.Apply(root, doc.Xfa.DataFields);

        XfaScriptHost host = new XfaScriptHost(root);
        XfaScriptRunner.RunInitialize(root, host);

        FindField(root, "Text1")!.Value!.Text
            .Should().Contain("I hereby certify that")
            .And.Contain("EXAMPLE COMPANY PRIVATE LIMITED");
        FindField(root, "Panline")!.Value!.Text
            .Should().StartWith("The Permanent Account Number (PAN)");
        FindField(root, "Tanline")!.Value!.Text
            .Should().StartWith("The Tax Deduction and Collection Account Number (TAN)");
    }

    // ── Render integration: default mode leaves scripts off ────────────────────

    [Fact]
    public void Render_DefaultMode_DoesNotRunScripts()
    {
        using PdfDocument doc = PdfDocument.Open(
            Path.Combine(FixturesDir, "livecycle-coi-redacted.pdf"));

        using MemoryStream output = new MemoryStream();
        XfaRenderer.Render(output, doc, XfaRenderOptions.Default);

        output.Length.Should().BeGreaterThan(1000);
        output.Position = 0;
        using PdfDocument rendered = PdfDocument.Open(output, leaveOpen: true);
        rendered.PageCount.Should().Be(1);
    }

    [Fact]
    public void Render_FullMode_RunsInitializeScripts()
    {
        using PdfDocument doc = PdfDocument.Open(
            Path.Combine(FixturesDir, "livecycle-coi-redacted.pdf"));

        using MemoryStream output = new MemoryStream();
        XfaRenderer.Render(output, doc, new XfaRenderOptions { ScriptMode = XfaScriptMode.Full });

        output.Length.Should().BeGreaterThan(1000);
        output.Position = 0;
        using PdfDocument rendered = PdfDocument.Open(output, leaveOpen: true);
        rendered.PageCount.Should().Be(1);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static XfaSubform LoadTemplate(string fixture)
    {
        byte[] xml = File.ReadAllBytes(Path.Combine(FixturesDir, fixture));
        return XfaTemplateParser.Parse(xml)!;
    }

    private static XfaSubform BuildTree(params (string Name, string? Value)[] fields)
    {
        XfaSubform root = new XfaSubform { Name = "form1" };
        foreach ((string name, string? value) in fields)
        {
            XfaField field = new XfaField { Name = name };
            if (value is not null)
            {
                field.Value = new XfaValue { Text = value };
            }

            root.AddChild(field);
        }

        return root;
    }

    private static XfaField? FindField(XfaNode root, string name)
    {
        Queue<XfaNode> queue = new Queue<XfaNode>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            XfaNode node = queue.Dequeue();
            if (node is XfaField field && field.Name == name)
            {
                return field;
            }

            foreach (XfaNode child in node.Children)
            {
                queue.Enqueue(child);
            }
        }

        return null;
    }
}
