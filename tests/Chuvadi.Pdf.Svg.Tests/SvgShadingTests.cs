// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.7.4.5 — Shadings; the sh operator (§8.7.4.2)
// PHASE: Phase 2 — rendering conformance (shadings)
//
// Coverage: an axial/radial shading painted via the sh operator must render as
// an SVG <linearGradient>/<radialGradient> with sampled stops, filling the
// current clip region. Previously sh was a no-op, so gradients rendered blank.

using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Svg.Tests;

public sealed class SvgShadingTests
{
    [Fact]
    public void AxialShading_EmitsLinearGradientWithStops()
    {
        using MemoryStream pdf = BuildShadingPdf(radial: false);
        using PdfDocument doc = PdfDocument.Open(pdf, leaveOpen: true);

        string svg = new SvgRenderer().RenderPage(doc, 0);

        svg.Should().Contain("<linearGradient");
        svg.Should().Contain("gradientUnits=\"userSpaceOnUse\"");
        svg.Should().Contain("<stop");
        // Red (1,0,0) at t=0, blue (0,0,1) at t=1.
        svg.Should().Contain("stop-color=\"#ff0000\"");
        svg.Should().Contain("stop-color=\"#0000ff\"");
        svg.Should().Contain("fill=\"url(#sh0)\"");
    }

    [Fact]
    public void AxialShading_GradientSpansCoords()
    {
        using MemoryStream pdf = BuildShadingPdf(radial: false);
        using PdfDocument doc = PdfDocument.Open(pdf, leaveOpen: true);

        string svg = new SvgRenderer().RenderPage(doc, 0);

        // Coords [0 0 200 0] under identity CTM -> x1=0 ... x2=200.
        svg.Should().Contain("x1=\"0\"");
        svg.Should().Contain("x2=\"200\"");
    }

    [Fact]
    public void RadialShading_EmitsRadialGradient()
    {
        using MemoryStream pdf = BuildShadingPdf(radial: true);
        using PdfDocument doc = PdfDocument.Open(pdf, leaveOpen: true);

        string svg = new SvgRenderer().RenderPage(doc, 0);

        svg.Should().Contain("<radialGradient");
        svg.Should().Contain("<stop");
        svg.Should().Contain("fill=\"url(#sh0)\"");
    }

    // Builds a 200x200 page that clips to the page box and paints an axial or
    // radial red->blue shading via sh.
    private static MemoryStream BuildShadingPdf(bool radial)
    {
        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Intern("Catalog"));
        catalog.Set(PdfName.Intern("Pages"), new PdfReference(2, 0));

        PdfDictionary pages = new PdfDictionary();
        pages.Set(PdfName.Type, PdfName.Intern("Pages"));
        pages.Set(PdfName.Intern("Kids"), new PdfArray(new PdfPrimitive[] { new PdfReference(3, 0) }));
        pages.Set(PdfName.Intern("Count"), 1);

        PdfDictionary page = new PdfDictionary();
        page.Set(PdfName.Type, PdfName.Intern("Page"));
        page.Set(PdfName.Intern("Parent"), new PdfReference(2, 0));
        page.Set(PdfName.Intern("MediaBox"), new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(200), new PdfInteger(200),
        }));
        page.Set(PdfName.Intern("Contents"), new PdfReference(4, 0));

        // Red -> blue ramp.
        PdfDictionary function = new PdfDictionary();
        function.Set(PdfName.Intern("FunctionType"), 2);
        function.Set(PdfName.Intern("Domain"), new PdfArray(new PdfPrimitive[]
        {
            new PdfReal(0.0), new PdfReal(1.0),
        }));
        function.Set(PdfName.Intern("C0"), new PdfArray(new PdfPrimitive[]
        {
            new PdfReal(1.0), new PdfReal(0.0), new PdfReal(0.0),
        }));
        function.Set(PdfName.Intern("C1"), new PdfArray(new PdfPrimitive[]
        {
            new PdfReal(0.0), new PdfReal(0.0), new PdfReal(1.0),
        }));
        function.Set(PdfName.Intern("N"), new PdfReal(1.0));

        PdfDictionary shading = new PdfDictionary();
        shading.Set(PdfName.Intern("ShadingType"), radial ? 3 : 2);
        shading.Set(PdfName.Intern("ColorSpace"), PdfName.Intern("DeviceRGB"));
        shading.Set(PdfName.Intern("Coords"), radial
            ? new PdfArray(new PdfPrimitive[]
            {
                new PdfReal(100), new PdfReal(100), new PdfReal(0),
                new PdfReal(100), new PdfReal(100), new PdfReal(100),
            })
            : new PdfArray(new PdfPrimitive[]
            {
                new PdfReal(0), new PdfReal(0), new PdfReal(200), new PdfReal(0),
            }));
        shading.Set(PdfName.Intern("Function"), function);

        PdfDictionary shadings = new PdfDictionary();
        shadings.Set(PdfName.Intern("Sh0"), shading);
        PdfDictionary resources = new PdfDictionary();
        resources.Set(PdfName.Intern("Shading"), shadings);
        page.Set(PdfName.Intern("Resources"), resources);

        byte[] content = Encoding.ASCII.GetBytes("q 0 0 200 200 re W n /Sh0 sh Q");
        PdfDictionary contentDict = new PdfDictionary();
        contentDict.Set(PdfName.Length, content.Length);

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(new PdfObjectId(1, 0), catalog),
            new PdfIndirectObject(new PdfObjectId(2, 0), pages),
            new PdfIndirectObject(new PdfObjectId(3, 0), page),
            new PdfIndirectObject(new PdfObjectId(4, 0), new PdfStream(contentDict, content)),
        };

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(1, 0));

        MemoryStream output = new MemoryStream();
        PdfWriter.Write(output, objects, trailer);
        output.Position = 0;
        return output;
    }
}
