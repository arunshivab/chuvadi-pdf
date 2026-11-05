// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.31 — annotation modes, field appearances and comment appearances
// (D-146, D-152): fixtures E1 to E6 and C1 to C15 of PLAN-3.31.0.md.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chuvadi.Pdf.Annotations;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Operations;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Rendering.DisplayList;
using Chuvadi.Pdf.Text;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Integration.Tests;

public sealed class AnnotationAppearance331Tests
{
    private const string HelvResources = "<</Font<</Helv<</Type/Font/Subtype/Type1/BaseFont/Helvetica/Encoding/WinAnsiEncoding>>>>>>";
    private const string BlueBox = "0 0 1 rg 0 0 40 40 re f";

    private static readonly string[] StampNames =
    {
        "Approved", "Experimental", "NotApproved", "AsIs", "Expired", "NotForPublicRelease", "Confidential",
        "Final", "Sold", "Departmental", "ForComment", "TopSecret", "Draft", "ForPublicRelease", "Custom Words",
    };

    private static readonly string[] NoteIcons = { "Note", "Comment", "Key", "Help", "NewParagraph", "Paragraph", "Insert", "Unknown" };

    private static readonly string[] AttachmentIcons = { "PushPin", "Paperclip", "Graph", "Tag", "Unknown" };

    // C1 to C15 without the icon and stamp variants: subtype and its own entries.
    public static IEnumerable<object[]> Comments() => new[]
    {
        new object[] { "C1 Highlight", "/Subtype/Highlight/QuadPoints[50 90 150 90 50 70 150 70]/C[1 1 0]" },
        new object[] { "C2 Underline", "/Subtype/Underline/QuadPoints[50 90 150 90 50 70 150 70]/C[1 0 0]" },
        new object[] { "C3 StrikeOut", "/Subtype/StrikeOut/QuadPoints[50 90 150 90 50 70 150 70]/C[1 0 0]" },
        new object[] { "C4 Square", "/Subtype/Square/C[1 0 0]/IC[0 0 1]/BS<</W 2>>" },
        new object[] { "C5 Circle", "/Subtype/Circle/C[1 0 0]/IC[0 1 0]" },
        new object[] { "C6 Line", "/Subtype/Line/L[55 75 145 85]/LE[/ClosedArrow/Circle]/C[0 0 0]/IC[1 0 0]/BS<</W 2>>" },
        new object[] { "C7 Ink", "/Subtype/Ink/InkList[[55 75 80 85 110 75 145 85]]/C[0 0 1]/BS<</W 3>>" },
        new object[] { "C8 FreeText", "/Subtype/FreeText/Contents(Hello comment)/DA(/Helv 9 Tf 0 0 0 rg)/C[1 1 0.8]" },
        new object[] { "C9 Text", "/Subtype/Text/Name/Comment/C[1 0.8 0]" },
        new object[] { "C10 Squiggly", "/Subtype/Squiggly/QuadPoints[50 90 150 90 50 70 150 70]/C[0 0 1]" },
        new object[] { "C11 Polygon", "/Subtype/Polygon/Vertices[55 72 145 72 100 88]/C[0 0 0]/IC[0 0 1]" },
        new object[] { "C12 PolyLine", "/Subtype/PolyLine/Vertices[55 72 100 88 145 72]/LE[/OpenArrow/Square]/C[0 0 0]" },
        new object[] { "C13 Stamp", "/Subtype/Stamp/Name/Approved" },
        new object[] { "C14 Caret", "/Subtype/Caret/Sy/P/C[0 0 1]" },
        new object[] { "C15 FileAttachment", "/Subtype/FileAttachment/Name/Paperclip/C[0.5 0.5 0.5]" },
    };

