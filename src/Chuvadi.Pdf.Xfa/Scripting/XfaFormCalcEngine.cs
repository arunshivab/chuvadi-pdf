// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  XFA 3.3 — FormCalc language (the default XFA script language).
// PHASE: LA-23b Phase E — FormCalc engine.
//
// Scope: expressions (arithmetic, the & string-concat operator, comparison,
// logical and/or/not, unary), if/then/elseif/else/endif, for/do/endfor,
// while/do/endwhile, assignment to SOM references, and the builtin functions
// XFA forms commonly use (Concat, Left, Right, Len, Substr, Upper, Lower,
// Sum, Avg, Min, Max, Round, Abs, At, Replace). Unsupported constructs raise
// XfaScriptException so the runner fails soft.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Chuvadi.Pdf.Xfa.Model;

namespace Chuvadi.Pdf.Xfa.Scripting;

/// <summary>
/// A FormCalc interpreter covering the language features XFA form scripts use.
/// Evaluates a script in the context of a <c>this</c> node against a
/// <see cref="XfaScriptHost"/>.
/// </summary>
public sealed class XfaFormCalcEngine
{
    private readonly XfaScriptHost _host;

    /// <summary>Initializes a new instance of the <see cref="XfaFormCalcEngine"/> class.</summary>
    /// <param name="host">The scripting host for SOM resolution.</param>
    /// <exception cref="ArgumentNullException"><paramref name="host"/> is null.</exception>
    public XfaFormCalcEngine(XfaScriptHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
    }

    /// <summary>Executes FormCalc source in the context of a node.</summary>
    /// <param name="source">The script source.</param>
    /// <param name="thisNode">The node bound to <c>this</c>.</param>
    /// <returns>The value of the final expression, coerced to a string.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    /// <exception cref="XfaScriptException">The script uses an unsupported construct.</exception>
    public string Execute(string source, XfaNode? thisNode)
    {
        ArgumentNullException.ThrowIfNull(source);

        List<FcToken> tokens = new FcLexer(source).Tokenize();
        List<FcNode> program = new FcParser(tokens).ParseProgram();
        FcInterpreter interpreter = new FcInterpreter(_host, thisNode);
        return interpreter.Run(program).ToStringValue();
    }

    // ── Lexer ─────────────────────────────────────────────────────────────────

    private enum FcKind
    {
        Identifier,
        Number,
        String,
        Operator,
        Keyword,
        Eof,
    }

    private readonly struct FcToken
    {
        internal FcToken(FcKind kind, string text)
        {
            Kind = kind;
            Text = text;
        }

        internal FcKind Kind { get; }

        internal string Text { get; }
    }

