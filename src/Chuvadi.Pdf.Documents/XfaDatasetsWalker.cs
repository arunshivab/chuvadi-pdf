// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  XFA 3.3 §A.2 — datasets packet (<xfa:datasets><xfa:data>...)
// PHASE: Document introspection — XFA data layer.
//
// A minimal, dependency-free XML walker for the XFA datasets packet. The library
// deliberately avoids System.Xml; this reader handles only what a datasets packet
// contains: nested elements, text leaves, self-closing tags, comments, CDATA, the
// XML declaration, and the common predefined / numeric character references. It
// does not implement namespaces, DTDs, or mixed content, none of which appear in
// a conformant <xfa:data> subtree.

using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Chuvadi.Pdf.Documents;

internal static class XfaDatasetsWalker
{
    internal readonly struct Leaf
    {
        internal Leaf(string path, string value)
        {
            NodePath = path;
            Value = value;
        }

        internal string NodePath { get; }

        internal string Value { get; }
    }

    private sealed class Node
    {
        internal string LocalName = string.Empty;
        internal string QualifiedName = string.Empty;
        internal Node? Parent;
        internal List<Node> Children = new List<Node>();
        internal StringBuilder Text = new StringBuilder();

        internal bool HasChildElements => Children.Count > 0;
    }

    internal static IReadOnlyList<Leaf> Walk(string xml)
    {
        if (string.IsNullOrEmpty(xml))
        {
            return System.Array.Empty<Leaf>();
        }

        Node root = Parse(xml);
        Node? data = FindDataRoot(root);

        if (data is null)
        {
            return System.Array.Empty<Leaf>();
        }

        List<Leaf> leaves = new List<Leaf>();

        for (int i = 0; i < data.Children.Count; i++)
        {
            Collect(data.Children[i], data.Children[i].LocalName, leaves);
        }

        return leaves;
    }

    private static void Collect(Node node, string path, List<Leaf> leaves)
    {
        if (!node.HasChildElements)
        {
            leaves.Add(new Leaf(path, node.Text.ToString().Trim()));
            return;
        }

        for (int i = 0; i < node.Children.Count; i++)
        {
            Node child = node.Children[i];
            Collect(child, path + "." + child.LocalName, leaves);
        }
    }

