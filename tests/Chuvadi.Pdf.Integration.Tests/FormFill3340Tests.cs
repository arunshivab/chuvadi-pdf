// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.7.3 (field dictionaries, inheritable entries),
//        §12.7.4.2 (check boxes, radio buttons), §12.7.4.4 (choice /Opt),
//        §7.9.2.2 (text strings)
// PHASE: 3.34.0 (D-335 items 1 and 2) — FormField.Widgets, Options, Flags,
//        MaxLength; FormFiller buttons and Unicode text; fill then flatten.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Forms;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Operations;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Text;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Integration.Tests;

public sealed class FormFill3340Tests
{
    private const string Helv = "<</Font<</Helv<</Type/Font/Subtype/Type1/BaseFont/Helvetica/Encoding/WinAnsiEncoding>>>>>>";

    // ── Reading ──────────────────────────────────────────────────────────

    [Fact]
    public void Widgets_GivePageRectFlagsAndInheritedEntries()
    {
        using PdfDocument doc = TestPdf.Open(Form(out _));

        FormField name = FormReader.GetLeafFields(doc).Single(f => f.FullyQualifiedName == "patient.name");
        name.Type.Should().Be(FormFieldType.Text, "the type is inherited from the parent");
        name.MaxLength.Should().Be(40, "the max length is inherited too");
        name.Flags.Should().Be(FormFieldFlags.Required | FormFieldFlags.Multiline);
        FormWidget widget = name.Widgets.Should().ContainSingle().Subject;
        widget.PageIndex.Should().Be(0);
        widget.Rect.X1.Should().Be(10);
        widget.Rect.Y1.Should().Be(150, "a reversed /Rect is normalised");
        widget.Rect.X2.Should().Be(190);
        widget.Rect.Y2.Should().Be(170);
        widget.OnState.Should().BeNull();
    }

    [Fact]
    public void Options_ReadStringsAndPairs_AsText()
    {
        using PdfDocument doc = TestPdf.Open(Form(out _));

        FormField ward = FormReader.GetLeafFields(doc).Single(f => f.FullyQualifiedName == "ward");
        ward.Options.Select(o => (o.ExportValue, o.DisplayText)).Should().Equal(
            ("ICU", "ICU"),
            ("W4", "வார்டு 4"));
    }

    [Fact]
    public void ButtonWidgets_GiveOnStatesAndExportValues()
    {
        using PdfDocument doc = TestPdf.Open(Form(out _));
        IReadOnlyList<FormField> leaves = FormReader.GetLeafFields(doc);

        leaves.Single(f => f.FullyQualifiedName == "consent").Widgets.Single().OnState.Should().Be("On");
        FormField radio = leaves.Single(f => f.FullyQualifiedName == "blood");
        radio.Flags.Should().HaveFlag(FormFieldFlags.Radio);
        radio.Widgets.Select(w => (w.OnState, w.ExportValue, w.PageIndex)).Should().Equal(
            ("0", "A", 0),
            ("1", "B", 0));
    }

    [Fact]
    public void Utf16NamesAndValues_ReadAsText()
    {
        using PdfDocument doc = TestPdf.Open(Form(out _));

        FormReader.GetLeafFields(doc).Should().Contain(f => f.FullyQualifiedName == "பெயர்" && f.Value == "அருண்");
    }

    // ── Filling ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Yes")]
    [InlineData("on")]
    [InlineData("TRUE")]
    [InlineData("On")]
    public void CheckBox_TurnsOnWithItsOwnState(string value)
    {
        using PdfDocument filled = Fill(new Dictionary<string, string> { ["consent"] = value });

        PdfDictionary box = Widget(filled, "consent", 0);
        box.GetName(PdfName.Intern("AS"))!.Value.Should().Be("On");
        filled.Objects.Resolve(box[PdfName.Intern("V")]).Should().BeOfType<PdfName>().Which.Value.Should().Be("On");
        TestPdf.Ink(filled, AnnotationMode.View, 12, 102, 28, 118).Should().BeGreaterThan(50);
    }

