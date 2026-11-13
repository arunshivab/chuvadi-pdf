// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.7.4.2 (sh fills the current clip),
//        §11.6.5.2 (soft masks from a group)
// PHASE: 3.34.0 (#41, D-353) — a shading in a soft mask's group paints.

using System.Globalization;
using System.Text.RegularExpressions;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Svg;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Integration.Tests;

public sealed class SvgNestedShading3340Tests
{
    [Fact]
    public void ShadingInASoftMaskGroup_FillsTheMask()
    {
        TestPdf pdf = new TestPdf();
        int catalog = pdf.Reserve();
        int pages = pdf.Reserve();
        int group = pdf.AddStream("<</Type/XObject/Subtype/Form/BBox[0 0 200 200]/Group<</S/Transparency/CS/DeviceGray>>"
            + "/Resources<</Shading<</b<</ShadingType 2/ColorSpace/DeviceGray/Coords[0 0 200 0]/Function<</FunctionType 2/Domain[0 1]/C0[0]/C1[1]/N 1>>/Extend[true true]>>>>>>>>", "/b sh");
        int contents = pdf.AddStream("<<>>", "/GS1 gs 1 0 0 rg 0 0 200 200 re f");
        int page = pdf.Add("<</Type/Page/Parent " + N(pages) + " 0 R/MediaBox[0 0 200 200]/Resources<</ExtGState<</GS1<</SMask<</S/Luminosity/G "
            + N(group) + " 0 R>>>>>>>>/Contents " + N(contents) + " 0 R>>");
        pdf.Set(pages, "<</Type/Pages/Kids[" + N(page) + " 0 R]/Count 1>>");
        pdf.Set(catalog, "<</Type/Catalog/Pages " + N(pages) + " 0 R>>");
        using PdfDocument doc = TestPdf.Open(pdf.Build(catalog));

        string svg = new SvgRenderer().RenderPage(doc, 0);

        Match mask = Regex.Match(svg, "<mask[^>]*>(.*?)</mask>", RegexOptions.Singleline);
        mask.Success.Should().BeTrue();
        mask.Groups[1].Value.Should().Contain("url(#sh").And.Contain("M -20000 -20000", "the shading fills the mask, not a 0 by 0 rectangle");
    }

    private static string N(int n) => n.ToString(CultureInfo.InvariantCulture);
}
