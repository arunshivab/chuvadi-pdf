// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.31 (D-173) — transparency groups on the structured display list:
//        group ids on every op (outermost first), a per-page group table,
//        unbroken and nested runs, empty groups left out. Print's test pages:
//        two identical sibling groups side by side, a group nested two deep,
//        a group's own opacity, blend mode and soft mask kept apart from its
//        ops' own, an empty group, a group under /Matrix and cm.

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Rendering.DisplayList;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Integration.Tests;

public sealed class TransparencyGroups331Tests
{
    private const string Group = "/Group<</S/Transparency>>";

    [Fact]
    public void TwoIdenticalSiblingGroups_SideBySide_GetTwoIds_AndTwoRuns()
    {
        TestPdf pdf = new TestPdf();
        int form = Form(pdf, Group, "0 0 1 rg 0 0 40 40 re f", "[0 0 40 40]");
        PageDisplayList list = Build(pdf, "q 1 0 0 1 20 20 cm /X1 Do Q q 1 0 0 1 120 20 cm /X1 Do Q", Xobjects(("X1", form)));

        list.TransparencyGroups.Select(g => g.Id).Should().Equal(1, 2);
        list.TransparencyGroups.Should().OnlyContain(g => g.ParentId == null && g.Depth == 1);
        List<PathOp> paths = list.OfType<PathOp>().ToList();
        paths[0].GroupIds.Should().Equal(1);
        paths[1].GroupIds.Should().Equal(2);
        list.TransparencyGroups[0].Box.Should().Be(new PdfRectangle(20, 20, 60, 60));
        list.TransparencyGroups[1].Box.Should().Be(new PdfRectangle(120, 20, 160, 60));
        GroupInvariants.Check(list);
    }

    [Fact]
    public void AGroupNestedTwoDeep_ListsBothIds_OutermostFirst()
    {
        TestPdf pdf = new TestPdf();
        int inner = Form(pdf, "/Group<</S/Transparency/K true>>", "0 1 0 rg 10 10 20 20 re f");
        int outer = Form(pdf, "/Group<</S/Transparency/I true/CS/DeviceCMYK>>/Resources<</XObject<</In " + inner.ToString(CultureInfo.InvariantCulture) + " 0 R>>>>",
            "1 0 0 rg 0 0 100 100 re f /In Do 0 0 1 rg 50 50 10 10 re f");
        PageDisplayList list = Build(pdf, "0 g 150 150 10 10 re f /Out Do", Xobjects(("Out", outer)));

        List<PathOp> paths = list.OfType<PathOp>().ToList();
        paths.Select(p => p.GroupIds.ToArray()).Should().BeEquivalentTo(
            new[] { System.Array.Empty<int>(), new[] { 1 }, new[] { 1, 2 }, new[] { 1 } },
            o => o.WithStrictOrdering());
        TransparencyGroup o1 = list.TransparencyGroups[0];
        TransparencyGroup i2 = list.TransparencyGroups[1];
        (o1.Isolated, o1.Knockout, o1.ColorSpace, o1.Depth).Should().Be((true, false, "DeviceCMYK", 1));
        (i2.Isolated, i2.Knockout, i2.ParentId, i2.Depth).Should().Be((false, true, (int?)1, 2));
        i2.FirstOpIndex.Should().BeGreaterThan(o1.FirstOpIndex);
        (i2.FirstOpIndex + i2.OpCount).Should().BeLessThanOrEqualTo(o1.FirstOpIndex + o1.OpCount);
        GroupInvariants.Check(list);
    }

