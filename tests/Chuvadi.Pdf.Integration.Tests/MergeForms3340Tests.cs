// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.7.2, §12.7.3.1, §12.7.3.3
// PHASE: 3.34.0 (D-335) — PageOperations.Merge carries form fields
//        (MergeOptions.FormFields) and can encrypt (MergeOptions.Encryption).

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
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Integration.Tests;

public sealed class MergeForms3340Tests
{
    [Fact]
    public void Merge_ByDefault_KeepsEveryField_RenamingClashes()
    {
        byte[] merged = Merge(new MergeOptions(), Form("Alice", "first"), Form("Bob", "second"));

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(merged));
        Dictionary<string, FormField> fields = FormReader.GetLeafFields(doc).ToDictionary(f => f.FullyQualifiedName);
        fields.Keys.Should().BeEquivalentTo(new[] { "patient.name", "note", "consent", "patient_2.name", "note_2", "consent_2" });
        fields["patient.name"].Value.Should().Be("Alice");
        fields["patient_2.name"].Value.Should().Be("Bob");
        fields["note"].Value.Should().Be("first");
        fields["note_2"].Value.Should().Be("second");
        fields["patient.name"].MaxLength.Should().Be(40, "inherited entries come along with the parent");
        fields["patient.name"].Widgets.Single().PageIndex.Should().Be(0);
        fields["patient_2.name"].Widgets.Single().PageIndex.Should().Be(1, "/P points at the merged page");
        fields["note_2"].Widgets.Single().PageIndex.Should().Be(1);
    }

    [Fact]
    public void Merge_TheParamsOverload_NowKeepsTheFormToo()
    {
        using PdfDocument a = PdfDocument.Open(new MemoryStream(Form("Alice", "first")));
        using PdfDocument b = PdfDocument.Open(new MemoryStream(Form("Bob", "second")));
        using MemoryStream output = new MemoryStream();

        PageOperations.Merge(output, a, b);

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(output.ToArray()));
        FormReader.GetLeafFields(doc).Should().HaveCount(6);
    }

    [Fact]
    public void Merge_FillingARenamedField_LeavesTheOtherAlone()
    {
        byte[] merged = Merge(new MergeOptions(), Form("Alice", "first"), Form("Bob", "second"));
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(merged));
        using MemoryStream filled = new MemoryStream();

        FormFiller.Fill(filled, doc, new Dictionary<string, string> { ["patient_2.name"] = "Carol", ["consent"] = "On" });

        using PdfDocument after = PdfDocument.Open(new MemoryStream(filled.ToArray()));
        Dictionary<string, FormField> fields = FormReader.GetLeafFields(after).ToDictionary(f => f.FullyQualifiedName);
        fields["patient.name"].Value.Should().Be("Alice");
        fields["patient_2.name"].Value.Should().Be("Carol");
        fields["consent"].Value.Should().Be("On");
        fields["consent_2"].Value.Should().Be("Off");
    }

    [Fact]
    public void Merge_ShareSameName_JoinsFieldsOfTheSameName()
    {
        byte[] merged = Merge(new MergeOptions { FormFields = MergeFormFields.ShareSameName }, Form("Alice", "first"), Form("Bob", "second"));

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(merged));
        Dictionary<string, FormField> fields = FormReader.GetLeafFields(doc).ToDictionary(f => f.FullyQualifiedName);
        fields.Keys.Should().BeEquivalentTo(new[] { "patient.name", "note", "consent" },
            "fields with the same full name are shared, also below a parent");
        fields["note"].Value.Should().Be("first", "the earlier input's value wins");
        fields["patient.name"].Value.Should().Be("Alice");
        fields["note"].Widgets.Select(w => w.PageIndex).Should().Equal(0, 1);
        fields["consent"].Widgets.Select(w => w.PageIndex).Should().Equal(0, 1);
        fields["patient.name"].Widgets.Select(w => w.PageIndex).Should().Equal(0, 1);

        using MemoryStream filled = new MemoryStream();
        FormFiller.Fill(filled, doc, new Dictionary<string, string> { ["note"] = "both", ["consent"] = "On", ["patient.name"] = "Arun" });
        using PdfDocument after = PdfDocument.Open(new MemoryStream(filled.ToArray()));
        Dictionary<string, FormField> filledFields = FormReader.GetLeafFields(after).ToDictionary(f => f.FullyQualifiedName);
        filledFields["note"].Value.Should().Be("both");
        filledFields["consent"].Value.Should().Be("On");
        filledFields["patient.name"].Value.Should().Be("Arun");
        filledFields["patient.name"].Widgets.Should().HaveCount(2, "one value, shown on both pages");
    }

    [Fact]
    public void Merge_ShareSameName_MovesAChildOnlyTheLaterParentHas_WithWhatItInherited()
    {
        byte[] merged = Merge(new MergeOptions { FormFields = MergeFormFields.ShareSameName }, Form("Alice", "first"), Form("Bob", "second", extraKid: true));

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(merged));
        Dictionary<string, FormField> fields = FormReader.GetLeafFields(doc).ToDictionary(f => f.FullyQualifiedName);
        fields.Keys.Should().BeEquivalentTo(new[] { "patient.name", "patient.ward", "note", "consent" });
        fields["patient.ward"].Type.Should().Be(FormFieldType.Text, "the type it inherited from the later 'patient' moves with it");
        fields["patient.ward"].MaxLength.Should().Be(40);
        fields["patient.ward"].Widgets.Single().PageIndex.Should().Be(1);
    }

    [Fact]
    public void Merge_ShareSameName_DifferentTypesBelowAParent_RenamesTheWholeParent()
    {
        byte[] merged = Merge(new MergeOptions { FormFields = MergeFormFields.ShareSameName }, Form("Alice", "first"), Form("Bob", "second", nameType: "/FT/Btn"));

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(merged));
        FormReader.GetLeafFields(doc).Select(f => f.FullyQualifiedName).Should().BeEquivalentTo(
            new[] { "patient.name", "patient_2.name", "note", "consent" },
            "'patient.name' is text in one input and a button in the other, so nothing below 'patient' is shared");
    }

    [Fact]
    public void Merge_Drop_WritesNoForm_ButKeepsTheWidgetsOnTheirPages()
    {
        byte[] merged = Merge(new MergeOptions { FormFields = MergeFormFields.Drop }, Form("Alice", "first"), Form("Bob", "second"));

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(merged));
        doc.Catalog.ContainsKey(PdfName.Intern("AcroForm")).Should().BeFalse();
        doc.Pages[1].Dictionary.ContainsKey(PdfName.Intern("Annots")).Should().BeTrue();

        // The field parent is not copied, so the widget's /Parent is null. Before
        // 3.34.0 it kept its source number, which named a stray page copy.
        PdfArray annots = (PdfArray)doc.Objects.Resolve(doc.Pages[1].Dictionary[PdfName.Intern("Annots")]);
        PdfDictionary widget = (PdfDictionary)doc.Objects.Resolve(annots[0]);
        doc.Objects.Resolve(widget[PdfName.Parent]).Should().BeOfType<PdfNull>();
    }

    [Fact]
    public void Merge_DefaultResources_RenameAClashingFont_AndTheDaFollows()
    {
        byte[] merged = Merge(new MergeOptions(), Form("Alice", "first"), Form("Bob", "second"));

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(merged));
        PdfDictionary acro = (PdfDictionary)doc.Objects.Resolve(doc.Catalog[PdfName.Intern("AcroForm")]);
        PdfDictionary dr = (PdfDictionary)doc.Objects.Resolve(acro[PdfName.Intern("DR")]);
        PdfDictionary fonts = (PdfDictionary)doc.Objects.Resolve(dr[PdfName.Intern("Font")]);
        fonts.Keys.Select(k => k.Value).Should().BeEquivalentTo(new[] { "Helv", "Helv_2" });

        PdfDictionary note2 = FieldDictionary(doc, "note_2");
        Encoding.Latin1.GetString(((PdfString)note2[PdfName.Intern("DA")]).Bytes).Should().Be("/Helv_2 12 Tf 0 g");
        PdfDictionary note = FieldDictionary(doc, "note");
        Encoding.Latin1.GetString(((PdfString)note[PdfName.Intern("DA")]).Bytes).Should().Be("/Helv 12 Tf 0 g");
    }

    [Fact]
    public void Merge_NeedAppearancesAndSigFlags_AreOrd_AndXfaIsDropped()
    {
        byte[] merged = Merge(new MergeOptions(), Form("Alice", "first"), Form("Bob", "second", extra: "/NeedAppearances true/SigFlags 1/XFA[(template) 99 0 R]"));

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(merged));
        PdfDictionary acro = (PdfDictionary)doc.Objects.Resolve(doc.Catalog[PdfName.Intern("AcroForm")]);
        acro[PdfName.Intern("NeedAppearances")].Should().BeOfType<PdfBoolean>().Which.Value.Should().BeTrue();
        ((PdfInteger)acro[PdfName.Intern("SigFlags")]).Value.Should().Be(1);
        acro.ContainsKey(PdfName.Intern("XFA")).Should().BeFalse();
    }

    [Fact]
    public void Merge_WithEncryption_WritesAnEncryptedDocument()
    {
        byte[] merged = Merge(new MergeOptions { Encryption = EncryptionOptions.Aes256("user", "owner") }, Form("Alice", "first"), Form("Bob", "second"));

        PdfDocument.RequiresPassword(new MemoryStream(merged)).Should().BeTrue();
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(merged), "user");
        doc.PageCount.Should().Be(2);
        FormReader.GetLeafFields(doc).Single(f => f.FullyQualifiedName == "note_2").Value.Should().Be("second");
    }

    [Fact]
    public void Merge_WithoutEncryption_IsUnencrypted_EvenFromAnEncryptedInput()
    {
        using PdfDocument plain = PdfDocument.Open(new MemoryStream(Form("Alice", "first")));
        using MemoryStream encrypted = new MemoryStream();
        plain.Save(encrypted, EncryptionOptions.Aes256("user", "owner"));
        using PdfDocument source = PdfDocument.Open(new MemoryStream(encrypted.ToArray()), "user");
        using MemoryStream output = new MemoryStream();

        PageOperations.Merge(output, new[] { source }, new MergeOptions());

        PdfDocument.RequiresPassword(new MemoryStream(output.ToArray())).Should().BeFalse();
    }

    private static PdfDictionary FieldDictionary(PdfDocument doc, string name)
    {
        PdfDictionary acro = (PdfDictionary)doc.Objects.Resolve(doc.Catalog[PdfName.Intern("AcroForm")]);
        PdfArray fields = (PdfArray)doc.Objects.Resolve(acro[PdfName.Intern("Fields")]);
        return fields.Select(f => (PdfDictionary)doc.Objects.Resolve(f))
            .Single(d => d.TryGetValue(PdfName.Intern("T"), out PdfPrimitive? t) && ((PdfString)t).ToTextString() == name);
    }

    private static byte[] Merge(MergeOptions options, params byte[][] inputs)
    {
        List<PdfDocument> docs = inputs.Select(b => PdfDocument.Open(new MemoryStream(b))).ToList();
        try
        {
            using MemoryStream output = new MemoryStream();
            PageOperations.Merge(output, docs, options);
            return output.ToArray();
        }
        finally
        {
            docs.ForEach(d => d.Dispose());
        }
    }

    // One page with: "patient" (Tx, MaxLen 40) > widget "name" (with
    // nameType, its own /FT; with extraKid, a second child "ward"); a merged
    // text field/widget "note"; a merged check box "consent". The DR font
    // is an indirect object, so two copies of this fixture clash on /Helv.
    private static byte[] Form(string name, string note, string extra = "", string nameType = "", bool extraKid = false)
    {
        TestPdf pdf = new TestPdf();
        int catalog = pdf.Reserve();
        int pages = pdf.Reserve();
        int page = pdf.Reserve();
        string p = N(page) + " 0 R";
        int font = pdf.Add("<</Type/Font/Subtype/Type1/BaseFont/Helvetica/Encoding/WinAnsiEncoding>>");
        int on = pdf.AddStream("<</Type/XObject/Subtype/Form/BBox[0 0 20 20]>>", "0 g 0 0 20 20 re f");
        int off = pdf.AddStream("<</Type/XObject/Subtype/Form/BBox[0 0 20 20]>>", string.Empty);
        int patient = pdf.Reserve();
        int nameWidget = pdf.Add("<</Type/Annot/Subtype/Widget" + nameType + "/T(name)/V(" + name + ")/Parent " + N(patient) + " 0 R/Rect[10 150 190 170]/P " + p + "/F 4>>");
        string patientKids = N(nameWidget) + " 0 R";
        string pageAnnots = string.Empty;
        if (extraKid)
        {
            int ward = pdf.Add("<</Type/Annot/Subtype/Widget/T(ward)/Parent " + N(patient) + " 0 R/Rect[10 60 190 80]/P " + p + "/F 4>>");
            patientKids += " " + N(ward) + " 0 R";
            pageAnnots = " " + N(ward) + " 0 R";
        }

        pdf.Set(patient, "<</T(patient)/FT/Tx/MaxLen 40/Kids[" + patientKids + "]>>");
        int noteWidget = pdf.Add("<</Type/Annot/Subtype/Widget/FT/Tx/T(note)/V(" + note + ")/Rect[10 120 190 140]/P " + p + "/F 4/DA(/Helv 12 Tf 0 g)>>");
        int consent = pdf.Add("<</Type/Annot/Subtype/Widget/FT/Btn/T(consent)/Rect[10 90 30 110]/P " + p + "/F 4/V/Off/AS/Off/AP<</N<</On " + N(on) + " 0 R/Off " + N(off) + " 0 R>>>>>>");
        int contents = pdf.AddStream("<<>>", string.Empty);
        pdf.Set(page, "<</Type/Page/Parent " + N(pages) + " 0 R/MediaBox[0 0 200 200]/Contents " + N(contents) + " 0 R/Annots[" + N(nameWidget) + " 0 R " + N(noteWidget) + " 0 R " + N(consent) + " 0 R" + pageAnnots + "]>>");
        pdf.Set(pages, "<</Type/Pages/Kids[" + p + "]/Count 1>>");
        pdf.Set(catalog, "<</Type/Catalog/Pages " + N(pages) + " 0 R/AcroForm<</Fields[" + N(patient) + " 0 R " + N(noteWidget) + " 0 R " + N(consent) + " 0 R]/DA(/Helv 0 Tf 0 g)/DR<</Font<</Helv " + N(font) + " 0 R>>>>" + extra + ">>>>");
        return pdf.Build(catalog);
    }

    private static string N(int n) => n.ToString(CultureInfo.InvariantCulture);
}
