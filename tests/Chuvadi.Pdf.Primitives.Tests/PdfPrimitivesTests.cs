// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Primitives.Tests;

public sealed class PdfObjectIdTests
{
    [Fact]
    public void Invalid_HasObjectNumberZero()
    {
        PdfObjectId.Invalid.ObjectNumber.Should().Be(0);
        PdfObjectId.Invalid.IsValid.Should().BeFalse();
    }

    [Fact]
    public void IsValid_ReturnsTrueWhenObjectNumberPositive()
    {
        var id = new PdfObjectId(1, 0);
        id.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ToString_ReturnsCanonicalForm()
    {
        var id = new PdfObjectId(12, 0);
        id.ToString().Should().Be("12 0 R");
    }

    [Fact]
    public void Parse_ValidReference_Succeeds()
    {
        var id = PdfObjectId.Parse("12 0 R");
        id.ObjectNumber.Should().Be(12);
        id.Generation.Should().Be(0);
    }

    [Fact]
    public void Parse_WithGeneration_Succeeds()
    {
        var id = PdfObjectId.Parse("5 3 R");
        id.ObjectNumber.Should().Be(5);
        id.Generation.Should().Be(3);
    }

    [Fact]
    public void Parse_InvalidFormat_ThrowsFormatException()
    {
        Action act = () => PdfObjectId.Parse("not a ref");
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void TryParse_ValidReference_ReturnsTrueAndResult()
    {
        bool success = PdfObjectId.TryParse("7 0 R", out PdfObjectId id);
        success.Should().BeTrue();
        id.ObjectNumber.Should().Be(7);
    }

    [Fact]
    public void TryParse_InvalidReference_ReturnsFalse()
    {
        bool success = PdfObjectId.TryParse("garbage", out PdfObjectId id);
        success.Should().BeFalse();
        id.Should().Be(PdfObjectId.Invalid);
    }

    [Fact]
    public void CompareTo_OrdersByObjectNumberFirst()
    {
        var a = new PdfObjectId(1, 0);
        var b = new PdfObjectId(2, 0);
        a.CompareTo(b).Should().BeNegative();
    }

    [Fact]
    public void CompareTo_SameObjectNumber_OrdersByGeneration()
    {
        var a = new PdfObjectId(5, 0);
        var b = new PdfObjectId(5, 1);
        a.CompareTo(b).Should().BeNegative();
    }
}

public sealed class PdfNullTests
{
    [Fact]
    public void Value_IsSingleton()
    {
        PdfNull.Value.Should().BeSameAs(PdfNull.Value);
    }

    [Fact]
    public void PrimitiveType_IsNull()
    {
        PdfNull.Value.PrimitiveType.Should().Be(PdfPrimitiveType.Null);
    }

    [Fact]
    public void IsNull_ReturnsTrue()
    {
        PdfNull.Value.IsNull.Should().BeTrue();
    }

    [Fact]
    public void ToString_ReturnsNullKeyword()
    {
        PdfNull.Value.ToString().Should().Be("null");
    }
}

public sealed class PdfBooleanTests
{
    [Fact]
    public void True_IsSingleton()
    {
        PdfBoolean.True.Should().BeSameAs(PdfBoolean.True);
    }

    [Fact]
    public void FromBool_ReturnsCorrectSingleton()
    {
        PdfBoolean.FromBool(true).Should().BeSameAs(PdfBoolean.True);
        PdfBoolean.FromBool(false).Should().BeSameAs(PdfBoolean.False);
    }

    [Fact]
    public void ToString_ReturnsPdfKeywords()
    {
        PdfBoolean.True.ToString().Should().Be("true");
        PdfBoolean.False.ToString().Should().Be("false");
    }

    [Fact]
    public void ImplicitConversion_FromBool()
    {
        PdfBoolean b = true;
        b.Should().BeSameAs(PdfBoolean.True);
    }

    [Fact]
    public void ImplicitConversion_ToBool()
    {
        bool b = PdfBoolean.True;
        b.Should().BeTrue();
    }
}

public sealed class PdfIntegerTests
{
    [Fact]
    public void Value_RoundTrips()
    {
        var i = new PdfInteger(42);
        i.Value.Should().Be(42);
    }

    [Fact]
    public void ToString_UsesInvariantCulture()
    {
        var i = new PdfInteger(1000);
        i.ToString().Should().Be("1000");
    }

    [Fact]
    public void NegativeValue_SerializesCorrectly()
    {
        var i = new PdfInteger(-7);
        i.ToString().Should().Be("-7");
    }

    [Fact]
    public void ImplicitConversion_FromInt()
    {
        PdfInteger i = 99;
        i.Value.Should().Be(99);
    }

    [Fact]
    public void ToReal_ConvertsCorrectly()
    {
        var i = new PdfInteger(3);
        i.ToReal().Value.Should().Be(3.0);
    }
}

public sealed class PdfRealTests
{
    [Fact]
    public void Value_RoundTrips()
    {
        var r = new PdfReal(3.14);
        r.Value.Should().BeApproximately(3.14, 1e-9);
    }

    [Fact]
    public void ToString_UsesInvariantCulture()
    {
        var r = new PdfReal(1.5);
        r.ToString().Should().Be("1.5");
    }

    [Fact]
    public void ToDouble_AcceptsInteger()
    {
        PdfInteger i = new(7);
        PdfReal.ToDouble(i).Should().Be(7.0);
    }

    [Fact]
    public void ToDouble_AcceptsReal()
    {
        PdfReal r = new(2.5);
        PdfReal.ToDouble(r).Should().Be(2.5);
    }

    [Fact]
    public void ToDouble_ThrowsForNonNumeric()
    {
        Action act = () => PdfReal.ToDouble(PdfNull.Value);
        act.Should().Throw<InvalidCastException>();
    }
}

public sealed class PdfNameTests
{
    [Fact]
    public void Intern_SameValue_ReturnsSameInstance()
    {
        PdfName a = PdfName.Intern("FlateDecode");
        PdfName b = PdfName.Intern("FlateDecode");
        a.Should().BeSameAs(b);
    }

    [Fact]
    public void Intern_DifferentValues_ReturnsDifferentInstances()
    {
        PdfName a = PdfName.Intern("Type");
        PdfName b = PdfName.Intern("Subtype");
        a.Should().NotBeSameAs(b);
    }

    [Fact]
    public void Equality_UsesReferenceEquality()
    {
        PdfName a = PdfName.Intern("Font");
        PdfName b = PdfName.Intern("Font");
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void ToString_IncludesLeadingSolidus()
    {
        PdfName.Type.ToString().Should().Be("/Type");
    }

    [Fact]
    public void ToString_EncodesSpecialCharacters()
    {
        PdfName name = PdfName.Intern("Hello World");
        name.ToString().Should().Be("/Hello#20World");
    }

    [Fact]
    public void FromRawBytes_NoEscapes_DecodesCorrectly()
    {
        byte[] bytes = "FlateDecode"u8.ToArray();
        PdfName name = PdfName.FromRawBytes(bytes);
        name.Value.Should().Be("FlateDecode");
    }

    [Fact]
    public void FromRawBytes_WithEscapes_DecodesCorrectly()
    {
        byte[] bytes = "Hello#20World"u8.ToArray();
        PdfName name = PdfName.FromRawBytes(bytes);
        name.Value.Should().Be("Hello World");
    }

    [Fact]
    public void ImplicitConversion_FromString()
    {
        PdfName name = "Type";
        name.Should().BeSameAs(PdfName.Type);
    }

    [Fact]
    public void Intern_EmptyString_ThrowsArgumentException()
    {
        Action act = () => PdfName.Intern(string.Empty);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FromRawBytes_EmptyInput_ThrowsPdfParseException()
    {
        // A name token that decodes to an empty string is invalid PDF syntax.
        // FromRawBytes is the parser entry point for untrusted bytes, so the
        // failure must surface as a catchable parse error, not the
        // argument-validation exception thrown by Intern.
        Action act = () => PdfName.FromRawBytes(ReadOnlySpan<byte>.Empty);
        act.Should().Throw<PdfParseException>();
    }
}

public sealed class PdfStringTests
{
    [Fact]
    public void Empty_HasZeroLength()
    {
        PdfString.Empty.Length.Should().Be(0);
    }

    [Fact]
    public void LiteralForm_Serializes_WithParentheses()
    {
        var s = new PdfString("Hello", preferHexForm: false);
        s.ToString().Should().Be("(Hello)");
    }

    [Fact]
    public void HexForm_Serializes_WithAngleBrackets()
    {
        var s = new PdfString("Hi", preferHexForm: true);
        s.ToString().Should().Be("<4869>");
    }

    [Fact]
    public void LiteralForm_EscapesParentheses()
    {
        var s = new PdfString("(test)", preferHexForm: false);
        s.ToString().Should().Be(@"(\(test\))");
    }

    [Fact]
    public void LiteralForm_EscapesBackslash()
    {
        var s = new PdfString(@"a\b", preferHexForm: false);
        s.ToString().Should().Be(@"(a\\b)");
    }

    [Fact]
    public void ToTextString_PlainAscii_DecodesCorrectly()
    {
        var s = new PdfString("Hello");
        s.ToTextString().Should().Be("Hello");
    }

    [Fact]
    public void ToTextString_Utf16BeBom_DecodesCorrectly()
    {
        // UTF-16BE BOM + "Hi" encoded as UTF-16BE
        byte[] bytes = [0xFE, 0xFF, 0x00, 0x48, 0x00, 0x69];
        var s = new PdfString(bytes);
        s.ToTextString().Should().Be("Hi");
    }

    [Fact]
    public void Equality_SameBytes_AreEqual()
    {
        var a = new PdfString("test");
        var b = new PdfString("test");
        a.Equals(b).Should().BeTrue();
    }

    [Fact]
    public void Equality_DifferentBytes_AreNotEqual()
    {
        var a = new PdfString("test");
        var b = new PdfString("other");
        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void FromUnicode_ProducesUtf16BeWithBom()
    {
        PdfString s = PdfString.FromUnicode("Hi");
        s.Bytes[0].Should().Be(0xFE);
        s.Bytes[1].Should().Be(0xFF);
        s.ToTextString().Should().Be("Hi");
    }
}

public sealed class PdfArrayTests
{
    [Fact]
    public void Empty_HasZeroCount()
    {
        var a = new PdfArray();
        a.Count.Should().Be(0);
    }

    [Fact]
    public void Add_IncreasesCount()
    {
        var a = new PdfArray();
        a.Add(new PdfInteger(1));
        a.Count.Should().Be(1);
    }

    [Fact]
    public void Indexer_ReturnsCorrectElement()
    {
        var a = new PdfArray();
        a.Add(new PdfInteger(42));
        a[0].Cast<PdfInteger>().Value.Should().Be(42);
    }

    [Fact]
    public void AsRectangle_FourElements_ReturnsCorrectValues()
    {
        var a = new PdfArray([
            new PdfInteger(0),
            new PdfInteger(0),
            new PdfReal(595.28),
            new PdfReal(841.89)
        ]);

        var (x1, y1, x2, y2) = a.AsRectangle();
        x1.Should().Be(0);
        y1.Should().Be(0);
        x2.Should().BeApproximately(595.28, 1e-2);
        y2.Should().BeApproximately(841.89, 1e-2);
    }

    [Fact]
    public void AsRectangle_WrongCount_Throws()
    {
        var a = new PdfArray([new PdfInteger(1), new PdfInteger(2)]);
        Action act = () => a.AsRectangle();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ToString_FormatsAsPdfSyntax()
    {
        var a = new PdfArray([new PdfInteger(1), PdfName.Intern("Type")]);
        a.ToString().Should().Be("[1 /Type]");
    }
}

public sealed class PdfDictionaryTests
{
    [Fact]
    public void Set_AndGet_RoundTrips()
    {
        var d = new PdfDictionary();
        d.Set(PdfName.Type, PdfName.Page);
        d.GetName(PdfName.Type).Should().BeSameAs(PdfName.Page);
    }

    [Fact]
    public void GetInteger_AbsentKey_ReturnsDefault()
    {
        var d = new PdfDictionary();
        d.GetInteger(PdfName.Rotate, 0).Should().Be(0);
    }

    [Fact]
    public void Remove_ExistingKey_Succeeds()
    {
        var d = new PdfDictionary();
        d.Set(PdfName.Type, PdfName.Page);
        d.Remove(PdfName.Type).Should().BeTrue();
        d.ContainsKey(PdfName.Type).Should().BeFalse();
    }

    [Fact]
    public void Type_Property_ReturnsTypeEntry()
    {
        var d = new PdfDictionary();
        d.Set(PdfName.Type, PdfName.Page);
        d.Type.Should().BeSameAs(PdfName.Page);
    }

    [Fact]
    public void GetNumber_AcceptsBothIntegerAndReal()
    {
        var d = new PdfDictionary();
        d.Set(PdfName.Rotate, 90);
        d.GetNumber(PdfName.Rotate).Should().Be(90.0);
    }
}

public sealed class PdfReferenceTests
{
    [Fact]
    public void ObjectNumber_RoundTrips()
    {
        var r = new PdfReference(12, 0);
        r.ObjectNumber.Should().Be(12);
        r.Generation.Should().Be(0);
    }

    [Fact]
    public void ToString_ReturnsCanonicalForm()
    {
        var r = new PdfReference(5, 0);
        r.ToString().Should().Be("5 0 R");
    }

    [Fact]
    public void Equality_SameObjectId_AreEqual()
    {
        var a = new PdfReference(3, 0);
        var b = new PdfReference(3, 0);
        a.Equals(b).Should().BeTrue();
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void Equality_DifferentObjectId_AreNotEqual()
    {
        var a = new PdfReference(3, 0);
        var b = new PdfReference(4, 0);
        a.Equals(b).Should().BeFalse();
        (a != b).Should().BeTrue();
    }
}

public sealed class PdfPrimitiveCastTests
{
    [Fact]
    public void As_CorrectType_ReturnsInstance()
    {
        PdfPrimitive p = new PdfInteger(1);
        p.As<PdfInteger>().Should().NotBeNull();
    }

    [Fact]
    public void As_WrongType_ReturnsNull()
    {
        PdfPrimitive p = new PdfInteger(1);
        p.As<PdfName>().Should().BeNull();
    }

    [Fact]
    public void Cast_CorrectType_ReturnsInstance()
    {
        PdfPrimitive p = new PdfInteger(1);
        p.Cast<PdfInteger>().Value.Should().Be(1);
    }

    [Fact]
    public void Cast_WrongType_ThrowsInvalidCastException()
    {
        PdfPrimitive p = new PdfInteger(1);
        Action act = () => p.Cast<PdfName>();
        act.Should().Throw<InvalidCastException>();
    }
}