    [Fact]
    public void AGroupsOwnOpacityBlendAndMask_AreOnTheTable_AndItsOpsKeepTheirOwn()
    {
        TestPdf pdf = new TestPdf();
        int mask = Form(pdf, Group, "1 g 0 0 200 200 re f");
        int form = Form(pdf, Group + "/Resources<</ExtGState<</Own<</ca 0.3>>>>>>", "0 0 1 rg 0 0 40 40 re f /Own gs 0 0 1 rg 50 0 40 40 re f");
        string resources = "<</XObject<</X1 " + form.ToString(CultureInfo.InvariantCulture) + " 0 R>>/ExtGState<</G<</ca 0.5/CA 0.6/BM/Multiply/SMask<</S/Luminosity/G "
            + mask.ToString(CultureInfo.InvariantCulture) + " 0 R>>>>>>>>";
        PageDisplayList list = Build(pdf, "/G gs /X1 Do", resources);

        TransparencyGroup g = list.TransparencyGroups.Should().ContainSingle().Subject;
        g.Opacity.Should().Be(0.5);
        g.StrokeOpacity.Should().Be(0.6);
        g.BlendMode.Should().Be(PdfBlendMode.Multiply);
        g.SoftMask.Should().NotBeNull();

        List<PathOp> paths = list.OfType<PathOp>().ToList();
        paths[0].GroupTransparency.Should().Be(new OpTransparency(1.0, 1.0, PdfBlendMode.Normal, null));
        paths[1].GroupTransparency!.FillOpacity.Should().Be(0.3);

        // The properties renderers draw from are as before 3.31: the group's
        // values carried onto its ops.
        paths[0].FillOpacity.Should().Be(0.5);
        paths[0].BlendMode.Should().Be(PdfBlendMode.Multiply);
        paths[0].SoftMask.Should().NotBeNull();
        GroupInvariants.Check(list);
    }

    [Fact]
    public void AnEmptyGroup_IsLeftOut_AndTheNextGroupIsId1()
    {
        TestPdf pdf = new TestPdf();
        int empty = Form(pdf, Group, "q Q");
        int emptyOuter = Form(pdf, Group + "/Resources<</XObject<</E " + empty.ToString(CultureInfo.InvariantCulture) + " 0 R>>>>", "/E Do");
        int full = Form(pdf, Group, "0 0 1 rg 0 0 40 40 re f");
        PageDisplayList list = Build(pdf, "/EO Do /F Do", Xobjects(("EO", emptyOuter), ("F", full)));

        list.TransparencyGroups.Should().ContainSingle().Which.Id.Should().Be(1);
        list.OfType<PathOp>().Single().GroupIds.Should().Equal(1);
        GroupInvariants.Check(list);
    }

    [Fact]
    public void TheBox_FollowsTheFormMatrixAndTheCtm()
    {
        TestPdf pdf = new TestPdf();
        int form = Form(pdf, Group + "/Matrix[2 0 0 2 0 0]", "0 0 1 rg 0 0 10 10 re f", "[0 0 10 20]");
        PageDisplayList list = Build(pdf, "1 0 0 1 30 40 cm /X1 Do", Xobjects(("X1", form)));

        TransparencyGroup g = list.TransparencyGroups.Should().ContainSingle().Subject;
        g.Box.Should().Be(new PdfRectangle(30, 40, 50, 80));
        g.Matrix.Should().Be(new AffineMatrix(2, 0, 0, 2, 30, 40));
        g.Form.Should().NotBeNull();
    }

    [Fact]
    public void AFormWithoutAGroup_GivesNoIds_AndFlattenedListsKeepUnbrokenRuns()
    {
        TestPdf pdf = new TestPdf();
        int plain = Form(pdf, string.Empty, "0 0 1 rg 0 0 40 40 re f");
        int grouped = Form(pdf, Group, "q 0 1 0 rg 0 0 40 40 re f Q q 1 0 0 rg 5 5 10 10 re f Q");
        byte[] bytes = OnePage(pdf, "/P Do q /G Do Q", Xobjects(("P", plain), ("G", grouped)));
        using PdfDocument doc = TestPdf.Open(bytes);

        PageDisplayList list = DisplayListBuilder.Build(doc, 0);
        list.OfType<PathOp>().First().GroupIds.Should().BeEmpty();
        GroupInvariants.Check(list);
        GroupInvariants.Check(DisplayListBuilder.Build(doc, 0, flattenTransforms: true));
    }