    [Fact]
    public void E1_ACommentWithoutThePrintFlag_ShowsOnScreen_ButDoesNotPrint()
    {
        using PdfDocument doc = TestPdf.Open(Annotated("/Subtype/Square/F 0", BlueBox));

        TestPdf.Ink(doc, AnnotationMode.View, 50, 50, 90, 90).Should().BeGreaterThan(1000);
        TestPdf.Ink(doc, AnnotationMode.Print, 50, 50, 90, 90).Should().Be(0);
    }

    [Fact]
    public void E2_APrintOnlyStamp_IsHiddenOnScreen_AndPrints()
    {
        using PdfDocument doc = TestPdf.Open(Annotated("/Subtype/Stamp/F 36", BlueBox));

        TestPdf.Ink(doc, AnnotationMode.View, 50, 50, 90, 90).Should().Be(0);
        TestPdf.Ink(doc, AnnotationMode.Print, 50, 50, 90, 90).Should().BeGreaterThan(1000);
    }

    [Fact]
    public void E3_AFormFilledWithStaleAppearances_ShowsAndExtractsItsNewValue()
    {
        byte[] pdf = FilledForm("(Arun Shiva B)", "/Helv 10 Tf 0 g", storedBlank: true, needAppearances: true);
        using PdfDocument doc = TestPdf.Open(pdf);

        TestPdf.Ink(doc, AnnotationMode.View, 20, 100, 180, 130).Should().BeGreaterThan(20);
        new TextExtractor(doc.Objects).ExtractText(doc.Pages[0]).Should().Contain("Arun Shiva B");
        PageDisplayList list = DisplayListBuilder.Build(doc, 0);
        TextOp text = list.OfType<TextOp>().Single(t => t.Annotation is not null);
        text.Annotation!.Subtype.Should().Be("Widget");
        text.Annotation.FieldName.Should().Be("name");
        text.Annotation.Generated.Should().BeTrue();
    }

    [Fact]
    public void E3_Flattening_BakesTheNewValue_NotTheStaleAppearance()
    {
        byte[] pdf = FilledForm("(Arun Shiva B)", "/Helv 10 Tf 0 g", storedBlank: true, needAppearances: true);
        using PdfDocument doc = TestPdf.Open(pdf);
        using MemoryStream output = new MemoryStream();
        AnnotationFlattener.Flatten(output, doc);

        using PdfDocument flat = PdfDocument.Open(new MemoryStream(output.ToArray()), new PdfOpenOptions());
        new TextExtractor(flat.Objects).ExtractText(flat.Pages[0]).Should().Contain("Arun Shiva B");
    }

    [Fact]
    public void E4_ACheckBoxWithItsOwnOnAppearance_KeepsIt()
    {
        string widget = "<</Type/Annot/Subtype/Widget/FT/Btn/T(agree)/V/Yes/AS/Yes/Rect[50 50 70 70]/F 4/P PAGE"
            + "/AP<</N<</Yes YES 0 R/Off OFF 0 R>>>>>>";
        TestPdf pdf = new TestPdf();
        int yes = pdf.AddStream("<</Type/XObject/Subtype/Form/BBox[0 0 20 20]>>", "0 0 1 rg 0 0 20 20 re f");
        int off = pdf.AddStream("<</Type/XObject/Subtype/Form/BBox[0 0 20 20]>>", string.Empty);
        byte[] bytes = WithPrefix(pdf, widget.Replace("YES", yes.ToString(System.Globalization.CultureInfo.InvariantCulture), System.StringComparison.Ordinal)
            .Replace("OFF", off.ToString(System.Globalization.CultureInfo.InvariantCulture), System.StringComparison.Ordinal), "<</Fields[FIELDS]/NeedAppearances true>>");
        using PdfDocument doc = TestPdf.Open(bytes);

        IReadOnlyList<AnnotationAppearance> aps = PageAnnotationAppearances.Collect(doc.Pages[0], doc.Objects, AnnotationMode.View, GeneratedAppearances.Instance);
        aps.Should().ContainSingle();
        aps[0].IsGenerated.Should().BeFalse();
        TestPdf.Ink(doc, AnnotationMode.View, 50, 50, 70, 70).Should().BeGreaterThan(300);
    }