    [Theory]
    [InlineData("Off")]
    [InlineData("false")]
    [InlineData("")]
    public void CheckBox_TurnsOff(string value)
    {
        using PdfDocument filled = Fill(new Dictionary<string, string> { ["consent"] = value });

        Widget(filled, "consent", 0).GetName(PdfName.Intern("AS"))!.Value.Should().Be("Off");
        TestPdf.Ink(filled, AnnotationMode.View, 12, 102, 28, 118).Should().Be(0);
    }

    [Theory]
    [InlineData("B")]
    [InlineData("1")]
    public void Radio_TurnsOnOneKid_ByExportValueOrState(string value)
    {
        using PdfDocument filled = Fill(new Dictionary<string, string> { ["blood"] = value });

        Widget(filled, "blood", 0).GetName(PdfName.Intern("AS"))!.Value.Should().Be("Off");
        Widget(filled, "blood", 1).GetName(PdfName.Intern("AS"))!.Value.Should().Be("1");
        FormReader.GetLeafFields(filled).Single(f => f.FullyQualifiedName == "blood").Value.Should().Be("1");
        TestPdf.Ink(filled, AnnotationMode.View, 52, 102, 68, 118).Should().Be(0);
        TestPdf.Ink(filled, AnnotationMode.View, 92, 102, 108, 118).Should().BeGreaterThan(50);
    }

    [Fact]
    public void Radio_Off_TurnsEveryKidOff()
    {
        using PdfDocument filled = Fill(new Dictionary<string, string> { ["blood"] = "Off" });

        Widget(filled, "blood", 0).GetName(PdfName.Intern("AS"))!.Value.Should().Be("Off");
        Widget(filled, "blood", 1).GetName(PdfName.Intern("AS"))!.Value.Should().Be("Off");
    }

    [Theory]
    [InlineData("blood", "O")]
    [InlineData("consent", "maybe")]
    public void UnknownButtonValue_Throws(string field, string value)
    {
        using PdfDocument doc = TestPdf.Open(Form(out _));
        using MemoryStream output = new MemoryStream();

        FluentActions.Invoking(() => FormFiller.Fill(output, doc, new Dictionary<string, string> { [field] = value }))
            .Should().Throw<FormException>().WithMessage("*'" + value + "'*");
    }

    [Fact]
    public void TamilTextValue_IsWrittenAsUnicode_AndFlattensAsWritten()
    {
        const string Value = "தமிழ்நாடு கொள்கை";
        using PdfDocument filled = Fill(new Dictionary<string, string> { ["patient.name"] = Value });

        FormReader.GetLeafFields(filled).Single(f => f.FullyQualifiedName == "patient.name").Value.Should().Be(Value);
        using MemoryStream flat = new MemoryStream();
        AnnotationFlattener.Flatten(flat, filled);
        using PdfDocument flattened = PdfDocument.Open(new MemoryStream(flat.ToArray()));
        new TextExtractor(flattened.Objects).ExtractText(flattened.Pages[0]).Should().Contain(Value);
    }

    [Fact]
    public void LatinTextValue_IsByteIdenticalToBefore()
    {
        using PdfDocument filled = Fill(new Dictionary<string, string> { ["patient.name"] = "Arun" });

        PdfDictionary field = Field(filled, "patient.name");
        ((PdfString)filled.Objects.Resolve(field[PdfName.Intern("V")])).Bytes.Should().Equal(Encoding.Latin1.GetBytes("Arun"));
    }

    [Fact]
    public void Pushbutton_IsLeftUnchanged()
    {
        using PdfDocument filled = Fill(new Dictionary<string, string> { ["print"] = "Yes" });

        Field(filled, "print").ContainsKey(PdfName.Intern("V")).Should().BeFalse();
    }

    [Fact]
    public void FillThenFlatten_DrawsEveryValue()
    {
        using PdfDocument filled = Fill(new Dictionary<string, string>
        {
            ["patient.name"] = "Arun Shiva",
            ["consent"] = "Yes",
            ["blood"] = "A",
        });
        using MemoryStream flat = new MemoryStream();
        AnnotationFlattener.Flatten(flat, filled);
        using PdfDocument flattened = PdfDocument.Open(new MemoryStream(flat.ToArray()));

        new TextExtractor(flattened.Objects).ExtractText(flattened.Pages[0]).Should().Contain("Arun Shiva");
        TestPdf.Ink(flattened, AnnotationMode.View, 12, 102, 28, 118).Should().BeGreaterThan(50, "the check box");
        TestPdf.Ink(flattened, AnnotationMode.View, 52, 102, 68, 118).Should().BeGreaterThan(50, "radio A");
        TestPdf.Ink(flattened, AnnotationMode.View, 92, 102, 108, 118).Should().Be(0, "radio B");
        FormReader.GetFields(flattened).Should().BeEmpty();
    }

