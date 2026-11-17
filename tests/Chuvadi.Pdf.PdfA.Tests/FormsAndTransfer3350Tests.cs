// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ISO 19005-1 §6.9 and ISO 19005-2 §6.4.1 (NeedAppearances, widget
//        appearances), ISO 19005-1 §6.2.8 and ISO 19005-2 §6.2.5 (/TR, /TR2)
// PHASE: 3.35.0 (backlog 49, D-385)

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.PdfA.Tests;

public sealed class FormsAndTransfer3350Tests
{
    [Theory]
    [InlineData(PdfAConformance.PdfA1B)]
    [InlineData(PdfAConformance.PdfA2B)]
    public void NeedAppearances_IsClearedAndTheFieldGetsAnAppearance(PdfAConformance conformance)
    {
        using PdfDocument document = Open(FormPdf(needAppearances: true, withAppearance: true));
        using MemoryStream output = new MemoryStream();

        PdfAResult result = PdfAWriter.Write(output, document, new PdfAOptions { Conformance = conformance });

        result.Succeeded.Should().BeTrue(string.Join("; ", result.Violations));
        result.Warnings.Should().Contain(w => w.Contains("/NeedAppearances was removed"));
        using PdfDocument reopened = Open(output.ToArray());
        PdfDictionary root = (PdfDictionary)reopened.Objects.Resolve(reopened.Trailer[PdfName.Root])!;
        PdfDictionary form = (PdfDictionary)reopened.Objects.Resolve(root[PdfName.Intern("AcroForm")])!;
        form.ContainsKey(PdfName.Intern("NeedAppearances")).Should().BeFalse();
        string appearance = Encoding.Latin1.GetString(NormalAppearance(reopened).RawBytes);
        appearance.Should().Contain("<46696C6C656420696E>", "the appearance is made from the field's value, not the stale one");
        appearance.Should().NotContain("STALE");
    }

    [Fact]
    public void AWidgetWithNoAppearance_GetsOne()
    {
        using PdfDocument document = Open(FormPdf(needAppearances: false, withAppearance: false));
        using MemoryStream output = new MemoryStream();

        PdfAWriter.Write(output, document, new PdfAOptions { Conformance = PdfAConformance.PdfA2B }).Succeeded.Should().BeTrue();

        using PdfDocument reopened = Open(output.ToArray());
        Encoding.Latin1.GetString(NormalAppearance(reopened).RawBytes).Should().Contain("<46696C6C656420696E>");
    }

    [Fact]
    public void TransferFunctions_AreRemoved()
    {
        using PdfDocument document = Open(FormPdf(needAppearances: false, withAppearance: true));
        using MemoryStream output = new MemoryStream();

        PdfAResult result = PdfAWriter.Write(output, document, new PdfAOptions { Conformance = PdfAConformance.PdfA1B });

        result.Warnings.Should().Contain(w => w.Contains("transfer function"));
        using PdfDocument reopened = Open(output.ToArray());
        PdfDictionary states = (PdfDictionary)reopened.Objects.Resolve(reopened.Pages[0].Resources![PdfName.Intern("ExtGState")])!;
        PdfDictionary gs0 = (PdfDictionary)reopened.Objects.Resolve(states[PdfName.Intern("GS0")])!;
        gs0.ContainsKey(PdfName.Intern("TR")).Should().BeFalse();
        gs0.ContainsKey(PdfName.Intern("TR2")).Should().BeFalse();
        PdfDictionary gs1 = (PdfDictionary)reopened.Objects.Resolve(states[PdfName.Intern("GS1")])!;
        gs1.ContainsKey(PdfName.Intern("TR2")).Should().BeTrue("/TR2 /Default is allowed");
    }

    private static PdfStream NormalAppearance(PdfDocument document)
    {
        PdfArray annots = (PdfArray)document.Objects.Resolve(document.Pages[0].Dictionary[PdfName.Intern("Annots")])!;
        PdfDictionary widget = (PdfDictionary)document.Objects.Resolve(annots[0])!;
        PdfDictionary ap = (PdfDictionary)document.Objects.Resolve(widget[PdfName.Intern("AP")])!;
        return (PdfStream)document.Objects.Resolve(ap[PdfName.Intern("N")])!;
    }

    private static PdfDocument Open(byte[] bytes) => PdfDocument.Open(new MemoryStream(bytes), leaveOpen: false);

    // One page with a text field (value "Filled in", hex 46696C6C656420696E) whose stored appearance,
    // if any, says STALE; two graphics states, one with /TR and /TR2 and one
    // with /TR2 /Default.
    private static byte[] FormPdf(bool needAppearances, bool withAppearance)
    {
        const string content = "/GS0 gs /GS1 gs BT /F1 12 Tf 72 700 Td (Form) Tj ET";
        const string stale = "/Tx BMC BT /Helv 12 Tf 2 5 Td (STALE) Tj ET EMC";
        List<string> bodies = new List<string>
        {
            "<</Type/Catalog/Pages 2 0 R/AcroForm<</Fields[5 0 R]/DA(/Helv 12 Tf 0 g)/DR<</Font<</Helv 6 0 R>>>>"
                + (needAppearances ? "/NeedAppearances true" : string.Empty) + ">>>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]/Resources<</Font<</F1 6 0 R>>"
                + "/ExtGState<</GS0<</TR/Identity/TR2/Identity>>/GS1<</TR2/Default>>>>>>/Contents 4 0 R/Annots[5 0 R]>>",
            "<</Length " + content.Length.ToString(CultureInfo.InvariantCulture) + ">>\nstream\n" + content + "\nendstream",
            "<</Type/Annot/Subtype/Widget/FT/Tx/T(name)/V(Filled in)/Rect[72 600 272 624]/P 3 0 R/F 4"
                + (withAppearance ? "/AP<</N 7 0 R>>" : string.Empty) + ">>",
            "<</Type/Font/Subtype/Type1/BaseFont/Helvetica/Encoding/WinAnsiEncoding>>",
            "<</Type/XObject/Subtype/Form/BBox[0 0 200 24]/Resources<</Font<</Helv 6 0 R>>>>/Length "
                + stale.Length.ToString(CultureInfo.InvariantCulture) + ">>\nstream\n" + stale + "\nendstream",
        };

        StringBuilder sb = new StringBuilder("%PDF-1.7\n");
        List<int> offsets = new List<int>();
        for (int i = 0; i < bodies.Count; i++)
        {
            offsets.Add(sb.Length);
            sb.Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append(" 0 obj\n").Append(bodies[i]).Append("\nendobj\n");
        }

        int xref = sb.Length;
        sb.Append("xref\n0 ").Append((bodies.Count + 1).ToString(CultureInfo.InvariantCulture)).Append("\n0000000000 65535 f \n");
        foreach (int off in offsets)
        {
            sb.Append(off.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        sb.Append("trailer <</Size ").Append((bodies.Count + 1).ToString(CultureInfo.InvariantCulture))
            .Append("/Root 1 0 R>>\nstartxref\n").Append(xref.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF");
        return Encoding.Latin1.GetBytes(sb.ToString());
    }
}
