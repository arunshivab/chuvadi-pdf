// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.7.8 (XFA forms), §12.5.2 (Widget /Rect)
//        XFA 3.3 §A — XFA packets; §A.2 — datasets data layer
// Coverage for PdfDocument.Xfa: packet access, datasets field/value walk,
// best-effort widget geometry, and the single-stream XDP shape. Uses synthetic
// fixtures with unfiltered packet streams so the test owns the exact XML.

using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Documents.Tests;

public sealed class XfaPacketTests
{
    private const string DatasetsXml =
        "<xfa:datasets xmlns:xfa=\"http://www.xfa.org/schema/xfa-data/1.0/\">"
        + "<xfa:data><record>"
        + "<FirstName>Arun</FirstName>"
        + "<City/>"
        + "<Note>A &amp; B</Note>"
        + "</record></xfa:data>"
        + "<dd:dataDescription xmlns:dd=\"http://ns.adobe.com/data-description/\" dd:name=\"data\">"
        + "<record><Ignored/></record></dd:dataDescription>"
        + "</xfa:datasets>";

    private const string TemplateXml =
        "<template xmlns=\"http://www.xfa.org/schema/xfa-template/3.3/\">"
        + "<subform name=\"record\"/></template>";

    [Fact]
    public void Xfa_Null_WhenDocumentHasNoXfa()
    {
        using MemoryStream ms = BuildPlainPdf();
        using PdfDocument doc = OpenPdf(ms);

        doc.Xfa.Should().BeNull();
    }

    [Fact]
    public void Xfa_ReadsNamedPackets_FromArray()
    {
        using MemoryStream ms = BuildHybridXfaPdf();
        using PdfDocument doc = OpenPdf(ms);

        XfaPackets xfa = doc.Xfa!;
        xfa.Should().NotBeNull();
        xfa.IsSingleStream.Should().BeFalse();
        xfa.Packets.Should().HaveCount(2);
        xfa.Template.Should().NotBeNull();
        xfa.Datasets.Should().NotBeNull();
        xfa.Config.Should().BeNull();
        xfa.Get("template")!.Text.Should().Be(TemplateXml);
    }

    [Fact]
    public void Get_ReturnsNull_ForUnknownPacket()
    {
        using MemoryStream ms = BuildHybridXfaPdf();
        using PdfDocument doc = OpenPdf(ms);

        doc.Xfa!.Get("nonexistent").Should().BeNull();
    }

    [Fact]
    public void DataFields_WalkPathsAndValues_FromDatasets()
    {
        using MemoryStream ms = BuildHybridXfaPdf();
        using PdfDocument doc = OpenPdf(ms);

        IReadOnlyList<XfaDataField> fields = doc.Xfa!.DataFields;

        fields.Should().HaveCount(3);
        fields.Should().Contain(f => f.NodePath == "record.FirstName" && f.Value == "Arun");
        fields.Should().Contain(f => f.NodePath == "record.City" && f.Value == "");
        fields.Should().Contain(f => f.NodePath == "record.Note" && f.Value == "A & B");
    }

    [Fact]
    public void DataFields_ExcludeDataDescriptionSubtree()
    {
        using MemoryStream ms = BuildHybridXfaPdf();
        using PdfDocument doc = OpenPdf(ms);

        doc.Xfa!.DataFields.Should().NotContain(f => f.NodePath.Contains("Ignored"));
    }

    [Fact]
    public void DataFields_Geometry_FromMatchingWidget()
    {
        using MemoryStream ms = BuildHybridXfaPdf();
        using PdfDocument doc = OpenPdf(ms);

        XfaDataField firstName = FieldByPath(doc.Xfa!.DataFields, "record.FirstName");
        firstName.Geometry.Should().NotBeNull();
        firstName.Geometry!.PageIndex.Should().Be(0);
        firstName.Geometry.Rectangle.X1.Should().Be(100.0);
        firstName.Geometry.Rectangle.Y1.Should().Be(200.0);
        firstName.Geometry.Rectangle.X2.Should().Be(300.0);
        firstName.Geometry.Rectangle.Y2.Should().Be(220.0);
    }

    [Fact]
    public void DataFields_Geometry_NullWhenNoWidgetMatches()
    {
        using MemoryStream ms = BuildHybridXfaPdf();
        using PdfDocument doc = OpenPdf(ms);

        FieldByPath(doc.Xfa!.DataFields, "record.City").Geometry.Should().BeNull();
    }