    [Fact]
    public void E5_AWidgetWithAValueButNoAppearance_IsDrawn()
    {
        byte[] pdf = FilledForm("(Hello)", "/Helv 12 Tf 0 g", storedBlank: false, needAppearances: false);
        using PdfDocument doc = TestPdf.Open(pdf);

        TestPdf.Ink(doc, AnnotationMode.View, 20, 100, 180, 130).Should().BeGreaterThan(20);
        new TextExtractor(doc.Objects).ExtractText(doc.Pages[0]).Should().Contain("Hello");
    }

    [Fact]
    public void E6_TamilTheFieldFontCannotShow_IsDrawnWithLiPiSansTamil_WithAWarning()
    {
        List<PdfWarning> warnings = new List<PdfWarning>();
        byte[] pdf = FilledForm("<FEFF004100720075006E00200B850BB00BC10BA30BCD>", "/Helv 12 Tf 0 g", storedBlank: false, needAppearances: false);
        using PdfDocument doc = TestPdf.Open(pdf, warnings);

        TestPdf.Ink(doc, AnnotationMode.View, 20, 100, 180, 130).Should().BeGreaterThan(40);
        warnings.Should().Contain(w => w.Kind == PdfWarningKind.FontSubstituted && w.Reason.Contains("LiPi-Sans-Tamil-Regular.ttf"));
        string text = new TextExtractor(doc.Objects).ExtractText(doc.Pages[0]);
        text.Should().Contain("Arun");
        text.Should().Contain("அ");
    }

    [Fact]
    public void E6_ABoldFieldFont_UsesTheBoldLiPiFace()
    {
        List<PdfWarning> warnings = new List<PdfWarning>();
        byte[] pdf = FilledForm("<FEFF0B850BB00BC10BA30BCD>", "/HeBo 12 Tf 0 g", storedBlank: false, needAppearances: false);
        using PdfDocument doc = TestPdf.Open(pdf, warnings);

        TestPdf.Ink(doc, AnnotationMode.View, 20, 100, 180, 130).Should().BeGreaterThan(40);
        warnings.Should().Contain(w => w.Kind == PdfWarningKind.FontSubstituted && w.Reason.Contains("LiPi-Sans-Tamil-Bold.ttf"));
    }

    [Fact]
    public void E6_AScriptLiPiSansDoesNotCover_IsReportedWithNoSubstitute()
    {
        List<PdfWarning> warnings = new List<PdfWarning>();
        byte[] pdf = FilledForm("<FEFF0645063106270628>", "/Helv 12 Tf 0 g", storedBlank: false, needAppearances: false);
        using PdfDocument doc = TestPdf.Open(pdf, warnings);

        _ = TestPdf.Ink(doc, AnnotationMode.View, 20, 100, 180, 130);
        warnings.Should().Contain(w => w.Kind == PdfWarningKind.FontSubstituted && w.Reason.Contains("no substitute"));
    }

    [Fact]
    public void FormFieldsOnly_DrawsTheFields_WithoutThePageContentOrComments()
    {
        byte[] pdf = TestPdf.OnePage(
            "0 0 0 rg 10 10 30 30 re f",
            HelvResources,
            new[]
            {
                "<</Type/Annot/Subtype/Widget/FT/Tx/T(name)/V(Value here)/Rect[20 100 180 130]/F 4/P PAGE/DA(/Helv 12 Tf 0 g)>>",
                "<</Type/Annot/Subtype/Square/Rect[150 10 190 50]/F 4/C[0 0 0]/IC[0 0 0]>>",
            },
            "<</Fields[FIELDS]/DR " + HelvResources + ">>");
        using PdfDocument doc = TestPdf.Open(pdf);

        TestPdf.Ink(doc, AnnotationMode.FormFieldsOnly, 10, 10, 40, 40).Should().Be(0);
        TestPdf.Ink(doc, AnnotationMode.FormFieldsOnly, 150, 10, 190, 50).Should().Be(0);
        TestPdf.Ink(doc, AnnotationMode.FormFieldsOnly, 20, 100, 180, 130).Should().BeGreaterThan(20);
        TestPdf.Ink(doc, AnnotationMode.None, 20, 100, 180, 130).Should().Be(0);
        TestPdf.Ink(doc, AnnotationMode.None, 10, 10, 40, 40).Should().BeGreaterThan(500);
    }

