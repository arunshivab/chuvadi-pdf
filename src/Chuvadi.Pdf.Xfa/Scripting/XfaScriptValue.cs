// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: LA-23b Phase E — scripting.

using System;
using System.Globalization;
using Chuvadi.Pdf.Xfa.Model;

namespace Chuvadi.Pdf.Xfa.Scripting;

/// <summary>
/// A dynamic script value: a string, a number, a boolean, a node reference, or
/// null/undefined. Shared by the FormCalc and JavaScript engines. Coercions
/// follow the pragmatic rules XFA form scripts rely on.
/// </summary>
public readonly struct XfaScriptValue : IEquatable<XfaScriptValue>
{
    private readonly string? _string;
    private readonly double _number;
    private readonly bool _bool;
    private readonly XfaNode? _node;

    private XfaScriptValue(ValueKind kind, string? s, double n, bool b, XfaNode? node)
    {
        Kind = kind;
        _string = s;
        _number = n;
        _bool = b;
        _node = node;
    }

    private enum ValueKind
    {
        Undefined,
        String,
        Number,
        Boolean,
        Node,
    }

    /// <summary>Gets the undefined/null value.</summary>
    public static XfaScriptValue Undefined { get; } =
        new XfaScriptValue(ValueKind.Undefined, null, 0, false, null);

    /// <summary>Gets a value indicating whether this value is undefined.</summary>
    public bool IsUndefined => Kind == ValueKind.Undefined;

    /// <summary>Gets a value indicating whether this value is a node reference.</summary>
    public bool IsNode => Kind == ValueKind.Node;

    /// <summary>Gets a value indicating whether this value is a string.</summary>
    public bool IsString => Kind == ValueKind.String;

    private ValueKind Kind { get; }

    /// <summary>Creates a string value.</summary>
    /// <param name="value">The string.</param>
    /// <returns>The wrapped value.</returns>
    public static XfaScriptValue FromString(string value) =>
        new XfaScriptValue(ValueKind.String, value ?? string.Empty, 0, false, null);

    /// <summary>Creates a numeric value.</summary>
    /// <param name="value">The number.</param>
    /// <returns>The wrapped value.</returns>
    public static XfaScriptValue FromNumber(double value) =>
        new XfaScriptValue(ValueKind.Number, null, value, false, null);

    /// <summary>Creates a boolean value.</summary>
    /// <param name="value">The boolean.</param>
    /// <returns>The wrapped value.</returns>
    public static XfaScriptValue FromBoolean(bool value) =>
        new XfaScriptValue(ValueKind.Boolean, null, 0, value, null);

    /// <summary>Creates a node-reference value.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The wrapped value.</returns>
    public static XfaScriptValue FromNode(XfaNode node) =>
        new XfaScriptValue(ValueKind.Node, null, 0, false, node);

    /// <summary>Gets the referenced node, or null when this is not a node value.</summary>
    /// <returns>The node or null.</returns>
    public XfaNode? AsNode() => _node;

    /// <summary>Coerces this value to a string.</summary>
    /// <returns>The string form.</returns>
    public string ToStringValue() => Kind switch
    {
        ValueKind.String => _string ?? string.Empty,
        ValueKind.Number => NumberToString(_number),
        ValueKind.Boolean => _bool ? "true" : "false",
        ValueKind.Node => string.Empty,
        _ => string.Empty,
    };

    /// <summary>Coerces this value to a number (NaN when not numeric).</summary>
    /// <returns>The numeric form.</returns>
    public double ToNumber() => Kind switch
    {
        ValueKind.Number => _number,
        ValueKind.Boolean => _bool ? 1.0 : 0.0,
        ValueKind.String => ParseNumber(_string),
        _ => double.NaN,
    };

    /// <summary>Coerces this value to a boolean using JavaScript truthiness.</summary>
    /// <returns>The boolean form.</returns>
    public bool ToBoolean() => Kind switch
    {
        ValueKind.Boolean => _bool,
        ValueKind.Number => _number != 0.0 && !double.IsNaN(_number),
        ValueKind.String => !string.IsNullOrEmpty(_string),
        ValueKind.Node => _node is not null,
        _ => false,
    };

    private static double ParseNumber(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return double.NaN;
        }

        return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double v)
            ? v
            : double.NaN;
    }

    private static string NumberToString(double value)
    {
        if (double.IsNaN(value))
        {
            return "NaN";
        }

        // Integers render without a decimal point, matching JS String(n).
        if (value == Math.Floor(value) && !double.IsInfinity(value))
        {
            return ((long)value).ToString(CultureInfo.InvariantCulture);
        }

        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public bool Equals(XfaScriptValue other)
    {
        if (Kind != other.Kind)
        {
            return false;
        }

        return Kind switch
        {
            ValueKind.String => _string == other._string,
            ValueKind.Number => _number.Equals(other._number),
            ValueKind.Boolean => _bool == other._bool,
            ValueKind.Node => ReferenceEquals(_node, other._node),
            _ => true,
        };
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is XfaScriptValue other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Kind switch
    {
        ValueKind.String => _string?.GetHashCode(StringComparison.Ordinal) ?? 0,
        ValueKind.Number => _number.GetHashCode(),
        ValueKind.Boolean => _bool.GetHashCode(),
        ValueKind.Node => _node?.GetHashCode() ?? 0,
        _ => 0,
    };

    /// <summary>Compares two values for equality.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>True when equal.</returns>
    public static bool operator ==(XfaScriptValue left, XfaScriptValue right) => left.Equals(right);

    /// <summary>Compares two values for inequality.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>True when not equal.</returns>
    public static bool operator !=(XfaScriptValue left, XfaScriptValue right) => !left.Equals(right);
}