    private static readonly HashSet<string> FcKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "if", "then", "elseif", "else", "endif", "for", "do", "endfor",
        "while", "endwhile", "and", "or", "not", "null", "upto", "downto", "step",
    };

    private sealed class FcLexer
    {
        private readonly string _s;
        private int _i;

        internal FcLexer(string source)
        {
            _s = source;
        }

        internal List<FcToken> Tokenize()
        {
            List<FcToken> tokens = new List<FcToken>();
            while (_i < _s.Length)
            {
                char c = _s[_i];
                if (char.IsWhiteSpace(c))
                {
                    _i++;
                    continue;
                }

                if (c == ';')
                {
                    // FormCalc statement separator: treat as a newline-like break.
                    _i++;
                    tokens.Add(new FcToken(FcKind.Operator, ";"));
                    continue;
                }

                if (c == '"')
                {
                    tokens.Add(new FcToken(FcKind.String, ReadString()));
                    continue;
                }

                if (char.IsDigit(c) || (c == '.' && Peek(1) is >= '0' and <= '9'))
                {
                    tokens.Add(new FcToken(FcKind.Number, ReadNumber()));
                    continue;
                }

                if (char.IsLetter(c) || c == '_' || c == '$')
                {
                    string ident = ReadIdentifier();
                    FcKind kind = FcKeywords.Contains(ident) ? FcKind.Keyword : FcKind.Identifier;
                    tokens.Add(new FcToken(kind, ident));
                    continue;
                }

                tokens.Add(new FcToken(FcKind.Operator, ReadOperator()));
            }

            tokens.Add(new FcToken(FcKind.Eof, string.Empty));
            return tokens;
        }

        private char Peek(int ahead) => _i + ahead < _s.Length ? _s[_i + ahead] : '\0';

        private string ReadString()
        {
            StringBuilder sb = new StringBuilder();
            _i++;
            while (_i < _s.Length && _s[_i] != '"')
            {
                // FormCalc escapes a quote by doubling it.
                if (_s[_i] == '"' && Peek(1) == '"')
                {
                    sb.Append('"');
                    _i += 2;
                    continue;
                }

                sb.Append(_s[_i]);
                _i++;
            }

            _i++;
            return sb.ToString();
        }

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
            while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '_' || _s[_i] == '$' || _s[_i] == '!'))
            {
                _i++;
            }

            return _s.Substring(start, _i - start);
        }

        private string ReadOperator()
        {
            foreach (string op in new[] { "<>", "<=", ">=", "==" })
            {
                if (Matches(op))
                {
                    _i += op.Length;
                    return op;
                }
            }

            char c = _s[_i];
            _i++;
            return c.ToString();
        }

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

    private abstract class FcNode
    {
    }

    private sealed class FcLiteral : FcNode
    {
        internal XfaScriptValue Value { get; init; }
    }

    private sealed class FcRef : FcNode
    {
        internal string Reference { get; init; } = string.Empty;
    }

    private sealed class FcCall : FcNode
    {
        internal string Name { get; init; } = string.Empty;

        internal List<FcNode> Args { get; init; } = new List<FcNode>();
    }

    private sealed class FcUnary : FcNode
    {
        internal string Op { get; init; } = string.Empty;

        internal FcNode Operand { get; init; } = default!;
    }

    private sealed class FcBinary : FcNode
    {
        internal string Op { get; init; } = string.Empty;

        internal FcNode Left { get; init; } = default!;

        internal FcNode Right { get; init; } = default!;
    }

    private sealed class FcAssign : FcNode
    {
        internal FcRef Target { get; init; } = default!;

        internal FcNode Value { get; init; } = default!;
    }

    private sealed class FcIf : FcNode
    {
        internal FcNode Condition { get; init; } = default!;

        internal List<FcNode> Then { get; init; } = new List<FcNode>();

        internal List<FcNode> Else { get; init; } = new List<FcNode>();
    }

    private sealed class FcFor : FcNode
    {
        internal string Var { get; init; } = string.Empty;

        internal FcNode From { get; init; } = default!;

        internal FcNode To { get; init; } = default!;

        internal bool Down { get; init; }

        internal List<FcNode> Body { get; init; } = new List<FcNode>();
    }

    private sealed class FcWhile : FcNode
    {
        internal FcNode Condition { get; init; } = default!;

        internal List<FcNode> Body { get; init; } = new List<FcNode>();
    }

    // ── Parser ─────────────────────────────────────────────────────────────────

    private sealed class FcParser
    {
        private readonly List<FcToken> _tokens;
        private int _i;

        internal FcParser(List<FcToken> tokens)
        {
            _tokens = tokens;
        }

        internal List<FcNode> ParseProgram()
        {
            List<FcNode> nodes = new List<FcNode>();
            while (!IsEof)
            {
                SkipSeparators();
                if (IsEof)
                {
                    break;
                }

                nodes.Add(ParseStatement());
            }

            return nodes;
        }

        private FcToken Current => _tokens[_i];

        private bool IsEof => Current.Kind == FcKind.Eof;

        private FcToken Advance() => _tokens[_i++];

        private bool CheckKeyword(string kw) =>
            Current.Kind == FcKind.Keyword && string.Equals(Current.Text, kw, StringComparison.OrdinalIgnoreCase);

        private bool CheckOp(string op) => Current.Kind == FcKind.Operator && Current.Text == op;

        private void SkipSeparators()
        {
            while (CheckOp(";"))
            {
                Advance();
            }
        }

        private void ExpectKeyword(string kw)
        {
            if (!CheckKeyword(kw))
            {
                throw new XfaScriptException($"Expected '{kw}' but found '{Current.Text}'.");
            }

            Advance();
        }

        private FcNode ParseStatement()
        {
            if (CheckKeyword("if"))
            {
                return ParseIf();
            }

            if (CheckKeyword("for"))
            {
                return ParseFor();
            }

            if (CheckKeyword("while"))
            {
                return ParseWhile();
            }

            // Assignment: <ref> = <expr>. Detect by lookahead for a ref followed
            // by a single '=' (FormCalc uses '==' for equality).
            int save = _i;
            if (Current.Kind == FcKind.Identifier || Current.Text == "$")
            {
                FcNode target = ParseUnary();
                if (target is FcRef refTarget && CheckOp("="))
                {
                    Advance();
                    FcNode value = ParseRhs();
                    return new FcAssign { Target = refTarget, Value = value };
                }

                _i = save;
            }

            return ParseExpression();
        }

        private FcNode ParseRhs()
        {
            // An assignment value may be an if/for/while expression (FormCalc
            // treats these as value-producing) or an ordinary expression.
            if (CheckKeyword("if"))
            {
                return ParseIf();
            }

            if (CheckKeyword("for"))
            {
                return ParseFor();
            }

            if (CheckKeyword("while"))
            {
                return ParseWhile();
            }

            return ParseExpression();
        }

        private FcIf ParseIf()
        {
            ExpectKeyword("if");
            FcNode condition = ParseExpression();
            ExpectKeyword("then");

            List<FcNode> then = new List<FcNode>();
            while (!CheckKeyword("elseif") && !CheckKeyword("else") && !CheckKeyword("endif") && !IsEof)
            {
                SkipSeparators();
                if (CheckKeyword("elseif") || CheckKeyword("else") || CheckKeyword("endif"))
                {
                    break;
                }

                then.Add(ParseStatement());
            }

            List<FcNode> els = new List<FcNode>();
            if (CheckKeyword("elseif"))
            {
                els.Add(ParseIf());
                return new FcIf { Condition = condition, Then = then, Else = els };
            }

            if (CheckKeyword("else"))
            {
                Advance();
                while (!CheckKeyword("endif") && !IsEof)
                {
                    SkipSeparators();
                    if (CheckKeyword("endif"))
                    {
                        break;
                    }

                    els.Add(ParseStatement());
                }
            }

            if (CheckKeyword("endif"))
            {
                Advance();
            }

            return new FcIf { Condition = condition, Then = then, Else = els };
        }

        private FcFor ParseFor()
        {
            ExpectKeyword("for");
            string varName = Advance().Text;
            if (!CheckOp("="))
            {
                throw new XfaScriptException("Expected '=' in for loop.");
            }

            Advance();
            FcNode from = ParseExpression();
            bool down = CheckKeyword("downto");
            if (!CheckKeyword("upto") && !CheckKeyword("downto"))
            {
                throw new XfaScriptException("Expected 'upto' or 'downto' in for loop.");
            }

            Advance();
            FcNode to = ParseExpression();
            ExpectKeyword("do");

            List<FcNode> body = new List<FcNode>();
            while (!CheckKeyword("endfor") && !IsEof)
            {
                SkipSeparators();
                if (CheckKeyword("endfor"))
                {
                    break;
                }

                body.Add(ParseStatement());
            }

            ExpectKeyword("endfor");
            return new FcFor { Var = varName, From = from, To = to, Down = down, Body = body };
        }

        private FcWhile ParseWhile()
        {
            ExpectKeyword("while");
            FcNode condition = ParseExpression();
            ExpectKeyword("do");
            List<FcNode> body = new List<FcNode>();
            while (!CheckKeyword("endwhile") && !IsEof)
            {
                SkipSeparators();
                if (CheckKeyword("endwhile"))
                {
                    break;
                }

                body.Add(ParseStatement());
            }

            ExpectKeyword("endwhile");
            return new FcWhile { Condition = condition, Body = body };
        }

        private FcNode ParseExpression() => ParseOr();

        private FcNode ParseOr()
        {
            FcNode left = ParseAnd();
            while (CheckKeyword("or") || CheckOp("|"))
            {
                Advance();
                FcNode right = ParseAnd();
                left = new FcBinary { Op = "or", Left = left, Right = right };
            }

            return left;
        }

        private FcNode ParseAnd()
        {
            FcNode left = ParseEquality();
            while (CheckKeyword("and") || CheckOp("&") && false)
            {
                Advance();
                FcNode right = ParseEquality();
                left = new FcBinary { Op = "and", Left = left, Right = right };
            }

            return left;
        }

        private FcNode ParseEquality()
        {
            FcNode left = ParseComparison();
            while (CheckOp("==") || CheckOp("<>"))
            {
                string op = Advance().Text;
                FcNode right = ParseComparison();
                left = new FcBinary { Op = op, Left = left, Right = right };
            }

            return left;
        }

        private FcNode ParseComparison()
        {
            FcNode left = ParseConcat();
            while (CheckOp("<") || CheckOp(">") || CheckOp("<=") || CheckOp(">="))
            {
                string op = Advance().Text;
                FcNode right = ParseConcat();
                left = new FcBinary { Op = op, Left = left, Right = right };
            }

            return left;
        }

        private FcNode ParseConcat()
        {
            FcNode left = ParseAdditive();
            while (CheckOp("&"))
            {
                Advance();
                FcNode right = ParseAdditive();
                left = new FcBinary { Op = "&", Left = left, Right = right };
            }

            return left;
        }

        private FcNode ParseAdditive()
        {
            FcNode left = ParseMultiplicative();
            while (CheckOp("+") || CheckOp("-"))
            {
                string op = Advance().Text;
                FcNode right = ParseMultiplicative();
                left = new FcBinary { Op = op, Left = left, Right = right };
            }

            return left;
        }

        private FcNode ParseMultiplicative()
        {
            FcNode left = ParseUnary();
            while (CheckOp("*") || CheckOp("/"))
            {
                string op = Advance().Text;
                FcNode right = ParseUnary();
                left = new FcBinary { Op = op, Left = left, Right = right };
            }

            return left;
        }

        private FcNode ParseUnary()
        {
            if (CheckKeyword("not") || CheckOp("-") || CheckOp("+"))
            {
                string op = NormalizeUnaryOp(Advance().Text);
                return new FcUnary { Op = op, Operand = ParseUnary() };
            }

            return ParsePrimary();
        }

        private static string NormalizeUnaryOp(string text) =>
            string.Equals(text, "not", StringComparison.OrdinalIgnoreCase) ? "not" : text;

        private FcNode ParsePrimary()
        {
            FcToken token = Current;

            switch (token.Kind)
            {
                case FcKind.Number:
                    Advance();
                    return new FcLiteral
                    {
                        Value = XfaScriptValue.FromNumber(
                            double.Parse(token.Text, NumberStyles.Any, CultureInfo.InvariantCulture)),
                    };
                case FcKind.String:
                    Advance();
                    return new FcLiteral { Value = XfaScriptValue.FromString(token.Text) };
                case FcKind.Keyword when string.Equals(token.Text, "null", StringComparison.OrdinalIgnoreCase):
                    Advance();
                    return new FcLiteral { Value = XfaScriptValue.Undefined };
                case FcKind.Identifier:
                    return ParseIdentifierOrCall();
                case FcKind.Operator when token.Text == "(":
                    Advance();
                    FcNode inner = ParseExpression();
                    if (!CheckOp(")"))
                    {
                        throw new XfaScriptException("Expected ')'.");
                    }

                    Advance();
                    return inner;
                default:
                    throw new XfaScriptException($"Unexpected token '{token.Text}'.");
            }
        }

        private FcNode ParseIdentifierOrCall()
        {
            StringBuilder path = new StringBuilder(Advance().Text);

            // A call: identifier immediately followed by '('.
            if (CheckOp("("))
            {
                return new FcCall { Name = path.ToString(), Args = ParseArguments() };
            }

            // A dotted SOM reference path.
            while (CheckOp(".") || CheckOp("!"))
            {
                Advance();
                path.Append('.');
                path.Append(Advance().Text);
            }

            return new FcRef { Reference = path.ToString() };
        }

        private List<FcNode> ParseArguments()
        {
            Advance(); // (
            List<FcNode> args = new List<FcNode>();
            if (!CheckOp(")"))
            {
                do
                {
                    args.Add(ParseExpression());
                }
                while (CheckOp(",") && Consume());
            }

            if (!CheckOp(")"))
            {
                throw new XfaScriptException("Expected ')' in argument list.");
            }

            Advance();
            return args;
        }

        private bool Consume()
        {
            Advance();
            return true;
        }
    }

    // ── Interpreter ────────────────────────────────────────────────────────────

    private sealed class FcInterpreter
    {
        private const int MaxIterations = 100000;

        private readonly XfaScriptHost _host;
        private readonly XfaNode? _thisNode;
        private readonly Dictionary<string, XfaScriptValue> _locals =
            new Dictionary<string, XfaScriptValue>(StringComparer.Ordinal);

        internal FcInterpreter(XfaScriptHost host, XfaNode? thisNode)
        {
            _host = host;
            _thisNode = thisNode;
        }

        internal XfaScriptValue Run(List<FcNode> program)
        {
            XfaScriptValue result = XfaScriptValue.Undefined;
            foreach (FcNode node in program)
            {
                result = Evaluate(node);
            }

            return result;
        }

        private XfaScriptValue Evaluate(FcNode node)
        {
            switch (node)
            {
                case FcLiteral literal:
                    return literal.Value;
                case FcRef reference:
                    return EvaluateRef(reference);
                case FcCall call:
                    return EvaluateCall(call);
                case FcUnary unary:
                    return EvaluateUnary(unary);
                case FcBinary binary:
                    return EvaluateBinary(binary);
                case FcAssign assign:
                    return EvaluateAssign(assign);
                case FcIf ifNode:
                    return EvaluateIf(ifNode);
                case FcFor forNode:
                    return EvaluateFor(forNode);
                case FcWhile whileNode:
                    return EvaluateWhile(whileNode);
                default:
                    return XfaScriptValue.Undefined;
            }
        }

        private XfaScriptValue EvaluateRef(FcRef reference)
        {
            if (_locals.TryGetValue(reference.Reference, out XfaScriptValue local))
            {
                return local;
            }

            // Split a trailing property (rawValue/value) from the node path.
            (string nodePath, string property) = SplitProperty(reference.Reference);
            XfaNode? node = _host.Resolve(nodePath, _thisNode);
            if (node is null)
            {
                return XfaScriptValue.Undefined;
            }

            return XfaScriptValue.FromString(XfaScriptHost.GetProperty(node, property));
        }

        private static (string NodePath, string Property) SplitProperty(string path)
        {
            foreach (string prop in new[] { "rawValue", "value", "presence", "name", "text" })
            {
                if (path.EndsWith("." + prop, StringComparison.Ordinal))
                {
                    return (path.Substring(0, path.Length - prop.Length - 1), prop);
                }
            }

            // No explicit property: FormCalc reads a field's value by default.
            return (path, "value");
        }

        private XfaScriptValue EvaluateCall(FcCall call)
        {
            List<XfaScriptValue> args = new List<XfaScriptValue>();
            foreach (FcNode arg in call.Args)
            {
                args.Add(Evaluate(arg));
            }

            string name = call.Name.ToUpperInvariant();
            return name switch
            {
                "CONCAT" => XfaScriptValue.FromString(ConcatArgs(args)),
                "LEFT" => XfaScriptValue.FromString(Left(args)),
                "RIGHT" => XfaScriptValue.FromString(Right(args)),
                "LEN" => XfaScriptValue.FromNumber(args.Count > 0 ? args[0].ToStringValue().Length : 0),
                "SUBSTR" => XfaScriptValue.FromString(Substr(args)),
                "UPPER" => XfaScriptValue.FromString(args.Count > 0 ? args[0].ToStringValue().ToUpperInvariant() : string.Empty),
#pragma warning disable CA1308 // FormCalc Lower() requires lowercase output
                "LOWER" => XfaScriptValue.FromString(args.Count > 0 ? args[0].ToStringValue().ToLowerInvariant() : string.Empty),
#pragma warning restore CA1308
                "SUM" => XfaScriptValue.FromNumber(Sum(args)),
                "AVG" => XfaScriptValue.FromNumber(args.Count > 0 ? Sum(args) / args.Count : 0),
                "MIN" => XfaScriptValue.FromNumber(MinMax(args, min: true)),
                "MAX" => XfaScriptValue.FromNumber(MinMax(args, min: false)),
                "ROUND" => XfaScriptValue.FromNumber(Round(args)),
                "ABS" => XfaScriptValue.FromNumber(args.Count > 0 ? Math.Abs(args[0].ToNumber()) : 0),
                "AT" => XfaScriptValue.FromNumber(At(args)),
                "REPLACE" => XfaScriptValue.FromString(Replace(args)),
                "STUFF" => XfaScriptValue.FromString(Stuff(args)),
                "SPACE" => XfaScriptValue.FromString(args.Count > 0 ? new string(' ', Math.Max(0, (int)args[0].ToNumber())) : string.Empty),
                _ => throw new XfaScriptException($"Unknown FormCalc function '{call.Name}'."),
            };
        }

        private static string ConcatArgs(List<XfaScriptValue> args)
        {
            StringBuilder sb = new StringBuilder();
            foreach (XfaScriptValue arg in args)
            {
                sb.Append(arg.ToStringValue());
            }

            return sb.ToString();
        }

        private static string Left(List<XfaScriptValue> args)
        {
            if (args.Count < 2)
            {
                return args.Count == 1 ? args[0].ToStringValue() : string.Empty;
            }

            string s = args[0].ToStringValue();
            int n = Math.Max(0, Math.Min((int)args[1].ToNumber(), s.Length));
            return s.Substring(0, n);
        }

        private static string Right(List<XfaScriptValue> args)
        {
            if (args.Count < 2)
            {
                return args.Count == 1 ? args[0].ToStringValue() : string.Empty;
            }

            string s = args[0].ToStringValue();
            int n = Math.Max(0, Math.Min((int)args[1].ToNumber(), s.Length));
            return s.Substring(s.Length - n);
        }

        private static string Substr(List<XfaScriptValue> args)
        {
            if (args.Count < 3)
            {
                return string.Empty;
            }

            string s = args[0].ToStringValue();
            int start = Math.Max(1, (int)args[1].ToNumber());
            int count = Math.Max(0, (int)args[2].ToNumber());
            int zeroBased = start - 1;
            if (zeroBased >= s.Length)
            {
                return string.Empty;
            }

            count = Math.Min(count, s.Length - zeroBased);
            return s.Substring(zeroBased, count);
        }

        private static double Sum(List<XfaScriptValue> args)
        {
            double total = 0;
            foreach (XfaScriptValue arg in args)
            {
                double v = arg.ToNumber();
                if (!double.IsNaN(v))
                {
                    total += v;
                }
            }

            return total;
        }

        private static double MinMax(List<XfaScriptValue> args, bool min)
        {
            bool any = false;
            double result = min ? double.MaxValue : double.MinValue;
            foreach (XfaScriptValue arg in args)
            {
                double v = arg.ToNumber();
                if (double.IsNaN(v))
                {
                    continue;
                }

                any = true;
                result = min ? Math.Min(result, v) : Math.Max(result, v);
            }

            return any ? result : 0;
        }

        private static double Round(List<XfaScriptValue> args)
        {
            if (args.Count == 0)
            {
                return 0;
            }

            double value = args[0].ToNumber();
            int digits = args.Count > 1 ? Math.Max(0, (int)args[1].ToNumber()) : 0;
            return Math.Round(value, digits, MidpointRounding.AwayFromZero);
        }

        private static double At(List<XfaScriptValue> args)
        {
            if (args.Count < 2)
            {
                return 0;
            }

            // FormCalc At(string, target) is 1-based; 0 when not found.
            string s = args[0].ToStringValue();
            string target = args[1].ToStringValue();
            int index = s.IndexOf(target, StringComparison.Ordinal);
            return index < 0 ? 0 : index + 1;
        }

        private static string Replace(List<XfaScriptValue> args)
        {
            if (args.Count < 3)
            {
                return args.Count > 0 ? args[0].ToStringValue() : string.Empty;
            }

            return args[0].ToStringValue().Replace(
                args[1].ToStringValue(), args[2].ToStringValue(), StringComparison.Ordinal);
        }

        private static string Stuff(List<XfaScriptValue> args)
        {
            if (args.Count < 4)
            {
                return args.Count > 0 ? args[0].ToStringValue() : string.Empty;
            }

            string s = args[0].ToStringValue();
            int start = Math.Max(1, (int)args[1].ToNumber()) - 1;
            int length = Math.Max(0, (int)args[2].ToNumber());
            string insert = args[3].ToStringValue();
            if (start > s.Length)
            {
                start = s.Length;
            }

            length = Math.Min(length, s.Length - start);
            return string.Concat(s.AsSpan(0, start), insert, s.AsSpan(start + length));
        }

        private XfaScriptValue EvaluateUnary(FcUnary unary)
        {
            XfaScriptValue operand = Evaluate(unary.Operand);
            return unary.Op switch
            {
                "not" => XfaScriptValue.FromBoolean(!operand.ToBoolean()),
                "-" => XfaScriptValue.FromNumber(-operand.ToNumber()),
                "+" => XfaScriptValue.FromNumber(operand.ToNumber()),
                _ => XfaScriptValue.Undefined,
            };
        }

        private XfaScriptValue EvaluateBinary(FcBinary binary)
        {
            if (binary.Op == "and")
            {
                return XfaScriptValue.FromBoolean(Evaluate(binary.Left).ToBoolean() && Evaluate(binary.Right).ToBoolean());
            }

            if (binary.Op == "or")
            {
                return XfaScriptValue.FromBoolean(Evaluate(binary.Left).ToBoolean() || Evaluate(binary.Right).ToBoolean());
            }

            XfaScriptValue left = Evaluate(binary.Left);
            XfaScriptValue right = Evaluate(binary.Right);

            return binary.Op switch
            {
                "&" => XfaScriptValue.FromString(left.ToStringValue() + right.ToStringValue()),
                "+" => XfaScriptValue.FromNumber(left.ToNumber() + right.ToNumber()),
                "-" => XfaScriptValue.FromNumber(left.ToNumber() - right.ToNumber()),
                "*" => XfaScriptValue.FromNumber(left.ToNumber() * right.ToNumber()),
                "/" => XfaScriptValue.FromNumber(left.ToNumber() / right.ToNumber()),
                "==" => XfaScriptValue.FromBoolean(Equal(left, right)),
                "<>" => XfaScriptValue.FromBoolean(!Equal(left, right)),
                "<" => XfaScriptValue.FromBoolean(CompareValues(left, right) < 0),
                ">" => XfaScriptValue.FromBoolean(CompareValues(left, right) > 0),
                "<=" => XfaScriptValue.FromBoolean(CompareValues(left, right) <= 0),
                ">=" => XfaScriptValue.FromBoolean(CompareValues(left, right) >= 0),
                _ => XfaScriptValue.Undefined,
            };
        }

        private static bool Equal(XfaScriptValue left, XfaScriptValue right)
        {
            double ln = left.ToNumber();
            double rn = right.ToNumber();
            if (!double.IsNaN(ln) && !double.IsNaN(rn))
            {
                return ln == rn;
            }

            return string.Equals(left.ToStringValue(), right.ToStringValue(), StringComparison.Ordinal);
        }

        private static int CompareValues(XfaScriptValue left, XfaScriptValue right)
        {
            double ln = left.ToNumber();
            double rn = right.ToNumber();
            if (!double.IsNaN(ln) && !double.IsNaN(rn))
            {
                return ln.CompareTo(rn);
            }

            return string.CompareOrdinal(left.ToStringValue(), right.ToStringValue());
        }

        private XfaScriptValue EvaluateAssign(FcAssign assign)
        {
            XfaScriptValue value = Evaluate(assign.Value);
            (string nodePath, string property) = SplitProperty(assign.Target.Reference);
            XfaNode? node = _host.Resolve(nodePath, _thisNode);
            if (node is not null)
            {
                XfaScriptHost.SetProperty(node, property, value.ToStringValue());
            }
            else
            {
                // No such node: treat as a local variable assignment.
                _locals[assign.Target.Reference] = value;
            }

            return value;
        }

        private XfaScriptValue EvaluateIf(FcIf ifNode)
        {
            if (Evaluate(ifNode.Condition).ToBoolean())
            {
                return RunBlock(ifNode.Then);
            }

            return RunBlock(ifNode.Else);
        }

        private XfaScriptValue EvaluateFor(FcFor forNode)
        {
            double from = Evaluate(forNode.From).ToNumber();
            double to = Evaluate(forNode.To).ToNumber();
            XfaScriptValue result = XfaScriptValue.Undefined;
            int guard = 0;

            if (forNode.Down)
            {
                for (double i = from; i >= to; i -= 1)
                {
                    if (++guard > MaxIterations)
                    {
                        throw new XfaScriptException("Loop iteration limit exceeded.");
                    }

                    _locals[forNode.Var] = XfaScriptValue.FromNumber(i);
                    result = RunBlock(forNode.Body);
                }
            }
            else
            {
                for (double i = from; i <= to; i += 1)
                {
                    if (++guard > MaxIterations)
                    {
                        throw new XfaScriptException("Loop iteration limit exceeded.");
                    }

                    _locals[forNode.Var] = XfaScriptValue.FromNumber(i);
                    result = RunBlock(forNode.Body);
                }
            }

            return result;
        }

        private XfaScriptValue EvaluateWhile(FcWhile whileNode)
        {
            XfaScriptValue result = XfaScriptValue.Undefined;
            int guard = 0;
            while (Evaluate(whileNode.Condition).ToBoolean())
            {
                if (++guard > MaxIterations)
                {
                    throw new XfaScriptException("Loop iteration limit exceeded.");
                }

                result = RunBlock(whileNode.Body);
            }

            return result;
        }

        private XfaScriptValue RunBlock(List<FcNode> block)
        {
            XfaScriptValue result = XfaScriptValue.Undefined;
            foreach (FcNode node in block)
            {
                result = Evaluate(node);
            }

            return result;
        }
    }
}