    // ── Document information, ID and encryption (D-342) ─────────────────

    [Fact]
    public void Fill_KeepsInfoAndId()
    {
        byte[] saved = Saved(encryption: null);
        using PdfDocument source = PdfDocument.Open(new MemoryStream(saved));
        string producer = InfoText(source, "Producer");
        PdfPrimitive id = source.Trailer[PdfName.Intern("ID")];

        using MemoryStream output = new MemoryStream();
        FormFiller.Fill(output, source, new Dictionary<string, string> { ["consent"] = "Yes" });
        using PdfDocument filled = PdfDocument.Open(new MemoryStream(output.ToArray()));

        InfoText(filled, "Producer").Should().Be(producer);
        filled.Trailer[PdfName.Intern("ID")].ToString().Should().Be(id.ToString());
    }

    [Fact]
    public void Fill_EncryptedSource_IsUnencryptedUnlessAsked()
    {
        byte[] encrypted = Saved(EncryptionOptions.Aes256("user", "owner"));
        using PdfDocument source = PdfDocument.Open(new MemoryStream(encrypted), "user");

        using MemoryStream plain = new MemoryStream();
        FormFiller.Fill(plain, source, new Dictionary<string, string> { ["patient.name"] = "Arun" });
        PdfDocument.RequiresPassword(new MemoryStream(plain.ToArray())).Should().BeFalse();

        using MemoryStream kept = new MemoryStream();
        FormFiller.Fill(kept, source, new Dictionary<string, string> { ["patient.name"] = "Arun" },
            new FormFillOptions { Encryption = EncryptionOptions.Aes256("user", "owner") });
        PdfDocument.RequiresPassword(new MemoryStream(kept.ToArray())).Should().BeTrue();
        using PdfDocument reopened = PdfDocument.Open(new MemoryStream(kept.ToArray()), "user");
        FormReader.GetLeafFields(reopened).Single(f => f.FullyQualifiedName == "patient.name").Value.Should().Be("Arun");
    }

    private static byte[] Saved(EncryptionOptions? encryption)
    {
        using PdfDocument doc = TestPdf.Open(Form(out _));
        using MemoryStream output = new MemoryStream();
        doc.Save(output, encryption);
        return output.ToArray();
    }

    private static string InfoText(PdfDocument doc, string key)
    {
        PdfDictionary info = (PdfDictionary)doc.Objects.Resolve(doc.Trailer[PdfName.Info]);
        return ((PdfString)doc.Objects.Resolve(info[PdfName.Intern(key)])).ToTextString();
    }

    // ── Fixture ──────────────────────────────────────────────────────────

    private static PdfDocument Fill(IReadOnlyDictionary<string, string> values)
    {
        using PdfDocument doc = TestPdf.Open(Form(out _));
        using MemoryStream output = new MemoryStream();
        FormFiller.Fill(output, doc, values);
        return PdfDocument.Open(new MemoryStream(output.ToArray()));
    }

    private static PdfDictionary Field(PdfDocument doc, string name) =>
        (PdfDictionary)doc.Objects.Resolve(new PdfReference(FormReader.GetLeafFields(doc).Single(f => f.FullyQualifiedName == name).ObjectId));

    private static PdfDictionary Widget(PdfDocument doc, string name, int index) =>
        (PdfDictionary)doc.Objects.Resolve(new PdfReference(FormReader.GetLeafFields(doc).Single(f => f.FullyQualifiedName == name).Widgets[index].ObjectId));