    [Fact]
    public void AnAnnotationAppearanceGroup_IsInTheTable()
    {
        TestPdf pdf2 = new TestPdf();
        int ap2 = pdf2.AddStream("<</Type/XObject/Subtype/Form/BBox[0 0 40 40]" + Group + ">>", "0 0 1 rg 0 0 40 40 re f");
        int catalog = pdf2.Reserve();
        int pages = pdf2.Reserve();
        int page = pdf2.Reserve();
        int contents = pdf2.AddStream("<<>>", string.Empty);
        int annot = pdf2.Add("<</Type/Annot/Subtype/Square/Rect[50 50 90 90]/F 4/AP<</N " + ap2.ToString(CultureInfo.InvariantCulture) + " 0 R>>>>");
        pdf2.Set(catalog, "<</Type/Catalog/Pages " + pages.ToString(CultureInfo.InvariantCulture) + " 0 R>>");
        pdf2.Set(pages, "<</Type/Pages/Kids[" + page.ToString(CultureInfo.InvariantCulture) + " 0 R]/Count 1>>");
        pdf2.Set(page, "<</Type/Page/Parent " + pages.ToString(CultureInfo.InvariantCulture) + " 0 R/MediaBox[0 0 200 200]/Resources<<>>/Contents "
            + contents.ToString(CultureInfo.InvariantCulture) + " 0 R/Annots[" + annot.ToString(CultureInfo.InvariantCulture) + " 0 R]>>");
        using PdfDocument doc = TestPdf.Open(pdf2.Build(catalog));

        PageDisplayList list = DisplayListBuilder.Build(doc, 0);
        PathOp op = list.OfType<PathOp>().Single();
        op.GroupIds.Should().Equal(1);
        op.Annotation.Should().NotBeNull();
        list.TransparencyGroups.Should().ContainSingle().Which.Box.Should().Be(new PdfRectangle(50, 50, 90, 90));
        GroupInvariants.Check(list);
    }

    private static int Form(TestPdf pdf, string extra, string content, string bbox = "[0 0 200 200]") =>
        pdf.AddStream("<</Type/XObject/Subtype/Form/BBox" + bbox + extra + ">>", content);

    private static string Xobjects(params (string Name, int Obj)[] forms) =>
        "<</XObject<<" + string.Concat(forms.Select(f => "/" + f.Name + " " + f.Obj.ToString(CultureInfo.InvariantCulture) + " 0 R")) + ">>>>";

    private static PageDisplayList Build(TestPdf pdf, string content, string resources)
    {
        using PdfDocument doc = TestPdf.Open(OnePage(pdf, content, resources));
        return DisplayListBuilder.Build(doc, 0);
    }

    private static byte[] OnePage(TestPdf pdf, string content, string resources)
    {
        int catalog = pdf.Reserve();
        int pages = pdf.Reserve();
        int page = pdf.Reserve();
        int contents = pdf.AddStream("<<>>", content);
        pdf.Set(catalog, "<</Type/Catalog/Pages " + pages.ToString(CultureInfo.InvariantCulture) + " 0 R>>");
        pdf.Set(pages, "<</Type/Pages/Kids[" + page.ToString(CultureInfo.InvariantCulture) + " 0 R]/Count 1>>");
        pdf.Set(page, "<</Type/Page/Parent " + pages.ToString(CultureInfo.InvariantCulture) + " 0 R/MediaBox[0 0 200 200]/Resources " + resources
            + "/Contents " + contents.ToString(CultureInfo.InvariantCulture) + " 0 R>>");
        return pdf.Build(catalog);
    }
}

/// <summary>Checks D-173's guarantees on a display list.</summary>
internal static class GroupInvariants
{
    internal static void Check(PageDisplayList list)
    {
        IReadOnlyList<TransparencyGroup> groups = list.TransparencyGroups;
        for (int i = 0; i < groups.Count; i++)
        {
            TransparencyGroup g = groups[i];
            g.Id.Should().Be(i + 1, "ids are 1, 2, 3 in table order");
            g.OpCount.Should().BeGreaterThan(0, "empty groups are left out");
            for (int k = 0; k < list.Count; k++)
            {
                bool inside = list[k].GroupIds.Contains(g.Id);
                bool inRun = k >= g.FirstOpIndex && k < g.FirstOpIndex + g.OpCount;
                inside.Should().Be(inRun, "group " + g.Id + "'s ops are one unbroken run (op " + k + ")");
            }

            if (g.ParentId is int p)
            {
                TransparencyGroup parent = groups[p - 1];
                g.FirstOpIndex.Should().BeGreaterThanOrEqualTo(parent.FirstOpIndex);
                (g.FirstOpIndex + g.OpCount).Should().BeLessThanOrEqualTo(parent.FirstOpIndex + parent.OpCount);
                g.Depth.Should().Be(parent.Depth + 1);
            }
        }

        foreach (RenderOp op in list)
        {
            for (int j = 0; j < op.GroupIds.Count; j++)
            {
                TransparencyGroup g = groups[op.GroupIds[j] - 1];
                g.Depth.Should().Be(j + 1, "ids are outermost first");
                g.ParentId.Should().Be(j == 0 ? null : op.GroupIds[j - 1]);
            }

            (op.GroupTransparency is null).Should().Be(op.GroupIds.Count == 0);
        }
    }
}
