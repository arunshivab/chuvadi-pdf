// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.7.4.3 (/MaxLen, /RV), §12.7.4.4 (/Opt, /I)
// PHASE: 3.34.0 (D-358, D-362) — several values for a multi-select list,
//        FormException details, /RV removed, optional checks, unknown names.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Forms;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Integration.Tests;

public sealed class FormFillValues3340Tests
{
    private static readonly string[] TwoAllergies = { "PEN", "LAT" };
    private static readonly string[] OneAllergy = { "SUL" };
    private static readonly string[] AllergyExports = { "PEN", "SUL", "LAT" };
    private static readonly string[] BloodStates = { "0", "A", "1", "B", "Off" };
    private static readonly int[] PenAndLat = { 0, 2 };
    private static readonly int[] Sul = { 1 };

    // ── (a) several values ──────────────────────────────────────────────

    [Fact]
    public void MultiSelect_WritesAnArrayAndSortedIndices()
    {
        (PdfDocument filled, FormFillResult result) = Fill(Many("allergies", "LAT", "PEN"), new FormFillOptions());
        using (filled)
        {
            PdfDictionary field = Field(filled, "allergies");
            PdfArray v = filled.Objects.ResolveAs<PdfArray>(field[PdfName.Intern("V")])!;
            v.Select(s => ((PdfString)s).ToTextString()).Should().Equal("LAT", "PEN");
            Indices(filled, field).Should().Equal(PenAndLat);
            result.FilledFields.Should().Equal("allergies");
        }
    }

    [Fact]
    public void MultiSelect_OneValue_IsAStringAndReplacesTheOldIndices()
    {
        (PdfDocument filled, _) = Fill(Many("allergies", "SUL"), new FormFillOptions());
        using (filled)
        {
            PdfDictionary field = Field(filled, "allergies");
            ((PdfString)filled.Objects.Resolve(field[PdfName.Intern("V")])).ToTextString().Should().Be("SUL");
            Indices(filled, field).Should().Equal(Sul);
        }
    }

    [Fact]
    public void MultiSelect_EmptyList_ClearsValueAndIndices()
    {
        (PdfDocument filled, _) = Fill(new Dictionary<string, IReadOnlyList<string>> { ["allergies"] = Array.Empty<string>() }, new FormFillOptions());
        using (filled)
        {
            PdfDictionary field = Field(filled, "allergies");
            ((PdfString)filled.Objects.Resolve(field[PdfName.Intern("V")])).ToTextString().Should().BeEmpty();
            field.ContainsKey(PdfName.Intern("I")).Should().BeFalse();
        }
    }

    [Theory]
    [InlineData("note")]
    [InlineData("ward")]
    [InlineData("consent")]
    public void SeveralValues_ForASingleValueField_Throw(string name)
    {
        using PdfDocument doc = TestPdf.Open(Form());
        using MemoryStream output = new MemoryStream();

        FormException ex = FluentActions.Invoking(() => FormFiller.Fill(output, doc, Many(name, "a", "b"), new FormFillOptions()))
            .Should().Throw<FormException>().Which;
        ex.Reason.Should().Be(FormErrorReason.TooManyValues);
        ex.FieldName.Should().Be(name);
        output.Length.Should().Be(0, "nothing is written");
    }

    [Fact]
    public void SingleValueOverload_ChoiceSetsIndicesOnlyWhereTheyBelong()
    {
        using PdfDocument doc = TestPdf.Open(Form());
        using MemoryStream output = new MemoryStream();
        FormFiller.Fill(output, doc, new Dictionary<string, string> { ["allergies"] = "SUL", ["ward"] = "W4" });
        using PdfDocument filled = PdfDocument.Open(new MemoryStream(output.ToArray()));

        Indices(filled, Field(filled, "allergies")).Should().Equal(Sul);
        Field(filled, "ward").ContainsKey(PdfName.Intern("I")).Should().BeFalse("a combo box without /I gets none");
    }

    // ── (b) FormException details ───────────────────────────────────────

    [Fact]
    public void UnknownButtonState_CarriesFieldValueAndStates()
    {
        using PdfDocument doc = TestPdf.Open(Form());
        using MemoryStream output = new MemoryStream();

        FormException ex = FluentActions.Invoking(() => FormFiller.Fill(output, doc, new Dictionary<string, string> { ["blood"] = "O" }))
            .Should().Throw<FormException>().Which;
        ex.Reason.Should().Be(FormErrorReason.UnknownState);
        ex.FieldName.Should().Be("blood");
        ex.Value.Should().Be("O");
        ex.ValidStates.Should().Equal(BloodStates);
        ex.MaxLength.Should().BeNull();
    }