    [Theory]
    [MemberData(nameof(Comments))]
    public void C1ToC15_ACommentWithoutAnAppearance_IsDrawnFromItsEntries(string name, string entries)
    {
        using PdfDocument doc = TestPdf.Open(Annotated(entries + "/F 4", null, "[50 60 150 100]"));

        TestPdf.Ink(doc, AnnotationMode.View, 45, 55, 155, 105).Should().BeGreaterThan(5, name);
        TestPdf.Ink(doc, AnnotationMode.Print, 45, 55, 155, 105).Should().BeGreaterThan(5, name);
    }

    [Theory]
    [MemberData(nameof(Comments))]
    public void C1ToC15_WithoutThePrintFlag_ADrawnCommentDoesNotPrint(string name, string entries)
    {
        using PdfDocument doc = TestPdf.Open(Annotated(entries + "/F 0", null, "[50 60 150 100]"));

        TestPdf.Ink(doc, AnnotationMode.View, 45, 55, 155, 105).Should().BeGreaterThan(5, name);
        TestPdf.Ink(doc, AnnotationMode.Print, 45, 55, 155, 105).Should().Be(0, name);
    }

    [Fact]
    public void C13_EveryStampName_IsDrawn_AndAStampWithoutAColourIsRed()
    {
        foreach (string stamp in StampNames)
        {
            string nameEntry = "/Name/" + stamp.Replace(" ", "#20", System.StringComparison.Ordinal);
            using PdfDocument doc = TestPdf.Open(Annotated("/Subtype/Stamp" + nameEntry, null, "[20 60 180 100]"));
            TestPdf.Ink(doc, AnnotationMode.View, 15, 55, 185, 105).Should().BeGreaterThan(50, stamp);
        }

        using PdfDocument red = TestPdf.Open(Annotated("/Subtype/Stamp", null, "[20 60 180 100]"));
        Chuvadi.Pdf.Graphics.PixelBuffer buf = new Chuvadi.Pdf.Rendering.PageRasterizer(red.Objects, new Chuvadi.Pdf.Rendering.RenderOptions { Dpi = 72 }).Rasterize(red.Pages[0]);
        int redPixels = 0;
        for (int x = 20; x < 180; x++)
        {
            for (int y = 100; y < 140; y++)
            {
                (byte b, byte g, byte r, byte _) = buf.GetPixelBgra(x, y);
                if (r > 150 && g < 90 && b < 90)
                {
                    redPixels++;
                }
            }
        }

        redPixels.Should().BeGreaterThan(50);
    }

    [Fact]
    public void C9AndC15_EveryIcon_IsDrawn()
    {
        foreach (string icon in NoteIcons)
        {
            using PdfDocument doc = TestPdf.Open(Annotated("/Subtype/Text/Name/" + icon, null, "[50 60 74 84]"));
            TestPdf.Ink(doc, AnnotationMode.View, 50, 60, 74, 84).Should().BeGreaterThan(20, icon);
        }

        foreach (string icon in AttachmentIcons)
        {
            using PdfDocument doc = TestPdf.Open(Annotated("/Subtype/FileAttachment/Name/" + icon, null, "[50 60 74 84]"));
            TestPdf.Ink(doc, AnnotationMode.View, 50, 60, 74, 84).Should().BeGreaterThan(20, icon);
        }
    }