    [Fact]
    public void Xfa_SingleStream_ExposesOnePacket()
    {
        using MemoryStream ms = BuildSingleStreamXfaPdf();
        using PdfDocument doc = OpenPdf(ms);

        XfaPackets xfa = doc.Xfa!;
        xfa.IsSingleStream.Should().BeTrue();
        xfa.Packets.Should().HaveCount(1);
        xfa.Packets[0].Name.Should().BeEmpty();
        xfa.Datasets.Should().BeNull();
        xfa.DataFields.Should().BeEmpty();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static XfaDataField FieldByPath(IReadOnlyList<XfaDataField> fields, string path)
    {
        for (int i = 0; i < fields.Count; i++)
        {
            if (fields[i].NodePath == path)
            {
                return fields[i];
            }
        }

        throw new KeyNotFoundException(path);
    }

    private static MemoryStream BuildPlainPdf()
    {
        List<PdfIndirectObject> objects = BaseObjects(out PdfDictionary catalog);
        return Write(objects);
    }

    private static MemoryStream BuildHybridXfaPdf()
    {
        PdfObjectId acroId = new PdfObjectId(4, 0);
        PdfObjectId fieldId = new PdfObjectId(5, 0);
        PdfObjectId templateId = new PdfObjectId(6, 0);
        PdfObjectId datasetsId = new PdfObjectId(7, 0);

        List<PdfIndirectObject> objects = BaseObjects(out PdfDictionary catalog);
        PdfDictionary pageDict = (PdfDictionary)objects[2].Value;

        PdfDictionary fieldDict = new PdfDictionary();
        fieldDict.Set(PdfName.Type, PdfName.Intern("Annot"));
        fieldDict.Set(PdfName.Subtype, PdfName.Intern("Widget"));
        fieldDict.Set(PdfName.Intern("T"), new PdfString("FirstName"));
        fieldDict.Set(PdfName.Intern("Rect"), new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(100), new PdfInteger(200), new PdfInteger(300), new PdfInteger(220),
        }));

        pageDict.Set(PdfName.Intern("Annots"), new PdfArray(new PdfPrimitive[]
        {
            new PdfReference(fieldId),
        }));

        PdfStream templateStream = new PdfStream(new PdfDictionary(), Encoding.UTF8.GetBytes(TemplateXml));
        PdfStream datasetsStream = new PdfStream(new PdfDictionary(), Encoding.UTF8.GetBytes(DatasetsXml));

        PdfArray xfa = new PdfArray(new PdfPrimitive[]
        {
            new PdfString("template"), new PdfReference(templateId),
            new PdfString("datasets"), new PdfReference(datasetsId),
        });

        PdfDictionary acroDict = new PdfDictionary();
        acroDict.Set(PdfName.Intern("XFA"), xfa);
        acroDict.Set(PdfName.Intern("Fields"), new PdfArray(new PdfPrimitive[]
        {
            new PdfReference(fieldId),
        }));

        catalog.Set(PdfName.Intern("AcroForm"), new PdfReference(acroId));

        objects.Add(new PdfIndirectObject(acroId, acroDict));
        objects.Add(new PdfIndirectObject(fieldId, fieldDict));
        objects.Add(new PdfIndirectObject(templateId, templateStream));
        objects.Add(new PdfIndirectObject(datasetsId, datasetsStream));

        return Write(objects);
    }

    private static MemoryStream BuildSingleStreamXfaPdf()
    {
        PdfObjectId acroId = new PdfObjectId(4, 0);
        PdfObjectId xfaId = new PdfObjectId(5, 0);

        List<PdfIndirectObject> objects = BaseObjects(out PdfDictionary catalog);

        string xdp = "<xdp:xdp xmlns:xdp=\"http://ns.adobe.com/xdp/\">" + TemplateXml + "</xdp:xdp>";
        PdfStream xfaStream = new PdfStream(new PdfDictionary(), Encoding.UTF8.GetBytes(xdp));

        PdfDictionary acroDict = new PdfDictionary();
        acroDict.Set(PdfName.Intern("XFA"), new PdfReference(xfaId));

        catalog.Set(PdfName.Intern("AcroForm"), new PdfReference(acroId));

        objects.Add(new PdfIndirectObject(acroId, acroDict));
        objects.Add(new PdfIndirectObject(xfaId, xfaStream));

        return Write(objects);
    }

    private static List<PdfIndirectObject> BaseObjects(out PdfDictionary catalog)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);

        catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(pagesId));

        PdfArray kids = new PdfArray(new PdfPrimitive[] { new PdfReference(pageId) });

        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, kids);
        pagesDict.Set(PdfName.Count, 1);
        pagesDict.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(612), new PdfInteger(792),
        }));

        PdfDictionary pageDict = new PdfDictionary();
        pageDict.Set(PdfName.Type, PdfName.Page);
        pageDict.Set(PdfName.Parent, new PdfReference(pagesId));

        return new List<PdfIndirectObject>
        {
            new PdfIndirectObject(catalogId, catalog),
            new PdfIndirectObject(pagesId, pagesDict),
            new PdfIndirectObject(pageId, pageDict),
        };
    }

    private static MemoryStream Write(List<PdfIndirectObject> objects)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        return ms;
    }

    private static PdfDocument OpenPdf(MemoryStream ms)
    {
        ms.Seek(0, SeekOrigin.Begin);
        return PdfDocument.Open(ms, leaveOpen: true);
    }
}