    [Fact]
    public void OtherFormExceptions_HaveNoReason()
    {
        FormException ex = new FormException("x");

        ex.Reason.Should().BeNull();
        ex.FieldName.Should().BeNull();
        ex.ValidStates.Should().BeEmpty();
    }

    // ── (c) /RV ─────────────────────────────────────────────────────────

    [Fact]
    public void TextValue_RemovesTheRichTextValue()
    {
        using PdfDocument doc = TestPdf.Open(Form());
        using MemoryStream output = new MemoryStream();
        FormFiller.Fill(output, doc, new Dictionary<string, string> { ["note"] = "plain" });
        using PdfDocument filled = PdfDocument.Open(new MemoryStream(output.ToArray()));

        PdfDictionary field = Field(filled, "note");
        field.ContainsKey(PdfName.Intern("RV")).Should().BeFalse();
        ((PdfString)filled.Objects.Resolve(field[PdfName.Intern("V")])).ToTextString().Should().Be("plain");
    }

    // ── (d) Validate (opt-in) ───────────────────────────────────────────

    [Fact]
    public void WithoutValidate_ValuesAreWrittenAsGiven()
    {
        string tooLong = new string('x', 12);
        using PdfDocument doc = TestPdf.Open(Form());
        using MemoryStream output = new MemoryStream();
        FormFiller.Fill(output, doc, new Dictionary<string, string> { ["note"] = tooLong, ["ward"] = "Lobby" });
        using PdfDocument filled = PdfDocument.Open(new MemoryStream(output.ToArray()));

        Value(filled, "note").Should().Be(tooLong, "never truncated");
        Value(filled, "ward").Should().Be("Lobby");
    }

    [Fact]
    public void Validate_TextLongerThanMaxLen_Throws()
    {
        using PdfDocument doc = TestPdf.Open(Form());
        using MemoryStream output = new MemoryStream();

        FormException ex = FluentActions.Invoking(() => FormFiller.Fill(output, doc,
                new Dictionary<string, string> { ["note"] = "ABCDEFGHIJK" }, new FormFillOptions { Validate = true }))
            .Should().Throw<FormException>().Which;
        ex.Reason.Should().Be(FormErrorReason.TooLong);
        ex.MaxLength.Should().Be(10);
        ex.Value.Should().Be("ABCDEFGHIJK");
        output.Length.Should().Be(0);
    }

    [Fact]
    public void Validate_MaxLenCountsCodePoints()
    {
        // Ten code points, twenty UTF-16 units (each is a surrogate pair).
        string tenEmoji = string.Concat(Enumerable.Repeat("\U0001F600", 10));
        using PdfDocument doc = TestPdf.Open(Form());
        using MemoryStream output = new MemoryStream();
        FormFiller.Fill(output, doc, new Dictionary<string, string> { ["note"] = tenEmoji }, new FormFillOptions { Validate = true });
        using PdfDocument filled = PdfDocument.Open(new MemoryStream(output.ToArray()));

        Value(filled, "note").Should().Be(tenEmoji);
    }

    [Fact]
    public void Validate_ChoiceNotInOptions_Throws_WithTheExportValues()
    {
        using PdfDocument doc = TestPdf.Open(Form());
        using MemoryStream output = new MemoryStream();

        FormException ex = FluentActions.Invoking(() => FormFiller.Fill(output, doc,
                Many("allergies", "PEN", "Penicillin"), new FormFillOptions { Validate = true }))
            .Should().Throw<FormException>().Which;
        ex.Reason.Should().Be(FormErrorReason.NotInOptions);
        ex.Value.Should().Be("Penicillin", "the display text is not a value: values are export values");
        ex.ValidStates.Should().Equal(AllergyExports);
    }

    [Fact]
    public void Validate_EditableComboAndEmptyValues_AreAccepted()
    {
        using PdfDocument doc = TestPdf.Open(Form());
        using MemoryStream output = new MemoryStream();
        FormFiller.Fill(output, doc, new Dictionary<string, string> { ["city"] = "Madurai", ["ward"] = string.Empty, ["note"] = string.Empty },
            new FormFillOptions { Validate = true });
        using PdfDocument filled = PdfDocument.Open(new MemoryStream(output.ToArray()));

        Value(filled, "city").Should().Be("Madurai");
        Value(filled, "ward").Should().BeEmpty();
    }

    [Fact]
    public void Validate_ValidValues_AreWritten()
    {
        (PdfDocument filled, _) = Fill(new Dictionary<string, IReadOnlyList<string>>
        {
            ["allergies"] = TwoAllergies,
            ["ward"] = new[] { "W4" },
            ["note"] = new[] { "0123456789" },
        }, new FormFillOptions { Validate = true });
        using (filled)
        {
            Value(filled, "ward").Should().Be("W4");
            Value(filled, "note").Should().Be("0123456789");
        }
    }