    [Fact]
    public void GeneratedStampAndIcons_AddNoText_ButFreeTextIsExtracted()
    {
        using PdfDocument stamp = TestPdf.Open(Annotated("/Subtype/Stamp/Name/Approved", null, "[20 60 180 100]"));
        new TextExtractor(stamp.Objects).ExtractText(stamp.Pages[0]).Trim().Should().BeEmpty();

        using PdfDocument note = TestPdf.Open(Annotated("/Subtype/FreeText/Contents(Hello comment)/DA(/Helv 9 Tf 0 g)", null, "[20 60 180 100]"));
        new TextExtractor(note.Objects).ExtractText(note.Pages[0]).Should().Contain("Hello comment");
    }

    // A page with one annotation on [50 50 90 90] (or the given rect), with
    // a stored appearance when one is given.
    private static byte[] Annotated(string entries, string? appearance, string rect = "[50 50 90 90]")
    {
        TestPdf pdf = new TestPdf();
        string ap = string.Empty;
        if (appearance is not null)
        {
            int n = pdf.AddStream("<</Type/XObject/Subtype/Form/BBox[0 0 40 40]>>", appearance);
            ap = "/AP<</N " + n.ToString(System.Globalization.CultureInfo.InvariantCulture) + " 0 R>>";
        }

        return WithPrefix(pdf, "<</Type/Annot" + entries + "/Rect" + rect + ap + ">>", null);
    }

    private static byte[] FilledForm(string value, string da, bool storedBlank, bool needAppearances)
    {
        TestPdf pdf = new TestPdf();
        string ap = string.Empty;
        if (storedBlank)
        {
            int n = pdf.AddStream("<</Type/XObject/Subtype/Form/BBox[0 0 160 30]>>", "/Tx BMC EMC");
            ap = "/AP<</N " + n.ToString(System.Globalization.CultureInfo.InvariantCulture) + " 0 R>>";
        }

        string widget = "<</Type/Annot/Subtype/Widget/FT/Tx/T(name)/V" + value + "/Rect[20 100 180 130]/F 4/P PAGE/DA(" + da + ")" + ap + ">>";
        return WithPrefix(pdf, widget, "<</Fields[FIELDS]/NeedAppearances " + (needAppearances ? "true" : "false") + "/DR " + HelvResources + ">>");
    }

    // Builds the one-page document after objects already added to `pdf`.
    private static byte[] WithPrefix(TestPdf pdf, string annotation, string? acroForm)
    {
        int catalog = pdf.Reserve();
        int pages = pdf.Reserve();
        int page = pdf.Reserve();
        int contents = pdf.AddStream("<<>>", string.Empty);
        string pageRef = page.ToString(System.Globalization.CultureInfo.InvariantCulture) + " 0 R";
        int annot = pdf.Add(annotation.Replace("PAGE", pageRef, System.StringComparison.Ordinal));
        string annotRef = annot.ToString(System.Globalization.CultureInfo.InvariantCulture) + " 0 R";
        string form = acroForm is null ? string.Empty : "/AcroForm " + acroForm.Replace("FIELDS", annotRef, System.StringComparison.Ordinal);
        pdf.Set(catalog, "<</Type/Catalog/Pages " + pages.ToString(System.Globalization.CultureInfo.InvariantCulture) + " 0 R" + form + ">>");
        pdf.Set(pages, "<</Type/Pages/Kids[" + pageRef + "]/Count 1>>");
        pdf.Set(page, "<</Type/Page/Parent " + pages.ToString(System.Globalization.CultureInfo.InvariantCulture) + " 0 R/MediaBox[0 0 200 200]/Resources " + HelvResources
            + "/Contents " + contents.ToString(System.Globalization.CultureInfo.InvariantCulture) + " 0 R/Annots[" + annotRef + "]>>");
        return pdf.Build(catalog);
    }
}