    private static Node? FindDataRoot(Node node)
    {
        for (int i = 0; i < node.Children.Count; i++)
        {
            Node child = node.Children[i];
            bool isData = string.Equals(child.LocalName, "data", System.StringComparison.Ordinal)
                && child.Parent is not null
                && string.Equals(child.Parent.LocalName, "datasets", System.StringComparison.Ordinal);

            if (isData)
            {
                return child;
            }

            Node? found = FindDataRoot(child);

            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static Node Parse(string xml)
    {
        Node root = new Node();
        Stack<Node> stack = new Stack<Node>();
        stack.Push(root);

        int i = 0;
        int n = xml.Length;

        if (n > 0 && xml[0] == '\uFEFF')
        {
            i = 1;
        }

        while (i < n)
        {
            char c = xml[i];

            if (c == '<')
            {
                if (Matches(xml, i, "<!--"))
                {
                    i = SkipUntil(xml, i + 4, "-->");
                    continue;
                }

                if (Matches(xml, i, "<![CDATA["))
                {
                    int end = IndexOf(xml, i + 9, "]]>");
                    int stop = end < 0 ? n : end;
                    stack.Peek().Text.Append(xml, i + 9, stop - (i + 9));
                    i = end < 0 ? n : end + 3;
                    continue;
                }

                if (Matches(xml, i, "<?"))
                {
                    i = SkipUntil(xml, i + 2, "?>");
                    continue;
                }

                if (Matches(xml, i, "<!"))
                {
                    i = SkipUntil(xml, i + 2, ">");
                    continue;
                }

                int tagEnd = xml.IndexOf('>', i + 1);

                if (tagEnd < 0)
                {
                    break;
                }

                string inner = xml.Substring(i + 1, tagEnd - (i + 1));

                if (inner.Length > 0 && inner[0] == '/')
                {
                    if (stack.Count > 1)
                    {
                        stack.Pop();
                    }
                }
                else
                {
                    bool selfClosing = inner.Length > 0 && inner[inner.Length - 1] == '/';
                    string body = selfClosing ? inner.Substring(0, inner.Length - 1) : inner;
                    string qualified = ReadName(body);

                    Node node = new Node
                    {
                        QualifiedName = qualified,
                        LocalName = LocalPart(qualified),
                        Parent = stack.Peek(),
                    };

                    stack.Peek().Children.Add(node);

                    if (!selfClosing)
                    {
                        stack.Push(node);
                    }
                }

                i = tagEnd + 1;
            }
            else
            {
                int next = xml.IndexOf('<', i);
                int stop = next < 0 ? n : next;
                AppendUnescaped(stack.Peek().Text, xml, i, stop);
                i = stop;
            }
        }

        return root;
    }

    private static string ReadName(string tagBody)
    {
        int start = 0;

        while (start < tagBody.Length && IsWhitespace(tagBody[start]))
        {
            start++;
        }

        int end = start;

        while (end < tagBody.Length && !IsWhitespace(tagBody[end]) && tagBody[end] != '/')
        {
            end++;
        }

        return tagBody.Substring(start, end - start);
    }

    private static string LocalPart(string qualified)
    {
        int colon = qualified.IndexOf(':');
        return colon < 0 ? qualified : qualified.Substring(colon + 1);
    }

    private static void AppendUnescaped(StringBuilder builder, string source, int start, int end)
    {
        int i = start;

        while (i < end)
        {
            char c = source[i];

            if (c == '&')
            {
                int semicolon = source.IndexOf(';', i + 1);

                if (semicolon > i && semicolon < end)
                {
                    string entity = source.Substring(i + 1, semicolon - (i + 1));

                    if (TryDecodeEntity(entity, out char decoded))
                    {
                        builder.Append(decoded);
                        i = semicolon + 1;
                        continue;
                    }
                }
            }

            builder.Append(c);
            i++;
        }
    }

    private static bool TryDecodeEntity(string entity, out char decoded)
    {
        switch (entity)
        {
            case "amp":
                decoded = '&';
                return true;
            case "lt":
                decoded = '<';
                return true;
            case "gt":
                decoded = '>';
                return true;
            case "quot":
                decoded = '"';
                return true;
            case "apos":
                decoded = '\'';
                return true;
            default:
                break;
        }

        if (entity.Length > 1 && entity[0] == '#')
        {
            bool hex = entity[1] == 'x' || entity[1] == 'X';
            string digits = hex ? entity.Substring(2) : entity.Substring(1);
            NumberStyles style = hex ? NumberStyles.HexNumber : NumberStyles.Integer;

            if (int.TryParse(digits, style, CultureInfo.InvariantCulture, out int code)
                && code >= 0
                && code <= char.MaxValue)
            {
                decoded = (char)code;
                return true;
            }
        }

        decoded = '\0';
        return false;
    }

    private static bool Matches(string source, int index, string token)
    {
        if (index + token.Length > source.Length)
        {
            return false;
        }

        return string.CompareOrdinal(source, index, token, 0, token.Length) == 0;
    }

    private static int IndexOf(string source, int start, string token)
    {
        return source.IndexOf(token, start, System.StringComparison.Ordinal);
    }

    private static int SkipUntil(string source, int start, string token)
    {
        int idx = source.IndexOf(token, start, System.StringComparison.Ordinal);
        return idx < 0 ? source.Length : idx + token.Length;
    }

    private static bool IsWhitespace(char c) => c == ' ' || c == '\t' || c == '\r' || c == '\n';
}