    // ── (e) unknown names ───────────────────────────────────────────────

    [Fact]
    public void UnknownNames_IgnoredByDefault()
    {
        (PdfDocument filled, FormFillResult result) = Fill(Many("nobody", "x"), new FormFillOptions());
        using (filled)
        {
            result.UnknownFields.Should().BeEmpty();
            result.FilledFields.Should().BeEmpty();
        }
    }

    [Fact]
    public void UnknownNames_Reported_InOrder()
    {
        (PdfDocument filled, FormFillResult result) = Fill(new Dictionary<string, IReadOnlyList<string>>
        {
            ["nobody"] = OneAllergy,
            ["allergies"] = OneAllergy,
            ["nothing"] = OneAllergy,
        }, new FormFillOptions { UnknownFields = FormUnknownFields.Report });
        using (filled)
        {
            result.UnknownFields.Should().Equal("nobody", "nothing");
            result.FilledFields.Should().Equal("allergies");
        }
    }

    [Fact]
    public void UnknownNames_Throw_BeforeWriting()
    {
        using PdfDocument doc = TestPdf.Open(Form());
        using MemoryStream output = new MemoryStream();

        FormException ex = FluentActions.Invoking(() => FormFiller.Fill(output, doc,
                new Dictionary<string, string> { ["note"] = "ok", ["nobody"] = "x" },
                new FormFillOptions { UnknownFields = FormUnknownFields.Throw }))
            .Should().Throw<FormException>().Which;
        ex.Reason.Should().Be(FormErrorReason.UnknownField);
        ex.FieldName.Should().Be("nobody");
        output.Length.Should().Be(0);
    }

    [Fact]
    public void Report_OnTheVoidOverload_IsAnArgumentException()
    {
        using PdfDocument doc = TestPdf.Open(Form());
        using MemoryStream output = new MemoryStream();

        FluentActions.Invoking(() => FormFiller.Fill(output, doc, new Dictionary<string, string> { ["note"] = "x" },
                new FormFillOptions { UnknownFields = FormUnknownFields.Report }))
            .Should().Throw<ArgumentException>().WithMessage("*FormFillResult*");
    }

    // ── Objects the fill does not touch ─────────────────────────────────

    // The /Info the output trailer keeps, and catalog entries such as
    // /Outlines, are written even when nothing loaded them before the fill
    // (before this was fixed, the output named objects it did not contain).
    [Fact]
    public void Fill_WritesInfoAndCatalogObjectsNothingLoaded()
    {
        using PdfDocument doc = TestPdf.Open(Form());
        using MemoryStream output = new MemoryStream();
        FormFiller.Fill(output, doc, new Dictionary<string, string> { ["note"] = "x" });
        using PdfDocument filled = PdfDocument.Open(new MemoryStream(output.ToArray()));

        PdfDictionary info = filled.Objects.ResolveAs<PdfDictionary>(filled.Trailer[PdfName.Info])!;
        ((PdfString)filled.Objects.Resolve(info[PdfName.Intern("Title")])).ToTextString().Should().Be("Admission form");
        PdfDictionary outlines = filled.Objects.ResolveAs<PdfDictionary>(filled.Catalog[PdfName.Intern("Outlines")])!;
        PdfDictionary first = filled.Objects.ResolveAs<PdfDictionary>(outlines[PdfName.Intern("First")])!;
        ((PdfString)filled.Objects.Resolve(first[PdfName.Intern("Title")])).ToTextString().Should().Be("Allergies");
    }

    // ── Fixture ─────────────────────────────────────────────────────────

    private static Dictionary<string, IReadOnlyList<string>> Many(string name, params string[] values) =>
        new Dictionary<string, IReadOnlyList<string>> { [name] = values };

    private static (PdfDocument Filled, FormFillResult Result) Fill(IReadOnlyDictionary<string, IReadOnlyList<string>> values, FormFillOptions options)
    {
        using PdfDocument doc = TestPdf.Open(Form());
        using MemoryStream output = new MemoryStream();
        FormFillResult result = FormFiller.Fill(output, doc, values, options);
        return (PdfDocument.Open(new MemoryStream(output.ToArray())), result);
    }

    private static PdfDictionary Field(PdfDocument doc, string name) =>
        (PdfDictionary)doc.Objects.Resolve(new PdfReference(FormReader.GetLeafFields(doc).Single(f => f.FullyQualifiedName == name).ObjectId));

