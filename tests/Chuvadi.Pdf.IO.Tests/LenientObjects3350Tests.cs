// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.3.7 (dictionaries), §7.3.8 (streams)
// PHASE: 3.35.0 (52h, D-385) — damaged syntax inside objects is read past
//        when the document is opened with RepairIfDamaged; a stream written
//        directly inside a dictionary no longer swallows the ">>" after it.

using System.IO;
using System.Text;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.IO.Tests;

public sealed class LenientObjects3350Tests
{
    [Fact]
    public void ADirectStreamWithAnIndirectLength_KeepsTheDictionaryAfterIt()
    {
        // Poppler's mask.pdf: a stream inside a resource dictionary, with an
        // indirect /Length. Reading up to endstream consumed it, and the
        // parser then took the next token (">>") as "endstream".
        PdfIndirectObject obj = Parse("2 0 obj\n<< /XObject << /Im << /Length 6 0 R >>\nstream\nabc\nendstream\n>> /Count 1 >>\nendobj\n", lenient: false);

        PdfDictionary dict = (PdfDictionary)obj.Value;
        dict.ContainsKey(PdfName.Intern("Count")).Should().BeTrue();
        PdfDictionary xobjects = (PdfDictionary)dict[PdfName.Intern("XObject")];
        xobjects[PdfName.Intern("Im")].Should().BeOfType<PdfStream>();
    }

    [Fact]
    public void ADictionaryClosedByStream_IsReadOnlyWhenLenient()
    {
        // pdfdiff UnknownFilter-PageContentStream.pdf: ">>" written as ">".
        const string text = "10 0 obj\n<< /Length 3 /Filter /XXXDecode\n>\nstream\nabc\nendstream\nendobj\n";

        System.Action strict = () => Parse(text, lenient: false);
        strict.Should().Throw<PdfParseException>();

        PdfIndirectObject obj = Parse(text, lenient: true);
        obj.Value.Should().BeOfType<PdfStream>();
        ((PdfStream)obj.Value).RawBytes.Should().Equal(Encoding.ASCII.GetBytes("abc"));
    }

    [Fact]
    public void AValueWithNoKey_IsSkippedOnlyWhenLenient()
    {
        const string text = "3 0 obj\n<< /A 1 << /B 2 >> /C 3 >>\nendobj\n";

        System.Action strict = () => Parse(text, lenient: false);
        strict.Should().Throw<PdfParseException>();

        PdfDictionary dict = (PdfDictionary)Parse(text, lenient: true).Value;
        dict.ContainsKey(PdfName.Intern("A")).Should().BeTrue();
        dict.ContainsKey(PdfName.Intern("C")).Should().BeTrue();
    }

    [Fact]
    public void AnArrayLeftOpen_IsClosedAtEndobjOnlyWhenLenient()
    {
        // stressful-0369: an unterminated array.
        const string text = "4 0 obj\n[ 1 2 3\nendobj\n5 0 obj\n<< >>\nendobj\n";

        System.Action strict = () => Parse(text, lenient: false);
        strict.Should().Throw<PdfParseException>();

        PdfArray array = (PdfArray)Parse(text, lenient: true).Value;
        array.Count.Should().Be(3);
    }

    private static PdfIndirectObject Parse(string text, bool lenient)
    {
        RecoveryLog log = new RecoveryLog { LenientObjects = lenient };
        PdfObjectParser parser = new PdfObjectParser(new MemoryStream(Encoding.Latin1.GetBytes(text))) { Log = log };
        return parser.ReadIndirectObject();
    }
}
