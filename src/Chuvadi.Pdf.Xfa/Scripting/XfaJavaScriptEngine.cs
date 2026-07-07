// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ECMAScript subset as used by XFA form scripts (field calculations,
//        SOM node access, string/number/date builtins).
// PHASE: LA-23b Phase E — JavaScript engine.
//
// Scope: expressions (arithmetic, string concat, comparison, logical, ternary,
// member access, calls, assignment), var declarations, if/else, for, while,
// do-while, blocks, return. Host objects: this, SOM node references, .rawValue
// /.value/.presence. Builtins: String, Number, Math.*, and common string
// methods. Constructs outside this subset cause the script to fail soft (the
// runner catches XfaScriptException and leaves state untouched).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Chuvadi.Pdf.Xfa.Model;

namespace Chuvadi.Pdf.Xfa.Scripting;

/// <summary>
/// A small JavaScript interpreter covering the language subset that XFA form
/// scripts use. Evaluates a script in the context of a <c>this</c> node against
/// a <see cref="XfaScriptHost"/>.
/// </summary>
public sealed class XfaJavaScriptEngine
{
    private readonly XfaScriptHost _host;

    /// <summary>Initializes a new instance of the <see cref="XfaJavaScriptEngine"/> class.</summary>
    /// <param name="host">The scripting host for SOM resolution.</param>
    /// <exception cref="ArgumentNullException"><paramref name="host"/> is null.</exception>
    public XfaJavaScriptEngine(XfaScriptHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
    }

    /// <summary>Executes JavaScript source in the context of a node.</summary>
    /// <param name="source">The script source.</param>
    /// <param name="thisNode">The node bound to <c>this</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    /// <exception cref="XfaScriptException">The script uses an unsupported construct.</exception>
    public void Execute(string source, XfaNode? thisNode)
    {
        ArgumentNullException.ThrowIfNull(source);

        List<JsToken> tokens = new JsLexer(source).Tokenize();
        List<JsStatement> program = new JsParser(tokens).ParseProgram();

        JsScope scope = new JsScope(null);
        JsInterpreter interpreter = new JsInterpreter(_host, thisNode);
        interpreter.Run(program, scope);
    }

    // ── Lexer ─────────────────────────────────────────────────────────────────

    private enum JsTokenKind
    {
        Identifier,
        Number,
        String,
        Punctuator,
        Keyword,
        Eof,
    }

    private readonly struct JsToken
    {
        internal JsToken(JsTokenKind kind, string text)
        {
            Kind = kind;
            Text = text;
        }

        internal JsTokenKind Kind { get; }

        internal string Text { get; }
    }

    private static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
    {
        "var", "if", "else", "for", "while", "do", "return", "true", "false",
        "null", "undefined", "function", "new", "this", "break", "continue",
    };

    private sealed class JsLexer
    {
        private readonly string _s;
        private int _i;

        internal JsLexer(string source)
        {
            _s = source;
        }

        internal List<JsToken> Tokenize()
        {
            List<JsToken> tokens = new List<JsToken>();
            while (_i < _s.Length)
            {
                char c = _s[_i];

                if (char.IsWhiteSpace(c))
                {
                    _i++;
                    continue;
                }

                if (c == '/' && Peek(1) == '/')
                {
                    SkipLineComment();
                    continue;
                }

                if (c == '/' && Peek(1) == '*')
                {
                    SkipBlockComment();
                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    tokens.Add(new JsToken(JsTokenKind.String, ReadString(c)));
                    continue;
                }

                if (char.IsDigit(c) || (c == '.' && char.IsDigit(Peek(1))))
                {
                    tokens.Add(new JsToken(JsTokenKind.Number, ReadNumber()));
                    continue;
                }

                if (char.IsLetter(c) || c == '_' || c == '$')
                {
                    string ident = ReadIdentifier();
                    JsTokenKind kind = Keywords.Contains(ident)
                        ? JsTokenKind.Keyword
                        : JsTokenKind.Identifier;
                    tokens.Add(new JsToken(kind, ident));
                    continue;
                }

                tokens.Add(new JsToken(JsTokenKind.Punctuator, ReadPunctuator()));
            }

            tokens.Add(new JsToken(JsTokenKind.Eof, string.Empty));
            return tokens;
        }

        private char Peek(int ahead) => _i + ahead < _s.Length ? _s[_i + ahead] : '\0';

        private void SkipLineComment()
        {
            while (_i < _s.Length && _s[_i] != '\n')
            {
                _i++;
            }
        }

        private void SkipBlockComment()
        {
            _i += 2;
            while (_i < _s.Length && !(_s[_i] == '*' && Peek(1) == '/'))
            {
                _i++;
            }

            _i += 2;
        }

        private string ReadString(char quote)
        {
            StringBuilder sb = new StringBuilder();
            _i++;
            while (_i < _s.Length && _s[_i] != quote)
            {
                char c = _s[_i];
                if (c == '\\' && _i + 1 < _s.Length)
                {
                    _i++;
                    sb.Append(Unescape(_s[_i]));
                }
                else
                {
                    sb.Append(c);
                }

                _i++;
            }

            _i++;
            return sb.ToString();
        }