    // A page with: a parent field "patient" (Tx, MaxLen 40) whose child "name"
    // is a merged widget with a reversed /Rect; a check box "consent" (on-state
    // /On); a radio group "blood" with two kid widgets (/0 and /1) and /Opt
    // export values A and B; a choice "ward"; a UTF-16 named text field; and a
    // pushbutton "print".
    private static byte[] Form(out int page)
    {
        TestPdf pdf = new TestPdf();
        int catalog = pdf.Reserve();
        int pages = pdf.Reserve();
        page = pdf.Reserve();
        string p = N(page) + " 0 R";
        int on = pdf.AddStream("<</Type/XObject/Subtype/Form/BBox[0 0 20 20]>>", "0 g 0 0 20 20 re f");
        int off = pdf.AddStream("<</Type/XObject/Subtype/Form/BBox[0 0 20 20]>>", string.Empty);
        int patient = pdf.Reserve();
        int name = pdf.Add("<</Type/Annot/Subtype/Widget/T(name)/Parent " + N(patient) + " 0 R/Ff 4098/Rect[10 170 190 150]/P " + p + "/F 4/DA(/Helv 12 Tf 0 g)>>");
        pdf.Set(patient, "<</T(patient)/FT/Tx/MaxLen 40/Kids[" + N(name) + " 0 R]>>");
        int consent = pdf.Add("<</Type/Annot/Subtype/Widget/FT/Btn/T(consent)/Rect[10 100 30 120]/P " + p + "/F 4/V/Off/AS/Off/AP<</N<</On " + N(on) + " 0 R/Off " + N(off) + " 0 R>>>>>>");
        int blood = pdf.Reserve();
        int kidA = pdf.Add("<</Type/Annot/Subtype/Widget/Parent " + N(blood) + " 0 R/Rect[50 100 70 120]/P " + p + "/F 4/AS/Off/AP<</N<</0 " + N(on) + " 0 R/Off " + N(off) + " 0 R>>>>>>");
        int kidB = pdf.Add("<</Type/Annot/Subtype/Widget/Parent " + N(blood) + " 0 R/Rect[90 100 110 120]/P " + p + "/F 4/AS/Off/AP<</N<</1 " + N(on) + " 0 R/Off " + N(off) + " 0 R>>>>>>");
        pdf.Set(blood, "<</FT/Btn/Ff 49152/T(blood)/V/Off/Opt[(A)(B)]/Kids[" + N(kidA) + " 0 R " + N(kidB) + " 0 R]>>");
        int ward = pdf.Add("<</Type/Annot/Subtype/Widget/FT/Ch/Ff 131072/T(ward)/Rect[10 60 90 80]/P " + p + "/F 4/DA(/Helv 10 Tf 0 g)/Opt[(ICU)[(W4)" + Hex("வார்டு 4") + "]]>>");
        int tamil = pdf.Add("<</Type/Annot/Subtype/Widget/FT/Tx/T" + Hex("பெயர்") + "/V" + Hex("அருண்") + "/Rect[100 60 190 80]/P " + p + "/F 4/DA(/Helv 10 Tf 0 g)>>");
        int print = pdf.Add("<</Type/Annot/Subtype/Widget/FT/Btn/Ff 65536/T(print)/Rect[150 100 190 120]/P " + p + "/F 4>>");
        int[] annots = { name, consent, kidA, kidB, ward, tamil, print };
        int[] fields = { patient, consent, blood, ward, tamil, print };
        int contents = pdf.AddStream("<<>>", string.Empty);
        pdf.Set(page, "<</Type/Page/Parent " + N(pages) + " 0 R/MediaBox[0 0 200 200]/Resources " + Helv + "/Contents " + N(contents) + " 0 R/Annots[" + Refs(annots) + "]>>");
        pdf.Set(pages, "<</Type/Pages/Kids[" + p + "]/Count 1>>");
        pdf.Set(catalog, "<</Type/Catalog/Pages " + N(pages) + " 0 R/AcroForm<</Fields[" + Refs(fields) + "]/DR " + Helv + ">>>>");
        return pdf.Build(catalog);
    }

    private static string N(int n) => n.ToString(CultureInfo.InvariantCulture);

    private static string Refs(IEnumerable<int> numbers) => string.Join(" ", numbers.Select(n => N(n) + " 0 R"));

    private static string Hex(string text)
    {
        StringBuilder sb = new StringBuilder("<FEFF");
        foreach (char c in text)
        {
            sb.Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
        }

        return sb.Append('>').ToString();
    }
}
