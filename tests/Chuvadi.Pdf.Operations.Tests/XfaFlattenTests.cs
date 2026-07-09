// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.5.5 — Appearance streams; §12.7.8 — XFA Forms
// PHASE: Chuvadi.Pdf.Operations — flattening hybrid XFA/AcroForm documents.
//
// Two defects pinned down here:
//
//   1. Content loss (BASELINE B16): the flattener iterated the lazy object
//      store without a preload, so every not-yet-resolved object — including
//      the page content stream — was silently dropped from the output. A
//      536 KB certificate flattened to a 6.7 KB empty page.
//
//   2. XFA retention: widgets with no usable normal appearance were kept
//      live, which kept the whole AcroForm (and the /XFA inside it) in the
//      output. Flattening a form removes its fields — the industry-standard
//      semantics — so such widgets are dropped and the flattened output of a
//      hybrid XFA document reports IsXfa == false.

using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Text;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

public sealed class XfaFlattenTests
{
    private const string Fixture = "fixtures/hybrid_xfa_widget.pdf";

    [Fact]
    public void FlattenPreservesPageContent()
    {
        using MemoryStream output = Flattened();
        using PdfDocument flat = PdfDocument.Open(output);

        flat.Pages[0].HasContent.Should().BeTrue();

        TextExtractor extractor = new TextExtractor(
            flat.Objects, ExtractionStrategy.Layout);
        string text = extractor.ExtractText(flat.Pages[0]);

        text.Should().Contain("Label", "the original page content must survive flattening");
    }

    [Fact]
    public void FlattenBakesWidgetValueIntoPage()
    {
        using MemoryStream output = Flattened();
        using PdfDocument flat = PdfDocument.Open(output);

        TextExtractor extractor = new TextExtractor(
            flat.Objects, ExtractionStrategy.Layout);
        string text = extractor.ExtractText(flat.Pages[0]);

        text.Should().Contain("HELLO-VALUE");
    }

    [Fact]
    public void FlattenedHybridXfaIsStatic()
    {
        using MemoryStream output = Flattened();
        using PdfDocument flat = PdfDocument.Open(output);

        flat.IsXfa.Should().BeFalse("flattening bakes the form; the /XFA entry must not survive");
        flat.Pages[0].Dictionary.ContainsKey(
            Chuvadi.Pdf.Primitives.PdfName.Intern("Annots")).Should().BeFalse();
    }

    private static MemoryStream Flattened()
    {
        using FileStream input = File.OpenRead(Fixture);
        using PdfDocument document = PdfDocument.Open(input, leaveOpen: false);

        MemoryStream output = new MemoryStream();
        AnnotationFlattener.Flatten(output, document);
        output.Position = 0;
        return output;
    }
}