        private static char Unescape(char c) => c switch
        {
            'n' => '\n',
            't' => '\t',
            'r' => '\r',
            '\\' => '\\',
            '\'' => '\'',
            '"' => '"',
            '0' => '\0',
            _ => c,
        };

        private string ReadNumber()
        {
            int start = _i;
            while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '.'))
            {
                _i++;
            }

            return _s.Substring(start, _i - start);
        }

        private string ReadIdentifier()
        {
            int start = _i;
            while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '_' || _s[_i] == '$'))
            {
                _i++;
            }

            return _s.Substring(start, _i - start);
        }

        private string ReadPunctuator()
        {
            foreach (string p in MultiCharPunctuators)
            {
                if (Matches(p))
                {
                    _i += p.Length;
                    return p;
                }
            }

            char c = _s[_i];
            _i++;
            return c.ToString();
        }

        private static readonly string[] MultiCharPunctuators =
        {
            "===", "!==", "==", "!=", "<=", ">=", "&&", "||", "+=", "-=", "++", "--",
        };

        private bool Matches(string p)
        {
            if (_i + p.Length > _s.Length)
            {
                return false;
            }

            for (int k = 0; k < p.Length; k++)
            {
                if (_s[_i + k] != p[k])
                {
                    return false;
                }
            }

            return true;
        }
    }

    // ── AST ───────────────────────────────────────────────────────────────────

    private abstract class JsStatement
    {
    }

    private sealed class JsExpressionStatement : JsStatement
    {
        internal JsExpr Expr { get; init; } = default!;
    }

    private sealed class JsVarStatement : JsStatement
    {
        internal string Name { get; init; } = string.Empty;

        internal JsExpr? Initializer { get; init; }
    }

    private sealed class JsIfStatement : JsStatement
    {
        internal JsExpr Condition { get; init; } = default!;

        internal List<JsStatement> Then { get; init; } = new List<JsStatement>();

        internal List<JsStatement> Else { get; init; } = new List<JsStatement>();
    }

    private sealed class JsForStatement : JsStatement
    {
        internal JsStatement? Init { get; init; }

        internal JsExpr? Condition { get; init; }

        internal JsExpr? Update { get; init; }

        internal List<JsStatement> Body { get; init; } = new List<JsStatement>();
    }

    private sealed class JsWhileStatement : JsStatement
    {
        internal JsExpr Condition { get; init; } = default!;

        internal List<JsStatement> Body { get; init; } = new List<JsStatement>();

        internal bool DoWhile { get; init; }
    }

    private sealed class JsReturnStatement : JsStatement
    {
        internal JsExpr? Value { get; init; }
    }

    private sealed class JsBreakStatement : JsStatement
    {
    }

    private sealed class JsContinueStatement : JsStatement
    {
    }

    private abstract class JsExpr
    {
    }

    private sealed class JsLiteral : JsExpr
    {
        internal XfaScriptValue Value { get; init; }
    }

    private sealed class JsIdentifier : JsExpr
    {
        internal string Name { get; init; } = string.Empty;
    }

    private sealed class JsThis : JsExpr
    {
    }

    private sealed class JsMember : JsExpr
    {
        internal JsExpr Target { get; init; } = default!;

        internal string Name { get; init; } = string.Empty;
    }

    private sealed class JsIndex : JsExpr
    {
        internal JsExpr Target { get; init; } = default!;

        internal JsExpr Index { get; init; } = default!;
    }

    private sealed class JsCall : JsExpr
    {
        internal JsExpr Callee { get; init; } = default!;

        internal List<JsExpr> Args { get; init; } = new List<JsExpr>();
    }

    private sealed class JsUnary : JsExpr
    {
        internal string Op { get; init; } = string.Empty;

        internal JsExpr Operand { get; init; } = default!;
    }

    private sealed class JsBinary : JsExpr
    {
        internal string Op { get; init; } = string.Empty;

        internal JsExpr Left { get; init; } = default!;

        internal JsExpr Right { get; init; } = default!;
    }

    private sealed class JsLogical : JsExpr
    {
        internal string Op { get; init; } = string.Empty;

        internal JsExpr Left { get; init; } = default!;

        internal JsExpr Right { get; init; } = default!;
    }

    private sealed class JsAssign : JsExpr
    {
        internal string Op { get; init; } = "=";

        internal JsExpr Target { get; init; } = default!;

        internal JsExpr Value { get; init; } = default!;
    }

    private sealed class JsConditional : JsExpr
    {
        internal JsExpr Condition { get; init; } = default!;

        internal JsExpr Then { get; init; } = default!;

        internal JsExpr Else { get; init; } = default!;
    }

    // ── Parser (recursive descent with precedence climbing) ────────────────────

    private sealed class JsParser
    {
        private readonly List<JsToken> _tokens;
        private int _i;

        internal JsParser(List<JsToken> tokens)
        {
            _tokens = tokens;
        }

        internal List<JsStatement> ParseProgram()
        {
            List<JsStatement> statements = new List<JsStatement>();
            while (!IsEof)
            {
                statements.Add(ParseStatement());
            }

            return statements;
        }

        private JsToken Current => _tokens[_i];

        private bool IsEof => Current.Kind == JsTokenKind.Eof;

        private JsToken Advance() => _tokens[_i++];

        private bool Check(string text) => Current.Text == text && !IsEof;

        private bool Match(string text)
        {
            if (Check(text))
            {
                _i++;
                return true;
            }

            return false;
        }

        private void Expect(string text)
        {
            if (!Match(text))
            {
                throw new XfaScriptException($"Expected '{text}' but found '{Current.Text}'.");
            }
        }

        private JsStatement ParseStatement()
        {
            if (Match(";"))
            {
                return new JsExpressionStatement { Expr = new JsLiteral { Value = XfaScriptValue.Undefined } };
            }

            if (Check("{"))
            {
                // A bare block: unwrap into a single synthetic if(true) body.
                List<JsStatement> body = ParseBlock();
                return new JsIfStatement
                {
                    Condition = new JsLiteral { Value = XfaScriptValue.FromBoolean(true) },
                    Then = body,
                };
            }

            if (Current.Kind == JsTokenKind.Keyword)
            {
                switch (Current.Text)
                {
                    case "var":
                        return ParseVar();
                    case "if":
                        return ParseIf();
                    case "for":
                        return ParseFor();
                    case "while":
                        return ParseWhile();
                    case "do":
                        return ParseDoWhile();
                    case "return":
                        return ParseReturn();
                    case "break":
                        Advance();
                        Match(";");
                        return new JsBreakStatement();
                    case "continue":
                        Advance();
                        Match(";");
                        return new JsContinueStatement();
                    case "function":
                        throw new XfaScriptException("Function declarations are not supported.");
                    default:
                        break;
                }
            }

            JsExpr expr = ParseExpression();
            Match(";");
            return new JsExpressionStatement { Expr = expr };
        }

        private List<JsStatement> ParseBlock()
        {
            Expect("{");
            List<JsStatement> statements = new List<JsStatement>();
            while (!Check("}") && !IsEof)
            {
                statements.Add(ParseStatement());
            }

            Expect("}");
            return statements;
        }

        private List<JsStatement> ParseBlockOrSingle()
        {
            if (Check("{"))
            {
                return ParseBlock();
            }

            return new List<JsStatement> { ParseStatement() };
        }

        private JsVarStatement ParseVar()
        {
            Advance();
            if (Current.Kind != JsTokenKind.Identifier)
            {
                throw new XfaScriptException("Expected identifier after 'var'.");
            }

            string name = Advance().Text;
            JsExpr? init = null;
            if (Match("="))
            {
                init = ParseAssignment();
            }

            Match(";");
            return new JsVarStatement { Name = name, Initializer = init };
        }

        private JsIfStatement ParseIf()
        {
            Advance();
            Expect("(");
            JsExpr condition = ParseExpression();
            Expect(")");
            List<JsStatement> then = ParseBlockOrSingle();
            List<JsStatement> els = new List<JsStatement>();
            if (Match("else"))
            {
                els = ParseBlockOrSingle();
            }

            return new JsIfStatement { Condition = condition, Then = then, Else = els };
        }

        private JsForStatement ParseFor()
        {
            Advance();
            Expect("(");
            JsStatement? init = null;
            if (!Check(";"))
            {
                init = Check("var") ? ParseVar() : new JsExpressionStatement { Expr = ParseExpression() };
            }

            Match(";");
            JsExpr? condition = Check(";") ? null : ParseExpression();
            Expect(";");
            JsExpr? update = Check(")") ? null : ParseExpression();
            Expect(")");
            List<JsStatement> body = ParseBlockOrSingle();
            return new JsForStatement { Init = init, Condition = condition, Update = update, Body = body };
        }

        private JsWhileStatement ParseWhile()
        {
            Advance();
            Expect("(");
            JsExpr condition = ParseExpression();
            Expect(")");
            List<JsStatement> body = ParseBlockOrSingle();
            return new JsWhileStatement { Condition = condition, Body = body };
        }

        private JsWhileStatement ParseDoWhile()
        {
            Advance();
            List<JsStatement> body = ParseBlockOrSingle();
            Expect("while");
            Expect("(");
            JsExpr condition = ParseExpression();
            Expect(")");
            Match(";");
            return new JsWhileStatement { Condition = condition, Body = body, DoWhile = true };
        }

        private JsReturnStatement ParseReturn()
        {
            Advance();
            JsExpr? value = (Check(";") || Check("}") || IsEof) ? null : ParseExpression();
            Match(";");
            return new JsReturnStatement { Value = value };
        }

        private JsExpr ParseExpression() => ParseAssignment();

        private JsExpr ParseAssignment()
        {
            JsExpr left = ParseConditional();
            if (Current.Text is "=" or "+=" or "-=")
            {
                string op = Advance().Text;
                JsExpr value = ParseAssignment();
                return new JsAssign { Op = op, Target = left, Value = value };
            }

            return left;
        }

        private JsExpr ParseConditional()
        {
            JsExpr condition = ParseBinary(0);
            if (Match("?"))
            {
                JsExpr then = ParseAssignment();
                Expect(":");
                JsExpr els = ParseAssignment();
                return new JsConditional { Condition = condition, Then = then, Else = els };
            }

            return condition;
        }

        private static int Precedence(string op) => op switch
        {
            "||" => 1,
            "&&" => 2,
            "==" or "!=" or "===" or "!==" => 3,
            "<" or ">" or "<=" or ">=" => 4,
            "+" or "-" => 5,
            "*" or "/" or "%" => 6,
            _ => -1,
        };

        private JsExpr ParseBinary(int minPrecedence)
        {
            JsExpr left = ParseUnary();
            while (true)
            {
                string op = Current.Text;
                int precedence = Precedence(op);
                if (precedence < 0 || precedence < minPrecedence || IsEof)
                {
                    break;
                }

                Advance();
                JsExpr right = ParseBinary(precedence + 1);
                left = op is "&&" or "||"
                    ? new JsLogical { Op = op, Left = left, Right = right }
                    : new JsBinary { Op = op, Left = left, Right = right };
            }

            return left;
        }

        private JsExpr ParseUnary()
        {
            if (Current.Text is "!" or "-" or "+")
            {
                string op = Advance().Text;
                return new JsUnary { Op = op, Operand = ParseUnary() };
            }

            return ParsePostfix();
        }

        private JsExpr ParsePostfix()
        {
            JsExpr expr = ParsePrimary();
            while (true)
            {
                if (Match("."))
                {
                    string name = Advance().Text;
                    expr = new JsMember { Target = expr, Name = name };
                }
                else if (Match("["))
                {
                    JsExpr index = ParseExpression();
                    Expect("]");
                    expr = new JsIndex { Target = expr, Index = index };
                }
                else if (Check("("))
                {
                    expr = new JsCall { Callee = expr, Args = ParseArguments() };
                }
                else
                {
                    break;
                }
            }

            return expr;
        }

        private List<JsExpr> ParseArguments()
        {
            Expect("(");
            List<JsExpr> args = new List<JsExpr>();
            if (!Check(")"))
            {
                do
                {
                    args.Add(ParseAssignment());
                }
                while (Match(","));
            }

            Expect(")");
            return args;
        }

        private JsExpr ParsePrimary()
        {
            JsToken token = Current;

            switch (token.Kind)
            {
                case JsTokenKind.Number:
                    Advance();
                    return new JsLiteral
                    {
                        Value = XfaScriptValue.FromNumber(
                            double.Parse(token.Text, NumberStyles.Any, CultureInfo.InvariantCulture)),
                    };
                case JsTokenKind.String:
                    Advance();
                    return new JsLiteral { Value = XfaScriptValue.FromString(token.Text) };
                case JsTokenKind.Identifier:
                    Advance();
                    return new JsIdentifier { Name = token.Text };
                case JsTokenKind.Keyword:
                    return ParseKeywordPrimary(token);
                case JsTokenKind.Punctuator when token.Text == "(":
                    Advance();
                    JsExpr inner = ParseExpression();
                    Expect(")");
                    return inner;
                default:
                    throw new XfaScriptException($"Unexpected token '{token.Text}'.");
            }
        }

        private JsExpr ParseKeywordPrimary(JsToken token)
        {
            switch (token.Text)
            {
                case "this":
                    Advance();
                    return new JsThis();
                case "true":
                    Advance();
                    return new JsLiteral { Value = XfaScriptValue.FromBoolean(true) };
                case "false":
                    Advance();
                    return new JsLiteral { Value = XfaScriptValue.FromBoolean(false) };
                case "null":
                case "undefined":
                    Advance();
                    return new JsLiteral { Value = XfaScriptValue.Undefined };
                case "new":
                    throw new XfaScriptException("'new' expressions are not supported.");
                default:
                    throw new XfaScriptException($"Unexpected keyword '{token.Text}'.");
            }
        }
    }

    // ── Interpreter ────────────────────────────────────────────────────────────

    private sealed class JsScope
    {
        private readonly JsScope? _parent;
        private readonly Dictionary<string, XfaScriptValue> _vars =
            new Dictionary<string, XfaScriptValue>(StringComparer.Ordinal);

        internal JsScope(JsScope? parent)
        {
            _parent = parent;
        }

        internal void Declare(string name, XfaScriptValue value) => _vars[name] = value;

        internal bool TryGet(string name, out XfaScriptValue value)
        {
            for (JsScope? scope = this; scope is not null; scope = scope._parent)
            {
                if (scope._vars.TryGetValue(name, out value))
                {
                    return true;
                }
            }

            value = XfaScriptValue.Undefined;
            return false;
        }

        internal bool TrySet(string name, XfaScriptValue value)
        {
            for (JsScope? scope = this; scope is not null; scope = scope._parent)
            {
                if (scope._vars.ContainsKey(name))
                {
                    scope._vars[name] = value;
                    return true;
                }
            }

            return false;
        }
    }

    private enum JsFlow
    {
        Normal,
        Return,
        Break,
        Continue,
    }

    private sealed class JsInterpreter
    {
        private const int MaxIterations = 100000;

        private readonly XfaScriptHost _host;
        private readonly XfaNode? _thisNode;

        internal JsInterpreter(XfaScriptHost host, XfaNode? thisNode)
        {
            _host = host;
            _thisNode = thisNode;
        }

        internal void Run(List<JsStatement> program, JsScope scope)
        {
            ExecuteBlock(program, scope);
        }

        private JsFlow ExecuteBlock(List<JsStatement> statements, JsScope scope)
        {
            foreach (JsStatement statement in statements)
            {
                JsFlow flow = ExecuteStatement(statement, scope);
                if (flow != JsFlow.Normal)
                {
                    return flow;
                }
            }

            return JsFlow.Normal;
        }

        private JsFlow ExecuteStatement(JsStatement statement, JsScope scope)
        {
            switch (statement)
            {
                case JsExpressionStatement expr:
                    Evaluate(expr.Expr, scope);
                    return JsFlow.Normal;
                case JsVarStatement var:
                    scope.Declare(
                        var.Name,
                        var.Initializer is null ? XfaScriptValue.Undefined : Evaluate(var.Initializer, scope));
                    return JsFlow.Normal;
                case JsIfStatement ifs:
                    return Evaluate(ifs.Condition, scope).ToBoolean()
                        ? ExecuteBlock(ifs.Then, new JsScope(scope))
                        : ExecuteBlock(ifs.Else, new JsScope(scope));
                case JsForStatement fors:
                    return ExecuteFor(fors, scope);
                case JsWhileStatement whiles:
                    return ExecuteWhile(whiles, scope);
                case JsReturnStatement:
                    return JsFlow.Return;
                case JsBreakStatement:
                    return JsFlow.Break;
                case JsContinueStatement:
                    return JsFlow.Continue;
                default:
                    return JsFlow.Normal;
            }
        }

        private JsFlow ExecuteFor(JsForStatement fors, JsScope parent)
        {
            JsScope scope = new JsScope(parent);
            if (fors.Init is not null)
            {
                ExecuteStatement(fors.Init, scope);
            }

            int guard = 0;
            while (fors.Condition is null || Evaluate(fors.Condition, scope).ToBoolean())
            {
                if (++guard > MaxIterations)
                {
                    throw new XfaScriptException("Loop iteration limit exceeded.");
                }

                JsFlow flow = ExecuteBlock(fors.Body, new JsScope(scope));
                if (flow == JsFlow.Return)
                {
                    return flow;
                }

                if (flow == JsFlow.Break)
                {
                    break;
                }

                if (fors.Update is not null)
                {
                    Evaluate(fors.Update, scope);
                }
            }

            return JsFlow.Normal;
        }

        private JsFlow ExecuteWhile(JsWhileStatement whiles, JsScope parent)
        {
            JsScope scope = new JsScope(parent);
            int guard = 0;
            bool first = true;

            while (true)
            {
                if (!whiles.DoWhile || !first)
                {
                    if (!Evaluate(whiles.Condition, scope).ToBoolean())
                    {
                        break;
                    }
                }

                first = false;
                if (++guard > MaxIterations)
                {
                    throw new XfaScriptException("Loop iteration limit exceeded.");
                }

                JsFlow flow = ExecuteBlock(whiles.Body, new JsScope(scope));
                if (flow == JsFlow.Return)
                {
                    return flow;
                }

                if (flow == JsFlow.Break)
                {
                    break;
                }

                if (whiles.DoWhile && !Evaluate(whiles.Condition, scope).ToBoolean())
                {
                    break;
                }
            }

            return JsFlow.Normal;
        }

        private XfaScriptValue Evaluate(JsExpr expr, JsScope scope)
        {
            switch (expr)
            {
                case JsLiteral literal:
                    return literal.Value;
                case JsThis:
                    return _thisNode is null ? XfaScriptValue.Undefined : XfaScriptValue.FromNode(_thisNode);
                case JsIdentifier identifier:
                    return EvaluateIdentifier(identifier, scope);
                case JsMember member:
                    return EvaluateMember(member, scope);
                case JsIndex index:
                    return EvaluateIndex(index, scope);
                case JsCall call:
                    return EvaluateCall(call, scope);
                case JsUnary unary:
                    return EvaluateUnary(unary, scope);
                case JsBinary binary:
                    return EvaluateBinary(binary, scope);
                case JsLogical logical:
                    return EvaluateLogical(logical, scope);
                case JsConditional conditional:
                    return Evaluate(conditional.Condition, scope).ToBoolean()
                        ? Evaluate(conditional.Then, scope)
                        : Evaluate(conditional.Else, scope);
                case JsAssign assign:
                    return EvaluateAssign(assign, scope);
                default:
                    return XfaScriptValue.Undefined;
            }
        }

        private XfaScriptValue EvaluateIdentifier(JsIdentifier identifier, JsScope scope)
        {
            if (scope.TryGet(identifier.Name, out XfaScriptValue value))
            {
                return value;
            }

            // Bare identifiers such as Math / String / Number are builtin roots;
            // return them as strings so member access can dispatch on the name.
            if (identifier.Name is "Math" or "String" or "Number" or "Date" or "xfa")
            {
                return XfaScriptValue.FromString("[builtin:" + identifier.Name + "]");
            }

            // A bare identifier may be a SOM node reference (e.g. Certificate).
            XfaNode? node = _host.Resolve(identifier.Name, _thisNode);
            return node is not null ? XfaScriptValue.FromNode(node) : XfaScriptValue.Undefined;
        }

        private XfaScriptValue EvaluateMember(JsMember member, JsScope scope)
        {
            // A dotted SOM path: build the full reference and resolve as a node
            // first; only fall back to property access when that fails.
            string? path = TryFlattenPath(member);
            if (path is not null)
            {
                XfaNode? node = _host.Resolve(path, _thisNode);
                if (node is not null)
                {
                    return XfaScriptValue.FromNode(node);
                }
            }

            XfaScriptValue target = Evaluate(member.Target, scope);

            if (target.IsNode)
            {
                XfaNode node = target.AsNode()!;
                XfaNode? child = _host.Resolve(node.Name is null ? member.Name : node.Name + "." + member.Name, _thisNode);
                if (child is not null && member.Name is not ("rawValue" or "value" or "text" or "presence" or "name"))
                {
                    return XfaScriptValue.FromNode(child);
                }

                return XfaScriptValue.FromString(XfaScriptHost.GetProperty(node, member.Name));
            }

            return EvaluateBuiltinMember(target, member.Name);
        }

        private static XfaScriptValue EvaluateBuiltinMember(XfaScriptValue target, string name)
        {
            // String length is the only zero-arg member forms rely on here;
            // method members are resolved at the call site.
            if (name == "length")
            {
                return XfaScriptValue.FromNumber(target.ToStringValue().Length);
            }

            return XfaScriptValue.FromString("[member:" + target.ToStringValue() + ":" + name + "]");
        }

        private XfaScriptValue EvaluateIndex(JsIndex index, JsScope scope)
        {
            XfaScriptValue target = Evaluate(index.Target, scope);
            XfaScriptValue key = Evaluate(index.Index, scope);
            string s = target.ToStringValue();
            int i = (int)key.ToNumber();
            if (i >= 0 && i < s.Length)
            {
                return XfaScriptValue.FromString(s[i].ToString());
            }

            return XfaScriptValue.Undefined;
        }

        private XfaScriptValue EvaluateCall(JsCall call, JsScope scope)
        {
            List<XfaScriptValue> args = new List<XfaScriptValue>();
            foreach (JsExpr argExpr in call.Args)
            {
                args.Add(Evaluate(argExpr, scope));
            }

            if (call.Callee is JsIdentifier callee)
            {
                return EvaluateGlobalCall(callee.Name, args);
            }

            if (call.Callee is JsMember member)
            {
                XfaScriptValue receiver = Evaluate(member.Target, scope);
                return EvaluateMethodCall(receiver, member.Name, args, member.Target);
            }

            return XfaScriptValue.Undefined;
        }

        private static XfaScriptValue EvaluateGlobalCall(string name, List<XfaScriptValue> args)
        {
            return name switch
            {
                "String" => XfaScriptValue.FromString(args.Count > 0 ? args[0].ToStringValue() : string.Empty),
                "Number" => XfaScriptValue.FromNumber(args.Count > 0 ? args[0].ToNumber() : 0.0),
                "Boolean" => XfaScriptValue.FromBoolean(args.Count > 0 && args[0].ToBoolean()),
                "parseInt" => XfaScriptValue.FromNumber(ParseIntLike(args)),
                "parseFloat" => XfaScriptValue.FromNumber(args.Count > 0 ? args[0].ToNumber() : double.NaN),
                "isNaN" => XfaScriptValue.FromBoolean(args.Count > 0 && double.IsNaN(args[0].ToNumber())),
                _ => throw new XfaScriptException($"Unknown function '{name}'."),
            };
        }

        private static double ParseIntLike(List<XfaScriptValue> args)
        {
            if (args.Count == 0)
            {
                return double.NaN;
            }

            double value = args[0].ToNumber();
            return double.IsNaN(value) ? double.NaN : Math.Truncate(value);
        }

        private XfaScriptValue EvaluateMethodCall(
            XfaScriptValue receiver, string method, List<XfaScriptValue> args, JsExpr receiverExpr)
        {
            // Math.* dispatches on the builtin root marker.
            string receiverText = receiver.ToStringValue();
            if (receiverExpr is JsIdentifier { Name: "Math" } || receiverText == "[builtin:Math]")
            {
                return EvaluateMathCall(method, args);
            }

            if (receiverExpr is JsIdentifier { Name: "xfa" } || receiverText == "[builtin:xfa]")
            {
                return EvaluateXfaCall(method, args);
            }

            return EvaluateStringMethod(receiver.ToStringValue(), method, args);
        }

        private XfaScriptValue EvaluateXfaCall(string method, List<XfaScriptValue> args)
        {
            if (method == "resolveNode" && args.Count > 0)
            {
                XfaNode? node = _host.Resolve(args[0].ToStringValue(), _thisNode);
                return node is not null ? XfaScriptValue.FromNode(node) : XfaScriptValue.Undefined;
            }

            return XfaScriptValue.Undefined;
        }

        private static XfaScriptValue EvaluateMathCall(string method, List<XfaScriptValue> args)
        {
            double a = args.Count > 0 ? args[0].ToNumber() : double.NaN;
            double b = args.Count > 1 ? args[1].ToNumber() : double.NaN;
            return method switch
            {
                "abs" => XfaScriptValue.FromNumber(Math.Abs(a)),
                "floor" => XfaScriptValue.FromNumber(Math.Floor(a)),
                "ceil" => XfaScriptValue.FromNumber(Math.Ceiling(a)),
                "round" => XfaScriptValue.FromNumber(Math.Round(a, MidpointRounding.AwayFromZero)),
                "sqrt" => XfaScriptValue.FromNumber(Math.Sqrt(a)),
                "pow" => XfaScriptValue.FromNumber(Math.Pow(a, b)),
                "min" => XfaScriptValue.FromNumber(Math.Min(a, b)),
                "max" => XfaScriptValue.FromNumber(Math.Max(a, b)),
                "trunc" => XfaScriptValue.FromNumber(Math.Truncate(a)),
                _ => XfaScriptValue.Undefined,
            };
        }

        private static XfaScriptValue EvaluateStringMethod(
            string s, string method, List<XfaScriptValue> args)
        {
            switch (method)
            {
                case "substr":
                    return XfaScriptValue.FromString(Substr(s, args));
                case "substring":
                    return XfaScriptValue.FromString(Substring(s, args));
                case "toUpperCase":
                    return XfaScriptValue.FromString(s.ToUpperInvariant());
                case "toLowerCase":
#pragma warning disable CA1308 // JS toLowerCase requires lowercase output
                    return XfaScriptValue.FromString(s.ToLowerInvariant());
#pragma warning restore CA1308
                case "charAt":
                    int idx = args.Count > 0 ? (int)args[0].ToNumber() : 0;
                    return XfaScriptValue.FromString(idx >= 0 && idx < s.Length ? s[idx].ToString() : string.Empty);
                case "indexOf":
                    return XfaScriptValue.FromNumber(
                        args.Count > 0 ? s.IndexOf(args[0].ToStringValue(), StringComparison.Ordinal) : -1);
                case "replace":
                    return XfaScriptValue.FromString(
                        args.Count > 1 ? ReplaceFirst(s, args[0].ToStringValue(), args[1].ToStringValue()) : s);
                case "concat":
                    return XfaScriptValue.FromString(Concat(s, args));
                case "trim":
                    return XfaScriptValue.FromString(s.Trim());
                case "split":
                    // split returns an array in JS; forms that call it here only
                    // use the first element, so return that pragmatically.
                    string sep = args.Count > 0 ? args[0].ToStringValue() : string.Empty;
                    string[] parts = sep.Length == 0 ? new[] { s } : s.Split(sep);
                    return XfaScriptValue.FromString(parts.Length > 0 ? parts[0] : string.Empty);
                default:
                    throw new XfaScriptException($"Unsupported string method '{method}'.");
            }
        }

        private static string Substr(string s, List<XfaScriptValue> args)
        {
            int start = args.Count > 0 ? (int)args[0].ToNumber() : 0;
            if (start < 0)
            {
                start = Math.Max(0, s.Length + start);
            }

            start = Math.Min(start, s.Length);
            int length = args.Count > 1 ? (int)args[1].ToNumber() : s.Length - start;
            length = Math.Max(0, Math.Min(length, s.Length - start));
            return s.Substring(start, length);
        }

        private static string Substring(string s, List<XfaScriptValue> args)
        {
            int a = args.Count > 0 ? (int)args[0].ToNumber() : 0;
            int b = args.Count > 1 ? (int)args[1].ToNumber() : s.Length;
            a = Math.Max(0, Math.Min(a, s.Length));
            b = Math.Max(0, Math.Min(b, s.Length));
            if (a > b)
            {
                (a, b) = (b, a);
            }

            return s.Substring(a, b - a);
        }

        private static string ReplaceFirst(string s, string find, string with)
        {
            if (find.Length == 0)
            {
                return s;
            }

            int i = s.IndexOf(find, StringComparison.Ordinal);
            return i < 0
                ? s
                : string.Concat(s.AsSpan(0, i), with, s.AsSpan(i + find.Length));
        }

        private static string Concat(string s, List<XfaScriptValue> args)
        {
            StringBuilder sb = new StringBuilder(s);
            foreach (XfaScriptValue arg in args)
            {
                sb.Append(arg.ToStringValue());
            }

            return sb.ToString();
        }

        private XfaScriptValue EvaluateUnary(JsUnary unary, JsScope scope)
        {
            XfaScriptValue operand = Evaluate(unary.Operand, scope);
            return unary.Op switch
            {
                "!" => XfaScriptValue.FromBoolean(!operand.ToBoolean()),
                "-" => XfaScriptValue.FromNumber(-operand.ToNumber()),
                "+" => XfaScriptValue.FromNumber(operand.ToNumber()),
                _ => XfaScriptValue.Undefined,
            };
        }

        private XfaScriptValue EvaluateBinary(JsBinary binary, JsScope scope)
        {
            XfaScriptValue left = Evaluate(binary.Left, scope);
            XfaScriptValue right = Evaluate(binary.Right, scope);

            switch (binary.Op)
            {
                case "+":
                    // String concatenation when either side is a (non-numeric)
                    // string; numeric addition otherwise.
                    if (IsStringy(left) || IsStringy(right))
                    {
                        return XfaScriptValue.FromString(left.ToStringValue() + right.ToStringValue());
                    }

                    return XfaScriptValue.FromNumber(left.ToNumber() + right.ToNumber());
                case "-":
                    return XfaScriptValue.FromNumber(left.ToNumber() - right.ToNumber());
                case "*":
                    return XfaScriptValue.FromNumber(left.ToNumber() * right.ToNumber());
                case "/":
                    return XfaScriptValue.FromNumber(left.ToNumber() / right.ToNumber());
                case "%":
                    return XfaScriptValue.FromNumber(left.ToNumber() % right.ToNumber());
                case "==":
                    return XfaScriptValue.FromBoolean(LooseEquals(left, right));
                case "!=":
                    return XfaScriptValue.FromBoolean(!LooseEquals(left, right));
                case "===":
                    return XfaScriptValue.FromBoolean(left.Equals(right));
                case "!==":
                    return XfaScriptValue.FromBoolean(!left.Equals(right));
                case "<":
                    return XfaScriptValue.FromBoolean(Compare(left, right) < 0);
                case ">":
                    return XfaScriptValue.FromBoolean(Compare(left, right) > 0);
                case "<=":
                    return XfaScriptValue.FromBoolean(Compare(left, right) <= 0);
                case ">=":
                    return XfaScriptValue.FromBoolean(Compare(left, right) >= 0);
                default:
                    return XfaScriptValue.Undefined;
            }
        }

        private static bool IsStringy(XfaScriptValue value)
        {
            // Node values (a field's rawValue) and string values force the +
            // operator into string concatenation, matching how form scripts
            // build sentences from literals and field values.
            return value.IsNode || value.IsString;
        }

        private static bool IsPlainString(XfaScriptValue value) => value.IsString;

        private static bool LooseEquals(XfaScriptValue left, XfaScriptValue right)
        {
            if (left.IsNode || right.IsNode)
            {
                return left.Equals(right);
            }

            double ln = left.ToNumber();
            double rn = right.ToNumber();
            if (!double.IsNaN(ln) && !double.IsNaN(rn))
            {
                return ln == rn;
            }

            return string.Equals(left.ToStringValue(), right.ToStringValue(), StringComparison.Ordinal);
        }

        private static int Compare(XfaScriptValue left, XfaScriptValue right)
        {
            double ln = left.ToNumber();
            double rn = right.ToNumber();
            if (!double.IsNaN(ln) && !double.IsNaN(rn))
            {
                return ln.CompareTo(rn);
            }

            return string.CompareOrdinal(left.ToStringValue(), right.ToStringValue());
        }

        private XfaScriptValue EvaluateLogical(JsLogical logical, JsScope scope)
        {
            XfaScriptValue left = Evaluate(logical.Left, scope);
            if (logical.Op == "&&")
            {
                return left.ToBoolean() ? Evaluate(logical.Right, scope) : left;
            }

            return left.ToBoolean() ? left : Evaluate(logical.Right, scope);
        }

        private XfaScriptValue EvaluateAssign(JsAssign assign, JsScope scope)
        {
            XfaScriptValue value = Evaluate(assign.Value, scope);

            if (assign.Op is "+=" or "-=")
            {
                XfaScriptValue current = Evaluate(assign.Target, scope);
                value = assign.Op == "+="
                    ? (IsPlainString(current) || IsPlainString(value)
                        ? XfaScriptValue.FromString(current.ToStringValue() + value.ToStringValue())
                        : XfaScriptValue.FromNumber(current.ToNumber() + value.ToNumber()))
                    : XfaScriptValue.FromNumber(current.ToNumber() - value.ToNumber());
            }

            AssignTo(assign.Target, value, scope);
            return value;
        }

        private void AssignTo(JsExpr target, XfaScriptValue value, JsScope scope)
        {
            switch (target)
            {
                case JsIdentifier identifier:
                    if (!scope.TrySet(identifier.Name, value))
                    {
                        scope.Declare(identifier.Name, value);
                    }

                    break;
                case JsMember member:
                    XfaScriptValue receiver = Evaluate(member.Target, scope);
                    if (receiver.IsNode)
                    {
                        XfaScriptHost.SetProperty(receiver.AsNode()!, member.Name, value.ToStringValue());
                    }

                    break;
                default:
                    throw new XfaScriptException("Unsupported assignment target.");
            }
        }

        // Builds a dotted path string from a chain of member accesses rooted at
        // an identifier (e.g. Certificate.CompanyName), or null when the chain
        // is not a pure identifier/member path.
        private static string? TryFlattenPath(JsExpr expr)
        {
            switch (expr)
            {
                case JsIdentifier identifier:
                    return identifier.Name;
                case JsMember member:
                    string? prefix = TryFlattenPath(member.Target);
                    return prefix is null ? null : prefix + "." + member.Name;
                default:
                    return null;
            }
        }
    }
}