    private static string Value(PdfDocument doc, string name) =>
        ((PdfString)doc.Objects.Resolve(Field(doc, name)[PdfName.Intern("V")])).ToTextString();

    private static IEnumerable<int> Indices(PdfDocument doc, PdfDictionary field) =>
        doc.Objects.ResolveAs<PdfArray>(field[PdfName.Intern("I")])!.Select(i => ((PdfInteger)i).Value);

    // A page with: a multi-select list box "allergies" (options PEN, SUL, LAT
    // with display texts, /I [1]); a combo box "ward"; an editable combo box
    // "city"; a rich-text field "note" (MaxLen 10, /RV); a check box
    // "consent"; a radio group "blood" (states 0 and 1, export values A
    // and B); one bookmark; and an /Info dictionary.
    private static byte[] Form()
    {
        TestPdf pdf = new TestPdf();
        int catalog = pdf.Reserve();
        int pages = pdf.Reserve();
        int page = pdf.Reserve();
        string p = N(page) + " 0 R";
        int on = pdf.AddStream("<</Type/XObject/Subtype/Form/BBox[0 0 20 20]>>", "0 g 0 0 20 20 re f");
        int allergies = pdf.Add("<</Type/Annot/Subtype/Widget/FT/Ch/Ff 2097152/T(allergies)/Rect[10 150 90 190]/P " + p
            + "/Opt[[(PEN)(Penicillin)][(SUL)(Sulfa)][(LAT)(Latex)]]/V(SUL)/I[1]>>");
        int ward = pdf.Add("<</Type/Annot/Subtype/Widget/FT/Ch/Ff 131072/T(ward)/Rect[100 150 190 170]/P " + p + "/Opt[(ICU)(W4)]>>");
        int city = pdf.Add("<</Type/Annot/Subtype/Widget/FT/Ch/Ff 393216/T(city)/Rect[100 120 190 140]/P " + p + "/Opt[(Chennai)]>>");
        int note = pdf.Add("<</Type/Annot/Subtype/Widget/FT/Tx/Ff 33554432/MaxLen 10/T(note)/Rect[10 120 90 140]/P " + p
            + "/V(old)/RV(<body><p>old</p></body>)>>");
        int consent = pdf.Add("<</Type/Annot/Subtype/Widget/FT/Btn/T(consent)/Rect[10 90 30 110]/P " + p + "/AS/Off/AP<</N<</On " + N(on) + " 0 R>>>>>>");
        int blood = pdf.Reserve();
        int kidA = pdf.Add("<</Type/Annot/Subtype/Widget/Parent " + N(blood) + " 0 R/Rect[50 90 70 110]/P " + p + "/AS/Off/AP<</N<</0 " + N(on) + " 0 R>>>>>>");
        int kidB = pdf.Add("<</Type/Annot/Subtype/Widget/Parent " + N(blood) + " 0 R/Rect[90 90 110 110]/P " + p + "/AS/Off/AP<</N<</1 " + N(on) + " 0 R>>>>>>");
        pdf.Set(blood, "<</FT/Btn/Ff 49152/T(blood)/V/Off/Opt[(A)(B)]/Kids[" + N(kidA) + " 0 R " + N(kidB) + " 0 R]>>");
        int[] annots = { allergies, ward, city, note, consent, kidA, kidB };
        int[] fields = { allergies, ward, city, note, consent, blood };
        int contents = pdf.AddStream("<<>>", string.Empty);
        pdf.Set(page, "<</Type/Page/Parent " + N(pages) + " 0 R/MediaBox[0 0 200 200]/Contents " + N(contents) + " 0 R/Annots[" + Refs(annots) + "]>>");
        pdf.Set(pages, "<</Type/Pages/Kids[" + p + "]/Count 1>>");
        int outlines = pdf.Reserve();
        int bookmark = pdf.Add("<</Title(Allergies)/Parent " + N(outlines) + " 0 R/Dest[" + p + "/Fit]>>");
        pdf.Set(outlines, "<</Type/Outlines/First " + N(bookmark) + " 0 R/Last " + N(bookmark) + " 0 R/Count 1>>");
        int info = pdf.Add("<</Title(Admission form)/Producer(test)>>");
        pdf.Set(catalog, "<</Type/Catalog/Pages " + N(pages) + " 0 R/Outlines " + N(outlines) + " 0 R/AcroForm<</Fields[" + Refs(fields) + "]>>>>");
        return pdf.Build(catalog, info);
    }

    private static string N(int n) => n.ToString(CultureInfo.InvariantCulture);

    private static string Refs(IEnumerable<int> numbers) => string.Join(" ", numbers.Select(n => N(n) + " 0 R"));
}
